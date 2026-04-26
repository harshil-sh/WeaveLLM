using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;
using WeaveLLM.Core.Providers;
using WeaveLLM.Core.RAG;
using WeaveLLM.Core.Tools;
using WeaveLLM.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ── Register WeaveLLM with fluent builder ───────────────────────────────────
builder.Services
    .AddWeaveLLM()
    .AddOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        modelId: "gpt-4o")
    .AddAnthropic(
        apiKey: builder.Configuration["Anthropic:ApiKey"]!,
        modelId: "claude-sonnet-4-5")
    .AddInMemoryMemory()
    .AddToolRegistry(registry =>
    {
        registry.RegisterFromObject(new WebSearchTool());
        registry.RegisterFromObject(new CalculatorTool());
    })
    .AddReActAgent("assistant")
    .AddRagPipeline();

var app = builder.Build();

// ── Chat endpoint with streaming ─────────────────────────────────────────────
app.MapPost("/chat", async (
    [FromBody] ChatRequest request,
    IChatModel model,
    IMemoryStore memory,
    CancellationToken ct) =>
{
    var history = await memory.LoadAsync(request.SessionId, cancellationToken: ct);
    var messages = history.ToList();
    messages.Add(Message.User(request.Message));

    var result = await model.ChatAsync(messages, cancellationToken: ct);
    if (!result.IsSuccess)
        return Results.Problem(result.Error!.Message);

    await memory.SaveAsync(request.SessionId, Message.User(request.Message), ct);
    await memory.SaveAsync(request.SessionId, result.Value!, ct);

    return Results.Ok(new ChatResponse(result.Value!.Content, result.TokenUsage));
});

// ── Streaming chat with SSE ──────────────────────────────────────────────────
app.MapGet("/chat/stream", async (
    string message,
    string sessionId,
    IChatModel model,
    HttpContext http,
    CancellationToken ct) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    await foreach (var chunk in model.StreamChatAsync([Message.User(message)], cancellationToken: ct))
    {
        await http.Response.WriteAsync($"data: {chunk}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
});

// ── Agent endpoint ────────────────────────────────────────────────────────────
app.MapPost("/agent/run", async (
    [FromBody] AgentRequest request,
    IAgent agent,
    CancellationToken ct) =>
{
    var result = await agent.RunAsync(request.Input, new AgentRunOptions
    {
        MaxIterations = 10,
        VerboseLogging = true
    }, ct);

    return result.IsSuccess
        ? Results.Ok(new AgentResponse(result.FinalAnswer, result.Steps, result.TotalTokenUsage))
        : Results.Problem(result.ErrorMessage);
});

// ── RAG indexing endpoint ────────────────────────────────────────────────────
app.MapPost("/rag/index", async (
    [FromBody] IndexRequest request,
    IRagPipeline rag,
    CancellationToken ct) =>
{
    var documents = request.Texts.Select((text, i) => new Document
    {
        Content = text,
        Source = request.Source ?? $"document_{i}",
        Title = $"Document {i + 1}"
    }).ToList();

    var result = await rag.IndexAsync(documents, ct);
    return Results.Ok(result);
});

// ── RAG query endpoint ───────────────────────────────────────────────────────
app.MapPost("/rag/query", async (
    [FromBody] RagQueryRequest request,
    IRagPipeline rag,
    CancellationToken ct) =>
{
    var result = await rag.QueryAsync(request.Question, new RagQueryOptions
    {
        TopK = request.TopK ?? 5,
        UseHybridSearch = true
    }, ct);

    return result.IsSuccess
        ? Results.Ok(new { answer = result.Value })
        : Results.Problem(result.Error!.Message);
});

app.Run();

// ── Request/Response DTOs ────────────────────────────────────────────────────
record ChatRequest(string Message, string SessionId);
record ChatResponse(string Reply, TokenUsage Usage);
record AgentRequest(string Input);
record AgentResponse(string Answer, IReadOnlyList<AgentStep> Steps, TokenUsage Usage);
record IndexRequest(IReadOnlyList<string> Texts, string? Source);
record RagQueryRequest(string Question, int? TopK);

// ── Example tool implementations ─────────────────────────────────────────────
public sealed class CalculatorTool
{
    [WeaveLLM.Core.Tools.LLMTool("calculate", "Evaluate a mathematical expression")]
    public string Calculate([System.ComponentModel.Description("Math expression, e.g. '2 + 2 * 10'")] string expression)
    {
        try
        {
            var table = new System.Data.DataTable();
            var result = table.Compute(expression, string.Empty);
            return result.ToString() ?? "0";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
