#nullable enable
using System.Text.RegularExpressions;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Memory.Search;

/// <summary>
/// Wraps any <see cref="IVectorStore"/> and adds a BM25 inverted-index layer.
/// <see cref="SearchAsync"/> fuses dense vector results with sparse BM25 results using
/// Reciprocal Rank Fusion (RRF, k=60). Sparse search runs only when
/// <see cref="QueryText"/> is set before calling <see cref="SearchAsync"/>.
/// </summary>
/// <remarks>
/// BM25 parameters: k₁ = 1.5, b = 0.75.
/// RRF formula: score = Σ 1 / (60 + rank).
/// Text for BM25 indexing comes from <c>VectorEntry.Metadata["text"]</c> if present;
/// otherwise falls back to <c>VectorEntry.Id</c>.
/// </remarks>
public sealed class HybridVectorStore : IVectorStore
{
    private const double K1 = 1.5;
    private const double B = 0.75;
    private const int RrfK = 60;

    private readonly IVectorStore _innerStore;

    // term → list of (docId, termFrequency)
    private readonly Dictionary<string, List<(string DocId, int TermFreq)>> _invertedIndex = new();
    private readonly Dictionary<string, int> _docLengths = new();
    private int _totalDocCount;
    private double _avgDocLength;

    /// <summary>
    /// Optional plain-text query used for BM25 sparse retrieval during the next
    /// <see cref="SearchAsync"/> call. When <c>null</c>, sparse search is skipped
    /// and only dense results are returned.
    /// </summary>
    public string? QueryText { get; set; }

    /// <param name="innerStore">Underlying dense vector store (e.g., <see cref="InMemory.InMemoryVectorStore"/>).</param>
    public HybridVectorStore(IVectorStore innerStore)
    {
        _innerStore = innerStore;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(VectorEntry entry, CancellationToken cancellationToken = default)
    {
        await _innerStore.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
        UpdateBm25Index(entry);
    }

    /// <inheritdoc />
    public async Task<ChainResult<IReadOnlyList<ScoredEntry>>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        // 1. Dense retrieval — fetch topK * 2 candidates for fusion headroom
        var denseResult = await _innerStore
            .SearchAsync(queryVector, topK * 2, cancellationToken)
            .ConfigureAwait(false);

        if (!denseResult.IsSuccess)
            return denseResult;

        var denseList = denseResult.Value!.ToList();

        // 2. Sparse BM25 retrieval (only when QueryText is supplied)
        List<(string DocId, double Score)>? bm25Ranked = null;
        if (!string.IsNullOrWhiteSpace(QueryText))
            bm25Ranked = ComputeBm25Rankings(QueryText);

        // 3. Reciprocal Rank Fusion
        var rrfScores = new Dictionary<string, double>();

        for (var i = 0; i < denseList.Count; i++)
        {
            var id = denseList[i].Entry.Id;
            rrfScores.TryGetValue(id, out var existing);
            rrfScores[id] = existing + 1.0 / (RrfK + i + 1);
        }

        if (bm25Ranked is not null)
        {
            for (var i = 0; i < bm25Ranked.Count; i++)
            {
                var id = bm25Ranked[i].DocId;
                rrfScores.TryGetValue(id, out var existing);
                rrfScores[id] = existing + 1.0 / (RrfK + i + 1);
            }
        }

        // 4. Map back to ScoredEntry using dense results as source of truth for entry data
        var entryMap = denseList.ToDictionary(e => e.Entry.Id, e => e.Entry);

        var fused = rrfScores
            .Where(kv => entryMap.ContainsKey(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => new ScoredEntry(entryMap[kv.Key], (float)kv.Value))
            .ToList();

        return ChainResult<IReadOnlyList<ScoredEntry>>.Success(fused);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _innerStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        RemoveFromBm25Index(id);
    }

    // ─── BM25 helpers ────────────────────────────────────────────────────────────

    private void UpdateBm25Index(VectorEntry entry)
    {
        // Remove stale data for this id (handles upsert / update scenario)
        RemoveFromBm25Index(entry.Id);

        var text = entry.Metadata?.GetValueOrDefault("text") ?? entry.Id;
        var tokens = Tokenize(text);
        if (tokens.Count == 0) return;

        _docLengths[entry.Id] = tokens.Count;
        _totalDocCount++;
        _avgDocLength = _totalDocCount > 0
            ? (double)_docLengths.Values.Sum() / _totalDocCount
            : 0;

        var termFreqs = tokens
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (term, freq) in termFreqs)
        {
            if (!_invertedIndex.TryGetValue(term, out var postings))
            {
                postings = [];
                _invertedIndex[term] = postings;
            }
            postings.Add((entry.Id, freq));
        }
    }

    private void RemoveFromBm25Index(string docId)
    {
        if (!_docLengths.Remove(docId)) return;

        _totalDocCount = Math.Max(0, _totalDocCount - 1);
        _avgDocLength = _totalDocCount > 0
            ? (double)_docLengths.Values.Sum() / _totalDocCount
            : 0;

        foreach (var postings in _invertedIndex.Values)
            postings.RemoveAll(p => p.DocId == docId);

        // Prune empty posting lists
        var emptyTerms = _invertedIndex
            .Where(kv => kv.Value.Count == 0)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var term in emptyTerms)
            _invertedIndex.Remove(term);
    }

    private List<(string DocId, double Score)> ComputeBm25Rankings(string query)
    {
        var queryTerms = Tokenize(query);
        var scores = new Dictionary<string, double>();

        foreach (var term in queryTerms.Distinct())
        {
            if (!_invertedIndex.TryGetValue(term, out var postings)) continue;

            var df = postings.Count;
            var idf = Math.Log((_totalDocCount - df + 0.5) / (df + 0.5) + 1.0);

            foreach (var (docId, tf) in postings)
            {
                var dl = _docLengths.GetValueOrDefault(docId, 1);
                var norm = tf * (K1 + 1.0)
                    / (tf + K1 * (1.0 - B + B * dl / Math.Max(1, _avgDocLength)));

                scores.TryGetValue(docId, out var existing);
                scores[docId] = existing + idf * norm;
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static readonly Regex TokenPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    private static List<string> Tokenize(string text) =>
        TokenPattern.Split(text.ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToList();
}
