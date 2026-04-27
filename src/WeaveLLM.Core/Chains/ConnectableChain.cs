using WeaveLLM.Core.Extensions;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Chains;

/// <summary>
/// Wraps any <see cref="IChain{TInput,TOutput}"/> to make it composable via <see cref="Pipe{TNext}"/>
/// and decoratable via <see cref="WithMiddleware"/>.
/// </summary>
public sealed class ConnectableChain<TInput, TOutput> : IConnectableChain<TInput, TOutput>
{
    private readonly IChain<TInput, TOutput> _inner;

    public ConnectableChain(IChain<TInput, TOutput> inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public Task<ChainResult<TOutput>> ExecuteAsync(TInput input, ChainContext context, CancellationToken cancellationToken = default)
        => _inner.ExecuteAsync(input, context, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TOutput> StreamAsync(TInput input, ChainContext context, CancellationToken cancellationToken = default)
        => _inner.StreamAsync(input, context, cancellationToken);

    /// <inheritdoc />
    public IConnectableChain<TInput, TNext> Pipe<TNext>(IChain<TOutput, TNext> next)
        => new ConnectableChain<TInput, TNext>(_inner.Then(next));

    /// <inheritdoc />
    public IConnectableChain<TInput, TOutput> WithMiddleware(IChainMiddleware<TInput, TOutput> middleware)
        => new ConnectableChain<TInput, TOutput>(_inner.WithMiddleware(middleware));
}
