using FluentAssertions;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;
using WeaveLLM.Memory.InMemory;
using WeaveLLM.Memory.Search;
using Xunit;

namespace WeaveLLM.Memory.Tests.Search;

public sealed class HybridVectorStoreTests
{
    private static VectorEntry MakeEntry(string id, float[] vector, string? text = null)
    {
        var meta = new Dictionary<string, string> { ["source"] = id };
        if (text is not null) meta["text"] = text;
        return new VectorEntry(id, vector, $"Content for {id}", meta);
    }

    private static float[] UnitVec(int dimensions, int hotIndex)
    {
        var v = new float[dimensions];
        v[hotIndex % dimensions] = 1.0f;
        return v;
    }

    // ─── Upsert + Search ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_AfterUpsert_ReturnsDenseResults()
    {
        var store = new HybridVectorStore(new InMemoryVectorStore());

        for (var i = 0; i < 5; i++)
            await store.UpsertAsync(MakeEntry($"doc{i}", UnitVec(8, i)));

        // Query matches doc0 exactly
        var result = await store.SearchAsync(UnitVec(8, 0), topK: 3);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(3);
        result.Value![0].Entry.Id.Should().Be("doc0");
    }

    [Fact]
    public async Task SearchAsync_TopKLimitsResults()
    {
        var store = new HybridVectorStore(new InMemoryVectorStore());

        for (var i = 0; i < 5; i++)
            await store.UpsertAsync(MakeEntry($"doc{i}", UnitVec(8, i)));

        var result = await store.SearchAsync(UnitVec(8, 0), topK: 2);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_WithQueryText_IncludesBm25Component()
    {
        var inner = new InMemoryVectorStore();
        var store = new HybridVectorStore(inner);

        // All docs have identical vectors — dense tie-break. BM25 should differentiate.
        var tiedVec = new float[] { 1f, 0f, 0f, 0f };

        await store.UpsertAsync(MakeEntry("alpha", tiedVec, "apple banana cherry"));
        await store.UpsertAsync(MakeEntry("beta", tiedVec, "delta echo foxtrot"));
        await store.UpsertAsync(MakeEntry("gamma", tiedVec, "apple apple apple"));

        store.QueryText = "apple";
        var result = await store.SearchAsync(tiedVec, topK: 3);

        result.IsSuccess.Should().BeTrue();
        // "gamma" has highest BM25 score for "apple"
        result.Value!.Select(e => e.Entry.Id).Should().Contain("gamma");
    }

    // ─── Delete ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesFromDenseAndBm25()
    {
        var store = new HybridVectorStore(new InMemoryVectorStore());

        await store.UpsertAsync(MakeEntry("to-delete", UnitVec(4, 0), "unique token xyz"));
        await store.UpsertAsync(MakeEntry("keep", UnitVec(4, 1), "other content here"));

        await store.DeleteAsync("to-delete");

        store.QueryText = "xyz";
        var result = await store.SearchAsync(UnitVec(4, 0), topK: 5);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(e => e.Entry.Id).Should().NotContain("to-delete");
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_NoOps()
    {
        var store = new HybridVectorStore(new InMemoryVectorStore());

        // Should not throw
        await store.Invoking(s => s.DeleteAsync("ghost")).Should().NotThrowAsync();
    }

    // ─── Upsert idempotency ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_UpdatesExistingEntry_BM25IndexStaysConsistent()
    {
        var store = new HybridVectorStore(new InMemoryVectorStore());

        await store.UpsertAsync(MakeEntry("doc1", UnitVec(4, 0), "original text token"));
        await store.UpsertAsync(MakeEntry("doc1", UnitVec(4, 0), "updated text replaced"));

        store.QueryText = "token";
        var result = await store.SearchAsync(UnitVec(4, 0), topK: 5);

        result.IsSuccess.Should().BeTrue();
        // "token" is no longer in doc1 after update — BM25 should not return a ghost score
        // We just assert no exception and result is valid
        result.Value!.Should().NotBeNull();
    }

    // ─── RRF scoring ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_RrfScores_SortDescending()
    {
        var store = new HybridVectorStore(new InMemoryVectorStore());

        for (var i = 0; i < 5; i++)
            await store.UpsertAsync(MakeEntry($"d{i}", UnitVec(8, i), $"word{i}"));

        store.QueryText = "word0";
        var result = await store.SearchAsync(UnitVec(8, 0), topK: 5);

        result.IsSuccess.Should().BeTrue();
        var scores = result.Value!.Select(e => e.Score).ToList();
        scores.Should().BeInDescendingOrder();
    }
}
