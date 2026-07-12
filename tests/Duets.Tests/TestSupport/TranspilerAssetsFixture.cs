using System.Net.Http.Json;
using System.Text.Json;
using Duets.Jint;

namespace Duets.Tests.TestSupport;

[CollectionDefinition("TranspilerAssets")]
public sealed class TranspilerAssetsCollectionDefinition
    : ICollectionFixture<TranspilerAssetsFixture> { }

public sealed class TranspilerAssetsFixture : IAsyncLifetime
{
    private static readonly HttpClient _http = new();

    private static readonly string _cacheDir = Path.Combine(
        Path.GetTempPath(),
        "duets-test-assets"
    );

    public string BabelJs { get; private set; } = null!;
    public string BabelVersion { get; private set; } = null!;
    public string TypeScriptJs { get; private set; } = null!;
    public string TypeScriptVersion { get; private set; } = null!;
    private BabelTranspiler? _sharedBabelTranspiler;
    private TypeScriptService? _sharedTypeScriptService;

    public Task<BabelTranspiler> CreateBabelTranspilerAsync()
    {
        var babelJs = this.BabelJs;
        return BabelTranspiler.CreateAsync(
            new BabelTranspilerOptions
            {
                BabelJs = AssetSources.From(_ => Task.FromResult(babelJs)),
            }
        );
    }

    public Task<TypeScriptService> CreateTypeScriptServiceAsync(
        TypeDeclarations declarations,
        bool includeStdLib = false
    )
    {
        var tsJs = this.TypeScriptJs;
        var tsVersion = this.TypeScriptVersion;
        return TypeScriptService.CreateAsync(
            declarations,
            new TypeScriptServiceOptions
            {
                TypeScriptJs = AssetSources.From(_ => Task.FromResult(tsJs)),
                LibEs5Source = _ =>
                    AssetSources
                        .Unpkg("typescript", tsVersion, "lib/lib.es5.d.ts")
                        .WithDiskCache(
                            Path.Combine(_cacheDir, $"typescript-lib.es5-{tsVersion}.d.ts"),
                            TimeSpan.FromDays(30)
                        ),
            },
            includeStdLib
        );
    }

    public async Task<BabelTranspiler> GetSharedBabelTranspilerAsync()
    {
        this._sharedBabelTranspiler ??= await this.CreateBabelTranspilerAsync();
        return this._sharedBabelTranspiler;
    }

    public async Task<TypeScriptService> GetSharedTypeScriptServiceAsync()
    {
        this._sharedTypeScriptService ??= await this.CreateTypeScriptServiceAsync(
            new TypeDeclarations()
        );
        return this._sharedTypeScriptService;
    }

    public ValueTask DisposeAsync()
    {
        this._sharedBabelTranspiler?.Dispose();
        this._sharedTypeScriptService?.Dispose();
        return ValueTask.CompletedTask;
    }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_cacheDir);

        var babelTask = FetchLatestAsync("@babel/standalone", "babel.js");
        var tsTask = FetchLatestTypeScriptAsync();
        await Task.WhenAll(babelTask, tsTask);

        (this.BabelJs, this.BabelVersion) = await babelTask;
        (this.TypeScriptJs, this.TypeScriptVersion) = await tsTask;
    }

    /// <summary>
    /// Fetches the newest npm TypeScript compiler that the Jint-hosted service can still run.
    /// TypeScript 7+ is the native (Go) distribution and ships no <c>lib/typescript.js</c> on npm
    /// (ADR-19), so blindly following the <c>latest</c> dist-tag across that boundary produces a
    /// 404 from unpkg. When <c>latest</c> has moved to 7+, fall back to the same JS-based-line
    /// version that the production default in <c>TypeScriptServiceOptions</c> pins.
    /// </summary>
    private static async Task<(string Content, string Version)> FetchLatestTypeScriptAsync()
    {
        var version = await ResolveLatestVersionAsync("typescript");
        var dot = version.IndexOf('.');
        if (dot > 0 && int.TryParse(version[..dot], out var major) && major >= 7)
        {
            version = "6.0.2";
        }

        return await FetchAsync("typescript", version, "lib/typescript.js");
    }

    private static async Task<(string Content, string Version)> FetchLatestAsync(
        string package,
        string filePath
    )
    {
        var version = await ResolveLatestVersionAsync(package);
        return await FetchAsync(package, version, filePath);
    }

    private static async Task<(string Content, string Version)> FetchAsync(
        string package,
        string version,
        string filePath
    )
    {
        var sanitized = package.TrimStart('@').Replace('/', '-');
        var cacheFile = Path.Combine(_cacheDir, $"{sanitized}-{version}.js");

        var content = await AssetSources
            .Unpkg(package, version, filePath)
            .WithDiskCache(cacheFile, TimeSpan.FromDays(30))
            .GetStringAsync();

        return (content, version);
    }

    private static async Task<string> ResolveLatestVersionAsync(string package)
    {
        var encoded = Uri.EscapeDataString(package);
        var element = await _http.GetFromJsonAsync<JsonElement>(
            $"https://registry.npmjs.org/{encoded}/latest"
        );
        return element.GetProperty("version").GetString()!;
    }
}
