using System.Collections;
using System.Globalization;
using System.Reflection;
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
            case Enum e:
                return new Text(e.ToString());

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
            // 1. Dynamic JS-object shape (e.g. ExpandoObject, the shape Jint marshals JS object
            //    literals to) → named-member object (Form A), projecting its string keys as member
            //    names. An empty dynamic object still renders as an empty object table rather than
            //    leaking the marshaled CLR type name via ToString(). This is checked before the map
            //    path because ExpandoObject also implements IDictionary<,>, yet ADR-40 routes it to
            //    Form A to converge with ordinary CLR objects.
            if (RecordProjector.TryProjectDynamicObjectLike(value, out var dynamicMembers))
            {
                return RenderNamedMemberObject(
                    value,
                    dynamicMembers,
                    showTypeHeader: false,
                    allowEmptyTable: true,
                    visited,
                    depth
                );
            }

            // 2. Map (Form B): non-generic IDictionary, or a generic dictionary
            //    (IDictionary<,> / IReadOnlyDictionary<,>) that is not the dynamic shape handled
            //    above. A bare IEnumerable<KeyValuePair<,>> is not a map.
            if (RecordProjector.TryExtractMapEntries(value, out var mapEntries))
            {
                return RenderMap(value.GetType(), mapEntries, visited, depth);
            }

            // 3. IEnumerable → existing collection path.
            if (value is IEnumerable enumerable)
            {
                return RenderEnumerable(enumerable, visited, depth);
            }

            // 4. Ordinary CLR object → named-member object (Form A).
            var members = RecordProjector.Project(value);
            var showTypeHeader = !IsAnonymousOrCompilerGenerated(value.GetType());
            return RenderNamedMemberObject(
                value,
                members,
                showTypeHeader,
                allowEmptyTable: false,
                visited,
                depth
            );
        }
        finally
        {
            visited.Remove(value);
        }
    }

    private static bool IsAnonymousOrCompilerGenerated(Type type) =>
        Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute))
        || type.Name.Contains("AnonymousType", StringComparison.Ordinal);

    private static Element RenderMap(
        Type mapType,
        IReadOnlyList<KeyValuePair<string, object?>> entries,
        HashSet<object> visited,
        int depth
    )
    {
        var typeHeader = new Element(
            "tr",
            ElementAttributes.Empty,
            new ElementChildren(
                new Element(
                    "th",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "duetspad-typeheader"),
                        new KeyValuePair<string, string?>("colspan", "2")
                    ),
                    new ElementChildren(new Text($"{mapType.Name} ({entries.Count} items)"))
                )
            )
        );

        var columnHeader = new Element(
            "tr",
            ElementAttributes.Empty,
            new ElementChildren(
                new Element("th", ElementAttributes.Empty, new ElementChildren(new Text("Key"))),
                new Element("th", ElementAttributes.Empty, new ElementChildren(new Text("Value")))
            )
        );

        var thead = new Element(
            "thead",
            ElementAttributes.Empty,
            new ElementChildren(typeHeader, columnHeader)
        );

        var rows = new List<ITerminalRenderNode>(entries.Count);
        foreach (var entry in entries)
        {
            var valueNode = RenderValue(entry.Value, visited, depth + 1);

            rows.Add(
                new Element(
                    "tr",
                    ElementAttributes.Empty,
                    new ElementChildren(
                        new Element(
                            "td",
                            ElementAttributes.Empty,
                            new ElementChildren(new Text(entry.Key))
                        ),
                        new Element("td", ElementAttributes.Empty, new ElementChildren(valueNode))
                    )
                )
            );
        }

        var tbody = new Element("tbody", ElementAttributes.Empty, [.. rows]);

        return new Element(
            "table",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-map")),
            new ElementChildren(thead, tbody)
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

    /// <summary>
    /// Renders a named-member object (Form A) — a vertical property table with one row per
    /// member (member name → rendered value). Used for ordinary CLR objects, JS object literals
    /// / dynamic-object values, and anonymous types.
    /// </summary>
    /// <param name="allowEmptyTable">
    /// When <see langword="true" />, a value with zero projectable members still renders as an
    /// empty object table (used for dynamic/JS objects, where <c>ToString()</c> would leak the
    /// marshaled CLR type name such as <c>System.Dynamic.ExpandoObject</c>). When
    /// <see langword="false" /> (ordinary CLR objects), a zero-member value falls back to
    /// <c>ToString()</c>.
    /// </param>
    private static ITerminalRenderNode RenderNamedMemberObject(
        object value,
        IReadOnlyList<KeyValuePair<string, object?>> members,
        bool showTypeHeader,
        bool allowEmptyTable,
        HashSet<object> visited,
        int depth
    )
    {
        // Fall back to ToString() if the object exposes no projectable members, unless an empty
        // table is explicitly allowed (dynamic/JS objects).
        if (members.Count == 0 && !allowEmptyTable)
        {
            return new Text(value.ToString() ?? "");
        }

        var rows = new List<ITerminalRenderNode>(members.Count);
        foreach (var member in members)
        {
            var valueNode = member.Value is ITerminalRenderNode markerNode
                ? markerNode
                : RenderValue(member.Value, visited, depth + 1);

            // RecordProjector may already return a Text("[error]") marker as the value if the
            // getter threw; render it as-is rather than recursing again.

            rows.Add(BuildMemberRow(member.Key, valueNode));
        }

        var tbody = new Element("tbody", ElementAttributes.Empty, [.. rows]);

        ElementChildren tableChildren;
        if (showTypeHeader)
        {
            var thead = new Element(
                "thead",
                ElementAttributes.Empty,
                new ElementChildren(
                    new Element(
                        "tr",
                        ElementAttributes.Empty,
                        new ElementChildren(
                            new Element(
                                "th",
                                new ElementAttributes(
                                    new KeyValuePair<string, string?>(
                                        "class",
                                        "duetspad-typeheader"
                                    ),
                                    new KeyValuePair<string, string?>("colspan", "2")
                                ),
                                new ElementChildren(new Text(value.GetType().Name))
                            )
                        )
                    )
                )
            );

            tableChildren = new ElementChildren(thead, tbody);
        }
        else
        {
            tableChildren = new ElementChildren(tbody);
        }

        return new Element(
            "table",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-object")),
            tableChildren
        );
    }

    /// <summary>
    /// Builds a member row <c>&lt;tr&gt;</c> for a named-member object table — a
    /// <c>&lt;th class="duetspad-key"&gt;</c> holding the member name, and a <c>&lt;td&gt;</c>
    /// holding the rendered value.
    /// </summary>
    private static Element BuildMemberRow(string key, ITerminalRenderNode valueNode)
    {
        var keyElement = new Element(
            "th",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-key")),
            new ElementChildren(new Text(key))
        );

        var valueCell = new Element("td", ElementAttributes.Empty, new ElementChildren(valueNode));

        return new Element(
            "tr",
            ElementAttributes.Empty,
            new ElementChildren(keyElement, valueCell)
        );
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
