#nullable enable
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Middleware;

/// <summary>
/// Middleware that enforces a maximum request rate using a token-bucket algorithm.
/// Each incoming request consumes one token. A background timer refills one token every
/// <c>60 / requestsPerMinute</c> seconds. When no tokens are available the request is
/// rejected immediately — it never blocks the caller.
/// </summary>
/// <remarks>
/// Dispose the middleware when the owning pipeline is torn down to stop the background timer.
/// </remarks>
/// <typeparam name="TInput">The chain input type.</typeparam>
/// <typeparam name="TOutput">The chain output type.</typeparam>
public sealed class RateLimitingMiddleware<TInput, TOutput>
    : IChainMiddleware<TInput, TOutput>, IDisposable
{
    private readonly int _requestsPerMinute;
    private readonly SemaphoreSlim _semaphore;
    private readonly Timer _refillTimer;
    private volatile bool _disposed;

    /// <summary>
    /// Initialises a new <see cref="RateLimitingMiddleware{TInput,TOutput}"/> with the given
    /// request rate. The token bucket starts full and refills one token at the steady rate.
    /// </summary>
    /// <param name="requestsPerMinute">
    /// Maximum allowed requests per minute. Must be greater than zero.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="requestsPerMinute"/> is zero or negative.
    /// </exception>
    public RateLimitingMiddleware(int requestsPerMinute)
    {
        if (requestsPerMinute <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(requestsPerMinute), requestsPerMinute, "Must be greater than zero.");

        _requestsPerMinute = requestsPerMinute;
        _semaphore = new SemaphoreSlim(requestsPerMinute, requestsPerMinute);

        var refillInterval = TimeSpan.FromSeconds(60.0 / requestsPerMinute);
        _refillTimer = new Timer(RefillToken, null, refillInterval, refillInterval);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Attempts a non-blocking token acquisition. Returns a <c>RATE_LIMITED</c> failure
    /// immediately if no tokens are available; otherwise forwards to <paramref name="next"/>.
    /// </remarks>
    public Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default)
    {
        if (!_semaphore.Wait(0))
            return Task.FromResult(ChainResult<TOutput>.Failure(
                $"Rate limit of {_requestsPerMinute} req/min exceeded. Reduce request rate.",
                "RATE_LIMITED"));

        return next(input, context, cancellationToken);
    }

    /// <summary>Stops the refill timer and releases the semaphore.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refillTimer.Dispose();
        _semaphore.Dispose();
    }

    private void RefillToken(object? _)
    {
        if (_disposed) return;
        // Only release if the bucket is not already full.
        if (_semaphore.CurrentCount < _requestsPerMinute)
        {
            try { _semaphore.Release(); }
            catch (ObjectDisposedException) { }
        }
    }
}
