#nullable enable
using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Providers.OpenAI;
using Xunit;

namespace WeaveLLM.IntegrationTests.Providers;

public class OpenAiIntegrationTests
{
    private static OpenAIChatModel CreateModel()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OPENAI_API_KEY environment variable is not set. Export it before running integration tests.");
        return new OpenAIChatModel(apiKey, "gpt-4o-mini");
    }

    [IntegrationFact]
    public async Task OpenAI_ChatAsync_ReturnsRealResponse()
    {
        var model = CreateModel();
        var messages = new[] { Message.User("Reply with only the word: pong") };

        var result = await model.ChatAsync(messages);

        result.IsSuccess.Should().BeTrue(because: result.Error?.Message ?? string.Empty);
        result.Value!.Content.ToLower().Should().Contain("pong");
    }

    [IntegrationFact]
    public async Task OpenAI_StreamChatAsync_YieldsChunks()
    {
        var model = CreateModel();
        var messages = new[] { Message.User("Say hello briefly.") };

        var chunks = new List<string>();
        await foreach (var chunk in model.StreamChatAsync(messages))
            chunks.Add(chunk);

        var joined = string.Join(string.Empty, chunks);
        joined.Should().NotBeEmpty();
    }
}
