namespace Duets.Pad.Rendering;

/// <summary>
/// Session-side callback surface for <see cref="DisplaySlot"/>. Implemented by the owning
/// <c>DuetsPadSession</c> so that reassigning a slot's content can re-project the affected
/// Canvas, Timeline, and Modal output in place.
/// </summary>
internal interface ISlotHost
{
    /// <summary>
    /// Re-renders <paramref name="slot"/>'s current content and updates every location where the
    /// slot is currently placed (Canvas children, Timeline entries, and Modal bodies). A no-op
    /// when the slot is not placed anywhere. Must never throw.
    /// </summary>
    public void UpdateSlot(DisplaySlot slot);
}
