# Contributing to WeaveLLM

Thank you for contributing. This guide covers the four most common contribution paths and the standards that apply to all of them.

---

## Table of Contents

1. [Development Setup](#development-setup)
2. [Adding a New Provider](#adding-a-new-provider)
3. [Adding Middleware](#adding-middleware)
4. [Testing Requirements](#testing-requirements)
5. [Code Style](#code-style)
6. [Pull Request Process](#pull-request-process)
7. [Code of Conduct](#code-of-conduct)

---

## Development Setup

```sh
git clone https://github.com/harshil-inspire2/WeaveLLM.git
cd WeaveLLM
dotnet restore
dotnet build
dotnet test
```

Requirements: .NET 8 SDK. No other tooling required.

---

## Adding a New Provider

Reference implementation: [`src/WeaveLLM.Providers/OpenAI/OpenAIChatModel.cs`](src/WeaveLLM.Providers/OpenAI/OpenAIChatModel.cs)

### Folder structure

Create a subfolder under `src/WeaveLLM.Providers/` named after the provider:

```
src/WeaveLLM.Providers/
└── MyProvider/
    └── MyProviderChatModel.cs
```

### Class contract

```csharp
using WeaveLLM.Core.Interfaces;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Providers.MyProvider;

/// <summary>MyProvider chat completion implementation.</summary>
public sealed class MyProviderChatModel : IChatModel
{
    private readonly HttpClient _http;
    private readonly string _modelId;

    /// <summary>Initializes a new instance with the given API key and model.</summary>
    public MyProviderChatModel(HttpClient http, string modelId)
    {
        _http = http;
        _modelId = modelId;
    }

    /// <inheritdoc/>
    public async Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default)
    {
        // build request, call API, map response to ChainResult
    }
}
```

### Rules

- **No vendor SDK** — use raw `HttpClient`, same as `OpenAIChatModel`. This keeps the dependency surface minimal.
- **Return `ChainResult<T>`**, never throw for expected errors. Map HTTP status codes explicitly:
  - `401` → `ChainError.Unauthorized`
  - `429` → `ChainError.RateLimited`
  - `503` → `ChainError.ServiceUnavailable`
- **`sealed` class** — providers are not designed for inheritance.
- Implement `IEmbeddingModel` as well if the provider supports embeddings (see `OpenAIChatModel` for reference).
- Use `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` for request/response serialization.
- Use `ConfigureAwait(false)` on every `await`.

### Registration

Add an extension method in `src/WeaveLLM.Extensions.DependencyInjection/` so callers can wire the provider with `.AddMyProvider(apiKey, modelId)`.

---

## Adding Middleware

Reference implementation: [`src/WeaveLLM.Core/Middleware/RetryMiddleware.cs`](src/WeaveLLM.Core/Middleware/RetryMiddleware.cs)

### Interface

```csharp
public interface IChainMiddleware<TInput, TOutput>
{
    Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        Func<TInput, CancellationToken, Task<ChainResult<TOutput>>> next,
        CancellationToken cancellationToken = default);
}
```

### Pattern

```csharp
/// <summary>One-line description of what this middleware does.</summary>
public sealed class MyMiddleware<TInput, TOutput> : IChainMiddleware<TInput, TOutput>
{
    /// <inheritdoc/>
    public async Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        Func<TInput, CancellationToken, Task<ChainResult<TOutput>>> next,
        CancellationToken cancellationToken = default)
    {
        // pre-processing

        var result = await next(input, cancellationToken).ConfigureAwait(false);

        // post-processing

        return result;
    }
}
```

### Rules

- **Never throw** — return `ChainResult<TOutput>.Failure(...)` for error conditions.
- Inspect `result.Error!.Code` to branch on specific error types, as `RetryMiddleware` does for `RATE_LIMITED` and `TIMEOUT`.
- Keep middleware single-responsibility. Retry logic does not log; logging middleware does not retry.
- Use `ConfigureAwait(false)` on every `await`.
- Generic over `<TInput, TOutput>` unless there is a concrete reason to constrain the types.

---

## Testing Requirements

- **Framework**: xUnit + FluentAssertions + NSubstitute
- **Coverage**: every public method on every public class must have at least one test. Happy path and at least one failure/edge case.
- **Location**: mirror the source path under `tests/`. For `src/WeaveLLM.Providers/OpenAI/OpenAIChatModel.cs` the test file is `tests/WeaveLLM.Providers.Tests/OpenAI/OpenAIChatModelTests.cs`.
- **No live API calls** — stub `HttpClient` with a fake `HttpMessageHandler` or use NSubstitute to mock `IChatModel` at boundaries.
- **Naming**: `MethodName_Condition_ExpectedOutcome` (e.g. `ChatAsync_RateLimitResponse_ReturnsRateLimitedError`).

```csharp
public sealed class MyMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NextSucceeds_ReturnsSuccess()
    {
        // Arrange
        var middleware = new MyMiddleware<string, string>();
        Task<ChainResult<string>> Next(string _, CancellationToken __) =>
            Task.FromResult(ChainResult<string>.Success("ok"));

        // Act
        var result = await middleware.InvokeAsync("input", Next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }
}
```

PRs that reduce overall test coverage will not be merged.

---

## Code Style

- **Target framework**: .NET 8 (`<TargetFramework>net8.0</TargetFramework>`)
- **Nullable**: `enable` — no `#nullable disable` suppressions without a comment explaining why
- **Implicit usings**: `enable`
- **XML docs**: required on every `public` type and member. Use `/// <inheritdoc/>` for interface implementations. Omit `<remarks>` unless the behavior is non-obvious.
- **No comments explaining what the code does** — rename instead. Reserve comments for non-obvious invariants or external constraints.
- **Formatting**: match the surrounding file. 4-space indentation, Allman braces for types, inline braces acceptable for short lambdas.
- Run `dotnet build /warnaserror` before pushing — the project treats warnings as errors.

---

## Pull Request Process

1. **Fork** the repository and create a branch from `main`: `feature/my-thing` or `fix/my-bug`.
2. **Make your changes** following the guidelines above.
3. **Run the full test suite**: `dotnet test` must pass with zero failures.
4. **Open a PR** against `main` with:
   - A concise title (`Add HuggingFace streaming support`, not `updates`)
   - A description covering: what changed, why, and how to test it
   - Reference to any related issue (`Closes #42`)
5. **Address review comments** — push additional commits; do not force-push during review.
6. A maintainer will merge once all checks pass and at least one approval is given.

**What gets rejected without review:**
- PRs that break existing tests
- New public APIs without XML docs
- Vendor SDK dependencies added to `WeaveLLM.Providers`
- Live API keys or credentials of any kind committed to the repo

---

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating you agree to abide by its terms. Report violations to **harshil.inpsire2@gmail.com**.
