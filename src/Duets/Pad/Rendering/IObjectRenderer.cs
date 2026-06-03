namespace Duets.Pad.Rendering;

/// <summary>
/// Renders CLR objects that do not implement <see cref="IRenderNode" /> into render nodes.
/// </summary>
/// <remarks>
/// Object renderers are consulted by a session-scoped registry. When more than one renderer
/// can render the same value, the registry should prefer the last registered renderer.
///
/// <see cref="CanRender" /> is kept separate from <see cref="Render" /> so registries can
/// cheaply test applicability before invoking a renderer. Implementations may inspect the
/// value's state, not only its CLR type.
///
/// <see cref="Render" /> returns an <see cref="IRenderNode" />, not necessarily an
/// <see cref="ITerminalRenderNode" />. The returned node is reduced by the render tree
/// reduction boundary before it is appended to a Timeline entry or Canvas state.
///
/// A renderer may still fail after <see cref="CanRender" /> returned true. Such failures
/// should be surfaced as exceptions and handled by the output dispatch layer; renderers
/// should not signal failure by returning null.
/// </remarks>
public interface IObjectRenderer
{
    public bool CanRender(object value);

    public IRenderNode Render(object value);
}
