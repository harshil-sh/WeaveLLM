using Microsoft.Extensions.Configuration;
using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Agents.Observers;
using WeaveLLM.Core.Agents.Tools;
using WeaveLLM.Core.Models;
using WeaveLLM.Providers.OpenAI;

// ── Configuration ──────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var apiKey = config["WeaveLLM:OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("Set WeaveLLM:OpenAI:ApiKey in appsettings.json.");
var modelId = config["WeaveLLM:OpenAI:ModelId"] ?? "gpt-4o";

// ── Graceful shutdown ──────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Shared model + tools ───────────────────────────────────────────────────────
var openai = new OpenAIChatModel(apiKey, modelId);
var model  = new ChatModelAdapter(openai);

var tools = new ToolRegistry();
tools.RegisterFromObject(new CalculatorTool());
tools.RegisterFromObject(new DateTimeTool());

var observer = new ConsoleAgentObserver();

// ─────────────────────────────────────────────────────────────────────────────
// Demo 1 — ReActAgent
// Thought → Action → Observation loop until Final Answer.
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine(" Demo 1: ReActAgent (Thought → Action → Observation)");
Console.WriteLine("═══════════════════════════════════════════════════════════\n");

var reactAgent = new ReActAgent(model, tools, observer, maxSteps: 10);
var reactResult = await reactAgent.RunAsync(
    "What is (144 + 56) * 3 and what is today's UTC date?",
    cts.Token);

if (reactResult.IsFailure)
    Console.Error.WriteLine($"ReActAgent failed: {reactResult.Error!.Message}");
else
    PrintSummary("ReActAgent", reactResult.Value!);

// ─────────────────────────────────────────────────────────────────────────────
// Demo 2 — PlanAndExecuteAgent
// Two models: planner decomposes, executor works each step, planner synthesises.
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n═══════════════════════════════════════════════════════════");
Console.WriteLine(" Demo 2: PlanAndExecuteAgent (Plan → Execute → Synthesise)");
Console.WriteLine("═══════════════════════════════════════════════════════════\n");

// Using the same model for both planner and executor is fine for demos.
var planAndExecute = new PlanAndExecuteAgent(
    planner:  model,
    executor: model,
    tools:    tools,
    observer: observer,
    maxSteps: 20);

var planResult = await planAndExecute.RunAsync(
    "Calculate the area of a rectangle with width 12.5 and height 8, " +
    "then calculate the perimeter, and finally report both along with the current UTC time.",
    cts.Token);

if (planResult.IsFailure)
    Console.Error.WriteLine($"PlanAndExecuteAgent failed: {planResult.Error!.Message}");
else
    PrintSummary("PlanAndExecuteAgent", planResult.Value!);

// ─────────────────────────────────────────────────────────────────────────────
// Demo 3 — AgentGraph
// State-machine workflow with nodes, edges, and conditional routing.
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n═══════════════════════════════════════════════════════════");
Console.WriteLine(" Demo 3: AgentGraph (State-machine multi-agent workflow)");
Console.WriteLine("═══════════════════════════════════════════════════════════\n");

var graph = AgentGraph<WorkflowState>.Create()
    .AddNode("classify", async (state, ctx, ct) =>
    {
        Console.WriteLine("[classify] Classifying the user's request...");
        var result = await model.ChatAsync(
            [
                Message.System("You are a classifier. Reply with exactly one word: MATH or GENERAL."),
                Message.User(state.UserInput)
            ],
            LLMOptions.Deterministic(), ct);

        state.Category = result.IsSuccess && result.Value!.Content.Contains("MATH", StringComparison.OrdinalIgnoreCase)
            ? "MATH"
            : "GENERAL";
        Console.WriteLine($"[classify] Category → {state.Category}");
        return state;
    })
    .AddNode("math-agent", async (state, ctx, ct) =>
    {
        Console.WriteLine("[math-agent] Running ReActAgent for math task...");
        var agent  = new ReActAgent(model, tools, maxSteps: 8);
        var result = await agent.RunAsync(state.UserInput, ct);
        state.Answer = result.IsSuccess ? result.Value!.FinalAnswer : $"Error: {result.Error!.Message}";
        return state;
    })
    .AddNode("general-agent", async (state, ctx, ct) =>
    {
        Console.WriteLine("[general-agent] Answering general question...");
        var result = await model.ChatAsync(
            [
                Message.System("You are a helpful assistant. Be concise."),
                Message.User(state.UserInput)
            ],
            LLMOptions.Balanced(), ct);
        state.Answer = result.IsSuccess ? result.Value!.Content : $"Error: {result.Error!.Message}";
        return state;
    })
    .AddNode("format-output", async (state, ctx, ct) =>
    {
        Console.WriteLine("[format-output] Formatting final response...");
        await Task.CompletedTask;
        state.FormattedOutput = $"[{state.Category}] {state.Answer}";
        return state;
    })
    .AddConditionalEdge("classify",
        state => state.Category,
        new Dictionary<string, string>
        {
            ["MATH"]    = "math-agent",
            ["GENERAL"] = "general-agent"
        })
    .AddEdge("math-agent",    "format-output")
    .AddEdge("general-agent", "format-output")
    .SetEntryPoint("classify")
    .SetEndPoint("end");

// Run with a math question
var mathState = new WorkflowState("What is 17 * 23 + 144?");
var mathRun   = await graph.RunAsync(mathState, cancellationToken: cts.Token);
PrintGraphResult("Math query", mathRun);

Console.WriteLine();

// Run with a general question
var generalState = new WorkflowState("What are the main benefits of using a RAG pipeline?");
var generalRun   = await graph.RunAsync(generalState, cancellationToken: cts.Token);
PrintGraphResult("General query", generalRun);

// ── Helpers ────────────────────────────────────────────────────────────────────

static void PrintSummary(string agentName, AgentResult result)
{
    Console.WriteLine($"\n── {agentName} Summary ────────────────────────────────");
    Console.WriteLine($"Steps taken : {result.Steps.Count}");
    if (result.TotalUsage is { } usage)
        Console.WriteLine($"Tokens used : {usage.TotalTokens} (prompt={usage.PromptTokens}, completion={usage.CompletionTokens})");
    Console.WriteLine($"Final Answer: {result.FinalAnswer}\n");
}

static void PrintGraphResult(string label, GraphResult<WorkflowState> result)
{
    Console.WriteLine($"\n── Graph run: {label} ─────────────────────────────────");
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"Graph failed: {result.ErrorMessage}");
        return;
    }
    Console.WriteLine($"Nodes visited : {string.Join(" → ", result.ExecutionLog)}");
    Console.WriteLine($"Final output  : {result.State.FormattedOutput}");
}

// ── Shared graph state ─────────────────────────────────────────────────────────
// AgentGraph<TState> requires a public parameterless constructor (new() constraint).
sealed class WorkflowState
{
    public WorkflowState() { }
    public WorkflowState(string userInput) => UserInput = userInput;

    public string UserInput       { get; init; } = string.Empty;
    public string Category        { get; set; }  = "GENERAL";
    public string Answer          { get; set; }  = string.Empty;
    public string FormattedOutput { get; set; }  = string.Empty;
}

// ── Adapter: WeaveLLM.Core.Providers → WeaveLLM.Core.Models ───────────────────
// Agents in WeaveLLM.Core.Agents expect WeaveLLM.Core.Models.IChatModel.
// OpenAIChatModel implements WeaveLLM.Core.Providers.IChatModel.
// This thin wrapper bridges the gap without any extra dependencies.

sealed class ChatModelAdapter(OpenAIChatModel inner) : WeaveLLM.Core.Models.IChatModel
{
    public string ModelId    => inner.ModelId;
    public string ProviderId => inner.ProviderName;

    public Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages, LLMOptions? options, CancellationToken ct)
        => inner.ChatAsync(messages, options, ct);
}
