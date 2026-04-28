using FluentAssertions;
using NSubstitute;
using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Agents.Observers;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;
using WeaveLLM.Core.Tests.Fakes;
using Xunit;

namespace WeaveLLM.Core.Tests.Agents;

public class ReActAgentTests
{
    private const string ActionResponse =
        "Thought: I need to look something up\nAction: my_tool\nAction Input: {\"query\":\"test\"}";

    private const string FinalAnswerResponse =
        "Thought: I now have the answer\nFinal Answer: The answer is 42";

    private static ToolRegistry BuildRegistry(string toolOutput = "tool result")
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition(
            "my_tool", "A test tool", default,
            (_, _) => Task.FromResult(toolOutput)));
        return registry;
    }

    [Fact]
    public async Task RunAsync_FinalAnswerOnSecondStep_ReturnsCorrectResult()
    {
        var model = new SequentialMockChatModel(ActionResponse, FinalAnswerResponse);
        var agent = new ReActAgent(model, BuildRegistry());

        var result = await agent.RunAsync("What is the answer?");

        result.IsSuccess.Should().BeTrue();
        result.Value!.FinalAnswer.Should().Be("The answer is 42");
    }

    [Fact]
    public async Task RunAsync_FinalAnswerOnSecondStep_StepsContainsOnlyActionStep()
    {
        var model = new SequentialMockChatModel(ActionResponse, FinalAnswerResponse);
        var agent = new ReActAgent(model, BuildRegistry());

        var result = await agent.RunAsync("What is the answer?");

        result.Value!.Steps.Should().HaveCount(1);
        var step = result.Value.Steps[0];
        step.ToolName.Should().Be("my_tool");
        step.ToolInput.Should().Be("{\"query\":\"test\"}");
        step.ToolOutput.Should().Be("tool result");
    }

    [Fact]
    public async Task RunAsync_FinalAnswerImmediate_ReturnsWithZeroSteps()
    {
        var model = new SequentialMockChatModel(FinalAnswerResponse);
        var agent = new ReActAgent(model, BuildRegistry());

        var result = await agent.RunAsync("What?");

        result.IsSuccess.Should().BeTrue();
        result.Value!.FinalAnswer.Should().Be("The answer is 42");
        result.Value.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MaxStepsExceeded_ReturnsMaxStepsExceededError()
    {
        var model = new SequentialMockChatModel(ActionResponse);
        var agent = new ReActAgent(model, BuildRegistry(), maxSteps: 2);

        var result = await agent.RunAsync("Infinite question");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("MaxStepsExceeded");
        result.Error.Message.Should().Contain("2 steps");
    }

    [Fact]
    public async Task RunAsync_ObserverReceivesThoughtAndAction()
    {
        var model = new SequentialMockChatModel(ActionResponse, FinalAnswerResponse);
        var observer = Substitute.For<IAgentObserver>();
        observer.OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        observer.OnActionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        observer.OnObservationAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        observer.OnFinalAnswerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var agent = new ReActAgent(model, BuildRegistry(), observer: observer);
        await agent.RunAsync("test");

        await observer.Received(1).OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await observer.Received(1).OnActionAsync("my_tool", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await observer.Received(1).OnObservationAsync("tool result", Arg.Any<int>(), Arg.Any<CancellationToken>());
        await observer.Received(1).OnFinalAnswerAsync("The answer is 42", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ObserverReceivesError_OnMaxSteps()
    {
        var model = new SequentialMockChatModel(ActionResponse);
        var observer = Substitute.For<IAgentObserver>();
        observer.OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        observer.OnActionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        observer.OnObservationAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        observer.OnErrorAsync(Arg.Any<ChainError>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var agent = new ReActAgent(model, BuildRegistry(), observer: observer, maxSteps: 1);
        await agent.RunAsync("test");

        await observer.Received(1).OnErrorAsync(
            Arg.Is<ChainError>(e => e.Code == "MaxStepsExceeded"),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_UnknownTool_ObservationContainsError()
    {
        const string unknownToolResponse =
            "Thought: Try unknown\nAction: unknown_tool\nAction Input: {}";
        var model = new SequentialMockChatModel(unknownToolResponse, FinalAnswerResponse);
        var agent = new ReActAgent(model, BuildRegistry());

        var result = await agent.RunAsync("test");

        result.IsSuccess.Should().BeTrue();
        var step = result.Value!.Steps.First();
        step.ToolOutput.Should().Contain("not registered");
    }

    [Fact]
    public async Task RunAsync_AccumulatesUsageStats()
    {
        var model = new SequentialMockChatModel(ActionResponse, FinalAnswerResponse);
        var agent = new ReActAgent(model, BuildRegistry());

        var result = await agent.RunAsync("test");

        // Each SequentialMockChatModel call returns Usage(10, 20, 30); 2 calls → 20 prompt, 40 completion
        result.Value!.TotalUsage.Should().NotBeNull();
        result.Value.TotalUsage!.PromptTokens.Should().Be(20);
        result.Value.TotalUsage.CompletionTokens.Should().Be(40);
    }

    [Fact]
    public async Task RunAsync_ModelFailure_PropagatesError()
    {
        var error = new WeaveLLMError("Provider down", "PROVIDER_ERROR");
        var model = new MockChatModel(errorToReturn: error);
        var agent = new ReActAgent(model, BuildRegistry());

        var result = await agent.RunAsync("test");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public void DefaultObserver_IsNullAgentObserver()
    {
        var model = new MockChatModel();
        var agent = new ReActAgent(model, BuildRegistry());
        // No observer passed — should not throw and use NullAgentObserver
        agent.MaxSteps.Should().Be(10);
    }
}
