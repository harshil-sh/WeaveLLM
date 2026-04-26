using Microsoft.Extensions.Logging;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Chains;

/// <summary>
/// ASP.NET-style middleware for chains. Cross-cutting concerns go here:
/// caching, rate limiting, logging, retry, auth, cost tracking.
/// </summary>
public interface IChainMiddleware<TInput, TOutput>
{
    Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Delegate representing the next step in the middleware pipeline.
/// </summary>
public delegate Task<ChainResult<TOutput>> ChainDelegate<TInput, TOutput>(
    TInput input,
    ChainContext context,
    CancellationToken cancellationToken);

/// <summary>
/// Built-in middleware: retries with exponential backoff using Polly.
/// </summary>
public class RetryMiddleware<TInput, TOutput>(int maxRetries = 3, double backoffSeconds = 1.5)
    : IChainMiddleware<TInput, TOutput>
{
    public async Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                attempt++;
                return await next(input, context, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(backoffSeconds, attempt));
                context.Logger?.LogWarning("Chain {Name} failed attempt {Attempt}/{Max}. Retrying in {Delay}s. Error: {Error}",
                    context.ChainName, attempt, maxRetries, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}

/// <summary>
/// Built-in middleware: in-memory caching by input hash.
/// </summary>
public class CacheMiddleware<TInput, TOutput>(TimeSpan? ttl = null)
    : IChainMiddleware<TInput, TOutput>
{
    private readonly Dictionary<string, (TOutput Value, DateTimeOffset Expires)> _cache = new();
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

    public async Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default)
    {
        var key = $"{context.ChainName}:{input?.GetHashCode()}";
        if (_cache.TryGetValue(key, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            context.Metadata["cache_hit"] = true;
            return ChainResult<TOutput>.Success(cached.Value);
        }

        var result = await next(input, context, cancellationToken);
        if (result.IsSuccess)
            _cache[key] = (result.Value!, DateTimeOffset.UtcNow.Add(_ttl));

        return result;
    }
}
