namespace Duets;

/// <summary>
/// Engine-agnostic wrapper for executing user code.
/// Concrete JavaScript runtime integrations live in separate backend packages.
/// </summary>
public abstract class ScriptEngine : IDisposable
{
    protected ScriptEngine(ITranspiler transpiler)
    {
        this.Transpiler = transpiler;
    }

    protected ITranspiler Transpiler { get; }

    protected abstract ScriptValue UndefinedValue { get; }

    public abstract bool CanRegisterTypeBuiltins { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    public abstract void SetValue(string name, object value);

    public abstract IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables();

    public abstract void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations);

    public void Execute(string tsCode)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            this.ExecuteJavaScript(jsCode);
            this.SetSpecialValue("$_", this.UndefinedValue);
            this.SetSpecialValue("$exception", this.UndefinedValue);
        }
        catch (Exception ex)
        {
            this.SetSpecialValue("$_", this.UndefinedValue);
            this.SetException(ex);
            throw;
        }
    }

    public async Task ExecuteAsync(string tsCode, CancellationToken cancellationToken = default)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            await this.ExecuteJavaScriptAsync(jsCode, cancellationToken);
            this.SetSpecialValue("$_", this.UndefinedValue);
            this.SetSpecialValue("$exception", this.UndefinedValue);
        }
        catch (Exception ex)
        {
            this.SetSpecialValue("$_", this.UndefinedValue);
            this.SetException(ex);
            throw;
        }
    }

    public ScriptValue Evaluate(string tsCode)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            var ret = this.EvaluateJavaScript(jsCode);
            this.SetSpecialValue("$_", ret);
            this.SetSpecialValue("$exception", this.UndefinedValue);
            return ret;
        }
        catch (Exception ex)
        {
            this.SetSpecialValue("$_", this.UndefinedValue);
            this.SetException(ex);
            throw;
        }
    }

    public async Task<ScriptValue> EvaluateAsync(string tsCode, CancellationToken cancellationToken = default)
    {
        var jsCode = this.Transpiler.Transpile(tsCode);

        try
        {
            var ret = await this.EvaluateJavaScriptAsync(jsCode, cancellationToken);
            this.SetSpecialValue("$_", ret);
            this.SetSpecialValue("$exception", this.UndefinedValue);
            return ret;
        }
        catch (Exception ex)
        {
            this.SetSpecialValue("$_", this.UndefinedValue);
            this.SetException(ex);
            throw;
        }
    }

    protected abstract void ExecuteJavaScript(string code);

    protected abstract Task ExecuteJavaScriptAsync(string code, CancellationToken cancellationToken);

    protected abstract ScriptValue EvaluateJavaScript(string code);

    protected abstract Task<ScriptValue> EvaluateJavaScriptAsync(string code, CancellationToken cancellationToken);

    protected abstract void SetSpecialValue(string name, ScriptValue value);

    protected abstract void SetException(Exception exception);

    protected void RaiseConsoleLogged(ScriptConsoleEntry entry)
    {
        this.ConsoleLogged?.Invoke(entry);
    }

    public abstract void Dispose();
}
