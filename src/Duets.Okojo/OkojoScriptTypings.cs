using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Okojo;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.Runtime.Interop;

namespace Duets.Okojo;

/// <summary>Okojo-backed script built-ins for managing TypeScript type declarations.</summary>
internal sealed class OkojoScriptTypings : IHostBindable
{
    public OkojoScriptTypings(
        JsRealm realm,
        ITypeDeclarationRegistrar declarations,
        Action<string, Type>? exposeGlobal = null,
        Action<Type>? registerExtensionMethods = null)
    {
        this._realm = realm;
        this._declarations = declarations;
        this._exposeGlobal = exposeGlobal;
        this._registerExtensionMethods = registerExtensionMethods;
    }

    private static readonly HostBinding _binding = new(
        typeof(OkojoScriptTypings),
        [
            MethodBinding(
                "importNamespace",
                static (in info) =>
                {
                    var ret = info.GetThis<OkojoScriptTypings>().ImportNamespace(info.GetArgument<object>(0));
                    return info.Realm.WrapHostValue(ret);
                },
                1
            ),
            MethodBinding(
                "usingNamespace",
                static (in info) =>
                {
                    info.GetThis<OkojoScriptTypings>().UsingNamespace(info.GetArgument<object>(0));
                    return JsValue.Undefined;
                },
                1
            ),
            MethodBinding(
                "importType",
                static (in info) =>
                {
                    info.GetThis<OkojoScriptTypings>().ImportType(info.GetArgument<object>(0));
                    return JsValue.Undefined;
                },
                1
            ),
            MethodBinding(
                "scanAssembly",
                static (in info) =>
                {
                    info.GetThis<OkojoScriptTypings>().ScanAssembly(info.GetArgument<object>(0));
                    return JsValue.Undefined;
                },
                1
            ),
            MethodBinding(
                "scanAssemblyOf",
                static (in info) =>
                {
                    info.GetThis<OkojoScriptTypings>().ScanAssemblyOf(info.GetArgument<object>(0));
                    return JsValue.Undefined;
                },
                1
            ),
            MethodBinding(
                "importAssembly",
                static (in info) =>
                {
                    info.GetThis<OkojoScriptTypings>().ImportAssembly(info.GetArgument<object>(0));
                    return JsValue.Undefined;
                },
                1
            ),
            MethodBinding(
                "importAssemblyOf",
                static (in info) =>
                {
                    info.GetThis<OkojoScriptTypings>().ImportAssemblyOf(info.GetArgument<object>(0));
                    return JsValue.Undefined;
                },
                1
            ),
            MethodBinding(
                "addExtensionMethods",
                static (in info) =>
                {
                    info.GetThis<OkojoScriptTypings>().AddExtensionMethods(info.GetArgument<object>(0));
                    return JsValue.Undefined;
                },
                1
            ),
        ],
        []
    );

    private static readonly Lazy<Func<object, string?>> _namespacePathAccessor = new(CreateNamespacePathAccessor);
    private static readonly Lazy<Func<object, Type?>> _clrTypeAccessor = new(CreateClrTypeAccessor);

    private readonly JsRealm _realm;
    private readonly ITypeDeclarationRegistrar _declarations;
    private readonly Action<string, Type>? _exposeGlobal;
    private readonly Action<Type>? _registerExtensionMethods;

    internal static bool TryExtractClrType(object? value, out Type type)
    {
        switch (value)
        {
            case Type directType:
                type = directType;
                return true;
            case JsHostObject { Data: Type hostType }:
                type = hostType;
                return true;
            case JsHostFunction hostFunction:
                var clrType = hostFunction.UserData is null ? null : _clrTypeAccessor.Value(hostFunction.UserData);
                if (clrType is not null)
                {
                    type = clrType;
                    return true;
                }

                break;
        }

        type = null!;
        return false;
    }

    public object ImportNamespace(object nsRef)
    {
        var ns = ResolveNamespace(nsRef, "typings.importNamespace");
        this.RegisterNamespaceTypes(ns);
        return this._realm.GetClrNamespace(ns);
    }

    public void UsingNamespace(object nsRef)
    {
        var ns = ResolveNamespace(nsRef, "typings.usingNamespace");
        var types = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(asm => TryGetExportedTypes(asm).Where(t => t.Namespace == ns))
            .ToList();

        foreach (var type in types)
        {
            this._declarations.RegisterType(type);
        }

        if (this._exposeGlobal is null)
        {
            return;
        }

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

    public void ImportType(object typeRef)
    {
        var type = ResolveTypeRef(
            typeRef,
            "Expected a CLR type reference (e.g., typings.importType(System.IO.File)) or an assembly-qualified name string."
        );
        this._declarations.RegisterType(type);
    }

    public void ScanAssembly(object assemblyRef)
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

    public void ScanAssemblyOf(object typeRef)
    {
        this.ScanAssembly(ResolveTypeRef(typeRef, "Expected a CLR type reference (e.g., typings.scanAssemblyOf(System.IO.File)).").Assembly);
    }

    public void ImportAssembly(object assemblyRef)
    {
        var asm = ResolveAssembly(assemblyRef);
        foreach (var type in TryGetExportedTypes(asm))
        {
            this._declarations.RegisterType(type);
        }
    }

    public void ImportAssemblyOf(object typeRef)
    {
        this.ImportAssembly(ResolveTypeRef(typeRef, "Expected a CLR type reference (e.g., typings.importAssemblyOf(System.IO.File)).").Assembly);
    }

    public void AddExtensionMethods(object typeRef)
    {
        if (this._registerExtensionMethods is null)
        {
            throw new InvalidOperationException(
                "typings.addExtensionMethods requires the engine to be configured with AllowClrAccess " +
                "and the built-in type registrar (RegisterTypeBuiltins)."
            );
        }

        var type = ResolveTypeRef(
            typeRef,
            "Expected a CLR type reference (e.g. typings.addExtensionMethods(System.Linq.Enumerable)) " +
            "or an assembly-qualified name string."
        );
        this._registerExtensionMethods(type);
    }

    public HostBinding GetHostBinding()
    {
        return _binding;
    }

    private static string ResolveNamespace(object nsRef, string apiName)
    {
        if (nsRef is string ns)
        {
            return ns;
        }

        var path = _namespacePathAccessor.Value(nsRef);
        if (!string.IsNullOrEmpty(path))
        {
            return path!;
        }

        throw new ArgumentException(
            $"Expected a namespace name string (e.g. {apiName}('System.IO')) or a CLR namespace reference."
        );
    }

    private static Type ResolveTypeRef(object typeRef, string message)
    {
        if (TryExtractClrType(typeRef, out var clrType))
        {
            return clrType;
        }

        if (typeRef is string typeName)
        {
            return Type.GetType(typeName)
                ?? throw new InvalidOperationException($"Type not found: {typeName}");
        }

        throw new ArgumentException(message);
    }

    private static Assembly ResolveAssembly(object assemblyRef)
    {
        return assemblyRef switch
        {
            Assembly assembly => assembly,
            string assemblyName => Assembly.Load(new AssemblyName(assemblyName)),
            _ => throw new ArgumentException(
                "Expected an assembly name string or an Assembly object (e.g., typings.scanAssembly(\"System.Net.Http\"))."
            ),
        };
    }

    private static IEnumerable<Type> TryGetExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null)!;
        }
    }

    private static HostMemberBinding MethodBinding(string name, JsHostFunctionBody body, int functionLength)
    {
        return new HostMemberBinding(name, HostMemberBindingKind.Method, false, methodBody: body, functionLength: functionLength);
    }

    private static Func<object, string?> CreateNamespacePathAccessor()
    {
        return CreatePropertyAccessor<string>("NamespacePath");
    }

    private static Func<object, Type?> CreateClrTypeAccessor()
    {
        return CreatePropertyAccessor<Type>("ClrType");
    }

    private static Func<object, T?> CreatePropertyAccessor<T>(string propertyName)
        where T : class
    {
        // Okojo does not currently expose a stable public API for reading CLR metadata
        // (namespace path or CLR type reference) from its runtime objects.
        // This method uses cached reflection against public properties that exist in the
        // current Okojo implementation (NamespacePath on namespace refs, ClrType on
        // JsHostFunction.UserData). If an Okojo upgrade removes or renames these,
        // callers receive null and fall through to throwing ArgumentException, keeping
        // the string overloads of the typings.* API functional as a fallback.
        // When upgrading Okojo: verify that NamespacePath and ClrType are still accessible,
        // or replace this seam with the public API Okojo provides at that point.
        var properties = new ConcurrentDictionary<Type, PropertyInfo?>();

        return target =>
        {
            var property = properties.GetOrAdd(
                target.GetType(),
                type => type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            );
            return property?.GetValue(target) as T;
        };
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
}
