namespace Duets.Pad.Rendering;

internal class DisplayRenderer(IReadOnlyList<IObjectRenderer> renderers)
{
    private readonly IReadOnlyList<IObjectRenderer> renderers =
        renderers ?? throw new ArgumentNullException(nameof(renderers));
    private readonly DefaultObjectRenderer defaultRenderer = new();
    private readonly RenderTreeReducer reducer = new();

    /// <summary>
    /// Renders <paramref name="value" /> to display content using the session-registered
    /// renderers and the default renderer. The central dispatch order (per ADR-35) is:
    /// 1) null/DBNull, 2) depth limit, 3) DisplayContent passthrough,
    /// 4) IRenderNode passthrough, 5) cycle detection, 6) registered renderers last-wins,
    /// 7) default renderer.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <param name="options">
    /// Options for this render pass. When <see langword="null" />, <see cref="DumpOptions.Default" />
    /// is used.
    /// </param>
    public DisplayContent Render(object? value, DumpOptions? options = null)
    {
        var effectiveOptions = options ?? DumpOptions.Default;
        var ctx = RenderContext.CreateRoot(effectiveOptions, this.Dispatch);
        return this.Dispatch(value, ctx);
    }

    /// <summary>
    /// Central dispatch applied both at the root (via <see cref="Render"/>) and inside
    /// <see cref="RenderContext.RenderChild"/>.
    /// </summary>
    private DisplayContent Dispatch(object? value, RenderContext ctx)
    {
        // Step 1: null / DBNull
        if (value is null or DBNull)
        {
            return DisplayContent.Text("null");
        }

        // Step 2: depth limit
        if (ctx.Depth >= ctx.Options.MaxDepth)
        {
            return DisplayContent.Text("[…]");
        }

        // Step 3: value already is display content — pass through without reflection
        if (value is DisplayContent content)
        {
            return this.Reduce(content);
        }

        // Step 4: value already is a render node — pass through without reflection
        if (value is IRenderNode node)
        {
            return DisplayContent.FromNode(node);
        }

        // Step 4.5: mutable slot — render its current content and wrap it in a locatable marker.
        // Intercepted before cycle detection and the renderer loop so the slot handle itself is
        // never reflected over; self-referential content is bounded by the depth limit (step 2).
        if (value is DisplaySlot slot)
        {
            var child = ctx.RenderChild(slot.Content);
            return new DisplayContent(
                SlotMarker.Wrap(slot.Id, child.Body),
                child.Interactions.PrependPath(0)
            );
        }

        // Step 5: cycle detection — only for reference types (not strings, not value types)
        var isRef = value is not string && !value.GetType().IsValueType;
        if (isRef)
        {
            if (!ctx.TryVisit(value))
            {
                return DisplayContent.Text("[Circular]");
            }
        }

        try
        {
            // Step 6: session-registered renderers, last-wins
            for (var i = this.renderers.Count - 1; i >= 0; i--)
            {
                var renderer = this.renderers[i];
                if (renderer.CanRender(value))
                {
                    return this.Reduce(renderer.Render(value, ctx));
                }
            }

            // Step 7: default renderer
            return this.Reduce(this.defaultRenderer.Render(value, ctx));
        }
        finally
        {
            if (isRef)
            {
                ctx.Unvisit(value);
            }
        }
    }

    private DisplayContent Reduce(DisplayContent content) =>
        new(this.reducer.Reduce(content.Body), content.Interactions);
}

internal sealed class ObjectRenderingPipeline(IReadOnlyList<IObjectRenderer> renderers)
{
    private readonly DisplayRenderer renderer = new(renderers);

    public ITerminalRenderNode Render(object? value, DumpOptions? options = null) =>
        this.renderer.Render(value, options).Body;
}
