using WeaveLLM.Core.Models;
using WeaveLLM.Core.Providers;

namespace WeaveLLM.Core.Memory;

/// <summary>
/// Pluggable memory store. Implement this for Redis, CosmosDB, Postgres, SQLite, etc.
/// </summary>
public interface IMemoryStore
{
    Task SaveAsync(string sessionId, Message message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> LoadAsync(string sessionId, int maxMessages = 20, CancellationToken cancellationToken = default);
    Task ClearAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Vector memory store for semantic similarity search.
/// Implement this for pgvector, Qdrant, Pinecone, Chroma, Weaviate, etc.
/// </summary>
public interface IVectorStore
{
    string CollectionName { get; }
    Task UpsertAsync(MemoryRecord record, CancellationToken cancellationToken = default);
    Task UpsertBatchAsync(IReadOnlyList<MemoryRecord> records, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryMatch>> SearchAsync(float[] queryEmbedding, int topK = 5, float minScore = 0.7f, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryMatch>> HybridSearchAsync(float[] queryEmbedding, string queryText, int topK = 5, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<long> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A single memory record stored in a vector store.
/// </summary>
public sealed class MemoryRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Text { get; init; } = string.Empty;
    public float[] Embedding { get; init; } = [];
    public Dictionary<string, object> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Source { get; init; }
    public string? DocumentId { get; init; }
    public int? ChunkIndex { get; init; }
}

/// <summary>
/// A search result from the vector store with similarity score.
/// </summary>
public sealed record MemoryMatch(MemoryRecord Record, float Score)
{
    public bool IsRelevant(float threshold = 0.7f) => Score >= threshold;
}

/// <summary>
/// In-memory store for development and testing.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public sealed class InMemoryStore : IMemoryStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<Message>> _sessions = new();

    public Task SaveAsync(string sessionId, Message message, CancellationToken cancellationToken = default)
    {
        _sessions.GetOrAdd(sessionId, _ => []).Add(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Message>> LoadAsync(string sessionId, int maxMessages = 20, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var messages))
            return Task.FromResult<IReadOnlyList<Message>>(messages.TakeLast(maxMessages).ToList());
        return Task.FromResult<IReadOnlyList<Message>>([]);
    }

    public Task ClearAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.ContainsKey(sessionId));
}
