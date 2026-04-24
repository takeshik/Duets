using System.Diagnostics;
using Duets.Jint;
using HttpHarker;
using Jint;

namespace Duets.Sandbox;

internal enum TranspilerKind
{
    TypeScript,
    Babel,
}

internal sealed class SandboxContext : IAsyncDisposable
{
    private SandboxContext(
        DuetsSession session,
        TranspilerKind activeTranspiler,
        Func<TypeDeclarations, Task<ITranspiler>> tsFactory,
        Func<TypeDeclarations, Task<ITranspiler>> babelFactory)
    {
        this._session = session;
        this.ActiveTranspiler = activeTranspiler;
        this._tsFactory = tsFactory;
        this._babelFactory = babelFactory;
    }

    private readonly Func<TypeDeclarations, Task<ITranspiler>> _tsFactory;
    private readonly Func<TypeDeclarations, Task<ITranspiler>> _babelFactory;
    private DuetsSession _session;
    private HttpServer? _webServer;
    private ReplService? _replService;
    private CancellationTokenSource? _webServerCts;
    private Task? _webServerTask;

    public TranspilerKind ActiveTranspiler { get; private set; }

    public string TranspilerDescription => this._session.Transpiler.Description;

    public bool IsServerRunning => this._webServer != null && this._webServerTask is { IsCompleted: false };

    internal static async Task<SandboxContext> CreateAsync(
        Func<TypeDeclarations, Task<TypeScriptService>>? tsFactory = null,
        Func<Task<BabelTranspiler>>? babelFactory = null)
    {
        Func<TypeDeclarations, Task<ITranspiler>> tsF =
            tsFactory is not null
                ? async decls => await tsFactory(decls)
                : async decls => await TypeScriptService.CreateAsync(decls, injectStdLib: true);

        Func<TypeDeclarations, Task<ITranspiler>> babelF =
            babelFactory is not null
                ? async _ => await babelFactory()
                : async _ => await BabelTranspiler.CreateAsync();

        var session = await CreateDuetsSessionAsync(TranspilerKind.TypeScript, tsF, babelF);
        return new SandboxContext(session, TranspilerKind.TypeScript, tsF, babelF);
    }

    public (string Result, IReadOnlyList<ScriptConsoleEntry> Logs) Evaluate(string code)
    {
        var logs = new List<ScriptConsoleEntry>();

        void OnLog(ScriptConsoleEntry e)
        {
            logs.Add(e);
        }

        this._session.ConsoleLogged += OnLog;
        try
        {
            return (this._session.Evaluate(code).ToString(), logs);
        }
        finally
        {
            this._session.ConsoleLogged -= OnLog;
        }
    }

    public IReadOnlyList<TypeScriptService.CompletionEntry> GetCompletions(string source, int position)
    {
        if (this._session.Transpiler is not TypeScriptService ts)
        {
            throw new InvalidOperationException(
                $"Completions require the TypeScript transpiler (active: {this.ActiveTranspiler})."
            );
        }

        return ts.GetCompletions(source, position);
    }

    public string RegisterType(string typeName)
    {
        var type = Type.GetType(typeName)
            ?? throw new InvalidOperationException($"Type not found: {typeName}");
        this._session.Declarations.RegisterType(type);
        return type.FullName!;
    }

    public IReadOnlyCollection<TypeDeclaration> GetTypeDeclarations()
    {
        return this._session.Declarations.GetDeclarations();
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
        var old = this._session;
        this._session = await CreateDuetsSessionAsync(kind, this._tsFactory, this._babelFactory);
        this.ActiveTranspiler = kind;
        old.Dispose();
    }

    public async Task ResetAsync()
    {
        if (this._webServer != null) await this.StopWebServerAsync();
        var old = this._session;
        this._session = await CreateDuetsSessionAsync(this.ActiveTranspiler, this._tsFactory, this._babelFactory);
        old.Dispose();
    }

    public void StartWebServer(int port = 17375)
    {
        if (this._session.Transpiler is not TypeScriptService)
        {
            throw new InvalidOperationException("The web server requires the TypeScript transpiler.");
        }

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
        this._replService = this._webServer.UseContentTypeDetection().UseRepl(this._session);
        this._webServerTask = this._webServer.RunAsync(cancellationToken: this._webServerCts.Token);
        Console.Error.WriteLine($"Web REPL server started at http://127.0.0.1:{port}/");
    }

    public async Task StopWebServerAsync()
    {
        if (this._webServer == null) return;
        await this._webServerCts!.CancelAsync();
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
        await Console.Error.WriteLineAsync("Web server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (this._webServerCts is { } cts) await cts.CancelAsync();
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
        this._session.Dispose();
    }

    private static async Task<DuetsSession> CreateDuetsSessionAsync(
        TranspilerKind kind,
        Func<TypeDeclarations, Task<ITranspiler>> tsFactory,
        Func<TypeDeclarations, Task<ITranspiler>> babelFactory)
    {
        var factory = kind switch
        {
            TranspilerKind.TypeScript => tsFactory,
            TranspilerKind.Babel => babelFactory,
            _ => throw new UnreachableException(),
        };
        var kindName = kind switch
        {
            TranspilerKind.TypeScript => "TypeScript",
            TranspilerKind.Babel => "Babel",
            _ => throw new UnreachableException(),
        };

        await Console.Error.WriteAsync($"Initializing {kindName} engine...");
        var session = await DuetsSession.CreateAsync(config => config
            .UseTranspiler(factory)
            .UseJint(opts => opts.AllowClr())
        );
        await Console.Error.WriteLineAsync($" {session.Transpiler.Description}");
        return session;
    }
}
