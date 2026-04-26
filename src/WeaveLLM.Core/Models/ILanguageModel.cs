#nullable enable
namespace WeaveLLM.Core.Models;

/// <summary>
/// Base interface for all language model providers.
/// Every model exposes its identity so consumers can make routing decisions at runtime.
/// </summary>
public interface ILanguageModel
{
    /// <summary>Provider-scoped identifier for the specific model variant (e.g., <c>"gpt-4o"</c>, <c>"claude-opus-4-7"</c>).</summary>
    string ModelId { get; }

    /// <summary>The name of the underlying provider (e.g., <c>"openai"</c>, <c>"anthropic"</c>, <c>"ollama"</c>).</summary>
    string ProviderId { get; }
}

/// <summary>
/// A chat-capable language model that processes a conversation history and returns a single structured reply.
/// </summary>
public interface IChatModel : ILanguageModel
{
    /// <summary>
    /// Sends a conversation to the model and returns a structured response.
    /// </summary>
    /// <param name="messages">The conversation history, ordered oldest-first. Must include at least one message.</param>
    /// <param name="options">Per-call tuning options; <c>null</c> means provider defaults.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>
    /// A <see cref="ChainResult{T}"/> containing a <see cref="ChatResponse"/> on success,
    /// or a <see cref="WeaveLLMError"/> describing the failure.
    /// </returns>
    Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A chat model that additionally supports token-by-token streaming via <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
public interface IStreamingChatModel : IChatModel
{
    /// <summary>
    /// Streams the model's response as an async sequence of token strings.
    /// The caller is responsible for assembling tokens into the full response.
    /// </summary>
    /// <param name="messages">The conversation history, ordered oldest-first.</param>
    /// <param name="options">Per-call tuning options; <c>null</c> means provider defaults.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A model that converts text into dense vector embeddings for semantic search and retrieval.
/// </summary>
public interface IEmbeddingModel : ILanguageModel
{
    /// <summary>Embeds a single text string into a float vector.</summary>
    /// <param name="text">The text to embed. Must be non-empty.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embeds multiple texts in a single batch call, reducing round-trips.</summary>
    /// <param name="texts">The texts to embed. Must be non-empty.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<ChainResult<float[][]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
