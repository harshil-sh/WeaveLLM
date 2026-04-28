#nullable enable
using System.Runtime.CompilerServices;
using WeaveLLM.Core.RAG;

namespace WeaveLLM.Memory.Loaders;

/// <summary>
/// Iterates all files in a directory and delegates to the first matching
/// <see cref="IDocumentLoader"/> from the provided list. Unmatched files are silently skipped.
/// Does not recurse into subdirectories.
/// </summary>
public sealed class DirectoryLoader : IDocumentLoader
{
    private readonly IReadOnlyList<IDocumentLoader> _loaders;

    /// <param name="loaders">
    /// Ordered list of loaders. The first loader for which <see cref="IDocumentLoader.CanLoad"/>
    /// returns <c>true</c> handles each file.
    /// </param>
    public DirectoryLoader(IReadOnlyList<IDocumentLoader> loaders)
    {
        _loaders = loaders;
    }

    /// <inheritdoc />
    public bool CanLoad(string source) => Directory.Exists(source);

    /// <inheritdoc />
    public async IAsyncEnumerable<Document> LoadAsync(
        string source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var filePath in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();

            var loader = _loaders.FirstOrDefault(l => l.CanLoad(filePath));
            if (loader is null) continue;

            await foreach (var doc in loader.LoadAsync(filePath, ct).ConfigureAwait(false))
                yield return doc;
        }
    }
}
