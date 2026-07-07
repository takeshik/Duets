namespace Duets.Pad.Rendering;

/// <summary>
/// The control kind of a <see cref="DisplayInput"/> field, encoded on the wire as the
/// <c>data-duetspad-field-kind</c> marker attribute (ADR-47).
/// </summary>
internal enum FieldKind
{
    Text,
    TextArea,
    Number,
    CheckBox,
    DropDown,
    Slider,
    Radio,
}

internal static class FieldKindExtensions
{
    /// <summary>
    /// Returns the <c>data-duetspad-field-kind</c> attribute value for <paramref name="kind"/>.
    /// </summary>
    public static string ToAttributeValue(this FieldKind kind) =>
        kind switch
        {
            FieldKind.Text => "text",
            FieldKind.TextArea => "textarea",
            FieldKind.Number => "number",
            FieldKind.CheckBox => "checkbox",
            FieldKind.DropDown => "dropdown",
            FieldKind.Slider => "slider",
            FieldKind.Radio => "radio",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    /// <summary>
    /// Parses a <c>data-duetspad-field-kind</c> attribute value back into a <see cref="FieldKind"/>.
    /// Used to resolve a marker's kind when it was not supplied by the caller (a browser-originated
    /// commit does not carry the kind; it is read off the marker element itself, ADR-47).
    /// </summary>
    public static bool TryParseAttributeValue(string value, out FieldKind kind)
    {
        switch (value)
        {
            case "text":
                kind = FieldKind.Text;
                return true;
            case "textarea":
                kind = FieldKind.TextArea;
                return true;
            case "number":
                kind = FieldKind.Number;
                return true;
            case "checkbox":
                kind = FieldKind.CheckBox;
                return true;
            case "dropdown":
                kind = FieldKind.DropDown;
                return true;
            case "slider":
                kind = FieldKind.Slider;
                return true;
            case "radio":
                kind = FieldKind.Radio;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
