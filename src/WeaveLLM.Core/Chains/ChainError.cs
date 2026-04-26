#nullable enable
namespace WeaveLLM.Core.Chains;

/// <summary>
/// Structured error value for chain failures. Errors are values — never throw for expected failures; return this instead.
/// </summary>
/// <param name="Code">Machine-readable error category (e.g., "RATE_LIMITED", "TIMEOUT").</param>
/// <param name="Message">Human-readable description of the failure.</param>
/// <param name="InnerException">The originating exception, if any.</param>
public sealed record ChainError(string Code, string Message, Exception? InnerException = null)
{
    /// <summary>Creates an error for provider-level failures (API errors, model errors).</summary>
    public static ChainError ProviderError(string message, Exception? ex = null) =>
        new("PROVIDER_ERROR", message, ex);

    /// <summary>Creates an error when the upstream provider enforces rate limits.</summary>
    public static ChainError RateLimited(string provider) =>
        new("RATE_LIMITED", $"Rate limited by provider '{provider}'.");

    /// <summary>Creates an error when the chain execution exceeds its deadline.</summary>
    public static ChainError Timeout(string chainName) =>
        new("TIMEOUT", $"Chain '{chainName}' timed out.");

    /// <summary>Creates an error for malformed or semantically invalid inputs.</summary>
    public static ChainError InvalidInput(string message) =>
        new("INVALID_INPUT", message);

    /// <summary>Creates an error when the prompt exceeds the model's context window.</summary>
    public static ChainError ContextTooLong(int tokenCount) =>
        new("CONTEXT_TOO_LONG", $"Context exceeds model limit: {tokenCount} tokens.");

    /// <summary>Creates an error when a tool invocation fails.</summary>
    public static ChainError ToolExecutionFailed(string toolName, string reason) =>
        new("TOOL_EXECUTION_FAILED", $"Tool '{toolName}' failed: {reason}");
}
