using System.Diagnostics;
using System.Diagnostics.Metrics;
using WeaveLLM.Core.Models;
using ILanguageModel = WeaveLLM.Core.Providers.ILanguageModel;

namespace WeaveLLM.Observability;

/// <summary>
/// OpenTelemetry integration for WeaveLLM.
/// All chain executions emit traces, spans, and metrics automatically.
/// Plug into Aspire, Jaeger, Zipkin, Grafana, Datadog — any OTel-compatible backend.
/// </summary>
public static class WeaveLLMTelemetry
{
    public static readonly string ActivitySourceName = "WeaveLLM";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly string MeterName = "WeaveLLM.Metrics";
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Metrics
    public static readonly Counter<long> ChainExecutionCount =
        Meter.CreateCounter<long>("weavellm.chain.executions", "count", "Total chain executions");

    public static readonly Histogram<double> ChainDuration =
        Meter.CreateHistogram<double>("weavellm.chain.duration_ms", "ms", "Chain execution duration");

    public static readonly Counter<long> TokensUsed =
        Meter.CreateCounter<long>("weavellm.tokens.used", "tokens", "Total tokens consumed");

    public static readonly Counter<decimal> EstimatedCost =
        Meter.CreateCounter<decimal>("weavellm.cost.usd", "USD", "Estimated API cost");

    public static readonly Counter<long> ChainErrors =
        Meter.CreateCounter<long>("weavellm.chain.errors", "count", "Total chain errors");

    public static Activity? StartChainActivity(string chainName, string traceId)
    {
        var activity = ActivitySource.StartActivity($"chain.{chainName}");
        activity?.SetTag("weavellm.chain.name", chainName);
        activity?.SetTag("weavellm.trace_id", traceId);
        return activity;
    }

    public static void RecordChainComplete(string chainName, bool success, TimeSpan duration, TokenUsage usage)
    {
        var tags = new TagList { { "chain.name", chainName }, { "success", success } };
        ChainExecutionCount.Add(1, tags);
        ChainDuration.Record(duration.TotalMilliseconds, tags);
        TokensUsed.Add(usage.TotalTokens, tags);
    }
}

/// <summary>
/// Evaluates chain/agent output quality.
/// Implement custom evaluators or use built-in ones.
/// </summary>
public interface IEvaluator<TOutput>
{
    string Name { get; }
    Task<EvaluationResult> EvaluateAsync(TOutput output, EvaluationContext context, CancellationToken cancellationToken = default);
}

public sealed class EvaluationContext
{
    public string Input { get; init; } = string.Empty;
    public string? ExpectedOutput { get; init; }
    public IReadOnlyList<string>? RetrievedDocuments { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed class EvaluationResult
{
    public string EvaluatorName { get; init; } = string.Empty;
    public float Score { get; init; }
    public string Reasoning { get; init; } = string.Empty;
    public bool Passed => Score >= PassThreshold;
    public float PassThreshold { get; init; } = 0.7f;
    public Dictionary<string, float> SubScores { get; init; } = new();
}

/// <summary>
/// LLM-as-judge evaluator. Uses another LLM call to score the output.
/// Equivalent to RAGAS faithfulness + relevancy scoring.
/// </summary>
public sealed class LlmJudgeEvaluator(ILanguageModel judge, string criteria) : IEvaluator<string>
{
    public string Name => "llm_judge";

    public async Task<EvaluationResult> EvaluateAsync(string output, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            Evaluate the following AI response based on these criteria: {{criteria}}

            Input: {{context.Input}}
            Response: {{output}}
            {{(context.ExpectedOutput is not null ? $"Expected: {context.ExpectedOutput}" : "")}}

            Score the response from 0.0 to 1.0 and provide brief reasoning.
            Respond in JSON: {"score": 0.85, "reasoning": "..."}
            """;

        var result = await judge.CompleteAsync(prompt, LLMOptions.Deterministic(256), cancellationToken);
        if (!result.IsSuccess) return new EvaluationResult { EvaluatorName = Name, Score = 0, Reasoning = "Evaluation failed" };

        try
        {
            var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(result.Value!);
            var score = json?["score"].GetSingle() ?? 0f;
            var reasoning = json?["reasoning"].GetString() ?? string.Empty;
            return new EvaluationResult { EvaluatorName = Name, Score = score, Reasoning = reasoning };
        }
        catch
        {
            return new EvaluationResult { EvaluatorName = Name, Score = 0, Reasoning = "Failed to parse evaluation" };
        }
    }
}

/// <summary>
/// Faithfulness evaluator — checks if the answer is grounded in retrieved documents.
/// Catches hallucinations in RAG pipelines.
/// </summary>
public sealed class FaithfulnessEvaluator(ILanguageModel judge) : IEvaluator<string>
{
    public string Name => "faithfulness";

    public async Task<EvaluationResult> EvaluateAsync(string output, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        if (context.RetrievedDocuments is null || context.RetrievedDocuments.Count == 0)
            return new EvaluationResult { EvaluatorName = Name, Score = 0, Reasoning = "No retrieved documents to check against" };

        var docs = string.Join("\n---\n", context.RetrievedDocuments);
        var prompt = $$"""
            Given these source documents:
            {{docs}}

            And this generated answer:
            {{output}}

            Score from 0.0-1.0 how faithfully the answer is grounded in the source documents.
            1.0 = every claim is supported. 0.0 = answer contradicts or fabricates information.
            Respond in JSON: {"score": 0.9, "reasoning": "..."}
            """;

        var result = await judge.CompleteAsync(prompt, LLMOptions.Deterministic(256), cancellationToken);
        if (!result.IsSuccess) return new EvaluationResult { EvaluatorName = Name, Score = 0 };

        try
        {
            var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(result.Value!);
            return new EvaluationResult
            {
                EvaluatorName = Name,
                Score = json?["score"].GetSingle() ?? 0f,
                Reasoning = json?["reasoning"].GetString() ?? string.Empty
            };
        }
        catch
        {
            return new EvaluationResult { EvaluatorName = Name, Score = 0 };
        }
    }
}
