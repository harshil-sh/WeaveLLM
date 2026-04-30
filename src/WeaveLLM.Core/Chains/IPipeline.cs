namespace WeaveLLM.Core.Chains;

/// <summary>
/// Fluent builder for composing a chain with an ordered set of middleware layers.
/// </summary>
/// <typeparam name="TInput">The input type accepted by the pipeline.</typeparam>
/// <typeparam name="TOutput">The output type produced by the pipeline.</typeparam>
public interface IPipeline<TInput, TOutput>
{
    /// <summary>
    /// Adds a middleware layer to the pipeline. Middleware added last runs outermost (LIFO).
    /// </summary>
    /// <param name="middleware">The middleware to register.</param>
    /// <returns>The same pipeline instance to allow chaining further <c>UseMiddleware</c> calls.</returns>
    IPipeline<TInput, TOutput> UseMiddleware(IChainMiddleware<TInput, TOutput> middleware);

    /// <summary>
    /// Builds the final chain, wrapping the inner chain with all registered middleware.
    /// </summary>
    /// <returns>The fully composed <see cref="IChain{TInput, TOutput}"/> ready for execution.</returns>
    Task<IChain<TInput, TOutput>> BuildAsync();
}
