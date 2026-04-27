using WeaveLLM.Core.Extensions;

namespace WeaveLLM.Core.Chains;

/// <summary>
/// Builds a chain by wrapping an inner chain with registered middleware layers.
/// The last middleware registered runs outermost (LIFO execution order).
/// </summary>
public sealed class PipelineBuilder<TInput, TOutput> : IPipeline<TInput, TOutput>
{
    private readonly IChain<TInput, TOutput> _innerChain;
    private readonly List<IChainMiddleware<TInput, TOutput>> _middlewares = new();

    public PipelineBuilder(IChain<TInput, TOutput> innerChain)
    {
        _innerChain = innerChain;
    }

    /// <inheritdoc />
    public IPipeline<TInput, TOutput> UseMiddleware(IChainMiddleware<TInput, TOutput> middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    /// <inheritdoc />
    public Task<IChain<TInput, TOutput>> BuildAsync()
    {
        IChain<TInput, TOutput> chain = _innerChain;
        // Iterate forward so last-registered ends up outermost (LIFO).
        for (var i = 0; i < _middlewares.Count; i++)
            chain = chain.WithMiddleware(_middlewares[i]);
        return Task.FromResult(chain);
    }
}
