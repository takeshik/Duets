using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Duets.Pad.Rendering;

internal sealed class DefaultObjectRenderer : IObjectRenderer
{
    public bool CanRender(object value) => true;

    public DisplayContent Render(object value, RenderContext context) =>
        RenderValue(value, context);

    private static DisplayContent RenderValue(object value, RenderContext context)
    {
        switch (value)
        {
            case Enum e:
                return DisplayContent.Text(e.ToString());

            case string s:
                return DisplayContent.Text(s);

            case bool b:
                return DisplayContent.Text(b ? "true" : "false");

            case char c:
                return DisplayContent.Text(c.ToString());

            case byte n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case sbyte n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case short n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case ushort n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case int n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case uint n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case long n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case ulong n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case float n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case double n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));

            case decimal n:
                return DisplayContent.Text(n.ToString(CultureInfo.InvariantCulture));
        }

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
                typeHeaderText: "Object",
                includeSummary: false,
                allowEmptyTable: true,
                context
            );
        }

        // 2. Map (Form B): non-generic IDictionary, or a generic dictionary
        //    (IDictionary<,> / IReadOnlyDictionary<,>) that is not the dynamic shape handled
        //    above. A bare IEnumerable<KeyValuePair<,>> is not a map.
        if (RecordProjector.TryExtractMapEntries(value, out var mapEntries, out var mapCheapCount))
        {
            return RenderMap(value, mapEntries, mapCheapCount, context);
        }

        // 3. IEnumerable → existing collection path.
        if (value is IEnumerable enumerable)
        {
            return RenderEnumerable(enumerable, context);
        }

        // 4. Ordinary CLR object → named-member object (Form A).
        var type = value.GetType();
        var members = RecordProjector.Project(value);
        var typeHeaderText = IsAnonymousOrCompilerGenerated(type) ? "Object" : type.Name;
        return RenderNamedMemberObject(
            value,
            members,
            typeHeaderText,
            includeSummary: !IsAnonymousOrCompilerGenerated(type),
            allowEmptyTable: false,
            context
        );
    }

    private static bool IsAnonymousOrCompilerGenerated(Type type) =>
        Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute))
        || type.Name.Contains("AnonymousType", StringComparison.Ordinal);

    /// <summary>
    /// Returns <see langword="true"/> when the runtime type of <paramref name="value"/> overrides
    /// <see cref="object.ToString()"/> — i.e. the declaring type of the no-argument
    /// <c>ToString</c> method is not <see cref="object"/>.
    /// </summary>
    private static bool OverridesToString(object value) =>
        value.GetType().GetMethod("ToString", Type.EmptyTypes)?.DeclaringType != typeof(object);

    /// <summary>
    /// Enumerates up to <paramref name="maxItems"/> + 1 items from <paramref name="source"/>,
    /// stopping early to detect overflow without fully materializing a lazy/infinite sequence.
    /// </summary>
    /// <param name="source">The sequence to materialize (partially).</param>
    /// <param name="maxItems">The cap on visible items.</param>
    /// <param name="truncated">
    /// Set to <see langword="true"/> when the sequence contained more than
    /// <paramref name="maxItems"/> items.
    /// </param>
    /// <returns>At most <paramref name="maxItems"/> items.</returns>
    private static List<object?> MaterializeCapped(
        IEnumerable source,
        int maxItems,
        out bool truncated
    )
    {
        var items = new List<object?>(Math.Min(maxItems, 64));
        truncated = false;

        foreach (var item in source)
        {
            if (items.Count >= maxItems)
            {
                // One extra item confirmed — sequence exceeds cap; stop immediately.
                truncated = true;
                break;
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Formats the header text for a collection: "{TypeName} (N items)" when the exact count
    /// is known; "{TypeName} (showing first {MaxItems})" when the sequence was truncated and the
    /// exact total is unknown; "{TypeName} (N items)" when truncated but the total was known cheaply.
    /// </summary>
    private static string FormatCollectionHeader(
        string typeName,
        int exactOrKnownCount,
        bool truncated,
        int maxItems
    ) =>
        truncated
            ? $"{typeName} (showing first {maxItems})"
            : $"{typeName} ({exactOrKnownCount} items)";

    private static DisplayContent RenderMap(
        object mapValue,
        IEnumerable<KeyValuePair<string, object?>> entriesSource,
        int? cheapCount,
        RenderContext context
    )
    {
        var mapType = mapValue.GetType();
        var maxItems = context.Options.MaxItems;

        // Materialize at most maxItems entries, detect overflow without full materialization.
        var visibleEntries = new List<KeyValuePair<string, object?>>(Math.Min(maxItems, 64));
        var truncated = false;

        foreach (var entry in entriesSource)
        {
            if (visibleEntries.Count >= maxItems)
            {
                truncated = true;
                break;
            }

            visibleEntries.Add(entry);
        }

        // Use cheap count when available; fall back to materialized count.
        var knownCount = cheapCount ?? visibleEntries.Count;
        var headerText = truncated
            ? $"{mapType.Name} (showing first {maxItems})"
            : $"{mapType.Name} ({knownCount} items)";

        var typeHeader = TableRenderBuilder.BuildTypeheaderRow(headerText, columnCount: 2);

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

        var rows = new List<ITerminalRenderNode>(visibleEntries.Count + (truncated ? 1 : 0));
        var interactions = new List<PendingInteractions>();
        for (var rowIndex = 0; rowIndex < visibleEntries.Count; rowIndex++)
        {
            var entry = visibleEntries[rowIndex];
            var valueContent = context.RenderChild(entry.Value);

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
                        new Element(
                            "td",
                            ElementAttributes.Empty,
                            new ElementChildren(valueContent.Body)
                        )
                    )
                )
            );
            interactions.Add(valueContent.Interactions.PrependPath(1, rowIndex, 1, 0));
        }

        if (truncated)
        {
            rows.Add(TableRenderBuilder.BuildTruncationRow(columnCount: 2));
        }

        var tbody = new Element("tbody", ElementAttributes.Empty, [.. rows]);

        return new DisplayContent(
            new Element(
                "table",
                new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-map")),
                new ElementChildren(thead, tbody)
            ),
            PendingInteractions.Merge(interactions)
        );
    }

    private static DisplayContent RenderEnumerable(IEnumerable enumerable, RenderContext context)
    {
        var maxItems = context.Options.MaxItems;

        // Check for cheap count before materializing (to avoid fully evaluating lazy sequences).
        var cheapCount = RecordProjector.TryGetCheapCount(enumerable);

        // Materialize at most maxItems items, detect overflow.
        var items = MaterializeCapped(enumerable, maxItems, out var truncated);

        // Determine if all items are record-like (dictionaries or non-primitive, non-string,
        // non-enumerable CLR objects). Record-like lists use member columns; scalar/mixed lists
        // still render as a one-column table so collection output has a consistent row shape.
        if (items.Count > 0 && items.All(RecordProjector.IsRecordLike))
        {
            return RenderTabular(enumerable, items, cheapCount, truncated, context);
        }

        return RenderScalarTable(enumerable, items, cheapCount, truncated, context);
    }

    private static DisplayContent RenderTabular(
        IEnumerable source,
        List<object?> items,
        int? cheapCount,
        bool truncated,
        RenderContext context
    )
    {
        // Build projected rows and compute union of columns in first-seen order.
        // Use ProjectCapped for the per-row projection so that a map used as a tabular row does
        // not fully enumerate its entries — only the first MaxItems keys are materialized.
        var maxItemsForRows = context.Options.MaxItems;
        var projectedRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>(items.Count);
        var columnOrder = new List<string>();
        var columnSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var projected = RecordProjector.ProjectCapped(item!, maxItemsForRows);
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
            return RenderScalarTable(source, items, cheapCount, truncated, context);
        }

        var maxItems = context.Options.MaxItems;
        var typeName = source.GetType().Name;

        // Exact count: use cheap count if available; otherwise use materialized count (possibly
        // truncated — in that case the header text communicates the cap rather than an exact total).
        var knownCount = cheapCount ?? items.Count;
        var headerText = FormatCollectionHeader(typeName, knownCount, truncated, maxItems);

        return TableRenderBuilder.Build(
            columnOrder,
            projectedRows,
            context.RenderChild,
            typeHeaderText: headerText,
            truncated: truncated
        );
    }

    private static DisplayContent RenderScalarTable(
        IEnumerable source,
        List<object?> items,
        int? cheapCount,
        bool truncated,
        RenderContext context
    )
    {
        var maxItems = context.Options.MaxItems;
        var typeName = source.GetType().Name;
        var knownCount = cheapCount ?? items.Count;
        var headerText = FormatCollectionHeader(typeName, knownCount, truncated, maxItems);

        var thead = new Element(
            "thead",
            ElementAttributes.Empty,
            new ElementChildren(TableRenderBuilder.BuildTypeheaderRow(headerText, columnCount: 1))
        );

        var rows = new List<ITerminalRenderNode>(items.Count + (truncated ? 1 : 0));
        var interactions = new List<PendingInteractions>();
        for (var rowIndex = 0; rowIndex < items.Count; rowIndex++)
        {
            var itemContent = context.RenderChild(items[rowIndex]);
            rows.Add(
                new Element(
                    "tr",
                    ElementAttributes.Empty,
                    new ElementChildren(
                        new Element(
                            "td",
                            ElementAttributes.Empty,
                            new ElementChildren(itemContent.Body)
                        )
                    )
                )
            );
            interactions.Add(itemContent.Interactions.PrependPath(1, rowIndex, 0, 0));
        }

        if (truncated)
        {
            rows.Add(TableRenderBuilder.BuildTruncationRow(columnCount: 1));
        }

        var tbody = new Element("tbody", ElementAttributes.Empty, [.. rows]);

        return new DisplayContent(
            new Element(
                "table",
                new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-table")),
                new ElementChildren(thead, tbody)
            ),
            PendingInteractions.Merge(interactions)
        );
    }

    /// <summary>
    /// Renders a named-member object (Form A) — a vertical property table with one row per
    /// member (member name → rendered value). Used for ordinary CLR objects, JS object literals
    /// / dynamic-object values, and anonymous types.
    /// </summary>
    /// <param name="typeHeaderText">
    /// The conceptual type name displayed in the always-present collapsible header. Dynamic and
    /// anonymous objects use <c>Object</c> so implementation-specific CLR type names do not leak
    /// into script output.
    /// </param>
    /// <param name="includeSummary">
    /// Whether an overridden <c>ToString()</c> may produce a summary row. Dynamic and anonymous
    /// objects keep this disabled so CLR implementation details cannot leak beneath their
    /// conceptual <c>Object</c> header.
    /// </param>
    /// <param name="allowEmptyTable">
    /// When <see langword="true" />, a value with zero projectable members still renders as an
    /// empty object table (used for dynamic/JS objects, where <c>ToString()</c> would leak the
    /// marshaled CLR type name such as <c>System.Dynamic.ExpandoObject</c>). When
    /// <see langword="false" /> (ordinary CLR objects), a zero-member value falls back to
    /// <c>ToString()</c>.
    /// </param>
    private static DisplayContent RenderNamedMemberObject(
        object value,
        IReadOnlyList<KeyValuePair<string, object?>> members,
        string typeHeaderText,
        bool includeSummary,
        bool allowEmptyTable,
        RenderContext context
    )
    {
        // Fall back to ToString() if the object exposes no projectable members, unless an empty
        // table is explicitly allowed (dynamic/JS objects).
        if (members.Count == 0 && !allowEmptyTable)
        {
            return DisplayContent.Text(value.ToString() ?? "");
        }

        var rows = new List<ITerminalRenderNode>(members.Count);
        var interactions = new List<PendingInteractions>();
        for (var rowIndex = 0; rowIndex < members.Count; rowIndex++)
        {
            var member = members[rowIndex];
            var valueContent = member.Value is ITerminalRenderNode markerNode
                ? DisplayContent.FromNode(markerNode)
                : context.RenderChild(member.Value);

            // RecordProjector may already return a Text("[error]") marker as the value if the
            // getter threw; render it as-is rather than recursing again.

            rows.Add(BuildMemberRow(member.Key, valueContent.Body));
            interactions.Add(valueContent.Interactions);
        }

        var tbody = new Element("tbody", ElementAttributes.Empty, [.. rows]);

        var type = value.GetType();
        var theadRows = new List<ITerminalRenderNode>
        {
            TableRenderBuilder.BuildTypeheaderRow(typeHeaderText, columnCount: 2),
        };

        // Summary row: only when the type overrides ToString() and is not compiler-generated.
        // The ToString() call is wrapped in try/catch: a throwing ToString() must not fail
        // the object render — the summary row is optional display metadata.
        if (
            includeSummary
            && OverridesToString(value)
            && !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute))
        )
        {
            string? summary;
            try
            {
                summary = value.ToString();
            }
            catch
            {
                summary = null;
            }

            if (summary is not null)
            {
                theadRows.Add(
                    new Element(
                        "tr",
                        ElementAttributes.Empty,
                        new ElementChildren(
                            new Element(
                                "td",
                                new ElementAttributes(
                                    new KeyValuePair<string, string?>("class", "duetspad-summary"),
                                    new KeyValuePair<string, string?>("colspan", "2")
                                ),
                                new ElementChildren(new Text(summary))
                            )
                        )
                    )
                );
            }
        }

        var thead = new Element("thead", ElementAttributes.Empty, [.. theadRows]);

        var body = new Element(
            "table",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-object")),
            new ElementChildren(thead, tbody)
        );
        return new DisplayContent(
            body,
            PendingInteractions.Merge(
                interactions.Select((items, rowIndex) => items.PrependPath(1, rowIndex, 1, 0))
            )
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
}
