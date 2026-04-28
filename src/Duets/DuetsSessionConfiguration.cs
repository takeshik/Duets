namespace Duets;

/// <summary>
/// Collects configuration needed to create a <see cref="DuetsSession"/>.
/// Backend packages extend this type with runtime-specific convenience methods.
/// When no explicit engine or transpiler is configured, defaults registered in
/// <see cref="DuetsBackendRegistry"/> are used.
/// </summary>
public sealed class DuetsSessionConfiguration
{
    internal DuetsSessionConfiguration()
    {
    }

    private Func<ITranspiler, IScriptEngine>? _engineFactory;
    private Func<TypeDeclarations, Task<ITranspiler>>? _transpilerFactory;

    /// <summary>
    /// Selects the script engine factory for the session being created.
    /// Intended for advanced callers and backend package extensions.
    /// </summary>
    public DuetsSessionConfiguration UseEngine(Func<ITranspiler, IScriptEngine> engineFactory)
    {
        if (engineFactory == null)
        {
            throw new ArgumentNullException(nameof(engineFactory));
        }

        if (this._engineFactory != null)
        {
            throw new InvalidOperationException("The session engine has already been configured.");
        }

        this._engineFactory = engineFactory;
        return this;
    }

    /// <summary>
    /// Selects the transpiler factory for the session being created.
    /// The factory receives the session-owned <see cref="TypeDeclarations"/> instance.
    /// </summary>
    public DuetsSessionConfiguration UseTranspiler(Func<TypeDeclarations, Task<ITranspiler>> transpilerFactory)
    {
        if (transpilerFactory == null)
        {
            throw new ArgumentNullException(nameof(transpilerFactory));
        }

        if (this._transpilerFactory != null)
        {
            throw new InvalidOperationException("The session transpiler has already been configured.");
        }

        this._transpilerFactory = transpilerFactory;
        return this;
    }

    /// <summary>
    /// Selects the transpiler factory for the session being created.
    /// Use this overload when the transpiler does not need the session's <see cref="TypeDeclarations"/>.
    /// </summary>
    public DuetsSessionConfiguration UseTranspiler(Func<Task<ITranspiler>> transpilerFactory)
    {
        if (transpilerFactory == null)
        {
            throw new ArgumentNullException(nameof(transpilerFactory));
        }

        return this.UseTranspiler(_ => transpilerFactory());
    }

    internal Func<ITranspiler, IScriptEngine> GetRequiredEngineFactory()
    {
        return this._engineFactory
            ?? DuetsBackendRegistry.DefaultEngineFactory
            ?? throw new InvalidOperationException(
                "No script engine has been configured. " +
                "Use a backend extension package (e.g. Duets.Jint) to register an engine."
            );
    }

    internal Func<TypeDeclarations, Task<ITranspiler>> GetRequiredTranspilerFactory()
    {
        return this._transpilerFactory
            ?? DuetsBackendRegistry.DefaultTranspilerFactory
            ?? throw new InvalidOperationException(
                "No transpiler has been configured. " +
                "Use a backend extension package (e.g. Duets.Jint) to register a transpiler."
            );
    }
}
