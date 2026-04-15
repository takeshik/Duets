using Okojo;
using Okojo.Objects;
using Okojo.Runtime;

namespace Duets.Okojo;

public record BabelTranspilerOptions
{
    public IAssetSource BabelJs { get; init; } =
        AssetSources.Unpkg("@babel/standalone", "7.29.2", "babel.js")
            .WithDiskCache(Path.Combine(Path.GetTempPath(), "babel-standalone.js"));
}

public sealed class BabelTranspiler : ITranspiler,
    IDisposable
{
    private BabelTranspiler(BabelTranspilerOptions? options = null)
    {
        this._options = options ?? new BabelTranspilerOptions();
    }

    private readonly BabelTranspilerOptions _options;
    private JsRuntime? _runtime;
    private JsRealm? _realm;

    public string? Version { get; private set; }

    public string Description => $"Babel {this.Version ?? string.Empty}".TrimEnd();

    public static async Task<BabelTranspiler> CreateAsync(BabelTranspilerOptions? options = null)
    {
        var transpiler = new BabelTranspiler(options);
        try
        {
            await transpiler.InitializeAsync();
            return transpiler;
        }
        catch
        {
            transpiler.Dispose();
            throw;
        }
    }

    public async Task InitializeAsync(bool forceDownload = false)
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;

        realm.Execute(
            """
            var process = { env: { NODE_ENV: 'production' } };
            var console = { log: function() {}, warn: function() {}, error: function() {}, info: function() {} };
            """
        );
        realm.Execute(await this._options.BabelJs.GetAsync(forceDownload));

        this._runtime?.Dispose();
        this._runtime = runtime;
        this._realm = realm;
        var babel = GetRequiredObject(realm, "Babel");
        var version = babel["version"];
        this.Version = version.IsUndefined ? null : version.AsString();
    }

    public void Dispose()
    {
        this._runtime?.Dispose();
    }

    public string Transpile(string input, string? fileName = null, IList<Diagnostic>? diagnostics = null, string? moduleName = null)
    {
        var realm = this._realm ?? throw new InvalidOperationException("Call InitializeAsync() first.");

        try
        {
            var babel = GetRequiredObject(realm, "Babel");
            var transform = GetRequiredFunction(babel, "transform");
            var presets = new JsArray(realm)
            {
                [0] = JsValue.FromString("typescript"),
            };
            var options = new JsUserDataObject(realm)
            {
                ["presets"] = JsValue.FromObject(presets),
                ["filename"] = JsValue.FromString(fileName ?? "input.ts"),
                ["retainLines"] = JsValue.True,
            };
            var result = realm.Call(
                transform,
                JsValue.FromObject(babel),
                JsValue.FromString(input),
                JsValue.FromObject(options)
            );

            if (!result.TryGetObject(out var resultObject))
            {
                throw new InvalidOperationException("Babel.transform did not return an object.");
            }

            var code = resultObject["code"];
            return code.IsUndefined ? string.Empty : code.AsString();
        }
        catch (Exception ex)
        {
            diagnostics?.Add(new Diagnostic(0, input.Length, ex.Message, 1, 0));
            throw;
        }
    }

    private static JsObject GetRequiredObject(JsRealm realm, string name)
    {
        if (realm.GlobalObject.TryGetProperty(name, out var value) && value.TryGetObject(out var obj))
        {
            return obj;
        }

        throw new InvalidOperationException($"Expected '{name}' to be an object.");
    }

    private static JsFunction GetRequiredFunction(JsObject source, string name)
    {
        if (source.TryGetProperty(name, out var value) && value.TryGetObject(out var obj) && obj is JsFunction function)
        {
            return function;
        }

        throw new InvalidOperationException($"Expected '{name}' to be a function.");
    }
}
