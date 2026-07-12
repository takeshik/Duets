namespace Duets.Pad.Rendering;

/// <summary>
/// Terminal renderable describing a structured, display-only HTML element.
/// </summary>
public sealed record Element(string Tag, ElementAttributes Attributes, ElementChildren Children)
    : ITerminalRenderNode
{
    public Element(string tag)
        : this(tag, ElementAttributes.Empty, ElementChildren.Empty) { }

    public Element(string tag, ElementChildren children)
        : this(tag, ElementAttributes.Empty, children) { }

    public Element(string tag, ElementAttributes attributes)
        : this(tag, attributes, ElementChildren.Empty) { }

    public string Tag { get; } = NormalizeAndValidateTag(Tag);

    public ElementAttributes Attributes { get; } =
        Attributes ?? throw new ArgumentNullException(nameof(Attributes));

    public ElementChildren Children { get; } =
        Children ?? throw new ArgumentNullException(nameof(Children));

    public bool CanReduce => false;

    public IRenderNode Reduce() => this;

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
