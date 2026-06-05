using System.Text.Json.Nodes;
using Duets.Pad.Rendering;

namespace Duets.Pad.State;

/// <summary>
/// Serializes <see cref="CanvasState" /> to the DuetsPad wire JSON format.
/// </summary>
/// <remarks>
/// Serializes <see cref="CanvasState.Root" /> by delegating to
/// <see cref="RenderNodeJsonSerializer" />, which reduces the tree to terminal nodes and emits
/// the fixed wire JSON forms:
///
/// - Text: { "kind": "text", "value": "..." }
/// - Element: { "kind": "element", "tag": "...", "attributes": { ... }, "children": [ ... ] }
/// - RawHtml: { "kind": "rawHtml", "content": "..." }
///
/// The output preserves the Text/Element/RawHtml tree shape and Element attribute null values
/// (so the browser projection decides how to represent boolean attributes), is deterministic
/// from <c>ElementAttributes</c>/<c>ElementChildren</c> enumeration, and rejects terminal node
/// kinds the protocol does not support. It does not sanitize HTML, does not reduce high-level
/// nodes itself (the render-node serializer does), introduces no handler/reset/client-owned
/// state semantics, and is internal so the wire schema is not accidentally public API.
/// </remarks>
internal sealed class CanvasSerializer
{
    /// <summary>
    /// Serializes the root element of <paramref name="state" /> to a JSON object.
    /// </summary>
    public JsonObject Serialize(CanvasState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var node = RenderNodeJsonSerializer.Serialize(state.Root);

        return (JsonObject)node;
    }
}
