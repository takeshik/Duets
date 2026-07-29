namespace Duets.Pad.Rendering;

/// <summary>Presentation and browser-selection options for <c>ui.filePicker</c>.</summary>
public sealed record FilePickerOptions
{
    /// <summary>Browser file-type hint passed to the native input's <c>accept</c> attribute.</summary>
    public string? Accept { get; init; }

    /// <summary>Whether the browser may select more than one file at a time.</summary>
    public bool Multiple { get; init; }

    /// <summary>Whether the native picker is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Optional hover text.</summary>
    public string? Title { get; init; }
}
