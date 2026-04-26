#nullable enable
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.RAG;

/// <summary>
/// Full RAG pipeline: Load → Split → Embed → Store → Retrieve → Generate.
/// </summary>
public interface IRagPipeline
{
    /// <summary>Embeds <paramref name="documents"/>, splits them into chunks, and indexes them in the vector store.</summary>
    Task<RagIndexResult> IndexAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken = default);

    /// <summary>Embeds the <paramref name="question"/>, retrieves relevant chunks, and generates an answer.</summary>
    Task<ChainResult<string>> QueryAsync(string question, RagQueryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Same as <see cref="QueryAsync"/> but streams the answer token-by-token.</summary>
    IAsyncEnumerable<string> StreamQueryAsync(string question, RagQueryOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Tuning options for a single RAG query.</summary>
public sealed class RagQueryOptions
{
    /// <summary>Maximum number of chunks to retrieve from the vector store.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Minimum similarity score for a chunk to be included in context. Applied client-side.</summary>
    public float MinScore { get; set; } = 0.7f;

    /// <summary>Optional system prompt override. Defaults to a concise RAG-focused instruction.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Per-call model options forwarded to the chat model.</summary>
    public LLMOptions? LLMOptions { get; set; }
}

/// <summary>Summary of a completed indexing operation.</summary>
public sealed class RagIndexResult
{
    /// <summary>Number of documents successfully embedded and stored.</summary>
    public int DocumentsIndexed { get; init; }

    /// <summary>Total number of chunks created across all documents.</summary>
    public int ChunksCreated { get; init; }

    /// <summary>Wall-clock time for the full indexing run.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Errors encountered per document (document ID → message).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Document loader contract. Implement for PDF, DOCX, HTML, CSV, web pages, etc.</summary>
public interface IDocumentLoader
{
    /// <summary>File extensions this loader handles (e.g., <c>[".pdf", ".txt"]</c>).</summary>
    string[] SupportedExtensions { get; }

    /// <summary>Loads documents from <paramref name="source"/> (a file path, URL, or connection string).</summary>
    Task<IReadOnlyList<Document>> LoadAsync(string source, CancellationToken cancellationToken = default);
}

/// <summary>Text splitter contract. Splits documents into overlapping chunks before embedding.</summary>
public interface ITextSplitter
{
    /// <summary>Splits <paramref name="document"/> into chunks according to <paramref name="options"/>.</summary>
    IReadOnlyList<DocumentChunk> Split(Document document, TextSplitterOptions? options = null);
}

/// <summary>A raw source document before chunking.</summary>
public sealed class Document
{
    /// <summary>Stable unique identifier for this document.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>The full text content of the document.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>The file path, URL, or other origin identifier.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Optional human-readable title.</summary>
    public string? Title { get; init; }

    /// <summary>Arbitrary key/value metadata to carry through to chunks.</summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>A single text chunk produced by splitting a <see cref="Document"/>.</summary>
public sealed class DocumentChunk
{
    /// <summary>Stable unique identifier for this chunk (used as the vector store entry ID).</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>The ID of the originating <see cref="Document"/>.</summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>The chunk text content.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Zero-based index within the document.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Character offset where this chunk starts in the original document.</summary>
    public int StartChar { get; init; }

    /// <summary>Character offset where this chunk ends in the original document.</summary>
    public int EndChar { get; init; }

    /// <summary>Inherited and augmented metadata from the source document.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>Options for splitting a document into chunks.</summary>
public sealed class TextSplitterOptions
{
    /// <summary>Target size for each chunk in characters.</summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>Number of characters to overlap between adjacent chunks.</summary>
    public int ChunkOverlap { get; set; } = 200;

    /// <summary>Ordered list of separator strings tried from largest to smallest unit.</summary>
    public string[] Separators { get; set; } = ["\n\n", "\n", ". ", " "];
}

/// <summary>
/// Recursive character text splitter — mirrors LangChain's most commonly used splitter.
/// Tries each separator in order, falling back to hard character splits when necessary.
/// </summary>
public sealed class RecursiveTextSplitter : ITextSplitter
{
    /// <inheritdoc />
    public IReadOnlyList<DocumentChunk> Split(Document document, TextSplitterOptions? options = null)
    {
        options ??= new TextSplitterOptions();
        var chunks = new List<DocumentChunk>();
        var texts = SplitText(document.Content, options);
        var charOffset = 0;

        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            var start = document.Content.IndexOf(text, charOffset, StringComparison.Ordinal);
            if (start < 0) start = charOffset;

            var metadata = new Dictionary<string, string>(
                document.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty))
            {
                ["source"] = document.Source,
                ["chunk_index"] = i.ToString(),
                ["document_id"] = document.Id
            };

            chunks.Add(new DocumentChunk
            {
                DocumentId = document.Id,
                Content = text,
                ChunkIndex = i,
                StartChar = start,
                EndChar = start + text.Length,
                Metadata = metadata
            });
            charOffset = Math.Max(0, start + text.Length - options.ChunkOverlap);
        }

        return chunks;
    }

    private static List<string> SplitText(string text, TextSplitterOptions options)
    {
        var chunks = new List<string>();
        if (text.Length <= options.ChunkSize)
        {
            chunks.Add(text);
            return chunks;
        }

        foreach (var sep in options.Separators)
        {
            var parts = text.Split(sep);
            if (parts.Length <= 1) continue;

            var current = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (current.Length + part.Length + sep.Length > options.ChunkSize && current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    var overlap = current.ToString()[Math.Max(0, current.Length - options.ChunkOverlap)..];
                    current.Clear();
                    current.Append(overlap);
                }
                current.Append(part);
                current.Append(sep);
            }
            if (current.Length > 0) chunks.Add(current.ToString().Trim());
            return chunks;
        }

        for (var i = 0; i < text.Length; i += options.ChunkSize - options.ChunkOverlap)
            chunks.Add(text[i..Math.Min(i + options.ChunkSize, text.Length)]);

        return chunks;
    }
}

/// <summary>
/// Default RAG pipeline implementation. Requires an <see cref="IEmbeddingModel"/>,
/// <see cref="IVectorStore"/>, and <see cref="IChatModel"/> injected at construction.
/// </summary>
public sealed class DefaultRagPipeline : IRagPipeline
{
    private readonly IEmbeddingModel _embedder;
    private readonly IVectorStore _vectorStore;
    private readonly IChatModel _chatModel;
    private readonly ITextSplitter _splitter;

    /// <param name="embedder">Model used to embed chunks and queries.</param>
    /// <param name="vectorStore">Store for embedding vectors.</param>
    /// <param name="chatModel">Model used to generate the final answer.</param>
    /// <param name="splitter">Text splitter; defaults to <see cref="RecursiveTextSplitter"/>.</param>
    public DefaultRagPipeline(
        IEmbeddingModel embedder,
        IVectorStore vectorStore,
        IChatModel chatModel,
        ITextSplitter? splitter = null)
    {
        _embedder = embedder;
        _vectorStore = vectorStore;
        _chatModel = chatModel;
        _splitter = splitter ?? new RecursiveTextSplitter();
    }

    /// <inheritdoc />
    public async Task<RagIndexResult> IndexAsync(
        IReadOnlyList<Document> documents,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var errors = new List<string>();
        var totalChunks = 0;

        foreach (var document in documents)
        {
            var chunks = _splitter.Split(document);
            totalChunks += chunks.Count;

            var texts = chunks.Select(c => c.Content).ToList();
            var embedResult = await _embedder.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);

            if (!embedResult.IsSuccess)
            {
                errors.Add($"Failed to embed '{document.Id}': {embedResult.Error?.Message}");
                continue;
            }

            foreach (var (chunk, embedding) in chunks.Zip(embedResult.Value!))
            {
                var entry = new VectorEntry(chunk.Id, embedding, chunk.Content, chunk.Metadata);
                await _vectorStore.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }

        return new RagIndexResult
        {
            DocumentsIndexed = documents.Count - errors.Count,
            ChunksCreated = totalChunks,
            Duration = DateTimeOffset.UtcNow - started,
            Errors = errors
        };
    }

    /// <inheritdoc />
    public async Task<ChainResult<string>> QueryAsync(
        string question,
        RagQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RagQueryOptions();

        var embedResult = await _embedder.EmbedAsync(question, cancellationToken).ConfigureAwait(false);
        if (!embedResult.IsSuccess)
            return ChainResult<string>.Failure(embedResult.Error!);

        var searchResult = await _vectorStore.SearchAsync(embedResult.Value!, options.TopK, cancellationToken)
            .ConfigureAwait(false);
        if (!searchResult.IsSuccess)
            return ChainResult<string>.Failure(searchResult.Error!);

        var matches = searchResult.Value!
            .Where(m => m.Score >= options.MinScore)
            .ToList();

        var context = string.Join("\n\n---\n\n", matches.Select((m, i) =>
        {
            var source = m.Entry.Metadata?.GetValueOrDefault("source") ?? "unknown";
            return $"[Source {i + 1}: {source}]\n{m.Entry.Content}";
        }));

        var messages = new List<Message>
        {
            Message.System(options.SystemPrompt ??
                "You are a helpful assistant. Answer using only the provided context. " +
                "If the context doesn't contain the answer, say so clearly."),
            Message.User($"Context:\n{context}\n\nQuestion: {question}")
        };

        var chatResult = await _chatModel.ChatAsync(messages, options.LLMOptions, cancellationToken)
            .ConfigureAwait(false);

        return chatResult.Map(r => r.Content);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamQueryAsync(
        string question,
        RagQueryOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await QueryAsync(question, options, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess) yield return result.Value!;
    }
}
