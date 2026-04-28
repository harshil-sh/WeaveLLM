using FluentAssertions;
using WeaveLLM.Core.Graphs;
using WeaveLLM.Core.Models;
using Xunit;

namespace WeaveLLM.Core.Tests.Graphs;

public class AgentGraphTests
{
    private sealed class CounterState
    {
        public int Count { get; set; }
        public string Route { get; set; } = "a";
        public List<string> Visited { get; set; } = [];
    }

    // ── Linear graph ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LinearGraph_ExecutesAllNodesInOrder()
    {
        var graph = AgentGraph<CounterState>.Create()
            .AddNode("step1", (s, _, _) => { s.Count += 1; s.Visited.Add("step1"); return Task.FromResult(s); })
            .AddNode("step2", (s, _, _) => { s.Count += 10; s.Visited.Add("step2"); return Task.FromResult(s); })
            .AddEdge("step1", "step2")
            .AddEdge("step2", "end")
            .SetEntryPoint("step1")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(11);
        result.Value.Visited.Should().Equal("step1", "step2");
    }

    [Fact]
    public async Task LinearGraph_SingleNode_ExecutesAndReachesEnd()
    {
        var graph = AgentGraph<CounterState>.Create()
            .AddNode("only", (s, _, _) => { s.Count = 99; return Task.FromResult(s); })
            .AddEdge("only", "end")
            .SetEntryPoint("only")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(99);
    }

    // ── Conditional routing ───────────────────────────────────────────────────

    [Fact]
    public async Task ConditionalGraph_RoutesToCorrectBranch_WhenRouteIsA()
    {
        var graph = AgentGraph<CounterState>.Create()
            .AddNode("start", (s, _, _) => { s.Route = "a"; return Task.FromResult(s); })
            .AddNode("branch-a", (s, _, _) => { s.Count = 1; return Task.FromResult(s); })
            .AddNode("branch-b", (s, _, _) => { s.Count = 2; return Task.FromResult(s); })
            .AddConditionalEdge("start",
                s => s.Route,
                new Dictionary<string, string> { ["a"] = "branch-a", ["b"] = "branch-b" })
            .AddEdge("branch-a", "end")
            .AddEdge("branch-b", "end")
            .SetEntryPoint("start")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState { Route = "a" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConditionalGraph_RoutesToCorrectBranch_WhenRouteIsB()
    {
        var graph = AgentGraph<CounterState>.Create()
            .AddNode("start", (s, _, _) => { s.Route = "b"; return Task.FromResult(s); })
            .AddNode("branch-a", (s, _, _) => { s.Count = 1; return Task.FromResult(s); })
            .AddNode("branch-b", (s, _, _) => { s.Count = 2; return Task.FromResult(s); })
            .AddConditionalEdge("start",
                s => s.Route,
                new Dictionary<string, string> { ["a"] = "branch-a", ["b"] = "branch-b" })
            .AddEdge("branch-a", "end")
            .AddEdge("branch-b", "end")
            .SetEntryPoint("start")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState { Route = "b" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(2);
    }

    // ── MaxSteps cycle detection ──────────────────────────────────────────────

    [Fact]
    public async Task Graph_ExceedingMaxSteps_ReturnsCycleDetectedError()
    {
        var graph = AgentGraph<CounterState>.Create()
            .WithMaxSteps(3)
            .AddNode("loop", (s, _, _) => { s.Count++; return Task.FromResult(s); })
            .AddEdge("loop", "loop")   // infinite cycle
            .SetEntryPoint("loop")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("MaxStepsCycleDetected");
        result.Error.Message.Should().Contain("3");
    }

    // ── GraphDeadEnd ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Graph_NodeWithNoEdge_ReturnsGraphDeadEndError()
    {
        var graph = AgentGraph<CounterState>.Create()
            .AddNode("step1", (s, _, _) => Task.FromResult(s))
            .AddNode("orphan", (s, _, _) => Task.FromResult(s))
            .AddEdge("step1", "orphan")
            // orphan has no outgoing edge and is not the end point
            .SetEntryPoint("step1")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("GraphDeadEnd");
        result.Error.Message.Should().Contain("orphan");
    }

    // ── Validation (InvalidGraph) ─────────────────────────────────────────────

    [Fact]
    public async Task Graph_EdgeTargetNotRegistered_ReturnsInvalidGraphError()
    {
        var graph = AgentGraph<CounterState>.Create()
            .AddNode("start", (s, _, _) => Task.FromResult(s))
            .AddEdge("start", "ghost")   // "ghost" is not registered
            .SetEntryPoint("start")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("InvalidGraph");
        result.Error.Message.Should().Contain("ghost");
    }

    [Fact]
    public async Task Graph_EntryPointNotRegistered_ReturnsInvalidGraphError()
    {
        var graph = AgentGraph<CounterState>.Create()
            .SetEntryPoint("nonexistent")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("InvalidGraph");
    }

    [Fact]
    public async Task Graph_ConditionalRouteTargetNotRegistered_ReturnsInvalidGraphError()
    {
        var graph = AgentGraph<CounterState>.Create()
            .AddNode("start", (s, _, _) => Task.FromResult(s))
            .AddConditionalEdge("start",
                _ => "a",
                new Dictionary<string, string> { ["a"] = "missing_node" })
            .SetEntryPoint("start")
            .SetEndPoint("end");

        var result = await graph.RunAsync(new CounterState());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("InvalidGraph");
        result.Error.Message.Should().Contain("missing_node");
    }

    // ── GraphContext ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Graph_NodeReceivesCorrectGraphContext()
    {
        string? capturedNode = null;
        int capturedStep = -1;

        var graph = AgentGraph<CounterState>.Create()
            .AddNode("only", (s, ctx, _) =>
            {
                capturedNode = ctx.CurrentNode;
                capturedStep = ctx.StepCount;
                return Task.FromResult(s);
            })
            .AddEdge("only", "end")
            .SetEntryPoint("only")
            .SetEndPoint("end");

        await graph.RunAsync(new CounterState());

        capturedNode.Should().Be("only");
        capturedStep.Should().Be(1);
    }
}
