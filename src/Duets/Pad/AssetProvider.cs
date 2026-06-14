using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using HttpHarker;

namespace Duets.Pad;

/// <summary>
/// Owns the static-asset caches and CDN/default source construction for DuetsPad.
/// Handles the 6 asset route bodies and the Tabler Icons CSS rewrite.
/// <see cref="DuetsPadService"/> maps routes to this provider; the provider has no
/// shared mutable state with the service.
/// </summary>
internal sealed class AssetProvider
{
    private const string TablerIconsPackageVersion = "3.44.0";
    private const string TablerCorePackageVersion = "1.4.0";

    private readonly DuetsPadServiceOptions _options;
    private readonly Lazy<Task<string>> _monaco;
    private readonly Lazy<Task<string>> _tabler;
    private readonly Lazy<Task<string>> _tablerIconsCss;
    private readonly Lazy<Task<byte[]>> _tablerIconsFont;

    internal AssetProvider(DuetsPadServiceOptions options)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));

        var monacoSource =
            options.MonacoLoader
            ?? AssetSources
                .Unpkg("monaco-editor", "0.55.1", "min/vs/loader.js")
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-monaco-loader.js"));
        this._monaco = new Lazy<Task<string>>(() => monacoSource.GetStringAsync());

        var tablerSource =
            options.TablerCss
            ?? AssetSources
                .Unpkg("@tabler/core", TablerCorePackageVersion, "dist/css/tabler.min.css")
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-tabler.css"));
        this._tabler = new Lazy<Task<string>>(() => tablerSource.GetStringAsync());

        var tablerIconsCssSource =
            options.TablerIconsCss
            ?? AssetSources
                .Unpkg(
                    "@tabler/icons-webfont",
                    TablerIconsPackageVersion,
                    "dist/tabler-icons.min.css"
                )
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-tabler-icons.css"));
        this._tablerIconsCss = new Lazy<Task<string>>(async () =>
            RewriteTablerIconsCss(await tablerIconsCssSource.GetStringAsync().ConfigureAwait(false))
        );

        var tablerIconsFontSource =
            options.TablerIconsFont
            ?? AssetSources
                .Unpkg(
                    "@tabler/icons-webfont",
                    TablerIconsPackageVersion,
                    "dist/fonts/tabler-icons.woff2"
                )
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-tabler-icons.woff2"));
        this._tablerIconsFont = new Lazy<Task<byte[]>>(() => tablerIconsFontSource.GetBytesAsync());
    }

    internal async Task HandleIndexAsync(HttpActionContext ctx)
    {
        using var stream = typeof(DuetsPadService).Assembly.GetManifestResourceStream(
            "Duets.Resources.DuetsPadStaticFiles.index.html"
        );
        if (stream is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();
        await ctx.CloseAsync("text/html; charset=utf-8", html);
    }

    internal async Task HandleDuetsPadConfigJsAsync(HttpActionContext ctx)
    {
        var url = JsonValue.Create(this._options.MonacoBaseUrl).ToJsonString();
        var js = $"window.DUETSPAD_MONACO_VS = {url};";
        await ctx.CloseAsync("text/javascript; charset=utf-8", js);
    }

    internal async Task HandleDuetsPadJsAsync(HttpActionContext ctx)
    {
        using var stream = typeof(DuetsPadService).Assembly.GetManifestResourceStream(
            "Duets.Resources.DuetsPadStaticFiles.duetspad.js"
        );
        if (stream is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var js = await reader.ReadToEndAsync();
        await ctx.CloseAsync("text/javascript; charset=utf-8", js);
    }

    internal async Task HandleMonacoLoaderAsync(HttpActionContext ctx)
    {
        var content = await this._monaco.Value;
        await ctx.CloseAsync("text/javascript", content);
    }

    internal async Task HandleTablerCssAsync(HttpActionContext ctx)
    {
        var content = await this._tabler.Value;
        await ctx.CloseAsync("text/css; charset=utf-8", content);
    }

    internal async Task HandleTablerIconsCssAsync(HttpActionContext ctx)
    {
        var content = await this._tablerIconsCss.Value;
        await ctx.CloseAsync("text/css; charset=utf-8", content);
    }

    internal async Task HandleTablerIconsFontAsync(HttpActionContext ctx)
    {
        var bytes = await this._tablerIconsFont.Value;
        await ctx.CloseAsync(
            new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("font/woff2") },
            }
        );
    }

    /// <summary>
    /// Rewrites each <c>@font-face</c> <c>src:</c> declaration in the Tabler Icons CSS so it
    /// references only the local woff2 route, dropping the upstream woff/ttf fallback entries
    /// that have no route in DuetsPad (ADR-33).
    /// </summary>
    internal static string RewriteTablerIconsCss(string css)
    {
        // DuetsPad serves only tabler-icons.woff2 (ADR-33). Replace each @font-face src list with a
        // single local woff2 reference, dropping the upstream woff/ttf fallbacks that have no route.
        // (In the Tabler Icons stylesheet, "src:" appears only inside @font-face, and is the last
        // declaration before the block's closing brace.)
        const string canonicalSrc = "src:url(\"tabler-icons.woff2\") format(\"woff2\")";
        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (true)
        {
            var srcIdx = css.IndexOf("src:", i, StringComparison.Ordinal);
            if (srcIdx < 0)
            {
                sb.Append(css, i, css.Length - i);
                break;
            }

            var brace = css.IndexOf('}', srcIdx);
            if (brace < 0)
            {
                sb.Append(css, i, css.Length - i);
                break;
            }

            sb.Append(css, i, srcIdx - i); // text before "src:"
            sb.Append(canonicalSrc); // canonical single-source replacement
            i = brace; // resume at the '}' (kept)
        }

        return sb.ToString();
    }
}
