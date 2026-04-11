using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets;

internal static class JsArrayConverter
{
    public static JsValue ToJsArray(Engine engine, JsValue value)
    {
        if (value.IsArray())
        {
            return value;
        }

        if (value is ObjectWrapper { Target: Array array })
        {
            return ToJsArray(engine, array);
        }

        throw new ArgumentException("Expected a CLR array or a JavaScript array.");
    }

    private static JsArray ToJsArray(Engine engine, Array array)
    {
        var indices = new int[array.Rank];
        return BuildDimension(engine, array, indices, 0);
    }

    private static JsArray BuildDimension(Engine engine, Array array, int[] indices, int dimension)
    {
        var length = array.GetLength(dimension);
        var items = new JsValue[length];

        for (var i = 0; i < length; i++)
        {
            indices[dimension] = i;
            items[i] = dimension == array.Rank - 1
                ? ConvertElement(engine, array.GetValue(indices))
                : BuildDimension(engine, array, indices, dimension + 1);
        }

        return new JsArray(engine, items);
    }

    private static JsValue ConvertElement(Engine engine, object? value)
    {
        if (value is null) return JsValue.Null;
        if (value is Array nestedArray) return ToJsArray(engine, nestedArray);
        return JsValue.FromObject(engine, value);
    }
}
