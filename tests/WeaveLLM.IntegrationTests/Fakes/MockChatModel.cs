#nullable enable
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WeaveLLM.Core.Models;

namespace WeaveLLM.IntegrationTests.Fakes;

/// <summary>
/// Thread-safe test double for <see cref="IStreamingChatModel"/> that returns a configurable fixed response.
/// </summary>
public sealed class MockChatModel : IStreamingChatModel
{
    private readonly string _fixedResponse;
    private readonly WeaveLLMError? _errorToReturn;
    private int _chatCallCount;
    private readonly ConcurrentBag<IReadOnlyList<Message>> _receivedMessages = new();

    /// <summary>Number of <see cref="ChatAsync"/> calls made, updated atomically.</summary>
    public int ChatCallCount => _chatCallCount;

    /// <summary>All message lists received by <see cref="ChatAsync"/>, in no guaranteed order.</summary>
    public IReadOnlyCollection<IReadOnlyList<Message>> ReceivedMessages => _receivedMessages;

    /// <inheritdoc/>
    public string ModelId => "mock";

    /// <inheritdoc/>
    public string ProviderId => "mock";

    /// <param name="fixedResponse">Text returned by every <see cref="ChatAsync"/> call.</param>
    /// <param name="errorToReturn">When non-null, every call returns this error instead of a success.</param>
    public MockChatModel(string fixedResponse = "Mock response", WeaveLLMError? errorToReturn = null)
    {
        _fixedResponse = fixedResponse;
        _errorToReturn = errorToReturn;
    }

    /// <inheritdoc/>
    public Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _chatCallCount);
        _receivedMessages.Add(messages);

        if (_errorToReturn is not null)
            return Task.FromResult(ChainResult<ChatResponse>.Failure(_errorToReturn));

        return Task.FromResult(ChainResult<ChatResponse>.Success(new ChatResponse
        {
            Content = _fixedResponse,
            FinishReason = "stop"
        }));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var word in _fixedResponse.Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return word + " ";
            await Task.Delay(10, cancellationToken);
        }
    }
}
