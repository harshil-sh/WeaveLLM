using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Providers;
using WeaveLLM.Core.RAG;
using WeaveLLM.Core.Tools;
using WeaveLLM.Providers.Anthropic;
using WeaveLLM.Providers.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WeaveLLM.Extensions.DependencyInjection;

/// <summary>
/// Fluent builder for registering WeaveLLM services.
/// Designed for .NET DI — works with ASP.NET Core, Worker Services, Aspire.
///
/// Usage:
///   builder.Services
///       .AddWeaveLLM()
///       .AddOpenAI(cfg["OpenAI:ApiKey"]!)
///       .AddAnthropicFallback(cfg["Anthropic:ApiKey"]!)
///       .AddInMemoryMemory()
///       .AddReActAgent();
/// </summary>
public sealed class WeaveLLMBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public WeaveLLMBuilder AddOpenAI(string apiKey, string modelId = "gpt-4o")
    {
        Services.AddSingleton<IChatModel>(new OpenAIChatModel(apiKey, modelId));
        Services.AddSingleton<ILanguageModel>(sp => sp.GetRequiredService<IChatModel>());
        return this;
    }

    public WeaveLLMBuilder AddAnthropic(string apiKey, string modelId = "claude-sonnet-4-5")
    {
        Services.AddSingleton<IChatModel>(new AnthropicChatModel(apiKey, modelId));
        Services.AddSingleton<ILanguageModel>(sp => sp.GetRequiredService<IChatModel>());
        return this;
    }

    public WeaveLLMBuilder AddInMemoryMemory()
    {
        Services.AddSingleton<IMemoryStore, InMemoryStore>();
        return this;
    }

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

    public WeaveLLMBuilder AddReActAgent(string agentName = "default")
    {
        Services.AddTransient<IAgent>(sp => new ReActAgent(
            sp.GetRequiredService<IChatModel>(),
            sp.GetRequiredService<IToolRegistry>(),
            agentName));
        return this;
    }

    public WeaveLLMBuilder AddRagPipeline()
    {
        Services.AddScoped<IRagPipeline, DefaultRagPipeline>();
        return this;
    }

    public WeaveLLMBuilder WithHttpClient(Action<HttpClient>? configure = null)
    {
        Services.AddHttpClient("weavellm", client => configure?.Invoke(client));
        return this;
    }
}

public static class WeaveLLMServiceExtensions
{
    /// <summary>
    /// Entry point for all WeaveLLM registrations.
    /// </summary>
    public static WeaveLLMBuilder AddWeaveLLM(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddHttpClient();
        return new WeaveLLMBuilder(services);
    }

    /// <summary>
    /// Convenience overload that reads config from IConfiguration.
    /// Expects: WeaveLLM:OpenAI:ApiKey, WeaveLLM:Anthropic:ApiKey
    /// </summary>
    public static WeaveLLMBuilder AddWeaveLLM(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddWeaveLLM();

        var openAiKey = configuration["WeaveLLM:OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(openAiKey))
            builder.AddOpenAI(openAiKey, configuration["WeaveLLM:OpenAI:ModelId"] ?? "gpt-4o");

        var anthropicKey = configuration["WeaveLLM:Anthropic:ApiKey"];
        if (!string.IsNullOrEmpty(anthropicKey))
            builder.AddAnthropic(anthropicKey, configuration["WeaveLLM:Anthropic:ModelId"] ?? "claude-sonnet-4-5");

        return builder;
    }
}
