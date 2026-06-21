using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Duets;

/// <summary>
/// Holds default engine and transpiler factories registered by backend packages via
/// <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>.
/// </summary>
public static class DuetsBackendRegistry
{
    // Guards the one-time eager probe of backend assemblies so it runs at most once.
    private static bool _probedBackendAssemblies;
    private static readonly object ProbeLock = new();

    internal static Func<ITranspiler, IScriptEngine>? DefaultEngineFactory { get; private set; }

    internal static Func<TypeDeclarations, Task<ITranspiler>>? DefaultTranspilerFactory
    {
        get;
        private set;
    }

    /// <summary>
    /// Registers the default engine factory. Intended to be called once from a backend
    /// package's module initializer. Throws if a default has already been registered.
    /// </summary>
    public static void RegisterDefaultEngine(Func<ITranspiler, IScriptEngine> factory)
    {
        if (DefaultEngineFactory != null)
        {
            throw new InvalidOperationException(
                "A default engine has already been registered. "
                    + "Call UseEngine() or UseJint() on DuetsSessionConfiguration to select an engine explicitly."
            );
        }

        DefaultEngineFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Registers the default transpiler factory. Intended to be called once from a backend
    /// package's module initializer. Throws if a default has already been registered.
    /// </summary>
    public static void RegisterDefaultTranspiler(Func<TypeDeclarations, Task<ITranspiler>> factory)
    {
        if (DefaultTranspilerFactory != null)
        {
            throw new InvalidOperationException(
                "A default transpiler has already been registered. "
                    + "Call UseTranspiler() or UseBabel() on DuetsSessionConfiguration to select a transpiler explicitly."
            );
        }

        DefaultTranspilerFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Forces candidate backend assemblies to load so their module initializers register
    /// defaults. A backend's <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>
    /// runs only once the CLR loads the assembly, which happens lazily on first use of one of its
    /// types. When application code references a backend solely through this core library (as in
    /// zero-config scenarios), nothing triggers that load, so the registry probes the deployment
    /// directory for <c>Duets.*.dll</c> backend assemblies and loads them on demand. Runs at most
    /// once per process.
    /// </summary>
    internal static void EnsureBackendAssembliesLoaded()
    {
        if (_probedBackendAssemblies)
        {
            return;
        }

        lock (ProbeLock)
        {
            if (_probedBackendAssemblies)
            {
                return;
            }

            _probedBackendAssemblies = true;

            // Once both defaults are present there is nothing left to discover.
            if (DefaultEngineFactory != null && DefaultTranspilerFactory != null)
            {
                return;
            }

            ProbeBackendAssemblies();
        }
    }

    private static void ProbeBackendAssemblies()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return;
        }

        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (name != null)
            {
                loaded.Add(name);
            }
        }

        foreach (var path in Directory.EnumerateFiles(baseDirectory, "Duets.*.dll"))
        {
            var simpleName = Path.GetFileNameWithoutExtension(path);

            // Skip the core library itself and obvious non-backends (tests, the sample CLI).
            if (
                !simpleName.StartsWith("Duets.", StringComparison.Ordinal)
                || simpleName.EndsWith(".Tests", StringComparison.Ordinal)
                || simpleName.Equals("Duets.Sandbox", StringComparison.Ordinal)
                || loaded.Contains(simpleName)
            )
            {
                continue;
            }

            try
            {
                ForceRunModuleInitializers(Assembly.Load(new AssemblyName(simpleName)));
            }
            catch (Exception ex)
                when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                // A non-loadable or unrelated Duets.*.dll is not a backend; ignore and continue.
            }

            // Stop early once a backend has supplied both defaults.
            if (DefaultEngineFactory != null && DefaultTranspilerFactory != null)
            {
                return;
            }
        }
    }

    private static void ForceRunModuleInitializers(Assembly assembly)
    {
        // Assembly.Load only brings in metadata; the CLR defers a module's initializer (the module
        // .cctor where [ModuleInitializer] methods are emitted) until the first access to a type or
        // member in that module. Run each module initializer explicitly so a backend referenced only
        // through this core library still registers its defaults.
        foreach (var module in assembly.GetModules())
        {
            RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);
        }
    }
}
