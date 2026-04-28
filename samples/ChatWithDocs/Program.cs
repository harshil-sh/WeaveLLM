using Microsoft.Extensions.Configuration;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;
using WeaveLLM.Memory.Chunking;
using WeaveLLM.Memory.InMemory;
using WeaveLLM.Memory.Loaders;
using WeaveLLM.Memory.Rag;
using WeaveLLM.Providers.OpenAI;

// ── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var apiKey = config["WeaveLLM:OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("Set WeaveLLM:OpenAI:ApiKey in appsettings.json.");

// ── Graceful shutdown ─────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Wire up pipeline ──────────────────────────────────────────────────────────
var openai = new OpenAIChatModel(apiKey);
var pipeline = new DefaultRagPipeline(
    vectorStore:    new InMemoryVectorStore(),
    embeddingModel: new EmbeddingAdapter(openai),
    chatModel:      new ChatModelAdapter(openai),
    textSplitter:   new RecursiveTextSplitter());

// ── Load and index docs/ ──────────────────────────────────────────────────────
Console.WriteLine("Indexing docs/...");
var loader = new DirectoryLoader([new PlainTextLoader(), new MarkdownLoader()]);
var rawDocs = new List<WeaveLLM.Core.RAG.Document>();
await foreach (var doc in loader.LoadAsync("docs", cts.Token))
    rawDocs.Add(doc);

var ragDocs = rawDocs
    .Select(d => new Document(d.Content, d.Source))
    .ToList();

var indexResult = await pipeline.IndexAsync(ragDocs, cts.Token);
if (indexResult.IsFailure)
{
    Console.Error.WriteLine($"Indexing failed: {indexResult.Error!.Message}");
    return;
}

Console.WriteLine($"Indexed {indexResult.Value} chunks from {rawDocs.Count} file(s). Ctrl+C to quit.\n");

// ── REPL ──────────────────────────────────────────────────────────────────────
while (!cts.Token.IsCancellationRequested)
{
    Console.Write("Question: ");
    var question = Console.ReadLine();

    if (cts.Token.IsCancellationRequested) break;
    if (string.IsNullOrWhiteSpace(question)) continue;

    var result = await pipeline.QueryAsync(question, ct: cts.Token);
    if (result.IsFailure)
    {
        Console.Error.WriteLine($"Error: {result.Error!.Message}");
        continue;
    }

    Console.WriteLine($"\nAnswer: {result.Value!.Answer}");
    if (result.Value.Sources.Count > 0)
        Console.WriteLine($"Sources: {string.Join(", ", result.Value.Sources)}");
    Console.WriteLine();
}

// ── Adapters: WeaveLLM.Core.Providers → WeaveLLM.Core.Models ─────────────────
// OpenAIChatModel implements WeaveLLM.Core.Providers interfaces; DefaultRagPipeline
// expects WeaveLLM.Core.Models interfaces. These thin wrappers bridge the gap.

sealed class ChatModelAdapter(OpenAIChatModel inner) : WeaveLLM.Core.Models.IChatModel
{
    public string ModelId    => inner.ModelId;
    public string ProviderId => inner.ProviderName;

    public Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages, LLMOptions? options, CancellationToken ct)
        => inner.ChatAsync(messages, options, ct);
}

sealed class EmbeddingAdapter(OpenAIChatModel inner) : WeaveLLM.Core.Models.IEmbeddingModel
{
    public string ModelId    => inner.ModelId;
    public string ProviderId => inner.ProviderName;

    public Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken ct)
        => inner.EmbedAsync(text, ct);

    public Task<ChainResult<float[][]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
        => inner.EmbedBatchAsync(texts, ct);
}
