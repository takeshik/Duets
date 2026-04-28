namespace Duets;

/// <summary>Engine-agnostic contract for executing user code.</summary>
public interface IScriptEngine : IDisposable
{
    bool CanRegisterTypeBuiltins { get; }

    /// <summary>Raised synchronously each time user script calls a <c>console</c> method.</summary>
    event Action<ScriptConsoleEntry>? ConsoleLogged;

    void SetValue(string name, object value);
    void SetValue(string name, ScriptValue value);
    IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables();
    void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations);
    void Execute(string tsCode);
    Task ExecuteAsync(string tsCode, CancellationToken cancellationToken = default);
    ScriptValue Evaluate(string tsCode);
    Task<ScriptValue> EvaluateAsync(string tsCode, CancellationToken cancellationToken = default);
}
