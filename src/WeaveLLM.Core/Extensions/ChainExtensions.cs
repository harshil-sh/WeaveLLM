#nullable enable
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Extensions;

/// <summary>
/// Fluent extension methods for composing and decorating <see cref="IChain{TInput,TOutput}"/> instances.
/// </summary>
public static class ChainExtensions
{
    /// <summary>
    /// Wraps <paramref name="chain"/> with <paramref name="middleware"/>, producing a new chain
    /// that passes every execution through the middleware before reaching the inner chain.
    /// </summary>
    /// <typeparam name="TInput">The chain's input type.</typeparam>
    /// <typeparam name="TOutput">The chain's output type.</typeparam>
    /// <param name="chain">The inner chain to decorate.</param>
    /// <param name="middleware">The middleware to apply.</param>
    /// <returns>A new <see cref="IChain{TInput,TOutput}"/> that executes middleware before the inner chain.</returns>
    public static IChain<TInput, TOutput> WithMiddleware<TInput, TOutput>(
        this IChain<TInput, TOutput> chain,
        IChainMiddleware<TInput, TOutput> middleware)
        => new MiddlewareChain<TInput, TOutput>(chain, middleware);

    /// <summary>
    /// Composes <paramref name="first"/> and <paramref name="second"/> into a single sequential chain.
    /// The output of <paramref name="first"/> is passed as the input to <paramref name="second"/>.
    /// If <paramref name="first"/> fails, execution stops and its error is propagated without calling <paramref name="second"/>.
    /// </summary>
    /// <typeparam name="TInput">The input type of the first chain.</typeparam>
    /// <typeparam name="TMiddle">The shared output/input type between the two chains.</typeparam>
    /// <typeparam name="TNext">The output type of the second chain.</typeparam>
    /// <param name="first">The upstream chain.</param>
    /// <param name="second">The downstream chain, fed the output of <paramref name="first"/>.</param>
    /// <returns>A new <see cref="IChain{TInput,TNext}"/> representing the sequential composition.</returns>
    public static IChain<TInput, TNext> Then<TInput, TMiddle, TNext>(
        this IChain<TInput, TMiddle> first,
        IChain<TMiddle, TNext> second)
        => new ComposedChain<TInput, TMiddle, TNext>(first, second);
}

/// <summary>
/// An <see cref="IChain{TInput,TOutput}"/> that applies a single middleware layer before the inner chain.
/// </summary>
internal sealed class MiddlewareChain<TInput, TOutput> : IChain<TInput, TOutput>
{
    private readonly IChain<TInput, TOutput> _inner;
    private readonly IChainMiddleware<TInput, TOutput> _middleware;

    internal MiddlewareChain(IChain<TInput, TOutput> inner, IChainMiddleware<TInput, TOutput> middleware)
    {
        _inner = inner;
        _middleware = middleware;
    }

    /// <inheritdoc />
    public string Name => $"{_middleware.GetType().Name}({_inner.Name})";

    /// <inheritdoc />
    public Task<ChainResult<TOutput>> ExecuteAsync(
        TInput input,
        ChainContext context,
        CancellationToken cancellationToken = default)
        => _middleware.InvokeAsync(input, context, _inner.ExecuteAsync, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<TOutput> StreamAsync(
        TInput input,
        ChainContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var token in _inner.StreamAsync(input, context, cancellationToken).ConfigureAwait(false))
            yield return token;
    }
}

/// <summary>
/// An <see cref="IChain{TInput,TNext}"/> formed by sequentially composing two chains.
/// </summary>
internal sealed class ComposedChain<TInput, TMiddle, TNext> : IChain<TInput, TNext>
{
    private readonly IChain<TInput, TMiddle> _first;
    private readonly IChain<TMiddle, TNext> _second;

    internal ComposedChain(IChain<TInput, TMiddle> first, IChain<TMiddle, TNext> second)
    {
        _first = first;
        _second = second;
    }

    /// <inheritdoc />
    public string Name => $"{_first.Name} -> {_second.Name}";

    /// <inheritdoc />
    public async Task<ChainResult<TNext>> ExecuteAsync(
        TInput input,
        ChainContext context,
        CancellationToken cancellationToken = default)
    {
        var firstResult = await _first.ExecuteAsync(input, context, cancellationToken).ConfigureAwait(false);
        if (!firstResult.IsSuccess)
            return ChainResult<TNext>.Failure(firstResult.Error!);

        return await _second.ExecuteAsync(firstResult.Value!, context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TNext> StreamAsync(
        TInput input,
        ChainContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var firstResult = await _first.ExecuteAsync(input, context, cancellationToken).ConfigureAwait(false);
        if (!firstResult.IsSuccess) yield break;

        await foreach (var token in _second.StreamAsync(firstResult.Value!, context, cancellationToken).ConfigureAwait(false))
            yield return token;
    }
}
