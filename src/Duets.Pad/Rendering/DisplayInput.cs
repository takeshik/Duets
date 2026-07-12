namespace Duets.Pad.Rendering;

/// <summary>
/// A form-input display handle: a placeholder placed once on a surface whose value lives in the
/// owning session's server-canonical field store (ADR-47), not on the handle itself. Obtained from
/// script via the <c>ui.*</c> input factories (<c>ui.textBox</c>, <c>ui.checkBox</c>, etc.).
/// </summary>
/// <remarks>
/// <para>
/// A field carries a stable <see cref="Id"/> that is emitted as a locatable marker attribute
/// (<c>data-duetspad-field</c>) directly on its rendered element. Unlike <see cref="DisplaySlot"/>,
/// no wrapper element is used: a form input is a single element, so the marker sits on that element
/// itself rather than on a surrounding div.
/// </para>
/// <para>
/// Reading <see cref="Value"/> reads the session's field store; assigning it writes the store and
/// re-projects every placement of the field in place, via the ADR-45 patch path. Assignment runs
/// synchronously on the script (eval) call stack and therefore funnels through the owning session
/// under its state lock, exactly like <see cref="DisplaySlot.Content"/>.
/// </para>
/// </remarks>
public sealed class DisplayInput
{
    private readonly IFieldHost _host;
    private readonly Func<string, DisplayContent> _render;
    private readonly string _initialValue;

    internal DisplayInput(
        IFieldHost host,
        Guid id,
        FieldKind kind,
        string initialValue,
        Func<string, DisplayContent> render
    )
    {
        this._host = host ?? throw new ArgumentNullException(nameof(host));
        this._render = render ?? throw new ArgumentNullException(nameof(render));
        this.Id = id;
        this.Kind = kind;
        this._initialValue = initialValue ?? "";

        // Seeds the store before the handle is placed anywhere; the loops in SetFieldValue find no
        // markers yet and therefore project nothing (mirrors DisplaySlot's constructor recording the
        // initial value without calling the host). If an unrelated canvas mutation prunes this seed
        // before the field is ever placed, Value/Render fall back to _initialValue below instead of
        // silently reading back "" (ADR-47).
        this._host.SetFieldValue(id, kind, this._initialValue);
    }

    /// <summary>The stable identity used to locate this field's marker in the projected output.</summary>
    public Guid Id { get; }

    internal FieldKind Kind { get; }

    /// <summary>
    /// The field's current value, read from and written through the session's field store. Every
    /// value is a plain string with no coercion or validation; a checkbox reports
    /// <c>"True"</c>/<c>"False"</c> (ADR-47). Assigning re-projects every placement of this field in
    /// place. When the store holds no entry for this field (e.g. its seed was pruned before it was
    /// ever placed on a surface), this falls back to the handle's own constructor-supplied initial
    /// value rather than an empty string. (JS: <c>input.value</c>)
    /// </summary>
    public string Value
    {
        get => this._host.TryGetFieldValue(this.Id, out var stored) ? stored : this._initialValue;
        set => this._host.SetFieldValue(this.Id, this.Kind, value ?? "");
    }

    /// <summary>
    /// Renders this field's current element using the session's stored value, falling back to the
    /// handle's initial value per <see cref="Value"/>. Runs outside <c>_stateLock</c> (called from
    /// <c>TryRenderContent</c>), so this must only read — it must never write back to the field
    /// store (no re-seeding on render).
    /// </summary>
    internal DisplayContent Render() => this._render(this.Value);
}
