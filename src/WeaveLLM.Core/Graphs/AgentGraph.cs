#nullable enable
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Graphs;

/// <summary>
/// Fluent builder for graph-based multi-agent workflows.
/// Nodes are async state-transform functions; edges declare routing between them.
/// Equivalent in purpose to LangGraph's <c>StateGraph</c>.
/// </summary>
/// <typeparam name="TState">
/// The shared state object threaded through every node. Must be a reference type with a
/// parameterless constructor so that nodes can safely mutate it.
/// </typeparam>
public sealed class AgentGraph<TState> where TState : class, new()
{
    private const int DefaultMaxSteps = 50;

    private readonly Dictionary<string, Func<TState, GraphContext, CancellationToken, Task<TState>>> _nodes = new();
    private readonly Dictionary<string, List<GraphEdge<TState>>> _edges = new();
    private string? _entryPoint;
    private string? _endPoint;
    private int _maxSteps = DefaultMaxSteps;

    /// <summary>Creates a new, empty graph builder.</summary>
    public static AgentGraph<TState> Create() => new();

    /// <summary>
    /// Registers a named node that transforms the state asynchronously.
    /// Registering a node with an existing name replaces it.
    /// </summary>
    /// <param name="name">The unique node identifier used in edge declarations.</param>
    /// <param name="handler">
    /// A function that receives the current state and <see cref="GraphContext"/>, performs work,
    /// and returns the (potentially mutated) state.
    /// </param>
    public AgentGraph<TState> AddNode(
        string name,
        Func<TState, GraphContext, CancellationToken, Task<TState>> handler)
    {
        _nodes[name] = handler;
        return this;
    }

    /// <summary>Adds an unconditional edge from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public AgentGraph<TState> AddEdge(string from, string to)
    {
        GetEdges(from).Add(new GraphEdge<TState>(to, null, null));
        return this;
    }

    /// <summary>
    /// Adds a conditional edge from <paramref name="from"/> that routes based on state at runtime.
    /// </summary>
    /// <param name="from">The source node name.</param>
    /// <param name="condition">
    /// A function that inspects the current state and returns a route key.
    /// The key is resolved through <paramref name="routes"/> to a target node name.
    /// If the key is absent from <paramref name="routes"/>, it is used as the target node name directly.
    /// </param>
    /// <param name="routes">Maps route keys returned by <paramref name="condition"/> to node names.</param>
    public AgentGraph<TState> AddConditionalEdge(
        string from,
        Func<TState, string> condition,
        Dictionary<string, string> routes)
    {
        GetEdges(from).Add(new GraphEdge<TState>(null, condition, routes));
        return this;
    }

    /// <summary>Sets the node where graph execution begins.</summary>
    public AgentGraph<TState> SetEntryPoint(string nodeName)
    {
        _entryPoint = nodeName;
        return this;
    }

    /// <summary>Sets the terminal node. Execution stops when this node is reached.</summary>
    public AgentGraph<TState> SetEndPoint(string nodeName)
    {
        _endPoint = nodeName;
        return this;
    }

    /// <summary>
    /// Overrides the maximum number of node executions before the graph is aborted with a
    /// cycle-detection error. Defaults to <c>50</c>.
    /// </summary>
    public AgentGraph<TState> WithMaxSteps(int maxSteps)
    {
        _maxSteps = maxSteps;
        return this;
    }

    /// <summary>
    /// Runs the graph from the entry point to the end point, threading
    /// <paramref name="initialState"/> through each node in sequence.
    /// </summary>
    /// <param name="initialState">The starting state value.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>
    /// A <see cref="ChainResult{T}"/> containing the final state on success,
    /// or a <see cref="WeaveLLMError"/> if a node fails, the graph cycles beyond
    /// <see cref="DefaultMaxSteps"/>, or the operation is cancelled.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="SetEntryPoint"/> was never called.</exception>
    public async Task<ChainResult<TState>> RunAsync(
        TState initialState,
        CancellationToken cancellationToken = default)
    {
        if (_entryPoint is null)
            throw new InvalidOperationException("Entry point not set. Call SetEntryPoint() before RunAsync().");

        var context = new GraphContext(_entryPoint, 0, DateTimeOffset.UtcNow);
        var state = initialState;
        var stepCount = 0;

        while (context.CurrentNode != _endPoint &&
               context.CurrentNode is not null &&
               !cancellationToken.IsCancellationRequested)
        {
            if (stepCount >= _maxSteps)
                return ChainResult<TState>.Failure(
                    $"Graph exceeded maximum step count ({_maxSteps}). Possible infinite cycle detected.",
                    "GRAPH_MAX_STEPS_EXCEEDED");

            if (!_nodes.TryGetValue(context.CurrentNode, out var handler))
                return ChainResult<TState>.Failure(
                    $"Node '{context.CurrentNode}' is referenced in an edge but was never registered via AddNode().",
                    "GRAPH_NODE_NOT_FOUND");

            stepCount++;
            context = context with { StepCount = stepCount };

            try
            {
                state = await handler(state, context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ChainResult<TState>.Failure("Graph execution was cancelled.", "CANCELLED");
            }
            catch (Exception ex)
            {
                return ChainResult<TState>.Failure(ex.Message, "NODE_EXECUTION_FAILED");
            }

            var nextNode = ResolveNextNode(context.CurrentNode, state);
            context = context with { CurrentNode = nextNode ?? _endPoint ?? string.Empty };
        }

        return ChainResult<TState>.Success(state);
    }

    private string? ResolveNextNode(string current, TState state)
    {
        if (!_edges.TryGetValue(current, out var edges) || edges.Count == 0)
            return _endPoint;

        foreach (var edge in edges)
        {
            if (edge.Condition is null)
                return edge.To;

            var routeKey = edge.Condition(state);
            return edge.Routes?.TryGetValue(routeKey, out var mapped) == true ? mapped : routeKey;
        }

        return _endPoint;
    }

    private List<GraphEdge<TState>> GetEdges(string node)
    {
        if (!_edges.TryGetValue(node, out var list))
            _edges[node] = list = new();
        return list;
    }
}

/// <summary>
/// Immutable execution context passed to every node handler.
/// Nodes may inspect it to make routing or logging decisions.
/// </summary>
/// <param name="CurrentNode">The name of the node currently executing.</param>
/// <param name="StepCount">The number of node executions completed so far in this run.</param>
/// <param name="StartedAt">The UTC timestamp when <c>RunAsync</c> was called.</param>
public sealed record GraphContext(string CurrentNode, int StepCount, DateTimeOffset StartedAt);

internal sealed record GraphEdge<TState>(
    string? To,
    Func<TState, string>? Condition,
    Dictionary<string, string>? Routes);
