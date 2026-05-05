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
    /// Validates the graph structure and caches the result.
    /// </summary>
    private string? _validationError;
    private bool _validated;

    private string? Validate()
    {
        if (_validated) return _validationError;
        _validated = true;

        var issues = new List<string>();

        if (string.IsNullOrEmpty(_entryPoint))
            issues.Add("Entry point is not set. Call SetEntryPoint().");
        else if (!_nodes.ContainsKey(_entryPoint))
            issues.Add($"Entry point '{_entryPoint}' is not a registered node.");

        if (string.IsNullOrEmpty(_endPoint))
            issues.Add("End point is not set. Call SetEndPoint().");

        foreach (var (from, edges) in _edges)
        {
            foreach (var edge in edges)
            {
                if (edge.To is not null && edge.To != _endPoint && !_nodes.ContainsKey(edge.To))
                    issues.Add($"Edge target '{edge.To}' (from '{from}') is not a registered node.");

                if (edge.Routes is not null)
                {
                    foreach (var (key, target) in edge.Routes)
                    {
                        if (target != _endPoint && !_nodes.ContainsKey(target))
                            issues.Add($"Conditional route target '{target}' (key '{key}', from '{from}') is not a registered node.");
                    }
                }
            }
        }

        _validationError = issues.Count > 0
            ? "Graph validation failed: " + string.Join("; ", issues)
            : null;

        return _validationError;
    }

    /// <summary>
    /// Runs the graph from the entry point to the end point, threading
    /// <paramref name="initialState"/> through each node in sequence.
    /// </summary>
    /// <param name="initialState">The starting state value.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>
    /// A <see cref="ChainResult{T}"/> containing the final state on success,
    /// or a descriptive failure for validation errors, cycles, or dead ends.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="SetEntryPoint"/> was never called.</exception>
    public async Task<ChainResult<TState>> RunAsync(
        TState initialState,
        CancellationToken cancellationToken = default)
    {
        if (_entryPoint is null)
            throw new InvalidOperationException("Entry point not set. Call SetEntryPoint() before RunAsync().");

        var validationError = Validate();
        if (validationError is not null)
            return ChainResult<TState>.Failure(validationError, "InvalidGraph");

        var startedAt = DateTimeOffset.UtcNow;
        var context = new GraphContext(_entryPoint, 0, startedAt);
        var state = initialState;
        var stepCount = 0;

        while (context.CurrentNode != _endPoint &&
               context.CurrentNode is not null &&
               !cancellationToken.IsCancellationRequested)
        {
            stepCount++;
            if (stepCount > _maxSteps)
                return ChainResult<TState>.Failure(
                    $"Graph exceeded {_maxSteps} steps — possible cycle. Increase MaxSteps or check edges.",
                    "MaxStepsCycleDetected");

            if (!_nodes.TryGetValue(context.CurrentNode, out var handler))
                return ChainResult<TState>.Failure(
                    $"Node '{context.CurrentNode}' is not registered.",
                    "InvalidGraph");

            context = context with { StepCount = stepCount };

            try
            {
                state = await handler(state, context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ChainResult<TState>.Failure(WeaveLLMError.Cancelled("Graph execution was cancelled."));
            }
            catch (Exception ex)
            {
                return ChainResult<TState>.Failure(ex.Message, "NODE_EXECUTION_FAILED");
            }

            var (nextNode, deadEnd) = ResolveNextNode(context.CurrentNode, state);
            if (deadEnd)
                return ChainResult<TState>.Failure(
                    $"No edge from node '{context.CurrentNode}'. Add an edge or set it as the end point.",
                    "GraphDeadEnd");

            context = context with { CurrentNode = nextNode! };
        }

        return ChainResult<TState>.Success(state);
    }

    private (string? nextNode, bool deadEnd) ResolveNextNode(string current, TState state)
    {
        if (!_edges.TryGetValue(current, out var edges) || edges.Count == 0)
        {
            // No edges: only valid if this node IS the end point
            return current == _endPoint ? (_endPoint, false) : (null, true);
        }

        foreach (var edge in edges)
        {
            if (edge.Condition is null)
                return (edge.To, false);

            var routeKey = edge.Condition(state);
            var target = edge.Routes?.TryGetValue(routeKey, out var mapped) == true ? mapped : routeKey;
            return (target, false);
        }

        return (null, true);
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
