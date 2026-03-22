using System.IO.Compression;
using System.Text;

namespace HttpHarker.Tests;

public sealed class ZipFileProviderTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static MemoryStream BuildZip(IEnumerable<(string path, string content)> entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }

    // ---------------------------------------------------------------------------
    // Concurrency
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetFileContent_is_safe_under_concurrent_access()
    {
        var zip = BuildZip([("file.txt", "data")]);
        var provider = new ZipFileProvider(zip);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => provider.GetFileContent("file.txt")))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(
            results,
            b =>
            {
                Assert.NotNull(b);
                Assert.Equal("data", Encoding.UTF8.GetString(b));
            }
        );
    }

    [Fact]
    public void GetFileContent_lookup_is_case_insensitive()
    {
        var zip = BuildZip([("Index.HTML", "<h1>hi</h1>")]);
        var provider = new ZipFileProvider(zip);

        Assert.NotNull(provider.GetFileContent("index.html"));
        Assert.NotNull(provider.GetFileContent("INDEX.HTML"));
    }

    // ---------------------------------------------------------------------------
    // Path normalisation
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetFileContent_normalises_backslash_entry_names()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(@"sub\page.html");
            using var w = new StreamWriter(entry.Open());
            w.Write("<p>sub</p>");
        }

        ms.Position = 0;
        var provider = new ZipFileProvider(ms);

        var bytes = provider.GetFileContent("sub/page.html");

        Assert.NotNull(bytes);
        Assert.Equal("<p>sub</p>", Encoding.UTF8.GetString(bytes));
    }

    // ---------------------------------------------------------------------------
    // Basic retrieval
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetFileContent_returns_bytes_for_existing_file()
    {
        var zip = BuildZip([("hello.txt", "Hello!")]);
        var provider = new ZipFileProvider(zip);

        var bytes = provider.GetFileContent("hello.txt");

        Assert.NotNull(bytes);
        Assert.Equal("Hello!", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void GetFileContent_returns_bytes_for_nested_path()
    {
        var zip = BuildZip([("assets/app.js", "console.log('hi')")]);
        var provider = new ZipFileProvider(zip);

        var bytes = provider.GetFileContent("assets/app.js");

        Assert.NotNull(bytes);
        Assert.Equal("console.log('hi')", Encoding.UTF8.GetString(bytes));
    }

    // ---------------------------------------------------------------------------
    // Directory entries
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetFileContent_returns_null_for_directory_entry_with_trailing_slash()
    {
        var zip = BuildZip([("dir/", "")]);
        var provider = new ZipFileProvider(zip);

        Assert.Null(provider.GetFileContent("dir/"));
    }

    [Fact]
    public void GetFileContent_returns_null_for_missing_path()
    {
        var zip = BuildZip([("hello.txt", "Hello!")]);
        var provider = new ZipFileProvider(zip);

        Assert.Null(provider.GetFileContent("notexist.txt"));
    }

    // ---------------------------------------------------------------------------
    // Stream consumed at construction
    // ---------------------------------------------------------------------------

    [Fact]
    public void Provider_works_after_original_stream_is_disposed()
    {
        MemoryStream zip;
        ZipFileProvider provider;

        zip = BuildZip([("x.txt", "x")]);
        provider = new ZipFileProvider(zip);
        zip.Dispose();

        // Must still work because bytes were copied at construction time.
        Assert.NotNull(provider.GetFileContent("x.txt"));
    }
}
