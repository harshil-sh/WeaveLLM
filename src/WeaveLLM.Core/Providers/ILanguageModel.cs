using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Providers;

/// <summary>
/// Provider-agnostic interface for all LLM text completion calls.
/// Works with OpenAI, Anthropic, Azure, Ollama, HuggingFace, etc.
/// </summary>
public interface ILanguageModel
{
    string ProviderName { get; }
    string ModelId { get; }

    Task<ChainResult<string>> CompleteAsync(
        string prompt,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamCompleteAsync(
        string prompt,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chat-specific model interface. Maintains message history.
/// </summary>
public interface IChatModel : ILanguageModel
{
    Task<ChainResult<Message>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Embedding model for vector representations.
/// </summary>
public interface IEmbeddingModel
{
    string ProviderName { get; }
    string ModelId { get; }
    int Dimensions { get; }

    Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<ChainResult<float[][]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tunable options per LLM call. All optional — each provider maps them to its own API params.
/// </summary>
public sealed class LLMOptions
{
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public int? Seed { get; set; }
    public string? ResponseFormat { get; set; } // "json_object", "text"
    public Dictionary<string, object> ProviderSpecific { get; set; } = new();

    public static LLMOptions Deterministic(int maxTokens = 2048) =>
        new() { Temperature = 0f, MaxTokens = maxTokens };

    public static LLMOptions Creative(int maxTokens = 2048) =>
        new() { Temperature = 0.9f, TopP = 0.95f, MaxTokens = maxTokens };

    public static LLMOptions Balanced(int maxTokens = 2048) =>
        new() { Temperature = 0.4f, MaxTokens = maxTokens };
}

/// <summary>
/// A single message in a chat conversation.
/// </summary>
public sealed class Message
{
    public MessageRole Role { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Name { get; init; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
    public string? ToolCallId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public static Message System(string content) => new() { Role = MessageRole.System, Content = content };
    public static Message User(string content) => new() { Role = MessageRole.User, Content = content };
    public static Message Assistant(string content) => new() { Role = MessageRole.Assistant, Content = content };
    public static Message Tool(string content, string toolCallId) =>
        new() { Role = MessageRole.Tool, Content = content, ToolCallId = toolCallId };
}

public enum MessageRole { System, User, Assistant, Tool }

/// <summary>
/// Represents a function/tool call requested by the LLM.
/// </summary>
public sealed class ToolCall
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string ToolName { get; init; } = string.Empty;
    public string Arguments { get; init; } = "{}";
}
