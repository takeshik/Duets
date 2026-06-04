namespace Duets;

/// <summary>Bidirectional converter between <see cref="ScriptValue"/> and a backend-specific value type.</summary>
public interface IScriptValueConverter<T>
{
    public ScriptValue Wrap(T value);

    public T Unwrap(ScriptValue value);
}
