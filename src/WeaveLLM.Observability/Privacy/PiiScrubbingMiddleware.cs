using Microsoft.Extensions.Logging;
using WeaveLLM.Core.Chains;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Observability.Privacy;

/// <summary>
/// Chain middleware that scrubs PII from inputs and outputs before they are written to debug logs.
/// The actual input passed to the next middleware is never modified — only log output is scrubbed.
/// </summary>
/// <typeparam name="TInput">The chain input type.</typeparam>
/// <typeparam name="TOutput">The chain output type.</typeparam>
public sealed class PiiScrubbingMiddleware<TInput, TOutput> : IChainMiddleware<TInput, TOutput>
{
    private readonly ILogger<PiiScrubbingMiddleware<TInput, TOutput>> _logger;

    /// <summary>
    /// Initialises a new <see cref="PiiScrubbingMiddleware{TInput,TOutput}"/>.
    /// </summary>
    /// <param name="logger">Logger used for scrubbed debug output.</param>
    public PiiScrubbingMiddleware(ILogger<PiiScrubbingMiddleware<TInput, TOutput>> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ChainResult<TOutput>> InvokeAsync(
        TInput input,
        ChainContext context,
        ChainDelegate<TInput, TOutput> next,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var scrubbedInput = PiiScrubber.Scrub(input?.ToString() ?? string.Empty);
            _logger.LogDebug("Chain {Name} input (scrubbed): {Input}", context.ChainName, scrubbedInput);
        }

        var result = await next(input, context, cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var scrubbedOutput = PiiScrubber.Scrub(result.Value?.ToString() ?? string.Empty);
            _logger.LogDebug("Chain {Name} output (scrubbed): {Output}", context.ChainName, scrubbedOutput);
        }

        return result;
    }
}
