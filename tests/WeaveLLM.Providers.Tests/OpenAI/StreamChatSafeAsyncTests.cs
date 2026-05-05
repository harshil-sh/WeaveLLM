using System.Net;
using System.Text;
using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Providers.OpenAI;
using Xunit;

namespace WeaveLLM.Providers.Tests.OpenAI;

public class StreamChatSafeAsyncTests
{
    private static readonly IReadOnlyList<Message> TestMessages = [Message.User("Hello")];

    [Fact]
    public async Task StreamChatSafeAsync_HappyPath_YieldsSuccessTokens()
    {
        const string sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n" +
            "data: [DONE]\n";
        var model = BuildModel(HttpStatusCode.OK, sseBody);

        var results = new List<ChainResult<string>>();
        await foreach (var item in model.StreamChatSafeAsync(TestMessages))
            results.Add(item);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results.Select(r => r.Value).Should().Equal("Hello", " world");
    }

    [Fact]
    public async Task StreamChatSafeAsync_MidStreamException_YieldsFailureThenStops()
    {
        var handler = new ThrowingStreamHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var model = new OpenAIChatModel("test-key", httpClient: http);

        var results = new List<ChainResult<string>>();
        await foreach (var item in model.StreamChatSafeAsync(TestMessages))
            results.Add(item);

        results.Should().HaveCount(1);
        results[0].IsFailure.Should().BeTrue();
        results[0].Error!.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task StreamChatSafeAsync_Cancellation_YieldsCancelledFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var model = BuildModel(HttpStatusCode.OK, string.Empty);

        var results = new List<ChainResult<string>>();
        await foreach (var item in model.StreamChatSafeAsync(TestMessages, cancellationToken: cts.Token))
            results.Add(item);

        results.Should().HaveCount(1);
        results[0].IsFailure.Should().BeTrue();
        results[0].Error!.Code.Should().Be("CANCELLED");
    }

    private static OpenAIChatModel BuildModel(HttpStatusCode statusCode, string body)
    {
        var handler = new FakeHandler(statusCode, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        return new OpenAIChatModel("test-key", httpClient: http);
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            });
    }

    private sealed class ThrowingStreamHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingStream())
            });
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated mid-stream error");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            throw new IOException("Simulated mid-stream error");

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
