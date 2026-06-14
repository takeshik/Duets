using Duets.Pad.Rendering;

namespace Duets.Tests.Pad.Rendering;

public sealed class DumpOptionsTests
{
    private static IReadOnlyList<Element> AssertScalarTableRows(IRenderNode result)
    {
        var table = Assert.IsType<Element>(result);
        Assert.Equal("table", table.Tag);
        Assert.Equal("duetspad-table", table.Attributes["class"]);
        Assert.Equal(2, table.Children.Count);

        var thead = Assert.IsType<Element>(table.Children[0]);
        Assert.Equal("thead", thead.Tag);
        Assert.Single(thead.Children);

        var tbody = Assert.IsType<Element>(table.Children[1]);
        Assert.Equal("tbody", tbody.Tag);
        return [.. tbody.Children.Select(Assert.IsType<Element>)];
    }

    private static ITerminalRenderNode GetScalarCellValue(Element row)
    {
        var td = Assert.IsType<Element>(Assert.Single(row.Children));
        Assert.Equal("td", td.Tag);
        return Assert.Single(td.Children);
    }

    // Default values

    [Fact]
    public void Default_MaxDepth_is_5()
    {
        Assert.Equal(5, DumpOptions.Default.MaxDepth);
    }

    [Fact]
    public void Default_MaxItems_is_1000()
    {
        Assert.Equal(1000, DumpOptions.Default.MaxItems);
    }

    [Fact]
    public void New_instance_has_same_defaults_as_Default()
    {
        var instance = new DumpOptions();

        Assert.Equal(DumpOptions.Default.MaxDepth, instance.MaxDepth);
        Assert.Equal(DumpOptions.Default.MaxItems, instance.MaxItems);
    }

    // With-expression override

    [Fact]
    public void With_MaxDepth_overrides_only_MaxDepth()
    {
        var opts = DumpOptions.Default with { MaxDepth = 3 };

        Assert.Equal(3, opts.MaxDepth);
        Assert.Equal(DumpOptions.Default.MaxItems, opts.MaxItems);
    }

    [Fact]
    public void With_MaxItems_overrides_only_MaxItems()
    {
        var opts = DumpOptions.Default with { MaxItems = 50 };

        Assert.Equal(DumpOptions.Default.MaxDepth, opts.MaxDepth);
        Assert.Equal(50, opts.MaxItems);
    }

    // Validation: negative limits are rejected

    [Fact]
    public void New_DumpOptions_with_negative_MaxDepth_throws_ArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new DumpOptions { MaxDepth = -1 }
        );
        Assert.Equal("MaxDepth", ex.ParamName);
    }

    [Fact]
    public void New_DumpOptions_with_negative_MaxItems_throws_ArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new DumpOptions { MaxItems = -1 }
        );
        Assert.Equal("MaxItems", ex.ParamName);
    }

    [Fact]
    public void DumpOptions_with_expression_negative_MaxDepth_throws_ArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = DumpOptions.Default with { MaxDepth = -5 }
        );
        Assert.Equal("MaxDepth", ex.ParamName);
    }

    [Fact]
    public void DumpOptions_with_expression_negative_MaxItems_throws_ArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = DumpOptions.Default with { MaxItems = -100 }
        );
        Assert.Equal("MaxItems", ex.ParamName);
    }

    [Fact]
    public void DumpOptions_allows_zero_MaxDepth()
    {
        var opts = new DumpOptions { MaxDepth = 0 };
        Assert.Equal(0, opts.MaxDepth);
    }

    [Fact]
    public void DumpOptions_allows_zero_MaxItems()
    {
        var opts = new DumpOptions { MaxItems = 0 };
        Assert.Equal(0, opts.MaxItems);
    }

    // Pipeline depth-limit override

    [Fact]
    public void Pipeline_Render_with_MaxDepth_1_truncates_at_depth_1()
    {
        // Build a list nested 3 levels deep — should be truncated at depth 1 with MaxDepth=1.
        var level2 = new List<object?> { "leaf" };
        var level1 = new List<object?> { level2 };
        var level0 = new List<object?> { level1 };

        var pipeline = new ObjectRenderingPipeline([]);
        var result = pipeline.Render(level0, new DumpOptions { MaxDepth = 1 });

        // level0 is a scalar collection table; its single row should be truncated ([…])
        // because the child collection is at depth 1>=1.
        var rows = AssertScalarTableRows(result);
        var row = Assert.Single(rows);
        Assert.Equal(new Text("[…]"), GetScalarCellValue(row));
    }

    [Fact]
    public void Pipeline_Render_with_MaxDepth_2_reaches_depth_1_child_but_truncates_at_depth_2()
    {
        // With MaxDepth=2, depth 0 and 1 render normally; depth 2 is truncated.
        var leaf = new List<object?> { "leaf" };
        var level1 = new List<object?> { leaf };
        var level0 = new List<object?> { level1 };

        var pipeline = new ObjectRenderingPipeline([]);
        var result = pipeline.Render(level0, new DumpOptions { MaxDepth = 2 });

        // Depth 0: scalar table with one row wrapping level1 (rendered at depth 1)
        var outerRows = AssertScalarTableRows(result);
        var outerRow = Assert.Single(outerRows);

        // Depth 1 child: scalar table with one row wrapping leaf (truncated at depth 2)
        var inner = Assert.IsType<Element>(GetScalarCellValue(outerRow));
        var innerRows = AssertScalarTableRows(inner);
        var innerRow = Assert.Single(innerRows);

        // Depth 2: truncated
        Assert.Equal(new Text("[…]"), GetScalarCellValue(innerRow));
    }
}
