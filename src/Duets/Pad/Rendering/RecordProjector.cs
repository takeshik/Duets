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
    /// an <see cref="IDictionary"/>, or a non-null, non-primitive, non-string, non-enumerable
    /// CLR object. Used to decide whether a sequence should be rendered as a table.
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
    /// Projects <paramref name="value" /> into an ordered list of key/value pairs:
    /// <list type="bullet">
    ///   <item><description><see cref="IDictionary" /> — iterates entries; key via <c>ToString()</c>.</description></item>
    ///   <item><description>CLR object — public instance non-indexer properties in reflection order, then public instance fields.</description></item>
    /// </list>
    /// If a property getter or field access throws, the member is still included with a <see cref="Text" /> marker
    /// value of <c>[error]</c> rather than propagating the exception.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object?>> Project(object value)
    {
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
