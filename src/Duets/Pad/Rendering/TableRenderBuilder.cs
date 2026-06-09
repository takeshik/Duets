using System.Globalization;

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
    /// <param name="typeHeaderText">
    /// When non-<see langword="null"/>, a type/count header row
    /// <c>&lt;th class="duetspad-typeheader" colspan="{N}"&gt;</c> is prepended to
    /// <c>thead</c> above the column-name row.
    /// </param>
    /// <param name="truncated">
    /// When <see langword="true"/>, a trailing truncation-indicator row is appended to
    /// <c>tbody</c>.
    /// </param>
    public static Element Build(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> rows,
        Func<object?, ITerminalRenderNode> renderCell,
        string? typeHeaderText = null,
        bool truncated = false
    )
    {
        var thead = BuildThead(columns, typeHeaderText);
        var tbody = BuildTbody(columns, rows, renderCell, truncated);
        return new Element("table", TableAttributes, new ElementChildren(thead, tbody));
    }

    private static Element BuildThead(IReadOnlyList<string> columns, string? typeHeaderText)
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

        var columnRow = new Element("tr", ElementAttributes.Empty, [.. thNodes]);

        if (typeHeaderText is null)
        {
            return new Element("thead", ElementAttributes.Empty, new ElementChildren(columnRow));
        }

        var typeHeaderRow = new Element(
            "tr",
            ElementAttributes.Empty,
            new ElementChildren(
                new Element(
                    "th",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "duetspad-typeheader"),
                        new KeyValuePair<string, string?>(
                            "colspan",
                            columns.Count.ToString(CultureInfo.InvariantCulture)
                        )
                    ),
                    new ElementChildren(new Text(typeHeaderText))
                )
            )
        );

        return new Element(
            "thead",
            ElementAttributes.Empty,
            new ElementChildren(typeHeaderRow, columnRow)
        );
    }

    private static Element BuildTbody(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> rows,
        Func<object?, ITerminalRenderNode> renderCell,
        bool truncated
    )
    {
        var capacity = rows.Count + (truncated ? 1 : 0);
        var trNodes = new List<ITerminalRenderNode>(capacity);
        for (var i = 0; i < rows.Count; i++)
        {
            trNodes.Add(BuildBodyRow(columns, rows[i], renderCell));
        }

        if (truncated)
        {
            trNodes.Add(BuildTruncationRow(columns.Count));
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

    /// <summary>
    /// Builds a truncation indicator row: a single <c>&lt;tr&gt;</c> with a
    /// <c>&lt;td class="duetspad-truncated" colspan="{columnCount}"&gt;…&lt;/td&gt;</c>.
    /// </summary>
    private static Element BuildTruncationRow(int columnCount)
    {
        return new Element(
            "tr",
            ElementAttributes.Empty,
            new ElementChildren(
                new Element(
                    "td",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "duetspad-truncated"),
                        new KeyValuePair<string, string?>(
                            "colspan",
                            columnCount.ToString(CultureInfo.InvariantCulture)
                        )
                    ),
                    new ElementChildren(new Text("…"))
                )
            )
        );
    }
}
