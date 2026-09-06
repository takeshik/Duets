namespace Duets.Pad.Rendering;

/// <summary>
/// Terminal renderable describing a structured, display-only HTML element.
/// </summary>
/// <param name="Tag">The HTML tag name, which is normalized and validated.</param>
/// <param name="Attributes">The validated element attributes.</param>
/// <param name="Children">The element's terminal child nodes.</param>
public sealed record Element(string Tag, ElementAttributes Attributes, ElementChildren Children)
    : ITerminalRenderNode
{
    /// <summary>Creates an empty element without attributes.</summary>
    /// <param name="tag">The HTML tag name.</param>
    public Element(string tag)
        : this(tag, ElementAttributes.Empty, ElementChildren.Empty) { }

    /// <summary>Creates an element with children and no attributes.</summary>
    /// <param name="tag">The HTML tag name.</param>
    /// <param name="children">The terminal child nodes.</param>
    public Element(string tag, ElementChildren children)
        : this(tag, ElementAttributes.Empty, children) { }

    /// <summary>Creates an empty element with attributes.</summary>
    /// <param name="tag">The HTML tag name.</param>
    /// <param name="attributes">The element attributes.</param>
    public Element(string tag, ElementAttributes attributes)
        : this(tag, attributes, ElementChildren.Empty) { }

    /// <summary>Gets the normalized HTML tag name.</summary>
    public string Tag { get; } = NormalizeAndValidateTag(Tag);

    /// <summary>Gets the validated element attributes.</summary>
    public ElementAttributes Attributes { get; } =
        Attributes ?? throw new ArgumentNullException(nameof(Attributes));

    /// <summary>Gets the terminal child nodes.</summary>
    public ElementChildren Children { get; } =
        Children ?? throw new ArgumentNullException(nameof(Children));

    /// <inheritdoc />
    public bool CanReduce => false;

    /// <inheritdoc />
    public IRenderNode Reduce() => this;

    /// <summary>Creates a copy with the specified child nodes.</summary>
    /// <param name="children">The replacement child nodes.</param>
    /// <returns>The updated element.</returns>
    public Element WithChildren(ElementChildren children) =>
        new(this.Tag, this.Attributes, children);

    private static string NormalizeAndValidateTag(string tag)
    {
        if (tag is null)
        {
            throw new ArgumentNullException(nameof(tag));
        }

        var normalized = tag.Trim().ToLowerInvariant();

        ValidateTagNameSyntax(normalized, tag);
        ValidateElementTagPolicy(normalized, tag);

        return normalized;
    }

    private static void ValidateTagNameSyntax(string normalizedTag, string tag)
    {
        if (normalizedTag.Length == 0)
        {
            throw new ArgumentException("Element tag cannot be empty.", nameof(tag));
        }

        if (!IsAsciiLowerLetter(normalizedTag[0]))
        {
            throw new ArgumentException(
                $"Element tag '{tag}' is not a valid tag name.",
                nameof(tag)
            );
        }

        foreach (var ch in normalizedTag)
        {
            if (!IsAsciiLetterOrDigit(ch) && ch != '-')
            {
                throw new ArgumentException(
                    $"Element tag '{tag}' is not a valid tag name.",
                    nameof(tag)
                );
            }
        }
    }

    private static bool IsAsciiLowerLetter(char ch) => ch is >= 'a' and <= 'z';

    private static bool IsAsciiLetterOrDigit(char ch) =>
        ch is (>= 'a' and <= 'z') or (>= '0' and <= '9');

    private static void ValidateElementTagPolicy(string normalizedTag, string tag)
    {
        if (normalizedTag is "script" or "iframe" or "object" or "embed" or "template")
        {
            throw new ArgumentException(
                $"Element tag '{tag}' is not allowed because it is outside the structured display contract.",
                nameof(tag)
            );
        }
    }
}
