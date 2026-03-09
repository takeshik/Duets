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

    public void SetValue(string name, object value)
    {
        this._engine.SetValue(name, value);
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and executes it.</summary>
    public void Execute(string tsCode)
    {
        this._engine.Execute(this._transpiler.Transpile(tsCode));
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and evaluates it.</summary>
    public JsValue Evaluate(string tsCode)
    {
        return this._engine.Evaluate(this._transpiler.Transpile(tsCode));
    }

    public void Dispose()
    {
        this._engine.Dispose();
    }
}
