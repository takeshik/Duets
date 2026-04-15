using Jint.Native;
using Jint.Runtime;

namespace Duets.Jint;

internal sealed class JintScriptValueAdapter : IScriptValueAdapter
{
    private JintScriptValueAdapter()
    {
    }

    public static readonly JintScriptValueAdapter Instance = new();

    public bool IsUndefined(object rawValue)
    {
        return ((JsValue) rawValue).Equals(JsValue.Undefined);
    }

    public bool IsNull(object rawValue)
    {
        return ((JsValue) rawValue).Equals(JsValue.Null);
    }

    public bool IsObject(object rawValue)
    {
        return ((JsValue) rawValue).Type == Types.Object;
    }

    public object? ToObject(object rawValue)
    {
        return ((JsValue) rawValue).ToObject();
    }

    public string ToDisplayString(object rawValue)
    {
        return ((JsValue) rawValue).ToString();
    }

    public bool AreEqual(object left, object right)
    {
        return ((JsValue) left).Equals((JsValue) right);
    }

    public int GetHashCode(object rawValue)
    {
        return ((JsValue) rawValue).GetHashCode();
    }
}
