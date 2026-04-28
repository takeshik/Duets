namespace Duets;

/// <summary>Bidirectional converter between <see cref="ScriptValue"/> and a backend-specific value type.</summary>
public interface IScriptValueConverter<T>
{
    ScriptValue Wrap(T value);

    T Unwrap(ScriptValue value);
}
