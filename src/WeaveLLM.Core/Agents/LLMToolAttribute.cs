#nullable enable
namespace WeaveLLM.Core.Agents;

/// <summary>
/// Marks a public method as an LLM-callable tool. WeaveLLM reads this attribute at registration time
/// to populate the tool name, description, and JSON schema automatically.
/// </summary>
/// <remarks>
/// Use <see cref="System.ComponentModel.DescriptionAttribute"/> on individual parameters to annotate
/// each argument in the generated schema, giving the model better context for how to call the tool.
/// </remarks>
/// <example>
/// <code>
/// [LLMTool("web_search", "Search the web for current information")]
/// public async Task&lt;string&gt; SearchAsync(
///     [Description("The search query")] string query,
///     [Description("Max results to return")] int maxResults = 5) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class LLMToolAttribute : Attribute
{
    /// <summary>The name the model uses to invoke this tool (e.g., <c>"web_search"</c>). Must be unique within a registry.</summary>
    public string Name { get; }

    /// <summary>A concise, natural-language description of what the tool does, shown verbatim to the model.</summary>
    public string Description { get; }

    /// <param name="name">The name the model uses to invoke this tool. Must be unique within a registry.</param>
    /// <param name="description">A concise description of what the tool does, shown verbatim to the model.</param>
    public LLMToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
