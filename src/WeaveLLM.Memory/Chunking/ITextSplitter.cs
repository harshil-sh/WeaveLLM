#nullable enable
namespace WeaveLLM.Memory.Chunking;

/// <summary>
/// Splits a plain-text string into a list of overlapping or non-overlapping chunks
/// suitable for embedding and retrieval.
/// </summary>
public interface ITextSplitter
{
    /// <summary>
    /// Splits <paramref name="text"/> into chunks according to the splitter's configuration.
    /// Returns an empty list when <paramref name="text"/> is null or empty.
    /// </summary>
    /// <param name="text">The input text to split.</param>
    IReadOnlyList<string> Split(string text);
}
