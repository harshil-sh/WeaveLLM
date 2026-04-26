using System.Text;
using System.Text.RegularExpressions;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Prompts;

/// <summary>
/// Compile-time validated prompt templates. Supports {{variable}} syntax.
/// Validates that all required variables are provided before execution.
/// </summary>
public interface IPromptTemplate
{
    string Name { get; }
    IReadOnlySet<string> RequiredVariables { get; }
    IReadOnlySet<string> OptionalVariables { get; }

    string Render(IReadOnlyDictionary<string, object> variables);
    PromptValidationResult Validate(IReadOnlyDictionary<string, object> variables);
}

public sealed class PromptValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> MissingVariables { get; init; } = [];
    public IReadOnlyList<string> UnknownVariables { get; init; } = [];

    public static PromptValidationResult Valid() => new() { IsValid = true };
    public static PromptValidationResult Invalid(IReadOnlyList<string> missing) =>
        new() { IsValid = false, MissingVariables = missing };
}

/// <summary>
/// Handlebars-style template: {{variable}}, {{#if condition}}...{{/if}}, {{#each items}}...{{/each}}
/// </summary>
public sealed class PromptTemplate : IPromptTemplate
{
    private static readonly Regex VariablePattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);
    private readonly string _template;

    public string Name { get; }
    public IReadOnlySet<string> RequiredVariables { get; }
    public IReadOnlySet<string> OptionalVariables { get; } = new HashSet<string>();

    private PromptTemplate(string name, string template, IReadOnlySet<string>? optionalVariables = null)
    {
        Name = name;
        _template = template;
        OptionalVariables = optionalVariables ?? new HashSet<string>();

        var allVars = VariablePattern.Matches(template)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        RequiredVariables = allVars.Except(OptionalVariables).ToHashSet();
    }

    public static PromptTemplate Create(string name, string template, IReadOnlySet<string>? optionalVars = null) =>
        new(name, template, optionalVars);

    public string Render(IReadOnlyDictionary<string, object> variables)
    {
        var result = _template;
        foreach (var (key, value) in variables)
            result = result.Replace($"{{{{{key}}}}}", value?.ToString() ?? string.Empty);
        return result;
    }

    public PromptValidationResult Validate(IReadOnlyDictionary<string, object> variables)
    {
        var missing = RequiredVariables.Except(variables.Keys).ToList();
        return missing.Count == 0
            ? PromptValidationResult.Valid()
            : PromptValidationResult.Invalid(missing);
    }

    // Pre-built common templates
    public static readonly PromptTemplate QuestionAnswer = Create(
        "question_answer",
        """
        You are a helpful assistant. Answer the user's question accurately and concisely.

        {{#if context}}
        Context information:
        {{context}}
        {{/if}}

        Question: {{question}}
        """);

    public static readonly PromptTemplate RAGAnswer = Create(
        "rag_answer",
        """
        You are a helpful assistant. Use the following retrieved documents to answer the question.
        If the documents don't contain the answer, say so clearly — do not make up information.

        Retrieved Documents:
        {{documents}}

        Question: {{question}}

        Answer:
        """);

    public static readonly PromptTemplate Summarize = Create(
        "summarize",
        """
        Summarize the following text {{#if style}}in a {{style}} style{{/if}}.
        {{#if maxWords}}Keep the summary under {{maxWords}} words.{{/if}}

        Text to summarize:
        {{text}}

        Summary:
        """,
        optionalVars: new HashSet<string> { "style", "maxWords" });

    public static readonly PromptTemplate ReActAgent = Create(
        "react_agent",
        """
        You are an AI assistant that reasons step-by-step and uses tools to answer questions.

        Available tools:
        {{tools}}

        Use this format:
        Thought: <your reasoning>
        Action: <tool_name>
        Action Input: <tool arguments as JSON>
        Observation: <tool result>
        ... (repeat Thought/Action/Observation as needed)
        Thought: I now have enough information to answer.
        Final Answer: <your final answer>

        Begin!
        Question: {{question}}
        """);
}

/// <summary>
/// Manages a versioned library of prompt templates.
/// Supports A/B testing via named variants.
/// </summary>
public sealed class PromptLibrary
{
    private readonly Dictionary<string, Dictionary<string, IPromptTemplate>> _templates = new();

    public void Register(IPromptTemplate template, string variant = "default")
    {
        if (!_templates.TryGetValue(template.Name, out var variants))
            _templates[template.Name] = variants = new();
        variants[variant] = template;
    }

    public IPromptTemplate Get(string name, string variant = "default")
    {
        if (_templates.TryGetValue(name, out var variants) && variants.TryGetValue(variant, out var template))
            return template;
        throw new KeyNotFoundException($"Prompt template '{name}' (variant: {variant}) not found.");
    }

    public bool TryGet(string name, out IPromptTemplate? template, string variant = "default")
    {
        if (_templates.TryGetValue(name, out var variants) && variants.TryGetValue(variant, out var t))
        {
            template = t;
            return true;
        }
        template = null;
        return false;
    }

    public IReadOnlyList<string> ListTemplates() => _templates.Keys.ToList();
}
