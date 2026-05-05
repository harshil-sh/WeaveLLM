using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Providers.Anthropic;

/// <summary>
/// Anthropic (Claude) provider — supports claude-opus-4, claude-sonnet-4, claude-haiku-4, etc.
/// Maps WeaveLLM's message format to Anthropic's messages API; system prompt goes to the
/// top-level "system" field, not inside the messages array.
/// </summary>
public sealed class AnthropicChatModel(
    string apiKey,
    string modelId = "claude-sonnet-4-5",
    HttpClient? httpClient = null) : WeaveLLM.Core.Providers.IChatModel
{
    private readonly HttpClient _http = httpClient ?? CreateDefaultClient(apiKey);

    /// <inheritdoc/>
    public string ProviderName => "anthropic";

    /// <inheritdoc/>
    public string ModelId { get; } = modelId;

    /// <inheritdoc/>
    public async Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var systemMessage = messages.FirstOrDefault(m => m.Role == Role.System)?.Content;
            var conversationMessages = messages
                .Where(m => m.Role != Role.System)
                .Select(m => new { role = m.Role == Role.User ? "user" : "assistant", content = m.Content })
                .ToList();

            var request = new Dictionary<string, object>
            {
                ["model"] = ModelId,
                ["max_tokens"] = options?.MaxTokens ?? 2048,
                ["messages"] = conversationMessages
            };

            if (!string.IsNullOrEmpty(systemMessage)) request["system"] = systemMessage;
            if (options?.Temperature is not null) request["temperature"] = options.Temperature;
            if (options?.TopP is not null) request["top_p"] = options.TopP;

            var response = await _http.PostAsJsonAsync("messages", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return MapHttpError<ChatResponse>(response.StatusCode, body);
            }

            var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: cancellationToken);
            var content = result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var usage = new TokenUsage
            {
                PromptTokens = result?.Usage?.InputTokens ?? 0,
                CompletionTokens = result?.Usage?.OutputTokens ?? 0
            };
            return ChainResult<ChatResponse>.Success(new ChatResponse
            {
                Content = content,
                Usage = new UsageStats(usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens)
            }, usage);
        }
        catch (Exception ex)
        {
            return ChainResult<ChatResponse>.Failure(new WeaveLLMError(ex.Message, "PROVIDER_ERROR", ex));
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var systemMessage = messages.FirstOrDefault(m => m.Role == Role.System)?.Content;
        var conversationMessages = messages
            .Where(m => m.Role != Role.System)
            .Select(m => new { role = m.Role == Role.User ? "user" : "assistant", content = m.Content })
            .ToList();

        var request = new Dictionary<string, object>
        {
            ["model"] = ModelId,
            ["max_tokens"] = options?.MaxTokens ?? 2048,
            ["messages"] = conversationMessages,
            ["stream"] = true
        };
        if (!string.IsNullOrEmpty(systemMessage)) request["system"] = systemMessage;

        using var req = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];

            var evt = JsonSerializer.Deserialize<AnthropicStreamEvent>(data);
            if (evt?.Type == "content_block_delta"
                && evt.Delta?.Type == "text_delta"
                && evt.Delta.Text is { } text)
                yield return text;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChainResult<string>> StreamChatSafeAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = StreamChatAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return ChainResult<string>.Failure(
                    WeaveLLMError.Cancelled($"Streaming cancelled by '{ProviderName}'."));
                yield break;
            }

            ChainResult<string>? failure = null;
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException ex)
            {
                failure = ChainResult<string>.Failure(
                    WeaveLLMError.Cancelled($"Streaming cancelled by '{ProviderName}'.", ex));
                hasNext = false;
            }
            catch (Exception ex)
            {
                failure = ChainResult<string>.Failure(
                    WeaveLLMError.ProviderError(ProviderName, ex.Message, ex));
                hasNext = false;
            }

            if (failure is not null) { yield return failure; yield break; }
            if (!hasNext) yield break;
            yield return ChainResult<string>.Success(enumerator.Current);
        }
    }

    /// <inheritdoc/>
    public async Task<ChainResult<string>> CompleteAsync(
        string prompt,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ChatAsync([Message.User(prompt)], options, cancellationToken);
        return result.Map(m => m.Content);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamCompleteAsync(
        string prompt,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in StreamChatAsync([Message.User(prompt)], options, cancellationToken))
            yield return chunk;
    }

    /// <inheritdoc/>
    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(text.Length / 4);

    private static ChainResult<T> MapHttpError<T>(System.Net.HttpStatusCode statusCode, string body) =>
        statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => ChainResult<T>.Failure(
                new WeaveLLMError("Anthropic API key invalid.", "INVALID_INPUT")),
            System.Net.HttpStatusCode.TooManyRequests => ChainResult<T>.Failure(
                new WeaveLLMError("Anthropic rate limit hit.", "RATE_LIMITED")),
            _ when (int)statusCode == 529 => ChainResult<T>.Failure(
                new WeaveLLMError("Anthropic overloaded. Retry.", "TIMEOUT")),
            _ => ChainResult<T>.Failure(WeaveLLMError.ProviderError("anthropic", $"{statusCode}: {body}"))
        };

    private static HttpClient CreateDefaultClient(string apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        return client;
    }

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] List<AnthropicContent>? Content,
        [property: JsonPropertyName("usage")] AnthropicUsage? Usage);

    private sealed record AnthropicContent(
        [property: JsonPropertyName("text")] string? Text);

    private sealed record AnthropicUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);

    private sealed record AnthropicStreamEvent(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("delta")] AnthropicStreamDelta? Delta);

    private sealed record AnthropicStreamDelta(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}
