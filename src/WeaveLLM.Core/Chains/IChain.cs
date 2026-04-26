using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Chains;

/// <summary>
/// Core chain abstraction. Every unit of work in WeaveLLM implements this.
/// Strongly typed input/output prevents runtime surprises.
/// </summary>
public interface IChain<TInput, TOutput>
{
    string Name { get; }
    Task<ChainResult<TOutput>> ExecuteAsync(TInput input, ChainContext context, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TOutput> StreamAsync(TInput input, ChainContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for chains that can be composed in a pipeline.
/// </summary>
public interface IChain : IChain<ChainInput, ChainOutput> { }

/// <summary>
/// A chain that can be connected to another chain, forming a pipeline.
/// </summary>
public interface IConnectableChain<TInput, TOutput> : IChain<TInput, TOutput>
{
    IConnectableChain<TInput, TNext> Pipe<TNext>(IChain<TOutput, TNext> next);
    IConnectableChain<TInput, TOutput> WithMiddleware(IChainMiddleware<TInput, TOutput> middleware);
}
