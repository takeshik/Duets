using Jint;
using Jint.Native;

namespace Duets;

/// <summary>
/// Top-level context for a single isolated TypeScript evaluation environment.
/// Owns <see cref="TypeDeclarations"/>, the active <see cref="ITranspiler"/>,
/// and <see cref="ScriptEngine"/> as a unit.
/// </summary>
/// <remarks>
/// Obtain an instance via <see cref="CreateAsync(Action{Options}, BabelTranspilerOptions)"/>
/// (default <see cref="BabelTranspiler"/>) or the factory overloads when
/// <see cref="TypeScriptService"/> or another transpiler is needed. Each session
/// creates and owns one <see cref="TypeDeclarations"/> instance and passes that
/// instance to the selected transpiler factory so the declaration store stays
/// consistent across the whole session. Create multiple sessions for concurrent use
/// rather than sharing one.
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
    public ScriptEngine Engine { get; }

    /// <summary>Raised synchronously each time script code calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    /// <summary>
    /// Creates a session with <see cref="BabelTranspiler"/> as the transpiler.
    /// </summary>
    public static async Task<DuetsSession> CreateAsync(
        Action<Options>? configure = null,
        BabelTranspilerOptions? transpilerOptions = null)
    {
        var declarations = new TypeDeclarations();
        var transpiler = await BabelTranspiler.CreateAsync(transpilerOptions);
        try
        {
            var engine = new ScriptEngine(configure, transpiler);
            return new DuetsSession(declarations, transpiler, engine);
        }
        catch
        {
            transpiler.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a session using a transpiler factory.
    /// The factory receives the session-owned <see cref="TypeDeclarations"/> instance,
    /// allowing the transpiler (e.g. <see cref="TypeScriptService"/>) to subscribe to
    /// declaration changes before the session is used.
    /// </summary>
    /// <example>
    /// <code>
    /// using var session = await DuetsSession.CreateAsync(
    ///     decls => TypeScriptService.CreateAsync(decls),
    ///     opts => opts.AllowClr());
    /// </code>
    /// </example>
    public static async Task<DuetsSession> CreateAsync(
        Func<TypeDeclarations, Task<ITranspiler>> transpilerFactory,
        Action<Options>? configure = null)
    {
        var declarations = new TypeDeclarations();
        var transpiler = await transpilerFactory(declarations);
        try
        {
            var engine = new ScriptEngine(configure, transpiler);
            return new DuetsSession(declarations, transpiler, engine);
        }
        catch
        {
            (transpiler as IDisposable)?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a session using a synchronous transpiler factory.
    /// The factory receives the session-owned <see cref="TypeDeclarations"/> instance,
    /// which prevents callers from accidentally wiring the session and transpiler to
    /// different declaration stores.
    /// </summary>
    /// <remarks>
    /// This overload is intended for test doubles and custom synchronous transpilers.
    /// Built-in transpilers (<see cref="BabelTranspiler"/>, <see cref="TypeScriptService"/>)
    /// require async initialization; use <see cref="CreateAsync(Func{TypeDeclarations,Task{ITranspiler}},Action{Options})"/>
    /// for those.
    /// </remarks>
    internal static DuetsSession Create(
        Func<TypeDeclarations, ITranspiler> transpilerFactory,
        Action<Options>? configure = null)
    {
        var declarations = new TypeDeclarations();
        var transpiler = transpilerFactory(declarations);
        try
        {
            var engine = new ScriptEngine(configure, transpiler);
            return new DuetsSession(declarations, transpiler, engine);
        }
        catch
        {
            (transpiler as IDisposable)?.Dispose();
            throw;
        }
    }

    /// <summary>Transpiles and executes TypeScript code in this session.</summary>
    public void Execute(string tsCode)
    {
        this.Engine.Execute(tsCode);
    }

    /// <summary>Transpiles and evaluates TypeScript code, returning the result.</summary>
    public JsValue Evaluate(string tsCode)
    {
        return this.Engine.Evaluate(tsCode);
    }

    /// <summary>Returns a snapshot of every name the user has defined in this session, excluding built-ins.</summary>
    public IReadOnlyDictionary<JsValue, JsValue> GetGlobalVariables()
    {
        return this.Engine.GetGlobalVariables();
    }

    /// <summary>Sets a global variable in the session's script engine.</summary>
    public void SetValue(string name, object value)
    {
        this.Engine.SetValue(name, value);
    }

    /// <summary>
    /// Registers the <c>typings</c> global object and <c>clrTypeOf</c> function into the session engine.
    /// Requires <c>AllowClr</c> to be configured for namespace and assembly operations.
    /// </summary>
    public DuetsSession RegisterTypeBuiltins()
    {
        this.Engine.RegisterTypeBuiltins(this.Declarations);
        return this;
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

    private void OnConsoleLogged(ScriptConsoleEntry entry)
    {
        this.ConsoleLogged?.Invoke(entry);
    }
}
