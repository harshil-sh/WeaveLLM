# Changelog

## 0.2.0-alpha — 2026-05-07

### Breaking Changes

- All `WeaveLLMError` factory methods now emit `SCREAMING_SNAKE_CASE` codes.
  Consumers matching codes as PascalCase strings must update their checks
  (e.g. `"InvalidInput"` → `"INVALID_INPUT"`, `"NotFound"` → `"NOT_FOUND"`).
- `IEmbeddingModel` moved to `WeaveLLM.Core.Providers.Embeddings`.
  Replace `using WeaveLLM.Core.Providers;` with
  `using WeaveLLM.Core.Providers.Embeddings;` (or use a type alias).
  The old namespace retains an `[Obsolete]` shim until 0.3.0.
- `IChatModel` (Providers namespace) gains `StreamChatSafeAsync`.
  Third-party `IChatModel` implementors must add the method.
  A default wrapper is available via `WeaveLLMError.ProviderError` + async iterator.

### New Features

- `WeaveLLMError` + `ChainError`: six new factory methods —
  `NotFound`, `Cancelled`, `AuthenticationFailed`, `RateLimitExceeded`,
  `NetworkTimeout`, `InvalidConfiguration` — all emitting `SCREAMING_SNAKE_CASE` codes.
- `IStreamingChatModel` (Providers namespace): new interface exposing `StreamChatAsync`;
  `IChatModel` now extends `IStreamingChatModel` — inject the streaming-only interface
  when blocking completion is not needed.
- `IChatModel.StreamChatSafeAsync`: streaming variant that wraps every token in
  `ChainResult<string>` and yields a `Failure` item on error instead of throwing —
  resolves the C# CS1626 `try-catch` + `yield` incompatibility.
- `IChatModel.CompleteAsync(string)`: single-turn convenience method; wraps the prompt
  in a `User` message and returns the assistant reply as a plain string.
- `IHttpClientFactory` constructor on `OpenAIChatModel` and `AnthropicChatModel`:
  preferred for production — enables named-client configuration and Polly retry policies.
- `AddOpenAIChatModel(IConfiguration)` / `AddAnthropicChatModel(IConfiguration)`:
  top-level DI extension methods in `WeaveLLM.Extensions.DependencyInjection`; register
  the model as `IChatModel` with a named `HttpClient` via `IHttpClientFactory`.
- `WeaveLLM.Testing` package: ships `FakeStreamingChatModel` — a pre-built test double
  implementing `IChatModel` with configurable token sequences, mid-stream errors, and
  correct `[EnumeratorCancellation]` support.
- `LLMOptions.Default` (MaxTokens = 2048, Temperature = 0.4) and
  `LLMOptions.Unconstrained` (all-null) static instances; XML docs clarify `null`
  semantics for every property.
