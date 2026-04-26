using WeaveLLM.Core.Models;
using WeaveLLM.Core.Providers;
using WeaveLLM.Core.Tools;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Core agent interface. Agents perceive → reason → act in a loop.
/// </summary>
public interface IAgent
{
    string Name { get; }
    string Description { get; }

    Task<AgentResult> RunAsync(string input, AgentRunOptions? options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentStep> StreamAsync(string input, AgentRunOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for a single agent run.
/// </summary>
public sealed class AgentRunOptions
{
    public int MaxIterations { get; set; } = 10;
    public bool VerboseLogging { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, object> Context { get; set; } = new();
    public IReadOnlyList<Message>? ConversationHistory { get; set; }
}

/// <summary>
/// The complete result of an agent run.
/// </summary>
public sealed class AgentResult
{
    public bool IsSuccess { get; init; }
    public string FinalAnswer { get; init; } = string.Empty;
    public IReadOnlyList<AgentStep> Steps { get; init; } = [];
    public TokenUsage TotalTokenUsage { get; init; } = new();
    public string? ErrorMessage { get; init; }
    public int IterationsUsed { get; init; }

    public static AgentResult Success(string answer, IReadOnlyList<AgentStep> steps, TokenUsage usage) =>
        new() { IsSuccess = true, FinalAnswer = answer, Steps = steps, TotalTokenUsage = usage, IterationsUsed = steps.Count };

    public static AgentResult Failure(string error, IReadOnlyList<AgentStep> steps) =>
        new() { IsSuccess = false, ErrorMessage = error, Steps = steps };
}

/// <summary>
/// A single thought/action/observation step in the agent loop.
/// </summary>
public sealed record AgentStep(
    int Iteration,
    string Thought,
    string? ActionName = null,
    string? ActionInput = null,
    string? Observation = null,
    bool IsFinalAnswer = false,
    DateTimeOffset Timestamp = default);

/// <summary>
/// ReAct (Reasoning + Acting) agent implementation.
/// Loops: Thought → Action → Observation until Final Answer.
/// </summary>
public sealed class ReActAgent(
    IChatModel model,
    IToolRegistry toolRegistry,
    string name = "ReActAgent") : IAgent
{
    public string Name { get; } = name;
    public string Description => "ReAct agent: reasons step-by-step and uses tools to answer questions.";

    public async Task<AgentResult> RunAsync(string input, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new AgentRunOptions();
        var steps = new List<AgentStep>();
        var totalUsage = new TokenUsage();
        var messages = BuildInitialMessages(input);

        for (var i = 0; i < options.MaxIterations; i++)
        {
            var result = await model.ChatAsync(messages, new LLMOptions { Temperature = 0.2f }, cancellationToken);
            if (!result.IsSuccess)
                return AgentResult.Failure(result.Error!.Message, steps);

            totalUsage += result.TokenUsage;
            var response = result.Value!.Content;
            var step = ParseStep(i + 1, response);
            steps.Add(step);

            if (step.IsFinalAnswer)
                return AgentResult.Success(step.Thought, steps, totalUsage);

            if (step.ActionName is not null)
            {
                var toolResult = await toolRegistry.InvokeAsync(step.ActionName, step.ActionInput ?? "{}", new ChainContext(), cancellationToken);
                var observation = toolResult.IsSuccess ? toolResult.Output : $"Error: {toolResult.ErrorMessage}";
                messages.Add(Message.Assistant(response));
                messages.Add(Message.User($"Observation: {observation}"));
                steps[^1] = step with { Observation = observation };
            }
        }

        return AgentResult.Failure($"Agent reached max iterations ({options.MaxIterations}) without a final answer.", steps);
    }

    public async IAsyncEnumerable<AgentStep> StreamAsync(string input, AgentRunOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(input, options, cancellationToken);
        foreach (var step in result.Steps)
            yield return step;
    }

    private List<Message> BuildInitialMessages(string input)
    {
        var tools = toolRegistry.GetAll();
        var toolDescriptions = string.Join("\n", tools.Select(t => $"- {t.Name}: {t.Description}"));
        var systemPrompt = $"""
            You are an AI assistant that uses tools to answer questions step-by-step.

            Available tools:
            {toolDescriptions}

            Format:
            Thought: <reasoning>
            Action: <tool_name>
            Action Input: <json arguments>

            When you have the final answer:
            Thought: I now know the answer.
            Final Answer: <your answer>
            """;

        return [Message.System(systemPrompt), Message.User(input)];
    }

    private static AgentStep ParseStep(int iteration, string response)
    {
        if (response.Contains("Final Answer:"))
        {
            var answer = response[(response.IndexOf("Final Answer:") + 13)..].Trim();
            return new AgentStep(iteration, answer, IsFinalAnswer: true, Timestamp: DateTimeOffset.UtcNow);
        }

        var thought = ExtractSection(response, "Thought:") ?? response;
        var action = ExtractSection(response, "Action:");
        var actionInput = ExtractSection(response, "Action Input:");

        return new AgentStep(iteration, thought, action, actionInput, Timestamp: DateTimeOffset.UtcNow);
    }

    private static string? ExtractSection(string text, string header)
    {
        var idx = text.IndexOf(header, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + header.Length;
        var end = text.IndexOf('\n', start);
        return end < 0 ? text[start..].Trim() : text[start..end].Trim();
    }
}
