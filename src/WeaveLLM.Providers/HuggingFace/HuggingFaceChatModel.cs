using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Providers.HuggingFace;

/// <summary>
/// Hugging Face Inference API provider. Supports chat completion and feature-extraction
/// embeddings. HuggingFace is request/response only — streaming is not supported.
/// </summary>
public sealed class HuggingFaceChatModel(
    string apiKey,
    string modelId,
    string baseUrl = "https://api-inference.huggingface.co",
    HttpClient? httpClient = null) : WeaveLLM.Core.Providers.IChatModel, WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel
{
    private readonly HttpClient _http = httpClient ?? CreateDefaultClient(apiKey, baseUrl);
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public string ProviderName => "huggingface";

    /// <inheritdoc/>
    public string ModelId { get; } = modelId;

    /// <inheritdoc/>
    public int Dimensions => 768; // common for sentence-transformers

    /// <inheritdoc/>
    public async Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                inputs = new
                {
                    messages = messages.Select(m => new { role = RoleToString(m.Role), content = m.Content })
                },
                parameters = new
                {
                    max_new_tokens = options?.MaxTokens ?? 512,
                    temperature = options?.Temperature ?? 0.7f
                }
            };

            var response = await _http.PostAsJsonAsync($"models/{ModelId}", request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return MapHttpError<ChatResponse>(response.StatusCode, body);
            }

            var results = await response.Content.ReadFromJsonAsync<List<HuggingFaceGenerationResult>>(cancellationToken: cancellationToken);
            var generatedText = results?.FirstOrDefault()?.GeneratedText;
            if (generatedText is null)
                return ChainResult<ChatResponse>.Failure(WeaveLLMError.ProviderError("huggingface", "No generated text in response."));

            return ChainResult<ChatResponse>.Success(new ChatResponse { Content = generatedText });
        }
        catch (Exception ex)
        {
            return ChainResult<ChatResponse>.Failure(new WeaveLLMError(ex.Message, "PROVIDER_ERROR", ex));
        }
    }

    /// <summary>
    /// HuggingFace Inference API does not support token streaming.
    /// This method yields the full response as a single chunk.
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await ChatAsync(messages, options, cancellationToken);
        if (result.IsSuccess && result.Value?.Content is { } content)
            yield return content;
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
            var request = new { inputs = text };
            var response = await _http.PostAsJsonAsync(
                $"pipeline/feature-extraction/{ModelId}", request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return MapHttpError<float[]>(response.StatusCode, body);
            }

            // HF feature-extraction returns float[][] — take [0] as the sentence embedding
            var matrix = await response.Content.ReadFromJsonAsync<float[][]>(cancellationToken: cancellationToken);
            if (matrix is null || matrix.Length == 0)
                return ChainResult<float[]>.Failure(WeaveLLMError.ProviderError("huggingface", "Empty embedding response."));

            return ChainResult<float[]>.Success(matrix[0]);
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
        var results = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            var result = await EmbedAsync(texts[i], cancellationToken);
            if (!result.IsSuccess)
                return ChainResult<float[][]>.Failure(result.Error!);
            results[i] = result.Value!;
        }
        return ChainResult<float[][]>.Success(results);
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
                new WeaveLLMError("HuggingFace API key invalid.", "INVALID_INPUT")),
            System.Net.HttpStatusCode.ServiceUnavailable when body.Contains("loading") => ChainResult<T>.Failure(
                new WeaveLLMError("Model is loading on HuggingFace. Wait 20s and retry.", "MODEL_LOADING")),
            _ => ChainResult<T>.Failure(WeaveLLMError.ProviderError("huggingface", $"{statusCode}: {body}"))
        };

    private static string RoleToString(Role role) => role switch
    {
        Role.System => "system",
        Role.Assistant => "assistant",
        _ => "user"
    };

    private static HttpClient CreateDefaultClient(string apiKey, string baseUrl)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        return client;
    }

    private sealed record HuggingFaceGenerationResult(
        [property: JsonPropertyName("generated_text")] string? GeneratedText);
}
