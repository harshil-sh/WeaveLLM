#nullable enable
namespace WeaveLLM.Memory.Chunking;

/// <summary>
/// Splits text by sliding a fixed-size window over the character sequence.
/// Every chunk is exactly <c>chunkSize</c> characters or shorter (for the final chunk).
/// The last <c>chunkOverlap</c> characters of each chunk are prepended to the next chunk,
/// ensuring context continuity across boundaries.
/// </summary>
public sealed class FixedSizeTextSplitter : ITextSplitter
{
    private readonly int _chunkSize;
    private readonly int _chunkOverlap;

    /// <param name="chunkSize">Maximum number of characters per chunk.</param>
    /// <param name="chunkOverlap">Number of trailing characters from each chunk that begin the next.</param>
    public FixedSizeTextSplitter(int chunkSize = 500, int chunkOverlap = 50)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (chunkOverlap < 0 || chunkOverlap >= chunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkOverlap));

        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var chunks = new List<string>();
        var step = _chunkSize - _chunkOverlap;
        var pos = 0;

        while (pos < text.Length)
        {
            var end = Math.Min(pos + _chunkSize, text.Length);
            chunks.Add(text[pos..end]);
            pos += step;
        }

        return chunks;
    }
}
