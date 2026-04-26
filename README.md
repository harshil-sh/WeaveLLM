# WeaveLLM 🧵

**The AI orchestration framework built for .NET — strongly typed, production-ready, and genuinely extensible.**

[![NuGet](https://img.shields.io/nuget/v/WeaveLLM.Core.svg)](https://www.nuget.org/packages/WeaveLLM.Core)
[![Build](https://github.com/yourusername/WeaveLLM/actions/workflows/ci.yml/badge.svg)](https://github.com/yourusername/WeaveLLM/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-blue)](https://dotnet.microsoft.com)

> Strongly typed LangChain alternative for .NET — chains, agents, RAG, and multi-agent graphs built for production.

---

## Why WeaveLLM?

Python's AI tooling is powerful but chaotic. LangChain suffers from abstraction leakage, opaque errors, and duck-typing that explodes at runtime. .NET developers deserve better.

| | LangChain (Python) | WeaveLLM (.NET) |
|---|---|---|
| **Type safety** | Duck typed | Full generics + compile-time validation |
| **Error handling** | Exceptions everywhere | `ChainResult<T>` — errors as values |
| **Streaming** | Inconsistent | Native `IAsyncEnumerable<T>` throughout |
| **DI integration** | Manual wiring | First-class `IServiceCollection` builder |
| **Cancellation** | Often ignored | `CancellationToken` on every async call |
| **Observability** | Third-party plugins | Native OpenTelemetry — traces + metrics |
| **Agent loops** | LangGraph | `AgentGraph<TState>` — type-safe graph engine |

---

## Quick Start

```bash
dotnet add package WeaveLLM.Core
dotnet add package WeaveLLM.Providers
dotnet add package WeaveLLM.Extensions.DependencyInjection
```

```csharp
// Program.cs
builder.Services
    .AddWeaveLLM()
    .AddOpenAI(apiKey: "sk-...")
    .AddInMemoryMemory()
    .AddToolRegistry(r => r.RegisterFromObject(new WebSearchTool()))
    .AddReActAgent();

// In your controller / minimal API:
var result = await agent.RunAsync("What is the weather in Sheffield today?");
Console.WriteLine(result.FinalAnswer);
```

---

## Core Concepts

### Chains — the atomic unit

```csharp
// Every chain is IChain<TInput, TOutput> — no surprises at runtime.
var result = await model.ChatAsync(
    messages: [Message.User("Explain transformers in one paragraph")],
    options: LLMOptions.Balanced()
);

if (result.IsSuccess)
    Console.WriteLine(result.Value.Content);
else
    Console.WriteLine($"Failed: {result.Error.Code} — {result.Error.Message}");
```

### Streaming — native IAsyncEnumerable

```csharp
await foreach (var chunk in model.StreamChatAsync([Message.User("Write a poem")]))
{
    Console.Write(chunk); // tokens arrive as they're generated
}
```

### Tool use — zero boilerplate

```csharp
public sealed class MyTools
{
    [LLMTool("search_web", "Search the internet for current information")]
    public async Task<string> SearchAsync(
        [Description("The search query")] string query,
        [Description("Max results to return")] int maxResults = 5)
    {
        // your implementation
        return searchResults;
    }
}

// Register — that's it. WeaveLLM generates the JSON schema automatically.
registry.RegisterFromObject(new MyTools());
```

### RAG Pipeline

```csharp
// Index documents
var indexResult = await rag.IndexAsync([
    new Document { Content = File.ReadAllText("docs.txt"), Source = "docs.txt" }
]);

// Query with hybrid search
var answer = await rag.QueryAsync("How do I configure authentication?");
Console.WriteLine(answer.Value);
```

### Multi-Agent Graphs — the LangGraph equivalent

```csharp
var graph = AgentGraph<ResearchState>.Create()
    .AddNode("researcher", async (state, ctx, ct) => {
        state.Notes = await ResearchTopic(state.Question);
        return state;
    })
    .AddNode("writer", async (state, ctx, ct) => {
        state.Draft = await WriteDraft(state.Notes);
        return state;
    })
    .AddNode("critic", async (state, ctx, ct) => {
        state.NeedsRevision = await Critique(state.Draft);
        return state;
    })
    .SetEntryPoint("researcher")
    .AddEdge("researcher", "writer")
    .AddEdge("writer", "critic")
    .AddConditionalEdge("critic",
        state => state.NeedsRevision ? "revise" : "finalize",
        new() { ["revise"] = "writer", ["finalize"] = "done" })
    .SetEndPoint("done");

var result = await graph.RunAsync(new ResearchState { Question = "Explain quantum computing" });
```

### Middleware — cross-cutting concerns

```csharp
// Apply retry + caching to any chain
var chain = myChain
    .WithMiddleware(new RetryMiddleware<Input, Output>(maxRetries: 3))
    .WithMiddleware(new CacheMiddleware<Input, Output>(ttl: TimeSpan.FromMinutes(10)));
```

---

## Supported Providers

| Provider | Chat | Streaming | Embeddings |
|---|---|---|---|
| OpenAI (GPT-4o, o1, o3) | ✅ | ✅ | ✅ |
| Anthropic (Claude 3.5, 4) | ✅ | ✅ | — |
| Azure OpenAI | ✅ | ✅ | ✅ |
| Ollama (local) | ✅ | ✅ | ✅ |
| Hugging Face | ✅ | — | ✅ |
| Google Gemini | 🔜 | 🔜 | 🔜 |

---

## Package Structure

```
WeaveLLM.Core                          — Interfaces, models, base abstractions
WeaveLLM.Providers                     — OpenAI, Anthropic, Azure, Ollama providers
WeaveLLM.Memory                        — Redis, Postgres, CosmosDB memory stores
WeaveLLM.Observability                 — OpenTelemetry, metrics, LLM evaluators
WeaveLLM.Extensions.DependencyInjection — IServiceCollection fluent builder
```

---

## Roadmap

- [x] Core chain abstractions + middleware pipeline
- [x] OpenAI + Anthropic providers
- [x] In-memory store + RAG pipeline
- [x] ReAct agent + AgentGraph
- [x] OpenTelemetry integration
- [ ] Redis + pgvector memory backends
- [ ] Ollama local model support
- [ ] Blazor visual chain builder (ChainForge)
- [ ] Prompt versioning + A/B testing server (PromptVault)
- [ ] RAGAS-equivalent evaluation suite (EvalKit.NET)
- [ ] Azure Aspire integration dashboard

---

## Contributing

Contributions welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

---

## License

MIT — free for personal and commercial use. See [LICENSE](LICENSE).

---

*Built with ❤️ for the .NET community. If this project helps you, please ⭐ the repo.*
