namespace Duets;

/// <summary>
/// Holds default engine and transpiler factories registered by backend packages via
/// <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>.
/// </summary>
public static class DuetsBackendRegistry
{
    internal static Func<ITranspiler, ScriptEngine>? DefaultEngineFactory { get; private set; }

    internal static Func<TypeDeclarations, Task<ITranspiler>>? DefaultTranspilerFactory { get; private set; }

    /// <summary>
    /// Registers the default engine factory. Intended to be called once from a backend
    /// package's module initializer. Throws if a default has already been registered.
    /// </summary>
    public static void RegisterDefaultEngine(Func<ITranspiler, ScriptEngine> factory)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (DefaultEngineFactory != null)
        {
            throw new InvalidOperationException(
                "A default engine has already been registered. " +
                "Call UseEngine() or UseJint() on DuetsSessionConfiguration to select an engine explicitly."
            );
        }

        DefaultEngineFactory = factory;
    }

    /// <summary>
    /// Registers the default transpiler factory. Intended to be called once from a backend
    /// package's module initializer. Throws if a default has already been registered.
    /// </summary>
    public static void RegisterDefaultTranspiler(Func<TypeDeclarations, Task<ITranspiler>> factory)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (DefaultTranspilerFactory != null)
        {
            throw new InvalidOperationException(
                "A default transpiler has already been registered. " +
                "Call UseTranspiler() or UseBabel() on DuetsSessionConfiguration to select a transpiler explicitly."
            );
        }

        DefaultTranspilerFactory = factory;
    }
}
