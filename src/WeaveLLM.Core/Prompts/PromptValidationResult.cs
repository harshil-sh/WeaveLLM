namespace WeaveLLM.Core.Prompts;

/// <summary>
/// The outcome of validating a set of template variables without rendering.
/// </summary>
public sealed class PromptValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> MissingVariables { get; init; } = [];
    public IReadOnlyList<string> UnknownVariables { get; init; } = [];

    public static PromptValidationResult Valid() => new() { IsValid = true };

    public static PromptValidationResult Invalid(IReadOnlyList<string> missing) =>
        new() { IsValid = false, MissingVariables = missing };
}
