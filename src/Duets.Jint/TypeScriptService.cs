using Jint;
using Jint.Native;

namespace Duets.Jint;

/// <summary>
/// Configuration options for <see cref="TypeScriptService"/>, controlling how runtime JS assets are fetched.
/// </summary>
public record TypeScriptServiceOptions
{
    /// <summary>
    /// Asset source for the TypeScript compiler script (<c>typescript.js</c>).
    /// Defaults to fetching from unpkg with a 7-day disk cache in the system temp directory.
    /// </summary>
    public IAssetSource TypeScriptJs { get; init; } =
        AssetSources.Unpkg("typescript", "6.0.2", "lib/typescript.js")
            .WithDiskCache(Path.Combine(Path.GetTempPath(), "typescript.js"));

    /// <summary>
    /// Factory that returns an asset source for the TypeScript ES5 standard library declaration file.
    /// Receives the detected TypeScript version string (e.g. <c>"5.9.3"</c>) and returns an
    /// <see cref="IAssetSource"/> for the corresponding <c>lib.es5.d.ts</c>.
    /// Defaults to fetching from unpkg with a version-keyed disk cache.
    /// </summary>
    public Func<string, IAssetSource> LibEs5Source { get; init; } =
        tsVersion => AssetSources.Unpkg("typescript", tsVersion, "lib/lib.es5.d.ts")
            .WithDiskCache(Path.Combine(Path.GetTempPath(), $"typescript-lib.es5-{tsVersion}.d.ts"));
}

public class TypeScriptService : ITranspiler,
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
    private Engine? _engine;
    private JsValue? _ts;
    private JsValue? _tsTranspile;
    private JsValue? _host;

    public string? Version { get; private set; }

    /// <inheritdoc/>
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

        var newEngine = new Engine();
        try
        {
            await newEngine.ExecuteAsync(typeScriptJs);
            var ts = newEngine.GetValue("ts");
            var tsTranspile = ts.Get("transpile");
            var version = ts.Get("version").AsString();

            // Initialize language services
            await newEngine.ExecuteAsync(languageServiceJs);

            Engine? previousEngine;
            lock (this._sync)
            {
                previousEngine = this._engine;
                this._engine = newEngine;
                this._ts = ts;
                this._tsTranspile = tsTranspile;
                this._host = newEngine.GetValue("$$host");
                this.Version = version;
                this.ReplayDeclarations();
            }

            previousEngine?.Dispose();
        }
        catch
        {
            newEngine.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Injects the ES5 standard library into the language service so that JS built-in completions
    /// (Array, string, Math, Promise, etc.) are available alongside registered .NET types.
    /// Optional: the Monaco-based web REPL runs its own TypeScript language service client-side
    /// and does not require this. Call this when using <see cref="GetCompletions"/> directly.
    /// </summary>
    public async Task InjectStdLibAsync(bool forceDownload = false)
    {
        string version;
        lock (this._sync)
        {
            if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
            version = this.Version ?? throw new InvalidOperationException("TypeScript version is not initialized.");
        }

        var content = await this._options.LibEs5Source(version).GetAsync(forceDownload);

        lock (this._sync)
        {
            if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
            var host = this._host!;
            host.Get("addFile").Call(host, ["lib.es5.d.ts", content]);
        }
    }

    /// <summary>
    /// Returns completion candidates at the specified position in a TypeScript source.
    /// Registered .NET type information is also included as completion targets.
    /// </summary>
    public IReadOnlyList<CompletionEntry> GetCompletions(
        string source,
        int position,
        string fileName = "script.ts")
    {
        lock (this._sync)
        {
            if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");

            var host = this._host!;
            // Add user code as a virtual file
            host.Get("addFile").Call(host, [fileName, source]);

            var service = this._engine.GetValue("$$service");
            var completions = service.Get("getCompletionsAtPosition")
                .Call(service, [fileName, position, new JsObject(this._engine)]);

            if (completions.Equals(JsValue.Null) || completions.Equals(JsValue.Undefined))
            {
                return [];
            }

            var entries = completions.Get("entries");
            if (entries.Equals(JsValue.Null) || entries.Equals(JsValue.Undefined))
            {
                return [];
            }

            return ((JsArray) entries)
                .Select(v => new CompletionEntry(
                        v.Get("name").AsString(),
                        v.Get("kind").AsString(),
                        v.Get("sortText").Equals(JsValue.Undefined) ? null : v.Get("sortText").AsString()
                    )
                )
                .ToList();
        }
    }

    public void Dispose()
    {
        this._typeDeclarations.DeclarationChanged -= this.OnDeclarationChanged;

        lock (this._sync)
        {
            this._engine?.Dispose();
            this._engine = null;
            this._ts = null;
            this._tsTranspile = null;
            this._host = null;
            this.Version = null;
        }
    }

    public string Transpile(
        string input,
        string? fileName = null,
        IList<Diagnostic>? diagnostics = null,
        string? moduleName = null)
    {
        lock (this._sync)
        {
            if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
            var diagsArray = new JsArray(this._engine);
            var ret = ((JsString) this._engine.Call(
                        this._tsTranspile!,
                        this._ts!,
                        [
                            input,
                            JsValue.Null,
                            fileName,
                            diagnostics == null
                                ? JsValue.Null
                                : diagsArray,
                            moduleName,
                        ]
                    )
                ).ToString();

            if (diagnostics != null)
            {
                foreach (var x in diagsArray.Select(v => new Diagnostic(
                            (int) v.Get("start").AsNumber(),
                            (int) v.Get("length").AsNumber(),
                            v.Get("messageText").AsString(),
                            (int) v.Get("category").AsNumber(),
                            (int) v.Get("code").AsNumber()
                        )
                    ))
                {
                    diagnostics.Add(x);
                }
            }

            return ret;
        }
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
        if (this._engine == null) return;

        var host = this._host!;
        host.Get("addFile").Call(host, [declaration.FileName, declaration.Content]);
    }

    public record CompletionEntry(string Name, string Kind, string? SortText);
}
