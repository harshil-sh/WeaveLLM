using FluentAssertions;
using NSubstitute;
using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Tests.Fakes;
using Xunit;

namespace WeaveLLM.Core.Tests.Agents;

public class PlanAndExecuteAgentTests
{
    private static ToolRegistry EmptyRegistry() => new();

    [Fact]
    public async Task RunAsync_ReturnsFinalAnswer_WhenPlannerAndExecutorSucceed()
    {
        // Planner: first call → plan, second call → synthesis
        var planner = new SequentialMockChatModel(
            "1. Search for X\n2. Summarise",
            "The capital of France is Paris.");

        // Executor: returns a plain result for each step (no Action: prefix)
        var executor = new MockChatModel("Step executed successfully.");

        var agent = new PlanAndExecuteAgent(planner, executor, EmptyRegistry());

        var result = await agent.RunAsync("What is the capital of France?");

        result.IsSuccess.Should().BeTrue();
        result.Value!.FinalAnswer.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunAsync_ReturnsMaxStepsExceeded_WhenStepsExceedLimit()
    {
        // Planner returns a 5-step plan; maxSteps is 2 so step 3 triggers the limit
        var planner = new MockChatModel(
            "1. Step one\n2. Step two\n3. Step three\n4. Step four\n5. Step five");

        var executor = new MockChatModel("result");

        var agent = new PlanAndExecuteAgent(
            planner, executor, EmptyRegistry(), maxSteps: 2);

        var result = await agent.RunAsync("Some complex query");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("MaxStepsExceeded");
    }

    [Fact]
    public async Task RunAsync_CallsObserver_ForEachPhase()
    {
        var planner = new SequentialMockChatModel(
            "1. Search for X\n2. Summarise",
            "Final synthesis answer.");

        var executor = new MockChatModel("Step result.");

        var observer = Substitute.For<IAgentObserver>();
        observer
            .OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        observer
            .OnObservationAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        observer
            .OnFinalAnswerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var agent = new PlanAndExecuteAgent(planner, executor, EmptyRegistry(), observer);

        var result = await agent.RunAsync("What is X?");

        result.IsSuccess.Should().BeTrue();

        // OnThoughtAsync is called exactly once — for the plan summary (stepNumber: 0)
        await observer.Received(1)
            .OnThoughtAsync(Arg.Any<string>(), 0, Arg.Any<CancellationToken>());

        // OnFinalAnswerAsync is called exactly once after synthesis
        await observer.Received(1)
            .OnFinalAnswerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
