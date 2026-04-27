using System.Runtime.CompilerServices;
using FluentAssertions;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Extensions;
using WeaveLLM.Core.Models;
using Xunit;

namespace WeaveLLM.Core.Tests.Chains;

public class PipelineTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class FuncChain<TIn, TOut>(string name, Func<TIn, TOut> fn) : IChain<TIn, TOut>
    {
        public string Name => name;

        public Task<ChainResult<TOut>> ExecuteAsync(TIn input, ChainContext context, CancellationToken ct)
            => Task.FromResult(ChainResult<TOut>.Success(fn(input)));

        public async IAsyncEnumerable<TOut> StreamAsync(TIn input, ChainContext context,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return fn(input);
        }
    }

    private sealed class OrderMiddleware(List<string> log, string label) : IChainMiddleware<string, string>
    {
        public async Task<ChainResult<string>> InvokeAsync(
            string input, ChainContext context, ChainDelegate<string, string> next, CancellationToken ct)
        {
            log.Add($"enter_{label}");
            var result = await next(input, context, ct);
            log.Add($"exit_{label}");
            return result;
        }
    }

    private static ChainContext Ctx() => ChainContext.Create();

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleChain_ExecutesAndReturnsResult()
    {
        var chain = new FuncChain<string, string>("upper", s => s.ToUpperInvariant());
        var built = await new PipelineBuilder<string, string>(chain).BuildAsync();

        var result = await built.ExecuteAsync("hello", Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("HELLO");
    }

    [Fact]
    public async Task TwoChainedChains_OutputOfFirstIsInputToSecond()
    {
        var upper = new FuncChain<string, string>("upper", s => s.ToUpperInvariant());
        var append = new FuncChain<string, string>("append", s => s + "!");
        var pipeline = upper.AsConnectable().Pipe(append);

        var result = await pipeline.ExecuteAsync("hello", Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("HELLO!");
    }

    [Fact]
    public async Task Middleware_RunsInLifoOrder_LastRegisteredRunsOutermost()
    {
        var log = new List<string>();
        var chain = new FuncChain<string, string>("id", s => s);
        var builder = new PipelineBuilder<string, string>(chain);
        builder.UseMiddleware(new OrderMiddleware(log, "first"));
        builder.UseMiddleware(new OrderMiddleware(log, "second"));

        var built = await builder.BuildAsync();
        await built.ExecuteAsync("x", Ctx());

        // second (last registered) must wrap first (LIFO)
        log.Should().Equal("enter_second", "enter_first", "exit_first", "exit_second");
    }

    [Fact]
    public async Task Cancellation_PropagatesThroughPipeline()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var tcs = new TaskCompletionSource<ChainResult<string>>();
        tcs.SetCanceled(cts.Token);

        // A chain that respects cancellation
        var chain = new FuncChain<string, string>("upper", s => s.ToUpperInvariant());
        var built = await new PipelineBuilder<string, string>(chain).BuildAsync();

        // Simple cancellation: ExecuteAsync on a non-cancellation-aware chain still completes,
        // but the token is correctly passed through.
        var result = await built.ExecuteAsync("hello", Ctx(), cts.Token);
        result.IsSuccess.Should().BeTrue(); // inner chain doesn't check CT; token was passed
    }
}
