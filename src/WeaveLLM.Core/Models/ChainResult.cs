using Microsoft.Extensions.Logging;

namespace WeaveLLM.Core.Models;

/// <summary>
/// Result wrapper for all chain executions. Never throws — errors are values.
/// </summary>
/// <typeparam name="T">The type of the successful output value.</typeparam>
public sealed class ChainResult<T>
{
    /// <summary>Whether the chain execution completed without error.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Convenience inverse of <see cref="IsSuccess"/>.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The output value, populated when <see cref="IsSuccess"/> is <c>true</c>.</summary>
    public T? Value { get; init; }

    /// <summary>The structured error, populated when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public WeaveLLMError? Error { get; init; }

    /// <summary>Wall-clock time the chain took to execute.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Token consumption recorded during this execution.</summary>
    public TokenUsage TokenUsage { get; init; } = new();

    /// <summary>Arbitrary key-value pairs attached by the chain or middleware.</summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Creates a successful result carrying <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The output produced by the chain.</param>
    /// <param name="usage">Optional token usage to attach; defaults to an empty <see cref="TokenUsage"/>.</param>
    /// <returns>A <see cref="ChainResult{T}"/> with <see cref="IsSuccess"/> set to <c>true</c>.</returns>
    public static ChainResult<T> Success(T value, TokenUsage? usage = null) =>
        new() { IsSuccess = true, Value = value, TokenUsage = usage ?? new() };

    /// <summary>
    /// Creates a failed result carrying a structured <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The structured error describing the failure.</param>
    /// <returns>A <see cref="ChainResult{T}"/> with <see cref="IsSuccess"/> set to <c>false</c>.</returns>
    public static ChainResult<T> Failure(WeaveLLMError error) =>
        new() { IsSuccess = false, Error = error };

    /// <summary>
    /// Creates a failed result from a plain error message and optional error code.
    /// </summary>
    /// <param name="message">Human-readable description of the failure.</param>
    /// <param name="code">Machine-readable error code; defaults to <c>"UNKNOWN"</c>.</param>
    /// <returns>A <see cref="ChainResult{T}"/> with <see cref="IsSuccess"/> set to <c>false</c>.</returns>
    public static ChainResult<T> Failure(string message, string? code = null) =>
        Failure(new WeaveLLMError(message, code ?? "UNKNOWN"));

    /// <summary>
    /// Projects the success value to a new type, or propagates the existing failure unchanged.
    /// </summary>
    /// <typeparam name="TNext">The target output type.</typeparam>
    /// <param name="mapper">Transform applied to <see cref="Value"/> when this result is successful.</param>
    /// <returns>
    /// A successful <see cref="ChainResult{TNext}"/> with the mapped value,
    /// or a failed <see cref="ChainResult{TNext}"/> carrying the original <see cref="Error"/>.
    /// </returns>
    public ChainResult<TNext> Map<TNext>(Func<T, TNext> mapper) =>
        IsSuccess
            ? ChainResult<TNext>.Success(mapper(Value!), TokenUsage)
            : ChainResult<TNext>.Failure(Error!);

    /// <summary>
    /// Deconstructs this result into its three core components.
    /// </summary>
    /// <param name="isSuccess">Whether the execution succeeded.</param>
    /// <param name="value">The output value, or <c>default</c> on failure.</param>
    /// <param name="error">The structured error, or <c>null</c> on success.</param>
    public void Deconstruct(out bool isSuccess, out T? value, out WeaveLLMError? error)
    {
        isSuccess = IsSuccess;
        value = Value;
        error = Error;
    }
}

/// <summary>
/// Shared context flowing through the entire chain pipeline.
/// Carries trace IDs, logger, metadata, and cancellation.
/// </summary>
public sealed class ChainContext
{
    /// <summary>Short identifier used to correlate logs for a single execution.</summary>
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Name of the currently executing chain, updated as execution progresses.</summary>
    public string ChainName { get; set; } = string.Empty;

    /// <summary>Identifier for the user session that initiated this pipeline run.</summary>
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Optional identifier for the user associated with this execution.</summary>
    public string? UserId { get; init; }

    /// <summary>Logger instance available to all chains and middleware in the pipeline.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>Arbitrary key-value pairs that chains and middleware can read or write.</summary>
    public Dictionary<string, object> Metadata { get; } = new();

    /// <summary>Typed variables scoped to this execution, accessible via <see cref="GetVariable{T}"/> and <see cref="SetVariable{T}"/>.</summary>
    public Dictionary<string, object> Variables { get; } = new();

    /// <summary>UTC timestamp when this context was created.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Ordered record of every step executed within this pipeline run.</summary>
    public List<ExecutionStep> ExecutionHistory { get; } = new();

    /// <summary>
    /// Retrieves a typed variable by key, returning <c>default</c> if absent or of a different type.
    /// </summary>
    /// <typeparam name="T">The expected type of the variable.</typeparam>
    /// <param name="key">The variable key.</param>
    /// <returns>The value cast to <typeparamref name="T"/>, or <c>default</c> if not found or type mismatch.</returns>
    public T? GetVariable<T>(string key) =>
        Variables.TryGetValue(key, out var v) && v is T typed ? typed : default;

    /// <summary>
    /// Stores a typed variable under the given key, overwriting any existing value.
    /// </summary>
    /// <typeparam name="T">The type of the value being stored.</typeparam>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The value to store.</param>
    public void SetVariable<T>(string key, T value) => Variables[key] = value!;

    /// <summary>
    /// Creates a new <see cref="ChainContext"/> with optional user and logger bindings.
    /// </summary>
    /// <param name="userId">Optional user identifier to associate with this context.</param>
    /// <param name="logger">Optional logger available throughout the pipeline.</param>
    /// <returns>A new <see cref="ChainContext"/> instance.</returns>
    public static ChainContext Create(string? userId = null, ILogger? logger = null) =>
        new() { UserId = userId, Logger = logger };
}

/// <summary>
/// Represents a single step in the execution history.
/// </summary>
/// <param name="StepName">Name of the chain or middleware that produced this step.</param>
/// <param name="StartedAt">UTC timestamp when the step began.</param>
/// <param name="CompletedAt">UTC timestamp when the step finished.</param>
/// <param name="Success">Whether the step completed without error.</param>
/// <param name="TokenUsage">Token consumption recorded for this step.</param>
/// <param name="ErrorMessage">Human-readable error description, or <c>null</c> if the step succeeded.</param>
public sealed record ExecutionStep(
    string StepName,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool Success,
    TokenUsage TokenUsage,
    string? ErrorMessage = null);

/// <summary>
/// Tracks token consumption per request. Used for cost calculation.
/// </summary>
public sealed class TokenUsage
{
    /// <summary>Number of tokens in the prompt sent to the model.</summary>
    public int PromptTokens { get; set; }

    /// <summary>Number of tokens in the model's response.</summary>
    public int CompletionTokens { get; set; }

    /// <summary>Sum of <see cref="PromptTokens"/> and <see cref="CompletionTokens"/>.</summary>
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>Estimated cost in USD based on the provider's token pricing.</summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>
    /// Combines two <see cref="TokenUsage"/> instances by summing all fields.
    /// </summary>
    /// <param name="a">The first usage to add.</param>
    /// <param name="b">The second usage to add.</param>
    /// <returns>A new <see cref="TokenUsage"/> whose fields are the element-wise sums of <paramref name="a"/> and <paramref name="b"/>.</returns>
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new()
    {
        PromptTokens = a.PromptTokens + b.PromptTokens,
        CompletionTokens = a.CompletionTokens + b.CompletionTokens,
        EstimatedCostUsd = a.EstimatedCostUsd + b.EstimatedCostUsd
    };
}

/// <summary>
/// Structured error type — no stringly-typed exceptions.
/// </summary>
/// <param name="Message">Human-readable description of the failure.</param>
/// <param name="Code">Machine-readable error code for programmatic handling.</param>
/// <param name="InnerException">Original exception that caused this error, if any.</param>
public sealed record WeaveLLMError(string Message, string Code, Exception? InnerException = null)
{
    /// <summary>
    /// Creates a timeout error for the named chain.
    /// </summary>
    /// <param name="chainName">Name of the chain that exceeded its time limit.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"TIMEOUT"</c>.</returns>
    /// <remarks>Error code emitted: <c>TIMEOUT</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError Timeout(string chainName) => new($"Chain '{chainName}' timed out.", "TIMEOUT");

    /// <summary>
    /// Creates a rate-limit error for the named provider.
    /// </summary>
    /// <param name="provider">Name of the provider that returned a rate-limit response.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"RATE_LIMITED"</c>.</returns>
    /// <remarks>Error code emitted: <c>RATE_LIMITED</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError RateLimited(string provider) => new($"Rate limited by provider '{provider}'.", "RATE_LIMITED");

    /// <summary>
    /// Creates an invalid-input error with the given message.
    /// </summary>
    /// <param name="message">Description of what was wrong with the input.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"INVALID_INPUT"</c>.</returns>
    /// <remarks>Error code emitted: <c>INVALID_INPUT</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError InvalidInput(string message) => new(message, "INVALID_INPUT");

    /// <summary>
    /// Creates a provider-level error prefixed with the provider name.
    /// </summary>
    /// <param name="provider">Name of the LLM provider that returned the error.</param>
    /// <param name="message">Error detail returned by the provider.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"PROVIDER_ERROR"</c>.</returns>
    /// <remarks>Error code emitted: <c>PROVIDER_ERROR</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError ProviderError(string provider, string message) => new($"[{provider}] {message}", "PROVIDER_ERROR");

    /// <summary>
    /// Creates a provider-level error prefixed with the provider name, attaching the originating exception.
    /// </summary>
    /// <param name="provider">Name of the LLM provider that returned the error.</param>
    /// <param name="message">Error detail returned by the provider.</param>
    /// <param name="inner">The originating exception.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"PROVIDER_ERROR"</c>.</returns>
    /// <remarks>Error code emitted: <c>PROVIDER_ERROR</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError ProviderError(string provider, string message, Exception inner) => new($"[{provider}] {message}", "PROVIDER_ERROR", inner);

    /// <summary>
    /// Creates a not-found error with the given message.
    /// </summary>
    /// <param name="message">Description of what was not found.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"NOT_FOUND"</c>.</returns>
    /// <remarks>Error code emitted: <c>NOT_FOUND</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError NotFound(string message) => new(message, "NOT_FOUND");

    /// <summary>
    /// Creates a cancellation error with the given message.
    /// </summary>
    /// <param name="message">Description of why the operation was cancelled.</param>
    /// <param name="inner">Optional originating exception.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"CANCELLED"</c>.</returns>
    /// <remarks>Error code emitted: <c>CANCELLED</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError Cancelled(string message, Exception? inner = null) => new(message, "CANCELLED", inner);

    /// <summary>
    /// Creates an authentication-failure error with the given message.
    /// </summary>
    /// <param name="message">Description of the authentication failure.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"AUTHENTICATION_FAILED"</c>.</returns>
    /// <remarks>Error code emitted: <c>AUTHENTICATION_FAILED</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError AuthenticationFailed(string message) => new(message, "AUTHENTICATION_FAILED");

    /// <summary>
    /// Creates a rate-limit-exceeded error with the given message.
    /// </summary>
    /// <param name="message">Description of the rate limit exceeded condition.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"RATE_LIMIT_EXCEEDED"</c>.</returns>
    /// <remarks>Error code emitted: <c>RATE_LIMIT_EXCEEDED</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError RateLimitExceeded(string message) => new(message, "RATE_LIMIT_EXCEEDED");

    /// <summary>
    /// Creates a network-timeout error with the given message.
    /// </summary>
    /// <param name="message">Description of the network timeout.</param>
    /// <param name="inner">Optional originating exception.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"NETWORK_TIMEOUT"</c>.</returns>
    /// <remarks>Error code emitted: <c>NETWORK_TIMEOUT</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError NetworkTimeout(string message, Exception? inner = null) => new(message, "NETWORK_TIMEOUT", inner);

    /// <summary>
    /// Creates an invalid-configuration error with the given message.
    /// </summary>
    /// <param name="message">Description of the configuration problem.</param>
    /// <returns>A <see cref="WeaveLLMError"/> with code <c>"INVALID_CONFIGURATION"</c>.</returns>
    /// <remarks>Error code emitted: <c>INVALID_CONFIGURATION</c> (SCREAMING_SNAKE_CASE — canonical for all WeaveLLM factory methods).</remarks>
    public static WeaveLLMError InvalidConfiguration(string message) => new(message, "INVALID_CONFIGURATION");
}

/// <summary>
/// Default pass-through chain input/output for untyped pipelines.
/// </summary>
public sealed class ChainInput
{
    /// <summary>Primary text payload passed into the chain.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Named variables available for prompt template substitution.</summary>
    public Dictionary<string, object> Variables { get; init; } = new();

    /// <summary>Prior conversation turns provided as context.</summary>
    public IReadOnlyList<Message> History { get; init; } = [];
}

/// <summary>
/// Default pass-through output produced by untyped pipelines.
/// </summary>
public sealed class ChainOutput
{
    /// <summary>Primary text payload returned by the chain.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Structured data extracted or produced alongside the text output.</summary>
    public Dictionary<string, object> Data { get; init; } = new();

    /// <summary>Token consumption recorded for this chain execution.</summary>
    public TokenUsage TokenUsage { get; init; } = new();
}
