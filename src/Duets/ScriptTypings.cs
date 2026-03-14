using System.Reflection;
using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets;

/// <summary>Provides script-accessible built-in functions for managing TypeScript type declarations.</summary>
public sealed class ScriptTypings
{
    public ScriptTypings(TypeScriptService ts)
    {
        this._ts = ts;
    }

    private readonly TypeScriptService _ts;

    /// <summary>Registers a single .NET type by assembly-qualified name as a TypeScript declaration target.</summary>
    public void Use(string typeName)
    {
        var type = Type.GetType(typeName)
            ?? throw new InvalidOperationException($"Type not found: {typeName}");
        this._ts.RegisterType(type);
    }

    /// <summary>
    /// Loads an assembly and registers namespace skeleton declarations so that its namespaces
    /// appear in TypeScript completions. No type members are registered.
    /// </summary>
    public void ScanAssembly(string assemblyName)
    {
        var asm = Assembly.Load(new AssemblyName(assemblyName));
        foreach (var ns in asm.GetExportedTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns != null)
            .Distinct())
        {
            this._ts.RegisterNamespaceSkeleton(ns!);
        }
    }

    /// <summary>
    /// Loads an assembly and registers all public types as TypeScript declaration targets.
    /// </summary>
    public void UseAssembly(string assemblyName)
    {
        var asm = Assembly.Load(new AssemblyName(assemblyName));
        foreach (var type in TryGetExportedTypes(asm))
        {
            this._ts.RegisterType(type);
        }
    }

    /// <summary>
    /// Registers all public types in the given namespace as TypeScript declaration targets.
    /// Accepts either a Jint namespace reference (e.g., <c>typings.useNamespace(System.Net.Http)</c>)
    /// or a plain string (e.g., <c>typings.useNamespace("System.Net.Http")</c>).
    /// The namespace reference form requires the assembly to be accessible via <c>AllowClr</c>.
    /// </summary>
    public void UseNamespace(JsValue nsRef)
    {
        var ns = nsRef switch
        {
            NamespaceReference nr => GetNamespaceNameFromRef(nr),
            _ when nsRef.IsString() => nsRef.AsString(),
            _ => throw new ArgumentException(
                "Expected a namespace reference or string (e.g., typings.useNamespace(System.Net.Http) or typings.useNamespace(\"System.Net.Http\"))"
            ),
        };
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in TryGetExportedTypes(asm).Where(t => t.Namespace == ns))
            {
                this._ts.RegisterType(type);
            }
        }
    }

    private static string GetNamespaceNameFromRef(NamespaceReference nr)
    {
        try
        {
            return GetPath(nr);
        }
        catch (MissingFieldException ex)
        {
            throw new InvalidOperationException(
                "Cannot extract namespace name from Jint NamespaceReference: the internal '_path' field was not found. " +
                "This may indicate an incompatible version of Jint. Use the string overload instead: typings.useNamespace(\"System.Net.Http\").",
                ex
            );
        }
    }

    // NamespaceReference does not expose the namespace string via a public API.
    // Access the backing field directly via UnsafeAccessor (AOT-safe, no reflection).
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_path")]
    private static extern ref string GetPath(NamespaceReference nr);

    private static IEnumerable<Type> TryGetExportedTypes(Assembly asm)
    {
        try
        {
            return asm.GetExportedTypes();
        }
        catch
        {
            return [];
        }
    }
}
