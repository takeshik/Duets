using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
#if !NETSTANDARD2_1
using System.Runtime.CompilerServices;
#endif

namespace Duets;

/// <summary>Provides script-accessible built-in functions for managing TypeScript type declarations.</summary>
public sealed class ScriptTypings
{
    public ScriptTypings(TypeScriptService ts, Func<JsValue, JsValue>? importNamespace = null)
    {
        this._ts = ts;
        this._importNamespace = importNamespace;
    }

    private readonly TypeScriptService _ts;
    private readonly Func<JsValue, JsValue>? _importNamespace;

    /// <summary>
    /// Imports a .NET namespace into the script environment and registers its types as TypeScript declaration targets.
    /// Accepts either a namespace name string or an existing namespace reference (e.g. <c>System.IO</c>).
    /// When passed a string, delegates to the Jint-provided <c>importNamespace</c> to obtain the reference,
    /// then calls <see cref="UseNamespace"/> to register type declarations.
    /// </summary>
    /// <param name="nsRef">
    /// A namespace name string (e.g. <c>"System.IO"</c>), or a namespace reference
    /// (e.g. <c>System.IO</c> — requires <c>AllowClr</c> on the engine).
    /// </param>
    /// <returns>The namespace reference returned by <c>importNamespace</c>, or the original value if already a reference.</returns>
    public JsValue ImportNamespace(JsValue nsRef)
    {
        if (nsRef is NamespaceReference)
        {
            this.UseNamespace(nsRef);
            return nsRef;
        }

        if (nsRef.IsString())
        {
            if (this._importNamespace is null)
            {
                throw new InvalidOperationException(
                    "typings.importNamespace with a string argument requires AllowClr to be configured on the engine."
                );
            }

            var result = this._importNamespace(nsRef);
            this.UseNamespace(result);
            return result;
        }

        throw new ArgumentException(
            "Expected a namespace name string (e.g. typings.importNamespace('System.IO')) " +
            "or a namespace reference (e.g. typings.importNamespace(System.IO))."
        );
    }

    /// <summary>Registers a single .NET type as a TypeScript declaration target.</summary>
    /// <param name="typeRef">An assembly-qualified type name string, or a CLR type reference (e.g. <c>System.IO.File</c>).</param>
    public void UseType(JsValue typeRef)
    {
        var type = typeRef switch
        {
            TypeReference tr => tr.ReferenceType,
            _ when typeRef.IsString() => Type.GetType(typeRef.AsString())
                ?? throw new InvalidOperationException($"Type not found: {typeRef.AsString()}"),
            _ => throw new ArgumentException(
                "Expected a CLR type reference (e.g., typings.useType(System.IO.File)) or an assembly-qualified name string."
            ),
        };
        this._ts.RegisterType(type);
    }

    /// <summary>
    /// Loads an assembly and registers namespace skeleton declarations so that its namespaces
    /// appear in TypeScript completions. No type members are registered.
    /// </summary>
    /// <param name="assemblyRef">An assembly name string or a wrapped <see cref="Assembly"/> object.</param>
    public void ScanAssembly(JsValue assemblyRef)
    {
        var asm = ResolveAssembly(assemblyRef);
        foreach (var ns in asm.GetExportedTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns != null)
            .Distinct())
        {
            this._ts.RegisterNamespaceSkeleton(ns!);
        }
    }

    /// <summary>
    /// Scans the assembly containing the given type and registers namespace skeleton declarations.
    /// No type members are registered.
    /// </summary>
    public void ScanAssemblyOf(JsValue typeRef)
    {
        this.ScanAssembly(new JsString(ResolveTypeRef(typeRef).Assembly.FullName!));
    }

    /// <summary>
    /// Loads an assembly and registers all public types as TypeScript declaration targets.
    /// </summary>
    /// <param name="assemblyRef">An assembly name string or a wrapped <see cref="Assembly"/> object.</param>
    public void UseAssembly(JsValue assemblyRef)
    {
        var asm = ResolveAssembly(assemblyRef);
        foreach (var type in TryGetExportedTypes(asm))
        {
            this._ts.RegisterType(type);
        }
    }

    /// <summary>
    /// Registers all public types from the assembly containing the given type as TypeScript declaration targets.
    /// </summary>
    public void UseAssemblyOf(JsValue typeRef)
    {
        this.UseAssembly(new JsString(ResolveTypeRef(typeRef).Assembly.FullName!));
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

    private static Type ResolveTypeRef(JsValue typeRef)
    {
        if (typeRef is TypeReference tr) return tr.ReferenceType;
        throw new ArgumentException(
            "Expected a CLR type reference (e.g., typings.scanAssemblyOf(System.IO.File))."
        );
    }

    private static Assembly ResolveAssembly(JsValue assemblyRef)
    {
        return assemblyRef switch
        {
            ObjectWrapper { Target: Assembly asm } => asm,
            _ when assemblyRef.IsString() => Assembly.Load(new AssemblyName(assemblyRef.AsString())),
            _ => throw new ArgumentException(
                "Expected an assembly name string or an Assembly object (e.g., typings.scanAssembly(\"System.Net.Http\"))."
            ),
        };
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
#if NETSTANDARD2_1
    // Use reflection as a fallback on platforms where UnsafeAccessor is unavailable (.NET 8+).
    private static string GetPath(NamespaceReference nr)
    {
        try
        {
            var field = typeof(NamespaceReference).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance);
            return (string) field!.GetValue(nr)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Cannot extract namespace name from Jint NamespaceReference: the internal '_path' field was not found. " +
                "This may indicate an incompatible version of Jint. Use the string overload instead: typings.useNamespace(\"System.Net.Http\").",
                ex
            );
        }
    }
#else
    // Access the backing field directly via UnsafeAccessor (AOT-safe, no reflection).
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_path")]
    private static extern ref string GetPath(NamespaceReference nr);
#endif

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
