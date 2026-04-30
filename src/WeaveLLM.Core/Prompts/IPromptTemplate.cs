namespace WeaveLLM.Core.Prompts;

/// <summary>
/// A validated, renderable prompt template. Supports Handlebars-style variable substitution.
/// </summary>
public interface IPromptTemplate
{
    /// <summary>Unique name identifying this template.</summary>
    string Name { get; }

    /// <summary>Variable names that must be supplied for <see cref="Render"/> to succeed.</summary>
    IReadOnlySet<string> RequiredVariables { get; }

    /// <summary>Variable names recognised by the template but not required for rendering.</summary>
    IReadOnlySet<string> OptionalVariables { get; }

    /// <summary>
    /// Renders the template with the supplied variables.
    /// Throws <see cref="InvalidOperationException"/> if any required variable is missing.
    /// </summary>
    /// <param name="variables">Key-value pairs substituted into the template placeholders.</param>
    /// <returns>The fully rendered prompt string.</returns>
    string Render(IReadOnlyDictionary<string, object> variables);

    /// <summary>
    /// Validates the supplied variables without rendering or throwing.
    /// </summary>
    /// <param name="variables">Key-value pairs to validate against the template's variable requirements.</param>
    /// <returns>A <see cref="PromptValidationResult"/> indicating whether all required variables are present.</returns>
    PromptValidationResult Validate(IReadOnlyDictionary<string, object> variables);
}
