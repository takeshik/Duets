namespace Duets;

/// <summary>Bidirectional converter between <see cref="ScriptValue"/> and a backend-specific value type.</summary>
public interface IScriptValueConverter<T>
{
    /// <summary>Wraps a backend value in an engine-neutral value.</summary>
    /// <param name="value">The backend value.</param>
    /// <returns>The wrapped value.</returns>
    public ScriptValue Wrap(T value);

    /// <summary>Extracts the backend value represented by an engine-neutral value.</summary>
    /// <param name="value">The engine-neutral value.</param>
    /// <returns>The backend value.</returns>
    public T Unwrap(ScriptValue value);
}
