namespace Duets;

/// <summary>
/// Converter-aware abstract base for backend implementations.
/// Backends inherit from this class and implement the engine-specific hooks.
/// </summary>
public abstract class ScriptEngine<TValue> : IScriptEngine
{
    protected ScriptEngine(ITranspiler transpiler, IScriptValueConverter<TValue> converter)
    {
        this.Transpiler = transpiler;
        this.Converter = converter;
    }

    protected ITranspiler Transpiler { get; }
    protected IScriptValueConverter<TValue> Converter { get; }

    public abstract bool CanRegisterTypeBuiltins { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    protected abstract void SetValue(string name, TValue value);

    protected abstract void ExecuteJs(string code);

    protected abstract Task ExecuteJsAsync(string code, CancellationToken cancellationToken);

    protected abstract TValue EvaluateJs(string code);

    protected abstract Task<TValue> EvaluateJsAsync(string code, CancellationToken cancellationToken);

    protected void RaiseConsoleLogged(ScriptConsoleEntry entry)
    {
        this.ConsoleLogged?.Invoke(entry);
    }

    public abstract void Dispose();

    public abstract void SetValue(string name, object value);

    public void SetValue(string name, ScriptValue value)
    {
        this.SetValue(name, this.Converter.Unwrap(value));
    }

    public abstract IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables();

    public abstract void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations);

    public void Execute(string tsCode)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            this.ExecuteJs(jsCode);
            this.SetValue("$_", ScriptValue.Undefined);
            this.SetValue("$exception", ScriptValue.Undefined);
        }
        catch (Exception ex)
        {
            this.SetValue("$_", ScriptValue.Undefined);
            this.SetValue("$exception", ex);
            throw;
        }
    }

    public async Task ExecuteAsync(string tsCode, CancellationToken cancellationToken = default)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            await this.ExecuteJsAsync(jsCode, cancellationToken);
            this.SetValue("$_", ScriptValue.Undefined);
            this.SetValue("$exception", ScriptValue.Undefined);
        }
        catch (Exception ex)
        {
            this.SetValue("$_", ScriptValue.Undefined);
            this.SetValue("$exception", ex);
            throw;
        }
    }

    public ScriptValue Evaluate(string tsCode)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            var ret = this.Converter.Wrap(this.EvaluateJs(jsCode));
            this.SetValue("$_", ret);
            this.SetValue("$exception", ScriptValue.Undefined);
            return ret;
        }
        catch (Exception ex)
        {
            this.SetValue("$_", ScriptValue.Undefined);
            this.SetValue("$exception", ex);
            throw;
        }
    }

    public async Task<ScriptValue> EvaluateAsync(string tsCode, CancellationToken cancellationToken = default)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            var ret = this.Converter.Wrap(await this.EvaluateJsAsync(jsCode, cancellationToken));
            this.SetValue("$_", ret);
            this.SetValue("$exception", ScriptValue.Undefined);
            return ret;
        }
        catch (Exception ex)
        {
            this.SetValue("$_", ScriptValue.Undefined);
            this.SetValue("$exception", ex);
            throw;
        }
    }
}
