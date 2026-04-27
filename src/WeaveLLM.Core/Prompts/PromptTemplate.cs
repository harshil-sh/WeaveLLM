using System.Text.RegularExpressions;

namespace WeaveLLM.Core.Prompts;

/// <summary>
/// Handlebars-style prompt template supporting {{variable}}, {{#if variable}}...{{/if}},
/// and {{#each items}}...{{/each}} syntax.
/// </summary>
public sealed class PromptTemplate : IPromptTemplate
{
    private static readonly Regex VariablePattern =
        new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    private static readonly Regex IfPattern =
        new(@"\{\{#if\s+(\w+)\}\}(.*?)\{\{/if\}\}", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex EachPattern =
        new(@"\{\{#each\s+(\w+)\}\}(.*?)\{\{/each\}\}", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly string _template;

    public string Name { get; }
    public IReadOnlySet<string> RequiredVariables { get; }
    public IReadOnlySet<string> OptionalVariables { get; }

    private PromptTemplate(string name, string template, string[]? optionalVariables = null)
    {
        Name = name;
        _template = template;
        OptionalVariables = optionalVariables?.ToHashSet() ?? new HashSet<string>();

        // Strip {{#each}}...{{/each}} blocks before scanning so iteration
        // placeholders like {{this}} are not counted as required variables.
        var scanTemplate = EachPattern.Replace(template, string.Empty);

        var allVars = VariablePattern.Matches(scanTemplate)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        RequiredVariables = allVars.Except(OptionalVariables).ToHashSet();
    }

    public static PromptTemplate Create(string name, string template, string[]? optionalVars = null) =>
        new(name, template, optionalVars);

    /// <inheritdoc />
    public string Render(IReadOnlyDictionary<string, object> variables)
    {
        var missing = RequiredVariables.Except(variables.Keys).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Missing required template variables: {string.Join(", ", missing)}");

        var result = _template;

        // Process {{#each items}}...{{/each}} blocks first.
        result = EachPattern.Replace(result, m =>
        {
            var varName = m.Groups[1].Value;
            var body = m.Groups[2].Value;
            if (variables.TryGetValue(varName, out var val) && val is IEnumerable<string> items)
                return string.Concat(items.Select(item => body.Replace("{{this}}", item)));
            return string.Empty;
        });

        // Process {{#if variable}}...{{/if}} blocks.
        result = IfPattern.Replace(result, m =>
        {
            var varName = m.Groups[1].Value;
            var body = m.Groups[2].Value;
            if (!variables.TryGetValue(varName, out var val))
                return string.Empty;
            var isTruthy = val switch
            {
                bool b => b,
                string s => !string.IsNullOrEmpty(s),
                _ => val != null
            };
            return isTruthy ? body : string.Empty;
        });

        // Replace {{variable}} tokens for provided variables.
        foreach (var (key, value) in variables)
            result = result.Replace($"{{{{{key}}}}}", value?.ToString() ?? string.Empty);

        // Clear any remaining optional variable tokens that were not supplied.
        foreach (var optVar in OptionalVariables)
            if (!variables.ContainsKey(optVar))
                result = result.Replace($"{{{{{optVar}}}}}", string.Empty);

        return result;
    }

    /// <inheritdoc />
    public PromptValidationResult Validate(IReadOnlyDictionary<string, object> variables)
    {
        var missing = RequiredVariables.Except(variables.Keys).ToList();
        return missing.Count == 0
            ? PromptValidationResult.Valid()
            : PromptValidationResult.Invalid(missing);
    }

    // Pre-built common templates (kept for backward compatibility).
    public static readonly PromptTemplate QuestionAnswer = Create(
        "question_answer",
        """
        You are a helpful assistant. Answer the user's question accurately and concisely.

        {{#if context}}
        Context information:
        {{context}}
        {{/if}}

        Question: {{question}}
        """,
        optionalVars: ["context"]);

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
        optionalVars: ["style", "maxWords"]);

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
/// Manages a versioned library of prompt templates. Supports A/B testing via named variants.
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
