using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WeaveLLM.Core.Providers;
using WeaveLLM.Extensions.DependencyInjection;
using Xunit;

namespace WeaveLLM.Providers.Tests.DI;

public class WeaveLLMServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddOpenAIChatModel_WithConfig_RegistersIChatModel()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new() { ["WeaveLLM:OpenAI:ApiKey"] = "sk-test" });

        services.AddOpenAIChatModel(config);

        services.Any(d => d.ServiceType == typeof(IChatModel)).Should().BeTrue();
    }

    [Fact]
    public void AddOpenAIChatModel_WithConfig_BindsApiKeyFromConfig()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new() { ["WeaveLLM:OpenAI:ApiKey"] = "sk-test-key-123" });
        services.AddOpenAIChatModel(config);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;

        opts.ApiKey.Should().Be("sk-test-key-123");
    }

    [Fact]
    public void AddAnthropicChatModel_WithConfig_RegistersIChatModel()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new() { ["WeaveLLM:Anthropic:ApiKey"] = "ant-test" });

        services.AddAnthropicChatModel(config);

        services.Any(d => d.ServiceType == typeof(IChatModel)).Should().BeTrue();
    }
}
