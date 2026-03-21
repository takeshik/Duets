using System.Diagnostics;
using HttpHarker;
using Jint;

namespace Duets.Sandbox;

internal enum TranspilerKind
{
    TypeScript,
    Babel,
}

internal sealed class SandboxSession : IAsyncDisposable
{
    public SandboxSession()
    {
        this._ts = new TypeScriptService();
        this._activeTranspiler = this._ts;
        this._scriptEngine = CreateScriptEngine(this._ts);
    }

    private TypeScriptService _ts;
    private BabelTranspiler? _babel;
    private ITranspiler _activeTranspiler;
    private ScriptEngine _scriptEngine;

    private HttpServer? _webServer;
    private ReplService? _replService;
    private CancellationTokenSource? _webServerCts;
    private Task? _webServerTask;

    private bool _initialized;

    public TranspilerKind ActiveTranspiler { get; private set; } = TranspilerKind.TypeScript;

    public string TranspilerDescription => this._activeTranspiler.Description;

    public bool IsServerRunning => this._webServer != null && this._webServerTask is { IsCompleted: false };

    public Task WebServerTask => this._webServerTask ?? Task.CompletedTask;

    public async Task EnsureInitializedAsync()
    {
        if (this._initialized) return;

        switch (this.ActiveTranspiler)
        {
            case TranspilerKind.TypeScript:
                Console.Error.Write("Initializing TypeScript engine...");
                await this._ts.ResetAsync();
                await this._ts.InjectStdLibAsync();
                Console.Error.WriteLine($" TypeScript {this._ts.Version}");
                this._scriptEngine.RegisterTypeBuiltins(this._ts);
                break;
            case TranspilerKind.Babel:
                Console.Error.Write("Initializing Babel transpiler...");
                await this._babel!.InitializeAsync();
                Console.Error.WriteLine($" Babel {this._babel.Version}");
                break;
            default:
                throw new UnreachableException();
        }

        this._initialized = true;
    }

    public async Task SetTranspilerAsync(string name)
    {
        var kind = name.ToLowerInvariant() switch
        {
            "typescript" => TranspilerKind.TypeScript,
            "babel" => TranspilerKind.Babel,
            _ => throw new ArgumentException($"Unknown transpiler: '{name}'. Valid values: typescript, babel"),
        };
        await this.SetTranspilerAsync(kind);
    }

    public async Task SetTranspilerAsync(TranspilerKind kind)
    {
        if (kind == this.ActiveTranspiler) return;
        if (this._webServer != null) await this.StopWebServerAsync();
        this._scriptEngine.Dispose();
        this._ts.Dispose();
        this._babel?.Dispose();
        this._babel = null;
        this._ts = new TypeScriptService();
        this.ActiveTranspiler = kind;
        this._initialized = false;

        switch (kind)
        {
            case TranspilerKind.TypeScript:
                this._activeTranspiler = this._ts;
                this._scriptEngine = CreateScriptEngine(this._ts);
                break;
            case TranspilerKind.Babel:
                this._babel = new BabelTranspiler();
                this._activeTranspiler = this._babel;
                this._scriptEngine = CreateScriptEngine(this._babel);
                break;
            default:
                throw new UnreachableException();
        }

        await this.EnsureInitializedAsync();
    }

    public string Evaluate(string code)
    {
        return this._scriptEngine.Evaluate(code).ToString();
    }

    public IReadOnlyList<TypeScriptService.CompletionEntry> GetCompletions(string source, int position)
    {
        this.RequireTranspiler(TranspilerKind.TypeScript, "Completions");
        return this._ts.GetCompletions(source, position);
    }

    public IReadOnlyCollection<TypeScriptService.TypeDeclaration> GetTypeDeclarations()
    {
        this.RequireTranspiler(TranspilerKind.TypeScript, "Type declarations");
        return this._ts.GetTypeDeclarations();
    }

    public string RegisterType(string typeName)
    {
        this.RequireTranspiler(TranspilerKind.TypeScript, "Type registration");
        var type = Type.GetType(typeName)
            ?? throw new InvalidOperationException($"Type not found: {typeName}");
        this._ts.RegisterType(type);
        return type.FullName!;
    }

    public void StartWebServer(int port = 17375)
    {
        this.RequireTranspiler(TranspilerKind.TypeScript, "The web server");
        if (this.IsServerRunning) return;

        // Clean up any previously faulted server state before restarting.
        if (this._webServer != null)
        {
            this._replService?.Dispose();
            this._replService = null;
            this._webServer.Dispose();
            this._webServer = null;
            this._webServerCts = null;
            this._webServerTask = null;
        }

        this._webServerCts = new CancellationTokenSource();
        this._webServer = new HttpServer($"http://127.0.0.1:{port}/");
        this._replService = this._webServer.UseContentTypeDetection().UseRepl(this._ts, this._scriptEngine);
        this._webServerTask = this._webServer.RunAsync(cancellationToken: this._webServerCts.Token);
        Console.Error.WriteLine($"Web REPL server started at http://127.0.0.1:{port}/");
    }

    public async Task StopWebServerAsync()
    {
        if (this._webServer == null) return;
        this._webServerCts!.Cancel();
        try
        {
            await this._webServerTask!;
        }
        catch
        {
            // Ignore both OperationCanceledException (normal cancellation) and any
            // fault the task may have accumulated before Stop() was called.
        }

        this._replService?.Dispose();
        this._replService = null;
        this._webServer.Dispose();
        this._webServer = null;
        this._webServerCts = null;
        this._webServerTask = null;
        Console.Error.WriteLine("Web server stopped.");
    }

    public async Task ResetAsync()
    {
        if (this._webServer != null) await this.StopWebServerAsync();
        this._scriptEngine.Dispose();
        this._ts.Dispose();
        this._babel?.Dispose();

        this._ts = new TypeScriptService();
        switch (this.ActiveTranspiler)
        {
            case TranspilerKind.TypeScript:
                this._babel = null;
                this._activeTranspiler = this._ts;
                this._scriptEngine = CreateScriptEngine(this._ts);
                break;
            case TranspilerKind.Babel:
                this._babel = new BabelTranspiler();
                this._activeTranspiler = this._babel;
                this._scriptEngine = CreateScriptEngine(this._babel);
                break;
            default:
                throw new UnreachableException();
        }

        this._initialized = false;
        await this.EnsureInitializedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        this._webServerCts?.Cancel();
        if (this._webServerTask != null)
        {
            try
            {
                await this._webServerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        this._replService?.Dispose();
        this._webServer?.Dispose();
        this._scriptEngine.Dispose();
        this._ts.Dispose();
        this._babel?.Dispose();
    }

    private static ScriptEngine CreateScriptEngine(ITranspiler transpiler)
    {
        return new ScriptEngine(opts => opts.AllowClr(), transpiler);
    }

    private void RequireTranspiler(TranspilerKind required, string featureName)
    {
        if (this.ActiveTranspiler != required)
        {
            throw new InvalidOperationException(
                $"{featureName} requires the {required} transpiler (active: {this.ActiveTranspiler})."
            );
        }
    }
}
