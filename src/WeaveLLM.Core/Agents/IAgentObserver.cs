using WeaveLLM.Core.Chains;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Receives real-time notifications from an agent's reasoning loop.
/// Implement this to add logging, UI updates, tracing, or cost tracking.
/// </summary>
public interface IAgentObserver
{
    Task OnThoughtAsync(string thought, int stepNumber, CancellationToken cancellationToken = default);
    Task OnActionAsync(string toolName, string toolInput, int stepNumber, CancellationToken cancellationToken = default);
    Task OnObservationAsync(string observation, int stepNumber, CancellationToken cancellationToken = default);
    Task OnFinalAnswerAsync(string answer, int totalSteps, CancellationToken cancellationToken = default);
    Task OnErrorAsync(ChainError error, int stepNumber, CancellationToken cancellationToken = default);
}
