using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Providers.Anthropic;
using Xunit;

namespace WeaveLLM.Providers.Tests.Anthropic;

public class AnthropicChatModelTests
{
    private static (AnthropicChatModel model, FakeHandler handler) BuildModel(
        HttpStatusCode statusCode, string body)
    {
        var handler = new FakeHandler(statusCode, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        return (new AnthropicChatModel("test-key", httpClient: http), handler);
    }

    [Fact]
    public async Task ChatAsync_MapsSystemMessageToTopLevelField_NotInMessagesArray()
    {
        const string json = """{"content":[{"text":"OK"}],"usage":{"input_tokens":5,"output_tokens":2}}""";
        var (model, handler) = BuildModel(HttpStatusCode.OK, json);
        var messages = new List<Message>
        {
            Message.System("You are helpful."),
            Message.User("Hello")
        };

        await model.ChatAsync(messages);

        var body = JsonDocument.Parse(handler.CapturedRequestBody!).RootElement;
        body.GetProperty("system").GetString().Should().Be("You are helpful.");
        body.GetProperty("messages").EnumerateArray()
            .Should().NotContain(el => el.GetProperty("role").GetString() == "system");
    }

    [Fact]
    public async Task ChatAsync_ReturnsSuccess_WhenApiReturns200()
    {
        const string json = """
            {"content":[{"text":"Hello there"}],
             "usage":{"input_tokens":10,"output_tokens":5}}
            """;
        var (model, _) = BuildModel(HttpStatusCode.OK, json);

        var result = await model.ChatAsync([Message.User("Hi")]);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Be("Hello there");
        result.Value.Usage!.PromptTokens.Should().Be(10);
        result.Value.Usage.CompletionTokens.Should().Be(5);
    }

    [Fact]
    public async Task ChatAsync_ReturnsRateLimited_WhenApiReturns429()
    {
        var (model, _) = BuildModel(HttpStatusCode.TooManyRequests, """{"error":"rate limited"}""");

        var result = await model.ChatAsync([Message.User("Hi")]);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("RATE_LIMITED");
    }

    [Fact]
    public async Task StreamChatAsync_YieldsOnlyTextDeltaEvents()
    {
        // message_start and content_block_start events must be silently ignored;
        // only content_block_delta + text_delta carries actual text.
        const string sseBody =
            "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_01\"}}\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\" world\"}}\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"k\\\":\"}}\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var (model, _) = BuildModel(HttpStatusCode.OK, sseBody);

        var tokens = new List<string>();
        await foreach (var token in model.StreamChatAsync([Message.User("Hi")]))
            tokens.Add(token);

        tokens.Should().Equal("Hello", " world");
    }

    internal sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
