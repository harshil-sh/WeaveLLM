#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Memory.Qdrant;

/// <summary>
/// Qdrant-backed vector store using the Qdrant REST API.
/// Supports upsert, cosine-similarity search, and deletion via HTTP.
/// Use <see cref="CreateCollectionIfNotExistsAsync"/> to initialise the collection before first use.
/// </summary>
public sealed class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _http;
    private readonly string _collectionName;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initialises a new <see cref="QdrantVectorStore"/> that targets the given Qdrant endpoint and collection.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create the underlying <see cref="HttpClient"/>.</param>
    /// <param name="endpoint">Base URL of the Qdrant instance, e.g. <c>http://localhost:6333</c>.</param>
    /// <param name="collectionName">Name of the Qdrant collection to read from and write to.</param>
    public QdrantVectorStore(IHttpClientFactory httpClientFactory, string endpoint, string collectionName)
    {
        _http = httpClientFactory.CreateClient("qdrant");
        _http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        _collectionName = collectionName;
    }

    /// <summary>
    /// Creates the Qdrant collection if it does not already exist, using cosine distance.
    /// Safe to call on every startup — it is a no-op when the collection already exists.
    /// </summary>
    /// <param name="vectorSize">Dimensionality of the embedding vectors stored in this collection.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public async Task CreateCollectionIfNotExistsAsync(int vectorSize, CancellationToken cancellationToken = default)
    {
        var checkResponse = await _http.GetAsync(
            $"collections/{_collectionName}", cancellationToken);

        if (checkResponse.StatusCode == HttpStatusCode.OK)
            return;

        if (checkResponse.StatusCode != HttpStatusCode.NotFound)
        {
            var body = await checkResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"[qdrant] Unexpected status {(int)checkResponse.StatusCode} checking collection: {body}");
        }

        var createBody = new
        {
            vectors = new { size = vectorSize, distance = "Cosine" }
        };

        var createResponse = await _http.PutAsJsonAsync(
            $"collections/{_collectionName}", createBody, _jsonOptions, cancellationToken);

        if (!createResponse.IsSuccessStatusCode)
        {
            var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"[qdrant] Failed to create collection '{_collectionName}': {body}");
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(VectorEntry entry, CancellationToken cancellationToken = default)
    {
        var pointId = ToQdrantId(entry.Id);
        var payload = BuildPayload(entry);

        var body = new
        {
            points = new[]
            {
                new
                {
                    id = pointId,
                    vector = entry.Vector,
                    payload
                }
            }
        };

        var response = await _http.PutAsJsonAsync(
            $"collections/{_collectionName}/points", body, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                WeaveLLMError.ProviderError("qdrant",
                    $"UpsertAsync failed ({(int)response.StatusCode}): {responseBody}").Message);
        }
    }

    /// <inheritdoc />
    public async Task<ChainResult<IReadOnlyList<ScoredEntry>>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new
            {
                vector = queryVector,
                limit = topK,
                with_payload = true
            };

            var response = await _http.PostAsJsonAsync(
                $"collections/{_collectionName}/points/search", body, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return ChainResult<IReadOnlyList<ScoredEntry>>.Failure(
                    WeaveLLMError.ProviderError("qdrant",
                        $"SearchAsync failed ({(int)response.StatusCode}): {responseBody}"));
            }

            var result = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(
                _jsonOptions, cancellationToken);

            if (result?.Result is null)
                return ChainResult<IReadOnlyList<ScoredEntry>>.Success([]);

            var scored = result.Result
                .Select(hit => new ScoredEntry(HitToVectorEntry(hit), (float)hit.Score))
                .ToList();

            return ChainResult<IReadOnlyList<ScoredEntry>>.Success(scored);
        }
        catch (HttpRequestException ex)
        {
            return ChainResult<IReadOnlyList<ScoredEntry>>.Failure(
                WeaveLLMError.ProviderError("qdrant",
                    $"HTTP error during SearchAsync: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var pointId = ToQdrantId(id);

        var body = new { points = new[] { pointId } };

        var response = await _http.PostAsJsonAsync(
            $"collections/{_collectionName}/points/delete", body, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                WeaveLLMError.ProviderError("qdrant",
                    $"DeleteAsync failed ({(int)response.StatusCode}): {responseBody}").Message);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a deterministic UUID from an arbitrary string ID so that upsert deduplication is stable.
    /// The original string ID is preserved in the point payload under the key <c>__id</c>.
    /// </summary>
    private static string ToQdrantId(string id)
    {
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(id));
        return new Guid(hashBytes).ToString();
    }

    private static Dictionary<string, string> BuildPayload(VectorEntry entry)
    {
        var payload = new Dictionary<string, string>(
            entry.Metadata ?? new Dictionary<string, string>())
        {
            ["__id"] = entry.Id,
            ["__content"] = entry.Content
        };
        return payload;
    }

    private static VectorEntry HitToVectorEntry(QdrantHit hit)
    {
        var payload = hit.Payload ?? new Dictionary<string, JsonElement>();

        var originalId = payload.TryGetValue("__id", out var idEl)
            ? idEl.GetString() ?? hit.Id
            : hit.Id;

        var content = payload.TryGetValue("__content", out var contentEl)
            ? contentEl.GetString() ?? string.Empty
            : string.Empty;

        var metadata = payload
            .Where(kv => kv.Key != "__id" && kv.Key != "__content")
            .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? string.Empty);

        return new VectorEntry(originalId, [], content, metadata);
    }

    // ─── Response DTOs ────────────────────────────────────────────────────────────

    private sealed class QdrantSearchResponse
    {
        [JsonPropertyName("result")]
        public List<QdrantHit>? Result { get; init; }
    }

    private sealed class QdrantHit
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; init; }

        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; init; }
    }
}
