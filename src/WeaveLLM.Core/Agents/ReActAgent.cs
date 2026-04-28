#nullable enable
using System.Text.Json;
using WeaveLLM.Core.Agents.Observers;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;
using WeaveLLM.Core.Prompts;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// ReAct (Reasoning + Acting) agent that loops Thought → Action → Observation
/// until the model emits a Final Answer or <see cref="MaxSteps"/> is exceeded.
/// </summary>
public sealed class ReActAgent : IAgent
{
    private readonly IChatModel _model;
    private readonly IToolRegistry _tools;
    private readonly IAgentObserver _observer;
    private readonly IPromptTemplate _systemPrompt;
    private readonly int _maxSteps;

    /// <inheritdoc/>
    public string Name => "ReActAgent";

    /// <inheritdoc/>
    public string Description => "ReAct agent: reasons step-by-step and uses tools to answer questions.";

    /// <summary>The maximum number of reasoning steps before aborting with <c>MaxStepsExceeded</c>.</summary>
    public int MaxSteps => _maxSteps;

    /// <summary>
    /// Creates a new <see cref="ReActAgent"/>.
    /// </summary>
    /// <param name="model">The chat model used for reasoning.</param>
    /// <param name="tools">The tool registry the agent may invoke.</param>
    /// <param name="observer">
    /// Optional observer for real-time step notifications.
    /// Defaults to <see cref="NullAgentObserver.Instance"/>.
    /// </param>
    /// <param name="systemPrompt">
    /// Optional system prompt override.
    /// Defaults to <see cref="PromptTemplateLibrary.ReActSystemPrompt"/>.
    /// </param>
    /// <param name="maxSteps">Maximum reasoning steps before giving up. Defaults to 10.</param>
    public ReActAgent(
        IChatModel model,
        IToolRegistry tools,
        IAgentObserver? observer = null,
        IPromptTemplate? systemPrompt = null,
        int maxSteps = 10)
    {
        _model = model;
        _tools = tools;
        _observer = observer ?? NullAgentObserver.Instance;
        _systemPrompt = systemPrompt ?? PromptTemplateLibrary.ReActSystemPrompt;
        _maxSteps = maxSteps;
    }

    /// <inheritdoc/>
    public async Task<ChainResult<AgentResult>> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var toolsJson = JsonSerializer.Serialize(
            _tools.GetAll().Select(t => new { name = t.Name, description = t.Description }));

        var renderedSystem = _systemPrompt.Render(
            new Dictionary<string, object> { ["tools"] = toolsJson });

        var messages = new List<Message>
        {
            Message.System(renderedSystem),
            Message.User(input)
        };

        var steps = new List<AgentStep>();
        int? totalPrompt = null, totalCompletion = null;

        for (var step = 0; step < _maxSteps; step++)
        {
            var result = await _model
                .ChatAsync(messages, LLMOptions.Deterministic(), cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                await _observer
                    .OnErrorAsync(new ChainError("ProviderError", result.Error!.Message), step, cancellationToken)
                    .ConfigureAwait(false);
                return ChainResult<AgentResult>.Failure(result.Error!);
            }

            if (result.Value!.Usage is { } usage)
            {
                totalPrompt = (totalPrompt ?? 0) + usage.PromptTokens;
                totalCompletion = (totalCompletion ?? 0) + usage.CompletionTokens;
            }

            var response = result.Value.Content;
            var (thought, toolName, toolInput, isFinal) = ParseResponse(response);

            if (isFinal)
            {
                await _observer
                    .OnFinalAnswerAsync(thought, step + 1, cancellationToken)
                    .ConfigureAwait(false);

                return ChainResult<AgentResult>.Success(new AgentResult
                {
                    FinalAnswer = thought,
                    Steps = steps,
                    TotalUsage = totalPrompt.HasValue
                        ? new UsageStats(totalPrompt.Value, totalCompletion!.Value,
                            totalPrompt.Value + totalCompletion!.Value)
                        : null
                });
            }

            await _observer
                .OnThoughtAsync(thought, step, cancellationToken)
                .ConfigureAwait(false);

            string observation;
            if (toolName is not null)
            {
                await _observer
                    .OnActionAsync(toolName, toolInput ?? "{}", step, cancellationToken)
                    .ConfigureAwait(false);

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
            }
            else
            {
                observation = "No action taken.";
            }

            await _observer
                .OnObservationAsync(observation, step, cancellationToken)
                .ConfigureAwait(false);

            steps.Add(new AgentStep(thought, toolName, toolInput, observation));
            messages.Add(Message.Assistant(response));
            messages.Add(Message.User($"Observation: {observation}"));
        }

        var maxStepsError = new ChainError(
            "MaxStepsExceeded",
            $"Agent hit {_maxSteps} steps without a Final Answer. Increase maxSteps or simplify the task.");

        await _observer
            .OnErrorAsync(maxStepsError, _maxSteps, cancellationToken)
            .ConfigureAwait(false);

        return ChainResult<AgentResult>.Failure(maxStepsError.Message, maxStepsError.Code);
    }

    private static (string thought, string? toolName, string? toolInput, bool isFinal) ParseResponse(
        string response)
    {
        if (response.Contains("Final Answer:", StringComparison.OrdinalIgnoreCase))
        {
            var idx = response.IndexOf("Final Answer:", StringComparison.OrdinalIgnoreCase);
            var answer = response[(idx + 13)..].Trim();
            return (answer, null, null, true);
        }

        var thought = Extract(response, "Thought:") ?? response.Trim();
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
