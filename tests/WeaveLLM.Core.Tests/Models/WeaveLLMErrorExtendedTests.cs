using FluentAssertions;
using WeaveLLM.Core.Models;
using Xunit;

namespace WeaveLLM.Core.Tests.Models;

public class WeaveLLMErrorExtendedTests
{
    [Fact]
    public void NotFound_WithMessage_EmitsNotFoundCode()
    {
        var error = WeaveLLMError.NotFound("resource missing");
        error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void NotFound_WithMessage_PreservesMessage()
    {
        var error = WeaveLLMError.NotFound("resource missing");
        error.Message.Should().Be("resource missing");
    }

    [Fact]
    public void Cancelled_WithMessage_EmitsCancelledCode()
    {
        var error = WeaveLLMError.Cancelled("user cancelled");
        error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public void Cancelled_WithInnerException_PropagatesInnerException()
    {
        var inner = new OperationCanceledException();
        var error = WeaveLLMError.Cancelled("user cancelled", inner);
        error.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Cancelled_WithoutInnerException_InnerExceptionIsNull()
    {
        var error = WeaveLLMError.Cancelled("user cancelled");
        error.InnerException.Should().BeNull();
    }

    [Fact]
    public void AuthenticationFailed_WithMessage_EmitsAuthenticationFailedCode()
    {
        var error = WeaveLLMError.AuthenticationFailed("invalid API key");
        error.Code.Should().Be("AUTHENTICATION_FAILED");
    }

    [Fact]
    public void AuthenticationFailed_WithMessage_PreservesMessage()
    {
        var error = WeaveLLMError.AuthenticationFailed("invalid API key");
        error.Message.Should().Be("invalid API key");
    }

    [Fact]
    public void RateLimitExceeded_WithMessage_EmitsRateLimitExceededCode()
    {
        var error = WeaveLLMError.RateLimitExceeded("quota exhausted");
        error.Code.Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public void RateLimitExceeded_WithMessage_PreservesMessage()
    {
        var error = WeaveLLMError.RateLimitExceeded("quota exhausted");
        error.Message.Should().Be("quota exhausted");
    }

    [Fact]
    public void NetworkTimeout_WithMessage_EmitsNetworkTimeoutCode()
    {
        var error = WeaveLLMError.NetworkTimeout("connection timed out");
        error.Code.Should().Be("NETWORK_TIMEOUT");
    }

    [Fact]
    public void NetworkTimeout_WithInnerException_PropagatesInnerException()
    {
        var inner = new TimeoutException();
        var error = WeaveLLMError.NetworkTimeout("connection timed out", inner);
        error.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void NetworkTimeout_WithoutInnerException_InnerExceptionIsNull()
    {
        var error = WeaveLLMError.NetworkTimeout("connection timed out");
        error.InnerException.Should().BeNull();
    }

    [Fact]
    public void InvalidConfiguration_WithMessage_EmitsInvalidConfigurationCode()
    {
        var error = WeaveLLMError.InvalidConfiguration("missing endpoint");
        error.Code.Should().Be("INVALID_CONFIGURATION");
    }

    [Fact]
    public void InvalidConfiguration_WithMessage_PreservesMessage()
    {
        var error = WeaveLLMError.InvalidConfiguration("missing endpoint");
        error.Message.Should().Be("missing endpoint");
    }
}
