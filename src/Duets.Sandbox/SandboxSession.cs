using HttpHarker;
using Jint;

namespace Duets.Sandbox;

internal sealed class SandboxSession : IAsyncDisposable
{
    public SandboxSession()
    {
        this._ts = new TypeScriptService();
        this._scriptEngine = CreateScriptEngine(this._ts);
    }

    private TypeScriptService _ts;
    private ScriptEngine _scriptEngine;

    private HttpServer? _webServer;
    private ReplService? _replService;
    private CancellationTokenSource? _webServerCts;
    private Task? _webServerTask;

    public string TypeScriptVersion => this._ts.Version ?? "unknown";

    public bool IsServerRunning => this._webServer != null;

    public Task WebServerTask => this._webServerTask ?? Task.CompletedTask;

    private bool _initialized;

    public async Task EnsureInitializedAsync()
    {
        if (this._initialized) return;
        Console.Error.Write("Initializing TypeScript engine...");
        await this._ts.ResetAsync();
        await this._ts.InjectStdLibAsync();
        Console.Error.WriteLine($" TypeScript {this._ts.Version}");
        this.RegisterBuiltins();
        this._initialized = true;
    }

    public string Evaluate(string code)
    {
        return this._scriptEngine.Evaluate(code).ToString();
    }

    public IReadOnlyList<TypeScriptService.CompletionEntry> GetCompletions(string source, int position)
    {
        return this._ts.GetCompletions(source, position);
    }

    public IReadOnlyCollection<TypeScriptService.TypeDeclaration> GetTypeDeclarations()
    {
        return this._ts.GetTypeDeclarations();
    }

    public string RegisterType(string typeName)
    {
        var type = Type.GetType(typeName)
            ?? throw new InvalidOperationException($"Type not found: {typeName}");
        this._ts.RegisterType(type);
        return type.FullName!;
    }

    public void StartWebServer(int port = 17375)
    {
        if (this._webServer != null) return;
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
        catch (OperationCanceledException)
        {
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
        this._ts = new TypeScriptService();
        this._scriptEngine = CreateScriptEngine(this._ts);
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
    }

    private static ScriptEngine CreateScriptEngine(TypeScriptService ts)
    {
        return new ScriptEngine(
            opts => opts.AllowClr(
                typeof(Math).Assembly,
                typeof(Enumerable).Assembly,
                typeof(HttpClient).Assembly
            ),
            ts
        );
    }

    private void RegisterBuiltins()
    {
        this._ts.RegisterType(typeof(Math));
        this._ts.RegisterType(typeof(Enumerable));
        this._scriptEngine.SetValue(
            "importTypeDefs",
            new Action<string>(typeName =>
                {
                    var type = Type.GetType(typeName)
                        ?? throw new InvalidOperationException($"Type not found: {typeName}");
                    this._ts.RegisterType(type);
                }
            )
        );
    }
}
