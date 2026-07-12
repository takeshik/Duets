using System.Collections;

namespace Duets.Pad.Rendering;

/// <summary>
/// Immutable, structurally comparable attribute set for <see cref="Element" />.
/// </summary>
public sealed class ElementAttributes
    : IReadOnlyDictionary<string, string?>,
        IEquatable<ElementAttributes>
{
    public static ElementAttributes Empty { get; } = new([]);

    private readonly SortedDictionary<string, string?> attributes;

    public ElementAttributes(IEnumerable<KeyValuePair<string, string?>> attributes)
    {
        if (attributes is null)
        {
            throw new ArgumentNullException(nameof(attributes));
        }

        this.attributes = new SortedDictionary<string, string?>(StringComparer.Ordinal);

        foreach (var attribute in attributes)
        {
            var normalized = Normalize(attribute.Key, attribute.Value);

            if (this.attributes.ContainsKey(normalized.Key))
            {
                throw new ArgumentException(
                    $"Element attribute '{normalized.Key}' is specified more than once.",
                    nameof(attributes)
                );
            }

            this.attributes.Add(normalized.Key, normalized.Value);
        }
    }

    public ElementAttributes(params KeyValuePair<string, string?>[] attributes)
        : this((IEnumerable<KeyValuePair<string, string?>>)attributes) { }

    public int Count => this.attributes.Count;

    public IEnumerable<string> Keys => this.attributes.Keys;

    public IEnumerable<string?> Values => this.attributes.Values;

    public string? this[string key]
    {
        get
        {
            var normalized = NormalizeAttributeName(key);
            ValidateAttributeNameSyntax(normalized, key);

            if (!this.attributes.TryGetValue(normalized, out var value))
            {
                throw new KeyNotFoundException($"Element attribute '{key}' was not found.");
            }

            return value;
        }
    }

    public bool ContainsKey(string key)
    {
        var normalized = NormalizeAttributeName(key);
        ValidateAttributeNameSyntax(normalized, key);

        return this.attributes.ContainsKey(normalized);
    }

    public bool TryGetValue(string name, out string? value)
    {
        var normalized = NormalizeAttributeName(name);
        ValidateAttributeNameSyntax(normalized, name);

        return this.attributes.TryGetValue(normalized, out value);
    }

    public bool Equals(ElementAttributes? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || this.attributes.Count != other.attributes.Count)
        {
            return false;
        }

        using var left = this.attributes.GetEnumerator();
        using var right = other.attributes.GetEnumerator();

        while (left.MoveNext() && right.MoveNext())
        {
            if (
                left.Current.Key != right.Current.Key
                || !StringComparer.Ordinal.Equals(left.Current.Value, right.Current.Value)
            )
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is ElementAttributes other && this.Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var attribute in this.attributes)
        {
            hash.Add(attribute.Key, StringComparer.Ordinal);
            hash.Add(attribute.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() =>
        this.attributes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private static KeyValuePair<string, string?> Normalize(string name, string? value)
    {
        var normalizedName = NormalizeAttributeName(name);

        ValidateAttributeNameSyntax(normalizedName, name);
        ValidateElementAttributePolicy(normalizedName, name);
        ValidateUrlAttributePolicy(normalizedName, name, value);

        return new KeyValuePair<string, string?>(normalizedName, value);
    }

    private static string NormalizeAttributeName(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        return name.Trim().ToLowerInvariant();
    }

    private static void ValidateAttributeNameSyntax(string normalizedName, string name)
    {
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("Element attribute name cannot be empty.", nameof(name));
        }

        if (!IsAsciiLowerLetter(normalizedName[0]) && normalizedName[0] is not '_' and not ':')
        {
            throw new ArgumentException(
                $"Element attribute '{name}' is not a valid attribute name.",
                nameof(name)
            );
        }

        foreach (var ch in normalizedName)
        {
            if (!IsAsciiLetterOrDigit(ch) && ch is not '_' and not ':' and not '.' and not '-')
            {
                throw new ArgumentException(
                    $"Element attribute '{name}' is not a valid attribute name.",
                    nameof(name)
                );
            }
        }
    }

    private static bool IsAsciiLowerLetter(char ch) => ch is >= 'a' and <= 'z';

    private static bool IsAsciiLetterOrDigit(char ch) =>
        ch is (>= 'a' and <= 'z') or (>= '0' and <= '9');

    private static void ValidateElementAttributePolicy(string normalizedName, string name)
    {
        if (normalizedName.StartsWith("on", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Element attribute '{name}' is not allowed because event attributes require a separate interaction contract.",
                nameof(name)
            );
        }

        if (normalizedName == "srcdoc")
        {
            throw new ArgumentException(
                "Element attribute 'srcdoc' is not allowed because inline HTML payloads must use RawHtml explicitly.",
                nameof(name)
            );
        }
    }

    private static bool IsUrlAttributeName(string normalizedName) =>
        normalizedName is "href" or "src" or "action" or "formaction" or "poster" or "srcset";

    private static void ValidateUrlAttributePolicy(
        string normalizedName,
        string name,
        string? value
    )
    {
        if (value is null || !IsUrlAttributeName(normalizedName))
        {
            return;
        }

        var trimmedValue = value.TrimStart();

        if (trimmedValue.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Element attribute '{name}' does not allow the javascript: URL scheme.",
                nameof(name)
            );
        }
    }
}
