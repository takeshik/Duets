using System.IO.Compression;
using System.Net;
using HttpHarker.Tests.TestSupport;

namespace HttpHarker.Tests;

public sealed class StaticFileMiddlewareTests
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

    private static Task RunAsync(
        Action<HttpServer> configure,
        Func<HttpClient, string, Task> test)
    {
        return ServerFixture.RunAsync(configure, test);
    }

    [Fact]
    public async Task Cache_control_header_absent_when_selector_returns_null()
    {
        var zip = BuildZip([("file.txt", "")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.CacheControlSelector = _ => null),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "file.txt");
                Assert.Null(resp.Headers.CacheControl);
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Cache-Control
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Cache_control_header_set_by_selector()
    {
        var zip = BuildZip([("app.js", "")]);

        await RunAsync(
            s => s.UseZipArchive(
                zip,
                "/",
                o =>
                    o.CacheControlSelector = suffix =>
                        suffix.EndsWith(".js") ? "public, max-age=3600" : null
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "app.js");
                var cc = resp.Headers.CacheControl?.ToString();
                Assert.Contains("max-age=3600", cc ?? "");
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Content-Type
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Content_type_resolved_from_file_extension()
    {
        var zip = BuildZip(
            [
                ("page.html", "<p/>"),
                ("style.css", "body{}"),
                ("app.js", ""),
            ]
        );

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var html = await client.GetAsync(prefix + "page.html");
                Assert.Contains("text/html", html.Content.Headers.ContentType?.MediaType ?? "");

                var css = await client.GetAsync(prefix + "style.css");
                Assert.Contains("text/css", css.Content.Headers.ContentType?.MediaType ?? "");

                var js = await client.GetAsync(prefix + "app.js");
                Assert.Contains("javascript", js.Content.Headers.ContentType?.ToString() ?? "");
            }
        );
    }

    [Fact]
    public async Task Content_type_resolved_from_real_file_extension_even_for_default_document()
    {
        // "/"  resolves to "index.html" → must report text/html, not octet-stream.
        var zip = BuildZip([("index.html", "<p/>")]);

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix);
                Assert.Contains("text/html", resp.Content.Headers.ContentType?.MediaType ?? "");
            }
        );
    }

    [Fact]
    public async Task Custom_default_document_is_served_for_root()
    {
        var zip = BuildZip([("home.html", "home")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.DefaultDocument = "home.html"),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("home", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    [Fact]
    public async Task ETag_header_absent_when_disabled()
    {
        var zip = BuildZip([("file.txt", "data")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableETag = false),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "file.txt");
                Assert.Null(resp.Headers.ETag);
            }
        );
    }

    // ---------------------------------------------------------------------------
    // ETag
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ETag_header_present_when_enabled()
    {
        var zip = BuildZip([("file.txt", "data")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableETag = true),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "file.txt");
                Assert.NotNull(resp.Headers.ETag);
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Explicit file paths
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Explicit_file_path_is_served()
    {
        var zip = BuildZip([("about.html", "<p>about</p>")]);

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "about.html");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("<p>about</p>", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Root prefix scoping
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Files_served_only_under_configured_root_prefix()
    {
        var zip = BuildZip([("file.txt", "content")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/static"),
            async (client, prefix) =>
            {
                // Under prefix: served
                var ok = await client.GetAsync(prefix + "static/file.txt");
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

                // Outside prefix: 404
                var notFound = await client.GetAsync(prefix + "file.txt");
                Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
            }
        );
    }

    // ---------------------------------------------------------------------------
    // HEAD request
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HEAD_returns_200_with_content_length_but_no_body()
    {
        var zip = BuildZip([("index.html", "<h1>hi</h1>")]);

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var resp = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, prefix + "index.html")
                );

                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.True((resp.Content.Headers.ContentLength ?? 0) > 0);
                Assert.Empty(await resp.Content.ReadAsByteArrayAsync());
            }
        );
    }

    [Fact]
    public async Task If_None_Match_matching_etag_returns_304()
    {
        var zip = BuildZip([("file.txt", "data")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableETag = true),
            async (client, prefix) =>
            {
                var first = await client.GetAsync(prefix + "file.txt");
                var etag = first.Headers.ETag!.Tag;

                var req = new HttpRequestMessage(HttpMethod.Get, prefix + "file.txt");
                req.Headers.TryAddWithoutValidation("If-None-Match", etag);
                var second = await client.SendAsync(req);

                Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
            }
        );
    }

    [Fact]
    public async Task If_None_Match_non_matching_etag_returns_200()
    {
        var zip = BuildZip([("file.txt", "data")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableETag = true),
            async (client, prefix) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, prefix + "file.txt");
                req.Headers.TryAddWithoutValidation("If-None-Match", "\"outdated\"");
                var resp = await client.SendAsync(req);

                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
        );
    }

    [Fact]
    public async Task If_None_Match_weak_etag_returns_304()
    {
        var zip = BuildZip([("file.txt", "data")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableETag = true),
            async (client, prefix) =>
            {
                var first = await client.GetAsync(prefix + "file.txt");
                var etag = first.Headers.ETag!.Tag;

                var req = new HttpRequestMessage(HttpMethod.Get, prefix + "file.txt");
                req.Headers.TryAddWithoutValidation("If-None-Match", $"W/{etag}");
                var resp = await client.SendAsync(req);

                Assert.Equal(HttpStatusCode.NotModified, resp.StatusCode);
            }
        );
    }

    [Fact]
    public async Task If_None_Match_wildcard_returns_304()
    {
        var zip = BuildZip([("file.txt", "data")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableETag = true),
            async (client, prefix) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, prefix + "file.txt");
                req.Headers.TryAddWithoutValidation("If-None-Match", "*");
                var resp = await client.SendAsync(req);

                Assert.Equal(HttpStatusCode.NotModified, resp.StatusCode);
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Missing file → passes to next middleware (404 from server default)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Missing_file_returns_404()
    {
        var zip = BuildZip([("index.html", "")]);

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "notexist.txt");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Nested_path_is_served()
    {
        var zip = BuildZip([("assets/app.js", "console.log('hi')")]);

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "assets/app.js");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("console.log('hi')", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Default document
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Root_path_serves_default_document()
    {
        var zip = BuildZip([("index.html", "<h1>root</h1>")]);

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("<h1>root</h1>", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    [Fact]
    public async Task Spa_fallback_disabled_by_default()
    {
        var zip = BuildZip([("index.html", "spa-root")]);

        await RunAsync(
            s => s.UseZipArchive(zip),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "some/route");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Spa_fallback_not_triggered_for_path_with_extension()
    {
        var zip = BuildZip([("index.html", "spa-root")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableSpaFallback = true),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "missing.css");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Spa_fallback_not_triggered_for_post_method()
    {
        var zip = BuildZip([("index.html", "spa-root")]);

        await RunAsync(
            s => s.UseZipArchive(zip, "/", o => o.EnableSpaFallback = true),
            async (client, prefix) =>
            {
                var resp = await client.PostAsync(prefix + "some/route", null);
                // POST should not trigger SPA fallback — passes to next → 404
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
        );
    }

    // ---------------------------------------------------------------------------
    // SPA fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Spa_fallback_serves_index_html_for_extensionless_path()
    {
        var zip = BuildZip([("index.html", "spa-root")]);

        await RunAsync(
            s => s.UseZipArchive(
                zip,
                "/",
                o =>
                {
                    o.EnableSpaFallback = true;
                    o.SpaFallbackDocument = "index.html";
                }
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "some/deep/route");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("spa-root", await resp.Content.ReadAsStringAsync());
            }
        );
    }
}
