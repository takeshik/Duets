using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets.Jint;

/// <summary>
/// Preserves CLR arrays as wrapped CLR objects so they participate in extension-method
/// dispatch just like other CLR objects.
/// </summary>
internal sealed class ClrArrayObjectConverter : IObjectConverter
{
    public bool TryConvert(Engine engine, object value, out JsValue result)
    {
        if (value is Array array)
        {
            result = ObjectWrapper.Create(engine, array, array.GetType());
            return true;
        }

        result = JsValue.Undefined;
        return false;
    }
}
