namespace Duets.Pad.Rendering;

/// <summary>
/// One selectable option of a <c>ui.dropDown</c> or <c>ui.radioGroup</c> field: the string value
/// committed to the field store and the label shown to the user.
/// </summary>
public sealed record FieldOption(string Value, string Label);
