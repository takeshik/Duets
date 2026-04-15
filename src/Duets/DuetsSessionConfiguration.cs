namespace Duets;

/// <summary>
/// Collects configuration needed to create a <see cref="DuetsSession"/>.
/// Backend packages extend this type with runtime-specific convenience methods.
/// </summary>
public sealed class DuetsSessionConfiguration
{
    internal DuetsSessionConfiguration()
    {
    }

    private Func<ITranspiler, ScriptEngine>? _engineFactory;

    /// <summary>
    /// Selects the script engine factory for the session being created.
    /// Intended for advanced callers and backend package extensions.
    /// </summary>
    public DuetsSessionConfiguration UseEngine(Func<ITranspiler, ScriptEngine> engineFactory)
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

    internal Func<ITranspiler, ScriptEngine> GetRequiredEngineFactory()
    {
        return this._engineFactory
            ?? throw new InvalidOperationException(
                "No script engine has been configured. " +
                "Use a backend extension package (e.g. Duets.Jint) to register an engine."
            );
    }
}
