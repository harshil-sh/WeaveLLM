using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Chains;

/// <summary>
/// Core chain abstraction. Every unit of work in WeaveLLM implements this.
/// Strongly typed input/output prevents runtime surprises.
/// </summary>
/// <typeparam name="TInput">The input type accepted by the chain.</typeparam>
/// <typeparam name="TOutput">The output type produced by the chain.</typeparam>
public interface IChain<TInput, TOutput>
{
    /// <summary>The unique name identifying this chain.</summary>
    string Name { get; }

    /// <summary>
    /// Executes the chain with the given input and returns a result.
    /// </summary>
    /// <param name="input">The input to process.</param>
    /// <param name="context">Shared context passed through the chain execution.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="ChainResult{TOutput}"/> that is successful with the produced output,
    /// or failed with an error describing what went wrong.
    /// </returns>
    Task<ChainResult<TOutput>> ExecuteAsync(TInput input, ChainContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams output tokens or chunks from the chain as they are produced.
    /// </summary>
    /// <param name="input">The input to process.</param>
    /// <param name="context">Shared context passed through the chain execution.</param>
    /// <param name="cancellationToken">Token to cancel the stream.</param>
    /// <returns>An async sequence of output values emitted incrementally.</returns>
    IAsyncEnumerable<TOutput> StreamAsync(TInput input, ChainContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for chains that can be composed in a pipeline.
/// </summary>
public interface IChain : IChain<ChainInput, ChainOutput> { }

/// <summary>
/// A chain that can be connected to another chain, forming a pipeline.
/// </summary>
/// <typeparam name="TInput">The input type accepted by this chain.</typeparam>
/// <typeparam name="TOutput">The output type produced by this chain.</typeparam>
public interface IConnectableChain<TInput, TOutput> : IChain<TInput, TOutput>
{
    /// <summary>
    /// Connects this chain to a downstream chain, producing a new connectable chain spanning both.
    /// </summary>
    /// <typeparam name="TNext">The output type of the downstream chain.</typeparam>
    /// <param name="next">The chain to execute after this one.</param>
    /// <returns>A new <see cref="IConnectableChain{TInput, TNext}"/> representing the composed sequence.</returns>
    IConnectableChain<TInput, TNext> Pipe<TNext>(IChain<TOutput, TNext> next);

    /// <summary>
    /// Wraps this chain with the given middleware, returning a new chain with the middleware applied.
    /// </summary>
    /// <param name="middleware">The middleware to wrap around this chain's execution.</param>
    /// <returns>A new <see cref="IConnectableChain{TInput, TOutput}"/> with the middleware applied.</returns>
    IConnectableChain<TInput, TOutput> WithMiddleware(IChainMiddleware<TInput, TOutput> middleware);
}
