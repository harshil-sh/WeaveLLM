#nullable enable
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Memory;

/// <summary>
/// Pluggable conversational memory store. Implement this for Redis, CosmosDB, Postgres, SQLite, etc.
/// Each session is identified by a string key; entries are appended in insertion order.
/// </summary>
public interface IMemoryStore
{
    /// <summary>Appends <paramref name="entry"/> to the session's history.</summary>
    /// <param name="entry">The memory entry to persist.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task AddAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent <paramref name="limit"/> entries for <paramref name="sessionId"/>,
    /// ordered oldest-first, as an async stream.
    /// </summary>
    /// <param name="sessionId">The session to query.</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    IAsyncEnumerable<MemoryEntry> GetAsync(
        string sessionId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Removes all entries for <paramref name="sessionId"/>. No-ops if the session does not exist.</summary>
    /// <param name="sessionId">The session to clear.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task ClearAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single persisted message in a conversational memory session.
/// </summary>
/// <param name="SessionId">The session this entry belongs to.</param>
/// <param name="Message">The chat message to store.</param>
/// <param name="Timestamp">The UTC time this entry was recorded.</param>
public sealed record MemoryEntry(string SessionId, Message Message, DateTimeOffset Timestamp);

/// <summary>
/// Volatile in-memory store backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Thread-safe. Suitable for development, testing, and single-instance deployments.
/// Data is lost on process restart.
/// </summary>
public sealed class InMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<string, List<MemoryEntry>> _sessions = new();

    /// <inheritdoc />
    public Task AddAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        _sessions.GetOrAdd(entry.SessionId, _ => []).Add(entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<MemoryEntry> GetAsync(
        string sessionId,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var entries))
            yield break;

        foreach (var entry in entries.TakeLast(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
