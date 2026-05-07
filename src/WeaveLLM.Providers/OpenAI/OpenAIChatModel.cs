using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Providers.OpenAI;

/// <summary>
/// OpenAI provider — supports GPT-4o, GPT-4-turbo, o1, o3, and other OpenAI chat models.
/// Also provides text embeddings via the Embeddings API.
/// Uses raw HttpClient; no vendor SDK dependency.
/// </summary>
public sealed class OpenAIChatModel : WeaveLLM.Core.Providers.IChatModel, WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel
{
    private readonly HttpClient? _httpDirect;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly string _httpClientName;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private HttpClient Http => _httpDirect ?? _httpClientFactory!.CreateClient(_httpClientName);

    /// <summary>
    /// Use in unit tests or when HttpClient lifetime is managed externally.
    /// For production, prefer the IHttpClientFactory overload.
    /// </summary>
    public OpenAIChatModel(
        string apiKey,
        string modelId = "gpt-4o",
        string baseUrl = "https://api.openai.com/v1",
        HttpClient? httpClient = null)
    {
        ModelId = modelId;
        _httpDirect = httpClient ?? CreateDefaultClient(apiKey, baseUrl);
        _httpClientName = string.Empty;
    }

    /// <summary>
    /// Preferred for production — resolves HttpClient via IHttpClientFactory,
    /// enabling named-client configuration and Polly retry policies.
    /// </summary>
    public OpenAIChatModel(
        string apiKey,
        string modelId,
        string baseUrl,
        IHttpClientFactory httpClientFactory,
        string httpClientName = "weave-openai")
    {
        ModelId = modelId;
        _httpClientFactory = httpClientFactory;
        _httpClientName = httpClientName;
    }

    /// <inheritdoc/>
    public string ProviderName => "openai";

    /// <inheritdoc/>
    public string ModelId { get; }

    /// <inheritdoc/>
    public int Dimensions => 1536; // text-embedding-3-small default

    /// <inheritdoc/>
    public async Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = BuildChatRequest(messages, options, stream: false);
            var response = await Http.PostAsJsonAsync("chat/completions", request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return MapHttpError<ChatResponse>(response.StatusCode, body);
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIResponse>(cancellationToken: cancellationToken);
            var choice = result?.Choices?.FirstOrDefault();
            if (choice is null)
                return ChainResult<ChatResponse>.Failure(WeaveLLMError.ProviderError("openai", "Empty response from API."));

            var usage = new TokenUsage
            {
                PromptTokens = result?.Usage?.PromptTokens ?? 0,
                CompletionTokens = result?.Usage?.CompletionTokens ?? 0
            };
            return ChainResult<ChatResponse>.Success(new ChatResponse
            {
                Content = choice.Message?.Content ?? string.Empty,
                FinishReason = choice.FinishReason,
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
        var request = BuildChatRequest(messages, options, stream: true);
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data);
            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (content is not null) yield return content;
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
    public async Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new { input = text, model = "text-embedding-3-small" };
            var response = await Http.PostAsJsonAsync("embeddings", request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ChainResult<float[]>.Failure(WeaveLLMError.ProviderError("openai", $"Embedding request failed: {response.StatusCode}: {body}"));
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(cancellationToken: cancellationToken);
            var embedding = result?.Data?.FirstOrDefault()?.Embedding;
            if (embedding is null)
                return ChainResult<float[]>.Failure(WeaveLLMError.ProviderError("openai", "No embedding data returned."));

            return ChainResult<float[]>.Success(embedding);
        }
        catch (Exception ex)
        {
            return ChainResult<float[]>.Failure(new WeaveLLMError(ex.Message, "PROVIDER_ERROR", ex));
        }
    }

    /// <inheritdoc/>
    public async Task<ChainResult<float[][]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new { input = texts, model = "text-embedding-3-small" };
            var response = await Http.PostAsJsonAsync("embeddings", request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ChainResult<float[][]>.Failure(WeaveLLMError.ProviderError("openai", $"Batch embedding failed: {response.StatusCode}: {body}"));
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(cancellationToken: cancellationToken);
            if (result?.Data is null)
                return ChainResult<float[][]>.Failure(WeaveLLMError.ProviderError("openai", "No embedding data returned."));

            return ChainResult<float[][]>.Success(
                result.Data.Select(d => d.Embedding ?? Array.Empty<float>()).ToArray());
        }
        catch (Exception ex)
        {
            return ChainResult<float[][]>.Failure(new WeaveLLMError(ex.Message, "PROVIDER_ERROR", ex));
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
                new WeaveLLMError("OpenAI API key is invalid. Check WeaveLLM:OpenAI:ApiKey.", "INVALID_INPUT")),
            System.Net.HttpStatusCode.TooManyRequests => ChainResult<T>.Failure(
                new WeaveLLMError("OpenAI rate limit hit. Add RateLimitingMiddleware.", "RATE_LIMITED")),
            System.Net.HttpStatusCode.ServiceUnavailable => ChainResult<T>.Failure(
                new WeaveLLMError("OpenAI service unavailable. Retry with RetryMiddleware.", "TIMEOUT")),
            _ => ChainResult<T>.Failure(WeaveLLMError.ProviderError("openai", $"{statusCode}: {body}"))
        };

    private object BuildChatRequest(IReadOnlyList<Message> messages, LLMOptions? options, bool stream) => new
    {
        model = ModelId,
        messages = messages.Select(m => new { role = RoleToString(m.Role), content = m.Content }),
        temperature = options?.Temperature ?? 0.7f,
        max_tokens = options?.MaxTokens ?? 2048,
        top_p = options?.TopP ?? 1f,
        stop = options?.StopSequences,
        stream
    };

    private static string RoleToString(Role role) => role switch
    {
        Role.System => "system",
        Role.Assistant => "assistant",
        Role.Tool => "tool",
        _ => "user"
    };

    private static HttpClient CreateDefaultClient(string apiKey, string baseUrl)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        return client;
    }

    private sealed record OpenAIResponse(
        [property: JsonPropertyName("choices")] List<OpenAIChoice>? Choices,
        [property: JsonPropertyName("usage")] OpenAIUsage? Usage);

    private sealed record OpenAIChoice(
        [property: JsonPropertyName("message")] OpenAIMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenAIMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record OpenAIUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);

    private sealed record OpenAIStreamChunk(
        [property: JsonPropertyName("choices")] List<OpenAIStreamChoice>? Choices);

    private sealed record OpenAIStreamChoice(
        [property: JsonPropertyName("delta")] OpenAIStreamDelta? Delta);

    private sealed record OpenAIStreamDelta(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record OpenAIEmbeddingResponse(
        [property: JsonPropertyName("data")] List<OpenAIEmbeddingData>? Data);

    private sealed record OpenAIEmbeddingData(
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
