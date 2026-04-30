#nullable enable
using FluentAssertions;
using WeaveLLM.Core.Graphs;
using Xunit;

namespace WeaveLLM.IntegrationTests.Graphs;

public class AgentGraphIntegrationTests
{
    private sealed class FlowState
    {
        public List<string> ExecutedNodes { get; } = new();
        public string Route { get; set; } = string.Empty;
    }

    [IntegrationFact]
    public async Task AgentGraph_LinearFlow_ExecutesAllNodes()
    {
        var graph = AgentGraph<FlowState>.Create()
            .AddNode("node_a", (s, _, _) =>
            {
                s.ExecutedNodes.Add("node_a");
                return Task.FromResult(s);
            })
            .AddNode("node_b", (s, _, _) =>
            {
                s.ExecutedNodes.Add("node_b");
                return Task.FromResult(s);
            })
            .AddEdge("node_a", "node_b")
            .AddEdge("node_b", "end")
            .SetEntryPoint("node_a")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new FlowState());

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExecutedNodes.Should().Equal("node_a", "node_b");
    }

    [IntegrationFact]
    public async Task AgentGraph_ConditionalEdge_RoutesCorrectly()
    {
        var graph = AgentGraph<FlowState>.Create()
            .AddNode("start", (s, _, _) =>
            {
                s.ExecutedNodes.Add("start");
                return Task.FromResult(s);
            })
            .AddNode("branch_a", (s, _, _) =>
            {
                s.ExecutedNodes.Add("branch_a");
                return Task.FromResult(s);
            })
            .AddNode("branch_b", (s, _, _) =>
            {
                s.ExecutedNodes.Add("branch_b");
                return Task.FromResult(s);
            })
            .AddConditionalEdge("start", s => s.Route, new Dictionary<string, string>
            {
                ["a"] = "branch_a",
                ["b"] = "branch_b"
            })
            .AddEdge("branch_a", "end")
            .AddEdge("branch_b", "end")
            .SetEntryPoint("start")
            .SetEndPoint("end");

        var resultA = await graph.RunAsync(new FlowState { Route = "a" });

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.ExecutedNodes.Should().Contain("start").And.Contain("branch_a");
        resultA.Value.ExecutedNodes.Should().NotContain("branch_b");
    }
}
