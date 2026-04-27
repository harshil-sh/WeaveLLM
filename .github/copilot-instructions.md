# WeaveLLM — Copilot Instructions

## Build & Test

```bash
# Build entire solution
dotnet build WeaveLLM.sln --configuration Release

# Run all tests
dotnet test WeaveLLM.sln --configuration Release --verbosity normal

# Run a single test project
dotnet test tests/WeaveLLM.Core.Tests/ --verbosity normal

# Pack a specific NuGet package
dotnet pack src/WeaveLLM.Core/WeaveLLM.Core.csproj --configuration Release --output ./nupkgs

# Pack all packages
dotnet pack WeaveLLM.sln --configuration Release --output ./nupkgs

# Publish a release (triggers CI NuGet publish)
git tag v0.1.0-alpha && git push origin v0.1.0-alpha
```

CI publishes to NuGet automatically when a `v*` tag is pushed to `main`.
Packing (no publish) runs on every push to `main`.
Integration tests are skipped in CI unless `WEAVELLM_RUN_INTEGRATION=true` is set.

---

## Architecture

```
WeaveLLM.Core                           ← All interfaces, models, base types. No vendor dependencies.
WeaveLLM.Providers                      ← IChatModel implementations (OpenAI, Anthropic, Ollama, Azure).
WeaveLLM.Memory                         ← IMemoryStore + IVectorStore (InMemory, Redis, Postgres/pgvector, Qdrant).
WeaveLLM.Observability                  ← OpenTelemetry ActivitySource + Meter + LLM evaluators.
WeaveLLM.Extensions.DependencyInjection ← Fluent WeaveLLMBuilder over IServiceCollection.
samples/                                ← Usage examples (not included in solution test run).
tests/
  WeaveLLM.Core.Tests/                  ← Unit tests for core abstractions and chain pipeline.
  WeaveLLM.Providers.Tests/             ← Unit tests for provider implementations (mocked HTTP).
  WeaveLLM.IntegrationTests/            ← Real provider integration tests (skipped by default).
```

**Dependency direction:**
- `Providers` → `Core`
- `Memory` → `Core`
- `Observability` → `Core`
- `DI` → `Providers` + `Memory` + `Core`
- Nothing depends on `Observability` or `DI` — they are opt-in layers.

`WeaveLLM.Core` only references:
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `System.Text.Json`

**Keep it that way — no vendor SDKs, no HTTP clients, no database drivers in Core.**

---

## Key Conventions

### `ChainResult<T>` — errors as values, never throw

All async operations return `ChainResult<T>`. Do not throw from chain or provider
implementations; return `ChainResult<T>.Failure(...)` instead.

```csharp
var result = await model.ChatAsync(messages);
if (result.IsSuccess)
    Console.WriteLine(result.Value!.Content);
else
    Console.WriteLine($"{result.Error!.Code}: {result.Error.Message}");

// Functional mapping — stays in the result monad:
var stringResult = result.Map(m => m.Content);
var bound       = result.Bind(m => Summarise(m.Content));
var matched     = result.Match(v => v.Content, e => $"Error: {e.Code}");

// Deconstruction:
var (ok, value, error) = result;
```

Use static factory methods on `ChainError` for structured codes:
`ChainError.Timeout(...)`, `ChainError.RateLimited(...)`,
`ChainError.InvalidInput(...)`, `ChainError.ProviderError(...)`,
`ChainError.ToolExecutionFailed(...)`, `ChainError.ContextTooLong(...)`.

Never create `ChainError` with a raw string code — always use the factory methods
so codes remain consistent across the codebase.

---

### `IChain<TInput, TOutput>` — the atomic unit

Every unit of work implements `IChain<TInput, TOutput>`.
Use `IConnectableChain<TInput, TOutput>` when the chain needs to be composed
via `.Pipe<TNext>()` or wrapped with `.WithMiddleware(...)`.

Built-in middleware lives in `WeaveLLM.Core/Middleware/`:
- `RetryMiddleware<TInput, TOutput>` — exponential backoff, retries on `RateLimited` and `Timeout` only.
- `CacheMiddleware<TInput, TOutput>` — in-memory, keyed by chain name + input hash.
- `RateLimitingMiddleware<TInput, TOutput>` — token bucket, returns `ChainError.RateLimited` when exceeded.
- `TracingMiddleware<TInput, TOutput>` — OpenTelemetry span per execution (in `WeaveLLM.Observability`).

Extension methods on `IChain` live in `ChainExtensions.cs`:
```csharp
var chain = myChain
    .WithMiddleware(new RetryMiddleware<Input, Output>(maxRetries: 3))
    .WithMiddleware(new CacheMiddleware<Input, Output>(ttl: TimeSpan.FromMinutes(10)));

var pipeline = chainA.Then(chainB); // TOutput of A must match TInput of B
```

---

### `ChainContext` — the pipeline's shared carrier

Always thread `ChainContext` through every `ExecuteAsync` / `StreamAsync` call.
It carries: `TraceId`, `SessionId`, `UserId`, `ILogger`, typed `Variables`,
and the `ExecutionHistory`.

Create with:
```csharp
var ctx = ChainContext.Create(userId: "user-123", logger: logger);
```

Never create a new `ChainContext` mid-pipeline — pass the same instance through.

---

### `Message` and `LLMOptions` — use static factories

```csharp
// Messages
var messages = new[]
{
    Message.System("You are a helpful assistant."),
    Message.User("Explain transformers briefly."),
    Message.Assistant("Transformers are..."),
    Message.Tool(content: "42", toolCallId: "call_abc")
};

// Options
LLMOptions.Deterministic()  // temperature: 0
LLMOptions.Balanced()       // temperature: 0.7
LLMOptions.Creative()       // temperature: 1.0
```

Do not construct `Message` or `LLMOptions` directly — always use the factory methods.

---

### Streaming — `IAsyncEnumerable<string>` throughout

```csharp
// Streaming is on IStreamingChatModel — cast or inject accordingly:
await foreach (var chunk in model.StreamChatAsync(messages, null, cancellationToken))
{
    Console.Write(chunk);
}
```

Streaming methods always accept `CancellationToken` and must honour cancellation
on every `yield return`. Do not buffer the full response before yielding.

---

### `AgentGraph<TState>` — multi-agent orchestration

`TState` must be a `class` with a `new()` constraint.
Nodes are either `IGraphNode<TState>` implementations or inline `Func` delegates.
Conditional routing uses a string key mapped to a node name via `RouteMap`.

```csharp
var graph = AgentGraph<MyState>.Create()
    .AddNode("step-a", async (state, ctx, ct) => { ... return state; })
    .AddEdge("step-a", "step-b")
    .AddConditionalEdge("step-b",
        state => state.NeedsRetry ? "retry" : "done",
        new() { ["retry"] = "step-a", ["done"] = "end" })
    .SetEntryPoint("step-a")
    .SetEndPoint("end");

var result = await graph.RunAsync(new MyState(), cancellationToken);
```

Always call `SetEntryPoint()` and `SetEndPoint()` before `RunAsync()`.
The graph validates node references on first run and returns `ChainError`
with `Code = "InvalidGraph"` if any edge target is missing.
Default `MaxSteps` is 50 — override in `AgentGraph.Create(maxSteps: N)`.

---

### `[LLMTool]` attribute — zero-boilerplate tool registration

```csharp
public sealed class MyTools
{
    [LLMTool("search_web", "Search the internet for current information")]
    public async Task<string> SearchAsync(
        [Description("The search query")] string query,
        [Description("Maximum results to return")] int maxResults = 5)
    {
        return await DoSearch(query, maxResults);
    }
}

// Register — JSON schema is generated automatically via reflection:
registry.RegisterFromObject(new MyTools());
```

Rules:
- Methods must be `public` instance methods (static not supported).
- Parameters must be JSON-serialisable primitives or simple POCOs.
- Use `System.ComponentModel.DescriptionAttribute` on every parameter.
- Both `Task<string>` and `string` return types are supported.
- Do not throw from tool methods — return an error string instead.

---

### Provider implementations

Providers use raw `HttpClient` — **no OpenAI/Anthropic/vendor SDK dependencies**.
The `baseUrl` constructor parameter makes every provider compatible with
Azure OpenAI, local Ollama proxies, and custom endpoints.

When adding a new provider:
1. Implement `IStreamingChatModel` (extends `IChatModel` extends `ILanguageModel`).
2. Place under `src/WeaveLLM.Providers/<ProviderName>/`.
3. Use `IHttpClientFactory` — never `new HttpClient()`.
4. Map all HTTP errors to `ChainResult.Failure(ChainError.ProviderError(...))`.
5. Pass `CancellationToken` to every `HttpClient` call.
6. Add a corresponding `Add<ProviderName>(...)` method to `WeaveLLMBuilder`.

---

### Memory & RAG conventions

`IMemoryStore` is for **conversation history** (short-term, session-scoped).
`IVectorStore` is for **semantic search** (long-term, embeddings-based).
`DefaultRagPipeline` composes both — use it unless you need custom chunking logic.

```csharp
// Indexing
var result = await rag.IndexAsync(documents, cancellationToken);

// Querying
var answer = await rag.QueryAsync("How do I configure auth?", topK: 5, cancellationToken);
Console.WriteLine(answer.Value.Answer);
Console.WriteLine(string.Join(", ", answer.Value.Sources));
```

`Document` metadata keys are freeform strings — use consistent keys:
`"source"`, `"title"`, `"author"`, `"created_at"` (ISO 8601).

For `IVectorStore.SearchAsync`, `topK` should default to 5 unless the caller
has a specific reason to change it. Cosine similarity is the standard metric.

---

### DI registration — `WeaveLLMBuilder`

```csharp
// Program.cs / Startup.cs
builder.Services
    .AddWeaveLLM()
    .AddOpenAI(apiKey: config["WeaveLLM:OpenAI:ApiKey"]!)
    .AddInMemoryMemory()
    .AddToolRegistry(r => r.RegisterFromObject(new BuiltInTools()))
    .AddReActAgent()
    .AddWeaveLLMTelemetry();
```

Configuration keys (appsettings.json / environment variables):
```
WeaveLLM:OpenAI:ApiKey
WeaveLLM:OpenAI:ModelId          (default: gpt-4o)
WeaveLLM:Anthropic:ApiKey
WeaveLLM:Anthropic:ModelId       (default: claude-sonnet-4-5)
WeaveLLM:Ollama:BaseUrl          (default: http://localhost:11434)
WeaveLLM:Ollama:ModelId
```

---

### Observability conventions

OpenTelemetry source name: `"WeaveLLM"` (ActivitySource and Meter use the same name).

Standard span tags set by `TracingMiddleware`:
- `chain.type` — fully qualified type name
- `chain.input_type`, `chain.output_type`
- `llm.provider`, `llm.model`, `llm.prompt_tokens`, `llm.completion_tokens`

Standard metric instruments (all in `LlmMetrics`):
- `weavellm.llm.tokens.prompt` — Histogram\<int\>
- `weavellm.llm.tokens.completion` — Histogram\<int\>
- `weavellm.llm.latency_ms` — Histogram\<double\>
- `weavellm.llm.errors` — Counter\<int\> (tag: `error.code`)
- `weavellm.llm.requests` — Counter\<int\> (tag: `provider.id`)

---

### Testing conventions

- Use **xUnit**, **FluentAssertions**, and **NSubstitute** in all test projects.
- Use `MockChatModel` (in `tests/WeaveLLM.Core.Tests/Fakes/`) for unit tests — never hit real APIs.
- Test naming: `{MethodName}_{Condition}_{ExpectedOutcome}`.
- Use `[Theory]` + `[InlineData]` for multiple input variants.
- Integration tests use `[IntegrationFact]` — skipped unless `WEAVELLM_RUN_INTEGRATION=true`.
- Assert `ChainResult` outcomes via `.IsSuccess` / `.IsFailure` and `.Error.Code`,
  never catch exceptions.

```csharp
// Good — tests the contract:
result.IsSuccess.Should().BeTrue();
result.Value!.Content.Should().Contain("pong");

// Good — error path:
result.IsFailure.Should().BeTrue();
result.Error!.Code.Should().Be("RateLimited");
```

---

### Project settings (all packages)

| Setting | Value |
|---|---|
| `TargetFramework` | `net8.0` |
| `Nullable` | `enable` |
| `ImplicitUsings` | `enable` |
| `LangVersion` | `latest` |
| `TreatWarningsAsErrors` | `false` (warn only during alpha) |

All **public** API types and members must have XML doc comments (`<summary>` minimum).
Internal types do not require XML docs.
Do not use "Gets or sets" in summaries — describe what the member represents.

---

### Versioning

All packages share the same version number, managed via `Directory.Build.props`.
Use semantic versioning: `MAJOR.MINOR.PATCH[-prerelease]`.
Pre-release suffixes: `-alpha`, `-beta`, `-rc.1`.
Never publish a non-pre-release version until the full test suite passes and
all public API XML docs are complete.

Current version: `0.1.0-alpha`
