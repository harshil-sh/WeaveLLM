#nullable enable
using System.Text.Json;
using WeaveLLM.Core.Agents.Observers;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// A two-model agent that first generates a step-by-step plan, executes each step
/// (optionally invoking tools), then synthesises a final answer from the collected results.
/// </summary>
/// <remarks>
/// <para>Phase 1 — Plan: the planner model decomposes the question into numbered steps.</para>
/// <para>Phase 2 — Execute: the executor model works through each step, invoking tools where needed.</para>
/// <para>Phase 3 — Synthesise: the planner model combines all step results into a final answer.</para>
/// </remarks>
public sealed class PlanAndExecuteAgent : IAgent
{
    private readonly IChatModel _planner;
    private readonly IChatModel _executor;
    private readonly IToolRegistry _tools;
    private readonly IAgentObserver _observer;
    private readonly int _maxSteps;

    /// <inheritdoc/>
    public string Name => "PlanAndExecuteAgent";

    /// <inheritdoc/>
    public string Description =>
        "Decomposes a question into a numbered plan, executes each step using tools, then synthesises the answer.";

    /// <summary>
    /// Creates a new <see cref="PlanAndExecuteAgent"/>.
    /// </summary>
    /// <param name="planner">The chat model used for planning and final synthesis.</param>
    /// <param name="executor">The chat model used to execute individual plan steps.</param>
    /// <param name="tools">The tool registry available during execution.</param>
    /// <param name="observer">
    /// Optional observer for real-time notifications.
    /// Defaults to <see cref="NullAgentObserver.Instance"/>.
    /// </param>
    /// <param name="maxSteps">
    /// Maximum total steps before aborting with <c>MaxStepsExceeded</c>. Defaults to 20.
    /// </param>
    public PlanAndExecuteAgent(
        IChatModel planner,
        IChatModel executor,
        IToolRegistry tools,
        IAgentObserver? observer = null,
        int maxSteps = 20)
    {
        _planner = planner;
        _executor = executor;
        _tools = tools;
        _observer = observer ?? NullAgentObserver.Instance;
        _maxSteps = maxSteps;
    }

    /// <inheritdoc/>
    public async Task<ChainResult<AgentResult>> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        // ── Phase 1: Plan ──────────────────────────────────────────────────────────────
        var planResult = await _planner.ChatAsync(
            [Message.System("You are a planning assistant."),
             Message.User($"Create a numbered plan to answer: {input}\n" +
                          "Return ONLY a numbered list. Example:\n1. Search for X\n2. Calculate Y")],
            LLMOptions.Deterministic(),
            cancellationToken).ConfigureAwait(false);

        if (!planResult.IsSuccess)
            return ChainResult<AgentResult>.Failure(planResult.Error!);

        var planSteps = ParsePlan(planResult.Value!.Content);
        if (planSteps.Count == 0)
            return ChainResult<AgentResult>.Failure(
                "Planner returned an empty plan.", "InvalidPlan");

        await _observer.OnThoughtAsync(
            $"Plan ({planSteps.Count} steps): " + string.Join("; ", planSteps),
            stepNumber: 0, cancellationToken).ConfigureAwait(false);

        // ── Phase 2: Execute ───────────────────────────────────────────────────────────
        var agentSteps = new List<AgentStep>();
        var stepResults = new List<(string StepText, string Result)>();
        var totalSteps = 0;
        int? totalPrompt = null, totalCompletion = null;

        AccumulateUsage(planResult.Value!.Usage, ref totalPrompt, ref totalCompletion);

        var toolsJson = JsonSerializer.Serialize(
            _tools.GetAll().Select(t => new { name = t.Name, description = t.Description }));

        for (var i = 0; i < planSteps.Count; i++)
        {
            totalSteps++;
            if (totalSteps > _maxSteps)
                return ChainResult<AgentResult>.Failure(
                    $"Agent hit {_maxSteps} steps without completing execution. Increase maxSteps or simplify the task.",
                    "MaxStepsExceeded");

            var stepText = planSteps[i];
            var execResult = await _executor.ChatAsync(
                [Message.System($"You are a task executor. Available tools: {toolsJson}"),
                 Message.User(stepText)],
                LLMOptions.Deterministic(),
                cancellationToken).ConfigureAwait(false);

            if (!execResult.IsSuccess)
                return ChainResult<AgentResult>.Failure(execResult.Error!);

            AccumulateUsage(execResult.Value!.Usage, ref totalPrompt, ref totalCompletion);

            var execResponse = execResult.Value.Content;
            var stepResult = execResponse;

            // Attempt tool call if the executor requested one
            var toolName = Extract(execResponse, "Action:");
            var toolInput = Extract(execResponse, "Action Input:");
            if (toolName is not null)
            {
                string observation;
                if (_tools.TryGet(toolName, out var toolDef) && toolDef is not null)
                {
                    observation = await toolDef
                        .Executor(toolInput ?? "{}", cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    observation = $"Error: Tool '{toolName}' is not registered.";
                }

                stepResult = observation;
                agentSteps.Add(new AgentStep(
                    Thought: $"Execute: {stepText}",
                    ToolName: toolName,
                    ToolInput: toolInput,
                    ToolOutput: observation));
            }
            else
            {
                agentSteps.Add(new AgentStep(
                    Thought: $"Execute: {stepText}",
                    ToolOutput: stepResult));
            }

            stepResults.Add((stepText, stepResult));
            await _observer.OnObservationAsync(stepResult, totalSteps, cancellationToken).ConfigureAwait(false);
        }

        // ── Phase 3: Synthesise ────────────────────────────────────────────────────────
        var resultsText = string.Join("\n", stepResults.Select(
            (sr, idx) => $"{idx + 1}. {sr.StepText}\n   Result: {sr.Result}"));

        var synthesisResult = await _planner.ChatAsync(
            [Message.System("You are a synthesis assistant. Combine the results into a clear final answer."),
             Message.User($"Given these results, answer the original question.\n" +
                          $"Question: {input}\nResults:\n{resultsText}")],
            LLMOptions.Balanced(),
            cancellationToken).ConfigureAwait(false);

        if (!synthesisResult.IsSuccess)
            return ChainResult<AgentResult>.Failure(synthesisResult.Error!);

        AccumulateUsage(synthesisResult.Value!.Usage, ref totalPrompt, ref totalCompletion);

        var finalAnswer = synthesisResult.Value.Content.Trim();
        await _observer.OnFinalAnswerAsync(finalAnswer, totalSteps, cancellationToken).ConfigureAwait(false);

        // Add plan step summary at the start
        agentSteps.Insert(0, new AgentStep(
            Thought: $"Plan: {string.Join("; ", planSteps)}"));

        return ChainResult<AgentResult>.Success(new AgentResult
        {
            FinalAnswer = finalAnswer,
            Steps = agentSteps,
            TotalUsage = totalPrompt.HasValue
                ? new UsageStats(totalPrompt.Value, totalCompletion!.Value,
                    totalPrompt.Value + totalCompletion!.Value)
                : null
        });
    }

    private static List<string> ParsePlan(string planText)
    {
        var steps = new List<string>();
        foreach (var line in planText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Strip leading "N." or "N)" or "N:" prefix
            var dotIdx = trimmed.IndexOfAny(['.', ')', ':']);
            if (dotIdx > 0 && int.TryParse(trimmed[..dotIdx], out _))
                trimmed = trimmed[(dotIdx + 1)..].Trim();

            if (!string.IsNullOrEmpty(trimmed))
                steps.Add(trimmed);
        }
        return steps;
    }

    private static string? Extract(string text, string header)
    {
        var idx = text.IndexOf(header, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + header.Length;
        var end = text.IndexOf('\n', start);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }

    private static void AccumulateUsage(
        UsageStats? usage, ref int? totalPrompt, ref int? totalCompletion)
    {
        if (usage is null) return;
        totalPrompt = (totalPrompt ?? 0) + usage.PromptTokens;
        totalCompletion = (totalCompletion ?? 0) + usage.CompletionTokens;
    }
}
