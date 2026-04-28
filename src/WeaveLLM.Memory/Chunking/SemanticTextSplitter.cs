#nullable enable
using WeaveLLM.Core.Models;

namespace WeaveLLM.Memory.Chunking;

/// <summary>
/// Splits text by grouping sentences whose consecutive cosine-similarity embedding scores
/// exceed <c>threshold</c>. When similarity drops below the threshold a new chunk begins.
/// Sentences that individually exceed <c>maxChunkSize</c> are further split with
/// <see cref="FixedSizeTextSplitter"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ITextSplitter.Split"/> method blocks synchronously on async embedding calls
/// because the interface contract is synchronous. Prefer calling <see cref="SplitAsync"/>
/// directly when an async context is available.
/// </para>
/// </remarks>
public sealed class SemanticTextSplitter : ITextSplitter
{
    private static readonly string[] SentenceDelimiters = [". ", "! ", "? "];

    private readonly IEmbeddingModel _embeddingModel;
    private readonly float _threshold;
    private readonly int _maxChunkSize;

    /// <param name="embeddingModel">Model used to compute per-sentence embeddings.</param>
    /// <param name="threshold">Cosine-similarity threshold; below this a new chunk starts.</param>
    /// <param name="maxChunkSize">Single-chunk character limit before hard splitting.</param>
    public SemanticTextSplitter(
        IEmbeddingModel embeddingModel,
        float threshold = 0.85f,
        int maxChunkSize = 1500)
    {
        _embeddingModel = embeddingModel;
        _threshold = threshold;
        _maxChunkSize = maxChunkSize;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Split(string text) =>
        SplitAsync(text, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Async variant of <see cref="Split"/>. Preferred when called from async code.
    /// </summary>
    public async Task<IReadOnlyList<string>> SplitAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var sentences = SplitIntoSentences(text);
        if (sentences.Count == 0) return [];
        if (sentences.Count == 1) return HandleOversizedChunk(sentences[0]);

        var chunks = new List<string>();
        var currentParts = new List<string>();
        float[]? prevEmbedding = null;

        foreach (var sentence in sentences)
        {
            ct.ThrowIfCancellationRequested();

            var embedResult = await _embeddingModel.EmbedAsync(sentence, ct).ConfigureAwait(false);

            if (!embedResult.IsSuccess)
            {
                // Cannot compute similarity — just accumulate
                currentParts.Add(sentence);
                continue;
            }

            var embedding = embedResult.Value!;

            if (prevEmbedding is not null)
            {
                var similarity = CosineSimilarity(prevEmbedding, embedding);
                if (similarity < _threshold && currentParts.Count > 0)
                {
                    chunks.AddRange(HandleOversizedChunk(string.Join(" ", currentParts)));
                    currentParts.Clear();
                }
            }

            currentParts.Add(sentence);
            prevEmbedding = embedding;
        }

        if (currentParts.Count > 0)
            chunks.AddRange(HandleOversizedChunk(string.Join(" ", currentParts)));

        return chunks;
    }

    private IReadOnlyList<string> HandleOversizedChunk(string chunk) =>
        chunk.Length > _maxChunkSize
            ? new FixedSizeTextSplitter(_maxChunkSize, 0).Split(chunk)
            : [chunk];

    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var remaining = text;

        while (remaining.Length > 0)
        {
            var splitAt = -1;
            var delimLen = 0;

            foreach (var delim in SentenceDelimiters)
            {
                var idx = remaining.IndexOf(delim, StringComparison.Ordinal);
                if (idx >= 0 && (splitAt < 0 || idx < splitAt))
                {
                    splitAt = idx;
                    delimLen = delim.Length;
                }
            }

            if (splitAt < 0)
            {
                sentences.Add(remaining.Trim());
                break;
            }

            var sentence = remaining[..(splitAt + 1)].Trim(); // include punctuation, exclude trailing space
            if (sentence.Length > 0) sentences.Add(sentence);
            remaining = remaining[(splitAt + delimLen)..];
        }

        return sentences;
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
