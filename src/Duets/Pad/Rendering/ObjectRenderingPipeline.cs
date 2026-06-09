namespace Duets.Pad.Rendering;

internal sealed class ObjectRenderingPipeline(IReadOnlyList<IObjectRenderer> renderers)
{
    private readonly IReadOnlyList<IObjectRenderer> renderers =
        renderers ?? throw new ArgumentNullException(nameof(renderers));
    private readonly DefaultObjectRenderer defaultRenderer = new();
    private readonly RenderTreeReducer reducer = new();

    /// <summary>
    /// Renders <paramref name="value" /> to a terminal render node using the session-registered
    /// renderers and the default renderer. The central dispatch order (per ADR-35) is:
    /// 1) null/DBNull, 2) depth limit, 3) IRenderNode passthrough, 4) cycle detection,
    /// 5) registered renderers last-wins, 6) default renderer.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <param name="options">
    /// Options for this render pass. When <see langword="null" />, <see cref="DumpOptions.Default" />
    /// is used.
    /// </param>
    public ITerminalRenderNode Render(object? value, DumpOptions? options = null)
    {
        var effectiveOptions = options ?? DumpOptions.Default;
        var ctx = RenderContext.CreateRoot(effectiveOptions, this.Dispatch);
        return this.Dispatch(value, ctx);
    }

    /// <summary>
    /// Central dispatch applied both at the root (via <see cref="Render"/>) and inside
    /// <see cref="RenderContext.RenderChild"/>.
    /// </summary>
    private ITerminalRenderNode Dispatch(object? value, RenderContext ctx)
    {
        // Step 1: null / DBNull
        if (value is null or DBNull)
        {
            return new Text("null");
        }

        // Step 2: depth limit
        if (ctx.Depth >= ctx.Options.MaxDepth)
        {
            return new Text("[…]");
        }

        // Step 3: value already is a render node — pass through without reflection
        if (value is IRenderNode node)
        {
            return this.reducer.Reduce(node);
        }

        // Step 4: cycle detection — only for reference types (not strings, not value types)
        var isRef = value is not string && !value.GetType().IsValueType;
        if (isRef)
        {
            if (!ctx.TryVisit(value))
            {
                return new Text("[Circular]");
            }
        }

        try
        {
            // Step 5: session-registered renderers, last-wins
            for (var i = this.renderers.Count - 1; i >= 0; i--)
            {
                var renderer = this.renderers[i];
                if (renderer.CanRender(value))
                {
                    return this.reducer.Reduce(renderer.Render(value, ctx));
                }
            }

            // Step 6: default renderer
            return this.reducer.Reduce(this.defaultRenderer.Render(value, ctx));
        }
        finally
        {
            if (isRef)
            {
                ctx.Unvisit(value);
            }
        }
    }
}
