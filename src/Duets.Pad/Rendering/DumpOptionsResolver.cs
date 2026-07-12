using System.Collections;
using System.Dynamic;
using System.Globalization;

namespace Duets.Pad.Rendering;

/// <summary>
/// Merges a JS options object into a <see cref="DumpOptions"/> baseline.
/// </summary>
/// <remarks>
/// The JS <c>dump(value, opts?)</c> function passes <paramref name="opts"/> as a dynamic
/// object whose concrete type depends on the script engine backend. This resolver normalises
/// the three common shapes (generic dictionary, non-generic dictionary, IDynamicMetaObjectProvider
/// enumerable) into a string-keyed map and extracts <c>maxDepth</c> / <c>maxItems</c>.
/// </remarks>
internal static class DumpOptionsResolver
{
    /// <summary>
    /// Returns a new <see cref="DumpOptions"/> that merges <paramref name="opts"/> over
    /// <paramref name="baseline"/>. Fields absent or null in <paramref name="opts"/> retain
    /// the baseline value. Returns <paramref name="baseline"/> unchanged when
    /// <paramref name="opts"/> is null or unrecognised.
    /// </summary>
    public static DumpOptions Merge(DumpOptions baseline, object? opts)
    {
        if (opts is null)
        {
            return baseline;
        }

        var dict = ToStringKeyedDict(opts);
        if (dict is null)
        {
            return baseline;
        }

        var maxDepth = baseline.MaxDepth;
        var maxItems = baseline.MaxItems;

        if (
            dict.TryGetValue("maxDepth", out var maxDepthRaw)
            && maxDepthRaw is not null
            && TryParseInt(maxDepthRaw, out var parsedDepth)
            && parsedDepth >= 0
        )
        {
            maxDepth = parsedDepth;
        }

        if (
            dict.TryGetValue("maxItems", out var maxItemsRaw)
            && maxItemsRaw is not null
            && TryParseInt(maxItemsRaw, out var parsedItems)
            && parsedItems >= 0
        )
        {
            maxItems = parsedItems;
        }

        return baseline with
        {
            MaxDepth = maxDepth,
            MaxItems = maxItems,
        };
    }

    private static IDictionary<string, object?>? ToStringKeyedDict(object opts)
    {
        if (opts is IDictionary<string, object?> generic)
        {
            return generic;
        }

        if (opts is IDictionary nonGeneric)
        {
            var converted = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in nonGeneric)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                converted[key] = entry.Value;
            }

            return converted;
        }

        if (opts is IDynamicMetaObjectProvider and IEnumerable enumerable)
        {
            // Project dynamic object (e.g. Jint object literal) as key/value pairs.
            var converted = new Dictionary<string, object?>();
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                var itemType = item.GetType();
                var keyProp = itemType.GetProperty("Key");
                var valueProp = itemType.GetProperty("Value");
                if (keyProp is null || valueProp is null)
                {
                    continue;
                }

                var k = keyProp.GetValue(item)?.ToString() ?? string.Empty;
                converted[k] = valueProp.GetValue(item);
            }

            return converted;
        }

        return null;
    }

    private static bool TryParseInt(object value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}
