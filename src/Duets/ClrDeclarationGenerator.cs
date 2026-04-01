using System.Reflection;
using System.Text;

namespace Duets;

/// <summary>
/// Generates TypeScript declaration (<c>.d.ts</c>) source from .NET types via reflection.
/// </summary>
public class ClrDeclarationGenerator
{
    private static readonly HashSet<Type> _numericTypes =
    [
        typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort),
        typeof(int), typeof(uint),
        typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
    ];

    /// <summary>
    /// Generates TypeScript type declaration (.d.ts) source for the specified .NET type.
    /// Types with a namespace are wrapped in a declare namespace block.
    /// Unsupported types and members fall back to any or are omitted from the output.
    /// </summary>
    public string GenerateTypeDefTs(Type targetType)
    {
        var sb = new StringBuilder();
        var visited = new HashSet<Type>();
        WriteDeclaration(sb, targetType, visited);
        return sb.ToString();
    }

    private static void WriteDeclaration(StringBuilder sb, Type type, HashSet<Type> visited)
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
            WriteEnumDeclaration(sb, type, typeIndent);
        }
        else if (type.IsInterface)
        {
            WriteInterfaceDeclaration(sb, type, visited, typeIndent);
        }
        else
        {
            WriteClassDeclaration(sb, type, visited, typeIndent);
        }

        if (ns != null)
        {
            sb.AppendLine("}");
        }
    }

    private static void WriteClassDeclaration(StringBuilder sb, Type type, HashSet<Type> visited, string typeIndent)
    {
        var header = BuildTypeHeader(type);
        var keyword = typeIndent.Length == 0 ? "declare class" : "class";

        var baseType = type.BaseType;
        var extendsClause = "";
        if (baseType != null && baseType != typeof(object) && baseType != typeof(ValueType))
        {
            extendsClause = $" extends {MapType(baseType, visited)}";
        }

        sb.AppendLine($"{typeIndent}{keyword} {header}{extendsClause} {{");

        var memberIndent = typeIndent + "  ";

        WriteConstructors(sb, type, visited, memberIndent);
        WriteFields(sb, type, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, visited, "static ", memberIndent);
        WriteProperties(sb, type, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, visited, "static ", memberIndent);
        WriteMethods(sb, type, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, visited, "static ", memberIndent);

        WriteFields(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        WriteProperties(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        WriteMethods(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);

        sb.AppendLine($"{typeIndent}}}");
    }

    private static void WriteInterfaceDeclaration(StringBuilder sb, Type type, HashSet<Type> visited, string typeIndent)
    {
        var header = BuildTypeHeader(type);
        var keyword = typeIndent.Length == 0 ? "declare interface" : "interface";
        sb.AppendLine($"{typeIndent}{keyword} {header} {{");
        var memberIndent = typeIndent + "  ";
        WriteProperties(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        WriteMethods(sb, type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, visited, "", memberIndent);
        sb.AppendLine($"{typeIndent}}}");
    }

    private static void WriteEnumDeclaration(StringBuilder sb, Type type, string typeIndent)
    {
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

    private static void WriteConstructors(StringBuilder sb, Type type, HashSet<Type> visited, string indent)
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
                sb.AppendLine($"{indent}constructor({string.Join(", ", paramParts)});");
            }
        }
    }

    private static void WriteFields(StringBuilder sb, Type type, BindingFlags flags, HashSet<Type> visited, string prefix, string indent)
    {
        foreach (var field in type.GetFields(flags))
        {
            var tsType = MapType(field.FieldType, visited);
            sb.AppendLine($"{indent}/** {field.FieldType.Name} {field.Name} */");
            sb.AppendLine($"{indent}{prefix}{field.Name}: {tsType};");
        }
    }

    private static void WriteProperties(StringBuilder sb, Type type, BindingFlags flags, HashSet<Type> visited, string prefix, string indent)
    {
        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            var tsType = MapType(prop.PropertyType, visited);
            var readonlyModifier = prop.SetMethod?.IsPublic == true ? "" : "readonly ";
            sb.AppendLine($"{indent}/** {prop.PropertyType.Name} {prop.Name} */");
            sb.AppendLine($"{indent}{prefix}{readonlyModifier}{prop.Name}: {tsType};");
        }
    }

    private static void WriteMethods(StringBuilder sb, Type type, BindingFlags flags, HashSet<Type> visited, string prefix, string indent)
    {
        // Collect .NET definitions keyed by TS signature (preserving insertion order)
        var entries = new List<(string? tsSig, List<string> netSigs)>();
        var tsSigIndex = new Dictionary<string, int>();

        foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName))
        {
            var tsSig = TryBuildMethodSignature(method, visited);
            var netSig = FormatClrSignature(method);

            if (tsSig == null)
            {
                entries.Add((null, [netSig]));
            }
            else
            {
                var sigKey = $"{prefix}{tsSig}"; // dedup key without indent
                var line = $"{indent}{prefix}{tsSig};";
                if (tsSigIndex.TryGetValue(sigKey, out var idx))
                {
                    entries[idx].netSigs.Add(netSig); // append .NET definition to the same TS signature
                }
                else
                {
                    tsSigIndex[sigKey] = entries.Count;
                    entries.Add((line, [netSig]));
                }
            }
        }

        foreach (var (tsSig, netSigs) in entries)
        {
            if (tsSig == null)
            {
                sb.AppendLine($"{indent}// [skipped] {prefix}{netSigs[0]}");
            }
            else
            {
                if (netSigs.Count == 1)
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

        // Task / Task<T>
        if (type == typeof(Task)) return "Promise<void>";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = MapType(type.GetGenericArguments()[0], visited);
            return $"Promise<{inner}>";
        }

        // T[] / IEnumerable<T> / List<T>
        if (type.IsArray && type.GetArrayRank() == 1)
        {
            var elem = MapType(type.GetElementType()!, visited);
            return $"{elem}[]";
        }

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(IList<>))
            {
                var elem = MapType(type.GetGenericArguments()[0], visited);
                return $"{elem}[]";
            }

            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>))
            {
                var keyTs = MapType(type.GetGenericArguments()[0], visited);
                var valTs = MapType(type.GetGenericArguments()[1], visited);
                if (keyTs != "any" && keyTs != "number" && keyTs != "string")
                {
                    return "any";
                }

                return $"{{ [key: {keyTs}]: {valTs} }}";
            }
        }

        // Classes, structs, interfaces → return the fully-qualified TypeScript type name with namespace
        // Even unregistered types appear in completions and hover; registered types get full completion support
        if (type.Namespace != null)
        {
            return $"{type.Namespace}.{BuildSimpleTypeName(type)}";
        }

        return "any";
    }

    /// <summary>Builds the type header including generic type parameters.</summary>
    private static string BuildTypeHeader(Type type)
    {
        if (!type.IsGenericTypeDefinition) return type.Name;
        var backtickIdx = type.Name.IndexOf('`');
        var name = backtickIdx >= 0 ? type.Name[..backtickIdx] : type.Name;
        var typeParams = string.Join(", ", type.GetGenericArguments().Select(a => a.Name));
        return $"{name}<{typeParams}>";
    }

    /// <summary>Builds the type name with actual type arguments, used in extends clauses and similar contexts.</summary>
    private static string BuildSimpleTypeName(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var def = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
        var backtickIdx = def.Name.IndexOf('`');
        var name = backtickIdx >= 0 ? def.Name[..backtickIdx] : def.Name;
        var args = string.Join(", ", type.GetGenericArguments().Select(a => MapType(a, [])));
        return $"{name}<{args}>";
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
}
