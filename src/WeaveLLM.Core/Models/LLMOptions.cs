#nullable enable
namespace WeaveLLM.Core.Models;

/// <summary>
/// Tunable per-call options for any language model. All properties are optional;
/// each provider maps them to its own API parameters and ignores unsupported fields.
/// </summary>
public sealed record LLMOptions
{
    /// <summary>
    /// Sampling temperature that controls output randomness. Valid range is 0.0–2.0.
    /// Lower values produce more deterministic output; higher values produce more varied output.
    /// <c>null</c> defers to the provider default.
    /// </summary>
    public float? Temperature { get; init; }

    /// <summary>
    /// Maximum number of tokens the model may generate in its response.
    /// <c>null</c> omits the field from the request body, causing the provider to use its own
    /// default (typically the model maximum).
    /// <para>
    /// <b>Warning:</b> <c>null</c> may produce large or expensive responses on providers that do
    /// not have a sensible built-in default.
    /// </para>
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Nucleus sampling probability cutoff in the range 0.0–1.0. Only tokens within the top-p
    /// cumulative probability mass are considered at each step.
    /// <c>null</c> defers to the provider default.
    /// </summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Sequences that cause the model to stop generating further tokens when any one of them
    /// appears in the output. <c>null</c> means no stop sequences are applied.
    /// </summary>
    public string[]? StopSequences { get; init; }

    /// <summary>
    /// Controls whether the provider streams the response token-by-token.
    /// <c>null</c> or <c>false</c> requests a blocking (complete) response;
    /// <c>true</c> enables streaming via <c>IStreamingChatModel.StreamChatAsync</c>.
    /// </summary>
    public bool? Stream { get; init; }

    /// <summary>
    /// Production-safe defaults: MaxTokens = 2048, Temperature = 0.4.
    /// Use as a starting point when no custom options are needed.
    /// </summary>
    public static LLMOptions Default { get; } =
        new() { MaxTokens = 2048, Temperature = 0.4f };

    /// <summary>
    /// No constraints — all properties null, provider decides everything.
    /// Explicit opt-in: use only when you understand the provider's defaults.
    /// </summary>
    public static LLMOptions Unconstrained { get; } = new();

    /// <summary>
    /// Balanced preset — moderate temperature suitable for most conversational and reasoning tasks.
    /// </summary>
    public static LLMOptions Balanced(int maxTokens = 2048) =>
        new() { Temperature = 0.4f, MaxTokens = maxTokens };

    /// <summary>
    /// Creative preset — high temperature for brainstorming, story generation, and open-ended tasks.
    /// </summary>
    public static LLMOptions Creative(int maxTokens = 2048) =>
        new() { Temperature = 0.9f, TopP = 0.95f, MaxTokens = maxTokens };

    /// <summary>
    /// Precise preset — near-zero temperature for deterministic, factual outputs and structured generation.
    /// </summary>
    public static LLMOptions Precise(int maxTokens = 2048) =>
        new() { Temperature = 0.0f, MaxTokens = maxTokens };

    /// <summary>
    /// Deterministic preset — zero temperature for reproducible outputs (e.g., evaluation and grading).
    /// </summary>
    public static LLMOptions Deterministic(int maxTokens = 256) =>
        new() { Temperature = 0.0f, MaxTokens = maxTokens };
}
