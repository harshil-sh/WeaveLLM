#nullable enable
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Thread-safe implementation of <see cref="IToolRegistry"/> that supports both
/// manual registration via <see cref="Register"/> and reflection-based discovery
/// via <see cref="RegisterFromObject"/>.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();

    /// <inheritdoc/>
    public void Register(ToolDefinition tool) => _tools[tool.Name] = tool;

    /// <inheritdoc/>
    public bool TryGet(string name, out ToolDefinition? tool) =>
        _tools.TryGetValue(name, out tool);

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> GetAll() => _tools.Values.ToList();

    /// <inheritdoc/>
    public void RegisterFromObject(object instance)
    {
        var methods = instance.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<LLMToolAttribute>() is not null);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<LLMToolAttribute>()!;
            var schema = BuildParameterSchema(method);
            var executor = BuildExecutor(instance, method);
            _tools[attr.Name] = new ToolDefinition(attr.Name, attr.Description, schema, executor);
        }
    }

    private static JsonElement BuildParameterSchema(MethodInfo method)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in method.GetParameters())
        {
            var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
            properties[param.Name!] = new Dictionary<string, string>
            {
                ["type"] = GetJsonType(param.ParameterType),
                ["description"] = desc
            };
            if (!param.HasDefaultValue)
                required.Add(param.Name!);
        }

        var schemaObj = new { type = "object", properties, required };
        var json = JsonSerializer.Serialize(schemaObj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static Func<string, CancellationToken, Task<string>> BuildExecutor(
        object instance, MethodInfo method)
    {
        return async (argsJson, externalCt) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            cts.CancelAfter(30_000);

            try
            {
                var json = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var parameters = method.GetParameters();
                var invokeArgs = new object?[parameters.Length];

                for (var i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (root.TryGetProperty(p.Name!, out var elem))
                    {
                        invokeArgs[i] = JsonSerializer.Deserialize(elem.GetRawText(), p.ParameterType);
                    }
                    else if (p.HasDefaultValue)
                    {
                        invokeArgs[i] = p.DefaultValue;
                    }
                    else
                    {
                        invokeArgs[i] = null;
                    }
                }

                var result = method.Invoke(instance, invokeArgs);
                return result switch
                {
                    Task<string> taskStr => await taskStr.WaitAsync(cts.Token).ConfigureAwait(false),
                    Task task => await HandleGenericTask(task, cts.Token).ConfigureAwait(false),
                    _ => result?.ToString() ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return "Error: Tool execution timed out.";
            }
            catch (TargetInvocationException tie)
            {
                return $"Error: {tie.InnerException?.Message ?? tie.Message}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        };
    }

    private static async Task<string> HandleGenericTask(Task task, CancellationToken ct)
    {
        await task.WaitAsync(ct).ConfigureAwait(false);
        var resultProp = task.GetType().GetProperty("Result");
        return resultProp?.GetValue(task)?.ToString() ?? string.Empty;
    }

    private static string GetJsonType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long) ||
            type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            return "number";
        return "string";
    }
}
