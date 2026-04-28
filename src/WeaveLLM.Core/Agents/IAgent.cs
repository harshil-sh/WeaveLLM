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


