using WeaveLLM.Core.Chains;

namespace WeaveLLM.Core.Agents.Observers;

/// <summary>
/// Writes agent reasoning steps to the console using distinct colors per event type.
/// </summary>
public sealed class ConsoleAgentObserver : IAgentObserver
{
    public Task OnThoughtAsync(string thought, int stepNumber, CancellationToken cancellationToken = default)
    {
        WriteColored(ConsoleColor.DarkCyan, $"[Step {stepNumber}] Thought: {thought}");
        return Task.CompletedTask;
    }

    public Task OnActionAsync(string toolName, string toolInput, int stepNumber, CancellationToken cancellationToken = default)
    {
        WriteColored(ConsoleColor.Yellow, $"[Step {stepNumber}] Action: {toolName} | Input: {toolInput}");
        return Task.CompletedTask;
    }

    public Task OnObservationAsync(string observation, int stepNumber, CancellationToken cancellationToken = default)
    {
        WriteColored(ConsoleColor.Gray, $"[Step {stepNumber}] Observation: {observation}");
        return Task.CompletedTask;
    }

    public Task OnFinalAnswerAsync(string answer, int totalSteps, CancellationToken cancellationToken = default)
    {
        WriteColored(ConsoleColor.Green, $"[Final Answer after {totalSteps} steps] {answer}");
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(ChainError error, int stepNumber, CancellationToken cancellationToken = default)
    {
        WriteColored(ConsoleColor.Red, $"[Step {stepNumber}] Error ({error.Code}): {error.Message}");
        return Task.CompletedTask;
    }

    private static void WriteColored(ConsoleColor color, string message)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }
}
