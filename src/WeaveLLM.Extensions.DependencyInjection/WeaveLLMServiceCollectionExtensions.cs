#nullable enable
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WeaveLLM.Core.Providers;
using WeaveLLM.Providers.Anthropic;
using WeaveLLM.Providers.OpenAI;

[assembly: InternalsVisibleTo("WeaveLLM.Providers.Tests")]

namespace WeaveLLM.Extensions.DependencyInjection;

internal sealed class OpenAIOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "gpt-4o";
}

internal sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "claude-sonnet-4-6";
}

/// <summary>
/// Top-level <see cref="IServiceCollection"/> extension methods for registering individual WeaveLLM providers.
/// Use these when you want provider registration without the full <see cref="WeaveLLMBuilder"/> fluent API.
/// </summary>
public static class WeaveLLMServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OpenAIChatModel"/> as <see cref="IChatModel"/> (singleton), configures a named
    /// HttpClient <c>"weave-openai"</c> with authorization headers, and binds OpenAI options from
    /// <paramref name="configuration"/>. Integrates with Polly and named-client policies via
    /// <c>services.AddHttpClient("weave-openai")</c> before calling this method.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="sectionName">Configuration section path. Defaults to <c>"WeaveLLM:OpenAI"</c>.</param>
    public static IServiceCollection AddOpenAIChatModel(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "WeaveLLM:OpenAI")
    {
        services.Configure<OpenAIOptions>(configuration.GetSection(sectionName));
        services.AddHttpClient("weave-openai", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        });
        services.AddSingleton<IChatModel>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new OpenAIChatModel(opts.ApiKey, opts.ModelId,
                "https://api.openai.com/v1", factory, "weave-openai");
        });
        return services;
    }

    /// <summary>
    /// Registers <see cref="AnthropicChatModel"/> as <see cref="IChatModel"/> (singleton), configures a named
    /// HttpClient <c>"weave-anthropic"</c> with API key and version headers, and binds Anthropic options from
    /// <paramref name="configuration"/>. Integrates with Polly and named-client policies via
    /// <c>services.AddHttpClient("weave-anthropic")</c> before calling this method.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="sectionName">Configuration section path. Defaults to <c>"WeaveLLM:Anthropic"</c>.</param>
    public static IServiceCollection AddAnthropicChatModel(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "WeaveLLM:Anthropic")
    {
        services.Configure<AnthropicOptions>(configuration.GetSection(sectionName));
        services.AddHttpClient("weave-anthropic", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            client.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        });
        services.AddSingleton<IChatModel>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new AnthropicChatModel(opts.ApiKey, opts.ModelId, factory, "weave-anthropic");
        });
        return services;
    }
}
