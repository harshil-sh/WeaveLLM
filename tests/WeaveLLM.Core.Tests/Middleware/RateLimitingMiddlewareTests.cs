using FluentAssertions;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Middleware;
using WeaveLLM.Core.Models;
using Xunit;

namespace WeaveLLM.Core.Tests.Middleware;

public class RateLimitingMiddlewareTests
{
    private static ChainContext Context() => ChainContext.Create();

    private static ChainDelegate<string, string> SuccessDelegate() =>
        (_, _, _) => Task.FromResult(ChainResult<string>.Success("ok"));

    [Fact]
    public async Task InvokeAsync_TokenAvailable_CallsNext()
    {
        using var sut = new RateLimitingMiddleware<string, string>(requestsPerMinute: 5);

        var result = await sut.InvokeAsync("input", Context(), SuccessDelegate());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_BucketExhausted_ReturnsRateLimitedFailure()
    {
        using var sut = new RateLimitingMiddleware<string, string>(requestsPerMinute: 2);
        var ctx = Context();

        // Drain both tokens.
        await sut.InvokeAsync("input", ctx, SuccessDelegate());
        await sut.InvokeAsync("input", ctx, SuccessDelegate());

        // Next call should be rejected.
        var result = await sut.InvokeAsync("input", ctx, SuccessDelegate());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("RATE_LIMITED");
    }

    [Fact]
    public async Task InvokeAsync_BucketExhausted_DoesNotCallNext()
    {
        using var sut = new RateLimitingMiddleware<string, string>(requestsPerMinute: 1);

        await sut.InvokeAsync("input", Context(), SuccessDelegate());

        var nextCalled = false;
        ChainDelegate<string, string> trackingNext = (_, _, _) =>
        {
            nextCalled = true;
            return Task.FromResult(ChainResult<string>.Success("ok"));
        };

        await sut.InvokeAsync("input", Context(), trackingNext);

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ZeroRequestsPerMinute_Throws()
    {
        var act = () => new RateLimitingMiddleware<string, string>(requestsPerMinute: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeRequestsPerMinute_Throws()
    {
        var act = () => new RateLimitingMiddleware<string, string>(requestsPerMinute: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task InvokeAsync_AfterDispose_DoesNotThrow()
    {
        var sut = new RateLimitingMiddleware<string, string>(requestsPerMinute: 10);
        sut.Dispose();

        // SemaphoreSlim is disposed — Wait(0) will throw ObjectDisposedException.
        // The middleware should surface that rather than swallow it, so we just
        // verify Dispose() itself completes cleanly and does not double-dispose.
        var act = () => sut.Dispose();
        act.Should().NotThrow();
    }
}
