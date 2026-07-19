namespace Duets.Pad.Rendering;

/// <summary>
/// Helpers for the locatable marker attribute emitted by <see cref="DisplayInput"/> (ADR-47) and
/// <see cref="DisplayFilePicker"/> (ADR-50). Unlike <see cref="SlotMarker"/>, the marked element
/// itself carries the field-backed state's identity and kind.
/// </summary>
internal static class FieldMarker
{
    /// <summary>The data attribute that identifies a field marker element by field id.</summary>
    public const string AttributeName = "data-duetspad-field";

    /// <summary>The data attribute carrying the field's <see cref="FieldKind"/>.</summary>
    public const string KindAttributeName = "data-duetspad-field-kind";

    /// <summary>The attribute encoding a text-like field's current value.</summary>
    public const string ValueAttributeName = "value";

    /// <summary>The boolean attribute encoding a checkbox or radio option's checked state.</summary>
    public const string CheckedAttributeName = "checked";

    /// <summary>
    /// Returns the display paths (relative to <paramref name="root"/>) of every marker element for
    /// the field identified by <paramref name="id"/>. A matched marker is not descended into: a
    /// field element never contains a nested field. A <see cref="FieldKind.Radio"/> group renders
    /// one marked element per option, so this may return more than one path.
    /// </summary>
    public static IReadOnlyList<DisplayPath> Find(ITerminalRenderNode root, Guid id) =>
        FindWithKind(root, id).Markers;

    /// <summary>
    /// Like <see cref="Find"/>, but also resolves the field's <see cref="FieldKind"/> from the first
    /// matched marker's own <c>data-duetspad-field-kind</c> attribute, rather than requiring the
    /// caller to supply it. Used for a browser-originated commit (ADR-47), whose request does not
    /// carry the kind. <c>Kind</c> is <see langword="null"/> when no marker was found, or the first
    /// marker's kind attribute is missing or unrecognized.
    /// </summary>
    public static (IReadOnlyList<DisplayPath> Markers, FieldKind? Kind) FindWithKind(
        ITerminalRenderNode root,
        Guid id
    )
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var idValue = id.ToString("D");
        var results = new List<DisplayPath>();
        var path = new List<int>();
        FieldKind? kind = null;
        Walk(root, idValue, path, results, ref kind);
        return (results, kind);
    }

    /// <summary>
    /// Collects the ids of every field marker reachable from <paramref name="root"/> into
    /// <paramref name="into"/>. Used to garbage-collect field-store entries whose markers are no
    /// longer reachable from any Canvas or Timeline content. The same retained set is supplied to
    /// the attachment store for file-picker cleanup (ADR-50).
    /// </summary>
    public static void CollectIds(ITerminalRenderNode root, ISet<Guid> into)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        CollectIdsCore(root, into);
    }

    /// <summary>
    /// Rebuilds the tree rooted at <paramref name="root"/>, updating the value-encoding attribute of
    /// the elements at <paramref name="markerPaths"/> to reflect <paramref name="value"/> for a field
    /// of the given <paramref name="kind"/>.
    /// </summary>
    public static ITerminalRenderNode ApplyValue(
        ITerminalRenderNode root,
        IReadOnlyList<DisplayPath> markerPaths,
        FieldKind kind,
        string value
    )
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (markerPaths is null)
        {
            throw new ArgumentNullException(nameof(markerPaths));
        }

        var result = root;
        foreach (var markerPath in markerPaths)
        {
            result = Rewrite(
                result,
                markerPath.Segments,
                0,
                element => TransformElement(element, kind, value)
            );
        }

        return result;
    }

    /// <summary>
    /// Replaces every marked element at <paramref name="markerPaths"/> with
    /// <paramref name="replacement"/>.
    /// </summary>
    public static ITerminalRenderNode Replace(
        ITerminalRenderNode root,
        IReadOnlyList<DisplayPath> markerPaths,
        ITerminalRenderNode replacement
    )
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (markerPaths is null)
        {
            throw new ArgumentNullException(nameof(markerPaths));
        }

        if (replacement is null)
        {
            throw new ArgumentNullException(nameof(replacement));
        }

        var result = root;
        foreach (var markerPath in markerPaths)
        {
            result = ReplaceNode(result, markerPath.Segments, 0, replacement);
        }

        return result;
    }

    private static Element TransformElement(Element element, FieldKind kind, string value)
    {
        switch (kind)
        {
            case FieldKind.CheckBox:
                return string.Equals(value, "True", StringComparison.Ordinal)
                    ? SetBooleanAttribute(element, CheckedAttributeName, present: true)
                    : SetBooleanAttribute(element, CheckedAttributeName, present: false);

            case FieldKind.Radio:
                // The element's own "value" attribute is the option's value, not the field's
                // current value: only the option whose value matches the field's value is checked.
                var optionValue = element.Attributes.TryGetValue(ValueAttributeName, out var ov)
                    ? ov
                    : null;
                var checkedNow = string.Equals(optionValue, value, StringComparison.Ordinal);
                return SetBooleanAttribute(element, CheckedAttributeName, checkedNow);

            default:
                return ReplaceAttribute(element, ValueAttributeName, value);
        }
    }

    private static ITerminalRenderNode ReplaceNode(
        ITerminalRenderNode node,
        IReadOnlyList<int> segments,
        int depth,
        ITerminalRenderNode replacement
    )
    {
        if (depth == segments.Count)
        {
            return replacement;
        }

        if (node is not Element element)
        {
            throw new InvalidOperationException("A field marker path crossed a non-element node.");
        }

        var childIndex = segments[depth];
        if (childIndex < 0 || childIndex >= element.Children.Count)
        {
            throw new InvalidOperationException("A field marker path is outside the render tree.");
        }

        var children = element.Children.ToArray();
        children[childIndex] = ReplaceNode(children[childIndex], segments, depth + 1, replacement);
        return new Element(element.Tag, element.Attributes, [.. children]);
    }

    private static Element ReplaceAttribute(Element element, string name, string? value)
    {
        var attributes = element
            .Attributes.Where(kv => kv.Key != name)
            .Append(new KeyValuePair<string, string?>(name, value));
        return new Element(element.Tag, new ElementAttributes(attributes), element.Children);
    }

    private static Element SetBooleanAttribute(Element element, string name, bool present)
    {
        var attributes = element.Attributes.Where(kv => kv.Key != name);
        if (present)
        {
            attributes = attributes.Append(new KeyValuePair<string, string?>(name, null));
        }

        return new Element(element.Tag, new ElementAttributes(attributes), element.Children);
    }

    private static void Walk(
        ITerminalRenderNode node,
        string idValue,
        List<int> path,
        List<DisplayPath> results,
        ref FieldKind? kind
    )
    {
        if (node is not Element element)
        {
            return;
        }

        if (
            element.Attributes.TryGetValue(AttributeName, out var value)
            && string.Equals(value, idValue, StringComparison.Ordinal)
        )
        {
            results.Add(new DisplayPath(path));
            if (
                kind is null
                && element.Attributes.TryGetValue(KindAttributeName, out var kindRaw)
                && kindRaw is not null
                && FieldKindExtensions.TryParseAttributeValue(kindRaw, out var parsedKind)
            )
            {
                kind = parsedKind;
            }

            return;
        }

        for (var i = 0; i < element.Children.Count; i++)
        {
            path.Add(i);
            Walk(element.Children[i], idValue, path, results, ref kind);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void CollectIdsCore(ITerminalRenderNode node, ISet<Guid> into)
    {
        if (node is not Element element)
        {
            return;
        }

        if (
            element.Attributes.TryGetValue(AttributeName, out var value)
            && Guid.TryParse(value, out var id)
        )
        {
            into.Add(id);
            return;
        }

        foreach (var child in element.Children)
        {
            CollectIdsCore(child, into);
        }
    }

    private static ITerminalRenderNode Rewrite(
        ITerminalRenderNode node,
        IReadOnlyList<int> segments,
        int index,
        Func<Element, Element> transform
    )
    {
        if (node is not Element element)
        {
            throw new InvalidOperationException(
                "Field marker path does not resolve to an element."
            );
        }

        if (index == segments.Count)
        {
            return transform(element);
        }

        var target = segments[index];
        if (target >= element.Children.Count)
        {
            throw new InvalidOperationException("Field marker path is out of range.");
        }

        var rebuilt = new ITerminalRenderNode[element.Children.Count];
        for (var i = 0; i < element.Children.Count; i++)
        {
            rebuilt[i] =
                i == target
                    ? Rewrite(element.Children[i], segments, index + 1, transform)
                    : element.Children[i];
        }

        return element.WithChildren([.. rebuilt]);
    }
}
