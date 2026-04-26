#nullable enable
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Core agent interface. Agents perceive → reason → act in a loop until they reach a final answer.
/// </summary>
public interface IAgent
{
    /// <summary>A stable, human-readable name for this agent instance.</summary>
    string Name { get; }

    /// <summary>A brief description of what this agent does, shown in logs and UIs.</summary>
    string Description { get; }

    /// <summary>
    /// Runs the agent to completion and returns its final answer.
    /// </summary>
    /// <param name="input">The natural-language task or question for the agent.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>A <see cref="ChainResult{T}"/> containing the <see cref="AgentResult"/> on success.</returns>
    Task<ChainResult<AgentResult>> RunAsync(string input, CancellationToken cancellationToken = default);
}

/// <summary>
/// The complete outcome of a finished agent run.
/// </summary>
public sealed class AgentResult
{
    /// <summary>The agent's final natural-language answer.</summary>
    public required string FinalAnswer { get; init; }

    /// <summary>The ordered list of reasoning steps the agent took to reach <see cref="FinalAnswer"/>.</summary>
    public required IReadOnlyList<AgentStep> Steps { get; init; }

    /// <summary>Aggregated token usage across all model calls in this run. May be <c>null</c> if the model does not report usage.</summary>
    public UsageStats? TotalUsage { get; init; }
}

/// <summary>
/// A single thought/action/observation step in the agent's reasoning loop.
/// </summary>
/// <param name="Thought">The agent's internal reasoning at this step.</param>
/// <param name="ToolName">The tool invoked, if any.</param>
/// <param name="ToolInput">The JSON arguments passed to the tool, if any.</param>
/// <param name="ToolOutput">The tool's text result, if any.</param>
public sealed record AgentStep(
    string Thought,
    string? ToolName = null,
    string? ToolInput = null,
    string? ToolOutput = null);

/// <summary>
/// ReAct (Reasoning + Acting) agent implementation.
/// Loops: Thought → Action → Observation until the model produces a Final Answer.
/// </summary>
public sealed class ReActAgent : IAgent
{
    private readonly IChatModel _model;
    private readonly IToolRegistry _toolRegistry;

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description => "ReAct agent: reasons step-by-step and uses tools to answer questions.";

    /// <param name="model">The chat model to use for reasoning.</param>
    /// <param name="toolRegistry">Registry of tools the agent may invoke.</param>
    /// <param name="name">Optional display name for this agent instance.</param>
    public ReActAgent(IChatModel model, IToolRegistry toolRegistry, string name = "ReActAgent")
    {
        _model = model;
        _toolRegistry = toolRegistry;
        Name = name;
    }

    /// <inheritdoc />
    public async Task<ChainResult<AgentResult>> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<AgentStep>();
        var messages = BuildInitialMessages(input);
        int? totalPrompt = null, totalCompletion = null;

        const int maxIterations = 10;
        for (var i = 0; i < maxIterations; i++)
        {
            var result = await _model.ChatAsync(messages, new LLMOptions { Temperature = 0.2f }, cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
                return ChainResult<AgentResult>.Failure(result.Error!);

            if (result.Value!.Usage is { } usage)
            {
                totalPrompt = (totalPrompt ?? 0) + usage.PromptTokens;
                totalCompletion = (totalCompletion ?? 0) + usage.CompletionTokens;
            }

            var response = result.Value.Content;
            var (thought, toolName, toolInput, isFinal) = ParseResponse(response);

            if (isFinal)
            {
                var usageStats = totalPrompt.HasValue
                    ? new UsageStats(totalPrompt.Value, totalCompletion!.Value, totalPrompt.Value + totalCompletion!.Value)
                    : null;

                steps.Add(new AgentStep(thought));
                return ChainResult<AgentResult>.Success(new AgentResult
                {
                    FinalAnswer = thought,
                    Steps = steps,
                    TotalUsage = usageStats
                });
            }

            string observation;
            if (toolName is not null && _toolRegistry.TryGet(toolName, out var toolDef) && toolDef is not null)
            {
                try
                {
                    observation = await toolDef.Executor(toolInput ?? "{}", cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    observation = $"Error: {ex.Message}";
                }
            }
            else if (toolName is not null)
            {
                observation = $"Error: Tool '{toolName}' is not registered.";
            }
            else
            {
                observation = "No action taken.";
            }

            steps.Add(new AgentStep(thought, toolName, toolInput, observation));
            messages.Add(Message.Assistant(response));
            messages.Add(Message.User($"Observation: {observation}"));
        }

        return ChainResult<AgentResult>.Failure(
            $"Agent reached the maximum iteration limit ({maxIterations}) without a final answer.",
            "AGENT_MAX_ITERATIONS");
    }

    private List<Message> BuildInitialMessages(string input)
    {
        var tools = _toolRegistry.GetAll();
        var toolDescriptions = string.Join("\n", tools.Select(t => $"- {t.Name}: {t.Description}"));

        var systemPrompt = $"""
            You are an AI assistant that uses tools to answer questions step-by-step.

            Available tools:
            {toolDescriptions}

            Respond using this exact format:
            Thought: <your reasoning>
            Action: <tool_name>
            Action Input: <json arguments>

            When you have the final answer, respond with:
            Thought: I now know the final answer.
            Final Answer: <your complete answer>
            """;

        return [Message.System(systemPrompt), Message.User(input)];
    }

    private static (string thought, string? toolName, string? toolInput, bool isFinal) ParseResponse(string response)
    {
        if (response.Contains("Final Answer:"))
        {
            var answer = response[(response.IndexOf("Final Answer:") + 13)..].Trim();
            return (answer, null, null, true);
        }

        var thought = Extract(response, "Thought:") ?? response;
        var action = Extract(response, "Action:");
        var actionInput = Extract(response, "Action Input:");
        return (thought, action, actionInput, false);
    }

    private static string? Extract(string text, string header)
    {
        var idx = text.IndexOf(header, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + header.Length;
        var end = text.IndexOf('\n', start);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }
}
