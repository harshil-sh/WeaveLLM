# WeaveLLM — 8-Week Build Roadmap

## Budget-conscious AI tool strategy

| Tool | Use for | Cost |
|---|---|---|
| GitHub Copilot | All implementation code | ~$10/mo (keep) |
| Claude Free | Architecture + design reviews | Free |
| ChatGPT Free | Debugging + short code tasks | Free |

---

## Week 1 — Foundation (Ship the NuGet skeleton)

**Goal:** Get `WeaveLLM.Core` published to NuGet. Even an empty-ish package with the right interfaces signals commitment and reserves the package name.

- [ ] Create the `.sln` file and all `.csproj` files
- [ ] Publish `WeaveLLM.Core` 0.1.0-alpha to NuGet
- [ ] Set up GitHub Actions CI (build + test on push)
- [ ] Write the README with badges (even before features — this is marketing)
- [ ] Create GitHub Discussions for community feedback

**Key files already generated for you:**
- `IChain.cs`, `IChainMiddleware.cs` — core chain contracts
- `ChainResult.cs` — error-as-value result type
- `ILanguageModel.cs`, `IChatModel.cs` — provider contracts
- `IMemoryStore.cs`, `IVectorStore.cs` — memory contracts

---

## Week 2 — First Provider (OpenAI + Anthropic)

**Goal:** Working end-to-end chat with both providers. Developers can `dotnet add package` and chat.

- [ ] Complete `OpenAIChatModel` (code already generated — wire up and test)
- [ ] Complete `AnthropicChatModel` (code already generated — wire up and test)
- [ ] Add `OllamaChatModel` for local model support (huge for privacy-conscious devs)
- [ ] Add `IEmbeddingModel` implementations for OpenAI + Ollama
- [ ] Write unit tests with a mock LLM that returns fake responses
- [ ] Publish `WeaveLLM.Providers.OpenAI` 0.1.0

---

## Week 3 — Memory + RAG

**Goal:** Developers can index documents and query them. This is the most-requested feature.

- [ ] `InMemoryStore` (already coded — ship it)
- [ ] `RecursiveTextSplitter` (already coded — ship it)
- [ ] `DefaultRagPipeline` (already coded — wire up and test)
- [ ] Add `RedisMemoryStore` (requires StackExchange.Redis)
- [ ] Add plain-text and markdown document loaders
- [ ] Write a sample: "Chat with your docs in 20 lines"
- [ ] Post sample on Reddit r/dotnet and r/csharp

---

## Week 4 — Agents (The Viral Moment)

**Goal:** A working ReAct agent with tools. This is what gets GitHub stars.

- [ ] `ReActAgent` (already coded — test and polish)
- [ ] `ToolRegistry` with `[LLMTool]` attribute (already coded)
- [ ] Built-in tools: web search (Brave/Serper API), calculator, datetime
- [ ] `AgentGraph<TState>` for multi-agent workflows (already coded)
- [ ] Demo: Multi-agent research pipeline (researcher → writer → critic)
- [ ] Post demo GIF to Twitter/X and LinkedIn

---

## Week 5 — DI + ASP.NET Core Integration

**Goal:** Zero-friction setup for ASP.NET Core developers (your biggest audience).

- [ ] `WeaveLLMBuilder` fluent API (already coded)
- [ ] `appsettings.json` configuration binding
- [ ] Health check endpoint (`IHealthCheck` for each provider)
- [ ] Rate limiting middleware out of the box
- [ ] Minimal API sample (already coded in `Program.cs`)
- [ ] Blog post: "Building an AI chat API in .NET in 5 minutes"

---

## Week 6 — Observability

**Goal:** Production-ready tracing. This is what enterprise teams need before adopting.

- [ ] OpenTelemetry traces on all chain/agent calls (skeleton already coded)
- [ ] Metrics: token count, latency, error rate, estimated cost
- [ ] `.AddWeaveLLMTelemetry()` one-liner for Aspire dashboard
- [ ] `LlmJudgeEvaluator` + `FaithfulnessEvaluator` (already coded)
- [ ] Structured logging with `ILogger` integration

---

## Week 7 — Mini Product #1: VecSearch.NET

**Goal:** A standalone NuGet for semantic search, no full platform needed. Drives discovery.

- [ ] Extract `IVectorStore` + in-memory implementation to separate package
- [ ] Add `QdrantVectorStore` implementation
- [ ] Add `PostgresVectorStore` (pgvector extension)
- [ ] Simple `VecSearch.NET` README with one-command Docker setup
- [ ] Publish as separate NuGet with its own README

---

## Week 8 — Polish + Launch

**Goal:** Make the repo "GitHub-star-worthy" on first impression.

- [ ] Clean up all TODOs, add XML docs to all public APIs
- [ ] Create a 2-minute demo video (screen record, no editing needed)
- [ ] Write a "Why I built WeaveLLM" dev.to / Hashnode post
- [ ] Submit to Awesome .NET, Awesome AI lists
- [ ] Post to: Hacker News (Show HN), r/dotnet, r/csharp, r/MachineLearning
- [ ] Create a GitHub Discussions roadmap + voting

---

## Mini Products (post-launch virality)

| Product | Description | ETA |
|---|---|---|
| **VecSearch.NET** | Standalone semantic search NuGet | Week 7 |
| **PromptVault** | Versioned prompt management server | Month 3 |
| **EvalKit.NET** | LLM output evaluation (RAGAS for .NET) | Month 3 |
| **ChainForge** | Blazor visual chain builder | Month 4-5 |
| **AgentWorker** | Aspire-integrated background agent host | Month 4 |

---

## GitHub Virality Checklist

- [ ] Repo name: `WeaveLLM` (not `weavellm-dotnet` — clean names win)
- [ ] Description: "AI orchestration framework for .NET — strongly typed LangChain alternative"
- [ ] Topics: `ai`, `llm`, `langchain`, `dotnet`, `csharp`, `rag`, `agents`, `openai`, `anthropic`
- [ ] README has a comparison table vs LangChain within the first scroll
- [ ] README has a working code sample within 10 lines
- [ ] Pinned: one demo GIF showing the agent in action
- [ ] GitHub Discussions enabled (community builds moat)
- [ ] MIT License (no friction for enterprise adoption)
- [ ] CONTRIBUTING.md (signals maturity)
- [ ] Semantic versioning from day one

---

## Low-Budget AI Tool Swap Guide

Once your subscriptions lapse, here's what to use for each task:

| Task | Tool |
|---|---|
| Writing new classes + interfaces | GitHub Copilot (inline) |
| Understanding a bug | ChatGPT free (paste the error) |
| Architecture decisions | Claude free (best reasoning) |
| Writing tests | Copilot + ChatGPT free |
| Writing README / docs | Claude free (best writing) |
| Provider integration (HTTP calls) | Copilot autocomplete |
