# Changelog

## 0.2.0-alpha

### Breaking Changes

- WeaveLLMError factory methods now document SCREAMING_SNAKE_CASE codes.
  Consumers matching codes as PascalCase strings must update their checks.
- `IEmbeddingModel` moved to `WeaveLLM.Core.Providers.Embeddings`.
  Replace `using WeaveLLM.Core.Providers;` with
  `using WeaveLLM.Core.Providers.Embeddings;` (or use a type alias).
  The old namespace retains an `[Obsolete]` shim until 0.3.0.
