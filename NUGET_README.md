LangChain is slow and leaky — WeaveLLM is a native .NET 8 AI orchestration framework that treats errors as values, wires into ASP.NET Core DI out of the box, and composes like middleware.

## Quick Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddWeaveLLM()
    .AddOpenAI(apiKey: builder.Configuration["OpenAI:ApiKey"]!, modelId: "gpt-4o");

var app = builder.Build();

app.MapPost("/chat", async (string message, IChatModel model) =>
{
    var result = await model.ChatAsync([Message.User(message)]);
    return result.IsSuccess ? Results.Ok(result.Value!.Content) : Results.Problem(result.Error!.Message);
});

app.Run();
```

## Why WeaveLLM

- **Railway-oriented results** — every call returns `ChainResult<T>`; no swallowed exceptions, composable error paths
- **ASP.NET-style middleware pipeline** — stack retry, caching, rate limiting, PII scrubbing, and cost tracking as layers
- **Single fluent registration** — `AddWeaveLLM()` wires providers, memory, agents, health checks, and telemetry in one call
- **Native .NET 8** — built on `IServiceCollection`, `IAsyncEnumerable`, and the BCL; no Python bridges, no wrapper tax
- **OpenTelemetry first-class** — traces, token cost metrics, and PII scrubbing ship in `WeaveLLM.Observability`

## Supported Providers

| Provider | Package | Notes |
|---|---|---|
| OpenAI | `WeaveLLM.Providers` | GPT-4o, GPT-4-turbo, GPT-3.5 |
| Anthropic | `WeaveLLM.Providers` | Claude 3.x / 4.x series |
| Ollama | `WeaveLLM.Providers` | Local models via Ollama REST API |
| HuggingFace | `WeaveLLM.Providers` | Inference API (text-generation) |

## Installation

```sh
dotnet add package WeaveLLM.Core
dotnet add package WeaveLLM.Providers
dotnet add package WeaveLLM.Memory
dotnet add package WeaveLLM.Observability
dotnet add package WeaveLLM.Extensions.DependencyInjection
```

## Links

- [GitHub](https://github.com/harshil-sh/WeaveLLM)
- [Samples](https://github.com/harshil-sh/WeaveLLM/tree/main/samples)
- [Discussions](https://github.com/harshil-sh/WeaveLLM/discussions)

⭐ Star on GitHub to follow development
