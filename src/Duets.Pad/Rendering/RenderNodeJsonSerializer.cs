using System.Text.Json.Nodes;

namespace Duets.Pad.Rendering;

/// <summary>
/// Serializes terminal render nodes to the DuetsPad wire-protocol JSON shapes:
/// <list type="bullet">
///   <item><description><c>text</c> — <c>{ kind, value }</c></description></item>
///   <item><description><c>element</c> — <c>{ kind, tag, attributes, children[] }</c></description></item>
///   <item><description><c>rawHtml</c> — <c>{ kind, content }</c></description></item>
/// </list>
/// </summary>
internal static class RenderNodeJsonSerializer
{
    private static readonly RenderTreeReducer Reducer = new();

    /// <summary>
    /// Reduces <paramref name="node" /> and serializes it to a JSON representation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the (reduced) node kind is not one of the three supported protocol types:
    /// <see cref="Text" />, <see cref="Element" />, or <see cref="RawHtml" />.
    /// </exception>
    public static JsonNode Serialize(IRenderNode node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        var terminal = Reducer.Reduce(node);

        return SerializeTerminal(terminal);
    }

    private static JsonNode SerializeTerminal(ITerminalRenderNode terminal) =>
        terminal switch
        {
            Text text => SerializeText(text),
            Element element => SerializeElement(element),
            RawHtml rawHtml => SerializeRawHtml(rawHtml),
            _ => throw new InvalidOperationException(
                $"Terminal render node kind '{terminal.GetType().FullName}' is not supported by the DuetsPad wire protocol."
            ),
        };

    private static JsonObject SerializeText(Text text) =>
        new() { ["kind"] = "text", ["value"] = text.Value };

    private static JsonObject SerializeElement(Element element)
    {
        var attributesNode = new JsonObject();

        foreach (var attribute in element.Attributes)
        {
            attributesNode[attribute.Key] = attribute.Value is not null
                ? JsonValue.Create(attribute.Value)
                : null;
        }

        var childrenNode = new JsonArray();

        foreach (var child in element.Children)
        {
            childrenNode.Add(SerializeTerminal(child));
        }

        return new JsonObject
        {
            ["kind"] = "element",
            ["tag"] = element.Tag,
            ["attributes"] = attributesNode,
            ["children"] = childrenNode,
        };
    }

    private static JsonObject SerializeRawHtml(RawHtml rawHtml) =>
        new() { ["kind"] = "rawHtml", ["content"] = rawHtml.Content };
}
