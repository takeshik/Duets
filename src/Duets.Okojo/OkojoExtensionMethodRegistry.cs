using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Okojo;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.Runtime.Interop;

namespace Duets.Okojo;

internal sealed class OkojoExtensionMethodRegistry
{
    private static readonly MethodInfo _invokeJsDelegateMethod =
        typeof(OkojoExtensionMethodRegistry).GetMethod(
            nameof(InvokeJavaScriptDelegate),
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

    private readonly object _bindingsGate = new();
    private Dictionary<Type, MethodInfo[]> _methods = new();
    private HashSet<Type> _registered = new();
    private Dictionary<Type, HostBinding> _bindings = new();

    public bool Register(Type containerType)
    {
        Dictionary<Type, MethodInfo[]> snapshot;
        Dictionary<Type, MethodInfo[]> merged;

        do
        {
            snapshot = this._methods;
            if (this._registered.Contains(containerType))
            {
                return false;
            }

            var incoming = containerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static method => method.IsDefined(typeof(ExtensionAttribute), false))
                .GroupBy(static method => GetDispatchKey(method.GetParameters()[0].ParameterType))
                .ToDictionary(group => group.Key, group => group.ToArray());

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

        // OPTIMIZE: Collapse _methods/_registered invalidation into a single synchronization strategy if concurrent
        // addExtensionMethods registration ever becomes observable in profiling. The current split update is safe
        // because registration is additive and duplicate work is idempotent.
        var newRegistered = new HashSet<Type>(this._registered) { containerType };
        Interlocked.CompareExchange(ref this._registered, newRegistered, this._registered);

        lock (this._bindingsGate)
        {
            // Extension-method registration is additive, so the simplest correct invalidation strategy is
            // to drop all cached bindings and let them rebuild lazily on next access.
            // OPTIMIZE: Invalidate only affected receiver-type bindings if addExtensionMethods becomes hot enough to matter.
            this._bindings = new Dictionary<Type, HostBinding>();
        }

        return true;
    }

    public object PrepareHostValue(object value)
    {
        return value is OkojoBoundHostObject or IHostBindable || !ShouldWrap(value.GetType())
            ? value
            : new OkojoBoundHostObject(value, this);
    }

    public object? Unwrap(object? value)
    {
        return value is OkojoBoundHostObject bound ? bound.Target : value;
    }

    public HostBinding GetBinding(Type targetType)
    {
        lock (this._bindingsGate)
        {
            if (this._bindings.TryGetValue(targetType, out var binding))
            {
                return binding;
            }

            binding = this.CreateBinding(targetType);
            this._bindings[targetType] = binding;
            return binding;
        }
    }

    public JsValue ToJsValue(JsRealm realm, object? value)
    {
        if (value is null)
        {
            return JsValue.Null;
        }

        if (value is JsValue jsValue)
        {
            return jsValue;
        }

        return realm.WrapHostValue(this.PrepareHostValueIfNeeded(value));
    }

    public JsValue ToJsArrayValue(JsRealm realm, object? value)
    {
        value = this.Unwrap(value);

        if (value is null)
        {
            return JsValue.Null;
        }

        if (value is JsArray jsArray)
        {
            return JsValue.FromObject(jsArray);
        }

        if (value is Array array)
        {
            return JsValue.FromObject(this.ToJsArray(realm, array));
        }

        if (value is IEnumerable enumerable and not string)
        {
            var jsValues = enumerable
                .Cast<object?>()
                .Select(item => this.ToJsArrayElement(realm, item))
                .ToArray();
            return JsValue.FromObject(this.ToJsArray(realm, jsValues));
        }

        throw new ArgumentException("Expected a CLR array, enumerable, or a JavaScript array.");
    }

    private static bool ShouldWrap(Type type)
    {
        return type != typeof(string)
            && type != typeof(Type)
            && !typeof(Delegate).IsAssignableFrom(type)
            && !typeof(JsObject).IsAssignableFrom(type)
            && !typeof(JsValue).IsAssignableFrom(type)
            && !type.IsPrimitive
            && !type.IsEnum;
    }

    private static Type GetDispatchKey(Type type)
    {
        if (type.IsArray && type.GetArrayRank() == 1 && type.GetElementType()!.IsGenericParameter)
        {
            return typeof(Array);
        }

        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericTypeDefinition()
            : type;
    }

    private static bool NameMatches(string clrName, string scriptName)
    {
        if (clrName.Length != scriptName.Length)
        {
            return false;
        }

        if (clrName.Length == 0)
        {
            return true;
        }

        return char.ToLowerInvariant(clrName[0]) == char.ToLowerInvariant(scriptName[0])
            && string.Equals(clrName[1..], scriptName[1..], StringComparison.Ordinal);
    }

    private static int GetFunctionLength(IEnumerable<MethodInfo> methods, int parameterOffset)
    {
        return methods
            .Select(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Count(parameter => !parameter.IsOptional) - parameterOffset;
                }
            )
            .Where(length => length >= 0)
            .DefaultIfEmpty(0)
            .Min();
    }

    private static bool ShouldExposeMethod(MethodInfo method)
    {
        if (!method.IsSpecialName)
        {
            return true;
        }

        return method.Name.StartsWith("op_", StringComparison.Ordinal);
    }

    private static MethodInfo? MakeConcreteExtensionMethod(MethodInfo method, ParameterInfo[] parameters, object target)
    {
        var genericArgs = method.GetGenericArguments();
        var resolved = new Type?[genericArgs.Length];

        InferGenericArgs(parameters[0].ParameterType, target.GetType(), resolved);

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
            return null;
        }
    }

    private static void InferGenericArgs(Type parameterType, Type argumentType, Type?[] resolved)
    {
        if (parameterType.IsGenericParameter)
        {
            var position = parameterType.GenericParameterPosition;
            if (position < resolved.Length)
            {
                resolved[position] ??= argumentType;
            }

            return;
        }

        if (parameterType.IsArray && argumentType.IsArray && parameterType.GetArrayRank() == argumentType.GetArrayRank())
        {
            InferGenericArgs(parameterType.GetElementType()!, argumentType.GetElementType()!, resolved);
            return;
        }

        if (!parameterType.IsGenericType)
        {
            return;
        }

        var parameterDefinition = parameterType.GetGenericTypeDefinition();
        var argumentTypes = new List<Type> { argumentType };
        argumentTypes.AddRange(argumentType.GetInterfaces());
        for (var baseType = argumentType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            argumentTypes.Add(baseType);
        }

        foreach (var candidate in argumentTypes)
        {
            if (!candidate.IsGenericType || candidate.GetGenericTypeDefinition() != parameterDefinition)
            {
                continue;
            }

            var parameterArguments = parameterType.GetGenericArguments();
            var candidateArguments = candidate.GetGenericArguments();
            for (var i = 0; i < Math.Min(parameterArguments.Length, candidateArguments.Length); i++)
            {
                InferGenericArgs(parameterArguments[i], candidateArguments[i], resolved);
            }

            break;
        }
    }

    private HostBinding CreateBinding(Type targetType)
    {
        var members = new List<HostMemberBinding>();

        foreach (var field in targetType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            JsHostFunctionBody getter = (scoped in info) => this.GetFieldValue(info, field);
            JsHostFunctionBody setter = (scoped in info) => this.SetFieldValue(info, field);
            members.Add(
                new HostMemberBinding(
                    field.Name,
                    HostMemberBindingKind.Field,
                    false,
                    getter,
                    setter
                )
            );
        }

        foreach (var property in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0 && (property.CanRead || property.CanWrite)))
        {
            JsHostFunctionBody? getter = null;
            JsHostFunctionBody? setter = null;
            if (property.CanRead)
            {
                getter = (scoped in info) => this.GetPropertyValue(info, property);
            }

            if (property.CanWrite)
            {
                setter = (scoped in info) => this.SetPropertyValue(info, property);
            }

            members.Add(
                new HostMemberBinding(
                    property.Name,
                    HostMemberBindingKind.Property,
                    false,
                    getter,
                    setter
                )
            );
        }

        foreach (var group in targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.DeclaringType != typeof(object) && ShouldExposeMethod(method))
            .GroupBy(static method => method.Name, StringComparer.Ordinal))
        {
            var overloads = group.OrderBy(static method => method.MetadataToken).ToArray();
            JsHostFunctionBody methodBody = (scoped in info) => this.InvokeInstanceMethods(info, overloads);
            members.Add(
                new HostMemberBinding(
                    group.Key,
                    HostMemberBindingKind.Method,
                    false,
                    methodBody: methodBody,
                    functionLength: GetFunctionLength(overloads, 0)
                )
            );
        }

        foreach (var group in this.FindCandidates(targetType)
            .GroupBy(static method => method.Name, StringComparer.Ordinal))
        {
            var candidates = group.OrderBy(static method => method.MetadataToken).ToArray();
            JsHostFunctionBody methodBody = (scoped in info) => this.InvokeExtensionMethods(info, candidates);
            members.Add(
                new HostMemberBinding(
                    group.Key,
                    HostMemberBindingKind.Method,
                    false,
                    methodBody: methodBody,
                    functionLength: GetFunctionLength(candidates, 1)
                )
            );
        }

        return new HostBinding(targetType, members.ToArray(), [])
        {
            Indexer = this.CreateIndexer(targetType),
            Enumerator = this.CreateEnumerator(targetType),
        };
    }

    private HostIndexerBinding? CreateIndexer(Type targetType)
    {
        if (targetType.IsArray && targetType.GetArrayRank() == 1)
        {
            var elementType = targetType.GetElementType()!;
            return new HostIndexerBinding(
                (realm, target, index) =>
                {
                    var array = (Array) ((OkojoBoundHostObject) target).Target;
                    if (index >= (uint) array.Length)
                    {
                        return (false, JsValue.Undefined);
                    }

                    return (true, this.ToJsValue(realm, array.GetValue((int) index)));
                },
                (realm, target, index, value) =>
                {
                    var array = (Array) ((OkojoBoundHostObject) target).Target;
                    if (index >= (uint) array.Length)
                    {
                        return false;
                    }

                    if (!this.TryConvertArgument(realm, elementType, value, out var converted))
                    {
                        return false;
                    }

                    array.SetValue(converted, (int) index);
                    return true;
                },
                (target, indices) =>
                {
                    var array = (Array) ((OkojoBoundHostObject) target).Target;
                    for (uint i = 0; i < array.Length; i++)
                    {
                        indices.Add(i);
                    }
                }
            );
        }

        if (typeof(IList).IsAssignableFrom(targetType))
        {
            return new HostIndexerBinding(
                (realm, target, index) =>
                {
                    var list = (IList) ((OkojoBoundHostObject) target).Target;
                    if (index >= (uint) list.Count)
                    {
                        return (false, JsValue.Undefined);
                    }

                    return (true, this.ToJsValue(realm, list[(int) index]));
                },
                (realm, target, index, value) =>
                {
                    var list = (IList) ((OkojoBoundHostObject) target).Target;
                    if (index >= (uint) list.Count)
                    {
                        return false;
                    }

                    list[(int) index] = this.ToBoxedValue(value);
                    return true;
                },
                (target, indices) =>
                {
                    var list = (IList) ((OkojoBoundHostObject) target).Target;
                    for (uint i = 0; i < list.Count; i++)
                    {
                        indices.Add(i);
                    }
                }
            );
        }

        return null;
    }

    private HostEnumeratorBinding? CreateEnumerator(Type targetType)
    {
        return typeof(IEnumerable).IsAssignableFrom(targetType)
            ? new HostEnumeratorBinding(target => ((IEnumerable) ((OkojoBoundHostObject) target).Target).GetEnumerator())
            : null;
    }

    private IEnumerable<MethodInfo> FindCandidates(Type objectType)
    {
        var snapshot = this._methods;
        if (snapshot.Count == 0)
        {
            return [];
        }

        var result = new List<MethodInfo>();

        void Collect(Type type)
        {
            void AddMatches(Type key)
            {
                if (!snapshot.TryGetValue(key, out var methods))
                {
                    return;
                }

                result.AddRange(methods);
            }

            AddMatches(type);
            if (type.IsArray)
            {
                AddMatches(typeof(Array));
            }

            if (type.IsGenericType)
            {
                AddMatches(type.GetGenericTypeDefinition());
            }

            foreach (var iface in type.GetInterfaces())
            {
                AddMatches(iface);
                if (iface.IsGenericType)
                {
                    AddMatches(iface.GetGenericTypeDefinition());
                }
            }

            for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                AddMatches(baseType);
                if (baseType.IsGenericType)
                {
                    AddMatches(baseType.GetGenericTypeDefinition());
                }
            }
        }

        Collect(objectType);
        return result;
    }

    private JsValue GetFieldValue(in CallInfo info, FieldInfo field)
    {
        var target = info.GetThis<OkojoBoundHostObject>().Target;
        return this.ToJsValue(info.Realm, field.GetValue(target));
    }

    private JsValue SetFieldValue(in CallInfo info, FieldInfo field)
    {
        var target = info.GetThis<OkojoBoundHostObject>().Target;
        if (!this.TryConvertArgument(info.Realm, field.FieldType, info.GetArgument(0), out var converted))
        {
            throw new InvalidOperationException($"Cannot convert the assigned value for field '{field.Name}'.");
        }

        field.SetValue(target, converted);
        return JsValue.Undefined;
    }

    private JsValue GetPropertyValue(in CallInfo info, PropertyInfo property)
    {
        var target = info.GetThis<OkojoBoundHostObject>().Target;
        return this.ToJsValue(info.Realm, property.GetValue(target));
    }

    private JsValue SetPropertyValue(in CallInfo info, PropertyInfo property)
    {
        var target = info.GetThis<OkojoBoundHostObject>().Target;
        if (!this.TryConvertArgument(info.Realm, property.PropertyType, info.GetArgument(0), out var converted))
        {
            throw new InvalidOperationException($"Cannot convert the assigned value for property '{property.Name}'.");
        }

        property.SetValue(target, converted);
        return JsValue.Undefined;
    }

    private JsValue InvokeInstanceMethods(in CallInfo info, MethodInfo[] methods)
    {
        var target = info.GetThis<OkojoBoundHostObject>().Target;
        return this.InvokeMethods(info.Realm, target, methods, info.Arguments, false);
    }

    private JsValue InvokeExtensionMethods(in CallInfo info, MethodInfo[] candidates)
    {
        var bound = info.GetThis<OkojoBoundHostObject>();
        var functionName = info.Function is JsHostFunction hostFunction
            ? hostFunction.Name
            : "(unknown)";
        var matchingCandidates = new List<MethodInfo>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (NameMatches(candidate.Name, functionName))
            {
                matchingCandidates.Add(candidate);
            }
        }

        return this.InvokeMethods(info.Realm, bound.Target, matchingCandidates.ToArray(), info.Arguments, true);
    }

    private JsValue InvokeMethods(JsRealm realm, object target, MethodInfo[] candidates, ReadOnlySpan<JsValue> jsArgs, bool isExtensionMethod)
    {
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("No overload candidates were available for extension-method dispatch.");
        }

        foreach (var method in candidates)
        {
            var parameters = method.GetParameters();
            var parameterOffset = isExtensionMethod ? 1 : 0;

            MethodInfo concreteMethod;
            ParameterInfo[] concreteParameters;
            if (isExtensionMethod && method.IsGenericMethodDefinition)
            {
                var concrete = MakeConcreteExtensionMethod(method, parameters, target);
                if (concrete is null)
                {
                    continue;
                }

                concreteMethod = concrete;
                concreteParameters = concrete.GetParameters();
            }
            else
            {
                concreteMethod = method;
                concreteParameters = parameters;
            }

            if (!this.TryBuildArguments(realm, concreteParameters, jsArgs, parameterOffset, out var clrArgs))
            {
                continue;
            }

            if (isExtensionMethod)
            {
                clrArgs[0] = target;
            }

            try
            {
                var result = concreteMethod.Invoke(isExtensionMethod ? null : target, clrArgs);
                return this.ToJsValue(realm, result);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        throw new InvalidOperationException(
            $"No overload matches the provided arguments for '{candidates[0].Name}'."
        );
    }

    private bool TryBuildArguments(
        JsRealm realm,
        ParameterInfo[] parameters,
        ReadOnlySpan<JsValue> jsArgs,
        int parameterOffset,
        out object?[] clrArgs)
    {
        var parameterCount = parameters.Length - parameterOffset;
        if (jsArgs.Length > parameterCount)
        {
            clrArgs = Array.Empty<object?>();
            return false;
        }

        for (var i = jsArgs.Length; i < parameterCount; i++)
        {
            if (!parameters[i + parameterOffset].IsOptional)
            {
                clrArgs = Array.Empty<object?>();
                return false;
            }
        }

        clrArgs = new object?[parameters.Length];

        for (var i = jsArgs.Length; i < parameterCount; i++)
        {
            clrArgs[i + parameterOffset] = parameters[i + parameterOffset].DefaultValue;
        }

        for (var i = 0; i < jsArgs.Length; i++)
        {
            if (!this.TryConvertArgument(realm, parameters[i + parameterOffset].ParameterType, jsArgs[i], out var converted))
            {
                clrArgs = Array.Empty<object?>();
                return false;
            }

            clrArgs[i + parameterOffset] = converted;
        }

        return true;
    }

    private bool TryConvertArgument(JsRealm realm, Type parameterType, JsValue value, out object? converted)
    {
        var nullableType = Nullable.GetUnderlyingType(parameterType);
        if (nullableType is not null)
        {
            if (value.IsNullOrUndefined)
            {
                converted = null;
                return true;
            }

            parameterType = nullableType;
        }

        if (parameterType == typeof(JsValue))
        {
            converted = value;
            return true;
        }

        if (parameterType == typeof(string))
        {
            converted = value.IsNull ? null : value.IsString ? value.AsString() : value.ToString();
            return true;
        }

        if (parameterType == typeof(bool))
        {
            if (!value.IsBool)
            {
                converted = null;
                return false;
            }

            converted = value.IsTrue;
            return true;
        }

        if (parameterType.IsEnum)
        {
            try
            {
                converted = value.IsString
                    ? Enum.Parse(parameterType, value.AsString(), false)
                    : Enum.ToObject(parameterType, checked((int) this.ToDouble(value)));
                return true;
            }
            catch
            {
                converted = null;
                return false;
            }
        }

        if (parameterType == typeof(int))
        {
            return this.TryConvertNumeric(value, static number => checked((int) number), out converted);
        }

        if (parameterType == typeof(uint))
        {
            return this.TryConvertNumeric(value, static number => checked((uint) number), out converted);
        }

        if (parameterType == typeof(long))
        {
            return this.TryConvertNumeric(value, static number => checked((long) number), out converted);
        }

        if (parameterType == typeof(ulong))
        {
            return this.TryConvertNumeric(value, static number => checked((ulong) number), out converted);
        }

        if (parameterType == typeof(short))
        {
            return this.TryConvertNumeric(value, static number => checked((short) number), out converted);
        }

        if (parameterType == typeof(ushort))
        {
            return this.TryConvertNumeric(value, static number => checked((ushort) number), out converted);
        }

        if (parameterType == typeof(byte))
        {
            return this.TryConvertNumeric(value, static number => checked((byte) number), out converted);
        }

        if (parameterType == typeof(sbyte))
        {
            return this.TryConvertNumeric(value, static number => checked((sbyte) number), out converted);
        }

        if (parameterType == typeof(float))
        {
            return this.TryConvertNumeric(value, static number => (float) number, out converted);
        }

        if (parameterType == typeof(double))
        {
            return this.TryConvertNumeric(value, static number => number, out converted);
        }

        if (parameterType == typeof(decimal))
        {
            return this.TryConvertNumeric(
                value,
                static number => Convert.ToDecimal(number, CultureInfo.InvariantCulture),
                out converted
            );
        }

        if (parameterType == typeof(object))
        {
            converted = this.ToBoxedValue(value);
            return true;
        }

        if (typeof(Delegate).IsAssignableFrom(parameterType))
        {
            if (value.TryGetObject(out var functionObject) && functionObject is JsFunction function)
            {
                converted = this.CreateDelegate(realm, function, parameterType);
                return true;
            }

            converted = null;
            return false;
        }

        if (value.TryGetObject(out var objectValue))
        {
            if (objectValue is JsHostObject hostObject)
            {
                var unwrapped = this.Unwrap(hostObject.Data);
                if (unwrapped is not null && parameterType.IsInstanceOfType(unwrapped))
                {
                    converted = unwrapped;
                    return true;
                }
            }

            if (parameterType.IsInstanceOfType(objectValue))
            {
                converted = objectValue;
                return true;
            }
        }

        if (value.IsNull && !parameterType.IsValueType)
        {
            converted = null;
            return true;
        }

        converted = null;
        return false;
    }

    private bool TryConvertNumeric<T>(JsValue value, Func<double, T> converter, out object? converted)
    {
        try
        {
            converted = converter(this.ToDouble(value));
            return true;
        }
        catch
        {
            converted = null;
            return false;
        }
    }

    private double ToDouble(JsValue value)
    {
        if (!value.IsNumber)
        {
            throw new InvalidOperationException($"Cannot convert {value} to a numeric CLR type.");
        }

        return value.NumberValue;
    }

    private object? ToBoxedValue(JsValue value)
    {
        if (value.IsNullOrUndefined)
        {
            return null;
        }

        if (value.IsString)
        {
            return value.AsString();
        }

        if (value.IsBool)
        {
            return value.IsTrue;
        }

        if (value.IsInt32)
        {
            return value.Int32Value;
        }

        if (value.IsNumber)
        {
            return value.NumberValue;
        }

        if (!value.TryGetObject(out var obj))
        {
            return value;
        }

        return obj is JsHostObject host ? this.Unwrap(host.Data) : obj;
    }

    private Delegate CreateDelegate(JsRealm realm, JsFunction function, Type delegateType)
    {
        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type '{delegateType}' is not invokable.");
        var parameters = invokeMethod.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
            .ToArray();
        var boxedArgs = Expression.NewArrayInit(
            typeof(object),
            parameters.Select(parameter => Expression.Convert(parameter, typeof(object)))
        );
        var invokeExpression = Expression.Call(
            Expression.Constant(this),
            _invokeJsDelegateMethod,
            Expression.Constant(realm),
            Expression.Constant(function, typeof(JsFunction)),
            boxedArgs,
            Expression.Constant(invokeMethod.ReturnType, typeof(Type))
        );

        Expression body = invokeMethod.ReturnType == typeof(void)
            ? invokeExpression
            : Expression.Convert(invokeExpression, invokeMethod.ReturnType);

        return Expression.Lambda(delegateType, body, parameters).Compile();
    }

    private object? InvokeJavaScriptDelegate(JsRealm realm, JsFunction function, object?[] args, Type returnType)
    {
        var jsArgs = args
            .Select(argument => this.ToJsValue(realm, argument))
            .ToArray();
        var result = realm.Call(function, JsValue.Undefined, jsArgs);

        if (returnType == typeof(void))
        {
            return null;
        }

        return this.TryConvertArgument(realm, returnType, result, out var converted)
            ? converted
            : this.ToBoxedValue(result);
    }

    private object PrepareHostValueIfNeeded(object value)
    {
        return ShouldWrap(value.GetType()) ? this.PrepareHostValue(value) : value;
    }

    private JsArray ToJsArray(JsRealm realm, Array array)
    {
        var indices = new int[array.Rank];
        return this.BuildArrayDimension(realm, array, indices, 0);
    }

    private JsArray BuildArrayDimension(JsRealm realm, Array array, int[] indices, int dimension)
    {
        var length = array.GetLength(dimension);
        var jsArray = new JsArray(realm);

        for (var i = 0; i < length; i++)
        {
            indices[dimension] = i;
            jsArray.SetElement(
                (uint) i,
                dimension == array.Rank - 1
                    ? this.ToJsArrayElement(realm, array.GetValue(indices))
                    : JsValue.FromObject(this.BuildArrayDimension(realm, array, indices, dimension + 1))
            );
        }

        return jsArray;
    }

    private JsArray ToJsArray(JsRealm realm, JsValue[] items)
    {
        var jsArray = new JsArray(realm);
        for (var i = 0; i < items.Length; i++)
        {
            jsArray.SetElement((uint) i, items[i]);
        }

        return jsArray;
    }

    private JsValue ToJsArrayElement(JsRealm realm, object? value)
    {
        value = this.Unwrap(value);

        if (value is null)
        {
            return JsValue.Null;
        }

        if (value is Array nestedArray)
        {
            return JsValue.FromObject(this.ToJsArray(realm, nestedArray));
        }

        return this.ToJsValue(realm, value);
    }
}

internal sealed class OkojoBoundHostObject(object target, OkojoExtensionMethodRegistry registry) : IHostBindable
{
    public object Target { get; } = target;

    public HostBinding GetHostBinding()
    {
        return registry.GetBinding(this.Target.GetType());
    }
}
