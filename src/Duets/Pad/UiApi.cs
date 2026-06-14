using System.Collections;
using System.Globalization;
using Duets.Pad.Rendering;

namespace Duets.Pad;

/// <summary>
/// Host object bound to the <c>ui</c> global in script. Provides the structured display surface:
/// <see cref="RawHtml"/>, <see cref="Element"/>, <see cref="Text"/>, <see cref="Label"/>,
/// <see cref="Stack"/>, <see cref="Button"/>, and <see cref="Table"/>.
/// </summary>
internal sealed class UiApi(Func<DisplayRenderer> rendererResolver, DumpOptions dumpOptions)
{
    // Resolves the session's current renderer on each render so a post-construction
    // SetObjectRenderers swap is observed instead of a stale construction-time snapshot.
    private readonly Func<DisplayRenderer> _rendererResolver =
        rendererResolver ?? throw new ArgumentNullException(nameof(rendererResolver));
    private readonly DumpOptions _dumpOptions =
        dumpOptions ?? throw new ArgumentNullException(nameof(dumpOptions));

    /// <summary>
    /// Returns a <see cref="Rendering.RawHtml"/> node. This is the only raw-HTML escape hatch.
    /// (JS: <c>ui.rawHtml</c>)
    /// </summary>
    public DisplayContent RawHtml(string content) => DisplayContent.RawHtml(content);

    /// <summary>
    /// Builds a structured <see cref="Rendering.Element"/>.
    /// (JS: <c>ui.element</c>)
    /// </summary>
    public DisplayContent Element(string tag, object? attributes = null, object? children = null)
    {
        var elementAttributes = BuildAttributes(attributes);
        var elementChildren = this.BuildChildren(children);
        return DisplayContent.Element(tag, elementAttributes, elementChildren);
    }

    /// <summary>
    /// Returns a <see cref="Rendering.Text"/> node.
    /// (JS: <c>ui.text</c>)
    /// </summary>
    public DisplayContent Text(string value) => DisplayContent.Text(value);

    /// <summary>
    /// Returns a <c>span.duetspad-label</c> element wrapping <paramref name="value"/>.
    /// (JS: <c>ui.label</c>)
    /// </summary>
    public DisplayContent Label(string value) => DisplayContent.Label(value);

    /// <summary>
    /// Returns a <c>div.duetspad-stack</c> element containing the rendered <paramref name="children"/>.
    /// (JS: <c>ui.stack</c>)
    /// </summary>
    public DisplayContent Stack(object? children = null)
    {
        return DisplayContent.Stack(this.BuildChildren(children));
    }

    /// <summary>
    /// Builds a button element with a click handler. (JS: <c>ui.button</c>)
    /// </summary>
    public DisplayContent Button(string label, Action handler, object? options = null) =>
        DisplayContent.Button(label, handler, BuildButtonOptions(options));

    /// <summary>
    /// Builds a <c>table.duetspad-table</c> element from <paramref name="rows"/>.
    /// Columns default to the keys of the first row; pass <c>options.columns</c> to specify
    /// an explicit ordered list of string column names. (JS: <c>ui.table</c>)
    /// </summary>
    public DisplayContent Table(object? rows, object? options = null)
    {
        if (rows is null or string || rows is not IEnumerable rowsEnumerable)
        {
            throw new ArgumentException("rows must be an array.", nameof(rows));
        }

        var rowList = new List<IDictionary<string, object?>>();
        foreach (var row in rowsEnumerable)
        {
            rowList.Add(CoerceRow(row));
        }

        List<string> columns;
        if (options is IDictionary<string, object?> optionsDict)
        {
            columns = ResolveColumns(optionsDict, rowList);
        }
        else if (options is IDictionary nonGenericOptionsDict)
        {
            var converted = ConvertNonGenericDictionary(nonGenericOptionsDict);
            columns = ResolveColumns(converted, rowList);
        }
        else
        {
            columns = ResolveColumnsFromRows(rowList);
        }

        var projectedRows = rowList
            .Select(r => (IReadOnlyList<KeyValuePair<string, object?>>)[.. r])
            .ToList();
        var renderer = this._rendererResolver();
        return TableRenderBuilder.Build(
            columns,
            projectedRows,
            v => renderer.Render(v, this._dumpOptions)
        );
    }

    private static IDictionary<string, object?> CoerceRow(object? row)
    {
        if (row is IDictionary<string, object?> genericDict)
        {
            return genericDict;
        }

        if (row is IDictionary nonGenericDict)
        {
            return ConvertNonGenericDictionary(nonGenericDict);
        }

        if (RecordProjector.IsRecordLike(row))
        {
            var projected = RecordProjector.Project(row!);
            var result = new Dictionary<string, object?>(projected.Count);
            foreach (var kv in projected)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
        }

        throw new ArgumentException(
            "invalid row: each row must be an object with key/value pairs.",
            "rows"
        );
    }

    private static IDictionary<string, object?> ConvertNonGenericDictionary(IDictionary dict)
    {
        var result = new Dictionary<string, object?>();
        foreach (DictionaryEntry entry in dict)
        {
            var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "";
            result[key] = entry.Value;
        }

        return result;
    }

    private static List<string> ResolveColumns(
        IDictionary<string, object?> optionsDict,
        List<IDictionary<string, object?>> rowList
    )
    {
        if (!optionsDict.TryGetValue("columns", out var columnsValue))
        {
            return ResolveColumnsFromRows(rowList);
        }

        if (columnsValue is null or string || columnsValue is not IEnumerable columnsEnumerable)
        {
            throw new ArgumentException("options.columns must be an array of strings.", "options");
        }

        var columns = new List<string>();
        foreach (var col in columnsEnumerable)
        {
            if (col is not string colStr)
            {
                throw new ArgumentException(
                    "options.columns must be an array of strings.",
                    "options"
                );
            }

            if (columns.Contains(colStr, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"options.columns contains duplicate column name '{colStr}'.",
                    "options"
                );
            }

            columns.Add(colStr);
        }

        return columns;
    }

    private static List<string> ResolveColumnsFromRows(List<IDictionary<string, object?>> rowList)
    {
        if (rowList.Count == 0)
        {
            return [];
        }

        return [.. rowList[0].Keys];
    }

    private static ElementAttributes BuildAttributes(object? attributes)
    {
        if (attributes is null)
        {
            return ElementAttributes.Empty;
        }

        if (attributes is IDictionary<string, object?> genericDict)
        {
            return new ElementAttributes(
                genericDict.Select(kv => new KeyValuePair<string, string?>(
                    kv.Key,
                    kv.Value is null
                        ? null
                        : Convert.ToString(kv.Value, CultureInfo.InvariantCulture)
                ))
            );
        }

        if (attributes is IDictionary nonGenericDict)
        {
            var pairs = new List<KeyValuePair<string, string?>>();
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "";
                var value = entry.Value is null
                    ? null
                    : Convert.ToString(entry.Value, CultureInfo.InvariantCulture);
                pairs.Add(new KeyValuePair<string, string?>(key, value));
            }

            return new ElementAttributes(pairs);
        }

        // Unsupported attributes type — let ElementAttributes constructor surface the error
        // by attempting to iterate; alternatively throw clearly here.
        throw new ArgumentException(
            "attributes must be an object with string keys and string values.",
            nameof(attributes)
        );
    }

    private static ButtonOptions? BuildButtonOptions(object? options)
    {
        if (options is null)
        {
            return null;
        }

        var dict =
            options switch
            {
                IDictionary<string, object?> generic => generic,
                IDictionary nonGeneric => ConvertNonGenericDictionary(nonGeneric),
                _ => null,
            } ?? throw new ArgumentException("options must be an object.", nameof(options));
        var result = new ButtonOptions();
        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        return result;
    }

    private IReadOnlyList<DisplayContent> BuildChildren(object? children)
    {
        if (children is null)
        {
            return [];
        }

        if (children is string)
        {
            throw new ArgumentException(
                "children must be an array, not a string.",
                nameof(children)
            );
        }

        if (children is IEnumerable childrenEnumerable)
        {
            var renderer = this._rendererResolver();
            return
            [
                .. childrenEnumerable
                    .Cast<object?>()
                    .Select(v => renderer.Render(v, this._dumpOptions)),
            ];
        }

        throw new ArgumentException("children must be an array.", nameof(children));
    }
}
