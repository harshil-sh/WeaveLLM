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

    /// <summary>Creates an error when a requested resource is not found.</summary>
    /// <remarks>Error code emitted: <c>NOT_FOUND</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static ChainError NotFound(string message) =>
        new("NOT_FOUND", message);

    /// <summary>Creates an error when the operation is cancelled.</summary>
    /// <remarks>Error code emitted: <c>CANCELLED</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static ChainError Cancelled(string message, Exception? inner = null) =>
        new("CANCELLED", message, inner);

    /// <summary>Creates an error when authentication fails.</summary>
    /// <remarks>Error code emitted: <c>AUTHENTICATION_FAILED</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static ChainError AuthenticationFailed(string message) =>
        new("AUTHENTICATION_FAILED", message);

    /// <summary>Creates an error when the provider's rate limit is exceeded.</summary>
    /// <remarks>Error code emitted: <c>RATE_LIMIT_EXCEEDED</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static ChainError RateLimitExceeded(string message) =>
        new("RATE_LIMIT_EXCEEDED", message);

    /// <summary>Creates an error when a network timeout occurs.</summary>
    /// <remarks>Error code emitted: <c>NETWORK_TIMEOUT</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static ChainError NetworkTimeout(string message, Exception? inner = null) =>
        new("NETWORK_TIMEOUT", message, inner);

    /// <summary>Creates an error for invalid or missing configuration.</summary>
    /// <remarks>Error code emitted: <c>INVALID_CONFIGURATION</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static ChainError InvalidConfiguration(string message) =>
        new("INVALID_CONFIGURATION", message);
}
