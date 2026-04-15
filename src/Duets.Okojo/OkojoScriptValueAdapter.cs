using Okojo;
using Okojo.Objects;

namespace Duets.Okojo;

internal sealed class OkojoScriptValueAdapter : IScriptValueAdapter
{
    private OkojoScriptValueAdapter()
    {
    }

    public static readonly OkojoScriptValueAdapter Instance = new();

    public bool IsUndefined(object rawValue)
    {
        return ((JsValue) rawValue).IsUndefined;
    }

    public bool IsNull(object rawValue)
    {
        return ((JsValue) rawValue).IsNull;
    }

    public bool IsObject(object rawValue)
    {
        return ((JsValue) rawValue).IsObject;
    }

    public object? ToObject(object rawValue)
    {
        var value = (JsValue) rawValue;
        if (value.IsNullOrUndefined)
        {
            return null;
        }

        if (value.IsString)
        {
            return value.AsString();
        }

        if (value.IsBool)
        {
            return value.IsTrue;
        }

        if (value.IsInt32)
        {
            return value.Int32Value;
        }

        if (value.IsNumber)
        {
            return value.NumberValue;
        }

        if (!value.TryGetObject(out var obj))
        {
            return value;
        }

        if (obj is JsHostObject host)
        {
            return host.Data is OkojoBoundHostObject bound ? bound.Target : host.Data;
        }

        return obj;
    }

    public string ToDisplayString(object rawValue)
    {
        return ((JsValue) rawValue).ToString() ?? string.Empty;
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
