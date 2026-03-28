using System.Reflection;
using Jint;
using Jint.Native;

namespace Duets;

/// <summary>Wrapper around the Jint engine for executing user code. Operates as a separate engine independent of TypeScriptService.</summary>
public class ScriptEngine : IDisposable
{
    public ScriptEngine(Action<Options>? configure, ITranspiler transpiler)
    {
        this.JintEngine = new Engine(configure);
        this._transpiler = transpiler;
        this.InitConsole();
    }

    private readonly ITranspiler _transpiler;
    private readonly object _sync = new();
    private bool _disposed;

    internal Engine JintEngine { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    internal JsValue GetValue(string name)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this.JintEngine.GetValue(name);
        }
    }

    public void SetValue(string name, object value)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this.JintEngine.SetValue(name, value);
        }
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and executes it.</summary>
    public void Execute(string tsCode)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            this.JintEngine.Execute(this._transpiler.Transpile(tsCode));
        }
    }

    /// <summary>Transpiles the TypeScript source to JavaScript and evaluates it.</summary>
    public JsValue Evaluate(string tsCode)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            return this.JintEngine.Evaluate(this._transpiler.Transpile(tsCode));
        }
    }

    public void Dispose()
    {
        lock (this._sync)
        {
            if (this._disposed) return;
            this.JintEngine.Dispose();
            this._disposed = true;
        }
    }

    private void InitConsole()
    {
        this.JintEngine.SetValue(
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
        this.JintEngine.Execute(reader.ReadToEnd());
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}
