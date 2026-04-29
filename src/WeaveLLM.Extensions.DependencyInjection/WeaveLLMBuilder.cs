#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;
using WeaveLLM.Core.Prompts;
using WeaveLLM.Extensions.DependencyInjection.HealthChecks;
// using WeaveLLM.Memory.InMemory; -- accessed via MemoryInMemoryStore alias below to avoid ambiguity
using WeaveLLM.Providers.Anthropic;
using WeaveLLM.Providers.HuggingFace;
using WeaveLLM.Providers.Ollama;
using WeaveLLM.Providers.OpenAI;
// Aliases to disambiguate the two IChatModel/IEmbeddingModel hierarchies:
// WeaveLLM.Core.Models.* — used by agents (IStreamingChatModel, IChatModel, IEmbeddingModel)
// WeaveLLM.Core.Providers.* — implemented by concrete provider classes
using ProviderIChatModel = WeaveLLM.Core.Providers.IChatModel;
using ProviderIEmbeddingModel = WeaveLLM.Core.Providers.IEmbeddingModel;
using MemoryInMemoryStore = WeaveLLM.Memory.InMemory.InMemoryStore;

namespace WeaveLLM.Extensions.DependencyInjection;

/// <summary>
/// Fluent builder for registering WeaveLLM services with the .NET DI container.
/// Works with ASP.NET Core, Worker Services, and any IServiceCollection host.
/// </summary>
/// <example>
/// <code>
/// builder.Services
///     .AddWeaveLLM()
///     .AddOpenAI(cfg["WeaveLLM:OpenAI:ApiKey"]!)
///     .AddInMemoryMemory()
///     .AddToolRegistry()
///     .AddReActAgent();
/// </code>
/// </example>
public sealed class WeaveLLMBuilder(IServiceCollection services)
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; } = services;

    // ── OpenAI ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers OpenAIChatModel as <see cref="IStreamingChatModel"/>, <see cref="IChatModel"/>,
    /// and <see cref="IEmbeddingModel"/> (singleton). Configures a named HttpClient <c>"openai"</c>
    /// with the API key and base address pre-set.
    /// </summary>
    /// <param name="apiKey">Your OpenAI API key.</param>
    /// <param name="model">Model ID to use; defaults to <c>gpt-4o</c>.</param>
    public WeaveLLMBuilder AddOpenAI(string apiKey, string? modelId = null)
    {
        modelId ??= "gpt-4o";
        Services.AddHttpClient("openai", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        });
        Services.AddSingleton<OpenAIChatModel>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai");
            return new OpenAIChatModel(apiKey, modelId, httpClient: http);
        });
        Services.AddSingleton<IStreamingChatModel>(sp =>
            new StreamingChatModelBridge(sp.GetRequiredService<OpenAIChatModel>()));
        Services.AddSingleton<IChatModel>(sp =>
            sp.GetRequiredService<IStreamingChatModel>());
        Services.AddSingleton<IEmbeddingModel>(sp =>
            new EmbeddingModelBridge(sp.GetRequiredService<OpenAIChatModel>()));
        return this;
    }

    /// <summary>
    /// Reads OpenAI configuration from <c>WeaveLLM:OpenAI:ApiKey</c> and optional <c>WeaveLLM:OpenAI:ModelId</c>.
    /// Throws <see cref="InvalidOperationException"/> if the API key is absent.
    /// </summary>
    public WeaveLLMBuilder AddOpenAI(IConfiguration config) =>
        AddOpenAI(
            apiKey: config["WeaveLLM:OpenAI:ApiKey"]
                ?? throw new InvalidOperationException("WeaveLLM:OpenAI:ApiKey is required in configuration."),
            modelId: config["WeaveLLM:OpenAI:ModelId"]);

    // ── Anthropic ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers AnthropicChatModel as <see cref="IStreamingChatModel"/> and <see cref="IChatModel"/> (singleton).
    /// Configures a named HttpClient <c>"anthropic"</c> with the API key and version headers.
    /// </summary>
    /// <param name="apiKey">Your Anthropic API key.</param>
    /// <param name="model">Model ID to use; defaults to <c>claude-sonnet-4-5</c>.</param>
    public WeaveLLMBuilder AddAnthropic(string apiKey, string? modelId = null)
    {
        modelId ??= "claude-sonnet-4-5";
        Services.AddHttpClient("anthropic", client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        });
        Services.AddSingleton<AnthropicChatModel>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic");
            return new AnthropicChatModel(apiKey, modelId, httpClient: http);
        });
        Services.AddSingleton<IStreamingChatModel>(sp =>
            new StreamingChatModelBridge(sp.GetRequiredService<AnthropicChatModel>()));
        Services.AddSingleton<IChatModel>(sp =>
            sp.GetRequiredService<IStreamingChatModel>());
        return this;
    }

    /// <summary>
    /// Reads Anthropic configuration from <c>WeaveLLM:Anthropic:ApiKey</c> and optional <c>WeaveLLM:Anthropic:ModelId</c>.
    /// Throws <see cref="InvalidOperationException"/> if the API key is absent.
    /// </summary>
    public WeaveLLMBuilder AddAnthropic(IConfiguration config) =>
        AddAnthropic(
            apiKey: config["WeaveLLM:Anthropic:ApiKey"]
                ?? throw new InvalidOperationException("WeaveLLM:Anthropic:ApiKey is required in configuration."),
            modelId: config["WeaveLLM:Anthropic:ModelId"]);

    // ── Ollama ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers OllamaChatModel as <see cref="IStreamingChatModel"/>, <see cref="IChatModel"/>,
    /// and <see cref="IEmbeddingModel"/> (singleton). Configures a named HttpClient <c>"ollama"</c>.
    /// </summary>
    /// <param name="baseUrl">The Ollama server base URL; defaults to <c>http://localhost:11434</c>.</param>
    /// <param name="model">Model ID to use; defaults to <c>llama3</c>.</param>
    public WeaveLLMBuilder AddOllama(string baseUrl = "http://localhost:11434", string? modelId = null)
    {
        modelId ??= "llama3";
        Services.AddHttpClient("ollama", client =>
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"));
        Services.AddSingleton<OllamaChatModel>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama");
            return new OllamaChatModel(baseUrl, modelId, httpClient: http);
        });
        Services.AddSingleton<IStreamingChatModel>(sp =>
            new StreamingChatModelBridge(sp.GetRequiredService<OllamaChatModel>()));
        Services.AddSingleton<IChatModel>(sp =>
            sp.GetRequiredService<IStreamingChatModel>());
        Services.AddSingleton<IEmbeddingModel>(sp =>
            new EmbeddingModelBridge(sp.GetRequiredService<OllamaChatModel>()));
        return this;
    }

    /// <summary>
    /// Reads Ollama configuration from <c>WeaveLLM:Ollama:BaseUrl</c> and optional <c>WeaveLLM:Ollama:ModelId</c>.
    /// </summary>
    public WeaveLLMBuilder AddOllama(IConfiguration config) =>
        AddOllama(
            config["WeaveLLM:Ollama:BaseUrl"] ?? "http://localhost:11434",
            config["WeaveLLM:Ollama:ModelId"]);

    // ── HuggingFace ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers HuggingFaceChatModel as <see cref="IChatModel"/> and <see cref="IEmbeddingModel"/> (singleton).
    /// HuggingFace is request/response only — streaming is not natively supported.
    /// </summary>
    /// <param name="apiKey">Your HuggingFace API key.</param>
    /// <param name="modelId">The HuggingFace model repository ID (e.g., <c>mistralai/Mistral-7B-Instruct-v0.1</c>).</param>
    public WeaveLLMBuilder AddHuggingFace(string apiKey, string modelId)
    {
        Services.AddSingleton<HuggingFaceChatModel>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new HuggingFaceChatModel(apiKey, modelId, httpClient: http);
        });
        Services.AddSingleton<IChatModel>(sp =>
            new StreamingChatModelBridge(sp.GetRequiredService<HuggingFaceChatModel>()));
        Services.AddSingleton<IEmbeddingModel>(sp =>
            new EmbeddingModelBridge(sp.GetRequiredService<HuggingFaceChatModel>()));
        return this;
    }

    /// <summary>
    /// Reads HuggingFace configuration from <c>WeaveLLM:HuggingFace:ApiKey</c> and <c>WeaveLLM:HuggingFace:ModelId</c>.
    /// Throws <see cref="InvalidOperationException"/> if either value is absent.
    /// </summary>
    public WeaveLLMBuilder AddHuggingFace(IConfiguration config) =>
        AddHuggingFace(
            config["WeaveLLM:HuggingFace:ApiKey"]
                ?? throw new InvalidOperationException("WeaveLLM:HuggingFace:ApiKey is required in configuration."),
            config["WeaveLLM:HuggingFace:ModelId"]
                ?? throw new InvalidOperationException("WeaveLLM:HuggingFace:ModelId is required in configuration."));

    // ── Memory ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a shared <see cref="InMemoryStore"/> singleton as both <see cref="IMemoryStore"/>
    /// and <see cref="IVectorStore"/> so the same instance backs both interfaces.
    /// </summary>
    public WeaveLLMBuilder AddInMemoryMemory()
    {
        Services.AddSingleton<MemoryInMemoryStore>();
        Services.AddSingleton<IMemoryStore>(sp => sp.GetRequiredService<MemoryInMemoryStore>());
        Services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<MemoryInMemoryStore>());
        return this;
    }

    // ── Tools ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a <see cref="ToolRegistry"/> singleton as <see cref="IToolRegistry"/>.
    /// The optional <paramref name="configure"/> action runs after creation to pre-register tools.
    /// </summary>
    /// <param name="configure">Optional action to register tools (e.g., <c>r => r.RegisterFromObject(new CalculatorTool())</c>).</param>
    public WeaveLLMBuilder AddToolRegistry(Action<IToolRegistry>? configure = null)
    {
        Services.AddSingleton<IToolRegistry>(sp =>
        {
            var registry = new ToolRegistry();
            configure?.Invoke(registry);
            return registry;
        });
        return this;
    }

    // ── Agents ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="ReActAgent"/> as <see cref="IAgent"/> (singleton) using the
    /// registered <see cref="IChatModel"/> and <see cref="IToolRegistry"/>.
    /// </summary>
    /// <param name="maxSteps">Maximum reasoning steps before the agent aborts with <c>MaxStepsExceeded</c>.</param>
    public WeaveLLMBuilder AddReActAgent(int maxSteps = 10)
    {
        Services.AddSingleton<IAgent>(sp => new ReActAgent(
            sp.GetRequiredService<IChatModel>(),
            sp.GetRequiredService<IToolRegistry>(),
            maxSteps: maxSteps));
        return this;
    }

    /// <summary>
    /// Registers <see cref="PlanAndExecuteAgent"/> as a keyed <see cref="IAgent"/> singleton
    /// under the key <c>"plan-execute"</c>. Both the planner and executor use the registered
    /// <see cref="IChatModel"/>; inject a second model via the service provider if you need
    /// separate planner/executor models.
    /// </summary>
    /// <param name="maxSteps">Maximum total execution steps before the agent aborts.</param>
    public WeaveLLMBuilder AddPlanAndExecuteAgent(int maxSteps = 20)
    {
        Services.AddKeyedSingleton<IAgent>("plan-execute", (sp, _) => new PlanAndExecuteAgent(
            sp.GetRequiredService<IChatModel>(),
            sp.GetRequiredService<IChatModel>(),
            sp.GetRequiredService<IToolRegistry>(),
            maxSteps: maxSteps));
        return this;
    }

    // ── Prompt Templates ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all four <see cref="PromptTemplateLibrary"/> templates as keyed <see cref="IPromptTemplate"/>
    /// singletons. Each template is keyed by its <see cref="IPromptTemplate.Name"/>.
    /// Resolve with <c>sp.GetRequiredKeyedService&lt;IPromptTemplate&gt;("react_system_prompt")</c>.
    /// </summary>
    public WeaveLLMBuilder AddPromptTemplateLibrary()
    {
        Services.AddKeyedSingleton<IPromptTemplate>(
            PromptTemplateLibrary.ReActSystemPrompt.Name,
            PromptTemplateLibrary.ReActSystemPrompt);
        Services.AddKeyedSingleton<IPromptTemplate>(
            PromptTemplateLibrary.RagQueryPrompt.Name,
            PromptTemplateLibrary.RagQueryPrompt);
        Services.AddKeyedSingleton<IPromptTemplate>(
            PromptTemplateLibrary.SummarisationPrompt.Name,
            PromptTemplateLibrary.SummarisationPrompt);
        Services.AddKeyedSingleton<IPromptTemplate>(
            PromptTemplateLibrary.CritiquePrompt.Name,
            PromptTemplateLibrary.CritiquePrompt);
        return this;
    }

    // ── Options ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Binds <see cref="WeaveLLMOptions"/> from the <c>WeaveLLM</c> configuration section,
    /// enables data-annotation validation, and validates on application start.
    /// </summary>
    /// <param name="configuration">The application configuration root.</param>
    public WeaveLLMBuilder AddWeaveLLMOptions(IConfiguration configuration)
    {
        Services.AddOptions<WeaveLLMOptions>()
            .Bind(configuration.GetSection("WeaveLLM"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return this;
    }

    // ── Health Checks ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="LlmProviderHealthCheck"/> with the ASP.NET Core health check system.
    /// The check pings the registered <see cref="IChatModel"/> and classifies the result by latency.
    /// </summary>
    /// <param name="checkName">
    /// The health check name shown in the <c>/healthz</c> response. Defaults to <c>"llm-provider"</c>.
    /// </param>
    public WeaveLLMBuilder AddWeaveLLMHealthChecks(string checkName = "llm-provider")
    {
        Services.AddHealthChecks()
            .AddCheck<LlmProviderHealthCheck>(checkName);
        return this;
    }

    // ── RAG ───────────────────────────────────────────────────────────────────────

    /// <summary>Registers the default RAG pipeline as <see cref="WeaveLLM.Core.RAG.IRagPipeline"/> (scoped).</summary>
    public WeaveLLMBuilder AddRagPipeline()
    {
        Services.AddScoped<WeaveLLM.Core.RAG.IRagPipeline, WeaveLLM.Core.RAG.DefaultRagPipeline>();
        return this;
    }

    /// <summary>Adds a named HttpClient <c>"weavellm"</c> with optional per-request configuration.</summary>
    public WeaveLLMBuilder WithHttpClient(Action<HttpClient>? configure = null)
    {
        Services.AddHttpClient("weavellm", client => configure?.Invoke(client));
        return this;
    }
}

/// <summary>
/// Extension methods for registering WeaveLLM with an <see cref="IServiceCollection"/>.
/// </summary>
public static class WeaveLLMServiceExtensions
{
    /// <summary>
    /// Registers base WeaveLLM services (logging, HttpClient factory) and returns a
    /// <see cref="WeaveLLMBuilder"/> for fluent provider configuration.
    /// </summary>
    public static WeaveLLMBuilder AddWeaveLLM(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddHttpClient();
        return new WeaveLLMBuilder(services);
    }

    /// <summary>
    /// Reads provider configuration from <paramref name="configuration"/> and auto-registers
    /// any provider whose key is present. Providers: <c>WeaveLLM:OpenAI:ApiKey</c>,
    /// <c>WeaveLLM:Anthropic:ApiKey</c>, <c>WeaveLLM:Ollama:BaseUrl</c>,
    /// <c>WeaveLLM:HuggingFace:ApiKey</c>.
    /// </summary>
    public static WeaveLLMBuilder AddWeaveLLM(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddWeaveLLM();

        if (!string.IsNullOrEmpty(configuration["WeaveLLM:OpenAI:ApiKey"]))
            builder.AddOpenAI(configuration);

        if (!string.IsNullOrEmpty(configuration["WeaveLLM:Anthropic:ApiKey"]))
            builder.AddAnthropic(configuration);

        if (!string.IsNullOrEmpty(configuration["WeaveLLM:Ollama:BaseUrl"])
            || !string.IsNullOrEmpty(configuration["WeaveLLM:Ollama:ModelId"]))
            builder.AddOllama(configuration);

        if (!string.IsNullOrEmpty(configuration["WeaveLLM:HuggingFace:ApiKey"]))
            builder.AddHuggingFace(configuration);

        return builder;
    }
}

// ── Bridges ───────────────────────────────────────────────────────────────────
// These adapters bridge WeaveLLM.Core.Providers.* (implemented by concrete providers)
// to WeaveLLM.Core.Models.* (consumed by agents and chains). They are DI-internal only.

/// <summary>
/// Adapts a <see cref="ProviderIChatModel"/> to the agent-facing
/// <see cref="IStreamingChatModel"/> interface. Maps <c>ProviderName → ProviderId</c>.
/// </summary>
internal sealed class StreamingChatModelBridge(ProviderIChatModel inner) : IStreamingChatModel
{
    /// <inheritdoc/>
    public string ModelId => inner.ModelId;

    /// <inheritdoc/>
    public string ProviderId => inner.ProviderName;

    /// <inheritdoc/>
    public Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
        => inner.ChatAsync(messages, options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
        => inner.StreamChatAsync(messages, options, cancellationToken);
}

/// <summary>
/// Adapts a <see cref="ProviderIEmbeddingModel"/> to the agent-facing
/// <see cref="IEmbeddingModel"/> interface. Maps <c>ProviderName → ProviderId</c>.
/// </summary>
internal sealed class EmbeddingModelBridge(ProviderIEmbeddingModel inner) : IEmbeddingModel
{
    /// <inheritdoc/>
    public string ModelId => inner.ModelId;

    /// <inheritdoc/>
    public string ProviderId => inner.ProviderName;

    /// <inheritdoc/>
    public Task<ChainResult<float[]>> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
        => inner.EmbedAsync(text, cancellationToken);

    /// <inheritdoc/>
    public Task<ChainResult<float[][]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
        => inner.EmbedBatchAsync(texts, cancellationToken);
}
