#nullable enable
using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Extensions.DependencyInjection.HealthChecks;

/// <summary>
/// Health check that probes the registered <see cref="IChatModel"/> with a minimal ping request
/// and classifies the result by response latency.
/// </summary>
/// <remarks>
/// <para>
/// Latency thresholds:
/// <list type="bullet">
///   <item><description><b>Healthy</b> — response received in under 5 000 ms.</description></item>
///   <item><description><b>Degraded</b> — response received between 5 000 and 15 000 ms.</description></item>
///   <item><description><b>Unhealthy</b> — model returned an error, or the 15 s overall timeout was exceeded.</description></item>
/// </list>
/// </para>
/// <para>
/// Register via <see cref="WeaveLLMBuilder.AddWeaveLLMHealthChecks"/>.
/// </para>
/// </remarks>
public sealed class LlmProviderHealthCheck : IHealthCheck
{
    private const double HealthyThresholdMs = 5_000;
    private const int TimeoutMs = 15_000;

    private readonly IChatModel _model;

    /// <summary>
    /// Creates a new <see cref="LlmProviderHealthCheck"/> that probes <paramref name="model"/>.
    /// </summary>
    /// <param name="model">The chat model to ping. Resolved from DI by the health check framework.</param>
    public LlmProviderHealthCheck(IChatModel model) => _model = model;

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeoutMs);

            var result = await _model.ChatAsync(
                [Message.User("ping")],
                new LLMOptions { MaxTokens = 1 },
                cts.Token).ConfigureAwait(false);

            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds;

            var data = BuildData(ms);

            if (!result.IsSuccess)
                return HealthCheckResult.Unhealthy(
                    $"[{_model.ProviderId}/{_model.ModelId}] returned error: {result.Error?.Message}",
                    data: data);

            return ms < HealthyThresholdMs
                ? HealthCheckResult.Healthy($"OK ({ms:F0} ms)", data)
                : HealthCheckResult.Degraded($"Slow response ({ms:F0} ms — threshold {HealthyThresholdMs:F0} ms)", data: data);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return HealthCheckResult.Unhealthy(
                $"[{_model.ProviderId}/{_model.ModelId}] health check timed out after {TimeoutMs / 1000} s.",
                data: BuildData(sw.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            var data = BuildData(sw.Elapsed.TotalMilliseconds);
            return HealthCheckResult.Unhealthy(
                $"[{_model.ProviderId}/{_model.ModelId}] unexpected error: {ex.Message}",
                exception: ex,
                data: data);
        }
    }

    private IReadOnlyDictionary<string, object> BuildData(double latencyMs) =>
        new Dictionary<string, object>
        {
            ["latency_ms"] = latencyMs,
            ["model_id"] = _model.ModelId,
            ["provider_id"] = _model.ProviderId
        };
}
