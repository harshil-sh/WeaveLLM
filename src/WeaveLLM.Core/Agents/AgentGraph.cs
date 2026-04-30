using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Graph-based agent workflow — WeaveLLM's equivalent of LangGraph.
/// Define nodes (agents/functions) and edges (routing logic) to build
/// complex multi-agent pipelines with cycles, conditionals, and parallel branches.
/// </summary>
/// <typeparam name="TState">Shared mutable state passed through every node in the graph.</typeparam>
public sealed class AgentGraph<TState> where TState : class, new()
{
    private readonly Dictionary<string, IGraphNode<TState>> _nodes = new();
    private readonly Dictionary<string, List<GraphEdge<TState>>> _edges = new();
    private string? _entryPoint;
    private string? _endPoint;

    /// <summary>Creates a new empty <see cref="AgentGraph{TState}"/>.</summary>
    /// <returns>A new <see cref="AgentGraph{TState}"/> with no nodes or edges.</returns>
    public static AgentGraph<TState> Create() => new();

    /// <summary>
    /// Registers a node backed by an <see cref="IGraphNode{TState}"/> implementation.
    /// </summary>
    /// <param name="name">Unique node name used when wiring edges.</param>
    /// <param name="node">The node implementation to execute when this node is reached.</param>
    /// <returns>The same graph to allow fluent chaining.</returns>
    public AgentGraph<TState> AddNode(string name, IGraphNode<TState> node)
    {
        _nodes[name] = node;
        return this;
    }

    /// <summary>
    /// Registers a node backed by an inline async delegate.
    /// </summary>
    /// <param name="name">Unique node name used when wiring edges.</param>
    /// <param name="handler">Async function that receives and returns the updated state.</param>
    /// <returns>The same graph to allow fluent chaining.</returns>
    public AgentGraph<TState> AddNode(string name, Func<TState, ChainContext, CancellationToken, Task<TState>> handler)
    {
        _nodes[name] = new FuncGraphNode<TState>(name, handler);
        return this;
    }

    /// <summary>
    /// Adds an unconditional directed edge from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="from">The source node name.</param>
    /// <param name="to">The destination node name.</param>
    /// <returns>The same graph to allow fluent chaining.</returns>
    public AgentGraph<TState> AddEdge(string from, string to)
    {
        GetEdges(from).Add(new GraphEdge<TState>(from, to, _ => true));
        return this;
    }

    /// <summary>
    /// Adds a conditional edge that uses a router function to select the next node at runtime.
    /// </summary>
    /// <param name="from">The source node name.</param>
    /// <param name="router">Function that inspects the current state and returns a route key or node name.</param>
    /// <param name="routeMap">Optional map translating route keys returned by <paramref name="router"/> to node names.</param>
    /// <returns>The same graph to allow fluent chaining.</returns>
    public AgentGraph<TState> AddConditionalEdge(string from, Func<TState, string> router, Dictionary<string, string>? routeMap = null)
    {
        GetEdges(from).Add(new GraphEdge<TState>(from, null, _ => true, router, routeMap));
        return this;
    }

    /// <summary>
    /// Sets the node where graph execution begins.
    /// </summary>
    /// <param name="nodeName">The name of the entry node.</param>
    /// <returns>The same graph to allow fluent chaining.</returns>
    public AgentGraph<TState> SetEntryPoint(string nodeName)
    {
        _entryPoint = nodeName;
        return this;
    }

    /// <summary>
    /// Sets the node whose completion terminates the execution loop.
    /// </summary>
    /// <param name="nodeName">The name of the terminal node.</param>
    /// <returns>The same graph to allow fluent chaining.</returns>
    public AgentGraph<TState> SetEndPoint(string nodeName)
    {
        _endPoint = nodeName;
        return this;
    }

    /// <summary>
    /// Executes the graph from the entry point, traversing nodes and edges until the end point is reached.
    /// </summary>
    /// <param name="initialState">The initial state passed to the first node.</param>
    /// <param name="context">Optional shared chain context; a new context is created if <c>null</c>.</param>
    /// <param name="cancellationToken">Token to cancel execution between nodes.</param>
    /// <returns>
    /// A <see cref="GraphResult{TState}"/> with the final state and execution log on success,
    /// or a failed result with an error message if a node name could not be resolved.
    /// </returns>
    public async Task<GraphResult<TState>> RunAsync(TState initialState, ChainContext? context = null, CancellationToken cancellationToken = default)
    {
        if (_entryPoint is null) throw new InvalidOperationException("Entry point not set. Call SetEntryPoint().");

        context ??= ChainContext.Create();
        var state = initialState;
        var executionLog = new List<string>();
        var currentNode = _entryPoint;

        while (currentNode != _endPoint && currentNode is not null && !cancellationToken.IsCancellationRequested)
        {
            if (!_nodes.TryGetValue(currentNode, out var node))
                return GraphResult<TState>.Failure(state, $"Node '{currentNode}' not found in graph.");

            executionLog.Add(currentNode);
            state = await node.ExecuteAsync(state, context, cancellationToken);

            currentNode = ResolveNextNode(currentNode, state);
        }

        return GraphResult<TState>.Success(state, executionLog);
    }

    private string? ResolveNextNode(string current, TState state)
    {
        if (!_edges.TryGetValue(current, out var edges) || edges.Count == 0)
            return _endPoint;

        foreach (var edge in edges)
        {
            if (!edge.Condition(state)) continue;
            if (edge.Router is not null)
            {
                var routeKey = edge.Router(state);
                return edge.RouteMap?.TryGetValue(routeKey, out var mapped) == true ? mapped : routeKey;
            }
            return edge.To;
        }
        return _endPoint;
    }

    private List<GraphEdge<TState>> GetEdges(string node)
    {
        if (!_edges.TryGetValue(node, out var edges))
            _edges[node] = edges = new();
        return edges;
    }
}

/// <summary>
/// The outcome of a completed <see cref="AgentGraph{TState}"/> run.
/// </summary>
/// <typeparam name="TState">The graph's shared state type.</typeparam>
public sealed class GraphResult<TState>
{
    /// <summary>Whether the graph reached the end point without error.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>The final state after all nodes have executed.</summary>
    public TState State { get; init; } = default!;

    /// <summary>Ordered list of node names visited during this run.</summary>
    public IReadOnlyList<string> ExecutionLog { get; init; } = [];

    /// <summary>Human-readable error description when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result with the final state and execution log.
    /// </summary>
    /// <param name="state">The state produced after the last node executed.</param>
    /// <param name="log">Ordered list of node names that were visited.</param>
    /// <returns>A <see cref="GraphResult{TState}"/> with <see cref="IsSuccess"/> set to <c>true</c>.</returns>
    public static GraphResult<TState> Success(TState state, IReadOnlyList<string> log) =>
        new() { IsSuccess = true, State = state, ExecutionLog = log };

    /// <summary>
    /// Creates a failed result with the last known state and an error message.
    /// </summary>
    /// <param name="state">The state at the point of failure.</param>
    /// <param name="error">Description of what went wrong.</param>
    /// <returns>A <see cref="GraphResult{TState}"/> with <see cref="IsSuccess"/> set to <c>false</c>.</returns>
    public static GraphResult<TState> Failure(TState state, string error) =>
        new() { IsSuccess = false, State = state, ErrorMessage = error };
}

/// <summary>
/// A single processing unit in an <see cref="AgentGraph{TState}"/>.
/// Receives the current state, performs work, and returns the updated state.
/// </summary>
/// <typeparam name="TState">The graph's shared state type.</typeparam>
public interface IGraphNode<TState>
{
    /// <summary>Unique name identifying this node within the graph.</summary>
    string Name { get; }

    /// <summary>
    /// Executes this node's logic against the current state and returns the updated state.
    /// </summary>
    /// <param name="state">The current graph state passed in from the previous node or initial input.</param>
    /// <param name="context">Shared chain context carrying trace IDs, logger, and metadata.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The updated state to pass to the next node.</returns>
    Task<TState> ExecuteAsync(TState state, ChainContext context, CancellationToken cancellationToken = default);
}

internal sealed class FuncGraphNode<TState>(string name, Func<TState, ChainContext, CancellationToken, Task<TState>> handler) : IGraphNode<TState>
{
    public string Name { get; } = name;
    public Task<TState> ExecuteAsync(TState state, ChainContext context, CancellationToken cancellationToken) =>
        handler(state, context, cancellationToken);
}

internal sealed record GraphEdge<TState>(
    string From,
    string? To,
    Func<TState, bool> Condition,
    Func<TState, string>? Router = null,
    Dictionary<string, string>? RouteMap = null);

/// <summary>
/// Usage example showing how to build a multi-agent research pipeline with AgentGraph.
/// </summary>
public static class AgentGraphExamples
{
    /// <summary>
    /// Shared state flowing through the example research pipeline.
    /// </summary>
    public sealed class ResearchState
    {
        /// <summary>The original question the pipeline was asked to answer.</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>Notes gathered by the researcher node.</summary>
        public string? ResearchNotes { get; set; }

        /// <summary>Draft answer produced by the writer node.</summary>
        public string? Draft { get; set; }

        /// <summary>Approved final answer set by the finalizer node.</summary>
        public string? FinalAnswer { get; set; }

        /// <summary>Whether the critic determined the draft requires another revision cycle.</summary>
        public bool NeedsRevision { get; set; }

        /// <summary>Number of revision cycles completed so far.</summary>
        public int RevisionCount { get; set; }
    }

    /// <summary>
    /// Builds a sample multi-agent pipeline with researcher, writer, critic, and finalizer nodes.
    /// </summary>
    /// <returns>A fully configured <see cref="AgentGraph{ResearchState}"/> ready to run.</returns>
    public static AgentGraph<ResearchState> BuildResearchPipeline()
    {
        return AgentGraph<ResearchState>.Create()
            .AddNode("researcher", async (state, ctx, ct) =>
            {
                // Researcher agent gathers information
                state.ResearchNotes = $"Research notes for: {state.Question}";
                return state;
            })
            .AddNode("writer", async (state, ctx, ct) =>
            {
                // Writer agent drafts the answer
                state.Draft = $"Draft answer based on: {state.ResearchNotes}";
                return state;
            })
            .AddNode("critic", async (state, ctx, ct) =>
            {
                // Critic agent reviews the draft
                state.NeedsRevision = state.RevisionCount < 2;
                state.RevisionCount++;
                return state;
            })
            .AddNode("finalizer", async (state, ctx, ct) =>
            {
                state.FinalAnswer = state.Draft;
                return state;
            })
            .SetEntryPoint("researcher")
            .AddEdge("researcher", "writer")
            .AddEdge("writer", "critic")
            .AddConditionalEdge("critic",
                state => state.NeedsRevision ? "revise" : "finalize",
                new Dictionary<string, string> { ["revise"] = "writer", ["finalize"] = "finalizer" })
            .SetEndPoint("finalizer");
    }
}
