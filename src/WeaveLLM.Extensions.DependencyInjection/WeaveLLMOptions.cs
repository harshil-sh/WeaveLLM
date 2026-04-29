#nullable enable
using System.ComponentModel.DataAnnotations;

namespace WeaveLLM.Extensions.DependencyInjection;

/// <summary>
/// Root options object for WeaveLLM, designed for binding against the <c>WeaveLLM</c>
/// configuration section via <c>IOptions&lt;WeaveLLMOptions&gt;</c>.
/// </summary>
/// <remarks>
/// Bind via:
/// <code>
/// services.AddOptions&lt;WeaveLLMOptions&gt;()
///     .Bind(configuration.GetSection("WeaveLLM"))
///     .ValidateDataAnnotations()
///     .ValidateOnStart();
/// </code>
/// Or use the <see cref="WeaveLLMBuilder.AddWeaveLLMOptions"/> helper which does this for you.
/// </remarks>
public sealed class WeaveLLMOptions
{
    /// <summary>OpenAI provider settings.</summary>
    public OpenAIOptions OpenAI { get; set; } = new();

    /// <summary>Anthropic provider settings.</summary>
    public AnthropicOptions Anthropic { get; set; } = new();

    /// <summary>Ollama local provider settings.</summary>
    public OllamaOptions Ollama { get; set; } = new();

    /// <summary>HuggingFace Inference API provider settings.</summary>
    public HuggingFaceOptions HuggingFace { get; set; } = new();

    /// <summary>Memory and vector store settings.</summary>
    public MemoryOptions Memory { get; set; } = new();

    /// <summary>Agent behaviour settings.</summary>
    public AgentsOptions Agents { get; set; } = new();

    /// <summary>Observability and telemetry settings.</summary>
    public TelemetryOptions Telemetry { get; set; } = new();

    // ── Nested option classes ─────────────────────────────────────────────────────

    /// <summary>OpenAI-specific provider configuration.</summary>
    public sealed class OpenAIOptions
    {
        /// <summary>
        /// Your OpenAI API key. Required when OpenAI is the active provider.
        /// Set via environment variable <c>WeaveLLM__OpenAI__ApiKey</c> to avoid committing secrets.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>Chat model variant to use. Defaults to <c>gpt-4o</c>.</summary>
        public string ModelId { get; set; } = "gpt-4o";

        /// <summary>Embedding model variant to use. Defaults to <c>text-embedding-3-small</c>.</summary>
        public string EmbeddingModelId { get; set; } = "text-embedding-3-small";
    }

    /// <summary>Anthropic-specific provider configuration.</summary>
    public sealed class AnthropicOptions
    {
        /// <summary>
        /// Your Anthropic API key. Required when Anthropic is the active provider.
        /// Set via environment variable <c>WeaveLLM__Anthropic__ApiKey</c> to avoid committing secrets.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>Chat model variant to use. Defaults to <c>claude-sonnet-4-20250514</c>.</summary>
        public string ModelId { get; set; } = "claude-sonnet-4-20250514";
    }

    /// <summary>Ollama local server configuration.</summary>
    public sealed class OllamaOptions
    {
        /// <summary>Base URL of the running Ollama server. Defaults to <c>http://localhost:11434</c>.</summary>
        public string BaseUrl { get; set; } = "http://localhost:11434";

        /// <summary>Model to load. If <c>null</c>, the builder default (<c>llama3</c>) is used.</summary>
        public string? ModelId { get; set; }
    }

    /// <summary>HuggingFace Inference API configuration.</summary>
    public sealed class HuggingFaceOptions
    {
        /// <summary>
        /// Your HuggingFace API key (Bearer token). Required when HuggingFace is the active provider.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// HuggingFace model repository ID (e.g., <c>mistralai/Mistral-7B-Instruct-v0.1</c>).
        /// Required when HuggingFace is the active provider.
        /// </summary>
        public string? ModelId { get; set; }
    }

    /// <summary>Memory and vector store configuration.</summary>
    public sealed class MemoryOptions
    {
        /// <summary>
        /// The backing store to use. Accepted values: <c>inmemory</c>, <c>redis</c>,
        /// <c>postgres</c>, <c>qdrant</c>. Defaults to <c>inmemory</c>.
        /// </summary>
        [Required]
        public string Provider { get; set; } = "inmemory";

        /// <summary>Redis connection string. Required when <see cref="Provider"/> is <c>redis</c>.</summary>
        public string? RedisConnectionString { get; set; }

        /// <summary>PostgreSQL connection string. Required when <see cref="Provider"/> is <c>postgres</c>.</summary>
        public string? PostgresConnectionString { get; set; }

        /// <summary>Qdrant gRPC/HTTP endpoint (e.g., <c>http://localhost:6333</c>). Required when <see cref="Provider"/> is <c>qdrant</c>.</summary>
        public string? QdrantEndpoint { get; set; }
    }

    /// <summary>Agent execution configuration.</summary>
    public sealed class AgentsOptions
    {
        /// <summary>
        /// The default agent type to register as <c>IAgent</c>.
        /// Accepted values: <c>react</c>, <c>plan-execute</c>. Defaults to <c>react</c>.
        /// </summary>
        [Required]
        public string DefaultType { get; set; } = "react";

        /// <summary>
        /// Maximum reasoning or execution steps before the agent returns a <c>MaxStepsExceeded</c> error.
        /// Defaults to <c>10</c>.
        /// </summary>
        [Range(1, 1000)]
        public int MaxSteps { get; set; } = 10;
    }

    /// <summary>Observability and OpenTelemetry configuration.</summary>
    public sealed class TelemetryOptions
    {
        /// <summary>
        /// When <c>true</c>, WeaveLLM registers its ActivitySource and Meter with the OpenTelemetry SDK.
        /// Defaults to <c>false</c>.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Service name reported in traces and metrics. Defaults to <c>WeaveLLM</c> when not set.
        /// </summary>
        public string? ServiceName { get; set; }
    }
}
