using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;
using WeaveLLM.Observability.Tracing;

namespace WeaveLLM.Observability.Cost;

/// <summary>
/// Chain middleware that estimates and records the USD cost of each LLM request.
/// Stores the estimated cost in <see cref="ChainContext.Variables"/> under the key
/// <c>weavellm.estimated_cost_usd</c> and records metrics via <see cref="LlmMetrics"/>.
/// </summary>
/// <typeparam name="TInput">The chain input type.</typeparam>
/// <typeparam name="TOutput">The chain output type.</typeparam>
public sealed class CostMiddleware<TInput, TOutput> : IChainMiddleware<TInput, TOutput>
{
    private readonly IChatModel _model;
    private readonly ILogger<CostMiddleware<TInput, TOutput>> _logger;

    /// <summary>
    /// Initialises a new <see cref="CostMiddleware{TInput,TOutput}"/>.
    /// </summary>
    /// <param name="model">
    /// The chat model whose <see cref="IChatModel.ProviderId"/> and <see cref="IChatModel.ModelId"/>
    /// are used to look up pricing.
    /// </param>
    /// <param name="logger">Logger for debug-level cost output.</param>
    public CostMiddleware(IChatModel model, ILogger<CostMiddleware<TInput, TOutput>> logger)
    {
        _model = model;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var result = await next(input, context, cancellationToken).ConfigureAwait(false);

        sw.Stop();

        var usage = new UsageStats(
            result.TokenUsage.PromptTokens,
            result.TokenUsage.CompletionTokens,
            result.TokenUsage.TotalTokens);

        var cost = CostTracker.EstimateCost(_model.ProviderId, _model.ModelId, usage);

        LlmMetrics.RecordRequest(
            providerId: _model.ProviderId,
            modelId: _model.ModelId,
            promptTokens: usage.PromptTokens,
            completionTokens: usage.CompletionTokens,
            latencyMs: sw.Elapsed.TotalMilliseconds,
            errorCode: result.IsFailure ? result.Error?.Code : null);

        context.Variables["weavellm.estimated_cost_usd"] = cost;

        _logger.LogDebug("Chain {Name} estimated cost: ${Cost:F6}", context.ChainName, cost);

        return result;
    }
}
