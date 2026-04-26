using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;
using WeaveLLM.Core.Providers;

namespace WeaveLLM.Core.RAG;

/// <summary>
/// Full RAG pipeline: Load → Split → Embed → Store → Retrieve → Generate
/// </summary>
public interface IRagPipeline
{
    Task<RagIndexResult> IndexAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken = default);
    Task<ChainResult<string>> QueryAsync(string question, RagQueryOptions? options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamQueryAsync(string question, RagQueryOptions? options = null, CancellationToken cancellationToken = default);
}

public sealed class RagQueryOptions
{
    public int TopK { get; set; } = 5;
    public float MinScore { get; set; } = 0.7f;
    public bool UseHybridSearch { get; set; } = true;
    public string? SystemPrompt { get; set; }
    public LLMOptions? LLMOptions { get; set; }
}

public sealed class RagIndexResult
{
    public int DocumentsIndexed { get; init; }
    public int ChunksCreated { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Document loader interface. Implement for PDF, DOCX, HTML, CSV, web pages, etc.
/// </summary>
public interface IDocumentLoader
{
    string[] SupportedExtensions { get; }
    Task<IReadOnlyList<Document>> LoadAsync(string source, CancellationToken cancellationToken = default);
}

/// <summary>
/// Text splitter interface for chunking documents before embedding.
/// </summary>
public interface ITextSplitter
{
    IReadOnlyList<DocumentChunk> Split(Document document, TextSplitterOptions? options = null);
}

/// <summary>
/// A source document before chunking.
/// </summary>
public sealed class Document
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Content { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? Title { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// A chunk of a document after splitting.
/// </summary>
public sealed class DocumentChunk
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string DocumentId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int ChunkIndex { get; init; }
    public int StartChar { get; init; }
    public int EndChar { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed class TextSplitterOptions
{
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 200;
    public string[] Separators { get; set; } = ["\n\n", "\n", ". ", " "];
}

/// <summary>
/// Recursive character text splitter — mirrors LangChain's most-used splitter.
/// </summary>
public sealed class RecursiveTextSplitter : ITextSplitter
{
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

            chunks.Add(new DocumentChunk
            {
                DocumentId = document.Id,
                Content = text,
                ChunkIndex = i,
                StartChar = start,
                EndChar = start + text.Length,
                Metadata = new Dictionary<string, object>(document.Metadata)
                {
                    ["source"] = document.Source,
                    ["chunk_index"] = i,
                    ["total_chunks"] = 0
                }
            });
            charOffset = Math.Max(0, start + text.Length - options.ChunkOverlap);
        }

        foreach (var chunk in chunks)
            ((Dictionary<string, object>)chunk.Metadata)["total_chunks"] = chunks.Count;

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
            if (parts.Length > 1)
            {
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
        }

        for (var i = 0; i < text.Length; i += options.ChunkSize - options.ChunkOverlap)
            chunks.Add(text[i..Math.Min(i + options.ChunkSize, text.Length)]);

        return chunks;
    }
}

/// <summary>
/// Default RAG pipeline tying together embeddings, vector store, and LLM.
/// </summary>
public sealed class DefaultRagPipeline(
    IEmbeddingModel embedder,
    IVectorStore vectorStore,
    IChatModel chatModel,
    ITextSplitter? splitter = null) : IRagPipeline
{
    private readonly ITextSplitter _splitter = splitter ?? new RecursiveTextSplitter();

    public async Task<RagIndexResult> IndexAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var errors = new List<string>();
        var totalChunks = 0;

        foreach (var document in documents)
        {
            var chunks = _splitter.Split(document);
            totalChunks += chunks.Count;

            var texts = chunks.Select(c => c.Content).ToList();
            var embedResult = await embedder.EmbedBatchAsync(texts, cancellationToken);

            if (!embedResult.IsSuccess)
            {
                errors.Add($"Failed to embed document {document.Id}: {embedResult.Error?.Message}");
                continue;
            }

            var records = chunks.Zip(embedResult.Value!, (chunk, embedding) => new MemoryRecord
            {
                Id = chunk.Id,
                Text = chunk.Content,
                Embedding = embedding,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                Metadata = chunk.Metadata,
                Source = document.Source
            }).ToList();

            await vectorStore.UpsertBatchAsync(records, cancellationToken);
        }

        return new RagIndexResult
        {
            DocumentsIndexed = documents.Count - errors.Count,
            ChunksCreated = totalChunks,
            Duration = DateTimeOffset.UtcNow - started,
            Errors = errors
        };
    }

    public async Task<ChainResult<string>> QueryAsync(string question, RagQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new RagQueryOptions();

        var embedResult = await embedder.EmbedAsync(question, cancellationToken);
        if (!embedResult.IsSuccess)
            return ChainResult<string>.Failure(embedResult.Error!);

        var matches = options.UseHybridSearch
            ? await vectorStore.HybridSearchAsync(embedResult.Value!, question, options.TopK, cancellationToken)
            : await vectorStore.SearchAsync(embedResult.Value!, options.TopK, options.MinScore, cancellationToken);

        var context = string.Join("\n\n---\n\n", matches.Select((m, i) =>
            $"[Source {i + 1}: {m.Record.Source ?? "unknown"}]\n{m.Record.Text}"));

        var messages = new List<Message>
        {
            Message.System(options.SystemPrompt ?? "You are a helpful assistant. Answer questions using only the provided context. If the context doesn't contain the answer, say so clearly."),
            Message.User($"Context:\n{context}\n\nQuestion: {question}")
        };

        var result = await chatModel.ChatAsync(messages, options.LLMOptions, cancellationToken);
        return result.Map(m => m.Content);
    }

    public async IAsyncEnumerable<string> StreamQueryAsync(string question, RagQueryOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await QueryAsync(question, options, cancellationToken);
        if (result.IsSuccess) yield return result.Value!;
    }
}
