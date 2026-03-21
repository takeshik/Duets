using System.Security.Cryptography;
using System.Text;
using Jint;
using Jint.Native;
using Mio;

namespace Duets;

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
        AssetSources.Unpkg("typescript", "5.9.3", "lib/typescript.js")
            .WithDiskCache(DirectoryPath.GetTempDirectory().ChildFile("typescript.js"));

    /// <summary>
    /// Factory that returns an asset source for the TypeScript ES5 standard library declaration file.
    /// Receives the detected TypeScript version string (e.g. <c>"5.9.3"</c>) and returns an
    /// <see cref="IAssetSource"/> for the corresponding <c>lib.es5.d.ts</c>.
    /// Defaults to fetching from unpkg with a version-keyed disk cache.
    /// </summary>
    public Func<string, IAssetSource> LibEs5Source { get; init; } =
        tsVersion => AssetSources.Unpkg("typescript", tsVersion, "lib/lib.es5.d.ts")
            .WithDiskCache(
                DirectoryPath.GetTempDirectory()
                    .ChildFile($"typescript-lib.es5-{tsVersion}.d.ts")
            );
}

public class TypeScriptService : ITranspiler,
    IDisposable
{
    public TypeScriptService(TypeScriptServiceOptions? options = null)
    {
        this._options = options ?? new TypeScriptServiceOptions();
    }

    private readonly TypeScriptServiceOptions _options;
    private readonly object _sync = new();
    private readonly HashSet<Type> _registeredTypes = [];

    // Tracks namespace skeletons that still carry the $name dummy member (namespace → file name).
    // Entries are removed when a real type from that namespace is registered.
    private readonly Dictionary<string, string> _pendingSkeletonNamespaces = new();

    // Tracks namespaces that already have at least one real type registered.
    private readonly HashSet<string> _coveredNamespaces = [];
    private readonly ClrDeclarationGenerator _declarationGenerator = new();
    private readonly Dictionary<string, TypeDeclaration> _typeDeclarations = new();
    private Engine? _engine;
    private JsValue? _ts;
    private JsValue? _tsTranspile;

    public string? Version { get; private set; }

    /// <inheritdoc/>
    public string Description => $"TypeScript {this.Version ?? "unknown"}";

    /// <summary>
    /// Fires when a type declaration is added or updated. Used for SSE delivery and similar purposes.
    /// </summary>
    public event Action<TypeDeclaration>? TypeDeclarationAdded;

    public async Task ResetAsync(bool forceDownloadCodes = false)
    {
        var typeScriptJs = await this._options.TypeScriptJs.GetAsync(forceDownloadCodes);
        await using var stream = typeof(TypeScriptService).Assembly.GetManifestResourceStream("Duets.Resources.ReplStaticFiles.language-service.js")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var languageServiceJs = await reader.ReadToEndAsync();

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
                this._registeredTypes.Clear();
                this._pendingSkeletonNamespaces.Clear();
                this._coveredNamespaces.Clear();
                this._typeDeclarations.Clear();
                previousEngine = this._engine;
                this._engine = newEngine;
                this._ts = ts;
                this._tsTranspile = tsTranspile;
                this.Version = version;
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
    /// Registers a .NET type as a completion target. Used to generate type declarations (.d.ts).
    /// Duplicate registrations of the same type are ignored.
    /// </summary>
    public void RegisterType(Type type)
    {
        List<TypeDeclaration> notifications = [];
        lock (this._sync)
        {
            if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
            if (!this._registeredTypes.Add(type)) return;

            // Register base type first so it's declared before this type references it
            var baseType = type.BaseType;
            if (baseType != null && baseType != typeof(object) && baseType != typeof(ValueType)) this.RegisterType(baseType);

            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(type.ToString())));
            var fileName = $"clr-{hash}.d.ts";
            var content = this._declarationGenerator.GenerateTypeDefTs(type);
            var decl = new TypeDeclaration(fileName, content);
            var host = this._engine.GetValue("$$host");
            host.Get("addFile").Call(host, [fileName, content]);
            this._typeDeclarations[fileName] = decl;
            notifications.Add(decl);

            // If a skeleton with a $name dummy exists for this namespace, clear it now that a real type is present.
            if (type.Namespace != null && this._pendingSkeletonNamespaces.TryGetValue(type.Namespace, out var skeletonFile))
            {
                var emptyContent = $"declare namespace {type.Namespace} {{ }}\n";
                host.Get("addFile").Call(host, [skeletonFile, emptyContent]);
                var updatedSkeleton = new TypeDeclaration(skeletonFile, emptyContent);
                this._typeDeclarations[skeletonFile] = updatedSkeleton;
                this._pendingSkeletonNamespaces.Remove(type.Namespace);
                this._coveredNamespaces.Add(type.Namespace);
                notifications.Add(updatedSkeleton);
            }
            else if (type.Namespace != null)
            {
                this._coveredNamespaces.Add(type.Namespace);
            }
        }

        foreach (var decl in notifications)
        {
            this.TypeDeclarationAdded?.Invoke(decl);
        }
    }

    /// <summary>
    /// Registers a namespace skeleton declaration so that the namespace appears in TypeScript completions
    /// without registering any type members. A <c>$name</c> dummy member is included to ensure the namespace
    /// is visible in completions; it is automatically removed when a real type from the namespace is registered.
    /// Duplicate registrations and namespaces that already have real types are ignored.
    /// </summary>
    public void RegisterNamespaceSkeleton(string namespaceName)
    {
        TypeDeclaration? notification = null;
        lock (this._sync)
        {
            if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
            if (this._coveredNamespaces.Contains(namespaceName)) return;
            if (this._pendingSkeletonNamespaces.ContainsKey(namespaceName)) return;

            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"ns:{namespaceName}")));
            var fileName = $"clr-ns-{hash}.d.ts";
            var content = $"declare namespace {namespaceName} {{ const $name: '{namespaceName}'; }}\n";
            var decl = new TypeDeclaration(fileName, content);
            var host = this._engine.GetValue("$$host");
            host.Get("addFile").Call(host, [fileName, content]);
            this._typeDeclarations[fileName] = decl;
            this._pendingSkeletonNamespaces[namespaceName] = fileName;
            notification = decl;
        }

        if (notification != null)
        {
            this.TypeDeclarationAdded?.Invoke(notification);
        }
    }

    /// <summary>
    /// Registers an arbitrary TypeScript declaration into the language service.
    /// The file name is derived from a hash of the content. Duplicate content is ignored.
    /// </summary>
    public void RegisterDeclaration(string content)
    {
        TypeDeclaration? notification = null;
        lock (this._sync)
        {
            if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content)));
            var fileName = $"decl-{hash}.d.ts";
            if (this._typeDeclarations.ContainsKey(fileName)) return;
            var decl = new TypeDeclaration(fileName, content);
            var host = this._engine.GetValue("$$host");
            host.Get("addFile").Call(host, [fileName, content]);
            this._typeDeclarations[fileName] = decl;
            notification = decl;
        }

        if (notification != null)
        {
            this.TypeDeclarationAdded?.Invoke(notification);
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
            var host = this._engine.GetValue("$$host");
            host.Get("addFile").Call(host, ["lib.es5.d.ts", content]);
        }
    }

    /// <summary>Returns all registered TypeDeclarations (for initial SSE delivery).</summary>
    public IReadOnlyCollection<TypeDeclaration> GetTypeDeclarations()
    {
        lock (this._sync)
        {
            return this._typeDeclarations.Values.ToArray();
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

            var host = this._engine.GetValue("$$host");
            // Add user code as a virtual file
            host.Get("addFile").Call(host, [fileName, source]);

            var service = this._engine.GetValue("$$service");
            var completions = service.Get("getCompletionsAtPosition")
                .Call(service, [fileName, position, new JsObject(this._engine)]);

            if (completions.IsNull() || completions.IsUndefined())
            {
                return [];
            }

            var entries = completions.Get("entries");
            if (entries.IsNull() || entries.IsUndefined())
            {
                return [];
            }

            return ((JsArray) entries)
                .Select(v => new CompletionEntry(
                        v.Get("name").AsString(),
                        v.Get("kind").AsString(),
                        v.Get("sortText").IsUndefined() ? null : v.Get("sortText").AsString()
                    )
                )
                .ToList();
        }
    }

    public void Dispose()
    {
        lock (this._sync)
        {
            this._engine?.Dispose();
            this._engine = null;
            this._ts = null;
            this._tsTranspile = null;
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

    public record CompletionEntry(string Name, string Kind, string? SortText);

    /// <summary>TypeScript declaration file passed to Monaco's addExtraLib.</summary>
    public record TypeDeclaration(string FileName, string Content);
}
