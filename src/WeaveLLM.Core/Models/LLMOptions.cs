#nullable enable
namespace WeaveLLM.Core.Models;

/// <summary>
/// Tunable per-call options for any language model. All properties are optional;
/// each provider maps them to its own API parameters and ignores unsupported fields.
/// </summary>
public sealed record LLMOptions
{
    /// <summary>Sampling temperature. Higher values produce more varied output; lower values produce more deterministic output.</summary>
    public float? Temperature { get; init; }

    /// <summary>Maximum number of tokens the model may generate in its response.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Nucleus sampling probability cutoff. Only the top-p probability mass is considered at each step.</summary>
    public float? TopP { get; init; }

    /// <summary>Stop generation when any of these sequences appear in the output.</summary>
    public string[]? StopSequences { get; init; }

    /// <summary>When <c>true</c>, the provider streams the response token-by-token.</summary>
    public bool? Stream { get; init; }

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
