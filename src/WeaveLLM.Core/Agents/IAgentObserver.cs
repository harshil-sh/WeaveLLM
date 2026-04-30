using WeaveLLM.Core.Chains;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Receives real-time notifications from an agent's reasoning loop.
/// Implement this to add logging, UI updates, tracing, or cost tracking.
/// </summary>
public interface IAgentObserver
{
    /// <summary>Called when the agent produces an internal reasoning thought.</summary>
    /// <param name="thought">The agent's reasoning text for this step.</param>
    /// <param name="stepNumber">Zero-based index of the current reasoning step.</param>
    /// <param name="cancellationToken">Token to cancel the notification.</param>
    Task OnThoughtAsync(string thought, int stepNumber, CancellationToken cancellationToken = default);

    /// <summary>Called when the agent decides to invoke a tool.</summary>
    /// <param name="toolName">The registered name of the tool being invoked.</param>
    /// <param name="toolInput">JSON-encoded arguments passed to the tool.</param>
    /// <param name="stepNumber">Zero-based index of the current reasoning step.</param>
    /// <param name="cancellationToken">Token to cancel the notification.</param>
    Task OnActionAsync(string toolName, string toolInput, int stepNumber, CancellationToken cancellationToken = default);

    /// <summary>Called when the agent receives a tool's result and adds it to its context.</summary>
    /// <param name="observation">The text output returned by the tool.</param>
    /// <param name="stepNumber">Zero-based index of the current reasoning step.</param>
    /// <param name="cancellationToken">Token to cancel the notification.</param>
    Task OnObservationAsync(string observation, int stepNumber, CancellationToken cancellationToken = default);

    /// <summary>Called once when the agent emits its conclusive answer and the loop terminates.</summary>
    /// <param name="answer">The agent's final natural-language answer.</param>
    /// <param name="totalSteps">Total number of reasoning steps taken to reach this answer.</param>
    /// <param name="cancellationToken">Token to cancel the notification.</param>
    Task OnFinalAnswerAsync(string answer, int totalSteps, CancellationToken cancellationToken = default);

    /// <summary>Called when an unrecoverable error halts the agent's reasoning loop.</summary>
    /// <param name="error">Structured error describing what failed.</param>
    /// <param name="stepNumber">Zero-based index of the step at which the error occurred.</param>
    /// <param name="cancellationToken">Token to cancel the notification.</param>
    Task OnErrorAsync(ChainError error, int stepNumber, CancellationToken cancellationToken = default);
}
