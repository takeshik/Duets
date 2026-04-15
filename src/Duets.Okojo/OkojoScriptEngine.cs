using Okojo;
using Okojo.Objects;
using Okojo.Runtime;

namespace Duets.Okojo;

/// <summary>Okojo-backed <see cref="ScriptEngine"/> implementation.</summary>
internal sealed class OkojoScriptEngine : ScriptEngine
{
    public OkojoScriptEngine(Action<JsRuntimeBuilder>? configure, ITranspiler transpiler)
        : base(transpiler)
    {
        var builder = JsRuntime.CreateBuilder();
        configure?.Invoke(builder);

        this._runtime = builder.Build();
        this._realm = this._runtime.MainRealm;
        this.InitCoreBuiltins();
        this.CanRegisterTypeBuiltins = this.ProbeClrInteropAvailability();
        this._predefinedGlobalKeys = this.GetGlobalPropertyNames()
            .Concat(["$_", "$exception"])
            .ToHashSet();
    }

    private readonly JsRuntime _runtime;
    private readonly JsRealm _realm;
    private readonly OkojoExtensionMethodRegistry _extensionMethods = new();
    private readonly IReadOnlyCollection<string> _predefinedGlobalKeys;
    private readonly object _sync = new();
    private bool _disposed;
    private bool _typeBuiltinsRegistered;

    protected override ScriptValue UndefinedValue => new(OkojoScriptValueAdapter.Instance, JsValue.Undefined);

    public override bool CanRegisterTypeBuiltins { get; }

    public override void SetValue(string name, object value)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._realm.Global[name] = this._realm.WrapHostValue(this.PrepareHostValue(value));
        }
    }

    public override IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables()
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            var result = new Dictionary<ScriptValue, ScriptValue>();
            foreach (var key in this.GetGlobalPropertyNames().Where(key => !this._predefinedGlobalKeys.Contains(key)))
            {
                if (this._realm.Global.TryGetValue(key, out var value))
                {
                    result[new ScriptValue(OkojoScriptValueAdapter.Instance, JsValue.FromString(key))] =
                        new ScriptValue(OkojoScriptValueAdapter.Instance, value);
                }
            }

            return result;
        }
    }

    public override void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations)
    {
        var typings = new OkojoScriptTypings(
            this._realm,
            declarations,
            (name, type) => this._realm.Global[name] = this._realm.WrapHostType(type),
            containerType =>
            {
                if (this._extensionMethods.Register(containerType))
                {
                    declarations.RegisterDeclaration(new ClrDeclarationGenerator().GenerateExtensionMethodsTs(containerType));
                }
            }
        );
        this._realm.Global["typings"] = this._realm.WrapHostValue(typings);
        this._typeBuiltinsRegistered = true;

        this._realm.Global["clrTypeOf"] = JsValue.FromObject(
            new JsHostFunction(
                this._realm,
                static (in info) =>
                {
                    var arg = info.Arguments.Length > 0 ? info.GetArgument<object>(0) : null;
                    if (OkojoScriptTypings.TryExtractClrType(arg, out var type))
                    {
                        return info.Realm.WrapHostValue(type);
                    }

                    throw new ArgumentException(
                        "Expected a CLR type reference (e.g., clrTypeOf(System.IO.File)). " +
                        "Make sure AllowClrAccess is configured on the engine."
                    );
                },
                "clrTypeOf",
                1
            )
        );

        declarations.RegisterDeclaration(ScriptEngineResources.LoadScriptEngineInitDts());
    }

    protected override void ExecuteJavaScript(string code)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._realm.Execute(code);
        }
    }

    protected override ScriptValue EvaluateJavaScript(string code)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return new ScriptValue(OkojoScriptValueAdapter.Instance, this._realm.Evaluate(code));
        }
    }

    protected override void SetSpecialValue(string name, ScriptValue value)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._realm.Global[name] = (JsValue) value.RawValue;
        }
    }

    protected override void SetException(Exception exception)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();

            try
            {
                this._realm.Global["$exception"] = this._realm.WrapHostValue(exception);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // Okojo CLR interop may reject raw Exception instances depending on host binding support.
                // Fall back to a stable string representation so $exception always contains usable data.
                this._realm.Global["$exception"] = JsValue.FromString(exception.ToString());
            }
        }
    }

    public override void Dispose()
    {
        lock (this._sync)
        {
            if (this._disposed)
            {
                return;
            }

            this._runtime.Dispose();
            this._disposed = true;
        }
    }

    private IEnumerable<string> GetGlobalPropertyNames()
    {
        return this._realm.GlobalObject.GetEnumerableOwnPropertyNames();
    }

    private void InitCoreBuiltins()
    {
        var consoleImpl = new JsHostFunction(
            this._realm,
            static (in info) =>
            {
                var level = info.GetArgumentStringOrDefault(0, "log") switch
                {
                    "info" => ConsoleLogLevel.Info,
                    "warn" => ConsoleLogLevel.Warn,
                    "error" => ConsoleLogLevel.Error,
                    "debug" => ConsoleLogLevel.Debug,
                    _ => ConsoleLogLevel.Log,
                };
                var text = info.GetArgumentStringOrDefault(1, string.Empty);
                ((OkojoScriptEngine) ((JsHostFunction) info.Function).UserData!).RaiseConsoleLogged(
                    new ScriptConsoleEntry(level, text)
                );
                return JsValue.Undefined;
            },
            "__consoleImpl__",
            2
        )
        {
            UserData = this,
        };
        this._realm.Global["__consoleImpl__"] = JsValue.FromObject(consoleImpl);
        this._realm.Global["__utilToJsArray__"] = JsValue.FromObject(
            new JsHostFunction(
                this._realm,
                (scoped in info) => this._extensionMethods.ToJsArrayValue(
                    info.Realm,
                    info.Arguments.Length > 0 ? this._extensionMethods.Unwrap(info.GetArgument<object>(0)) : null
                ),
                "__utilToJsArray__",
                1
            )
        );

        this._realm.Global["importNamespace"] = JsValue.FromObject(
            new JsHostFunction(
                this._realm,
                static (in info) =>
                {
                    var ns = info.GetArgumentString(0);
                    return JsValue.FromObject(info.Realm.GetClrNamespace(ns));
                },
                "importNamespace",
                1
            )
        );

        this._realm.Execute(ScriptEngineResources.LoadScriptEngineInitJs());
    }

    private void ThrowIfDisposed()
    {
#if NETSTANDARD2_1
        if (this._disposed) throw new ObjectDisposedException(this.GetType().FullName);
#else
        ObjectDisposedException.ThrowIf(this._disposed, this);
#endif
    }

    private object PrepareHostValue(object value)
    {
        return this._typeBuiltinsRegistered ? this._extensionMethods.PrepareHostValue(value) : value;
    }

    private bool ProbeClrInteropAvailability()
    {
        try
        {
            _ = this._realm.GetClrNamespace("System");
            _ = this._realm.WrapHostType(typeof(string));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
