namespace Duets;

/// <summary>Engine-agnostic contract for executing user code.</summary>
public interface IScriptEngine : IDisposable
{
    public bool CanRegisterTypeBuiltins { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    public event Action<ScriptConsoleEntry>? ConsoleLogged;

    public void SetValue(string name, object value);
    public void SetValue(string name, ScriptValue value);
    public IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables();
    public void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations);
    public void Execute(string tsCode);
    public Task ExecuteAsync(string tsCode, CancellationToken cancellationToken = default);
    public ScriptValue Evaluate(string tsCode);
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
