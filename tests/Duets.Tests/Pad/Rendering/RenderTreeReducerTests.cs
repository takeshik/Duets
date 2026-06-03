using Duets.Pad.Rendering;

namespace Duets.Tests.Pad.Rendering;

public sealed class RenderTreeReducerTests
{
    [Fact]
    public void Reduce_returns_terminal_node_unchanged()
    {
        var reducer = new RenderTreeReducer();
        var node = new Text("hello");

        var reduced = reducer.Reduce(node);

        Assert.Same(node, reduced);
    }

    [Fact]
    public void Reduce_reduces_high_level_node_to_terminal_node()
    {
        var reducer = new RenderTreeReducer();
        var node = new WrapperNode(new Text("hello"));

        var reduced = reducer.Reduce(node);

        Assert.Equal(new Text("hello"), reduced);
    }

    [Fact]
    public void Reduce_reduces_nested_high_level_nodes()
    {
        var reducer = new RenderTreeReducer();
        var node = new WrapperNode(new WrapperNode(new RawHtml("<b>hello</b>")));

        var reduced = reducer.Reduce(node);

        Assert.Equal(new RawHtml("<b>hello</b>"), reduced);
    }

    [Fact]
    public void Reduce_rejects_non_terminal_node_that_cannot_reduce()
    {
        var reducer = new RenderTreeReducer();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            reducer.Reduce(new NonTerminalIdentityNode())
        );

        Assert.Contains("is not terminal and cannot reduce", exception.Message);
    }

    [Fact]
    public void Reduce_rejects_null_result()
    {
        var reducer = new RenderTreeReducer();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            reducer.Reduce(new NullReducingNode())
        );

        Assert.Contains("returned null", exception.Message);
    }

    [Fact]
    public void Reduce_rejects_self_reduction()
    {
        var reducer = new RenderTreeReducer();
        var node = new SelfReducingNode();

        var exception = Assert.Throws<InvalidOperationException>(() => reducer.Reduce(node));

        Assert.Contains("returned itself", exception.Message);
    }

    [Fact]
    public void Reduce_rejects_two_node_cycles()
    {
        var reducer = new RenderTreeReducer();
        var first = new ForwardingNode();
        var second = new ForwardingNode();
        first.Next = second;
        second.Next = first;

        var exception = Assert.Throws<InvalidOperationException>(() => reducer.Reduce(first));

        Assert.Contains("cycle detected", exception.Message);
    }

    [Fact]
    public void Reduce_uses_reference_identity_for_cycle_detection()
    {
        var reducer = new RenderTreeReducer();
        var first = new ValueEqualForwardingNode("same");
        var second = new ValueEqualForwardingNode("same") { Next = new Text("done") };
        first.Next = second;

        var reduced = reducer.Reduce(first);

        Assert.Equal(new Text("done"), reduced);
    }

    private sealed record WrapperNode(IRenderNode Inner) : IRenderNode
    {
        public bool CanReduce => true;

        public IRenderNode Reduce() => this.Inner;
    }

    private sealed class NonTerminalIdentityNode : IRenderNode
    {
        public bool CanReduce => false;

        public IRenderNode Reduce() => this;
    }

    private sealed class NullReducingNode : IRenderNode
    {
        public bool CanReduce => true;

        public IRenderNode Reduce() => null!;
    }

    private sealed class SelfReducingNode : IRenderNode
    {
        public bool CanReduce => true;

        public IRenderNode Reduce() => this;
    }

    private sealed class ForwardingNode : IRenderNode
    {
        public IRenderNode? Next { get; set; }

        public bool CanReduce => true;

        public IRenderNode Reduce() => this.Next ?? throw new InvalidOperationException();
    }

    private sealed record ValueEqualForwardingNode(string Value) : IRenderNode
    {
        public IRenderNode? Next { get; set; }

        public bool CanReduce => true;

        public IRenderNode Reduce() => this.Next ?? throw new InvalidOperationException(this.Value);
    }
}
