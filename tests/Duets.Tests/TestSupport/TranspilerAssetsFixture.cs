using System.Net.Http.Json;
using System.Text.Json;
using Duets.Jint;

namespace Duets.Tests.TestSupport;

[CollectionDefinition("TranspilerAssets")]
public sealed class TranspilerAssetsCollectionDefinition : ICollectionFixture<TranspilerAssetsFixture>
{
}

public sealed class TranspilerAssetsFixture : IAsyncLifetime
{
    private static readonly HttpClient _http = new();

    private static readonly string _cacheDir =
        Path.Combine(Path.GetTempPath(), "duets-test-assets");

    public string BabelJs { get; private set; } = null!;
    public string BabelVersion { get; private set; } = null!;
    public string TypeScriptJs { get; private set; } = null!;
    public string TypeScriptVersion { get; private set; } = null!;

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
        bool includeStdLib = false)
    {
        var tsJs = this.TypeScriptJs;
        var tsVersion = this.TypeScriptVersion;
        return TypeScriptService.CreateAsync(
            declarations,
            new TypeScriptServiceOptions
            {
                TypeScriptJs = AssetSources.From(_ => Task.FromResult(tsJs)),
                LibEs5Source = _ => AssetSources
                    .Unpkg("typescript", tsVersion, "lib/lib.es5.d.ts")
                    .WithDiskCache(
                        Path.Combine(_cacheDir, $"typescript-lib.es5-{tsVersion}.d.ts"),
                        TimeSpan.FromDays(30)
                    ),
            },
            includeStdLib
        );
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_cacheDir);

        var babelTask = FetchLatestAsync("@babel/standalone", "babel.js");
        var tsTask = FetchLatestAsync("typescript", "lib/typescript.js");
        await Task.WhenAll(babelTask, tsTask);

        (this.BabelJs, this.BabelVersion) = await babelTask;
        (this.TypeScriptJs, this.TypeScriptVersion) = await tsTask;
    }

    private static async Task<(string Content, string Version)> FetchLatestAsync(
        string package, string filePath)
    {
        var version = await ResolveLatestVersionAsync(package);
        var sanitized = package.TrimStart('@').Replace('/', '-');
        var cacheFile = Path.Combine(_cacheDir, $"{sanitized}-{version}.js");

        var content = await AssetSources
            .Unpkg(package, version, filePath)
            .WithDiskCache(cacheFile, TimeSpan.FromDays(30))
            .GetAsync();

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
