using Duets.Pad.Rendering;

namespace Duets.Pad.Tests.Rendering;

public sealed class ObjectRenderingPipelineTests
{
    [Fact]
    public void Null_value_renders_to_null_text()
    {
        var pipeline = new ObjectRenderingPipeline([]);

        var result = pipeline.Render(null);

        Assert.Equal(new Text("null"), result);
    }

    [Fact]
    public void DBNull_renders_to_null_text()
    {
        var pipeline = new ObjectRenderingPipeline([]);

        var result = pipeline.Render(DBNull.Value);

        Assert.Equal(new Text("null"), result);
    }

    [Fact]
    public void IRenderNode_passthrough_is_reduced_and_returned()
    {
        var pipeline = new ObjectRenderingPipeline([]);
        var wrapper = new WrapperNode(new Text("reduced"));

        var result = pipeline.Render(wrapper);

        Assert.Equal(new Text("reduced"), result);
    }

    [Fact]
    public void Terminal_IRenderNode_passthrough_is_returned_directly()
    {
        var pipeline = new ObjectRenderingPipeline([]);
        var text = new Text("hello");

        var result = pipeline.Render(text);

        Assert.Same(text, result);
    }

    [Fact]
    public void Last_registered_renderer_wins_when_multiple_can_render()
    {
        var first = new ConstantRenderer(new Text("from-first"));
        var second = new ConstantRenderer(new Text("from-second"));
        var pipeline = new ObjectRenderingPipeline([first, second]);

        var result = pipeline.Render(new object());

        Assert.Equal(new Text("from-second"), result);
    }

    [Fact]
    public void First_renderer_is_used_when_second_cannot_render()
    {
        var first = new ConstantRenderer(new Text("from-first"));
        var second = new NeverRenderer();
        var pipeline = new ObjectRenderingPipeline([first, second]);

        var result = pipeline.Render(new object());

        Assert.Equal(new Text("from-first"), result);
    }

    [Fact]
    public void Unregistered_value_falls_back_to_default_renderer()
    {
        var pipeline = new ObjectRenderingPipeline([]);

        var result = pipeline.Render("hello");

        Assert.Equal(new Text("hello"), result);
    }

    [Fact]
    public void Renderer_returning_non_terminal_reducible_node_is_reduced()
    {
        var wrapper = new WrapperNode(new Text("unwrapped"));
        var renderer = new ConstantRenderer(wrapper);
        var pipeline = new ObjectRenderingPipeline([renderer]);

        var result = pipeline.Render(new object());

        Assert.Equal(new Text("unwrapped"), result);
    }

    private sealed record WrapperNode(IRenderNode Inner) : IRenderNode
    {
        public bool CanReduce => true;

        public IRenderNode Reduce() => this.Inner;
    }

    private sealed class ConstantRenderer(IRenderNode node) : IObjectRenderer
    {
        public bool CanRender(object value) => true;

        public DisplayContent Render(object value, RenderContext context) =>
            DisplayContent.FromNode(node);
    }

    private sealed class NeverRenderer : IObjectRenderer
    {
        public bool CanRender(object value) => false;

        public DisplayContent Render(object value, RenderContext context) =>
            throw new InvalidOperationException("NeverRenderer.Render should not be called.");
    }
}
