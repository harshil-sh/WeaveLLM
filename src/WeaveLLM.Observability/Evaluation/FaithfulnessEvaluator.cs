using System.Text.Json;
using WeaveLLM.Core.Models;
using IChatModel = WeaveLLM.Core.Providers.IChatModel;

namespace WeaveLLM.Observability.Evaluation;

/// <summary>
/// Evaluates whether a model's answer is faithful to the supplied context.
/// Uses an LLM-as-judge approach: the injected <see cref="IChatModel"/> scores the
/// answer against the context and returns a JSON object with a score and reasoning.
/// </summary>
public sealed class FaithfulnessEvaluator : IEvaluator
{
    private readonly IChatModel _judge;

    /// <summary>
    /// Initialises a new <see cref="FaithfulnessEvaluator"/> using <paramref name="judge"/> as the scoring model.
    /// </summary>
    /// <param name="judge">The chat model used to score faithfulness.</param>
    public FaithfulnessEvaluator(IChatModel judge)
    {
        _judge = judge;
    }

    /// <inheritdoc/>
    public async Task<EvaluationResult> EvaluateAsync(EvaluationInput input, CancellationToken cancellationToken = default)
    {
        var context = input.Context ?? string.Empty;
        var prompt = $$"""
            Score 0.0–1.0: Is this answer faithful to the context?
            Context: {{context}}
            Answer: {{input.Answer}}
            Return JSON only: {"score": 0.0, "reasoning": "..."}
            """;

        var messages = new[] { Message.User(prompt) };
        var result = await _judge.ChatAsync(messages, LLMOptions.Deterministic(), cancellationToken);

        if (!result.IsSuccess)
            return new EvaluationResult("faithfulness", 0f, "Evaluation failed: " + result.Error!.Message);

        return ParseResult("faithfulness", result.Value!.Content);
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
