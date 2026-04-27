using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Providers.OpenAI;

/// <summary>
/// OpenAI provider — supports GPT-4o, GPT-4-turbo, GPT-3.5-turbo, o1, o3 etc.
/// Uses raw HttpClient for maximum control, no vendor SDK dependency.
/// </summary>
public sealed class OpenAIChatModel(
    string apiKey,
    string modelId = "gpt-4o",
    string baseUrl = "https://api.openai.com/v1",
    HttpClient? httpClient = null) : WeaveLLM.Core.Providers.IChatModel
{
    private readonly HttpClient _http = httpClient ?? CreateDefaultClient(apiKey, baseUrl);

    public string ProviderName => "openai";
    public string ModelId { get; } = modelId;

    public async Task<ChainResult<ChatResponse>> ChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = BuildRequest(messages, options, stream: false);
            var response = await _http.PostAsJsonAsync("chat/completions", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return ChainResult<ChatResponse>.Failure(WeaveLLMError.ProviderError("openai", $"{response.StatusCode}: {error}"));
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIResponse>(cancellationToken: cancellationToken);
            var choice = result?.Choices?.FirstOrDefault();
            if (choice is null)
                return ChainResult<ChatResponse>.Failure(WeaveLLMError.ProviderError("openai", "Empty response"));

            var usage = new TokenUsage
            {
                PromptTokens = result?.Usage?.PromptTokens ?? 0,
                CompletionTokens = result?.Usage?.CompletionTokens ?? 0
            };
            var chatResponse = new ChatResponse
            {
                Content = choice.Message?.Content ?? string.Empty,
                Usage = new UsageStats(usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens)
            };

            return ChainResult<ChatResponse>.Success(chatResponse, usage);
        }
        catch (Exception ex)
        {
            return ChainResult<ChatResponse>.Failure(new WeaveLLMError(ex.Message, "PROVIDER_ERROR", ex));
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options, stream: true);
        var json = JsonSerializer.Serialize(request);
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
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
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data);
            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (content is not null) yield return content;
        }
    }

    public async Task<ChainResult<string>> CompleteAsync(string prompt, LLMOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await ChatAsync([Message.User(prompt)], options, cancellationToken);
        return result.Map(m => m.Content);
    }

    public async IAsyncEnumerable<string> StreamCompleteAsync(string prompt, LLMOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in StreamChatAsync([Message.User(prompt)], options, cancellationToken))
            yield return chunk;
    }

    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(text.Length / 4); // rough approximation; use tiktoken for accuracy

    private static object BuildRequest(IReadOnlyList<Message> messages, LLMOptions? options, bool stream) => new
    {
        model = "gpt-4o",
        messages = messages.Select(m => new { role = m.Role.ToString().ToLower(), content = m.Content }),
        temperature = options?.Temperature ?? 0.7f,
        max_tokens = options?.MaxTokens ?? 2048,
        top_p = options?.TopP ?? 1f,
        stream
    };

    private static HttpClient CreateDefaultClient(string apiKey, string baseUrl)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        return client;
    }

    // Response DTOs
    private sealed record OpenAIResponse(
        [property: JsonPropertyName("choices")] List<OpenAIChoice>? Choices,
        [property: JsonPropertyName("usage")] OpenAIUsage? Usage);

    private sealed record OpenAIChoice(
        [property: JsonPropertyName("message")] OpenAIMessage? Message);

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
}
