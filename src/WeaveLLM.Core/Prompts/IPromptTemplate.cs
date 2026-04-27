namespace WeaveLLM.Core.Prompts;

/// <summary>
/// A validated, renderable prompt template. Supports Handlebars-style variable substitution.
/// </summary>
public interface IPromptTemplate
{
    string Name { get; }
    IReadOnlySet<string> RequiredVariables { get; }
    IReadOnlySet<string> OptionalVariables { get; }

    /// <summary>
    /// Renders the template with the supplied variables.
    /// Throws <see cref="InvalidOperationException"/> if any required variable is missing.
    /// </summary>
    string Render(IReadOnlyDictionary<string, object> variables);

    /// <summary>
    /// Validates the supplied variables without rendering or throwing.
    /// </summary>
    PromptValidationResult Validate(IReadOnlyDictionary<string, object> variables);
}
