namespace Duets.Pad.Rendering;

/// <summary>
/// Per-session server-canonical store of form-input field values (ADR-47): a simple
/// <see cref="Guid"/>-keyed string map. Values are retained for exactly as long as the field's
/// marker is reachable from Canvas, Timeline, or Dialog content; <see cref="Retain"/> prunes entries
/// whose marker has become unreachable (a full surface rebuild, a clear, or a replace that drops
/// the field's placement).
/// </summary>
/// <remarks>
/// <b>Thread safety:</b> this type has no internal locking. The caller (<see cref="DuetsPadSession"/>)
/// is responsible for holding <c>_stateLock</c> for every mutating or lookup call that must be
/// atomic with respect to state changes, exactly as it does for <c>InteractionStore</c>.
/// </remarks>
internal sealed class FieldStore
{
    private readonly Dictionary<Guid, string> _values = [];

    /// <summary>
    /// Returns whether the store holds no values. Lets callers skip the reachability scan that
    /// feeds <see cref="Retain"/> entirely: pruning an empty store is a no-op, but the scan that
    /// collects retained ids walks every canvas tree and Timeline entry and runs on every Timeline
    /// append, so sessions that never create a form input should not pay for it.
    /// </summary>
    public bool IsEmpty => this._values.Count == 0;

    /// <summary>Returns the stored value for <paramref name="fieldId"/>, or <c>""</c> if none.</summary>
    public string GetValue(Guid fieldId) =>
        this._values.TryGetValue(fieldId, out var value) ? value : "";

    /// <summary>
    /// Returns whether a value has been stored for <paramref name="fieldId"/>, distinguishing
    /// "never stored" from "stored as the empty string" — unlike <see cref="GetValue"/>, which
    /// conflates the two by returning <c>""</c> for both. Used by <see cref="DisplayInput"/> to fall
    /// back to its own constructor-supplied initial value when the store holds no entry for it (its
    /// marker may have been pruned by an unrelated canvas mutation before the field was ever placed,
    /// ADR-47).
    /// </summary>
    public bool TryGetValue(Guid fieldId, out string value)
    {
        var found = this._values.TryGetValue(fieldId, out var raw);
        value = raw ?? "";
        return found;
    }

    /// <summary>Stores <paramref name="value"/> for <paramref name="fieldId"/>.</summary>
    public void SetValue(Guid fieldId, string value) => this._values[fieldId] = value;

    /// <summary>
    /// Removes every stored value whose field id is not present in <paramref name="retainedIds"/>.
    /// </summary>
    public void Retain(HashSet<Guid> retainedIds)
    {
        if (this._values.Count == 0)
        {
            return;
        }

        foreach (var fieldId in this._values.Keys.ToList())
        {
            if (!retainedIds.Contains(fieldId))
            {
                this._values.Remove(fieldId);
            }
        }
    }

    /// <summary>Removes all stored values. Called on session dispose.</summary>
    public void Clear() => this._values.Clear();
}
