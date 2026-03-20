using Jint;
using Jint.Native;

namespace Duets;

/// <summary>Wrapper around the Jint engine for executing user code. Operates as a separate engine independent of TypeScriptService.</summary>
public class ScriptEngine : IDisposable
{
    public ScriptEngine(Action<Options>? configure, ITranspiler transpiler)
    {
        this._engine = new Engine(configure);
        this._transpiler = transpiler;
    }

    private readonly Engine _engine;
    private readonly ITranspiler _transpiler;
    private readonly object _sync = new();
    private bool _disposed;

    public void SetValue(string name, object value)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._engine.SetValue(name, value);
        }
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and executes it.</summary>
    public void Execute(string tsCode)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this._engine.Execute(this._transpiler.Transpile(tsCode));
        }
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and evaluates it.</summary>
    public JsValue Evaluate(string tsCode)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this._engine.Evaluate(this._transpiler.Transpile(tsCode));
        }
    }

    public void Dispose()
    {
        lock (this._sync)
        {
            if (this._disposed) return;
            this._engine.Dispose();
            this._disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}
