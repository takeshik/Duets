namespace Duets.Pad.Rendering;

/// <summary>
/// Factory for output-error marker elements. An output error is represented as an
/// <c>Element</c> with the <c>duetspad-output-error</c> class (not a new terminal node kind).
/// </summary>
internal static class OutputError
{
    /// <summary>
    /// Creates a display-only error element wrapping <paramref name="message" />.
    /// </summary>
    public static Element Create(string message) =>
        new(
            "div",
            new ElementAttributes(
                new KeyValuePair<string, string?>("class", "duetspad-output-error")
            ),
            new ElementChildren(new Text(message))
        );
}
