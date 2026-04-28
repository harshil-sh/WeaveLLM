#nullable enable
using WeaveLLM.Core.Models;
using WeaveLLM.Core.RAG;

namespace WeaveLLM.Memory.Loaders;

/// <summary>
/// Central registry of <see cref="IDocumentLoader"/> implementations.
/// Automatically selects the first registered loader whose <see cref="IDocumentLoader.CanLoad"/>
/// returns <c>true</c> for the given source.
/// </summary>
public sealed class DocumentLoaderRegistry
{
    private readonly List<IDocumentLoader> _loaders = [];

    /// <summary>Adds <paramref name="loader"/> to the end of the candidate list.</summary>
    public void Register(IDocumentLoader loader) => _loaders.Add(loader);

    /// <summary>
    /// Loads all documents from <paramref name="source"/> using the first matching loader.
    /// Returns a failure result with code <c>"NoLoaderFound"</c> if no registered loader
    /// reports <see cref="IDocumentLoader.CanLoad"/> as <c>true</c>.
    /// </summary>
    /// <param name="source">File path, directory path, or other origin identifier.</param>
    /// <param name="ct">Cancellation support.</param>
    public async Task<ChainResult<IReadOnlyList<Document>>> LoadAsync(
        string source,
        CancellationToken ct = default)
    {
        var loader = _loaders.FirstOrDefault(l => l.CanLoad(source));
        if (loader is null)
            return ChainResult<IReadOnlyList<Document>>.Failure(
                $"No loader found for source: '{source}'.",
                "NoLoaderFound");

        var docs = new List<Document>();
        await foreach (var doc in loader.LoadAsync(source, ct).ConfigureAwait(false))
            docs.Add(doc);

        return ChainResult<IReadOnlyList<Document>>.Success(docs);
    }
}
