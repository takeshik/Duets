namespace Duets;

/// <summary>
/// Converter-aware abstract base for backend implementations.
/// Backends inherit from this class and implement the engine-specific hooks.
/// </summary>
public abstract class ScriptEngine<TValue>(
    ITranspiler transpiler,
    IScriptValueConverter<TValue> converter
) : IScriptEngine
{
    /// <summary>Gets the transpiler used before engine execution.</summary>
    protected ITranspiler Transpiler { get; } = transpiler;

    /// <summary>Gets the converter between backend and engine-neutral values.</summary>
    protected IScriptValueConverter<TValue> Converter { get; } = converter;

    /// <inheritdoc />
    public abstract bool CanRegisterTypeBuiltins { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    /// <summary>Sets a script global to a backend-native value.</summary>
    /// <param name="name">The script global name.</param>
    /// <param name="value">The backend-native value.</param>
    protected abstract void SetValue(string name, TValue value);

    /// <summary>Executes JavaScript source in the backend.</summary>
    /// <param name="code">The JavaScript source to execute.</param>
    protected abstract void ExecuteJs(string code);

    /// <summary>Asynchronously executes JavaScript source in the backend.</summary>
    /// <param name="code">The JavaScript source to execute.</param>
    /// <param name="cancellationToken">A token that cancels asynchronous execution.</param>
    protected abstract Task ExecuteJsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Evaluates JavaScript source in the backend.</summary>
    /// <param name="code">The JavaScript source to evaluate.</param>
    /// <returns>The backend-native evaluation result.</returns>
    protected abstract TValue EvaluateJs(string code);

    /// <summary>Asynchronously evaluates JavaScript source in the backend.</summary>
    /// <param name="code">The JavaScript source to evaluate.</param>
    /// <param name="cancellationToken">A token that cancels asynchronous evaluation.</param>
    /// <returns>The backend-native evaluation result.</returns>
    protected abstract Task<TValue> EvaluateJsAsync(
        string code,
        CancellationToken cancellationToken
    );

    /// <summary>Raises <see cref="ConsoleLogged"/> for a backend console entry.</summary>
    /// <param name="entry">The console entry.</param>
    protected void RaiseConsoleLogged(ScriptConsoleEntry entry)
    {
        this.ConsoleLogged?.Invoke(entry);
    }

    /// <inheritdoc />
    public abstract void Dispose();

    /// <inheritdoc />
    public abstract void SetValue(string name, object value);

    /// <inheritdoc />
    public void SetValue(string name, ScriptValue value)
    {
        this.SetValue(name, this.Converter.Unwrap(value));
    }

    /// <inheritdoc />
    public abstract IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables();

    /// <inheritdoc />
    public abstract void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations);

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<ScriptValue> EvaluateAsync(
        string tsCode,
        CancellationToken cancellationToken = default
    )
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
