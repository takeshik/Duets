namespace Duets.Pad.Rendering;

internal sealed class ObjectRenderingPipeline(IReadOnlyList<IObjectRenderer> renderers)
{
    private readonly IReadOnlyList<IObjectRenderer> renderers =
        renderers ?? throw new ArgumentNullException(nameof(renderers));
    private readonly DefaultObjectRenderer defaultRenderer = new();
    private readonly RenderTreeReducer reducer = new();

    /// <summary>
    /// Renders <paramref name="value" /> to a terminal render node using the resolution order: 1) null/DBNull, 2) IRenderNode, 3) registered renderers last-wins, 4) default renderer.
    /// </summary>
    public ITerminalRenderNode Render(object? value)
    {
        // Resolution step 1: null and DBNull
        if (value is null or DBNull)
        {
            return new Text("null");
        }

        // Resolution step 2: value already is a render node
        if (value is IRenderNode node)
        {
            return this.reducer.Reduce(node);
        }

        // Resolution step 3: iterate registered renderers in reverse (last-registered wins)
        for (var i = this.renderers.Count - 1; i >= 0; i--)
        {
            var renderer = this.renderers[i];

            if (renderer.CanRender(value))
            {
                return this.reducer.Reduce(renderer.Render(value));
            }
        }

        // Resolution step 4: fall back to default renderer
        return this.reducer.Reduce(this.defaultRenderer.Render(value));
    }
}
