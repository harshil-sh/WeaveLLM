using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Providers.Ollama;

/// <summary>
/// Ollama provider for locally-hosted models. Connects to a running Ollama server
/// (default: http://localhost:11434) and supports chat, streaming, and embeddings.
/// </summary>
public sealed class OllamaChatModel(
    string baseUrl = "http://localhost:11434",
    string modelId = "llama3",
    HttpClient? httpClient = null) : WeaveLLM.Core.Providers.IChatModel, WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel
{
    private readonly HttpClient _http = httpClient ?? CreateDefaultClient(baseUrl);
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public string ProviderName => "ollama";

    /// <inheritdoc/>
    public string ModelId { get; } = modelId;

    /// <inheritdoc/>
    public int Dimensions => 4096; // varies by model; llama3 default

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
                model = ModelId,
                messages = messages.Select(m => new { role = RoleToString(m.Role), content = m.Content }),
                stream = false,
                options = BuildOptions(options)
            };

            var response = await _http.PostAsJsonAsync("api/chat", request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return MapHttpError<ChatResponse>(response.StatusCode, body);
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
            return ChainResult<ChatResponse>.Success(new ChatResponse
            {
                Content = result?.Message?.Content ?? string.Empty,
                FinishReason = result?.Done == true ? "stop" : null
            });
        }
        catch (HttpRequestException ex) when (IsConnectionRefused(ex))
        {
            return ChainResult<ChatResponse>.Failure(new WeaveLLMError(
                "Ollama is not running. Start it with: ollama serve", "PROVIDER_UNAVAILABLE", ex));
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
        var request = new
        {
            model = ModelId,
            messages = messages.Select(m => new { role = RoleToString(m.Role), content = m.Content }),
            stream = true,
            options = BuildOptions(options)
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line, _jsonOptions);
            if (chunk?.Message?.Content is { } content)
                yield return content;

            if (chunk?.Done == true) break;
        }
    }

    /// <inheritdoc/>
    public async Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new { model = ModelId, prompt = text };
            var response = await _http.PostAsJsonAsync("api/embeddings", request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return MapHttpError<float[]>(response.StatusCode, body);
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken);
            if (result?.Embedding is null)
                return ChainResult<float[]>.Failure(WeaveLLMError.ProviderError("ollama", "No embedding returned."));

            return ChainResult<float[]>.Success(result.Embedding);
        }
        catch (HttpRequestException ex) when (IsConnectionRefused(ex))
        {
            return ChainResult<float[]>.Failure(new WeaveLLMError(
                "Ollama is not running. Start it with: ollama serve", "PROVIDER_UNAVAILABLE", ex));
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

    private static bool IsConnectionRefused(HttpRequestException ex) =>
        ex.InnerException is System.Net.Sockets.SocketException se &&
        se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused;

    private static ChainResult<T> MapHttpError<T>(System.Net.HttpStatusCode statusCode, string body) =>
        statusCode switch
        {
            System.Net.HttpStatusCode.NotFound => ChainResult<T>.Failure(
                new WeaveLLMError($"Model not found. Pull it with: ollama pull <model>", "INVALID_INPUT")),
            _ => ChainResult<T>.Failure(WeaveLLMError.ProviderError("ollama", $"{statusCode}: {body}"))
        };

    private static object BuildOptions(LLMOptions? options)
    {
        if (options is null) return new { };
        return new
        {
            temperature = options.Temperature,
            num_predict = options.MaxTokens,
            top_p = options.TopP
        };
    }

    private static string RoleToString(Role role) => role switch
    {
        Role.System => "system",
        Role.Assistant => "assistant",
        _ => "user"
    };

    private static HttpClient CreateDefaultClient(string baseUrl)
    {
        return new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaMessage? Message,
        [property: JsonPropertyName("done")] bool Done);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record OllamaStreamChunk(
        [property: JsonPropertyName("message")] OllamaMessage? Message,
        [property: JsonPropertyName("done")] bool Done);

    private sealed record OllamaEmbeddingResponse(
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
