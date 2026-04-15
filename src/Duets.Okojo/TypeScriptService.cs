using Okojo;
using Okojo.Objects;
using Okojo.Runtime;

namespace Duets.Okojo;

public record TypeScriptServiceOptions
{
    public IAssetSource TypeScriptJs { get; init; } =
        AssetSources.Unpkg("typescript", "6.0.2", "lib/typescript.js")
            .WithDiskCache(Path.Combine(Path.GetTempPath(), "typescript.js"));

    public Func<string, IAssetSource> LibEs5Source { get; init; } =
        tsVersion => AssetSources.Unpkg("typescript", tsVersion, "lib/lib.es5.d.ts")
            .WithDiskCache(Path.Combine(Path.GetTempPath(), $"typescript-lib.es5-{tsVersion}.d.ts"));
}

public sealed class TypeScriptService : ITranspiler,
    IDisposable
{
    private TypeScriptService(ITypeDeclarationProvider typeDeclarations, TypeScriptServiceOptions? options = null)
    {
        this._typeDeclarations = typeDeclarations;
        this._options = options ?? new TypeScriptServiceOptions();
        typeDeclarations.DeclarationChanged += this.OnDeclarationChanged;
    }

    private readonly ITypeDeclarationProvider _typeDeclarations;
    private readonly TypeScriptServiceOptions _options;
    private readonly object _sync = new();
    private JsRuntime? _runtime;
    private JsRealm? _realm;
    private JsObject? _host;
    private JsFunction? _addFile;

    public string? Version { get; private set; }

    public string Description => $"TypeScript {this.Version ?? "unknown"}";

    public static async Task<TypeScriptService> CreateAsync(ITypeDeclarationProvider typeDeclarations, TypeScriptServiceOptions? options = null, bool injectStdLib = false)
    {
        var service = new TypeScriptService(typeDeclarations, options);
        try
        {
            await service.ResetAsync();
            if (injectStdLib)
            {
                await service.InjectStdLibAsync();
            }

            return service;
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    public async Task ResetAsync(bool forceDownloadCodes = false)
    {
        var typeScriptJs = await this._options.TypeScriptJs.GetAsync(forceDownloadCodes);
        var languageServiceJs = await ScriptEngineResources.LoadLanguageServiceJsAsync();

        var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;

        realm.Execute(typeScriptJs);
        realm.Execute(languageServiceJs);

        var host = GetRequiredObject(realm, "$$host");
        var addFile = GetRequiredFunction(host, "addFile");

        lock (this._sync)
        {
            this._runtime?.Dispose();
            this._runtime = runtime;
            this._realm = realm;
            this._host = host;
            this._addFile = addFile;
            this.Version = realm.Evaluate("ts.version").AsString();
            this.ReplayDeclarations();
        }
    }

    public async Task InjectStdLibAsync(bool forceDownload = false)
    {
        string version;
        lock (this._sync)
        {
            version = this.Version ?? throw new InvalidOperationException("TypeScript version is not initialized.");
        }

        var content = await this._options.LibEs5Source(version).GetAsync(forceDownload);
        lock (this._sync)
        {
            var realm = this._realm ?? throw new InvalidOperationException("Call ResetAsync() first.");
            realm.Call(
                this._addFile!,
                JsValue.FromObject(this._host!),
                JsValue.FromString("lib.es5.d.ts"),
                JsValue.FromString(content)
            );
        }
    }

    public IReadOnlyList<CompletionEntry> GetCompletions(string source, int position, string fileName = "script.ts")
    {
        lock (this._sync)
        {
            var realm = this._realm ?? throw new InvalidOperationException("Call ResetAsync() first.");
            realm.Call(
                this._addFile!,
                JsValue.FromObject(this._host!),
                JsValue.FromString(fileName),
                JsValue.FromString(source)
            );

            var service = GetRequiredObject(realm, "$$service");
            var getCompletions = GetRequiredFunction(service, "getCompletionsAtPosition");
            var options = new JsUserDataObject(realm);
            var result = realm.Call(
                getCompletions,
                JsValue.FromObject(service),
                JsValue.FromString(fileName),
                JsValue.FromInt32(position),
                JsValue.FromObject(options)
            );

            return ParseCompletionEntries(result);
        }
    }

    public void Dispose()
    {
        this._typeDeclarations.DeclarationChanged -= this.OnDeclarationChanged;

        lock (this._sync)
        {
            this._runtime?.Dispose();
            this._runtime = null;
            this._realm = null;
            this._host = null;
            this._addFile = null;
            this.Version = null;
        }
    }

    public string Transpile(string input, string? fileName = null, IList<Diagnostic>? diagnostics = null, string? moduleName = null)
    {
        lock (this._sync)
        {
            var realm = this._realm ?? throw new InvalidOperationException("Call ResetAsync() first.");
            var ts = GetRequiredObject(realm, "ts");
            var transpile = GetRequiredFunction(ts, "transpile");
            var diagsArray = new JsArray(realm);
            var result = realm.Call(
                transpile,
                JsValue.FromObject(ts),
                JsValue.FromString(input),
                JsValue.Null,
                fileName is null ? JsValue.Null : JsValue.FromString(fileName),
                JsValue.FromObject(diagsArray),
                moduleName is null ? JsValue.Null : JsValue.FromString(moduleName)
            );

            if (diagnostics != null)
            {
                foreach (var x in ParseDiagnostics(diagsArray))
                {
                    diagnostics.Add(x);
                }
            }

            return result.AsString();
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

    private static IReadOnlyList<CompletionEntry> ParseCompletionEntries(JsValue completionResult)
    {
        if (completionResult.IsNullOrUndefined || !completionResult.TryGetObject(out var completionObject))
        {
            return [];
        }

        if (!completionObject.TryGetProperty("entries", out var entriesValue) ||
            entriesValue.IsNullOrUndefined ||
            !entriesValue.TryGetObject(out var entriesObject) ||
            entriesObject is not JsArray entriesArray)
        {
            return [];
        }

        var entries = new List<CompletionEntry>((int) entriesArray.Length);
        for (uint i = 0; i < entriesArray.Length; i++)
        {
            var entryValue = entriesArray[i];
            if (!entryValue.TryGetObject(out var entryObject))
            {
                continue;
            }

            var name = entryObject["name"];
            var kind = entryObject["kind"];
            var sortText = entryObject["sortText"];
            entries.Add(
                new CompletionEntry(
                    name.IsUndefined ? string.Empty : name.AsString(),
                    kind.IsUndefined ? string.Empty : kind.AsString(),
                    sortText.IsUndefined ? null : sortText.AsString()
                )
            );
        }

        return entries;
    }

    private static IReadOnlyList<Diagnostic> ParseDiagnostics(JsArray diagnosticsArray)
    {
        var diagnostics = new List<Diagnostic>((int) diagnosticsArray.Length);
        for (uint i = 0; i < diagnosticsArray.Length; i++)
        {
            var item = diagnosticsArray[i];
            if (!item.TryGetObject(out var diagnosticObject))
            {
                continue;
            }

            diagnostics.Add(
                new Diagnostic(
                    (int) diagnosticObject["start"].Float64Value,
                    (int) diagnosticObject["length"].Float64Value,
                    diagnosticObject["messageText"].AsString(),
                    (int) diagnosticObject["category"].Float64Value,
                    (int) diagnosticObject["code"].Float64Value
                )
            );
        }

        return diagnostics;
    }

    private void OnDeclarationChanged(TypeDeclaration declaration)
    {
        lock (this._sync)
        {
            this.AddDeclarationToLanguageService(declaration);
        }
    }

    private void ReplayDeclarations()
    {
        foreach (var declaration in this._typeDeclarations.GetDeclarations())
        {
            this.AddDeclarationToLanguageService(declaration);
        }
    }

    private void AddDeclarationToLanguageService(TypeDeclaration declaration)
    {
        var realm = this._realm;
        if (realm is null)
        {
            return;
        }

        realm.Call(
            this._addFile!,
            JsValue.FromObject(this._host!),
            JsValue.FromString(declaration.FileName),
            JsValue.FromString(declaration.Content)
        );
    }

    public sealed record CompletionEntry(string Name, string Kind, string? SortText);
}
