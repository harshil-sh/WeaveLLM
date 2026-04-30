# WeaveLLM

[![NuGet Version](https://img.shields.io/nuget/v/WeaveLLM.Core?label=nuget&color=blue)](https://www.nuget.org/packages/WeaveLLM.Core)
[![Build Status](https://img.shields.io/github/actions/workflow/status/harshil-inspire2/WeaveLLM/build.yml?branch=main)](https://github.com/harshil-inspire2/WeaveLLM/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> A composable AI orchestration framework for .NET — build LLM chains, RAG pipelines, and autonomous agents with idiomatic C#.

---

## Why WeaveLLM?

**Pain points with LangChain / Semantic Kernel:**

- LangChain is Python-first; .NET ports lag behind and feel bolted on
- Semantic Kernel's kernel-centric model adds ceremony around simple chat calls
- Both frameworks hide errors inside exceptions, making retry logic hard to compose
- Neither integrates naturally with ASP.NET Core DI, health checks, or OpenTelemetry

**WeaveLLM advantages:**

- **Native .NET 8** — built on `IServiceCollection`, `IAsyncEnumerable`, and the BCL; no Python bridges
- **Railway-oriented results** — every call returns `ChainResult<T>` instead of throwing; errors are first-class values
- **ASP.NET-style middleware pipeline** — add retry, caching, rate limiting, PII scrubbing, and cost tracking as composable layers
- **Single fluent registration** — one `AddWeaveLLM()` call wires providers, memory, agents, health checks, and telemetry

---

## Installation

```sh
dotnet add package WeaveLLM.Core
dotnet add package WeaveLLM.Providers      # OpenAI, Anthropic, Ollama, HuggingFace
dotnet add package WeaveLLM.Memory         # RAG, vector stores, chunking
dotnet add package WeaveLLM.Observability  # OpenTelemetry, cost tracking, PII scrubbing
dotnet add package WeaveLLM.Extensions.DependencyInjection  # ASP.NET Core integration
```

---

## Quick Start

```csharp
using WeaveLLM.Core.Models;
using WeaveLLM.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddWeaveLLM()
    .AddOpenAI(apiKey: builder.Configuration["OpenAI:ApiKey"]!, modelId: "gpt-4o")
    .AddInMemoryMemory()
    .AddToolRegistry()
    .AddReActAgent();

var app = builder.Build();

app.MapPost("/chat", async (ChatRequest req, IChatModel model) =>
{
    var result = await model.ChatAsync([Message.User(req.Message)]);
    return result.IsSuccess
        ? Results.Ok(result.Value!.Content)
        : Results.Problem(result.Error!.Message);
});

app.Run();

record ChatRequest(string Message);
```

---

## Features

### Composable Chains

Build type-safe pipelines from reusable `IChain<TInput, TOutput>` blocks. Wrap any chain with middleware layers using `PipelineBuilder`:

```csharp
var pipeline = new PipelineBuilder<IReadOnlyList<Message>, ChatResponse>(chatChain)
    .UseMiddleware(new RetryMiddleware<IReadOnlyList<Message>, ChatResponse>(maxRetries: 3))
    .UseMiddleware(new CacheMiddleware<IReadOnlyList<Message>, ChatResponse>(ttl: TimeSpan.FromMinutes(5)));

var chain = await pipeline.BuildAsync();
var result = await chain.ExecuteAsync([Message.User("Hello")]);
```

### Streaming

Every provider supports `IAsyncEnumerable<string>` streaming out of the box:

```csharp
app.MapGet("/chat/stream", async (string message, IChatModel model, HttpContext http) =>
{
    http.Response.ContentType = "text/event-stream";
    await foreach (var chunk in model.StreamChatAsync([Message.User(message)]))
        await http.Response.WriteAsync($"data: {chunk}\n\n");
});
```

### Middleware

Cross-cutting concerns slot into the chain pipeline without modifying chain logic. Implement one interface:

```csharp
public sealed class LoggingMiddleware<TIn, TOut> : IChainMiddleware<TIn, TOut>
{
    public async Task<ChainResult<TOut>> InvokeAsync(
        TIn input, ChainContext ctx, ChainDelegate<TIn, TOut> next, CancellationToken ct)
    {
        Console.WriteLine($"[{ctx.ChainName}] invoking");
        var result = await next(input, ctx, ct);
        Console.WriteLine($"[{ctx.ChainName}] success={result.IsSuccess}");
        return result;
    }
}
```

Built-in: `RetryMiddleware` (exponential backoff), `CacheMiddleware`, `RateLimitingMiddleware`, `TracingMiddleware`, `CostMiddleware`, `PiiScrubbingMiddleware`.

### Memory

Persist conversation history and enable semantic search with a swappable `IMemoryStore` / `IVectorStore`:

```csharp
builder.Services.AddWeaveLLM()
    .AddOpenAI(apiKey)
    .AddInMemoryMemory();  // swap for QdrantVectorStore or PostgresVectorStore

// In a handler:
var history = new List<Message>();
await foreach (var entry in memory.GetAsync(sessionId, limit: 20, ct))
    history.Add(entry.Message);

history.Add(Message.User(userInput));
var response = await model.ChatAsync(history, cancellationToken: ct);
await memory.AddAsync(
    new MemoryEntry(sessionId, Message.Assistant(response.Value!.Content), DateTimeOffset.UtcNow), ct);
```

Supported vector backends: **In-Memory**, **Qdrant**, **PostgreSQL + pgvector**.

### Agents

Two strategies ship out of the box, both composable with `[LLMTool]`-annotated methods:

```csharp
public sealed class CalculatorTool
{
    [LLMTool("calculate", "Evaluate a mathematical expression")]
    public string Calculate([Description("e.g. '2 + 2 * 10'")] string expression)
    {
        var table = new System.Data.DataTable();
        return table.Compute(expression, string.Empty)?.ToString() ?? "0";
    }
}

builder.Services.AddWeaveLLM()
    .AddOpenAI(apiKey)
    .AddToolRegistry(r => r.RegisterFromObject(new CalculatorTool()))
    .AddReActAgent(maxSteps: 10);

// Inject IAgent and run:
var result = await agent.RunAsync("What is (123 * 456) + 789?", ct);
Console.WriteLine(result.Value!.FinalAnswer);
```

**ReActAgent** — Thought → Action → Observation loop until the model emits a Final Answer  
**PlanAndExecuteAgent** — separate planning and execution phases for complex multi-step tasks

---

## Supported Providers

| Provider | Chat | Streaming | Embeddings | Notes |
|---|---|---|---|---|
| **OpenAI** | ✅ | ✅ | ✅ | Default model: `gpt-4o` |
| **Anthropic** | ✅ | ✅ | — | Default model: `claude-sonnet-4-5` |
| **Azure OpenAI** | Planned | Planned | Planned | v0.2.0-alpha |
| **Ollama** | ✅ | ✅ | ✅ | Local inference; default model: `llama3` |
| **HuggingFace** | ✅ | — | ✅ | Inference API; bring your own model ID |

---

## Examples

| Sample | Description |
|---|---|
| [`samples/WeaveLLM.Sample.BasicChain`](samples/WeaveLLM.Sample.BasicChain) | Multi-provider ASP.NET Core API with session memory, SSE streaming, agent endpoint, and RAG index/query routes |
| [`samples/ChatWithDocs`](samples/ChatWithDocs) | Console RAG chatbot — loads a local `docs/` folder, indexes into a vector store, answers questions with source citations |

---

## Roadmap

| Version | Status | Highlights |
|---|---|---|
| **v0.1.0-alpha** | ✅ Current | Core chains · OpenAI / Anthropic / Ollama / HuggingFace providers · ReAct + PlanAndExecute agents · RAG pipeline · In-Memory / Qdrant / Postgres vector stores · OpenTelemetry · PII scrubbing · cost tracking |
| **v0.2.0-alpha** | Planned | Azure OpenAI provider · multi-modal (image) input · streaming agents · evaluation suite GA |
| **v1.0.0** | Target: Q4 2026 | Stable public API · NuGet stable release · documentation site |

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) for branch conventions, coding guidelines, and the pull-request checklist before opening a PR.

---

## License

WeaveLLM is released under the [MIT License](LICENSE).

---

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=harshil-inspire2/WeaveLLM&type=Date)](https://star-history.com/#harshil-inspire2/WeaveLLM&Date)
