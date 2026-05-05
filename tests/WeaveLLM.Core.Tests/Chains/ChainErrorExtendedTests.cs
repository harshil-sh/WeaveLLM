using FluentAssertions;
using WeaveLLM.Core.Chains;
using Xunit;

namespace WeaveLLM.Core.Tests.Chains;

public class ChainErrorExtendedTests
{
    [Fact]
    public void NotFound_WithMessage_EmitsNotFoundCode()
    {
        var error = ChainError.NotFound("tool not registered");
        error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void NotFound_WithMessage_PreservesMessage()
    {
        var error = ChainError.NotFound("tool not registered");
        error.Message.Should().Be("tool not registered");
    }

    [Fact]
    public void Cancelled_WithMessage_EmitsCancelledCode()
    {
        var error = ChainError.Cancelled("pipeline cancelled");
        error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public void Cancelled_WithInnerException_PropagatesInnerException()
    {
        var inner = new OperationCanceledException();
        var error = ChainError.Cancelled("pipeline cancelled", inner);
        error.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Cancelled_WithoutInnerException_InnerExceptionIsNull()
    {
        var error = ChainError.Cancelled("pipeline cancelled");
        error.InnerException.Should().BeNull();
    }

    [Fact]
    public void AuthenticationFailed_WithMessage_EmitsAuthenticationFailedCode()
    {
        var error = ChainError.AuthenticationFailed("invalid API key");
        error.Code.Should().Be("AUTHENTICATION_FAILED");
    }

    [Fact]
    public void AuthenticationFailed_WithMessage_PreservesMessage()
    {
        var error = ChainError.AuthenticationFailed("invalid API key");
        error.Message.Should().Be("invalid API key");
    }

    [Fact]
    public void RateLimitExceeded_WithMessage_EmitsRateLimitExceededCode()
    {
        var error = ChainError.RateLimitExceeded("daily quota exhausted");
        error.Code.Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public void RateLimitExceeded_WithMessage_PreservesMessage()
    {
        var error = ChainError.RateLimitExceeded("daily quota exhausted");
        error.Message.Should().Be("daily quota exhausted");
    }

    [Fact]
    public void NetworkTimeout_WithMessage_EmitsNetworkTimeoutCode()
    {
        var error = ChainError.NetworkTimeout("upstream unreachable");
        error.Code.Should().Be("NETWORK_TIMEOUT");
    }

    [Fact]
    public void NetworkTimeout_WithInnerException_PropagatesInnerException()
    {
        var inner = new TimeoutException();
        var error = ChainError.NetworkTimeout("upstream unreachable", inner);
        error.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void NetworkTimeout_WithoutInnerException_InnerExceptionIsNull()
    {
        var error = ChainError.NetworkTimeout("upstream unreachable");
        error.InnerException.Should().BeNull();
    }

    [Fact]
    public void InvalidConfiguration_WithMessage_EmitsInvalidConfigurationCode()
    {
        var error = ChainError.InvalidConfiguration("BaseUrl is not set");
        error.Code.Should().Be("INVALID_CONFIGURATION");
    }

    [Fact]
    public void InvalidConfiguration_WithMessage_PreservesMessage()
    {
        var error = ChainError.InvalidConfiguration("BaseUrl is not set");
        error.Message.Should().Be("BaseUrl is not set");
    }
}
