#nullable enable
using System.Runtime.CompilerServices;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Testing;

/// <summary>
/// Pre-built test double for <see cref="WeaveLLM.Core.Providers.IChatModel"/> and
/// <see cref="WeaveLLM.Core.Providers.IStreamingChatModel"/>.
/// Configure <see cref="Tokens"/>, <see cref="BlockingResponse"/>, and
/// <see cref="ErrorAfterTokens"/> to drive the fake's behaviour without touching a real API.
/// </summary>
/// <example>
/// <code>
/// var fake = new FakeStreamingChatModel
/// {
///     Tokens = ["Hello", ", ", "world", "!"],
///     BlockingResponse = "Hello, world!"
/// };
/// var result = await fake.ChatAsync([Message.User("Hi")]);
/// result.IsSuccess.Should().BeTrue();
/// result.Value!.Content.Should().Be("Hello, world!");
/// </code>
/// </example>
public sealed class FakeStreamingChatModel : WeaveLLM.Core.Providers.IChatModel
{
    /// <summary>Token strings yielded one-by-one by <see cref="StreamChatAsync"/> and <see cref="StreamChatSafeAsync"/>.</summary>
    public IEnumerable<string> Tokens { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When non-<c>null</c>, the stream injects this error after <see cref="TokensBeforeError"/>
    /// tokens have been yielded, then stops. The error is returned as a failed
    /// <see cref="ChainResult{T}"/> by <see cref="StreamChatSafeAsync"/> and thrown as an
    /// <see cref="InvalidOperationException"/> by <see cref="StreamChatAsync"/>.
    /// </summary>
    public WeaveLLMError? ErrorAfterTokens { get; set; }

    /// <summary>
    /// Number of tokens to yield before injecting <see cref="ErrorAfterTokens"/>.
    /// Ignored when <see cref="ErrorAfterTokens"/> is <c>null</c>.
    /// </summary>
    public int TokensBeforeError { get; set; } = 0;

    /// <summary>
    /// The content returned by <see cref="ChatAsync"/> and <see cref="CompleteAsync"/>.
    /// When <c>null</c>, both methods return a success result with an empty string.
    /// </summary>
    public string? BlockingResponse { get; set; }

    /// <inheritdoc/>
    public string ProviderName => "fake";

    /// <inheritdoc/>
    public string ModelId => "fake-model";

    /// <inheritdoc/>
    public Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse
        {
            Content = BlockingResponse ?? string.Empty,
            FinishReason = "stop"
        };
        return Task.FromResult(ChainResult<ChatResponse>.Success(response));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var yielded = 0;
        foreach (var token in Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ErrorAfterTokens is not null && yielded >= TokensBeforeError)
                throw new InvalidOperationException(ErrorAfterTokens.Message);

            yield return token;
            yielded++;
            await Task.Yield();
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChainResult<string>> StreamChatSafeAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var yielded = 0;
        foreach (var token in Tokens)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return ChainResult<string>.Failure(
                    WeaveLLMError.Cancelled("Streaming cancelled by 'fake'."));
                yield break;
            }

            if (ErrorAfterTokens is not null && yielded >= TokensBeforeError)
            {
                yield return ChainResult<string>.Failure(ErrorAfterTokens);
                yield break;
            }

            yield return ChainResult<string>.Success(token);
            yielded++;
            await Task.Yield();
        }
    }

    /// <inheritdoc/>
    public Task<ChainResult<string>> CompleteAsync(
        string prompt,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ChainResult<string>.Success(BlockingResponse ?? string.Empty));

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamCompleteAsync(
        string prompt,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var token in StreamChatAsync(Array.Empty<Message>(), options, cancellationToken))
            yield return token;
    }

    /// <inheritdoc/>
    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
