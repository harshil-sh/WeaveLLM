using FluentAssertions;
using WeaveLLM.Providers.OpenAI;
using Xunit;

namespace WeaveLLM.Providers.Tests;

public class ProvidersPlaceholderTests
{
    [Fact]
    public void OpenAIChatModel_Type_Exists()
    {
        typeof(OpenAIChatModel).Name.Should().Be("OpenAIChatModel");
    }
}
