using FluentAssertions;
using WeaveLLM.Core.Models;
using Xunit;

namespace WeaveLLM.Core.Tests.Chains;

public class ChainResultTests
{
    [Fact]
    public void ChainResult_Success_ShouldHaveIsSuccessTrue()
    {
        var result = ChainResult<string>.Success("ok");
        result.IsSuccess.Should().BeTrue();
    }
}
