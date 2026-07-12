namespace Duets.Pad.Rendering;

/// <summary>
/// Renders CLR objects that do not implement <see cref="IRenderNode" /> into display content.
/// </summary>
/// <remarks>
/// Object renderers are consulted by a session-scoped registry. When more than one renderer
/// can render the same value, the registry should prefer the last registered renderer.
///
/// <see cref="CanRender" /> is kept separate from <see cref="Render" /> so registries can
/// cheaply test applicability before invoking a renderer. Implementations may inspect the
/// value's state, not only its CLR type.
///
/// <see cref="Render" /> returns <see cref="DisplayContent" />. The returned body's render node
/// is reduced by the render tree reduction boundary before it is appended to a Timeline entry or
/// Canvas state; any pending interactions are preserved as sidecar state.
///
/// A renderer may still fail after <see cref="CanRender" /> returned true. Such failures
/// should be surfaced as exceptions and handled by the output dispatch layer; renderers
/// should not signal failure by returning null.
///
/// The <paramref name="context" /> parameter carries the current nesting depth, the shared
/// cycle-detection set, and the caller-configured <see cref="Rendering.DumpOptions" />.
/// Renderers must recurse into nested values via <see cref="RenderContext.RenderChild" />
/// rather than calling the pipeline directly, so that depth limiting and cycle detection remain
/// consistent across renderer boundaries.
///
/// <strong>Enforcement contract:</strong>
/// <see cref="Rendering.DumpOptions.MaxDepth" /> and cycle detection are enforced centrally by the
/// pipeline dispatch layer; renderers inherit these automatically via <see cref="RenderContext" />.
/// <see cref="Rendering.DumpOptions.MaxItems" />, however, is <em>not</em> centrally enforced —
/// each renderer is responsible for reading <c>context.Options.MaxItems</c> and capping the number
/// of collection items it materializes. Custom renderers that ignore <see cref="Rendering.DumpOptions.MaxItems" />
/// will silently exceed the configured limit.
/// </remarks>
public interface IObjectRenderer
{
    public bool CanRender(object value);

    public DisplayContent Render(object value, RenderContext context);
}
