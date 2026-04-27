using WeaveLLM.Core.Chains;

namespace WeaveLLM.Core.Agents.Observers;

/// <summary>
/// A no-op observer. Use as the default when no observation is needed.
/// </summary>
public sealed class NullAgentObserver : IAgentObserver
{
    public static readonly NullAgentObserver Instance = new();

    private NullAgentObserver() { }

    public Task OnThoughtAsync(string thought, int stepNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OnActionAsync(string toolName, string toolInput, int stepNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OnObservationAsync(string observation, int stepNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OnFinalAnswerAsync(string answer, int totalSteps, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OnErrorAsync(ChainError error, int stepNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
