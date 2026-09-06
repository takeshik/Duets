namespace Duets;

/// <summary>Engine-agnostic contract for executing user code.</summary>
public interface IScriptEngine : IDisposable
{
    /// <summary>Gets whether this engine can install the built-in type registration API.</summary>
    public bool CanRegisterTypeBuiltins { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    /// <summary>Sets a script global to a CLR value.</summary>
    /// <param name="name">The script global name.</param>
    /// <param name="value">The CLR value to expose.</param>
    public void SetValue(string name, object value);

    /// <summary>Sets a script global to an engine-neutral script value.</summary>
    /// <param name="name">The script global name.</param>
    /// <param name="value">The value to expose.</param>
    public void SetValue(string name, ScriptValue value);

    /// <summary>Returns a snapshot of globals defined by evaluated user code.</summary>
    /// <returns>The current user-defined global names and values.</returns>
    public IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables();

    /// <summary>Installs the engine's built-in type registration API.</summary>
    /// <param name="declarations">The declaration store updated by the built-ins.</param>
    public void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations);

    /// <summary>Transpiles and executes source code.</summary>
    /// <param name="tsCode">The TypeScript source to execute.</param>
    public void Execute(string tsCode);

    /// <summary>Transpiles and asynchronously executes source code.</summary>
    /// <param name="tsCode">The TypeScript source to execute.</param>
    /// <param name="cancellationToken">A token that cancels asynchronous execution.</param>
    public Task ExecuteAsync(string tsCode, CancellationToken cancellationToken = default);

    /// <summary>Transpiles and evaluates source code.</summary>
    /// <param name="tsCode">The TypeScript source to evaluate.</param>
    /// <returns>The evaluation result.</returns>
    public ScriptValue Evaluate(string tsCode);

    /// <summary>Transpiles and asynchronously evaluates source code.</summary>
    /// <param name="tsCode">The TypeScript source to evaluate.</param>
    /// <param name="cancellationToken">A token that cancels asynchronous evaluation.</param>
    /// <returns>The resolved evaluation result.</returns>
    public Task<ScriptValue> EvaluateAsync(
        string tsCode,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Optional backend contract for installing runtime tagged-template functions.</summary>
public interface ITaggedTemplateScriptEngine
{
    /// <summary>Registers or replaces a script global tagged-template function.</summary>
    public void RegisterTaggedTemplate(
        string tag,
        Duets.Completions.TemplateEvaluationCallback evaluate
    );

    /// <summary>Removes a script global tagged-template function if present.</summary>
    public void UnregisterTaggedTemplate(string tag);
}
