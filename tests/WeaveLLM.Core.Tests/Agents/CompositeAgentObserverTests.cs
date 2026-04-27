using FluentAssertions;
using NSubstitute;
using WeaveLLM.Core.Agents;
using WeaveLLM.Core.Agents.Observers;
using WeaveLLM.Core.Chains;
using Xunit;

namespace WeaveLLM.Core.Tests.Agents;

public class CompositeAgentObserverTests
{
    [Fact]
    public async Task OnThought_CallsAllChildObservers()
    {
        var obs1 = Substitute.For<IAgentObserver>();
        var obs2 = Substitute.For<IAgentObserver>();
        obs1.OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        obs2.OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var composite = new CompositeAgentObserver([obs1, obs2]);

        await composite.OnThoughtAsync("thinking", 1);

        await obs1.Received(1).OnThoughtAsync("thinking", 1, Arg.Any<CancellationToken>());
        await obs2.Received(1).OnThoughtAsync("thinking", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneFailingObserver_DoesNotStopOthers()
    {
        var failing = Substitute.For<IAgentObserver>();
        failing.OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("observer failed")));

        var good = Substitute.For<IAgentObserver>();
        good.OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var composite = new CompositeAgentObserver([failing, good]);

        var act = async () => await composite.OnThoughtAsync("thinking", 1);

        await act.Should().NotThrowAsync();
        await good.Received(1).OnThoughtAsync("thinking", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllEvents_FanOutToAllObservers()
    {
        var obs = Substitute.For<IAgentObserver>();
        obs.OnThoughtAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        obs.OnActionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        obs.OnObservationAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        obs.OnFinalAnswerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        obs.OnErrorAsync(Arg.Any<ChainError>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var composite = new CompositeAgentObserver([obs]);
        var error = ChainError.InvalidInput("oops");

        await composite.OnThoughtAsync("t", 1);
        await composite.OnActionAsync("tool", "{}", 1);
        await composite.OnObservationAsync("obs", 1);
        await composite.OnFinalAnswerAsync("answer", 3);
        await composite.OnErrorAsync(error, 1);

        await obs.Received(1).OnThoughtAsync("t", 1, Arg.Any<CancellationToken>());
        await obs.Received(1).OnActionAsync("tool", "{}", 1, Arg.Any<CancellationToken>());
        await obs.Received(1).OnObservationAsync("obs", 1, Arg.Any<CancellationToken>());
        await obs.Received(1).OnFinalAnswerAsync("answer", 3, Arg.Any<CancellationToken>());
        await obs.Received(1).OnErrorAsync(error, 1, Arg.Any<CancellationToken>());
    }
}
