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
        this._jintEngine = new Engine(configure);
        this._transpiler = transpiler;
        this.InitConsole();
    }

    private readonly Engine _jintEngine;
    private readonly ITranspiler _transpiler;
    private readonly object _sync = new();
    private bool _disposed;

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
            this._jintEngine.Execute(prepared);
        }
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and evaluates it.</summary>
    public JsValue Evaluate(string tsCode)
    {
        var prepared = Engine.PrepareScript(this._transpiler.Transpile(tsCode));
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this._jintEngine.Evaluate(prepared);
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
