# WeaveLLM

LangChain / LangGraph alternative for the .NET ecosystem.
See `WeaveLLM_Project_Knowledge.md` for full architecture and roadmap.

## Build & Test Commands

dotnet build
dotnet test
dotnet pack src/WeaveLLM.Core/WeaveLLM.Core.csproj

## Project Structure

src/
  WeaveLLM.Core/        ← Chains, Memory, Middleware, Graph
  WeaveLLM.Providers/   ← {ProviderName}/{ProviderName}ChatModel.cs

## Code Conventions

- All errors returned as ChainResult.Failure — never throw
- CancellationToken on every async method
- IHttpClientFactory for all HTTP — never new HttpClient()
- XML doc comments on all public members
- Thread-safe implementations

## Naming

- Tests: {Method}_{Condition}_{ExpectedOutcome}
- ChainError codes: PascalCase constants

## Reference Implementation

OpenAIChatModel is the canonical provider pattern.
All new providers must follow it exactly.

## Safety Rules

- Never commit secrets or API keys
- Never modify ChainResult<T> or ChainError core signatures without discussion
- Do not push directly to main