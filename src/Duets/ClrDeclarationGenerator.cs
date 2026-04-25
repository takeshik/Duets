using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Duets;

/// <summary>
/// Generates TypeScript declaration (<c>.d.ts</c>) source from .NET types via reflection.
/// </summary>
public class ClrDeclarationGenerator
{
    /// <summary>Initializes a new instance with an optional JSDoc provider.</summary>
    public ClrDeclarationGenerator(IJsDocProvider? jsDocProvider = null)
    {
        this._jsDocProvider = jsDocProvider;
    }

    private static readonly HashSet<Type> _numericTypes =
    [
        typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort),
        typeof(int), typeof(uint),
        typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
    ];

    private static readonly Type[] _arrayProjectionNamedTypes =
    [
        typeof(IEnumerable<>),
        typeof(IList<>),
        typeof(IReadOnlyList<>),
        typeof(List<>),
    ];

    private static readonly Type[] _dictionaryProjectionNamedTypes =
    [
        typeof(IDictionary<,>),
        typeof(Dictionary<,>),
    ];

    private readonly IJsDocProvider? _jsDocProvider;

    /// <summary>
    /// Returns the bare TypeScript identifier name for a CLR type — the simple name with any
    /// backtick arity suffix removed (e.g. <c>List`1</c> → <c>List</c>).
    /// This is the name used in global variable bindings and <c>declare var</c> declarations.
    /// </summary>
    public static string GetScriptName(Type type)
    {
        var backtickIdx = type.Name.IndexOf('`');
        return backtickIdx >= 0 ? type.Name[..backtickIdx] : type.Name;
    }

    /// <summary>
    /// Generates TypeScript type declaration (.d.ts) source for the specified .NET type.
    /// Types with a namespace are wrapped in a declare namespace block.
    /// Unsupported types and members fall back to any or are omitted from the output.
    /// </summary>
    public string GenerateTypeDefTs(Type targetType)
    {
        var sb = new StringBuilder();
        var visited = new HashSet<Type>();
        this.WriteDeclaration(sb, targetType, visited);
        return sb.ToString();
    }

    /// <summary>
    /// Generates TypeScript interface augmentation declarations for all extension methods
    /// defined in <paramref name="containerType"/>, grouped by their target (first-parameter) type.
    /// </summary>
    public string GenerateExtensionMethodsTs(Type containerType)
    {
        var sb = new StringBuilder();
        var visited = new HashSet<Type>();

        var groups = containerType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.IsDefined(typeof(ExtensionAttribute), false))
            .GroupBy(m =>
                {
                    var p = m.GetParameters()[0].ParameterType;
                    return p.IsGenericType && !p.IsGenericTypeDefinition
                        ? p.GetGenericTypeDefinition()
                        : p;
                }
            );

        foreach (var group in groups)
        {
            foreach (var target in GetAugmentationTargets(group.Key))
            {
                this.WriteExtensionAugmentation(sb, group.Key, target, [.. group], visited);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the method signature string. Returns null if the method cannot be represented.
    /// </summary>
    private static string? TryBuildMethodSignature(MethodInfo method, HashSet<Type> visited)
    {
        var returnTs = MapType(method.ReturnType, visited);
        var paramParts = new List<string>();

        foreach (var param in method.GetParameters())
        {
            if (param.ParameterType.IsByRef || param.ParameterType.IsPointer)
            {
                return null; // ref/out/pointer parameters are not supported
            }

            var paramTs = MapType(param.ParameterType, visited);
            paramParts.Add($"{SanitizeParamName(param.Name ?? $"arg{param.Position}")}: {paramTs}");
        }

        // Type parameters for generic methods
        var typeParams = "";
        if (method.IsGenericMethodDefinition)
        {
            var tps = string.Join(", ", method.GetGenericArguments().Select(a => a.Name));
            typeParams = $"<{tps}>";
        }

        return $"{method.Name}{typeParams}({string.Join(", ", paramParts)}): {returnTs}";
    }

    /// <summary>
    /// Maps a .NET type to a TypeScript type string. Returns "any" for unsupported types.
    /// </summary>
    private static string MapType(Type type, HashSet<Type> visited)
    {
        // Generic type parameters (T, TKey, etc.) are used as-is by name
        if (type.IsGenericTypeParameter || type.IsGenericMethodParameter) return type.Name;

        // void
        if (type == typeof(void)) return "void";

        // Nullable<T>
        var nullableUnderlying = Nullable.GetUnderlyingType(type);
        if (nullableUnderlying != null)
        {
            var inner = MapType(nullableUnderlying, visited);
            return inner == "any" ? "any" : $"{inner} | null";
        }

        // Primitives
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (_numericTypes.Contains(type)) return "number";

        if (TryGetTaskResultSlot(type, out var taskResult))
        {
            return taskResult is null
                ? "Promise<void>"
                : $"Promise<{MapType(taskResult, visited)}>";
        }

        if (TryGetArrayProjectionSlot(type, out var arrayElement))
        {
            return $"{MapType(arrayElement, visited)}[]";
        }

        if (type == typeof(Action)) return "() => void";

        if (TryGetDictionaryProjectionSlots(type, out var keyType, out var valueType))
        {
            var keyTs = MapType(keyType, visited);
            var valTs = MapType(valueType, visited);
            if (keyTs != "any" && keyTs != "number" && keyTs != "string")
            {
                return "any";
            }

            return $"{{ [key: {keyTs}]: {valTs} }}";
        }

        if (TryFormatDelegateType(type, visited, MapType, out var delegateTs))
        {
            return delegateTs;
        }

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments()
                .Select(a => MapType(a, visited))
                .ToArray();

            if (type.Namespace != null)
            {
                return $"{type.Namespace}.{GetScriptName(def)}<{string.Join(", ", args)}>";
            }
        }

        return MapNamedType(type);
    }

    /// <summary>Builds the type header including generic type parameters.</summary>
    private static string BuildTypeHeader(Type type)
    {
        if (!type.IsGenericTypeDefinition) return type.Name;
        var name = GetScriptName(type);
        var typeParams = string.Join(", ", type.GetGenericArguments().Select(a => a.Name));
        return $"{name}<{typeParams}>";
    }

    /// <summary>Builds the type name with actual type arguments, used in extends clauses and similar contexts.</summary>
    private static string BuildSimpleTypeName(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var def = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
        var name = GetScriptName(def);
        var args = string.Join(", ", type.GetGenericArguments().Select(a => MapType(a, [])));
        return $"{name}<{args}>";
    }

    private static void WriteJsDocBlock(StringBuilder sb, string indent, string body)
    {
        var lines = body.Split('\n');
        var trimmed = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (trimmed.Length == 0) return;

        if (trimmed.Length == 1)
        {
            sb.AppendLine($"{indent}/** {trimmed[0]} */");
        }
        else
        {
            sb.AppendLine($"{indent}/**");
            foreach (var line in trimmed)
            {
                sb.AppendLine($"{indent} * {line}");
            }

            sb.AppendLine($"{indent} */");
        }
    }

    private static List<ExtensionAugmentationTarget> GetAugmentationTargets(Type targetType)
    {
        var targets = new List<ExtensionAugmentationTarget>();
        var added = new HashSet<string>(StringComparer.Ordinal);

        void Add(ExtensionAugmentationTarget target)
        {
            var key = $"{target.Namespace}|{target.ScriptTypeName}|{target.TypeParamStr}";
            if (added.Add(key))
            {
                targets.Add(target);
            }
        }

        if (TryCreateClrAugmentationTarget(targetType, out var directTarget))
        {
            Add(directTarget);
        }

        foreach (var alias in GetNamedProjectionAliases(targetType))
        {
            if (TryCreateClrAugmentationTarget(alias, out var aliasTarget))
            {
                Add(aliasTarget);
            }
        }

        if (TryCreateProjectedAugmentationTarget(targetType, out var projectedTarget))
        {
            Add(projectedTarget);
        }

        return targets;
    }

    private static IEnumerable<Type> GetNamedProjectionAliases(Type targetType)
    {
        if (!TryGetProjectionAliasFamily(targetType, out var family))
        {
            return [];
        }

        return family.Where(candidate => candidate != GetOpenType(targetType) && IsAssignableToReceiver(targetType, candidate));
    }

    private static bool TryGetProjectionAliasFamily(Type type, out Type[] family)
    {
        if (TryGetArrayProjectionSlot(type, out _))
        {
            family = _arrayProjectionNamedTypes;
            return true;
        }

        if (TryGetDictionaryProjectionSlots(type, out _, out _))
        {
            family = _dictionaryProjectionNamedTypes;
            return true;
        }

        family = [];
        return false;
    }

    private static bool IsAssignableToReceiver(Type receiverType, Type candidateType)
    {
        var receiver = GetOpenType(receiverType);
        var candidate = GetOpenType(candidateType);

        if (receiver == candidate) return true;

        if (receiver.IsGenericTypeDefinition && candidate.IsGenericTypeDefinition)
        {
            var arity = receiver.GetGenericArguments().Length;
            if (candidate.GetGenericArguments().Length != arity) return false;

            try
            {
                var sampleArgs = Enumerable.Repeat(typeof(object), arity).ToArray();
                var closedReceiver = receiver.MakeGenericType(sampleArgs);
                var closedCandidate = candidate.MakeGenericType(sampleArgs);
                return closedReceiver.IsAssignableFrom(closedCandidate);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return receiver.IsAssignableFrom(candidate);
    }

    private static bool TryCreateProjectedAugmentationTarget(Type targetType, out ExtensionAugmentationTarget target)
    {
        if (TryGetAugmentableProjectedType(targetType, out var projectedKind, out var typeParamNames))
        {
            target = new ExtensionAugmentationTarget(
                null,
                projectedKind switch
                {
                    ProjectedTypeKind.Array => "Array",
                    ProjectedTypeKind.Promise => "Promise",
                    _ => throw new InvalidOperationException("Unexpected projected augmentation target."),
                },
                BuildTypeParamString(typeParamNames),
                typeParamNames,
                false
            );
            return true;
        }

        target = default;
        return false;
    }

    private static bool TryCreateClrAugmentationTarget(Type targetType, out ExtensionAugmentationTarget target)
    {
        if (targetType.IsArray)
        {
            target = default;
            return false;
        }

        var openType = GetOpenType(targetType);
        var typeParamNames = openType.IsGenericTypeDefinition
            ? openType.GetGenericArguments().Select(t => t.Name).ToArray()
            : [];
        target = new ExtensionAugmentationTarget(
            openType.Namespace,
            GetScriptName(openType),
            BuildTypeParamString(typeParamNames),
            typeParamNames,
            openType.Namespace is null
        );
        return true;
    }

    private static string BuildTypeParamString(string[] typeParamNames)
    {
        return typeParamNames.Length > 0
            ? $"<{string.Join(", ", typeParamNames)}>"
            : "";
    }

    private static string? TryBuildExtensionMethodSignature(
        MethodInfo method, Dictionary<Type, string> substitution, HashSet<Type> visited)
    {
        // Skip 'this' (first) parameter
        var parameters = method.GetParameters().Skip(1).ToArray();

        var paramParts = new List<string>();
        foreach (var param in parameters)
        {
            if (param.ParameterType.IsByRef || param.ParameterType.IsPointer)
            {
                return null;
            }

            paramParts.Add(
                $"{SanitizeParamName(param.Name ?? $"arg{param.Position}")}: " +
                $"{MapTypeWithSubstitution(param.ParameterType, substitution, visited)}"
            );
        }

        var returnTs = MapTypeWithSubstitution(method.ReturnType, substitution, visited);

        // Method-level type params: generic args NOT already mapped by the substitution
        var methodTypeParams = method.IsGenericMethodDefinition
            ? method.GetGenericArguments().Where(t => !substitution.ContainsKey(t)).ToList()
            : [];

        var typeParamStr = methodTypeParams.Count > 0
            ? $"<{string.Join(", ", methodTypeParams.Select(t => t.Name))}>"
            : "";

        return $"{method.Name}{typeParamStr}({string.Join(", ", paramParts)}): {returnTs}";
    }

    /// <summary>
    /// Like <see cref="MapType"/> but applies <paramref name="substitution"/> to generic type
    /// parameters before mapping, so that e.g. <c>TSource</c> becomes the interface's own <c>T</c>.
    /// </summary>
    private static string MapTypeWithSubstitution(
        Type type, Dictionary<Type, string> substitution, HashSet<Type> visited)
    {
        if (type.IsGenericParameter)
        {
            return substitution.TryGetValue(type, out var s) ? s : type.Name;
        }

        if (type == typeof(void)) return "void";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (_numericTypes.Contains(type)) return "number";
        if (type == typeof(Action)) return "() => void";

        var nullableUnderlying = Nullable.GetUnderlyingType(type);
        if (nullableUnderlying != null)
        {
            var inner = MapTypeWithSubstitution(nullableUnderlying, substitution, visited);
            return inner == "any" ? "any" : $"{inner} | null";
        }

        if (TryGetTaskResultSlot(type, out var taskResult))
        {
            return taskResult is null
                ? "Promise<void>"
                : $"Promise<{MapTypeWithSubstitution(taskResult, substitution, visited)}>";
        }

        if (TryGetArrayProjectionSlot(type, out var arrayElement))
        {
            return $"{MapTypeWithSubstitution(arrayElement, substitution, visited)}[]";
        }

        if (TryGetDictionaryProjectionSlots(type, out var keyType, out var valueType))
        {
            var mappedKey = MapTypeWithSubstitution(keyType, substitution, visited);
            var mappedValue = MapTypeWithSubstitution(valueType, substitution, visited);
            return mappedKey is "string" or "number"
                ? $"{{ [key: {mappedKey}]: {mappedValue} }}"
                : "any";
        }

        if (TryFormatDelegateType(
                type,
                visited,
                (innerType, innerVisited) => MapTypeWithSubstitution(innerType, substitution, innerVisited),
                out var delegateTs
            ))
        {
            return delegateTs;
        }

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments()
                .Select(a => MapTypeWithSubstitution(a, substitution, visited))
                .ToArray();

            if (type.Namespace != null)
            {
                return $"{type.Namespace}.{GetScriptName(def)}<{string.Join(", ", args)}>";
            }

            return "any";
        }

        return MapNamedType(type);
    }

    private static bool TryGetArrayProjectionSlot(Type type, out Type elementType)
    {
        if (type.IsArray && type.GetArrayRank() == 1)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (_arrayProjectionNamedTypes.Contains(def))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }

        if (type.IsGenericTypeDefinition && _arrayProjectionNamedTypes.Contains(type))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static bool TryGetDictionaryProjectionSlots(Type type, out Type keyType, out Type valueType)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (_dictionaryProjectionNamedTypes.Contains(def))
            {
                var args = type.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        if (type.IsGenericTypeDefinition && _dictionaryProjectionNamedTypes.Contains(type))
        {
            var args = type.GetGenericArguments();
            keyType = args[0];
            valueType = args[1];
            return true;
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    private static bool TryGetTaskResultSlot(Type type, out Type? resultType)
    {
        if (type == typeof(Task))
        {
            resultType = null;
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            resultType = type.GetGenericArguments()[0];
            return true;
        }

        if (type.IsGenericTypeDefinition && type == typeof(Task<>))
        {
            resultType = type.GetGenericArguments()[0];
            return true;
        }

        resultType = null;
        return false;
    }

    private static bool TryFormatDelegateType(
        Type type,
        HashSet<Type> visited,
        Func<Type, HashSet<Type>, string> mapType,
        out string tsType)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (typeof(Delegate).IsAssignableFrom(type) && type.Namespace == "System")
            {
                var typeArgs = type.GetGenericArguments();
                if (def.Name.StartsWith("Func`", StringComparison.Ordinal))
                {
                    var paramParts = typeArgs[..^1].Select((t, i) => $"arg{i}: {mapType(t, visited)}");
                    var retType = mapType(typeArgs[^1], visited);
                    tsType = $"({string.Join(", ", paramParts)}) => {retType}";
                    return true;
                }

                if (def.Name.StartsWith("Action`", StringComparison.Ordinal))
                {
                    var paramParts = typeArgs.Select((t, i) => $"arg{i}: {mapType(t, visited)}");
                    tsType = $"({string.Join(", ", paramParts)}) => void";
                    return true;
                }
            }
        }

        tsType = "";
        return false;
    }

    private static Type GetOpenType(Type type)
    {
        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericTypeDefinition()
            : type;
    }

    private static IReadOnlyList<Type> GetDirectlyImplementedInterfaces(Type type)
    {
        var baseInterfaces = type.BaseType?.GetInterfaces().ToHashSet() ?? [];

        return type.GetInterfaces()
            .Where(i => !baseInterfaces.Contains(i))
            .Where(i => !type.GetInterfaces().Any(other => other != i && i.IsAssignableFrom(other)))
            .ToArray();
    }

    private static bool TryGetAugmentableProjectedType(
        Type type, out ProjectedTypeKind projectedKind, out string[] typeParamNames)
    {
        if (TryGetArrayProjectionSlot(type, out var arraySlot) && IsRepresentableAsGlobalArrayAugmentation(type, arraySlot))
        {
            projectedKind = ProjectedTypeKind.Array;
            typeParamNames = GetCanonicalProjectedTypeParamNames(projectedKind);
            return true;
        }

        if (TryGetTaskResultSlot(type, out var taskResult) && taskResult?.IsGenericParameter == true)
        {
            projectedKind = ProjectedTypeKind.Promise;
            typeParamNames = GetCanonicalProjectedTypeParamNames(projectedKind);
            return true;
        }

        projectedKind = ProjectedTypeKind.None;
        typeParamNames = [];
        return false;
    }

    private static bool IsRepresentableAsGlobalArrayAugmentation(Type type, Type arraySlot)
    {
        if (!arraySlot.IsGenericParameter) return false;

        return (type.IsArray && type.GetArrayRank() == 1)
            || _arrayProjectionNamedTypes.Contains(GetOpenType(type));
    }

    private static Type[] GetTypeParameterSlots(Type type)
    {
        if (type.IsArray && type.GetArrayRank() == 1)
        {
            var elem = type.GetElementType()!;
            return elem.IsGenericParameter ? [elem] : [];
        }

        if (type.IsGenericType || type.IsGenericTypeDefinition)
        {
            return type.GetGenericArguments();
        }

        return [];
    }

    private static string[] GetCanonicalProjectedTypeParamNames(ProjectedTypeKind projectedKind)
    {
        return projectedKind switch
        {
            ProjectedTypeKind.Array => ["T"],
            ProjectedTypeKind.Promise => ["T"],
            _ => [],
        };
    }

    private static string MapNamedType(Type type)
    {
        // Classes, structs, interfaces → return the fully-qualified TypeScript type name with namespace
        // Even unregistered types appear in completions and hover; registered types get full completion support
        if (type.Namespace != null)
        {
            return $"{type.Namespace}.{BuildSimpleTypeName(type)}";
        }

        return "any";
    }

    private static string FormatClrSignature(MethodInfo method)
    {
        var paramStr = string.Join(
            ", ",
            method.GetParameters()
                .Select(p =>
                    {
                        var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "";
                        var paramType = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
                        return $"{prefix}{paramType.Name} {p.Name}";
                    }
                )
        );
        return $"{method.ReturnType.Name} {method.Name}({paramStr})";
    }

    private static string SanitizeParamName(string name)
    {
        string[] reserved = ["arguments", "default", "delete", "export", "import", "in", "instanceof", "new", "return", "this", "typeof", "void"];
        return Array.IndexOf(reserved, name) >= 0 ? $"_{name}" : name;
    }

    private void WriteEnumDeclaration(StringBuilder sb, Type type, string typeIndent)
    {
        var jsDoc = this._jsDocProvider?.Get(type);
        if (jsDoc != null) WriteJsDocBlock(sb, typeIndent, jsDoc);
        var keyword = typeIndent.Length == 0 ? "declare enum" : "enum";
        sb.AppendLine($"{typeIndent}{keyword} {type.Name} {{");
        var names = Enum.GetNames(type);
#if NETSTANDARD2_1
        var underlyingType = Enum.GetUnderlyingType(type);
        var values = Enum.GetValues(type).Cast<object>().Select(v => Convert.ChangeType(v, underlyingType)).ToArray();
#else
        var values = Enum.GetValuesAsUnderlyingType(type).Cast<object>().ToArray();
#endif
        var memberIndent = typeIndent + "  ";
        for (var i = 0; i < names.Length; i++)
        {
            sb.AppendLine($"{memberIndent}{names[i]} = {values[i]},");
        }

        sb.AppendLine($"{typeIndent}}}");
    }

    private void WriteDeclaration(StringBuilder sb, Type type, HashSet<Type> visited)
    {
        // Constructed generic types (e.g. List<string>) are treated as their open form (List<T>)
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            type = type.GetGenericTypeDefinition();
        }

        if (!visited.Add(type)) return;

        var ns = type.Namespace;
        var typeIndent = ns != null ? "  " : "";

        if (ns != null)
        {
            sb.AppendLine($"declare namespace {ns} {{");
        }

        if (type.IsEnum)
        {
            this.WriteEnumDeclaration(sb, type, typeIndent);
        }
        else if (type.IsInterface)
        {
            this.WriteInterfaceDeclaration(sb, type, visited, typeIndent);
        }
        else
        {
            this.WriteClassDeclaration(sb, type, visited, typeIndent);
        }

        if (ns != null)
        {
            sb.AppendLine("}");
        }
    }

    private void WriteClassDeclaration(StringBuilder sb, Type type, HashSet<Type> visited, string typeIndent)
    {
        var jsDoc = this._jsDocProvider?.Get(type);
        if (jsDoc != null) WriteJsDocBlock(sb, typeIndent, jsDoc);
        var header = BuildTypeHeader(type);
        var keyword = typeIndent.Length == 0 ? "declare class" : "class";

        var baseType = type.BaseType;
        var extendsClause = "";
        if (baseType != null && baseType != typeof(object) && baseType != typeof(ValueType))
        {
            extendsClause = $" extends {MapType(baseType, visited)}";
        }

        var implementedInterfaces = GetDirectlyImplementedInterfaces(type);
        var implementsClause = implementedInterfaces.Count > 0
            ? $" implements {string.Join(", ", implementedInterfaces.Select(i => MapType(i, visited)))}"
            : "";

        sb.AppendLine($"{typeIndent}{keyword} {header}{extendsClause}{implementsClause} {{");

        var memberIndent = typeIndent + "  ";

        this.WriteConstructors(sb, type, visited, memberIndent);
        this.WriteFields(sb, type, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, visited, "static ", memberIndent);
        this.WriteProperties(sb, type, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, visited, "static ", memberIndent);
        this.WriteMethods(sb, type, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, visited, "static ", memberIndent);

        this.WriteFields(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        this.WriteProperties(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        this.WriteMethods(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);

        sb.AppendLine($"{typeIndent}}}");

        if (implementedInterfaces.Count > 0)
        {
            var interfaceKeyword = typeIndent.Length == 0 ? "declare interface" : "interface";
            sb.AppendLine(
                $"{typeIndent}{interfaceKeyword} {header} extends " +
                $"{string.Join(", ", implementedInterfaces.Select(i => MapType(i, visited)))} {{}}"
            );
        }
    }

    private void WriteInterfaceDeclaration(StringBuilder sb, Type type, HashSet<Type> visited, string typeIndent)
    {
        var jsDoc = this._jsDocProvider?.Get(type);
        if (jsDoc != null) WriteJsDocBlock(sb, typeIndent, jsDoc);
        var header = BuildTypeHeader(type);
        var keyword = typeIndent.Length == 0 ? "declare interface" : "interface";
        sb.AppendLine($"{typeIndent}{keyword} {header} {{");
        var memberIndent = typeIndent + "  ";
        this.WriteProperties(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        this.WriteMethods(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        sb.AppendLine($"{typeIndent}}}");
    }

    private void WriteConstructors(StringBuilder sb, Type type, HashSet<Type> visited, string indent)
    {
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            var paramParts = new List<string>();
            var ok = true;
            foreach (var param in ctor.GetParameters())
            {
                if (param.ParameterType.IsByRef || param.ParameterType.IsPointer)
                {
                    ok = false;
                    break;
                }

                paramParts.Add($"{SanitizeParamName(param.Name ?? $"arg{param.Position}")}: {MapType(param.ParameterType, visited)}");
            }

            if (ok)
            {
                var jsDoc = this._jsDocProvider?.Get(ctor);
                if (jsDoc != null) WriteJsDocBlock(sb, indent, jsDoc);
                sb.AppendLine($"{indent}constructor({string.Join(", ", paramParts)});");
            }
        }
    }

    private void WriteFields(StringBuilder sb, Type type, BindingFlags flags, HashSet<Type> visited, string prefix, string indent)
    {
        foreach (var field in type.GetFields(flags))
        {
            var tsType = MapType(field.FieldType, visited);
            var jsDoc = this._jsDocProvider?.Get(field);
            if (jsDoc != null)
            {
                WriteJsDocBlock(sb, indent, jsDoc);
            }
            else
            {
                sb.AppendLine($"{indent}/** {field.FieldType.Name} {field.Name} */");
            }

            sb.AppendLine($"{indent}{prefix}{field.Name}: {tsType};");
        }
    }

    private void WriteProperties(StringBuilder sb, Type type, BindingFlags flags, HashSet<Type> visited, string prefix, string indent)
    {
        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            var tsType = MapType(prop.PropertyType, visited);
            var readonlyModifier = prop.SetMethod?.IsPublic == true ? "" : "readonly ";
            var jsDoc = this._jsDocProvider?.Get(prop);
            if (jsDoc != null)
            {
                WriteJsDocBlock(sb, indent, jsDoc);
            }
            else
            {
                sb.AppendLine($"{indent}/** {prop.PropertyType.Name} {prop.Name} */");
            }

            sb.AppendLine($"{indent}{prefix}{readonlyModifier}{prop.Name}: {tsType};");
        }
    }

    private void WriteMethods(StringBuilder sb, Type type, BindingFlags flags, HashSet<Type> visited, string prefix, string indent)
    {
        // Collect .NET definitions keyed by TS signature (preserving insertion order)
        var entries = new List<(string? tsSig, List<string> netSigs, MethodInfo? singleMethod)>();
        var tsSigIndex = new Dictionary<string, int>();

        foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName))
        {
            var tsSig = TryBuildMethodSignature(method, visited);
            var netSig = FormatClrSignature(method);

            if (tsSig == null)
            {
                entries.Add((null, [netSig], null));
            }
            else
            {
                var sigKey = $"{prefix}{tsSig}"; // dedup key without indent
                var line = $"{indent}{prefix}{tsSig};";
                if (tsSigIndex.TryGetValue(sigKey, out var idx))
                {
                    var (existingSig, existingNetSigs, _) = entries[idx];
                    existingNetSigs.Add(netSig); // append .NET definition to the same TS signature
                    entries[idx] = (existingSig, existingNetSigs, null); // clear single method on collision
                }
                else
                {
                    tsSigIndex[sigKey] = entries.Count;
                    entries.Add((line, [netSig], method));
                }
            }
        }

        foreach (var (tsSig, netSigs, singleMethod) in entries)
        {
            if (tsSig == null)
            {
                sb.AppendLine($"{indent}// [skipped] {prefix}{netSigs[0]}");
            }
            else
            {
                var jsDoc = singleMethod != null ? this._jsDocProvider?.Get(singleMethod) : null;
                if (jsDoc != null)
                {
                    WriteJsDocBlock(sb, indent, jsDoc);
                }
                else if (netSigs.Count == 1)
                {
                    sb.AppendLine($"{indent}/** {netSigs[0]} */");
                }
                else
                {
                    sb.AppendLine($"{indent}/**");
                    foreach (var n in netSigs)
                    {
                        sb.AppendLine($"{indent} * - {n}");
                    }

                    sb.AppendLine($"{indent} */");
                }

                sb.AppendLine(tsSig);
            }
        }
    }

    private void WriteExtensionAugmentation(
        StringBuilder sb,
        Type receiverType,
        ExtensionAugmentationTarget augmentationTarget,
        MethodInfo[] methods,
        HashSet<Type> visited)
    {
        var ns = augmentationTarget.Namespace;
        var typeIndent = ns != null ? "  " : "";
        var memberIndent = typeIndent + "  ";

        if (ns != null) sb.AppendLine($"declare namespace {ns} {{");

        var keyword = augmentationTarget.UseDeclareKeyword ? "declare interface" : "interface";
        sb.AppendLine(
            $"{typeIndent}{keyword} {augmentationTarget.ScriptTypeName}{augmentationTarget.TypeParamStr} {{"
        );

        // Collect entries with overload-collapsing (same pattern as WriteMethods)
        var entries = new List<(string? tsSig, List<string> netSigs, MethodInfo? singleMethod)>();
        var tsSigIndex = new Dictionary<string, int>();

        foreach (var method in methods)
        {
            // Build substitution: method's first-param generic args → interface type param names.
            // e.g. for Select(this IEnumerable<TSource>, ...) targeting IEnumerable<T>:
            //   TSource (method's generic param) → "T" (interface's type param name)
            var firstParamType = method.GetParameters()[0].ParameterType;
            var firstParamArgs = GetTypeParameterSlots(firstParamType);

            var substitution = new Dictionary<Type, string>();
            for (var i = 0; i < Math.Min(firstParamArgs.Length, augmentationTarget.TypeParamNames.Length); i++)
            {
                substitution[firstParamArgs[i]] = augmentationTarget.TypeParamNames[i];
            }

            var tsSig = TryBuildExtensionMethodSignature(method, substitution, visited);
            var netSig = FormatClrSignature(method);

            if (tsSig is null)
            {
                entries.Add((null, [netSig], null));
            }
            else
            {
                if (tsSigIndex.TryGetValue(tsSig, out var idx))
                {
                    var (existingSig, existingNetSigs, _) = entries[idx];
                    existingNetSigs.Add(netSig);
                    entries[idx] = (existingSig, existingNetSigs, null);
                }
                else
                {
                    tsSigIndex[tsSig] = entries.Count;
                    entries.Add(($"{memberIndent}{tsSig};", [netSig], method));
                }
            }
        }

        foreach (var (tsSig, netSigs, singleMethod) in entries)
        {
            if (tsSig is null)
            {
                sb.AppendLine($"{memberIndent}// [skipped] {netSigs[0]}");
            }
            else
            {
                var jsDoc = singleMethod != null ? this._jsDocProvider?.Get(singleMethod) : null;
                if (jsDoc != null)
                {
                    WriteJsDocBlock(sb, memberIndent, jsDoc);
                }
                else if (netSigs.Count == 1)
                {
                    sb.AppendLine($"{memberIndent}/** {netSigs[0]} */");
                }
                else
                {
                    sb.AppendLine($"{memberIndent}/**");
                    foreach (var n in netSigs)
                    {
                        sb.AppendLine($"{memberIndent} * - {n}");
                    }

                    sb.AppendLine($"{memberIndent} */");
                }

                sb.AppendLine(tsSig);
            }
        }

        sb.AppendLine($"{typeIndent}}}");
        if (ns != null) sb.AppendLine("}");
    }

    private enum ProjectedTypeKind
    {
        None,
        Array,
        Promise,
    }

    private readonly record struct ExtensionAugmentationTarget(
        string? Namespace,
        string ScriptTypeName,
        string TypeParamStr,
        string[] TypeParamNames,
        bool UseDeclareKeyword
    );
}
