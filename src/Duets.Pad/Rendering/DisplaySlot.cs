namespace Duets.Pad.Rendering;

/// <summary>
/// A mutable display handle: a placeholder placed once on a surface whose rendered output updates
/// in place when its <see cref="Content"/> is reassigned. Analogous to LINQPad's
/// <c>DumpContainer</c>, but belongs to the <c>ui.*</c> / <see cref="DisplayContent"/> family
/// because it is consumed wherever display content is accepted (<c>canvas.add</c>,
/// <c>canvas.set</c>, <c>ui.stack</c> children, etc.). Obtained from script via <c>ui.slot(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A slot carries a stable <see cref="Id"/> that is emitted as a locatable marker element when the
/// slot is rendered. Reassigning <see cref="Content"/> searches the authoritative server state for
/// that marker and replaces the marked subtree, so updates survive intervening canvas mutations and
/// apply to every location where the same slot was placed.
/// </para>
/// <para>
/// Assignment to <see cref="Content"/> runs synchronously on the script (eval) call stack and
/// therefore funnels through the owning session under its state lock, exactly like
/// <c>canvas.add</c>/<c>set</c>. Setting the content before the slot is placed simply records the
/// value; the first render reflects it.
/// </para>
/// </remarks>
public sealed class DisplaySlot
{
    private readonly ISlotHost _host;
    private object? _content;

    internal DisplaySlot(ISlotHost host, object? initial)
    {
        this._host = host ?? throw new ArgumentNullException(nameof(host));
        this._content = initial;
    }

    /// <summary>The stable identity used to locate this slot's marker in the projected output.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// The slot's current content. Reading returns the last assigned value; assigning re-renders
    /// the content and updates every placement of this slot in place. (JS: <c>slot.content</c>)
    /// </summary>
    public object? Content
    {
        get => this._content;
        set
        {
            this._content = value;
            this._host.UpdateSlot(this);
        }
    }
}
