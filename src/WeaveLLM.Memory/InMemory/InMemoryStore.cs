#nullable enable
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Memory.InMemory;

/// <summary>
/// Volatile in-memory implementation of both <see cref="IMemoryStore"/> and <see cref="IVectorStore"/>.
/// Suitable for development, testing, and single-instance deployments where durability is not required.
/// All data is lost on process restart.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IMemoryStore"/> operations are backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of session lists.
/// List mutations are protected by a per-list <c>lock</c> to guarantee ordering under concurrent access.
/// </para>
/// <para>
/// <see cref="IVectorStore"/> operations are backed by a separate
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of <see cref="VectorEntry"/> values.
/// Search performs exact cosine-similarity over all stored entries.
/// </para>
/// </remarks>
public sealed class InMemoryStore : IMemoryStore, IVectorStore
{
    // ─── IMemoryStore backing store ───────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, List<MemoryEntry>> _sessions = new();

    // ─── IVectorStore backing store ───────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, VectorEntry> _vectors = new();

    // ═══════════════════════════════════════════════════════════════════════════════
    // IMemoryStore
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Appends <paramref name="entry"/> to the end of the session's history list.
    /// The list is created on first use.
    /// </summary>
    /// <param name="entry">The memory entry to persist.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public Task AddAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        var list = _sessions.GetOrAdd(entry.SessionId, _ => []);
        lock (list)
            list.Add(entry);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the most recent <paramref name="limit"/> entries for <paramref name="sessionId"/>,
    /// ordered newest-first, as an async stream.
    /// Yields nothing if the session does not exist.
    /// </summary>
    /// <param name="sessionId">The session to query.</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public async IAsyncEnumerable<MemoryEntry> GetAsync(
        string sessionId,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var list))
            yield break;

        MemoryEntry[] snapshot;
        lock (list)
            snapshot = [.. list];

        foreach (var entry in snapshot.Reverse().Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    /// <summary>
    /// Removes all entries for <paramref name="sessionId"/>. No-ops if the session does not exist.
    /// </summary>
    /// <param name="sessionId">The session to clear.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public Task ClearAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // IVectorStore
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inserts a new entry or replaces an existing one with the same <see cref="VectorEntry.Id"/>.
    /// </summary>
    /// <param name="entry">The vector entry to upsert.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public Task UpsertAsync(VectorEntry entry, CancellationToken cancellationToken = default)
    {
        _vectors[entry.Id] = entry;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the top-<paramref name="topK"/> entries most similar to <paramref name="queryVector"/>,
    /// ordered by descending cosine-similarity score.
    /// </summary>
    /// <param name="queryVector">The query embedding to compare against all stored vectors.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public Task<ChainResult<IReadOnlyList<ScoredEntry>>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var results = _vectors.Values
            .Select(e => new ScoredEntry(e, CosineSimilarity(queryVector, e.Vector)))
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult(ChainResult<IReadOnlyList<ScoredEntry>>.Success(results));
    }

    /// <summary>
    /// Removes the entry with the specified <paramref name="id"/>.
    /// No-ops if the entry does not exist.
    /// </summary>
    /// <param name="id">The unique identifier of the entry to delete.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _vectors.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the cosine similarity between two vectors.
    /// Returns <c>0f</c> when either vector has zero magnitude to avoid division by zero.
    /// </summary>
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
