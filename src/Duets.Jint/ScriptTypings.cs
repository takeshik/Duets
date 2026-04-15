using System.Reflection;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
#if !NETSTANDARD2_1
using System.Runtime.CompilerServices;
#endif

namespace Duets.Jint;

/// <summary>Provides script-accessible built-in functions for managing TypeScript type declarations.</summary>
internal sealed class ScriptTypings
{
    public ScriptTypings(
        ITypeDeclarationRegistrar declarations,
        Func<JsValue, JsValue>? importNamespace = null,
        Action<string, Type>? exposeGlobal = null,
        Action<Type>? registerExtensionMethods = null)
    {
        this._declarations = declarations;
        this._importNamespace = importNamespace;
        this._exposeGlobal = exposeGlobal;
        this._registerExtensionMethods = registerExtensionMethods;
    }

    private readonly ITypeDeclarationRegistrar _declarations;
    private readonly Func<JsValue, JsValue>? _importNamespace;
    private readonly Action<string, Type>? _exposeGlobal;
    private readonly Action<Type>? _registerExtensionMethods;

    /// <summary>
    /// Imports a .NET namespace into the script environment and registers its types as TypeScript declaration targets.
    /// Accepts either a namespace name string or an existing namespace reference (e.g. <c>System.IO</c>).
    /// When passed a string, delegates to the Jint-provided <c>importNamespace</c> to obtain the reference,
    /// then registers the namespace's types as TypeScript declarations.
    /// </summary>
    /// <param name="nsRef">
    /// A namespace name string (e.g. <c>"System.IO"</c>), or a namespace reference
    /// (e.g. <c>System.IO</c> — requires <c>AllowClr</c> on the engine).
    /// </param>
    /// <returns>The namespace reference returned by <c>importNamespace</c>, or the original value if already a reference.</returns>
    public JsValue ImportNamespace(JsValue nsRef)
    {
        if (nsRef is NamespaceReference nr)
        {
            this.RegisterNamespaceTypes(GetNamespaceNameFromRef(nr));
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
            this.RegisterNamespaceTypes(nsRef.AsString());
            return result;
        }

        throw new ArgumentException(
            "Expected a namespace name string (e.g. typings.importNamespace('System.IO')) " +
            "or a namespace reference (e.g. typings.importNamespace(System.IO))."
        );
    }

    /// <summary>
    /// Equivalent to C#'s <c>using System.IO;</c> — imports a .NET namespace, registers its types as TypeScript
    /// declaration targets, and exposes each type as a global variable so they can be referenced without the
    /// namespace prefix (e.g. <c>new FileInfo('path')</c> instead of <c>new System.IO.FileInfo('path')</c>).
    /// Accepts either a namespace name string or an existing namespace reference.
    /// </summary>
    /// <param name="nsRef">
    /// A namespace name string (e.g. <c>"System.IO"</c>), or a namespace reference
    /// (e.g. <c>System.IO</c> — requires <c>AllowClr</c> on the engine).
    /// </param>
    public void UsingNamespace(JsValue nsRef)
    {
        string ns;

        if (nsRef is NamespaceReference nr)
        {
            ns = GetNamespaceNameFromRef(nr);
        }
        else if (nsRef.IsString())
        {
            if (this._importNamespace is null)
            {
                throw new InvalidOperationException(
                    "typings.usingNamespace with a string argument requires AllowClr to be configured on the engine."
                );
            }

            this._importNamespace(nsRef);
            ns = nsRef.AsString();
        }
        else
        {
            throw new ArgumentException(
                "Expected a namespace name string (e.g. typings.usingNamespace('System.IO')) " +
                "or a namespace reference (e.g. typings.usingNamespace(System.IO))."
            );
        }

        var types = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(asm => TryGetExportedTypes(asm).Where(t => t.Namespace == ns))
            .ToList();

        foreach (var type in types)
        {
            this._declarations.RegisterType(type);
        }

        if (this._exposeGlobal != null)
        {
            var sb = new StringBuilder();
            foreach (var type in types.Where(t => !t.IsNested))
            {
                var scriptName = ClrDeclarationGenerator.GetScriptName(type);
                this._exposeGlobal(scriptName, type);
                sb.AppendLine($"declare var {scriptName}: typeof {ns}.{scriptName};");
            }

            if (sb.Length > 0)
            {
                this._declarations.RegisterDeclaration(sb.ToString());
            }
        }
    }

    /// <summary>Registers a single .NET type as a TypeScript declaration target.</summary>
    /// <param name="typeRef">An assembly-qualified type name string, or a CLR type reference (e.g. <c>System.IO.File</c>).</param>
    public void ImportType(JsValue typeRef)
    {
        var type = typeRef switch
        {
            TypeReference tr => tr.ReferenceType,
            _ when typeRef.IsString() => Type.GetType(typeRef.AsString())
                ?? throw new InvalidOperationException($"Type not found: {typeRef.AsString()}"),
            _ => throw new ArgumentException(
                "Expected a CLR type reference (e.g., typings.importType(System.IO.File)) or an assembly-qualified name string."
            ),
        };
        this._declarations.RegisterType(type);
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
            this._declarations.RegisterNamespace(ns!);
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
    public void ImportAssembly(JsValue assemblyRef)
    {
        var asm = ResolveAssembly(assemblyRef);
        foreach (var type in TryGetExportedTypes(asm))
        {
            this._declarations.RegisterType(type);
        }
    }

    /// <summary>
    /// Registers all public types from the assembly containing the given type as TypeScript declaration targets.
    /// </summary>
    public void ImportAssemblyOf(JsValue typeRef)
    {
        this.ImportAssembly(new JsString(ResolveTypeRef(typeRef).Assembly.FullName!));
    }

    /// <summary>
    /// Registers a static class containing extension methods so that they appear as instance-method
    /// completions on their target types and are callable at runtime.
    /// </summary>
    /// <param name="typeRef">A CLR type reference pointing to the static class that defines the extension methods.</param>
    public void AddExtensionMethods(JsValue typeRef)
    {
        if (this._registerExtensionMethods is null)
        {
            throw new InvalidOperationException(
                "typings.addExtensionMethods requires the engine to be configured with AllowClr " +
                "and the built-in type registrar (RegisterTypeBuiltins)."
            );
        }

        var type = typeRef switch
        {
            TypeReference tr => tr.ReferenceType,
            _ when typeRef.IsString() => Type.GetType(typeRef.AsString())
                ?? throw new InvalidOperationException($"Type not found: {typeRef.AsString()}"),
            _ => throw new ArgumentException(
                "Expected a CLR type reference (e.g. typings.addExtensionMethods(System.Linq.Enumerable)) " +
                "or an assembly-qualified name string."
            ),
        };

        this._registerExtensionMethods(type);
    }

    private void RegisterNamespaceTypes(string ns)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in TryGetExportedTypes(asm).Where(t => t.Namespace == ns))
            {
                this._declarations.RegisterType(type);
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
                "This may indicate an incompatible version of Jint. Use the string overload instead: typings.usingNamespace(\"System.Net.Http\").",
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
                "This may indicate an incompatible version of Jint. Use the string overload instead: typings.usingNamespace(\"System.Net.Http\").",
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
