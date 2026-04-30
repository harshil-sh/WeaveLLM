#nullable enable
using FluentAssertions;
using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Agents.Tools;
using WeaveLLM.IntegrationTests.Fakes;
using Xunit;

namespace WeaveLLM.IntegrationTests.Agents;

public class ReActAgentIntegrationTests
{
    private static ToolRegistry BuildRegistryWithCalculator()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new CalculatorTool());
        return registry;
    }

    [IntegrationFact]
    public async Task ReActAgent_WithCalculatorTool_SolvesSimpleMath()
    {
        const string actionResponse =
            "Thought: I need to calculate 142 * 37\nAction: calculate\nAction Input: {\"expression\":\"142*37\"}";
        const string finalResponse =
            "Thought: I have the answer\nFinal Answer: 5254";

        var model = new SequentialMockChatModel(actionResponse, finalResponse);
        var agent = new ReActAgent(model, BuildRegistryWithCalculator());

        var result = await agent.RunAsync("What is 142 multiplied by 37?");

        result.IsSuccess.Should().BeTrue();
        result.Value!.FinalAnswer.Should().Contain("5254");
    }

    [IntegrationFact]
    public async Task ReActAgent_MaxStepsExceeded_ReturnsError()
    {
        const string alwaysAction =
            "Thought: Still thinking\nAction: calculate\nAction Input: {\"expression\":\"1+1\"}";

        var model = new MockChatModel(alwaysAction);
        var agent = new ReActAgent(model, BuildRegistryWithCalculator(), maxSteps: 3);

        var result = await agent.RunAsync("A question that never resolves.");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("MaxStepsExceeded");
    }
}
