#nullable enable
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WeaveLLM.Core.RAG;

namespace WeaveLLM.Memory.Loaders;

/// <summary>
/// Loads Markdown (<c>.md</c>) files. Strips ATX heading markers (<c>#</c>…<c>######</c>)
/// and inline bold markers (<c>**…**</c>). Sets metadata key <c>"format"</c> to <c>"markdown"</c>
/// and <c>"title"</c> to the text of the first level-1 heading (if present).
/// </summary>
public sealed class MarkdownLoader : IDocumentLoader
{
    private static readonly Regex HeadingPattern =
        new(@"^#{1,6}\s+", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex BoldPattern =
        new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

    /// <inheritdoc />
    public bool CanLoad(string source) =>
        Path.GetExtension(source).Equals(".md", StringComparison.OrdinalIgnoreCase)
        && File.Exists(source);

    /// <inheritdoc />
    public async IAsyncEnumerable<Document> LoadAsync(
        string source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var raw = await File.ReadAllTextAsync(source, ct).ConfigureAwait(false);

        var title = ExtractFirstH1Title(raw);
        var content = StripMarkdown(raw);

        yield return new Document
        {
            Content = content,
            Source = source,
            Title = title,
            Metadata = new Dictionary<string, object>
            {
                ["format"] = "markdown",
                ["title"] = title ?? string.Empty
            }
        };
    }

    private static string? ExtractFirstH1Title(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return trimmed[2..].TrimEnd('\r', ' ');
        }
        return null;
    }

    private static string StripMarkdown(string text)
    {
        text = HeadingPattern.Replace(text, string.Empty);
        text = BoldPattern.Replace(text, "$1");
        return text;
    }
}
