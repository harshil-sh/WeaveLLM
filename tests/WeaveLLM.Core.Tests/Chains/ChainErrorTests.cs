using FluentAssertions;
using WeaveLLM.Core.Chains;
using Xunit;

namespace WeaveLLM.Core.Tests.Chains;

public class ChainErrorTests
{
    [Fact]
    public void ProviderError_WithMessage_EmitsProviderErrorCode()
    {
        var error = ChainError.ProviderError("upstream failure");
        error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public void ProviderError_WithException_AttachesInnerException()
    {
        var ex = new InvalidOperationException("boom");
        var error = ChainError.ProviderError("upstream failure", ex);
        error.InnerException.Should().BeSameAs(ex);
    }

    [Fact]
    public void RateLimited_WithProvider_EmitsRateLimitedCode()
    {
        var error = ChainError.RateLimited("OpenAI");
        error.Code.Should().Be("RATE_LIMITED");
    }

    [Fact]
    public void RateLimited_WithProvider_IncludesProviderInMessage()
    {
        var error = ChainError.RateLimited("OpenAI");
        error.Message.Should().Contain("OpenAI");
    }

    [Fact]
    public void Timeout_WithChainName_EmitsTimeoutCode()
    {
        var error = ChainError.Timeout("SummaryChain");
        error.Code.Should().Be("TIMEOUT");
    }

    [Fact]
    public void Timeout_WithChainName_IncludesChainNameInMessage()
    {
        var error = ChainError.Timeout("SummaryChain");
        error.Message.Should().Contain("SummaryChain");
    }

    [Fact]
    public void InvalidInput_WithMessage_EmitsInvalidInputCode()
    {
        var error = ChainError.InvalidInput("missing required field");
        error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public void InvalidInput_WithMessage_PreservesMessage()
    {
        var error = ChainError.InvalidInput("missing required field");
        error.Message.Should().Be("missing required field");
    }

    [Fact]
    public void ContextTooLong_WithTokenCount_EmitsContextTooLongCode()
    {
        var error = ChainError.ContextTooLong(200_000);
        error.Code.Should().Be("CONTEXT_TOO_LONG");
    }

    [Fact]
    public void ContextTooLong_WithTokenCount_IncludesTokenCountInMessage()
    {
        var error = ChainError.ContextTooLong(200_000);
        error.Message.Should().Contain("200000");
    }

    [Fact]
    public void ToolExecutionFailed_WithToolNameAndReason_EmitsToolExecutionFailedCode()
    {
        var error = ChainError.ToolExecutionFailed("SearchTool", "network timeout");
        error.Code.Should().Be("TOOL_EXECUTION_FAILED");
    }

    [Fact]
    public void ToolExecutionFailed_WithToolNameAndReason_IncludesToolNameAndReasonInMessage()
    {
        var error = ChainError.ToolExecutionFailed("SearchTool", "network timeout");
        error.Message.Should().Contain("SearchTool");
        error.Message.Should().Contain("network timeout");
    }
}
