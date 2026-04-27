using WeaveLLM.Core.Chains;

namespace WeaveLLM.Core.Agents.Observers;

/// <summary>
/// Fans out each agent event to multiple observers via <see cref="Task.WhenAll"/>.
/// A failing observer is swallowed so it does not prevent others from receiving the event.
/// </summary>
public sealed class CompositeAgentObserver : IAgentObserver
{
    private readonly IReadOnlyList<IAgentObserver> _observers;

    public CompositeAgentObserver(IReadOnlyList<IAgentObserver> observers)
    {
        _observers = observers;
    }

    public Task OnThoughtAsync(string thought, int stepNumber, CancellationToken cancellationToken = default)
        => FanOutAsync(o => o.OnThoughtAsync(thought, stepNumber, cancellationToken));

    public Task OnActionAsync(string toolName, string toolInput, int stepNumber, CancellationToken cancellationToken = default)
        => FanOutAsync(o => o.OnActionAsync(toolName, toolInput, stepNumber, cancellationToken));

    public Task OnObservationAsync(string observation, int stepNumber, CancellationToken cancellationToken = default)
        => FanOutAsync(o => o.OnObservationAsync(observation, stepNumber, cancellationToken));

    public Task OnFinalAnswerAsync(string answer, int totalSteps, CancellationToken cancellationToken = default)
        => FanOutAsync(o => o.OnFinalAnswerAsync(answer, totalSteps, cancellationToken));

    public Task OnErrorAsync(ChainError error, int stepNumber, CancellationToken cancellationToken = default)
        => FanOutAsync(o => o.OnErrorAsync(error, stepNumber, cancellationToken));

    private Task FanOutAsync(Func<IAgentObserver, Task> action)
    {
        var tasks = _observers.Select(async observer =>
        {
            try { await action(observer).ConfigureAwait(false); }
            catch { /* swallow so other observers still run */ }
        });
        return Task.WhenAll(tasks);
    }
}
