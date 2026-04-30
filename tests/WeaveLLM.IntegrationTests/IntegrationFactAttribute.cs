using Xunit;

namespace WeaveLLM.IntegrationTests;

/// <summary>
/// Marks a test as an integration test that only runs when the
/// <c>WEAVELLM_RUN_INTEGRATION</c> environment variable is set to <c>true</c>.
/// </summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WEAVELLM_RUN_INTEGRATION") is not "true")
            Skip = "Set WEAVELLM_RUN_INTEGRATION=true to run integration tests.";
    }
}
