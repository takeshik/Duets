using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Duets.Jint;

/// <summary>Jint-backed <see cref="IScriptEngine"/> implementation.</summary>
internal sealed class JintScriptEngine : ScriptEngine<JsValue>
{
    public JintScriptEngine(Action<Options>? configure, ITranspiler transpiler)
        : base(transpiler, JintScriptValueConverter.Instance)
    {
        var extensionMethods = new ExtensionMethodRegistry();
        this.ExtensionMethods = extensionMethods;

        this._jintEngine = new Engine(opts =>
            {
                configure?.Invoke(opts);
                opts.AddObjectConverter(new ClrArrayObjectConverter());

                var hostAccessor = opts.Interop.MemberAccessor;
                opts.Interop.MemberAccessor = (engine, target, member) =>
                    hostAccessor(engine, target, member)
                    ?? extensionMethods.CreateMemberValue(engine, target, member);
            }
        );

        this.InitCoreBuiltins();
        this._predefinedGlobalKeys = this._jintEngine
            .Global
            .GetOwnProperties()
            .Where(x => x.Key.IsString())
            .Select(x => x.Key.AsString())
            .Concat(["$_", "$exception"])
            .ToHashSet();
    }

    private readonly Engine _jintEngine;
    private readonly IReadOnlyCollection<string> _predefinedGlobalKeys;
    private readonly object _sync = new();
    private bool _disposed;

    public override bool CanRegisterTypeBuiltins => !this.GetValue("importNamespace").Equals(JsValue.Undefined);

    /// <summary>Registry of extension method container types made available via <c>MemberAccessor</c>.</summary>
    internal ExtensionMethodRegistry ExtensionMethods { get; }

    public override void SetValue(string name, object value)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._jintEngine.SetValue(name, value);
        }
    }

    protected override void SetValue(string name, JsValue value)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._jintEngine.SetValue(name, value);
        }
    }

    public override IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables()
    {
        lock (this._sync)
        {
            return this._jintEngine
                .Global
                .GetOwnProperties()
                .Where(x => !this._predefinedGlobalKeys.Contains(x.Key.ToString()))
                .ToDictionary(
                    x => this.Converter.Wrap(x.Key),
                    x => this.Converter.Wrap(x.Value.Value)
                );
        }
    }

    public override void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations)
    {
        var originalImportNs = this.GetValue("importNamespace");
        Func<JsValue, JsValue>? importNsFn = !originalImportNs.Equals(JsValue.Undefined)
            ? ns => this.Call(originalImportNs, ns)
            : null;

        Action<Type> registerExtensionMethods = containerType =>
        {
            if (this.ExtensionMethods.Register(containerType))
            {
                declarations.RegisterExtensionMethodContainer(containerType);
            }
        };

        var typings = new ScriptTypings(declarations, importNsFn, this.SetTypeReferenceValue, registerExtensionMethods);
        this.SetValue("typings", typings);

        this.SetValue(
            "clrTypeOf",
            new Func<JsValue, object>(jsValue =>
                {
                    if (jsValue is TypeReference tr) return tr.ReferenceType;
                    throw new ArgumentException(
                        "Expected a CLR type reference (e.g., clrTypeOf(System.IO.File)). " +
                        "Make sure AllowClr is configured on the engine."
                    );
                }
            )
        );

        declarations.RegisterDeclaration(ScriptEngineResources.LoadScriptEngineInitDts());
    }

    protected override void ExecuteJs(string code)
    {
        var prepared = Engine.PrepareScript(code);
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._jintEngine.Execute(prepared);
        }
    }

    protected override Task ExecuteJsAsync(string code, CancellationToken cancellationToken)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this._jintEngine.ExecuteAsync(code, cancellationToken: cancellationToken);
        }
    }

    protected override JsValue EvaluateJs(string code)
    {
        var prepared = Engine.PrepareScript(code);
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this._jintEngine.Evaluate(prepared);
        }
    }

    protected override async Task<JsValue> EvaluateJsAsync(
        string code, CancellationToken cancellationToken)
    {
        var prepared = Engine.PrepareScript(code);
        Task<JsValue> ret;
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            ret = this._jintEngine.EvaluateAsync(prepared, cancellationToken);
        }

        return await ret;
    }

    public override void Dispose()
    {
        lock (this._sync)
        {
            if (this._disposed) return;
            this._jintEngine.Dispose();
            this._disposed = true;
        }
    }

    internal JsValue GetValue(string name)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this._jintEngine.GetValue(name);
        }
    }

    internal JsValue Call(JsValue callee, JsValue arg)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this._jintEngine.Call(callee, arg);
        }
    }

    internal void SetTypeReferenceValue(string name, Type type)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            var typeRef = TypeReference.CreateTypeReference(this._jintEngine, type);
            this._jintEngine.SetValue(name, typeRef);
        }
    }

    private void InitCoreBuiltins()
    {
        this._jintEngine.SetValue(
            "__consoleImpl__",
            new Action<string, string>((levelStr, text) =>
                {
                    var level = levelStr switch
                    {
                        "info" => ConsoleLogLevel.Info,
                        "warn" => ConsoleLogLevel.Warn,
                        "error" => ConsoleLogLevel.Error,
                        "debug" => ConsoleLogLevel.Debug,
                        _ => ConsoleLogLevel.Log,
                    };
                    this.RaiseConsoleLogged(new ScriptConsoleEntry(level, text));
                }
            )
        );
        this._jintEngine.SetValue(
            "__utilToJsArray__",
            new Func<JsValue, JsValue>(value => JsArrayConverter.ToJsArray(this._jintEngine, value))
        );

        this._jintEngine.Execute(ScriptEngineResources.LoadScriptEngineInitJs());
    }

    private void ThrowIfDisposed()
    {
#if NETSTANDARD2_1
        if (this._disposed) throw new ObjectDisposedException(this.GetType().FullName);
#else
        ObjectDisposedException.ThrowIf(this._disposed, this);
#endif
    }

    private sealed class JintScriptValueConverter : IScriptValueConverter<JsValue>
    {
        internal static JintScriptValueConverter Instance { get; } = new();

        public ScriptValue Wrap(JsValue value)
        {
            return value.Type switch
            {
                Types.Undefined => ScriptValue.Undefined,
                Types.Null => ScriptValue.Null,
                _ => new JintScriptValue(value),
            };
        }

        public JsValue Unwrap(ScriptValue value)
        {
            return value switch
            {
                JintScriptValue jv => jv.Value,
                _ when value == ScriptValue.Undefined => JsValue.Undefined,
                _ when value == ScriptValue.Null => JsValue.Null,
                _ => throw new ArgumentException("ScriptValue from incompatible backend."),
            };
        }
    }
}
