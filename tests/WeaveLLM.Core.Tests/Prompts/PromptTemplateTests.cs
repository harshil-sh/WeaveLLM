using FluentAssertions;
using WeaveLLM.Core.Prompts;
using Xunit;

namespace WeaveLLM.Core.Tests.Prompts;

public class PromptTemplateTests
{
    [Fact]
    public void Render_HappyPath_ReturnsRenderedTemplate()
    {
        var template = PromptTemplate.Create("t", "Hello {{name}}, you are {{age}} years old.");
        var vars = new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30 };

        var result = template.Render(vars);

        result.Should().Be("Hello Alice, you are 30 years old.");
    }

    [Fact]
    public void Render_OptionalVariableMissing_Succeeds()
    {
        var template = PromptTemplate.Create("t", "Hello {{name}}{{#if title}}, {{title}}{{/if}}!",
            optionalVars: ["title"]);
        var vars = new Dictionary<string, object> { ["name"] = "Alice" };

        var act = () => template.Render(vars);

        act.Should().NotThrow();
        act().Should().Contain("Alice");
    }

    [Fact]
    public void Render_RequiredVariableMissing_ThrowsInvalidOperationException()
    {
        var template = PromptTemplate.Create("t", "Hello {{name}}.");
        var vars = new Dictionary<string, object>();

        var act = () => template.Render(vars);

        act.Should().Throw<InvalidOperationException>().WithMessage("*name*");
    }

    [Fact]
    public void Validate_ReturnsCorrectMissingVariables()
    {
        var template = PromptTemplate.Create("t", "{{first}} {{last}}");
        var vars = new Dictionary<string, object> { ["first"] = "Alice" };

        var result = template.Validate(vars);

        result.IsValid.Should().BeFalse();
        result.MissingVariables.Should().Contain("last");
    }

    [Fact]
    public void Render_IfBlock_RendersContentWhenVariableTruthy()
    {
        var template = PromptTemplate.Create("t", "{{name}}{{#if show}}, welcome!{{/if}}");
        var vars = new Dictionary<string, object> { ["name"] = "Alice", ["show"] = true };

        var result = template.Render(vars);

        result.Should().Contain("welcome!");
    }

    [Fact]
    public void Render_IfBlock_SkipsContentWhenVariableFalsy()
    {
        var template = PromptTemplate.Create("t", "{{name}}{{#if show}}, welcome!{{/if}}");
        var vars = new Dictionary<string, object> { ["name"] = "Alice", ["show"] = false };

        var result = template.Render(vars);

        result.Should().NotContain("welcome!");
    }

    [Fact]
    public void Render_EachBlock_IteratesOverStringList()
    {
        var template = PromptTemplate.Create("t", "Items:{{#each items}} {{this}}{{/each}}");
        var vars = new Dictionary<string, object>
        {
            ["items"] = new List<string> { "Apple", "Banana", "Cherry" }
        };

        var result = template.Render(vars);

        result.Should().Contain("Apple");
        result.Should().Contain("Banana");
        result.Should().Contain("Cherry");
    }

    [Fact]
    public void Validate_ReturnsValid_WhenAllRequiredVariablesPresent()
    {
        var template = PromptTemplate.Create("t", "{{x}} {{y}}", optionalVars: ["y"]);
        var vars = new Dictionary<string, object> { ["x"] = "val" };

        var result = template.Validate(vars);

        result.IsValid.Should().BeTrue();
        result.MissingVariables.Should().BeEmpty();
    }
}
