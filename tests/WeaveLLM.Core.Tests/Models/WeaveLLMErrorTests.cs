using FluentAssertions;
using WeaveLLM.Core.Models;
using Xunit;

namespace WeaveLLM.Core.Tests.Models;

public class WeaveLLMErrorTests
{
    [Fact]
    public void Timeout_WithChainName_EmitsTimeoutCode()
    {
        var error = WeaveLLMError.Timeout("MyChain");
        error.Code.Should().Be("TIMEOUT");
    }

    [Fact]
    public void Timeout_WithChainName_IncludesChainNameInMessage()
    {
        var error = WeaveLLMError.Timeout("MyChain");
        error.Message.Should().Contain("MyChain");
    }

    [Fact]
    public void RateLimited_WithProvider_EmitsRateLimitedCode()
    {
        var error = WeaveLLMError.RateLimited("OpenAI");
        error.Code.Should().Be("RATE_LIMITED");
    }

    [Fact]
    public void RateLimited_WithProvider_IncludesProviderInMessage()
    {
        var error = WeaveLLMError.RateLimited("OpenAI");
        error.Message.Should().Contain("OpenAI");
    }

    [Fact]
    public void InvalidInput_WithMessage_EmitsInvalidInputCode()
    {
        var error = WeaveLLMError.InvalidInput("bad value");
        error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public void InvalidInput_WithMessage_PreservesMessage()
    {
        var error = WeaveLLMError.InvalidInput("bad value");
        error.Message.Should().Be("bad value");
    }

    [Fact]
    public void ProviderError_WithProviderAndMessage_EmitsProviderErrorCode()
    {
        var error = WeaveLLMError.ProviderError("Anthropic", "500 internal server error");
        error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public void ProviderError_WithProviderAndMessage_PrefixesProviderInMessage()
    {
        var error = WeaveLLMError.ProviderError("Anthropic", "500 internal server error");
        error.Message.Should().StartWith("[Anthropic]");
        error.Message.Should().Contain("500 internal server error");
    }
}
