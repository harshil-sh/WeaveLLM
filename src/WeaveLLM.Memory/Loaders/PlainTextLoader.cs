#nullable enable
using System.Runtime.CompilerServices;
using WeaveLLM.Core.RAG;

namespace WeaveLLM.Memory.Loaders;

/// <summary>
/// Loads plain-text (<c>.txt</c>) files. Yields one <see cref="Document"/> per file,
/// using the file name (without extension) as the document title.
/// </summary>
public sealed class PlainTextLoader : IDocumentLoader
{
    /// <inheritdoc />
    public bool CanLoad(string source) =>
        Path.GetExtension(source).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        && File.Exists(source);

    /// <inheritdoc />
    public async IAsyncEnumerable<Document> LoadAsync(
        string source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(source, ct).ConfigureAwait(false);

        yield return new Document
        {
            Content = content,
            Source = source,
            Title = Path.GetFileNameWithoutExtension(source)
        };
    }
}
