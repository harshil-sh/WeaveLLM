using FluentAssertions;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;
using WeaveLLM.Observability.Tracing;
using Xunit;

namespace WeaveLLM.Observability.Tests.Tracing;

public class TracingMiddlewareTests
{
    private static ChainContext Context() => ChainContext.Create();

    [Fact]
    public async Task InvokeAsync_ReturnsSuccessResult_WhenNextSucceeds()
    {
        var sut = new TracingMiddleware<string, string>();
        ChainDelegate<string, string> next = (_, _, _) =>
            Task.FromResult(ChainResult<string>.Success("hello"));

        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public async Task InvokeAsync_ReturnsFailureResult_WhenNextFails()
    {
        var sut = new TracingMiddleware<string, string>();
        ChainDelegate<string, string> next = (_, _, _) =>
            Task.FromResult(ChainResult<string>.Failure("failed", "PROVIDER_ERROR"));

        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task InvokeAsync_PassesInputToNext_Unchanged()
    {
        var sut = new TracingMiddleware<string, string>();
        string? capturedInput = null;
        ChainDelegate<string, string> next = (input, _, _) =>
        {
            capturedInput = input;
            return Task.FromResult(ChainResult<string>.Success("ok"));
        };

        await sut.InvokeAsync("my-input", Context(), next);

        capturedInput.Should().Be("my-input");
    }

    [Fact]
    public async Task InvokeAsync_DoesNotThrow_WhenNoActivityListenerIsRegistered()
    {
        var sut = new TracingMiddleware<string, string>();
        ChainDelegate<string, string> next = (_, _, _) =>
            Task.FromResult(ChainResult<string>.Success("ok"));

        var act = async () => await sut.InvokeAsync("input", Context(), next);

        await act.Should().NotThrowAsync();
    }
}
