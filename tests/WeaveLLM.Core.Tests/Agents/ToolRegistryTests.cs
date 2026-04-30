using System.ComponentModel;
using FluentAssertions;
using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Agents.Tools;
using Xunit;

namespace WeaveLLM.Core.Tests.Agents;

public class ToolRegistryTests
{
    private sealed class SampleTools
    {
        [LLMTool("echo", "Returns the input string unchanged")]
        public Task<string> EchoAsync(
            [Description("The text to echo")] string text) =>
            Task.FromResult($"Echo: {text}");

        [LLMTool("add", "Adds two numbers")]
        public string Add(
            [Description("First number")] int a,
            [Description("Second number")] int b) =>
            (a + b).ToString();

        [LLMTool("with_default", "Has an optional parameter")]
        public Task<string> WithDefaultAsync(
            [Description("Required text")] string text,
            [Description("Optional suffix")] string suffix = "!") =>
            Task.FromResult(text + suffix);

        [LLMTool("slow_tool", "Takes a long time")]
        public async Task<string> SlowAsync(
            [Description("Some input")] string input)
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            return input;
        }
    }

    [Fact]
    public void RegisterFromObject_RegistersAllLLMToolMethods()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());

        var tools = registry.GetAll();
        tools.Should().HaveCount(4);
        tools.Select(t => t.Name).Should().Contain(["echo", "add", "with_default", "slow_tool"]);
    }

    [Fact]
    public void TryGet_ReturnsRegisteredTool()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());

        var found = registry.TryGet("echo", out var tool);

        found.Should().BeTrue();
        tool.Should().NotBeNull();
        tool!.Name.Should().Be("echo");
        tool.Description.Should().Be("Returns the input string unchanged");
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknownTool()
    {
        var registry = new ToolRegistry();
        var found = registry.TryGet("nonexistent", out var tool);
        found.Should().BeFalse();
        tool.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteTool_TaskStringReturn_ReturnsOutput()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());
        registry.TryGet("echo", out var tool);

        var result = await tool!.Executor("""{"text":"hello"}""", CancellationToken.None);

        result.Should().Be("Echo: hello");
    }

    [Fact]
    public async Task ExecuteTool_SyncStringReturn_ReturnsOutput()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());
        registry.TryGet("add", out var tool);

        var result = await tool!.Executor("""{"a":3,"b":4}""", CancellationToken.None);

        result.Should().Be("7");
    }

    [Fact]
    public async Task ExecuteTool_OptionalParameterOmitted_UsesDefault()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());
        registry.TryGet("with_default", out var tool);

        var result = await tool!.Executor("""{"text":"hello"}""", CancellationToken.None);

        result.Should().Be("hello!");
    }

    [Fact]
    public async Task ExecuteTool_OptionalParameterProvided_UsesProvidedValue()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());
        registry.TryGet("with_default", out var tool);

        var result = await tool!.Executor("""{"text":"hello","suffix":"???"}""", CancellationToken.None);

        result.Should().Be("hello???");
    }

    [Fact]
    public async Task ExecuteTool_WithPreCancelledToken_ReturnsTimeoutError()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());
        registry.TryGet("slow_tool", out var tool);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await tool!.Executor("""{"input":"test"}""", cts.Token);

        result.Should().StartWith("Error:");
    }

    [Fact]
    public void Register_ManualRegistration_OverwritesSameName()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition(
            "my_tool", "First", default, (_, _) => Task.FromResult("first")));
        registry.Register(new ToolDefinition(
            "my_tool", "Second", default, (_, _) => Task.FromResult("second")));

        registry.TryGet("my_tool", out var tool);
        tool!.Description.Should().Be("Second");
    }

    [Fact]
    public void BuildParameterSchema_SetsCorrectJsonTypes()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new SampleTools());
        registry.TryGet("add", out var tool);

        var schema = tool!.ParameterSchema;
        var aType = schema.GetProperty("properties").GetProperty("a").GetProperty("type").GetString();
        var bType = schema.GetProperty("properties").GetProperty("b").GetProperty("type").GetString();
        aType.Should().Be("number");
        bType.Should().Be("number");
    }

    [Fact]
    public void BuiltInTools_DateTimeTool_CanRegister()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new DateTimeTool());

        registry.TryGet("get_current_datetime", out var tool).Should().BeTrue();
        tool!.Description.Should().Be("Get the current UTC date and time");
    }

    [Fact]
    public async Task BuiltInTools_CalculatorTool_EvaluatesExpression()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new CalculatorTool());
        registry.TryGet("calculate", out var tool);

        var result = await tool!.Executor("""{"expression":"(12 + 4) * 3"}""", CancellationToken.None);

        result.Should().Be("48");
    }

    [Fact]
    public async Task BuiltInTools_WebSearchTool_WithoutProvider_ReturnsStubMessage()
    {
        var registry = new ToolRegistry();
        registry.RegisterFromObject(new WebSearchTool());
        registry.TryGet("search_web", out var tool);

        var result = await tool!.Executor("""{"query":"test"}""", CancellationToken.None);

        result.Should().Contain("not configured");
    }
}
