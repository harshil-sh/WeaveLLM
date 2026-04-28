#nullable enable
using System.Text;

namespace WeaveLLM.Memory.Chunking;

/// <summary>
/// Tries a cascade of separators — double newline, single newline, period+space, space —
/// to split text at natural boundaries. Falls back to <see cref="FixedSizeTextSplitter"/>
/// when no separator produces multiple parts within the chunk size.
/// The last <c>chunkOverlap</c> characters of each produced chunk are prepended to the
/// next chunk to preserve context continuity.
/// </summary>
public sealed class RecursiveTextSplitter : ITextSplitter
{
    private static readonly string[] Separators = ["\n\n", "\n", ". ", " "];

    private readonly int _chunkSize;
    private readonly int _chunkOverlap;

    /// <param name="chunkSize">Maximum number of characters per chunk.</param>
    /// <param name="chunkOverlap">Number of trailing characters carried into the next chunk.</param>
    public RecursiveTextSplitter(int chunkSize = 1000, int chunkOverlap = 200)
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
        if (text.Length <= _chunkSize) return [text];

        return SplitInternal(text);
    }

    private List<string> SplitInternal(string text)
    {
        if (text.Length <= _chunkSize) return [text];

        foreach (var sep in Separators)
        {
            var parts = text.Split(sep);
            if (parts.Length <= 1) continue;

            var result = new List<string>();
            var current = new StringBuilder();

            foreach (var part in parts)
            {
                var needed = current.Length == 0
                    ? part.Length
                    : current.Length + sep.Length + part.Length;

                if (needed > _chunkSize && current.Length > 0)
                {
                    result.Add(current.ToString());

                    // Seed next chunk with overlap tail
                    var tail = current.ToString();
                    var overlapStart = Math.Max(0, tail.Length - _chunkOverlap);
                    current.Clear();
                    current.Append(tail[overlapStart..]);
                }

                if (current.Length > 0)
                    current.Append(sep);

                current.Append(part);
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }

        // Hard character fallback
        return new FixedSizeTextSplitter(_chunkSize, _chunkOverlap).Split(text).ToList();
    }
}
