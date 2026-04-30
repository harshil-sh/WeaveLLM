using System.Net;
using System.Text;
using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Providers.HuggingFace;
using Xunit;

namespace WeaveLLM.Providers.Tests.HuggingFace;

public class HuggingFaceChatModelTests
{
    private static readonly IReadOnlyList<Message> TestMessages = [Message.User("Hello")];

    private static HuggingFaceChatModel BuildModel(HttpStatusCode statusCode, string body)
    {
        var handler = new FakeHandler(statusCode, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api-inference.huggingface.co/") };
        return new HuggingFaceChatModel("test-key", "distilgpt2", httpClient: http);
    }

    [Fact]
    public async Task ChatAsync_ReturnsSuccess_WhenApiReturnsValidJson()
    {
        const string json = """[{"generated_text":"Hello from HF"}]""";
        var model = BuildModel(HttpStatusCode.OK, json);

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Be("Hello from HF");
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
    public async Task ChatAsync_ReturnsModelLoading_WhenApiReturns503WithLoadingBody()
    {
        var model = BuildModel(HttpStatusCode.ServiceUnavailable, """{"error":"Model is loading"}""");

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("MODEL_LOADING");
    }

    [Fact]
    public async Task ChatAsync_ReturnsProviderError_WhenApiReturnsOtherFailure()
    {
        var model = BuildModel(HttpStatusCode.InternalServerError, """{"error":"Internal server error"}""");

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task StreamChatAsync_YieldsSingleChunk_WhenApiReturns200()
    {
        const string json = """[{"generated_text":"Stream chunk"}]""";
        var model = BuildModel(HttpStatusCode.OK, json);

        var tokens = new List<string>();
        await foreach (var token in model.StreamChatAsync(TestMessages))
            tokens.Add(token);

        tokens.Should().ContainSingle().Which.Should().Be("Stream chunk");
    }

    [Fact]
    public async Task EmbedAsync_ReturnsFirstRowOfMatrix_WhenApiReturns200()
    {
        const string json = """[[0.1, 0.2, 0.3],[0.4, 0.5, 0.6]]""";
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
