namespace Duets;

/// <summary>
/// Top-level context for a single isolated TypeScript evaluation environment.
/// Owns <see cref="TypeDeclarations"/>, the active <see cref="ITranspiler"/>,
/// and <see cref="ScriptEngine"/> as a unit.
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
        TypeDeclarations declarations,
        ITranspiler transpiler,
        ScriptEngine engine)
    {
        this.Declarations = declarations;
        this.Transpiler = transpiler;
        this.Engine = engine;
        engine.ConsoleLogged += this.OnConsoleLogged;
    }

    /// <summary>The runtime declaration store for this session.</summary>
    public TypeDeclarations Declarations { get; }

    /// <summary>The active transpiler for this session.</summary>
    public ITranspiler Transpiler { get; }

    /// <summary>The script execution engine for this session.</summary>
    internal ScriptEngine Engine { get; }

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
        this.Engine.Execute(tsCode);
    }

    /// <summary>Transpiles and evaluates TypeScript code, returning the result.</summary>
    public ScriptValue Evaluate(string tsCode)
    {
        return this.Engine.Evaluate(tsCode);
    }

    /// <summary>Returns a snapshot of every name the user has defined in this session, excluding built-ins.</summary>
    public IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables()
    {
        return this.Engine.GetGlobalVariables();
    }

    /// <summary>Sets a global variable in the session's script engine.</summary>
    public void SetValue(string name, object value)
    {
        this.Engine.SetValue(name, value);
    }

    public void Dispose()
    {
        this.Engine.ConsoleLogged -= this.OnConsoleLogged;
        this.Engine.Dispose();
        if (this.Transpiler is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static async Task<DuetsSession> CreateCoreAsync(
        Func<TypeDeclarations, Task<ITranspiler>> transpilerFactory,
        Func<ITranspiler, ScriptEngine> engineFactory)
    {
        var declarations = new TypeDeclarations();
        var transpiler = await transpilerFactory(declarations);
        ScriptEngine? engine = null;
        try
        {
            engine = engineFactory(transpiler);
            if (engine.CanRegisterTypeBuiltins)
            {
                engine.RegisterTypeBuiltins(declarations);
            }

            return new DuetsSession(declarations, transpiler, engine);
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
}
