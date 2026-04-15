namespace Duets;

/// <summary>Engine-neutral wrapper around a JavaScript value.</summary>
public sealed class ScriptValue : IEquatable<ScriptValue>
{
    public ScriptValue(IScriptValueAdapter adapter, object rawValue)
    {
        this._adapter = adapter;
        this.RawValue = rawValue;
    }

    private readonly IScriptValueAdapter _adapter;

    public object RawValue { get; }

    public override string ToString()
    {
        return this._adapter.ToDisplayString(this.RawValue);
    }

    public override bool Equals(object? obj)
    {
        return this.Equals(obj as ScriptValue);
    }

    public override int GetHashCode()
    {
        return this._adapter.GetHashCode(this.RawValue);
    }

    public bool IsUndefined()
    {
        return this._adapter.IsUndefined(this.RawValue);
    }

    public bool IsNull()
    {
        return this._adapter.IsNull(this.RawValue);
    }

    public bool IsObject()
    {
        return this._adapter.IsObject(this.RawValue);
    }

    public object? ToObject()
    {
        return this._adapter.ToObject(this.RawValue);
    }

    public bool Equals(ScriptValue? other)
    {
        return other is not null
            && ReferenceEquals(this._adapter, other._adapter)
            && this._adapter.AreEqual(this.RawValue, other.RawValue);
    }
}
