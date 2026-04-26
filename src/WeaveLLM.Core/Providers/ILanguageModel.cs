#nullable enable
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Providers;

/// <summary>
/// Provider-agnostic interface for LLM text completion calls.
/// Works with OpenAI, Anthropic, Azure, Ollama, HuggingFace, and others.
/// Concrete implementations live in provider-specific packages (e.g., WeaveLLM.Providers.OpenAI).
/// </summary>
public interface ILanguageModel
{
    /// <summary>The provider's display name (e.g., <c>"openai"</c>, <c>"anthropic"</c>).</summary>
    string ProviderName { get; }

    /// <summary>The model variant identifier (e.g., <c>"gpt-4o"</c>, <c>"claude-opus-4-7"</c>).</summary>
    string ModelId { get; }

    /// <summary>Sends a plain-text prompt and returns the raw completion.</summary>
    Task<ChainResult<string>> CompleteAsync(
        string prompt,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Streams the completion token-by-token.</summary>
    IAsyncEnumerable<string> StreamCompleteAsync(
        string prompt,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Estimates the token count for <paramref name="text"/> using the provider's tokenizer.</summary>
    Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chat-capable model interface. Processes a full conversation history and returns a structured reply.
/// </summary>
public interface IChatModel : ILanguageModel
{
    /// <summary>Sends a conversation and returns a structured <see cref="ChatResponse"/>.</summary>
    Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Streams the model's reply token-by-token.</summary>
    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Embedding model for converting text into dense vector representations.
/// </summary>
public interface IEmbeddingModel
{
    /// <summary>The provider's display name.</summary>
    string ProviderName { get; }

    /// <summary>The model variant identifier.</summary>
    string ModelId { get; }

    /// <summary>Output vector dimensionality (e.g., 1536 for text-embedding-3-small).</summary>
    int Dimensions { get; }

    /// <summary>Embeds a single text string.</summary>
    Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embeds multiple texts in a single batched call.</summary>
    Task<ChainResult<float[][]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
