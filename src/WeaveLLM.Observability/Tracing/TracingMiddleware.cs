using System.Diagnostics;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Observability.Tracing;

/// <summary>
/// Chain middleware that emits an OpenTelemetry span for every chain execution.
/// Tags the span with chain type information and sets the status to Ok or Error based on the result.
/// </summary>
/// <typeparam name="TInput">The chain input type.</typeparam>
/// <typeparam name="TOutput">The chain output type.</typeparam>
public sealed class TracingMiddleware<TInput, TOutput> : IChainMiddleware<TInput, TOutput>
{
    private static readonly string ActivityName =
        $"{typeof(TInput).Name} \u2192 {typeof(TOutput).Name}";

    private static readonly string InputTypeName = typeof(TInput).FullName ?? typeof(TInput).Name;
    private static readonly string OutputTypeName = typeof(TOutput).FullName ?? typeof(TOutput).Name;

    /// <inheritdoc/>
    public async Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default)
    {
        using var activity = WeaveLLMTelemetry.ActivitySource.StartActivity(ActivityName);

        activity?.SetTag("chain.type", $"{InputTypeName} \u2192 {OutputTypeName}");
        activity?.SetTag("chain.input_type", InputTypeName);
        activity?.SetTag("chain.output_type", OutputTypeName);

        var sw = Stopwatch.StartNew();

        var result = await next(input, context, cancellationToken).ConfigureAwait(false);

        sw.Stop();

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error!.Message);
            activity?.SetTag("error.code", result.Error.Code);
            activity?.SetTag("error.message", result.Error.Message);
        }

        LlmMetrics.RecordRequest(
            providerId: context.GetVariable<string>("provider_id") ?? "unknown",
            modelId: context.GetVariable<string>("model_id") ?? "unknown",
            promptTokens: result.TokenUsage.PromptTokens,
            completionTokens: result.TokenUsage.CompletionTokens,
            latencyMs: sw.Elapsed.TotalMilliseconds,
            errorCode: result.IsFailure ? result.Error?.Code : null);

        return result;
    }
}
