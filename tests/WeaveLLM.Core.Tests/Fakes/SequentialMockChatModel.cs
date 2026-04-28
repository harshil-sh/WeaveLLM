using System.Runtime.CompilerServices;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Core.Tests.Fakes;

/// <summary>
/// A mock chat model that returns a pre-programmed sequence of responses, one per call.
/// When the sequence is exhausted the last response is repeated.
/// </summary>
public sealed class SequentialMockChatModel : IStreamingChatModel
{
    private readonly string[] _responses;
    private int _callIndex;

    /// <inheritdoc/>
    public string ModelId => "sequential-mock";

    /// <inheritdoc/>
    public string ProviderId => "mock";

    /// <summary>Number of <see cref="ChatAsync"/> calls made so far.</summary>
    public int CallCount => _callIndex;

    /// <param name="responses">Responses returned in order. The last one repeats indefinitely.</param>
    public SequentialMockChatModel(params string[] responses)
    {
        if (responses.Length == 0)
            throw new ArgumentException("At least one response is required.", nameof(responses));
        _responses = responses;
    }

    /// <inheritdoc/>
    public Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var idx = Math.Min(_callIndex, _responses.Length - 1);
        Interlocked.Increment(ref _callIndex);

        var response = new ChatResponse
        {
            Content = _responses[idx],
            FinishReason = "stop",
            Usage = new UsageStats(10, 20, 30)
        };

        return Task.FromResult(ChainResult<ChatResponse>.Success(response));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await ChatAsync(messages, options, cancellationToken);
        yield return result.Value?.Content ?? string.Empty;
    }
}
