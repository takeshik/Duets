using System.Reflection;
using System.Text;

namespace HttpHarker.Tests;

/// <summary>
/// Tests for <see cref="EmbeddedResourceFileProvider"/> using resources embedded in this test assembly.
/// Resources are under TestResources/ and have the logical prefix "HttpHarker.Tests.TestResources".
/// </summary>
public sealed class EmbeddedResourceFileProviderTests
{
    private static readonly Assembly ThisAssembly = typeof(EmbeddedResourceFileProviderTests).Assembly;
    private const string Prefix = "HttpHarker.Tests.TestResources";

    [Fact]
    public void Constructor_trims_trailing_dot_from_prefix()
    {
        // Prefix with trailing dot must behave identically to one without.
        var provider = new EmbeddedResourceFileProvider(ThisAssembly, Prefix + ".");

        Assert.NotNull(provider.GetFileContent("hello.txt"));
    }

    [Fact]
    public void GetFileContent_returns_bytes_for_existing_resource()
    {
        var provider = new EmbeddedResourceFileProvider(ThisAssembly, Prefix);

        var bytes = provider.GetFileContent("hello.txt");

        Assert.NotNull(bytes);
        Assert.Equal("Hello, World!", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void GetFileContent_returns_bytes_for_nested_resource()
    {
        var provider = new EmbeddedResourceFileProvider(ThisAssembly, Prefix);

        var bytes = provider.GetFileContent("sub/page.html");

        Assert.NotNull(bytes);
        Assert.Equal("<p>sub page</p>\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void GetFileContent_returns_null_for_missing_resource()
    {
        var provider = new EmbeddedResourceFileProvider(ThisAssembly, Prefix);

        Assert.Null(provider.GetFileContent("notexist.txt"));
    }
}
