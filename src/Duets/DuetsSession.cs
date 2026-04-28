namespace Duets;

/// <summary>
/// Top-level context for a single isolated TypeScript evaluation environment.
/// Owns <see cref="TypeDeclarations"/>, the active <see cref="ITranspiler"/>,
/// and <see cref="IScriptEngine"/> as a unit.
/// </summary>
/// <remarks>
/// Obtain an instance via <see cref="CreateAsync(Action{DuetsSessionConfiguration})"/>
/// and configure the desired backend through extensions provided by backend packages.
/// When no engine or transpiler is configured explicitly, defaults registered by the
/// loaded backend package (e.g. Duets.Jint) are used automatically.
/// Each session creates and owns one <see cref="TypeDeclarations"/>
/// instance and passes that instance to the selected transpiler factory so the declaration
/// store stays consistent across the whole session. Create multiple sessions for concurrent
/// use rather than sharing one.
/// </remarks>
public sealed class DuetsSession : IDisposable
{
    private DuetsSession(
        JsDocProviders jsDocProviders,
        TypeDeclarations declarations,
        ITranspiler transpiler,
        IScriptEngine engine)
    {
        this.JsDocProviders = jsDocProviders;
        this.Declarations = declarations;
        this.Transpiler = transpiler;
        this.Engine = engine;
        engine.ConsoleLogged += this.OnConsoleLogged;
    }

    private const string ConcurrentOperationMessage =
        "Concurrent use of a DuetsSession is not supported. Create a separate session for each concurrent operation.";

    private int _operationInProgress;
    private bool _disposed;

    /// <summary>The JSDoc provider repository for this session.</summary>
    public JsDocProviders JsDocProviders { get; }

    /// <summary>The runtime declaration store for this session.</summary>
    public TypeDeclarations Declarations { get; }

    /// <summary>The active transpiler for this session.</summary>
    public ITranspiler Transpiler { get; }

    /// <summary>The script execution engine for this session.</summary>
    internal IScriptEngine Engine { get; }

    /// <summary>Raised synchronously each time script code calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    /// <summary>
    /// Creates a session with optional explicit configuration.
    /// When no engine or transpiler is specified, defaults registered by the loaded backend
    /// package are used. Call <see cref="DuetsSessionConfiguration.UseEngine"/> or
    /// <see cref="DuetsSessionConfiguration.UseTranspiler(Func{TypeDeclarations,Task{ITranspiler}})"/>
    /// for advanced control, or use the convenience extensions provided by backend packages.
    /// </summary>
    public static Task<DuetsSession> CreateAsync(Action<DuetsSessionConfiguration>? configure = null)
    {
        var configuration = new DuetsSessionConfiguration();
        configure?.Invoke(configuration);
        return CreateCoreAsync(
            configuration.GetRequiredTranspilerFactory(),
            configuration.GetRequiredEngineFactory()
        );
    }

    /// <summary>Transpiles and executes TypeScript code in this session.</summary>
    public void Execute(string tsCode)
    {
        using var _ = this.EnterOperation();
        this.ThrowIfDisposed();
        this.Engine.Execute(tsCode);
    }

    /// <summary>Transpiles and executes TypeScript code in this session, properly awaiting any top-level promises.</summary>
    public Task ExecuteAsync(string tsCode, CancellationToken cancellationToken = default)
    {
        return this.ExecuteCoreAsync(tsCode, cancellationToken);
    }

    /// <summary>Transpiles and evaluates TypeScript code, returning the result.</summary>
    public ScriptValue Evaluate(string tsCode)
    {
        using var _ = this.EnterOperation();
        this.ThrowIfDisposed();
        return this.Engine.Evaluate(tsCode);
    }

    /// <summary>Transpiles and evaluates TypeScript code, returning the resolved result of any top-level promise.</summary>
    public Task<ScriptValue> EvaluateAsync(string tsCode, CancellationToken cancellationToken = default)
    {
        return this.EvaluateCoreAsync(tsCode, cancellationToken);
    }

    /// <summary>Returns a snapshot of every name the user has defined in this session, excluding built-ins.</summary>
    public IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables()
    {
        using var _ = this.EnterOperation();
        this.ThrowIfDisposed();
        return this.Engine.GetGlobalVariables();
    }

    /// <summary>Sets a global variable in the session's script engine.</summary>
    public void SetValue(string name, object value)
    {
        using var _ = this.EnterOperation();
        this.ThrowIfDisposed();
        this.Engine.SetValue(name, value);
    }

    public void Dispose()
    {
        using var _ = this.EnterOperation();
        if (this._disposed) return;
        this.Engine.ConsoleLogged -= this.OnConsoleLogged;
        this.Engine.Dispose();
        if (this.Transpiler is IDisposable disposable)
        {
            disposable.Dispose();
        }

        this._disposed = true;
    }

    private static async Task<DuetsSession> CreateCoreAsync(
        Func<TypeDeclarations, Task<ITranspiler>> transpilerFactory,
        Func<ITranspiler, IScriptEngine> engineFactory)
    {
        var jsDocProviders = new JsDocProviders();
        var generator = new ClrDeclarationGenerator(jsDocProviders);
        var declarations = new TypeDeclarations(generator);
        jsDocProviders.ProviderAdded += declarations.RefreshDeclarations;
        var transpiler = await transpilerFactory(declarations);
        IScriptEngine? engine = null;
        try
        {
            engine = engineFactory(transpiler);
            if (engine.CanRegisterTypeBuiltins)
            {
                engine.RegisterTypeBuiltins(declarations);
            }

            return new DuetsSession(jsDocProviders, declarations, transpiler, engine);
        }
        catch
        {
            engine?.Dispose();
            (transpiler as IDisposable)?.Dispose();
            throw;
        }
    }

    private void OnConsoleLogged(ScriptConsoleEntry entry)
    {
        this.ConsoleLogged?.Invoke(entry);
    }

    private OperationScope EnterOperation(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref this._operationInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException(ConcurrentOperationMessage);
        }

        return new OperationScope(this);
    }

    private async Task ExecuteCoreAsync(string tsCode, CancellationToken cancellationToken)
    {
        using var _ = this.EnterOperation(cancellationToken);
        this.ThrowIfDisposed();
        await this.Engine.ExecuteAsync(tsCode, cancellationToken);
    }

    private async Task<ScriptValue> EvaluateCoreAsync(string tsCode, CancellationToken cancellationToken)
    {
        using var _ = this.EnterOperation(cancellationToken);
        this.ThrowIfDisposed();
        return await this.Engine.EvaluateAsync(tsCode, cancellationToken);
    }

    private void ThrowIfDisposed()
    {
#if NETSTANDARD2_1
        if (this._disposed) throw new ObjectDisposedException(this.GetType().FullName);
#else
        ObjectDisposedException.ThrowIf(this._disposed, this);
#endif
    }

    private readonly struct OperationScope(DuetsSession session) : IDisposable
    {
        public void Dispose()
        {
            Volatile.Write(ref session._operationInProgress, 0);
        }
    }
}
