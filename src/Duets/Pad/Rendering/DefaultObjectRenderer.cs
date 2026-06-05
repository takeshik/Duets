using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Duets.Pad.Rendering;

internal sealed class DefaultObjectRenderer : IObjectRenderer
{
    private const int MaxDepth = 32;

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

            return new Text(value.ToString() ?? "");
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
        var children = new List<ITerminalRenderNode>();

        foreach (var item in enumerable)
        {
            children.Add(RenderValue(item, visited, depth + 1));
        }

        return new Element(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-array")),
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
