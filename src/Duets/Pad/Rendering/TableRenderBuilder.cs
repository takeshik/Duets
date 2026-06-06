namespace Duets.Pad.Rendering;

/// <summary>
/// Builds a <c>table.duetspad-table</c> element from columns, rows, and a cell-render delegate.
/// </summary>
internal static class TableRenderBuilder
{
    private static readonly ElementAttributes TableAttributes = new(
        new KeyValuePair<string, string?>("class", "duetspad-table")
    );

    /// <summary>
    /// Builds a <c>table.duetspad-table</c> element.
    /// </summary>
    /// <param name="columns">Ordered column names for thead and cell lookup.</param>
    /// <param name="rows">Rows as ordered lists of key/value pairs.</param>
    /// <param name="renderCell">Delegate used to render an individual cell value.</param>
    public static Element Build(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> rows,
        Func<object?, ITerminalRenderNode> renderCell
    )
    {
        var thead = BuildThead(columns);
        var tbody = BuildTbody(columns, rows, renderCell);
        return new Element("table", TableAttributes, new ElementChildren(thead, tbody));
    }

    private static Element BuildThead(IReadOnlyList<string> columns)
    {
        var thNodes = new ITerminalRenderNode[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            thNodes[i] = new Element(
                "th",
                ElementAttributes.Empty,
                new ElementChildren(new Text(columns[i]))
            );
        }

        var tr = new Element("tr", ElementAttributes.Empty, [.. thNodes]);
        return new Element("thead", ElementAttributes.Empty, new ElementChildren(tr));
    }

    private static Element BuildTbody(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> rows,
        Func<object?, ITerminalRenderNode> renderCell
    )
    {
        var trNodes = new ITerminalRenderNode[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            trNodes[i] = BuildBodyRow(columns, rows[i], renderCell);
        }

        return new Element("tbody", ElementAttributes.Empty, [.. trNodes]);
    }

    private static Element BuildBodyRow(
        IReadOnlyList<string> columns,
        IReadOnlyList<KeyValuePair<string, object?>> row,
        Func<object?, ITerminalRenderNode> renderCell
    )
    {
        // Build a lookup for this row by key.
        var lookup = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in row)
        {
            lookup.TryAdd(kv.Key, kv.Value);
        }

        var tdNodes = new ITerminalRenderNode[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            ITerminalRenderNode cellContent;
            if (lookup.TryGetValue(columns[i], out var cellValue))
            {
                try
                {
                    cellContent = renderCell(cellValue);
                }
                catch (Exception ex)
                {
                    cellContent = OutputError.Create(ex.Message);
                }
            }
            else
            {
                cellContent = new Text("");
            }

            tdNodes[i] = new Element(
                "td",
                ElementAttributes.Empty,
                new ElementChildren(cellContent)
            );
        }

        return new Element("tr", ElementAttributes.Empty, [.. tdNodes]);
    }
}
