using System.Security.Cryptography;
using System.Text;
using Jint;
using Jint.Native;
using Mio;
using Mio.Destructive;

namespace Duets;

public class TypeScriptService : ITranspiler,
    IDisposable
{
    private readonly HashSet<Type> _registeredTypes = [];
    private readonly HashSet<string> _registeredNamespaces = [];
    private readonly ClrDeclarationGenerator _declarationGenerator = new();
    private readonly Dictionary<string, TypeDeclaration> _typeDeclarations = new();
    private Engine? _engine;
    private JsValue? _ts;
    private JsValue? _tsTranspile;

    public string? Version { get; private set; }

    /// <summary>Fires when a new type declaration is registered. Used for SSE delivery and similar purposes.</summary>
    public event Action<TypeDeclaration>? TypeDeclarationAdded;

    public async Task ResetAsync(bool forceDownloadCodes = false)
    {
        this._registeredTypes.Clear();
        this._registeredNamespaces.Clear();
        this._typeDeclarations.Clear();
        this._engine?.Dispose();
        this._engine = new Engine();
        await this._engine.ExecuteAsync(await FetchTypeScriptJsAsync(forceDownloadCodes));
        this._ts = this._engine.GetValue("ts");
        this._tsTranspile = this._ts.Get("transpile");
        this.Version = this._ts.Get("version").AsString();

        // Initialize language services
        await using var stream = typeof(TypeScriptService).Assembly.GetManifestResourceStream("Duets.Resources.ReplStaticFiles.language-service.js")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await this._engine!.ExecuteAsync(await reader.ReadToEndAsync());
    }

    /// <summary>
    /// Registers a .NET type as a completion target. Used to generate type declarations (.d.ts).
    /// Duplicate registrations of the same type are ignored.
    /// </summary>
    public void RegisterType(Type type)
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
        this._engine.GetValue("$$host").Get("addFile").Call(this._engine.GetValue("$$host"), [fileName, content]);
        this._typeDeclarations[fileName] = decl;
        this.TypeDeclarationAdded?.Invoke(decl);
    }

    /// <summary>
    /// Registers a namespace skeleton declaration so that the namespace appears in TypeScript completions
    /// without registering any type members. Duplicate registrations are ignored.
    /// </summary>
    public void RegisterNamespaceSkeleton(string namespaceName)
    {
        if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
        if (!this._registeredNamespaces.Add(namespaceName)) return;

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"ns:{namespaceName}")));
        var fileName = $"clr-ns-{hash}.d.ts";
        var content = $"declare namespace {namespaceName} {{ }}\n";
        var decl = new TypeDeclaration(fileName, content);
        this._engine.GetValue("$$host").Get("addFile").Call(this._engine.GetValue("$$host"), [fileName, content]);
        this._typeDeclarations[fileName] = decl;
        this.TypeDeclarationAdded?.Invoke(decl);
    }

    /// <summary>
    /// Injects the ES5 standard library into the language service so that JS built-in completions
    /// (Array, string, Math, Promise, etc.) are available alongside registered .NET types.
    /// Optional: the Monaco-based web REPL runs its own TypeScript language service client-side
    /// and does not require this. Call this when using <see cref="GetCompletions"/> directly.
    /// </summary>
    public async Task InjectStdLibAsync(bool forceDownload = false)
    {
        if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
        var content = await FetchLibEs5Async(this.Version!, forceDownload);
        this._engine.GetValue("$$host").Get("addFile").Call(this._engine.GetValue("$$host"), ["lib.es5.d.ts", content]);
    }

    /// <summary>Returns all registered TypeDeclarations (for initial SSE delivery).</summary>
    public IReadOnlyCollection<TypeDeclaration> GetTypeDeclarations()
    {
        return this._typeDeclarations.Values;
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
        if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");

        // Add user code as a virtual file
        this._engine.GetValue("$$host").Get("addFile").Call(this._engine.GetValue("$$host"), [fileName, source]);

        var completions = this._engine.GetValue("$$service").Get("getCompletionsAtPosition").Call(this._engine.GetValue("$$service"), [fileName, position, new JsObject(this._engine)]);

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

    public void Dispose()
    {
        this._engine?.Dispose();
    }

    public string Transpile(
        string input,
        CompilerOptions? compilerOptions = null,
        string? fileName = null,
        IList<Diagnostic>? diagnostics = null,
        string? moduleName = null)
    {
        if (this._engine == null) throw new InvalidOperationException("Call ResetAsync() first.");
        var diagsArray = new JsArray(this._engine);
        var ret = ((JsString) this._engine.Call(
                    this._tsTranspile!,
                    this._ts!,
                    [
                        input,
                        compilerOptions == null
                            ? JsValue.Null
                            : JsValue.FromObject(this._engine, compilerOptions),
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

    private static async Task<string> FetchTypeScriptJsAsync(bool forceFetch = true)
    {
        var file = DirectoryPath.GetTempDirectory().ChildFile("typescript.js");
        if (!forceFetch
            && file.Exists()
            && (DateTimeOffset.Now - file.GetCreationTime()).TotalDays < 7)
        {
            return await file.ReadAllTextAsync();
        }

        var code = await new HttpClient().GetStringAsync("https://unpkg.com/typescript@5.9.3/lib/typescript.js");
        await file.AsDestructive().WriteAsync(code);
        return code;
    }

    private static async Task<string> FetchLibEs5Async(string tsVersion, bool forceFetch = false)
    {
        var file = DirectoryPath.GetTempDirectory().ChildFile($"typescript-lib.es5-{tsVersion}.d.ts");
        if (!forceFetch
            && file.Exists()
            && (DateTimeOffset.Now - file.GetCreationTime()).TotalDays < 7)
        {
            return await file.ReadAllTextAsync();
        }

        var content = await new HttpClient().GetStringAsync($"https://unpkg.com/typescript@{tsVersion}/lib/lib.es5.d.ts");
        await file.AsDestructive().WriteAsync(content);
        return content;
    }

    public record CompletionEntry(string Name, string Kind, string? SortText);

    /// <summary>TypeScript declaration file passed to Monaco's addExtraLib.</summary>
    public record TypeDeclaration(string FileName, string Content);
}
