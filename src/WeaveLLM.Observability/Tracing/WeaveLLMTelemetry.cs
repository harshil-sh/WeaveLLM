using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WeaveLLM.Observability.Tracing;

/// <summary>
/// Central OpenTelemetry instrumentation hub for WeaveLLM.
/// All chain middleware and provider integrations emit spans and metrics via these shared instances.
/// </summary>
public static class WeaveLLMTelemetry
{
    /// <summary>The OpenTelemetry ActivitySource used to emit spans for every chain execution.</summary>
    public static readonly ActivitySource ActivitySource = new("WeaveLLM", "0.1.0");

    /// <summary>The OpenTelemetry Meter used to record LLM-specific counters and histograms.</summary>
    public static readonly Meter Meter = new("WeaveLLM", "0.1.0");
}
