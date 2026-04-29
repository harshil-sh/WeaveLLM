using WeaveLLM.Core.Models;

namespace WeaveLLM.Observability.Cost;

/// <summary>
/// Estimates the USD cost of LLM API calls based on published per-token pricing.
/// Returns <c>0</c> for unknown provider/model combinations — never throws.
/// </summary>
public static class CostTracker
{
    private static readonly Dictionary<string, (decimal PromptPer1M, decimal CompletionPer1M)> Pricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["openai/gpt-4o"]                         = (2.50m,   10.00m),
            ["openai/gpt-4o-mini"]                    = (0.15m,    0.60m),
            ["openai/gpt-4-turbo"]                    = (10.00m,  30.00m),
            ["openai/text-embedding-3-small"]         = (0.02m,    0.00m),
            ["openai/text-embedding-3-large"]         = (0.13m,    0.00m),
            ["anthropic/claude-sonnet-4-20250514"]    = (3.00m,   15.00m),
            ["anthropic/claude-haiku-4-5"]            = (0.80m,    4.00m),
        };

    /// <summary>
    /// Estimates the USD cost for a single LLM request.
    /// </summary>
    /// <param name="provider">Provider name, e.g., <c>"openai"</c> or <c>"anthropic"</c>.</param>
    /// <param name="modelId">Model identifier, e.g., <c>"gpt-4o"</c>.</param>
    /// <param name="usage">Token usage statistics from the response.</param>
    /// <returns>
    /// The estimated cost in USD, or <c>0</c> if the model is not in the pricing table.
    /// </returns>
    public static decimal EstimateCost(string provider, string modelId, UsageStats usage)
    {
        var key = $"{provider}/{modelId}";
        if (!Pricing.TryGetValue(key, out var rates))
            return 0m;

        return (usage.PromptTokens / 1_000_000m) * rates.PromptPer1M
             + (usage.CompletionTokens / 1_000_000m) * rates.CompletionPer1M;
    }
}
