namespace Duets.Pad.Rendering;

/// <summary>
/// Session-side callback surface for <see cref="DisplayInput"/>. Implemented by the owning
/// <c>DuetsPadSession</c> so that reading or reassigning a field's value can consult and update the
/// session's server-canonical field store (ADR-47) and re-project every placement of the field's
/// marker in Canvas, Timeline, and Dialog output.
/// </summary>
internal interface IFieldHost
{
    /// <summary>
    /// Returns the current stored value for <paramref name="fieldId"/>, or <c>""</c> when no value
    /// has been stored yet.
    /// </summary>
    public string GetFieldValue(Guid fieldId);

    /// <summary>
    /// Returns whether a value has been stored for <paramref name="fieldId"/>, distinguishing
    /// "never stored" from "stored as the empty string" — unlike <see cref="GetFieldValue"/>, which
    /// conflates the two. Used by <see cref="DisplayInput"/> to fall back to its own
    /// constructor-supplied initial value when the store holds no entry for it (ADR-47).
    /// </summary>
    public bool TryGetFieldValue(Guid fieldId, out string value);

    /// <summary>
    /// Stores <paramref name="value"/> for <paramref name="fieldId"/> and re-projects every
    /// placement of the field's marker in Canvas, Timeline, and Dialog output. A no-op
    /// projection-wise when the field is not currently placed anywhere. Must never throw.
    /// </summary>
    public void SetFieldValue(Guid fieldId, FieldKind kind, string value);
}
