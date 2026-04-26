using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Graph-based agent workflow — WeaveLLM's equivalent of LangGraph.
/// Define nodes (agents/functions) and edges (routing logic) to build
/// complex multi-agent pipelines with cycles, conditionals, and parallel branches.
/// </summary>
public sealed class AgentGraph<TState> where TState : class, new()
{
    private readonly Dictionary<string, IGraphNode<TState>> _nodes = new();
    private readonly Dictionary<string, List<GraphEdge<TState>>> _edges = new();
    private string? _entryPoint;
    private string? _endPoint;

    public static AgentGraph<TState> Create() => new();

    public AgentGraph<TState> AddNode(string name, IGraphNode<TState> node)
    {
        _nodes[name] = node;
        return this;
    }

    public AgentGraph<TState> AddNode(string name, Func<TState, ChainContext, CancellationToken, Task<TState>> handler)
    {
        _nodes[name] = new FuncGraphNode<TState>(name, handler);
        return this;
    }

    public AgentGraph<TState> AddEdge(string from, string to)
    {
        GetEdges(from).Add(new GraphEdge<TState>(from, to, _ => true));
        return this;
    }

    public AgentGraph<TState> AddConditionalEdge(string from, Func<TState, string> router, Dictionary<string, string>? routeMap = null)
    {
        GetEdges(from).Add(new GraphEdge<TState>(from, null, _ => true, router, routeMap));
        return this;
    }

    public AgentGraph<TState> SetEntryPoint(string nodeName)
    {
        _entryPoint = nodeName;
        return this;
    }

    public AgentGraph<TState> SetEndPoint(string nodeName)
    {
        _endPoint = nodeName;
        return this;
    }

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

public sealed class GraphResult<TState>
{
    public bool IsSuccess { get; init; }
    public TState State { get; init; } = default!;
    public IReadOnlyList<string> ExecutionLog { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public static GraphResult<TState> Success(TState state, IReadOnlyList<string> log) =>
        new() { IsSuccess = true, State = state, ExecutionLog = log };

    public static GraphResult<TState> Failure(TState state, string error) =>
        new() { IsSuccess = false, State = state, ErrorMessage = error };
}

public interface IGraphNode<TState>
{
    string Name { get; }
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
    public sealed class ResearchState
    {
        public string Question { get; set; } = string.Empty;
        public string? ResearchNotes { get; set; }
        public string? Draft { get; set; }
        public string? FinalAnswer { get; set; }
        public bool NeedsRevision { get; set; }
        public int RevisionCount { get; set; }
    }

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
