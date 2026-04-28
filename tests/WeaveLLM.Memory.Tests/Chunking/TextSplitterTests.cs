using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Memory.Chunking;
using Xunit;

namespace WeaveLLM.Memory.Tests.Chunking;

public sealed class TextSplitterTests
{
    // ─── FixedSizeTextSplitter ────────────────────────────────────────────────────

    [Fact]
    public void FixedSizeTextSplitter_Split_EmptyString_ReturnsEmptyList()
    {
        var splitter = new FixedSizeTextSplitter(chunkSize: 10, chunkOverlap: 2);
        splitter.Split(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void FixedSizeTextSplitter_Split_ShortText_ReturnsSingleChunk()
    {
        var splitter = new FixedSizeTextSplitter(chunkSize: 100, chunkOverlap: 10);
        var result = splitter.Split("hello world");

        result.Should().HaveCount(1);
        result[0].Should().Be("hello world");
    }

    [Fact]
    public void FixedSizeTextSplitter_Split_LongText_ReturnsMultipleChunks()
    {
        var text = new string('A', 1000);
        var splitter = new FixedSizeTextSplitter(chunkSize: 200, chunkOverlap: 50);

        var result = splitter.Split(text);

        result.Should().HaveCountGreaterThan(1);
        result.Should().AllSatisfy(c => c.Length.Should().BeLessOrEqualTo(200));
    }

    [Fact]
    public void FixedSizeTextSplitter_Split_OverlapPrependsEndOfPreviousChunk()
    {
        // 10 chars, size=6, overlap=2  →  chunk0: [0..5], chunk1 starts at 4
        var text = "ABCDEFGHIJ";
        var splitter = new FixedSizeTextSplitter(chunkSize: 6, chunkOverlap: 2);

        var result = splitter.Split(text);

        result.Should().HaveCountGreaterThan(1);
        // The second chunk should start with the last 2 chars of the first chunk
        result[1].Should().StartWith(result[0][^2..]);
    }

    [Fact]
    public void FixedSizeTextSplitter_Split_NoChunkExceedsChunkSize()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 500));
        var splitter = new FixedSizeTextSplitter(chunkSize: 100, chunkOverlap: 20);

        var result = splitter.Split(text);

        result.Should().AllSatisfy(c => c.Length.Should().BeLessOrEqualTo(100));
    }

    // ─── RecursiveTextSplitter ────────────────────────────────────────────────────

    [Fact]
    public void RecursiveTextSplitter_Split_EmptyString_ReturnsEmptyList()
    {
        var splitter = new RecursiveTextSplitter();
        splitter.Split(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void RecursiveTextSplitter_Split_ShortText_ReturnsSingleChunk()
    {
        var splitter = new RecursiveTextSplitter(chunkSize: 1000);
        var result = splitter.Split("Short paragraph.");

        result.Should().HaveCount(1);
        result[0].Should().Be("Short paragraph.");
    }

    [Fact]
    public void RecursiveTextSplitter_Split_LongParagraphs_SplitsOnDoubleNewline()
    {
        var para1 = new string('A', 400);
        var para2 = new string('B', 400);
        var text = $"{para1}\n\n{para2}";
        var splitter = new RecursiveTextSplitter(chunkSize: 500, chunkOverlap: 0);

        var result = splitter.Split(text);

        result.Should().HaveCountGreaterThan(1);
        result.Should().AllSatisfy(c => c.Length.Should().BeLessOrEqualTo(500));
    }

    [Fact]
    public void RecursiveTextSplitter_Split_LongText_NoChunkExceedsChunkSize()
    {
        var text = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"Sentence number {i} is here"));
        var splitter = new RecursiveTextSplitter(chunkSize: 300, chunkOverlap: 50);

        var result = splitter.Split(text);

        result.Should().HaveCountGreaterThan(1);
        result.Should().AllSatisfy(c => c.Length.Should().BeLessOrEqualTo(300));
    }

    // ─── SemanticTextSplitter ─────────────────────────────────────────────────────

    [Fact]
    public void SemanticTextSplitter_Split_EmptyString_ReturnsEmptyList()
    {
        var model = new MockEmbeddingModel(dimensions: 4);
        var splitter = new SemanticTextSplitter(model, threshold: 0.85f, maxChunkSize: 1500);

        splitter.Split(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void SemanticTextSplitter_Split_SingleSentence_ReturnsSingleChunk()
    {
        var model = new MockEmbeddingModel(dimensions: 4);
        var splitter = new SemanticTextSplitter(model, threshold: 0.85f, maxChunkSize: 1500);

        var result = splitter.Split("Only one sentence here.");

        result.Should().HaveCount(1);
    }

    [Fact]
    public void SemanticTextSplitter_Split_LowSimilarity_SplitsIntoMultipleChunks()
    {
        // Model returns alternating high/low similarity — triggers splits
        var model = new AlternatingEmbeddingModel();
        var splitter = new SemanticTextSplitter(model, threshold: 0.9f, maxChunkSize: 2000);

        // Provide 4 sentences that will alternate similarity
        var text = "First sentence here. Second sentence here. Third sentence here. Fourth sentence here.";
        var result = splitter.Split(text);

        // Alternating embeddings should cause at least one split
        result.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void SemanticTextSplitter_Split_OversizedSentence_DelegatesToFixedSizeSplitter()
    {
        var model = new MockEmbeddingModel(dimensions: 4);
        var splitter = new SemanticTextSplitter(model, threshold: 0.85f, maxChunkSize: 20);

        // Single very long "sentence" (no ". " delimiter)
        var longText = new string('X', 100);
        var result = splitter.Split(longText);

        result.Should().HaveCountGreaterThan(1);
        result.Should().AllSatisfy(c => c.Length.Should().BeLessOrEqualTo(20));
    }

    // ─── Mock embedding models ────────────────────────────────────────────────────

    /// <summary>Returns an identical fixed vector for every input — cosine similarity = 1.0.</summary>
    private sealed class MockEmbeddingModel : IEmbeddingModel
    {
        private readonly int _dimensions;
        public string ModelId => "mock";
        public string ProviderId => "mock";

        public MockEmbeddingModel(int dimensions = 4) => _dimensions = dimensions;

        public Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken ct = default)
        {
            var vec = Enumerable.Repeat(1.0f / MathF.Sqrt(_dimensions), _dimensions).ToArray();
            return Task.FromResult(ChainResult<float[]>.Success(vec));
        }

        public Task<ChainResult<float[][]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            var vecs = texts.Select(_ => EmbedAsync(_, ct).Result.Value!).ToArray();
            return Task.FromResult(ChainResult<float[][]>.Success(vecs));
        }
    }

    /// <summary>
    /// Alternates between two orthogonal vectors to force low cosine similarity on every
    /// other sentence, triggering chunk boundaries.
    /// </summary>
    private sealed class AlternatingEmbeddingModel : IEmbeddingModel
    {
        private int _callCount;
        public string ModelId => "alternating";
        public string ProviderId => "mock";

        private static readonly float[] VecA = [1f, 0f, 0f, 0f];
        private static readonly float[] VecB = [0f, 1f, 0f, 0f]; // orthogonal → similarity = 0

        public Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken ct = default)
        {
            var vec = Interlocked.Increment(ref _callCount) % 2 == 1 ? VecA : VecB;
            return Task.FromResult(ChainResult<float[]>.Success(vec));
        }

        public Task<ChainResult<float[][]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            var vecs = texts.Select(t => EmbedAsync(t, ct).Result.Value!).ToArray();
            return Task.FromResult(ChainResult<float[][]>.Success(vecs));
        }
    }
}
