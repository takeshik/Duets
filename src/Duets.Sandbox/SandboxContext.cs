using Duets.Jint;
using Duets.Okojo;
using HttpHarker;
using Jint;
using Okojo.Reflection;
using BabelTranspiler = Duets.Jint.BabelTranspiler;
using TypeScriptService = Duets.Jint.TypeScriptService;

namespace Duets.Sandbox;

internal sealed class PassThroughTranspiler : ITranspiler
{
    public string Description => "none";

    public string Transpile(string input, string? fileName = null, IList<Diagnostic>? diagnostics = null, string? moduleName = null)
    {
        return input;
    }
}

internal sealed record TranspilerChoice(
    string Name,
    Func<TypeDeclarations, Task<ITranspiler>> Factory)
{
    public static readonly TranspilerChoice TypeScript = new(
        "typescript",
        async decls => await TypeScriptService.CreateAsync(decls, injectStdLib: true)
    );

    public static readonly TranspilerChoice Babel = new(
        "babel",
        async _ => await BabelTranspiler.CreateAsync()
    );

    public static TranspilerChoice Parse(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "typescript" => TypeScript,
            "babel" => Babel,
            _ => throw new ArgumentException($"Unknown transpiler: '{name}'. Valid values: typescript, babel"),
        };
    }
}

internal sealed record BackendChoice(
    string Name,
    Action<DuetsSessionConfiguration> Configure,
    bool SupportsExternalTranspiler = true)
{
    public static readonly BackendChoice Jint = new(
        "jint",
        config => config.UseJint(opts => opts.AllowClr())
    );

    // Okojo's JS runtime cannot load Babel or TypeScript bundles (runtime bugs in 0.1.1-preview.1).
    // Engine-only mode: IdentityTranspiler is used regardless of the transpiler choice.
    public static readonly BackendChoice Okojo = new(
        "okojo",
        config => config.UseOkojo(builder =>
            builder.AllowClrAccess(AppDomain.CurrentDomain.GetAssemblies())
        ),
        false
    );

    public static BackendChoice Parse(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "jint" => Jint,
            "okojo" => Okojo,
            _ => throw new ArgumentException($"Unknown backend: '{name}'. Valid values: jint, okojo"),
        };
    }
}

internal sealed class SandboxContext : IAsyncDisposable
{
    private SandboxContext(
        DuetsSession session,
        TranspilerChoice transpiler,
        BackendChoice backend)
    {
        this._session = session;
        this.ActiveTranspiler = transpiler;
        this.ActiveBackend = backend;
    }

    private DuetsSession _session;
    private HttpServer? _webServer;
    private ReplService? _replService;
    private CancellationTokenSource? _webServerCts;
    private Task? _webServerTask;

    public TranspilerChoice ActiveTranspiler { get; private set; }
    public BackendChoice ActiveBackend { get; private set; }

    public string TranspilerDescription => this._session.Transpiler.Description;

    public bool IsServerRunning => this._webServer != null && this._webServerTask is { IsCompleted: false };

    internal static async Task<SandboxContext> CreateAsync(
        TranspilerChoice? transpiler = null,
        BackendChoice? backend = null)
    {
        var t = transpiler ?? TranspilerChoice.TypeScript;
        var b = backend ?? BackendChoice.Jint;
        var session = await CreateDuetsSessionAsync(t, b);
        return new SandboxContext(session, t, b);
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
                $"Completions require the TypeScript transpiler (active: {this.ActiveTranspiler.Name})."
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
        await this.SetTranspilerAsync(TranspilerChoice.Parse(name));
    }

    public async Task SetTranspilerAsync(TranspilerChoice choice)
    {
        if (!this.ActiveBackend.SupportsExternalTranspiler)
        {
            throw new InvalidOperationException(
                $"The {this.ActiveBackend.Name} backend does not support external transpilers (engine-only mode)."
            );
        }

        if (choice == this.ActiveTranspiler) return;
        if (this._webServer != null) await this.StopWebServerAsync();
        var old = this._session;
        this._session = await CreateDuetsSessionAsync(choice, this.ActiveBackend);
        this.ActiveTranspiler = choice;
        old.Dispose();
    }

    public async Task SetBackendAsync(string name)
    {
        await this.SetBackendAsync(BackendChoice.Parse(name));
    }

    public async Task SetBackendAsync(BackendChoice choice)
    {
        if (choice == this.ActiveBackend) return;
        if (this._webServer != null) await this.StopWebServerAsync();
        var old = this._session;
        this._session = await CreateDuetsSessionAsync(this.ActiveTranspiler, choice);
        this.ActiveBackend = choice;
        old.Dispose();
    }

    public async Task ResetAsync()
    {
        if (this._webServer != null) await this.StopWebServerAsync();
        var old = this._session;
        this._session = await CreateDuetsSessionAsync(this.ActiveTranspiler, this.ActiveBackend);
        old.Dispose();
    }

    public void StartWebServer(int port = 17375)
    {
        if (this._session.Transpiler is not TypeScriptService)
        {
            throw new InvalidOperationException("The web server requires the TypeScript transpiler.");
        }

        if (this.IsServerRunning) return;

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
        TranspilerChoice transpiler,
        BackendChoice backend)
    {
        if (backend.SupportsExternalTranspiler)
        {
            await Console.Error.WriteAsync($"Initializing {backend.Name}/{transpiler.Name} engine...");
            var session = await DuetsSession.CreateAsync(transpiler.Factory, backend.Configure);
            await Console.Error.WriteLineAsync($" {session.Transpiler.Description}");
            return session;
        }
        else
        {
            await Console.Error.WriteAsync($"Initializing {backend.Name} engine (engine-only, no transpiler)...");
            var session = await DuetsSession.CreateAsync(
                _ => Task.FromResult<ITranspiler>(new PassThroughTranspiler()),
                backend.Configure
            );
            await Console.Error.WriteLineAsync(" done");
            return session;
        }
    }
}
