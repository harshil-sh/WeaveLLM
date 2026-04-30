using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;
using WeaveLLM.Observability.Cost;
using Xunit;

namespace WeaveLLM.Observability.Tests.Cost;

public class CostMiddlewareTests
{
    private static ChainContext Context() => ChainContext.Create();

    private static ChainDelegate<string, string> SuccessWithUsage(int prompt, int completion) =>
        (_, _, _) => Task.FromResult(
            ChainResult<string>.Success("ok", new TokenUsage { PromptTokens = prompt, CompletionTokens = completion }));

    private static ChainDelegate<string, string> FailureDelegate() =>
        (_, _, _) => Task.FromResult(ChainResult<string>.Failure("model error", "PROVIDER_ERROR"));

    private static CostMiddleware<string, string> BuildMiddleware(
        string providerId = "openai", string modelId = "gpt-4o")
    {
        var model = Substitute.For<IChatModel>();
        model.ProviderId.Returns(providerId);
        model.ModelId.Returns(modelId);
        var logger = Substitute.For<ILogger<CostMiddleware<string, string>>>();
        return new CostMiddleware<string, string>(model, logger);
    }

    [Fact]
    public async Task InvokeAsync_StoresCostInContext_ForKnownModel()
    {
        var sut = BuildMiddleware("openai", "gpt-4o");
        var ctx = Context();

        // gpt-4o: $2.50/1M prompt + $10.00/1M completion
        await sut.InvokeAsync("input", ctx, SuccessWithUsage(1_000_000, 1_000_000));

        ctx.Variables.Should().ContainKey("weavellm.estimated_cost_usd");
        ((decimal)ctx.Variables["weavellm.estimated_cost_usd"]).Should().Be(12.50m);
    }

    [Fact]
    public async Task InvokeAsync_StoresZeroCost_ForUnknownModel()
    {
        var sut = BuildMiddleware("unknown-provider", "unknown-model");
        var ctx = Context();

        await sut.InvokeAsync("input", ctx, SuccessWithUsage(1_000, 500));

        ctx.Variables.Should().ContainKey("weavellm.estimated_cost_usd");
        ((decimal)ctx.Variables["weavellm.estimated_cost_usd"]).Should().Be(0m);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsSuccessResult_WhenNextSucceeds()
    {
        var sut = BuildMiddleware();
        var ctx = Context();

        var result = await sut.InvokeAsync("input", ctx, SuccessWithUsage(10, 5));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_PassesThroughFailure_WhenNextFails()
    {
        var sut = BuildMiddleware();
        var ctx = Context();

        var result = await sut.InvokeAsync("input", ctx, FailureDelegate());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PROVIDER_ERROR");
    }
}
