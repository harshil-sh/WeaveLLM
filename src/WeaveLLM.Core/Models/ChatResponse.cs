#nullable enable
namespace WeaveLLM.Core.Models;

/// <summary>
/// The structured response returned by a successful chat model call.
/// </summary>
public sealed record ChatResponse
{
    /// <summary>The text content of the model's reply.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The reason the model stopped generating tokens.
    /// Common values: <c>"stop"</c>, <c>"length"</c>, <c>"tool_calls"</c>, <c>"content_filter"</c>.
    /// </summary>
    public string? FinishReason { get; init; }

    /// <summary>Token usage statistics for this request. May be <c>null</c> if the provider does not report usage.</summary>
    public UsageStats? Usage { get; init; }

    /// <summary>
    /// Tool calls requested by the model in this turn, or <c>null</c> when no tools were invoked.
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
}

/// <summary>
/// Token consumption statistics for a single model request.
/// Used for cost accounting and context-length tracking.
/// </summary>
/// <param name="PromptTokens">Tokens consumed by the input prompt and conversation history.</param>
/// <param name="CompletionTokens">Tokens generated in the model's response.</param>
/// <param name="TotalTokens">Sum of <see cref="PromptTokens"/> and <see cref="CompletionTokens"/>.</param>
public sealed record UsageStats(int PromptTokens, int CompletionTokens, int TotalTokens);

/// <summary>
/// A single tool or function call requested by the model within a chat turn.
/// </summary>
/// <param name="Id">Unique call identifier used to correlate the tool result back to this call.</param>
/// <param name="Name">The registered name of the tool to invoke.</param>
/// <param name="ArgumentsJson">JSON-encoded arguments matching the tool's parameter schema.</param>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);
