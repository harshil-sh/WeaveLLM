#nullable enable
using WeaveLLM.Core.RAG;

namespace WeaveLLM.Memory.Loaders;

/// <summary>
/// Contract for streaming document loaders. Each loader handles one or more source types
/// (file extensions, directory paths, URLs, etc.) and emits <see cref="Document"/> instances
/// as an async sequence so large sources do not need to be fully buffered.
/// </summary>
public interface IDocumentLoader
{
    /// <summary>
    /// Streams documents from <paramref name="source"/>.
    /// Implementations should yield one <see cref="Document"/> per logical unit (e.g., one per file).
    /// </summary>
    /// <param name="source">A file path, directory path, URL, or other origin identifier.</param>
    /// <param name="ct">Cancellation support.</param>
    IAsyncEnumerable<Document> LoadAsync(string source, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if this loader can handle <paramref name="source"/>.
    /// Checked before <see cref="LoadAsync"/> is called.
    /// </summary>
    /// <param name="source">The source identifier to probe.</param>
    bool CanLoad(string source);
}
