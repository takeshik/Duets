using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets;

/// <summary>Wrapper around the Jint engine for executing user code. Operates as a separate engine independent of TypeScriptService.</summary>
public class ScriptEngine : IDisposable
{
    public ScriptEngine(Action<Options>? configure, ITranspiler transpiler)
    {
        var extensionMethods = new ExtensionMethodRegistry();
        this.ExtensionMethods = extensionMethods;

        this._jintEngine = new Engine(opts =>
            {
                configure?.Invoke(opts);
                opts.AddObjectConverter(new ClrArrayObjectConverter());

                // Chain with any host-provided MemberAccessor: host runs first, then extension methods.
                var hostAccessor = opts.Interop.MemberAccessor;
                opts.Interop.MemberAccessor = (engine, target, member) =>
                    hostAccessor(engine, target, member)
                    ?? extensionMethods.CreateMemberValue(engine, target, member);
            }
        );

        this._transpiler = transpiler;
        this.InitConsole();

        this._predefinedGlobalKeys = this._jintEngine
            .Global
            .GetOwnProperties()
            .Where(x => x.Key.IsString())
            .Select(x => x.Key.AsString())
            .Concat(["$_", "$exception"]) // defined by ScriptEngine itself
            .ToHashSet();
    }

    private readonly Engine _jintEngine;
    private readonly ITranspiler _transpiler;
    private readonly IReadOnlyCollection<string> _predefinedGlobalKeys;
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>Registry of extension method container types made available via <c>MemberAccessor</c>.</summary>
    internal ExtensionMethodRegistry ExtensionMethods { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    internal JsValue GetValue(string name)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this._jintEngine.GetValue(name);
        }
    }

    public void SetValue(string name, object value)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._jintEngine.SetValue(name, value);
        }
    }

    public IReadOnlyDictionary<JsValue, JsValue> GetGlobalVariables()
    {
        lock (this._sync)
        {
            return this._jintEngine
                .Global
                .GetOwnProperties()
                .Where(x => !this._predefinedGlobalKeys.Contains(x.Key.ToString()))
                .ToDictionary(x => x.Key, x => x.Value.Value);
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

    /// <summary>Transpiles the TypeScript source to JavaScript and executes it.</summary>
    public void Execute(string tsCode)
    {
        var prepared = Engine.PrepareScript(this._transpiler.Transpile(tsCode));
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            try
            {
                this._jintEngine.Execute(prepared);
                this._jintEngine.SetValue("$_", JsValue.Undefined);
                this._jintEngine.SetValue("$exception", JsValue.Undefined);
            }
            catch (Exception ex)
            {
                this._jintEngine.SetValue("$_", JsValue.Undefined);
                this._jintEngine.SetValue("$exception", ex);
                throw;
            }
        }
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and evaluates it.</summary>
    public JsValue Evaluate(string tsCode)
    {
        var prepared = Engine.PrepareScript(this._transpiler.Transpile(tsCode));
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            try
            {
                var ret = this._jintEngine.Evaluate(prepared);
                this._jintEngine.SetValue("$_", ret);
                this._jintEngine.SetValue("$exception", JsValue.Undefined);
                return ret;
            }
            catch (Exception ex)
            {
                this._jintEngine.SetValue("$_", JsValue.Undefined);
                this._jintEngine.SetValue("$exception", ex);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (this._sync)
        {
            if (this._disposed) return;
            this._jintEngine.Dispose();
            this._disposed = true;
        }
    }

    private void InitConsole()
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
                    this.ConsoleLogged?.Invoke(new ScriptConsoleEntry(level, text));
                }
            )
        );
        this._jintEngine.SetValue(
            "__utilToJsArray__",
            new Func<JsValue, JsValue>(value => JsArrayConverter.ToJsArray(this._jintEngine, value))
        );

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Duets.Resources.ScriptEngineInit.js")!;
        using var reader = new StreamReader(stream);
        this._jintEngine.Execute(reader.ReadToEnd());
    }

    private void ThrowIfDisposed()
    {
#if NETSTANDARD2_1
        if (this._disposed) throw new ObjectDisposedException(this.GetType().FullName);
#else
        ObjectDisposedException.ThrowIf(this._disposed, this);
#endif
    }
}
