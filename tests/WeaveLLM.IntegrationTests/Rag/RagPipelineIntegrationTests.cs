#nullable enable
using FluentAssertions;
using WeaveLLM.IntegrationTests.Fakes;
using WeaveLLM.Memory.Chunking;
using WeaveLLM.Memory.InMemory;
using WeaveLLM.Memory.Rag;
using WeaveLLM.Memory.Search;
using Xunit;

namespace WeaveLLM.IntegrationTests.Rag;

public class RagPipelineIntegrationTests
{
    [IntegrationFact]
    public async Task Rag_IndexAndQuery_ReturnsAnswer()
    {
        var store = new InMemoryStore();
        var embedding = new MockEmbeddingModel();
        var chat = new MockChatModel("The answer is WeaveLLM.");
        var splitter = new FixedSizeTextSplitter(500, 0);
        var pipeline = new DefaultRagPipeline(store, embedding, chat, splitter);

        var docs = new[]
        {
            new Document("WeaveLLM is a .NET AI orchestration framework.", "doc1"),
            new Document("It supports multiple providers including OpenAI and Anthropic.", "doc2"),
            new Document("The framework features a graph-based execution engine.", "doc3"),
        };

        var indexResult = await pipeline.IndexAsync(docs);
        indexResult.IsSuccess.Should().BeTrue();
        indexResult.Value.Should().Be(3);

        var queryResult = await pipeline.QueryAsync("What is WeaveLLM?");

        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value!.Answer.Should().NotBeEmpty();
        queryResult.Value.SourceChunks.Should().NotBeEmpty();
        queryResult.Value.Sources.Should().NotBeEmpty();
        queryResult.Value.Sources.Should().AllSatisfy(s => new[] { "doc1", "doc2", "doc3" }.Should().Contain(s));
    }

    [IntegrationFact]
    public async Task Rag_HybridSearch_ReturnsMoreRelevantResults()
    {
        var innerStore = new InMemoryStore();
        var hybridStore = new HybridVectorStore(innerStore);
        var embedding = new MockEmbeddingModel();
        var chat = new MockChatModel("The answer is relevant.");
        var splitter = new FixedSizeTextSplitter(500, 0);
        var pipeline = new DefaultRagPipeline(hybridStore, embedding, chat, splitter);

        const string targetContent = "target document with specific information about WeaveLLM graphs";

        var docs = new[]
        {
            new Document("Alpha content about completely different stuff", "doc1"),
            new Document("Beta content on another unrelated topic here", "doc2"),
            new Document(targetContent, "doc3"),
            new Document("Gamma unrelated material that has nothing to do", "doc4"),
            new Document("Delta another separate document on other topics", "doc5"),
        };

        var indexResult = await pipeline.IndexAsync(docs);
        indexResult.IsSuccess.Should().BeTrue();

        // Querying with the exact target text produces a vector identical to doc3's chunk vector.
        var queryResult = await pipeline.QueryAsync(targetContent);

        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value!.Sources.Should().Contain("doc3");
    }
}
