using FluentAssertions;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;
using Xunit;
using Sut = WeaveLLM.Core.Middleware.RetryMiddleware<string, string>;

namespace WeaveLLM.Core.Tests.Middleware;

public class RetryMiddlewareTests
{
    private static ChainContext Context() => ChainContext.Create();
    private static readonly TimeSpan FastDelay = TimeSpan.FromMilliseconds(1);

    private static ChainDelegate<string, string> AlwaysSuccess() =>
        (_, _, _) => Task.FromResult(ChainResult<string>.Success("ok"));

    private static ChainDelegate<string, string> FailThenSucceed(int failCount, string code)
    {
        int calls = 0;
        return (_, _, _) =>
        {
            calls++;
            return Task.FromResult(calls <= failCount
                ? ChainResult<string>.Failure($"fail #{calls}", code)
                : ChainResult<string>.Success("recovered"));
        };
    }

    [Fact]
    public async Task InvokeAsync_FirstCallSucceeds_ReturnsSuccess()
    {
        var sut = new Sut(maxRetries: 3, initialDelay: FastDelay);

        var result = await sut.InvokeAsync("input", Context(), AlwaysSuccess());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_NonRetryableError_ReturnsImmediately()
    {
        int callCount = 0;
        ChainDelegate<string, string> next = (_, _, _) =>
        {
            callCount++;
            return Task.FromResult(ChainResult<string>.Failure("boom", "PROVIDER_ERROR"));
        };

        var sut = new Sut(maxRetries: 3, initialDelay: FastDelay);
        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PROVIDER_ERROR");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_RateLimitedError_RetriesUpToMaxRetries()
    {
        int callCount = 0;
        ChainDelegate<string, string> next = (_, _, _) =>
        {
            callCount++;
            return Task.FromResult(ChainResult<string>.Failure("rate limited", "RATE_LIMITED"));
        };

        var sut = new Sut(maxRetries: 3, initialDelay: FastDelay);
        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("RATE_LIMITED");
        callCount.Should().Be(4); // 1 initial + 3 retries
    }

    [Fact]
    public async Task InvokeAsync_TimeoutError_RetriesUpToMaxRetries()
    {
        int callCount = 0;
        ChainDelegate<string, string> next = (_, _, _) =>
        {
            callCount++;
            return Task.FromResult(ChainResult<string>.Failure("timed out", "TIMEOUT"));
        };

        var sut = new Sut(maxRetries: 2, initialDelay: FastDelay);
        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsFailure.Should().BeTrue();
        callCount.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task InvokeAsync_SucceedsAfterRetries_ReturnsSuccess()
    {
        var sut = new Sut(maxRetries: 3, initialDelay: FastDelay);

        var result = await sut.InvokeAsync("input", Context(), FailThenSucceed(2, "RATE_LIMITED"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("recovered");
    }

    [Fact]
    public async Task InvokeAsync_ZeroMaxRetries_DoesNotRetry()
    {
        int callCount = 0;
        ChainDelegate<string, string> next = (_, _, _) =>
        {
            callCount++;
            return Task.FromResult(ChainResult<string>.Failure("rate limited", "RATE_LIMITED"));
        };

        var sut = new Sut(maxRetries: 0, initialDelay: FastDelay);
        var result = await sut.InvokeAsync("input", Context(), next);

        result.IsFailure.Should().BeTrue();
        callCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_NegativeMaxRetries_Throws()
    {
        var act = () => new WeaveLLM.Core.Middleware.RetryMiddleware<string, string>(maxRetries: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
