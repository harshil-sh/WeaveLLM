#nullable enable
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Memory;

/// <summary>
/// Pluggable vector store for dense-vector similarity search and retrieval.
/// Implement this for pgvector, Qdrant, Pinecone, Chroma, Weaviate, and similar stores.
/// </summary>
public interface IVectorStore
{
    /// <summary>Inserts a new entry or updates an existing one with the same <see cref="VectorEntry.Id"/>.</summary>
    /// <param name="entry">The vector entry to upsert.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task UpsertAsync(VectorEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the top-<paramref name="topK"/> entries most similar to the query vector,
    /// ordered by descending similarity score.
    /// </summary>
    /// <param name="queryVector">The query embedding to search against.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<ChainResult<IReadOnlyList<ScoredEntry>>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the entry with the specified identifier. No-ops if the entry does not exist.</summary>
    /// <param name="id">The unique identifier of the entry to delete.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// A document chunk stored alongside its dense vector embedding.
/// </summary>
/// <param name="Id">Unique, stable identifier for this entry. Used for upsert deduplication and deletion.</param>
/// <param name="Vector">The dense embedding vector produced by an <see cref="IEmbeddingModel"/>.</param>
/// <param name="Content">The original text this vector was derived from, returned in search results.</param>
/// <param name="Metadata">Optional key/value pairs for filtering, display, or source attribution.</param>
public sealed record VectorEntry(
    string Id,
    float[] Vector,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// A vector store search result paired with its cosine similarity score.
/// </summary>
/// <param name="Entry">The matched vector entry.</param>
/// <param name="Score">Similarity score in [0, 1]; higher means more similar to the query.</param>
public sealed record ScoredEntry(VectorEntry Entry, float Score);
