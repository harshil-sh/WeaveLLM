using System.Text.Json;
using WeaveLLM.Core.Models;
using IChatModel = WeaveLLM.Core.Providers.IChatModel;

namespace WeaveLLM.Observability.Evaluation;

/// <summary>
/// Evaluates whether a model's answer is relevant to the question that was asked.
/// Uses an LLM-as-judge approach: the injected <see cref="IChatModel"/> scores the
/// answer against the question and returns a JSON object with a score and reasoning.
/// </summary>
public sealed class RelevanceEvaluator : IEvaluator
{
    private readonly IChatModel _judge;

    /// <summary>
    /// Initialises a new <see cref="RelevanceEvaluator"/> using <paramref name="judge"/> as the scoring model.
    /// </summary>
    /// <param name="judge">The chat model used to score relevance.</param>
    public RelevanceEvaluator(IChatModel judge)
    {
        _judge = judge;
    }

    /// <inheritdoc/>
    public async Task<EvaluationResult> EvaluateAsync(EvaluationInput input, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            Score 0.0–1.0: Is this answer relevant to the question?
            Question: {{input.Question}}
            Answer: {{input.Answer}}
            Return JSON only: {"score": 0.0, "reasoning": "..."}
            """;

        var messages = new[] { Message.User(prompt) };
        var result = await _judge.ChatAsync(messages, LLMOptions.Deterministic(), cancellationToken);

        if (!result.IsSuccess)
            return new EvaluationResult("relevance", 0f, "Evaluation failed: " + result.Error!.Message);

        return ParseResult("relevance", result.Value!.Content);
    }

    private static EvaluationResult ParseResult(string metric, string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var score = doc?["score"].GetSingle() ?? 0f;
            var reasoning = doc?["reasoning"].GetString() ?? string.Empty;
            return new EvaluationResult(metric, Math.Clamp(score, 0f, 1f), reasoning);
        }
        catch
        {
            return new EvaluationResult(metric, 0f, "Failed to parse evaluation response.");
        }
    }
}
