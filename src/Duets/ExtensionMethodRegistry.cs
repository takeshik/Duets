using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets;

/// <summary>
/// A mutable, thread-safe registry of extension method container types.
/// Registered methods are made available as instance methods on CLR-wrapped objects
/// via the Jint <see cref="Options.InteropOptions.MemberAccessor"/> hook.
/// </summary>
internal sealed class ExtensionMethodRegistry
{
    private static readonly Type _genericArrayKey = typeof(Array);

    // Keyed by open-generic target type (e.g. IEnumerable<>) or concrete type (e.g. string).
    // Replaced atomically on Register; readers take a snapshot.
    private Dictionary<Type, MethodInfo[]> _methods = new();
    private HashSet<Type> _registered = new();

    public bool HasMethods => this._methods.Count > 0;

    /// <summary>Scans <paramref name="containerType"/> for extension methods and adds them to the registry.</summary>
    public bool Register(Type containerType)
    {
        Dictionary<Type, MethodInfo[]> snapshot;
        Dictionary<Type, MethodInfo[]> merged;

        do
        {
            snapshot = this._methods;
            if (this._registered.Contains(containerType)) return false;

            var incoming = containerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.IsDefined(typeof(ExtensionAttribute), false))
                .GroupBy(m =>
                    {
                        var p = m.GetParameters()[0].ParameterType;
                        return GetDispatchKey(p);
                    }
                )
                .ToDictionary(g => g.Key, g => g.ToArray());

            merged = new Dictionary<Type, MethodInfo[]>(snapshot);
            foreach (var (type, methods) in incoming)
            {
                merged[type] = merged.TryGetValue(type, out var existing)
                    ? [.. existing, .. methods]
                    : methods;
            }
        } while (!ReferenceEquals(
                Interlocked.CompareExchange(ref this._methods, merged, snapshot),
                snapshot
            ));

        // Track registered containers (best-effort; minor racy double-registration is harmless)
        var newRegistered = new HashSet<Type>(this._registered) { containerType };
        Interlocked.CompareExchange(ref this._registered, newRegistered, this._registered);
        return true;
    }

    /// <summary>
    /// Returns a <see cref="ClrFunction"/> that dispatches to the matching extension method,
    /// or <c>null</c> if no registered extension method targets <paramref name="target"/>'s type
    /// with the given <paramref name="memberName"/>.
    /// </summary>
    public JsValue? CreateMemberValue(Engine engine, object target, string memberName)
    {
        var candidates = this.FindCandidates(target.GetType(), memberName);
        if (candidates.Count == 0) return null;

        return new ClrFunction(engine, memberName, (_, jsArgs) => this.Invoke(engine, target, candidates, jsArgs));
    }

    // Jint resolves CLR member names with the first character case-insensitive and the rest exact.
    private static bool NameMatches(string clrName, string jsName)
    {
        if (clrName.Length != jsName.Length) return false;
        if (clrName.Length == 0) return true;
        return char.ToLowerInvariant(clrName[0]) == char.ToLowerInvariant(jsName[0])
            && string.Equals(clrName[1..], jsName[1..], StringComparison.Ordinal);
    }

    private static bool TryConvertArguments(
        Engine engine, ParameterInfo[] parameters, JsValue[] jsArgs, object?[] clrArgs)
    {
        for (var i = 0; i < jsArgs.Length; i++)
        {
            var paramType = parameters[i + 1].ParameterType;
            try
            {
                clrArgs[i + 1] = engine.TypeConverter.Convert(
                    jsArgs[i].ToObject(),
                    paramType,
                    CultureInfo.InvariantCulture
                );
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    // Infers generic type arguments from the 'this' parameter only (the concrete target object),
    // then substitutes object for any type params that could not be inferred (e.g. TResult that
    // appears only in a delegate's return type). This lets us make the method concrete before
    // converting JS arguments, so Jint's type converter sees Func<Item, object> rather than the
    // open Func<Item, TResult>.
    private static MethodInfo? MakeConcreteMethod(MethodInfo method, ParameterInfo[] parameters, object target)
    {
        var genericArgs = method.GetGenericArguments();
        var resolved = new Type?[genericArgs.Length];

        InferGenericArgs(parameters[0].ParameterType, target.GetType(), genericArgs, resolved);

        for (var i = 0; i < resolved.Length; i++)
        {
            resolved[i] ??= typeof(object);
        }

        try
        {
            return method.MakeGenericMethod(resolved!);
        }
        catch (ArgumentException)
        {
            // Type constraints not satisfied — skip this candidate.
            return null;
        }
    }

    private static void InferGenericArgs(Type paramType, Type argType, Type[] genericParams, Type?[] resolved)
    {
        if (paramType.IsGenericParameter)
        {
            var pos = paramType.GenericParameterPosition;
            if (pos < resolved.Length) resolved[pos] ??= argType;
            return;
        }

        if (paramType.IsArray && argType.IsArray && paramType.GetArrayRank() == argType.GetArrayRank())
        {
            InferGenericArgs(paramType.GetElementType()!, argType.GetElementType()!, genericParams, resolved);
            return;
        }

        if (!paramType.IsGenericType) return;

        var paramDef = paramType.GetGenericTypeDefinition();
        // Walk argType's interfaces to find a match for paramDef
        var argTypes = new List<Type> { argType };
        argTypes.AddRange(argType.GetInterfaces());
        for (var t = argType.BaseType; t is not null; t = t.BaseType)
        {
            argTypes.Add(t);
        }

        foreach (var candidate in argTypes)
        {
            if (!candidate.IsGenericType) continue;
            if (candidate.GetGenericTypeDefinition() != paramDef) continue;
            var paramArgs = paramType.GetGenericArguments();
            var candidateArgs = candidate.GetGenericArguments();
            for (var i = 0; i < Math.Min(paramArgs.Length, candidateArgs.Length); i++)
            {
                InferGenericArgs(paramArgs[i], candidateArgs[i], genericParams, resolved);
            }

            break;
        }
    }

    private static Type GetDispatchKey(Type type)
    {
        if (type.IsArray && type.GetArrayRank() == 1 && type.GetElementType()!.IsGenericParameter)
        {
            return _genericArrayKey;
        }

        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericTypeDefinition()
            : type;
    }

    private JsValue Invoke(Engine engine, object target, List<MethodInfo> candidates, JsValue[] jsArgs)
    {
        foreach (var method in candidates)
        {
            var parameters = method.GetParameters(); // [0] = 'this', [1..] = user args
            if (parameters.Length - 1 != jsArgs.Length) continue;

            var clrArgs = new object?[parameters.Length];
            clrArgs[0] = target;

            // For generic methods, make them concrete first (inferring from 'this' and falling
            // back to object for any unresolved type params such as return-only TResult).
            // This ensures TryConvertArguments sees concrete types like Func<Item, object>
            // rather than the open Func<Item, TResult>, which the type converter cannot handle.
            MethodInfo concrete;
            ParameterInfo[] concreteParams;
            if (method.IsGenericMethodDefinition)
            {
                var made = MakeConcreteMethod(method, parameters, target);
                if (made is null) continue;
                concrete = made;
                concreteParams = made.GetParameters();
            }
            else
            {
                concrete = method;
                concreteParams = parameters;
            }

            if (!TryConvertArguments(engine, concreteParams, jsArgs, clrArgs)) continue;

            try
            {
                var result = concrete.Invoke(null, clrArgs);
                return result as JsValue ?? JsValue.FromObject(engine, result);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo
                    .Capture(ex.InnerException)
                    .Throw();
                throw; // unreachable
            }
        }

        throw new InvalidOperationException(
            $"No extension method overload matches the provided arguments for '{candidates[0].Name}'."
        );
    }

    private List<MethodInfo> FindCandidates(Type objectType, string memberName)
    {
        var snapshot = this._methods;
        if (snapshot.Count == 0) return [];

        var result = new List<MethodInfo>();

        void Collect(Type t)
        {
            void AddMatches(Type key)
            {
                if (!snapshot.TryGetValue(key, out var methods)) return;

                foreach (var m in methods)
                {
                    if (NameMatches(m.Name, memberName))
                    {
                        result.Add(m);
                    }
                }
            }

            AddMatches(GetDispatchKey(t));

            if (t.IsArray && t.GetArrayRank() == 1)
            {
                AddMatches(_genericArrayKey);
            }
        }

        Collect(objectType);
        foreach (var iface in objectType.GetInterfaces())
        {
            Collect(iface);
        }

        for (var t = objectType.BaseType; t is not null; t = t.BaseType)
        {
            Collect(t);
        }

        return result;
    }
}
