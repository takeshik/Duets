using System.Collections;
using System.Reflection;

namespace Duets.Pad.Rendering;

/// <summary>
/// Projects an object value into an ordered sequence of named members for structured display.
/// </summary>
internal static class RecordProjector
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="item"/> is a record-like value:
    /// an <see cref="IDictionary"/>, a dynamic JS-object-shaped value, or a non-null,
    /// non-primitive, non-string, non-enumerable CLR object. Used to decide whether a sequence
    /// should be rendered as a table.
    /// </summary>
    public static bool IsRecordLike(object? item)
    {
        if (
            item
            is null
                or string
                or bool
                or char
                or byte
                or sbyte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal
        )
        {
            return false;
        }

        // Dynamic JS-object-shaped values (e.g. ExpandoObject) are named-member objects, not
        // collections, so a sequence of them renders as a table (ADR-40 conceptual-shape rule).
        if (item is System.Dynamic.IDynamicMetaObjectProvider and not IDictionary)
        {
            return true;
        }

        // Enumerables (but not dictionaries) are not record-like.
        if (item is IEnumerable and not IDictionary)
        {
            return false;
        }

        // Render nodes are already in the rendering domain — not data records.
        if (item is IRenderNode)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to project <paramref name="value" /> as a dynamic JS-object-like value — one that
    /// implements <see cref="System.Dynamic.IDynamicMetaObjectProvider" /> (e.g.
    /// <see cref="System.Dynamic.ExpandoObject" />, which is the shape Jint marshals JS object
    /// literals to) but is NOT a non-generic <see cref="IDictionary" />. Such values expose their
    /// members through a string-keyed enumeration of <see cref="KeyValuePair{TKey, TValue}" />;
    /// keys are projected as member names via <c>ToString()</c>.
    /// </summary>
    /// <remarks>
    /// Detection is deliberately restricted to the dynamic-object shape via the BCL type
    /// <see cref="System.Dynamic.IDynamicMetaObjectProvider" />, so genuine generic CLR maps
    /// (e.g. types implementing only <see cref="IReadOnlyDictionary{TKey, TValue}" /> or
    /// <see cref="IDictionary{TKey, TValue}" />) are NOT captured here and continue to render as
    /// maps (Form B). The renderer stays engine-agnostic: no backend-specific type is referenced.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> and the projected member list if <paramref name="value" /> has
    /// this shape; otherwise <see langword="false" /> and an empty list.
    /// </returns>
    public static bool TryProjectDynamicObjectLike(
        object value,
        out IReadOnlyList<KeyValuePair<string, object?>> members
    )
    {
        if (value is IDictionary)
        {
            members = [];
            return false;
        }

        if (value is not System.Dynamic.IDynamicMetaObjectProvider)
        {
            members = [];
            return false;
        }

        if (value is not IEnumerable enumerable)
        {
            members = [];
            return false;
        }

        members = EnumerateKeyValuePairs(enumerable);
        return true;
    }

    /// <summary>
    /// Attempts to extract the entries of a map value for the Key/Value grid (Form B). Handles a
    /// non-generic <see cref="IDictionary" />, or a generic-only dictionary value that implements
    /// <see cref="IDictionary{TKey, TValue}" /> or <see cref="IReadOnlyDictionary{TKey, TValue}" />.
    /// Keys are projected via <c>ToString()</c>.
    /// </summary>
    /// <remarks>
    /// The dynamic JS-object shape (<see cref="System.Dynamic.IDynamicMetaObjectProvider" />, e.g.
    /// <see cref="System.Dynamic.ExpandoObject" />) is excluded here: although it is
    /// dictionary-like, ADR-40 routes it to the named-member object presentation (Form A) via
    /// <see cref="TryProjectDynamicObjectLike" />, which the renderer checks first.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> and the extracted entries if <paramref name="value" /> is a map;
    /// otherwise <see langword="false" /> and an empty list.
    /// </returns>
    public static bool TryExtractMapEntries(
        object value,
        out IReadOnlyList<KeyValuePair<string, object?>> entries
    )
    {
        if (value is IDictionary dict)
        {
            entries = ProjectDictionary(dict);
            return true;
        }

        if (value is System.Dynamic.IDynamicMetaObjectProvider)
        {
            entries = [];
            return false;
        }

        if (
            value is IEnumerable enumerable
            && ImplementsGenericDictionaryInterface(value.GetType())
        )
        {
            entries = EnumerateKeyValuePairs(enumerable);
            return true;
        }

        // A bare KeyValuePair sequence (e.g. List<KeyValuePair<string, int>>) is intentionally
        // NOT treated as a map: only genuine dictionary interfaces qualify. Such sequences fall
        // through to the collection path and render as an array or tabular list.
        entries = [];
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> implements a genuine generic
    /// dictionary interface: <see cref="IDictionary{TKey, TValue}"/> or
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/>. A bare
    /// <see cref="IEnumerable{T}"/> of <see cref="KeyValuePair{TKey, TValue}"/> does NOT qualify.
    /// </summary>
    private static bool ImplementsGenericDictionaryInterface(Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var definition = iface.GetGenericTypeDefinition();

            if (
                definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates an <see cref="IEnumerable" /> whose items expose <c>Key</c>/<c>Value</c>
    /// properties (e.g. <see cref="KeyValuePair{TKey, TValue}" />), projecting each key via
    /// <c>ToString()</c>. Items without both properties (or null items) are skipped.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, object?>> EnumerateKeyValuePairs(
        IEnumerable enumerable
    )
    {
        var result = new List<KeyValuePair<string, object?>>();

        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            var itemType = item.GetType();
            var keyProperty = itemType.GetProperty("Key");
            var valueProperty = itemType.GetProperty("Value");

            if (keyProperty is null || valueProperty is null)
            {
                continue;
            }

            var key = keyProperty.GetValue(item);
            var memberValue = valueProperty.GetValue(item);

            result.Add(new KeyValuePair<string, object?>(key?.ToString() ?? "", memberValue));
        }

        return result;
    }

    /// <summary>
    /// Projects <paramref name="value" /> into an ordered list of key/value pairs:
    /// <list type="bullet">
    ///   <item><description>Dynamic JS-object shape (e.g. <see cref="System.Dynamic.ExpandoObject"/>) — string keys from the dynamic enumeration.</description></item>
    ///   <item><description><see cref="IDictionary" /> — iterates entries; key via <c>ToString()</c>.</description></item>
    ///   <item><description>CLR object — public instance non-indexer properties in reflection order, then public instance fields.</description></item>
    /// </list>
    /// If a property getter or field access throws, the member is still included with a <see cref="Text" /> marker
    /// value of <c>[error]</c> rather than propagating the exception.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object?>> Project(object value)
    {
        if (TryProjectDynamicObjectLike(value, out var dynamicMembers))
        {
            return dynamicMembers;
        }

        if (value is IDictionary dict)
        {
            return ProjectDictionary(dict);
        }

        return ProjectClrObject(value);
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> ProjectDictionary(IDictionary dict)
    {
        var result = new List<KeyValuePair<string, object?>>();

        foreach (DictionaryEntry entry in dict)
        {
            var key = entry.Key?.ToString() ?? "";
            result.Add(new KeyValuePair<string, object?>(key, entry.Value));
        }

        return result;
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> ProjectClrObject(object value)
    {
        var result = new List<KeyValuePair<string, object?>>();
        var type = value.GetType();

        // Public instance properties, non-indexer only, in reflection order.
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? memberValue;
            try
            {
                memberValue = property.GetValue(value);
            }
            catch
            {
                memberValue = new Text("[error]");
            }

            result.Add(new KeyValuePair<string, object?>(property.Name, memberValue));
        }

        // Public instance fields, in reflection order.
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? memberValue;
            try
            {
                memberValue = field.GetValue(value);
            }
            catch
            {
                memberValue = new Text("[error]");
            }

            result.Add(new KeyValuePair<string, object?>(field.Name, memberValue));
        }

        return result;
    }
}
