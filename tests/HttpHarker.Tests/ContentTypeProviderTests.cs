namespace HttpHarker.Tests;

public sealed class ContentTypeProviderTests
{
    // ---------------------------------------------------------------------------
    // CreateDefault — known extensions
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(".html", "text/html; charset=utf-8")]
    [InlineData(".css", "text/css; charset=utf-8")]
    [InlineData(".js", "application/javascript; charset=utf-8")]
    [InlineData(".mjs", "application/javascript; charset=utf-8")]
    [InlineData(".json", "application/json; charset=utf-8")]
    [InlineData(".txt", "text/plain; charset=utf-8")]
    [InlineData(".svg", "image/svg+xml")]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".ico", "image/x-icon")]
    [InlineData(".woff", "font/woff")]
    [InlineData(".woff2", "font/woff2")]
    [InlineData(".ttf", "font/ttf")]
    [InlineData(".map", "application/json; charset=utf-8")]
    public void CreateDefault_resolves_known_extension(string extension, string expected)
    {
        var provider = ContentTypeProvider.CreateDefault();

        var result = provider.Resolve(extension, null!);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void AddRange_registers_multiple_mappings()
    {
        var provider = ContentTypeProvider.CreateDefault();
        provider.AddRange(
            new Dictionary<string, string>
            {
                [".foo"] = "application/foo",
                [".bar"] = "application/bar",
            }
        );

        Assert.Equal("application/foo", provider.Resolve(".foo", null!));
        Assert.Equal("application/bar", provider.Resolve(".bar", null!));
    }

    // ---------------------------------------------------------------------------
    // Add / AddRange
    // ---------------------------------------------------------------------------

    [Fact]
    public void Add_overrides_existing_mapping()
    {
        var provider = ContentTypeProvider.CreateDefault();
        provider.Add(".html", "text/html");

        var result = provider.Resolve(".html", null!);

        Assert.Equal("text/html", result);
    }

    [Fact]
    public void Add_registers_new_mapping()
    {
        var provider = ContentTypeProvider.CreateDefault();
        provider.Add(".custom", "application/x-custom");

        var result = provider.Resolve(".custom", null!);

        Assert.Equal("application/x-custom", result);
    }

    [Fact]
    public void CreateDefault_falls_back_to_octet_stream_for_empty_key()
    {
        var provider = ContentTypeProvider.CreateDefault();

        var result = provider.Resolve("", null!);

        Assert.Equal("application/octet-stream", result);
    }

    [Fact]
    public void CreateDefault_falls_back_to_octet_stream_for_null_key()
    {
        var provider = ContentTypeProvider.CreateDefault();

        var result = provider.Resolve(null, null!);

        Assert.Equal("application/octet-stream", result);
    }

    [Fact]
    public void CreateDefault_falls_back_to_octet_stream_for_unknown_extension()
    {
        var provider = ContentTypeProvider.CreateDefault();

        var result = provider.Resolve(".xyz", null!);

        Assert.Equal("application/octet-stream", result);
    }

    // ---------------------------------------------------------------------------
    // Custom fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public void Custom_fallback_is_invoked_for_unknown_key()
    {
        var provider = new ContentTypeProvider(
            _ => ".unknown",
            _ => "text/plain"
        );

        var result = provider.Resolve(".unknown", null!);

        Assert.Equal("text/plain", result);
    }

    // ---------------------------------------------------------------------------
    // Key lookup is case-insensitive
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_key_lookup_is_case_insensitive()
    {
        var provider = ContentTypeProvider.CreateDefault();

        Assert.Equal(provider.Resolve(".HTML", null!), provider.Resolve(".html", null!));
    }
}
