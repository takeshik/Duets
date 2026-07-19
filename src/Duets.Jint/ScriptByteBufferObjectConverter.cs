using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets.Jint;

/// <summary>Consumes an owned host byte buffer as a JavaScript <c>Uint8Array</c>.</summary>
internal sealed class ScriptByteBufferObjectConverter : IObjectConverter
{
    public bool TryConvert(Engine engine, object value, out JsValue result)
    {
        if (value is not ScriptByteBuffer buffer)
        {
            result = JsValue.Undefined;
            return false;
        }

        // ScriptByteBuffer explicitly transfers its backing array, so ArrayBuffer can take
        // ownership without another managed allocation or exposing host-owned mutable memory.
        var bytes = buffer.Consume();
        var arrayBuffer = engine.Intrinsics.ArrayBuffer.Construct(bytes);
        result = engine.Intrinsics.Uint8Array.Construct(arrayBuffer, 0, bytes.Length);
        return true;
    }
}
