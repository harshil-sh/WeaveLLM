#nullable enable
using System.Text.Json;

namespace WeaveLLM.Core.Agents;

/// <summary>
/// Registry that exposes tools for agent discovery and invocation at runtime.
/// The registry is the contract between the agent loop and tool implementations.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers a tool definition, overwriting any existing entry with the same <see cref="ToolDefinition.Name"/>.
    /// </summary>
    /// <param name="tool">The tool to register.</param>
    void Register(ToolDefinition tool);

    /// <summary>
    /// Attempts to look up a tool by its registered name.
    /// </summary>
    /// <param name="name">The tool name (case-sensitive).</param>
    /// <param name="tool">The definition, if found; <c>null</c> otherwise.</param>
    /// <returns><c>true</c> if the tool was found; <c>false</c> otherwise.</returns>
    bool TryGet(string name, out ToolDefinition? tool);

    /// <summary>Returns a snapshot of all currently registered tool definitions.</summary>
    IReadOnlyList<ToolDefinition> GetAll();

    /// <summary>
    /// Reflects all public instance methods on <paramref name="instance"/> that carry
    /// <see cref="LLMToolAttribute"/>, builds JSON Schema for their parameters, and
    /// registers each as a <see cref="ToolDefinition"/>.
    /// </summary>
    /// <param name="instance">The object whose public methods carry <see cref="LLMToolAttribute"/>.</param>
    void RegisterFromObject(object instance);
}

/// <summary>
/// Describes a single tool — its identity, JSON schema, and execution delegate.
/// </summary>
/// <param name="Name">The tool name the model uses to refer to it (e.g., <c>"web_search"</c>).</param>
/// <param name="Description">A natural-language description of what the tool does, included in the model's prompt.</param>
/// <param name="ParameterSchema">
/// A JSON Schema <see cref="JsonElement"/> describing the tool's input parameters.
/// Passed to the model so it can generate correctly-shaped arguments.
/// </param>
/// <param name="Executor">
/// Async delegate that receives the model's JSON arguments string and returns the tool output as plain text.
/// Errors should be returned as descriptive strings rather than thrown as exceptions.
/// </param>
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParameterSchema,
    Func<string, CancellationToken, Task<string>> Executor);
