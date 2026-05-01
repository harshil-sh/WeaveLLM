using Microsoft.Extensions.Configuration;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;
using WeaveLLM.Memory.Chunking;
using WeaveLLM.Memory.InMemory;
using WeaveLLM.Memory.Rag;
using WeaveLLM.Providers.OpenAI;

// ── Configuration ──────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var apiKey = config["WeaveLLM:OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("Set WeaveLLM:OpenAI:ApiKey in appsettings.json.");
var modelId = config["WeaveLLM:OpenAI:ModelId"] ?? "gpt-4o";

// ── Graceful shutdown ──────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Wire up pipeline ───────────────────────────────────────────────────────────
var openai   = new OpenAIChatModel(apiKey, modelId);
var pipeline = new DefaultRagPipeline(
    vectorStore:    new InMemoryVectorStore(),
    embeddingModel: new EmbeddingAdapter(openai),
    chatModel:      new ChatModelAdapter(openai),
    textSplitter:   new RecursiveTextSplitter());

// ── Hardcoded knowledge base ───────────────────────────────────────────────────
// In a real app, load these from files, a database, or an API.
var knowledgeBase = new List<Document>
{
    new("""
        WeaveLLM is a .NET 8 framework for building LLM-powered applications.
        It follows a chain-based architecture where every unit of work implements
        IChain<TInput, TOutput>. Results are returned as ChainResult<T> — errors
        are values, never exceptions.
        """,
        Source: "docs/architecture.md"),

    new("""
        WeaveLLM supports multiple LLM providers out of the box: OpenAI (GPT-4o, GPT-4-turbo),
        Anthropic (Claude Sonnet), Ollama (local models), Azure OpenAI, and HuggingFace.
        All providers implement IChatModel and use raw HttpClient — no vendor SDK dependencies.
        The baseUrl constructor parameter makes every provider compatible with custom endpoints.
        """,
        Source: "docs/providers.md"),

    new("""
        The RAG (Retrieval-Augmented Generation) pipeline in WeaveLLM consists of two phases:
        Indexing (Load → Split → Embed → Store) and Querying (Embed → Search → Render → Generate).
        DefaultRagPipeline accepts any ITextSplitter, IVectorStore, IEmbeddingModel and IChatModel.
        Built-in splitters: RecursiveTextSplitter, FixedSizeTextSplitter, SemanticTextSplitter.
        """,
        Source: "docs/rag.md"),

    new("""
        WeaveLLM agents implement IAgent and return ChainResult<AgentResult>.
        ReActAgent uses Thought → Action → Observation loops until a Final Answer is reached.
        PlanAndExecuteAgent first generates a numbered plan, executes each step, then synthesises.
        AgentGraph supports graph-based multi-agent workflows with conditional routing.
        Tools are registered via IToolRegistry using the [LLMTool] attribute.
        """,
        Source: "docs/agents.md"),

    new("""
        Middleware in WeaveLLM wraps IChain<TInput, TOutput> execution:
        RetryMiddleware applies exponential backoff on RateLimited and Timeout errors.
        CacheMiddleware stores results in-memory, keyed by chain name and input hash.
        RateLimitingMiddleware enforces a token-bucket limit per chain.
        TracingMiddleware records OpenTelemetry spans for every execution.
        Use .WithMiddleware(...) on any IConnectableChain to compose behaviour.
        """,
        Source: "docs/middleware.md"),

    new("""
        ChainContext is the pipeline's shared carrier. It holds TraceId, SessionId, UserId,
        ILogger, typed Variables, and ExecutionHistory. Create once with ChainContext.Create()
        and thread the same instance through every ExecuteAsync / StreamAsync call.
        Never create a new ChainContext mid-pipeline.
        """,
        Source: "docs/context.md"),
};

// ── Index ──────────────────────────────────────────────────────────────────────
Console.WriteLine("Indexing knowledge base...");
var indexResult = await pipeline.IndexAsync(knowledgeBase, cts.Token);
if (indexResult.IsFailure)
{
    Console.Error.WriteLine($"Indexing failed: {indexResult.Error!.Message}");
    return;
}
Console.WriteLine($"Indexed {indexResult.Value} chunks from {knowledgeBase.Count} document(s).");
Console.WriteLine("Ask anything about WeaveLLM. Press Ctrl+C to quit.\n");

// ── Interactive Q&A REPL ───────────────────────────────────────────────────────
while (!cts.Token.IsCancellationRequested)
{
    Console.Write("Question: ");
    var question = Console.ReadLine();

    if (cts.Token.IsCancellationRequested) break;
    if (string.IsNullOrWhiteSpace(question)) continue;

    var result = await pipeline.QueryAsync(question, ct: cts.Token);
    if (result.IsFailure)
    {
        Console.Error.WriteLine($"Error ({result.Error!.Code}): {result.Error.Message}");
        continue;
    }

    var answer = result.Value!;
    Console.WriteLine($"\nAnswer: {answer.Answer}");
    if (answer.Sources.Count > 0)
        Console.WriteLine($"Sources: {string.Join(", ", answer.Sources)}");
    Console.WriteLine();
}

// ── Adapters: WeaveLLM.Core.Providers → WeaveLLM.Core.Models ──────────────────
// OpenAIChatModel implements Core.Providers interfaces; DefaultRagPipeline
// expects Core.Models interfaces. These thin wrappers bridge the gap.

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
