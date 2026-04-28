#nullable enable
using System.Collections.Concurrent;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Memory.InMemory;

/// <summary>
/// Volatile in-memory vector store backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Performs exact cosine-similarity search over all stored entries. Suitable for development,
/// testing, and small datasets. Data is lost on process restart.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, VectorEntry> _store = new();

    /// <inheritdoc />
    public Task UpsertAsync(VectorEntry entry, CancellationToken cancellationToken = default)
    {
        _store[entry.Id] = entry;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ChainResult<IReadOnlyList<ScoredEntry>>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var results = _store.Values
            .Select(e => new ScoredEntry(e, CosineSimilarity(queryVector, e.Vector)))
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult(ChainResult<IReadOnlyList<ScoredEntry>>.Success(results));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0.0;
        var magA = 0.0;
        var magB = 0.0;

        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        magA = Math.Sqrt(magA);
        magB = Math.Sqrt(magB);

        return (magA == 0 || magB == 0) ? 0f : (float)(dot / (magA * magB));
    }
}
