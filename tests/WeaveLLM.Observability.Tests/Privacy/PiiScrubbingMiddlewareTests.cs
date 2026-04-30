using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;
using WeaveLLM.Observability.Privacy;
using Xunit;

namespace WeaveLLM.Observability.Tests.Privacy;

public class PiiScrubbingMiddlewareTests
{
    private static ChainContext Context() => ChainContext.Create();

    private static PiiScrubbingMiddleware<string, string> BuildMiddleware()
    {
        var logger = Substitute.For<ILogger<PiiScrubbingMiddleware<string, string>>>();
        return new PiiScrubbingMiddleware<string, string>(logger);
    }

    [Fact]
    public async Task InvokeAsync_PassesInputToNextUnchanged_EvenWhenContainsPii()
    {
        var sut = BuildMiddleware();
        string? capturedInput = null;
        ChainDelegate<string, string> next = (input, _, _) =>
        {
            capturedInput = input;
            return Task.FromResult(ChainResult<string>.Success("ok"));
        };

        await sut.InvokeAsync("email: alice@example.com", Context(), next);

        capturedInput.Should().Be("email: alice@example.com");
    }

    [Fact]
    public async Task InvokeAsync_ReturnsSuccessResult_WhenNextSucceeds()
    {
        var sut = BuildMiddleware();
        ChainDelegate<string, string> next = (_, _, _) =>
            Task.FromResult(ChainResult<string>.Success("response"));

        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("response");
    }

    [Fact]
    public async Task InvokeAsync_PassesThroughFailure_WhenNextFails()
    {
        var sut = BuildMiddleware();
        ChainDelegate<string, string> next = (_, _, _) =>
            Task.FromResult(ChainResult<string>.Failure("model error", "PROVIDER_ERROR"));

        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PROVIDER_ERROR");
    }
}
