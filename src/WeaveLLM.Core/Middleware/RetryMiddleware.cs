#nullable enable
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Middleware;

/// <summary>
/// Middleware that retries a chain invocation when the result carries a retryable error code.
/// Only <c>RATE_LIMITED</c> and <c>TIMEOUT</c> errors trigger a retry; all other failures are
/// returned to the caller immediately.
/// </summary>
/// <remarks>
/// <para>
/// Backoff formula: <c>initialDelay × 2^attempt</c>, where <c>attempt</c> is zero-based
/// (0 on the first retry, 1 on the second, …). With the defaults of <c>maxRetries = 3</c> and
/// <c>initialDelay = 1 s</c>, the delays are 1 s → 2 s → 4 s.
/// </para>
/// <para>
/// After all retries are exhausted the last failure <see cref="ChainResult{TOutput}"/> is
/// returned — no exception is thrown.
/// </para>
/// </remarks>
/// <typeparam name="TInput">The chain input type.</typeparam>
/// <typeparam name="TOutput">The chain output type.</typeparam>
public sealed class RetryMiddleware<TInput, TOutput>
    : IChainMiddleware<TInput, TOutput>
{
    private static readonly HashSet<string> RetryableCodes =
        new(StringComparer.OrdinalIgnoreCase) { "RATE_LIMITED", "TIMEOUT" };

    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;

    /// <summary>
    /// Initialises a new <see cref="RetryMiddleware{TInput,TOutput}"/>.
    /// </summary>
    /// <param name="maxRetries">
    /// Maximum number of additional attempts after the first failure. Must be ≥ 0.
    /// Defaults to <c>3</c>.
    /// </param>
    /// <param name="initialDelay">
    /// Base delay for the exponential backoff. Defaults to <c>1 second</c>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="maxRetries"/> is negative.
    /// </exception>
    public RetryMiddleware(int maxRetries = 3, TimeSpan? initialDelay = null)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxRetries), maxRetries, "Must be zero or greater.");

        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Invokes <paramref name="next"/> up to <c>maxRetries + 1</c> times. A retry is
    /// attempted only when the result is a failure whose
    /// <see cref="WeaveLLMError.Code"/> is <c>RATE_LIMITED</c> or <c>TIMEOUT</c>.
    /// </remarks>
    public async Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default)
    {
        ChainResult<TOutput>? lastResult = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            lastResult = await next(input, context, cancellationToken).ConfigureAwait(false);

            if (lastResult.IsSuccess)
                return lastResult;

            var code = lastResult.Error?.Code;
            bool retryable = code is not null && RetryableCodes.Contains(code);

            if (!retryable || attempt == _maxRetries)
                return lastResult;

            var delay = _initialDelay * Math.Pow(2, attempt);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return lastResult!;
    }
}
