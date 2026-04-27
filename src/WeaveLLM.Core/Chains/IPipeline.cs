namespace WeaveLLM.Core.Chains;

/// <summary>
/// Fluent builder for composing a chain with an ordered set of middleware layers.
/// </summary>
public interface IPipeline<TInput, TOutput>
{
    /// <summary>
    /// Adds a middleware layer to the pipeline. Middleware added last runs outermost (LIFO).
    /// </summary>
    IPipeline<TInput, TOutput> UseMiddleware(IChainMiddleware<TInput, TOutput> middleware);

    /// <summary>
    /// Builds the final chain, wrapping the inner chain with all registered middleware.
    /// </summary>
    Task<IChain<TInput, TOutput>> BuildAsync();
}
