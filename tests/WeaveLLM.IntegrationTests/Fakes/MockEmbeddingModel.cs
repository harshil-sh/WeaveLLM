#nullable enable
using WeaveLLM.Core.Models;

namespace WeaveLLM.IntegrationTests.Fakes;

/// <summary>
/// Deterministic embedding model for integration tests.
/// Generates reproducible unit-length vectors derived from a stable hash of the input text.
/// Identical text always produces identical vectors, giving cosine similarity of 1.0.
/// </summary>
public sealed class MockEmbeddingModel : IEmbeddingModel
{
    private const int Dimensions = 8;

    /// <inheritdoc/>
    public string ModelId => "mock-embedding";

    /// <inheritdoc/>
    public string ProviderId => "mock";

    /// <inheritdoc/>
    public Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(ChainResult<float[]>.Success(GenerateVector(text)));

    /// <inheritdoc/>
    public Task<ChainResult<float[][]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        var vectors = texts.Select(GenerateVector).ToArray();
        return Task.FromResult(ChainResult<float[][]>.Success(vectors));
    }

    private static float[] GenerateVector(string text)
    {
        var seed = Math.Abs(string.GetHashCode(text, StringComparison.Ordinal));
        var rng = new Random(seed);
        var vector = new float[Dimensions];

        for (var i = 0; i < Dimensions; i++)
            vector[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0f)
            for (var i = 0; i < Dimensions; i++)
                vector[i] /= magnitude;

        return vector;
    }
}
