using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Duets.Pad.Rendering;

internal sealed class DefaultObjectRenderer : IObjectRenderer
{
    private const int MaxDepth = 32;

    private static readonly RenderTreeReducer Reducer = new();

    public bool CanRender(object value) => true;

    public IRenderNode Render(object value) =>
        RenderValue(value, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);

    private static ITerminalRenderNode RenderValue(
        object? value,
        HashSet<object> visited,
        int depth
    )
    {
        if (value is null or DBNull)
        {
            return new Text("null");
        }

        if (depth >= MaxDepth)
        {
            return new Text("[…]");
        }

        // Render nodes are already in the rendering domain — pass them through without reflection.
        if (value is ITerminalRenderNode terminalNode)
        {
            return terminalNode;
        }

        if (value is IRenderNode renderNode)
        {
            return Reducer.Reduce(renderNode);
        }

        switch (value)
        {
            case string s:
                return new Text(s);

            case bool b:
                return new Text(b ? "true" : "false");

            case char c:
                return new Text(c.ToString());

            case byte n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case sbyte n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case short n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case ushort n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case int n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case uint n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case long n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case ulong n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case float n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case double n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));

            case decimal n:
                return new Text(n.ToString(CultureInfo.InvariantCulture));
        }

        // Reference types from here — cycle detection applies.
        if (!visited.Add(value))
        {
            return new Text("[Circular]");
        }

        try
        {
            if (value is IDictionary dict)
            {
                return RenderDictionary(dict, visited, depth);
            }

            if (value is IEnumerable enumerable)
            {
                return RenderEnumerable(enumerable, visited, depth);
            }

            return RenderClrObject(value, visited, depth);
        }
        finally
        {
            visited.Remove(value);
        }
    }

    private static Element RenderDictionary(IDictionary dict, HashSet<object> visited, int depth)
    {
        var children = new List<ITerminalRenderNode>();

        foreach (DictionaryEntry entry in dict)
        {
            var keyText = new Text(entry.Key?.ToString() ?? "");
            var valueNode = RenderValue(entry.Value, visited, depth + 1);

            var entryElement = new Element(
                "div",
                ElementAttributes.Empty,
                new ElementChildren(keyText, valueNode)
            );
            children.Add(entryElement);
        }

        return new Element(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-object")),
            [.. children]
        );
    }

    private static Element RenderEnumerable(
        IEnumerable enumerable,
        HashSet<object> visited,
        int depth
    )
    {
        // Materialize items into a list.
        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
        }

        // Determine if all items are record-like (dictionaries or non-primitive, non-string,
        // non-enumerable CLR objects). Empty lists are not tabular.
        if (items.Count > 0 && items.All(RecordProjector.IsRecordLike))
        {
            return RenderTabular(items, visited, depth);
        }

        return RenderArray(items, visited, depth);
    }

    private static Element RenderTabular(List<object?> items, HashSet<object> visited, int depth)
    {
        // Build projected rows and compute union of columns in first-seen order.
        var projectedRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>(items.Count);
        var columnOrder = new List<string>();
        var columnSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var projected = RecordProjector.Project(item!);
            projectedRows.Add(projected);

            foreach (var kv in projected)
            {
                if (columnSet.Add(kv.Key))
                {
                    columnOrder.Add(kv.Key);
                }
            }
        }

        // If every item projects to zero members (e.g. types with no public properties or fields),
        // the union of columns is empty and building a 0-column table is not useful.
        // Fall back to array rendering so each item renders via its ToString() fallback.
        if (columnOrder.Count == 0)
        {
            return RenderArray(items, visited, depth);
        }

        return TableRenderBuilder.Build(
            columnOrder,
            projectedRows,
            v => RenderValue(v, visited, depth + 1)
        );
    }

    private static Element RenderArray(List<object?> items, HashSet<object> visited, int depth)
    {
        var children = new List<ITerminalRenderNode>(items.Count);
        foreach (var item in items)
        {
            children.Add(RenderValue(item, visited, depth + 1));
        }

        return new Element(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-array")),
            [.. children]
        );
    }

    private static ITerminalRenderNode RenderClrObject(
        object value,
        HashSet<object> visited,
        int depth
    )
    {
        var members = RecordProjector.Project(value);

        // Fall back to ToString() if the object exposes no public members.
        if (members.Count == 0)
        {
            return new Text(value.ToString() ?? "");
        }

        var children = new List<ITerminalRenderNode>(members.Count);
        foreach (var member in members)
        {
            var keyText = new Text(member.Key);
            var valueNode = member.Value is ITerminalRenderNode markerNode
                ? markerNode
                : RenderValue(member.Value, visited, depth + 1);

            // RecordProjector may already return a Text("[error]") marker as the value if the
            // getter threw; render it as-is rather than recursing again.

            var entryElement = new Element(
                "div",
                ElementAttributes.Empty,
                new ElementChildren(keyText, valueNode)
            );
            children.Add(entryElement);
        }

        return new Element(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-object")),
            [.. children]
        );
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
