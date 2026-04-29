using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WeaveLLM.Observability.Tracing;

/// <summary>
/// OpenTelemetry metric instruments for LLM request observability.
/// Records token usage, latency, errors, and request counts with provider and model tags.
/// </summary>
public static class LlmMetrics
{
    /// <summary>Distribution of prompt token counts per request.</summary>
    public static readonly Histogram<int> PromptTokens =
        WeaveLLMTelemetry.Meter.CreateHistogram<int>(
            "weavellm_llm_tokens_prompt", "tokens", "Prompt tokens consumed per LLM request.");

    /// <summary>Distribution of completion token counts per request.</summary>
    public static readonly Histogram<int> CompletionTokens =
        WeaveLLMTelemetry.Meter.CreateHistogram<int>(
            "weavellm_llm_tokens_completion", "tokens", "Completion tokens generated per LLM request.");

    /// <summary>Distribution of end-to-end LLM request latency in milliseconds.</summary>
    public static readonly Histogram<double> LatencyMs =
        WeaveLLMTelemetry.Meter.CreateHistogram<double>(
            "weavellm_llm_latency_ms", "ms", "End-to-end LLM request latency.");

    /// <summary>Total number of LLM errors, tagged by <c>error_code</c>.</summary>
    public static readonly Counter<int> Errors =
        WeaveLLMTelemetry.Meter.CreateCounter<int>(
            "weavellm_llm_errors_total", "errors", "Total LLM errors by error code.");

    /// <summary>Total number of LLM requests, tagged by <c>provider_id</c> and <c>model_id</c>.</summary>
    public static readonly Counter<int> Requests =
        WeaveLLMTelemetry.Meter.CreateCounter<int>(
            "weavellm_llm_requests_total", "requests", "Total LLM requests by provider and model.");

    /// <summary>
    /// Records metrics for a single completed LLM request.
    /// Increments the request counter, records token histograms and latency, and optionally records an error.
    /// </summary>
    /// <param name="providerId">The provider identifier (e.g., <c>"openai"</c>).</param>
    /// <param name="modelId">The model identifier (e.g., <c>"gpt-4o"</c>).</param>
    /// <param name="promptTokens">Number of tokens in the prompt.</param>
    /// <param name="completionTokens">Number of tokens in the completion.</param>
    /// <param name="latencyMs">End-to-end request latency in milliseconds.</param>
    /// <param name="errorCode">Error code to record, or <c>null</c> for a successful request.</param>
    public static void RecordRequest(
        string providerId,
        string modelId,
        int promptTokens,
        int completionTokens,
        double latencyMs,
        string? errorCode = null)
    {
        var providerTags = new TagList { { "provider_id", providerId }, { "model_id", modelId } };

        Requests.Add(1, providerTags);
        PromptTokens.Record(promptTokens, providerTags);
        CompletionTokens.Record(completionTokens, providerTags);
        LatencyMs.Record(latencyMs, providerTags);

        if (errorCode is not null)
            Errors.Add(1, new TagList { { "error_code", errorCode } });
    }
}
