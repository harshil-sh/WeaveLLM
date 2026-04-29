---
name: new-provider
description: Add a new LLM provider following the OpenAIChatModel pattern
---
Create {ProviderName}ChatModel in src/WeaveLLM.Providers/{ProviderName}/
Follow OpenAIChatModel structure exactly:
- Implements IStreamingChatModel
- Constructor: IHttpClientFactory, string apiKey, string model
- All errors as ChainResult.Failure — never throw
- CancellationToken on all async methods
- IHttpClientFactory for all HTTP — never new HttpClient()
- XML doc comments on all public members
- Thread-safe

Provider and API endpoint: $ARGUMENTS