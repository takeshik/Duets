using Jint.Native;
using Jint.Runtime;

namespace Duets.Jint;

internal sealed class JintScriptValue : ScriptValue
{
    internal JintScriptValue(JsValue value)
    {
        this.Value = value;
    }

    internal JsValue Value { get; }

    public override object? ToObject()
    {
        return this.Value.ToObject();
    }

    public override string ToString()
    {
        return this.Value.ToString();
    }

    protected override bool EqualsCore(ScriptValue other)
    {
        if (other is JintScriptValue jv) return this.Value.Equals(jv.Value);
        if (ReferenceEquals(other, Null)) return this.Value.Type == Types.Null;
        if (ReferenceEquals(other, Undefined)) return this.Value.Type == Types.Undefined;
        throw new InvalidOperationException("Cannot compare ScriptValues from different backends.");
    }

    protected override int GetHashCodeCore()
    {
        if (this.Value.Type == Types.Null) return 1;
        if (this.Value.Type == Types.Undefined) return 0;
        return this.Value.GetHashCode();
    }
}
