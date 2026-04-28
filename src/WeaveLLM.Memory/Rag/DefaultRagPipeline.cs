#nullable enable
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;
using WeaveLLM.Core.Prompts;
using WeaveLLM.Memory.Chunking;

namespace WeaveLLM.Memory.Rag;

// ─── Value types ─────────────────────────────────────────────────────────────────

/// <summary>
/// A source document submitted for indexing. Carries raw text, an origin identifier,
/// and optional key/value metadata forwarded to each <see cref="VectorEntry"/>.
/// </summary>
/// <param name="Content">The full text of the document to be split and embedded.</param>
/// <param name="Source">A stable origin identifier (file path, URL, document ID, etc.)
/// used to construct chunk IDs and populate the <c>"source"</c> metadata key.</param>
/// <param name="Metadata">Optional string metadata merged into every chunk's vector entry.</param>
public sealed record Document(
    string Content,
    string Source,
    Dictionary<string, string>? Metadata = null);

/// <summary>
/// The result of a successful <see cref="DefaultRagPipeline.QueryAsync"/> call.
/// </summary>
/// <param name="Answer">The model's generated answer.</param>
/// <param name="SourceChunks">The raw chunk texts retrieved from the vector store and used as context.</param>
/// <param name="Sources">Deduplicated list of origin identifiers from the retrieved chunks.</param>
public sealed record RagAnswer(
    string Answer,
    IReadOnlyList<string> SourceChunks,
    IReadOnlyList<string> Sources);

// ─── Pipeline ────────────────────────────────────────────────────────────────────

/// <summary>
/// Default Retrieve-and-Generate pipeline.
/// </summary>
/// <remarks>
/// <para><b>Indexing</b> (Load → Split → Embed → Store):</para>
/// <para>Each <see cref="Document"/> is split into chunks using the supplied
/// <see cref="ITextSplitter"/>. Every chunk is embedded and stored as a
/// <see cref="VectorEntry"/> whose <c>Id</c> follows the pattern
/// <c>"{source}::chunk{index}"</c>.</para>
/// <para><b>Querying</b> (Embed → Search → Render → Generate):</para>
/// <para>The question is embedded, the top-K nearest chunks are retrieved, a context
/// string is assembled, the configured <see cref="IPromptTemplate"/> is rendered,
/// and the chat model produces the final answer.</para>
/// </remarks>
public sealed class DefaultRagPipeline
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingModel _embeddingModel;
    private readonly IChatModel _chatModel;
    private readonly ITextSplitter _textSplitter;
    private readonly IPromptTemplate _queryPrompt;

    /// <summary>
    /// Creates a new <see cref="DefaultRagPipeline"/>.
    /// </summary>
    /// <param name="vectorStore">Store used for upsert during indexing and nearest-neighbour search during querying.</param>
    /// <param name="embeddingModel">Model used to embed both document chunks and the query.</param>
    /// <param name="chatModel">Model used to generate the final answer from retrieved context.</param>
    /// <param name="textSplitter">Splitter that converts document text into indexable chunks.</param>
    /// <param name="queryPrompt">
    /// Optional prompt template rendered before the chat call.
    /// Must accept variables <c>{{context}}</c> and <c>{{question}}</c>.
    /// Defaults to <see cref="PromptTemplateLibrary.RagQueryPrompt"/> when <c>null</c>.
    /// </param>
    public DefaultRagPipeline(
        IVectorStore vectorStore,
        IEmbeddingModel embeddingModel,
        IChatModel chatModel,
        ITextSplitter textSplitter,
        IPromptTemplate? queryPrompt = null)
    {
        _vectorStore = vectorStore;
        _embeddingModel = embeddingModel;
        _chatModel = chatModel;
        _textSplitter = textSplitter;
        _queryPrompt = queryPrompt ?? PromptTemplateLibrary.RagQueryPrompt;
    }

    /// <summary>
    /// Splits, embeds, and indexes all provided documents into the vector store.
    /// </summary>
    /// <param name="documents">Documents to index. Each document is split independently.</param>
    /// <param name="ct">Cancellation support.</param>
    /// <returns>
    /// A <see cref="ChainResult{T}"/> containing the total number of chunks indexed across
    /// all documents on success, or a failure result if any embedding call fails.
    /// </returns>
    public async Task<ChainResult<int>> IndexAsync(
        IReadOnlyList<Document> documents,
        CancellationToken ct = default)
    {
        var totalChunks = 0;

        foreach (var doc in documents)
        {
            var chunks = _textSplitter.Split(doc.Content);

            for (var i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = chunks[i];
                var embedResult = await _embeddingModel.EmbedAsync(chunk, ct).ConfigureAwait(false);

                if (!embedResult.IsSuccess)
                    return ChainResult<int>.Failure(embedResult.Error!);

                var metadata = new Dictionary<string, string>
                {
                    ["source"] = doc.Source,
                    ["chunkIndex"] = i.ToString(),
                    ["text"] = chunk
                };

                if (doc.Metadata is not null)
                    foreach (var (key, value) in doc.Metadata)
                        metadata.TryAdd(key, value);

                var entry = new VectorEntry(
                    Id: $"{doc.Source}::chunk{i}",
                    Vector: embedResult.Value!,
                    Content: chunk,
                    Metadata: metadata);

                await _vectorStore.UpsertAsync(entry, ct).ConfigureAwait(false);
                totalChunks++;
            }
        }

        return ChainResult<int>.Success(totalChunks);
    }

    /// <summary>
    /// Answers a natural-language question by retrieving relevant chunks and generating a response.
    /// </summary>
    /// <param name="question">The question to answer.</param>
    /// <param name="topK">Maximum number of chunks to retrieve from the vector store.</param>
    /// <param name="ct">Cancellation support.</param>
    /// <returns>
    /// A <see cref="ChainResult{T}"/> containing a <see cref="RagAnswer"/> on success,
    /// or a failure result propagated from the embedding, search, or chat model call.
    /// </returns>
    public async Task<ChainResult<RagAnswer>> QueryAsync(
        string question,
        int topK = 5,
        CancellationToken ct = default)
    {
        // 1. Embed the question
        var embedResult = await _embeddingModel.EmbedAsync(question, ct).ConfigureAwait(false);
        if (!embedResult.IsSuccess)
            return ChainResult<RagAnswer>.Failure(embedResult.Error!);

        // 2. Retrieve top-K nearest chunks
        var searchResult = await _vectorStore
            .SearchAsync(embedResult.Value!, topK, ct)
            .ConfigureAwait(false);
        if (!searchResult.IsSuccess)
            return ChainResult<RagAnswer>.Failure(searchResult.Error!);

        var hits = searchResult.Value!;

        // 3. Build context string
        var context = string.Join(
            "\n\n---\n\n",
            hits.Select(h => h.Entry.Content));

        // 4. Render the RAG prompt
        var rendered = _queryPrompt.Render(new Dictionary<string, object>
        {
            ["context"] = context,
            ["question"] = question
        });

        // 5. Generate the answer
        var chatResult = await _chatModel
            .ChatAsync([Message.User(rendered)], null, ct)
            .ConfigureAwait(false);
        if (!chatResult.IsSuccess)
            return ChainResult<RagAnswer>.Failure(chatResult.Error!);

        var sourceChunks = hits.Select(h => h.Entry.Content).ToList();
        var sources = hits
            .Select(h => h.Entry.Metadata?.GetValueOrDefault("source") ?? string.Empty)
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        return ChainResult<RagAnswer>.Success(
            new RagAnswer(chatResult.Value!.Content, sourceChunks, sources));
    }
}
