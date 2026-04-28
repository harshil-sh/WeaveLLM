using System.Net;
using System.Text;
using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Providers.OpenAI;
using Xunit;

namespace WeaveLLM.Providers.Tests.OpenAI;

public class OpenAIChatModelTests
{
    private static readonly IReadOnlyList<Message> TestMessages = [Message.User("Hello")];

    private static OpenAIChatModel BuildModel(HttpStatusCode statusCode, string body)
    {
        var handler = new FakeHandler(statusCode, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        return new OpenAIChatModel("test-key", httpClient: http);
    }

    [Fact]
    public async Task ChatAsync_ReturnsSuccess_WhenApiReturns200WithValidJson()
    {
        const string json = """
            {"choices":[{"message":{"content":"Hello there"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":10,"completion_tokens":5}}
            """;
        var model = BuildModel(HttpStatusCode.OK, json);

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Be("Hello there");
        result.Value.FinishReason.Should().Be("stop");
    }

    [Fact]
    public async Task ChatAsync_ReturnsInvalidInput_WhenApiReturns401()
    {
        var model = BuildModel(HttpStatusCode.Unauthorized, """{"error":"Unauthorized"}""");

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task ChatAsync_ReturnsRateLimited_WhenApiReturns429()
    {
        var model = BuildModel(HttpStatusCode.TooManyRequests, """{"error":"Rate limited"}""");

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("RATE_LIMITED");
    }

    [Fact]
    public async Task ChatAsync_ReturnsTimeout_WhenApiReturns503()
    {
        var model = BuildModel(HttpStatusCode.ServiceUnavailable, """{"error":"Service unavailable"}""");

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TIMEOUT");
    }

    [Fact]
    public async Task StreamChatAsync_YieldsTokens_WhenApiReturnsValidSse()
    {
        const string sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n" +
            "data: [DONE]\n";
        var model = BuildModel(HttpStatusCode.OK, sseBody);

        var tokens = new List<string>();
        await foreach (var token in model.StreamChatAsync(TestMessages))
            tokens.Add(token);

        tokens.Should().Equal("Hello", " world");
    }

    [Fact]
    public async Task EmbedAsync_ReturnsFloatArray_WhenApiReturns200()
    {
        const string json = """{"data":[{"embedding":[0.1,0.2,0.3]}]}""";
        var model = BuildModel(HttpStatusCode.OK, json);

        var result = await model.EmbedAsync("test text");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new float[] { 0.1f, 0.2f, 0.3f });
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
