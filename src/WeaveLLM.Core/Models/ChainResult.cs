using Microsoft.Extensions.Logging;

namespace WeaveLLM.Core.Models;

/// <summary>
/// Result wrapper for all chain executions. Never throws — errors are values.
/// </summary>
public sealed class ChainResult<T>
{
    public bool IsSuccess { get; init; }

    /// <summary>Convenience inverse of <see cref="IsSuccess"/>.</summary>
    public bool IsFailure => !IsSuccess;

    public T? Value { get; init; }
    public WeaveLLMError? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public TokenUsage TokenUsage { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();

    public static ChainResult<T> Success(T value, TokenUsage? usage = null) =>
        new() { IsSuccess = true, Value = value, TokenUsage = usage ?? new() };

    public static ChainResult<T> Failure(WeaveLLMError error) =>
        new() { IsSuccess = false, Error = error };

    public static ChainResult<T> Failure(string message, string? code = null) =>
        Failure(new WeaveLLMError(message, code ?? "UNKNOWN"));

    public ChainResult<TNext> Map<TNext>(Func<T, TNext> mapper) =>
        IsSuccess
            ? ChainResult<TNext>.Success(mapper(Value!), TokenUsage)
            : ChainResult<TNext>.Failure(Error!);

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
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string ChainName { get; set; } = string.Empty;
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
    public string? UserId { get; init; }
    public ILogger? Logger { get; init; }
    public Dictionary<string, object> Metadata { get; } = new();
    public Dictionary<string, object> Variables { get; } = new();
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public List<ExecutionStep> ExecutionHistory { get; } = new();

    public T? GetVariable<T>(string key) =>
        Variables.TryGetValue(key, out var v) && v is T typed ? typed : default;

    public void SetVariable<T>(string key, T value) => Variables[key] = value!;

    public static ChainContext Create(string? userId = null, ILogger? logger = null) =>
        new() { UserId = userId, Logger = logger };
}

/// <summary>
/// Represents a single step in the execution history.
/// </summary>
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
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public decimal EstimatedCostUsd { get; set; }

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
public sealed record WeaveLLMError(string Message, string Code, Exception? InnerException = null)
{
    public static WeaveLLMError Timeout(string chainName) => new($"Chain '{chainName}' timed out.", "TIMEOUT");
    public static WeaveLLMError RateLimited(string provider) => new($"Rate limited by provider '{provider}'.", "RATE_LIMITED");
    public static WeaveLLMError InvalidInput(string message) => new(message, "INVALID_INPUT");
    public static WeaveLLMError ProviderError(string provider, string message) => new($"[{provider}] {message}", "PROVIDER_ERROR");
}

/// <summary>
/// Default pass-through chain input/output for untyped pipelines.
/// </summary>
public sealed class ChainInput
{
    public string Text { get; init; } = string.Empty;
    public Dictionary<string, object> Variables { get; init; } = new();
    public IReadOnlyList<Message> History { get; init; } = [];
}

public sealed class ChainOutput
{
    public string Text { get; init; } = string.Empty;
    public Dictionary<string, object> Data { get; init; } = new();
    public TokenUsage TokenUsage { get; init; } = new();
}
