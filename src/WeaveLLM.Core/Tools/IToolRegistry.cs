using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Tools;

/// <summary>
/// Decorate any public method with [LLMTool] to register it as a tool.
/// WeaveLLM auto-generates the JSON schema from method signature.
/// Zero boilerplate — works exactly like minimal API route attributes.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LLMToolAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public bool IsLongRunning { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Registry that discovers and invokes tools at runtime.
/// Supports both attribute-discovered tools and manually registered lambdas.
/// </summary>
public interface IToolRegistry
{
    void Register(ToolDefinition tool);
    void RegisterFromObject(object instance);
    ToolDefinition? GetTool(string name);
    IReadOnlyList<ToolDefinition> GetAll();
    Task<ToolResult> InvokeAsync(string toolName, string argumentsJson, ChainContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata + invocation delegate for a single tool.
/// </summary>
public sealed class ToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string JsonSchema { get; init; } = "{}";
    public Func<string, ChainContext, CancellationToken, Task<ToolResult>> Handler { get; init; } = null!;
    public bool IsLongRunning { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// The result of invoking a tool.
/// </summary>
public sealed class ToolResult
{
    public bool IsSuccess { get; init; }
    public string Output { get; init; } = string.Empty;
    public object? Data { get; init; }
    public string? ErrorMessage { get; init; }

    public static ToolResult Success(string output, object? data = null) =>
        new() { IsSuccess = true, Output = output, Data = data };

    public static ToolResult Failure(string error) =>
        new() { IsSuccess = false, ErrorMessage = error, Output = $"Error: {error}" };
}

/// <summary>
/// Default tool registry implementation with reflection-based discovery.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();

    public void Register(ToolDefinition tool) =>
        _tools[tool.Name] = tool;

    public void RegisterFromObject(object instance)
    {
        var methods = instance.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<LLMToolAttribute>() is not null);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<LLMToolAttribute>()!;
            var schema = GenerateJsonSchema(method);
            var tool = new ToolDefinition
            {
                Name = attr.Name,
                Description = attr.Description,
                JsonSchema = schema,
                IsLongRunning = attr.IsLongRunning,
                TimeoutSeconds = attr.TimeoutSeconds,
                Handler = async (argsJson, ctx, ct) =>
                {
                    try
                    {
                        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson) ?? new();
                        var parameters = method.GetParameters();
                        var invokeArgs = parameters.Select(p =>
                        {
                            if (args.TryGetValue(p.Name!, out var element))
                                return JsonSerializer.Deserialize(element.GetRawText(), p.ParameterType);
                            return p.HasDefaultValue ? p.DefaultValue : null;
                        }).ToArray();

                        var result = method.Invoke(instance, invokeArgs);
                        if (result is Task task)
                        {
                            await task;
                            var resultProp = task.GetType().GetProperty("Result");
                            return ToolResult.Success(JsonSerializer.Serialize(resultProp?.GetValue(task)));
                        }
                        return ToolResult.Success(JsonSerializer.Serialize(result));
                    }
                    catch (Exception ex)
                    {
                        return ToolResult.Failure(ex.InnerException?.Message ?? ex.Message);
                    }
                }
            };
            _tools[tool.Name] = tool;
        }
    }

    public ToolDefinition? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    public IReadOnlyList<ToolDefinition> GetAll() =>
        _tools.Values.ToList();

    public async Task<ToolResult> InvokeAsync(string toolName, string argumentsJson, ChainContext context, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
            return ToolResult.Failure($"Tool '{toolName}' not found in registry.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(tool.TimeoutSeconds));

        try
        {
            return await tool.Handler(argumentsJson, context, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Failure($"Tool '{toolName}' timed out after {tool.TimeoutSeconds}s.");
        }
    }

    private static string GenerateJsonSchema(MethodInfo method)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in method.GetParameters())
        {
            var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? param.Name ?? "";
            properties[param.Name!] = new
            {
                type = GetJsonType(param.ParameterType),
                description = desc
            };
            if (!param.HasDefaultValue)
                required.Add(param.Name!);
        }

        return JsonSerializer.Serialize(new
        {
            type = "object",
            properties,
            required
        });
    }

    private static string GetJsonType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))) return "array";
        return "object";
    }
}

/// <summary>
/// Example tool class showing the attribute pattern.
/// </summary>
public sealed class WebSearchTool
{
    [LLMTool("web_search", "Search the web for current information")]
    public Task<string> SearchAsync(
        [Description("The search query")] string query,
        [Description("Maximum number of results")] int maxResults = 5)
    {
        // Replace with a real HTTP call to Bing, Serper, Brave, etc.
        return Task.FromResult($"Search results for: {query}");
    }

    [LLMTool("get_weather", "Get current weather for a location")]
    public Task<string> GetWeatherAsync(
        [Description("City name or coordinates")] string location)
    {
        // Replace with a real HTTP call to a weather provider.
        return Task.FromResult($"Weather for {location}: Sunny, 22°C");
    }
}
