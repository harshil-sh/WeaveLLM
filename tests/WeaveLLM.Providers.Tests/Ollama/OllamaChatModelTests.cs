using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Providers.Ollama;
using Xunit;

namespace WeaveLLM.Providers.Tests.Ollama;

public class OllamaChatModelTests
{
    private static readonly IReadOnlyList<Message> TestMessages = [Message.User("Hello")];

    private static OllamaChatModel BuildModel(HttpStatusCode statusCode, string body)
    {
        var handler = new FakeHandler(statusCode, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        return new OllamaChatModel(httpClient: http);
    }

    private static OllamaChatModel BuildConnectionRefusedModel()
    {
        var handler = new ConnectionRefusedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        return new OllamaChatModel(httpClient: http);
    }

    [Fact]
    public async Task ChatAsync_ReturnsProviderUnavailable_WhenConnectionRefused()
    {
        var model = BuildConnectionRefusedModel();

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PROVIDER_UNAVAILABLE");
    }

    [Fact]
    public async Task ChatAsync_ReturnsInvalidInput_WhenModelNotFound_404()
    {
        var model = BuildModel(HttpStatusCode.NotFound, """{"error":"model not found"}""");

        var result = await model.ChatAsync(TestMessages);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task EmbedAsync_ReturnsFloatArray_WhenApiReturns200()
    {
        const string json = """{"embedding":[0.1,0.2,0.3]}""";
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

    // Simulates the OS-level error that fires when Ollama is not running.
    // IsConnectionRefused checks ex.InnerException is SocketException with SocketError.ConnectionRefused.
    private sealed class ConnectionRefusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var socketEx = new SocketException((int)SocketError.ConnectionRefused);
            throw new HttpRequestException("Connection refused", socketEx);
        }
    }
}
