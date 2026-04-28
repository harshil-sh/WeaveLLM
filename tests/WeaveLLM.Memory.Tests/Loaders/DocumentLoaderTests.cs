using FluentAssertions;
using WeaveLLM.Core.Models;
using WeaveLLM.Memory.Loaders;
using Xunit;

namespace WeaveLLM.Memory.Tests.Loaders;

public sealed class DocumentLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public DocumentLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ─── PlainTextLoader ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PlainTextLoader_LoadAsync_ReturnsSingleDocument()
    {
        var path = WriteTempFile("hello.txt", "Hello, world!");
        var loader = new PlainTextLoader();

        var docs = await CollectAsync(loader.LoadAsync(path));

        docs.Should().HaveCount(1);
        docs[0].Content.Should().Be("Hello, world!");
        docs[0].Source.Should().Be(path);
        docs[0].Title.Should().Be("hello");
    }

    [Fact]
    public void PlainTextLoader_CanLoad_ReturnsTrueForTxtFile()
    {
        var path = WriteTempFile("sample.txt", "x");
        var loader = new PlainTextLoader();

        loader.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void PlainTextLoader_CanLoad_ReturnsFalseForMdFile()
    {
        var path = WriteTempFile("sample.md", "x");
        var loader = new PlainTextLoader();

        loader.CanLoad(path).Should().BeFalse();
    }

    // ─── MarkdownLoader ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkdownLoader_LoadAsync_StripsBoldAndHeadings()
    {
        var content = "# My Title\n\n## Section\n\n**bold** text here.";
        var path = WriteTempFile("doc.md", content);
        var loader = new MarkdownLoader();

        var docs = await CollectAsync(loader.LoadAsync(path));

        docs.Should().HaveCount(1);
        docs[0].Content.Should().NotContain("##");
        docs[0].Content.Should().NotContain("**");
        docs[0].Content.Should().Contain("bold text here.");
        docs[0].Title.Should().Be("My Title");
    }

    [Fact]
    public async Task MarkdownLoader_LoadAsync_SetsFormatMetadata()
    {
        var path = WriteTempFile("readme.md", "# Title\nsome text");
        var loader = new MarkdownLoader();

        var docs = await CollectAsync(loader.LoadAsync(path));

        docs[0].Metadata.Should().ContainKey("format");
        docs[0].Metadata["format"].Should().Be("markdown");
    }

    [Fact]
    public async Task MarkdownLoader_LoadAsync_ExtractsTitleFromFirstH1()
    {
        var path = WriteTempFile("article.md", "# Extracted Title\n\n## Subtitle\n\nBody.");
        var loader = new MarkdownLoader();

        var docs = await CollectAsync(loader.LoadAsync(path));

        docs[0].Title.Should().Be("Extracted Title");
    }

    [Fact]
    public void MarkdownLoader_CanLoad_ReturnsTrueForMdFile()
    {
        var path = WriteTempFile("readme.md", "# Hi");
        var loader = new MarkdownLoader();

        loader.CanLoad(path).Should().BeTrue();
    }

    // ─── DirectoryLoader ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DirectoryLoader_LoadAsync_LoadsAllMatchedFiles()
    {
        WriteTempFile("a.txt", "text content");
        WriteTempFile("b.md", "# MD\n\nmd content");
        WriteTempFile("c.json", "{}"); // no loader — should be skipped

        var loader = new DirectoryLoader([new PlainTextLoader(), new MarkdownLoader()]);

        var docs = await CollectAsync(loader.LoadAsync(_tempDir));

        docs.Should().HaveCount(2);
        docs.Select(d => Path.GetExtension(d.Source))
            .Should().BeEquivalentTo([".txt", ".md"]);
    }

    [Fact]
    public async Task DirectoryLoader_LoadAsync_SilentlySkipsUnmatchedFiles()
    {
        WriteTempFile("data.csv", "a,b,c");
        var loader = new DirectoryLoader([new PlainTextLoader()]);

        var docs = await CollectAsync(loader.LoadAsync(_tempDir));

        docs.Should().BeEmpty();
    }

    [Fact]
    public void DirectoryLoader_CanLoad_ReturnsTrueForExistingDirectory()
    {
        var loader = new DirectoryLoader([]);
        loader.CanLoad(_tempDir).Should().BeTrue();
    }

    [Fact]
    public void DirectoryLoader_CanLoad_ReturnsFalseForFile()
    {
        var path = WriteTempFile("x.txt", "x");
        var loader = new DirectoryLoader([]);
        loader.CanLoad(path).Should().BeFalse();
    }

    // ─── DocumentLoaderRegistry ──────────────────────────────────────────────────

    [Fact]
    public async Task DocumentLoaderRegistry_LoadAsync_SelectsCorrectLoader()
    {
        var path = WriteTempFile("doc.txt", "registry test");
        var registry = new DocumentLoaderRegistry();
        registry.Register(new MarkdownLoader());
        registry.Register(new PlainTextLoader());

        var result = await registry.LoadAsync(path);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value![0].Content.Should().Be("registry test");
    }

    [Fact]
    public async Task DocumentLoaderRegistry_LoadAsync_ReturnsNoLoaderFoundForUnmatchedSource()
    {
        var path = WriteTempFile("data.xlsx", "dummy");
        var registry = new DocumentLoaderRegistry();
        registry.Register(new PlainTextLoader());

        var result = await registry.LoadAsync(path);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("NoLoaderFound");
    }

    [Fact]
    public async Task DocumentLoaderRegistry_LoadAsync_ReturnsNoLoaderFoundWhenEmpty()
    {
        var registry = new DocumentLoaderRegistry();

        var result = await registry.LoadAsync("anything.pdf");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("NoLoaderFound");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private string WriteTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<List<WeaveLLM.Core.RAG.Document>> CollectAsync(
        IAsyncEnumerable<WeaveLLM.Core.RAG.Document> source)
    {
        var list = new List<WeaveLLM.Core.RAG.Document>();
        await foreach (var doc in source) list.Add(doc);
        return list;
    }
}
