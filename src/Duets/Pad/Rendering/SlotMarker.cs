namespace Duets.Pad.Rendering;

/// <summary>
/// Helpers for the locatable marker element wrapping a <see cref="DisplaySlot"/>'s content in a
/// projected render tree. The marker is a layout-transparent <c>&lt;div&gt;</c> carrying
/// <see cref="AttributeName"/> set to the slot id; its single child is the rendered content.
/// </summary>
internal static class SlotMarker
{
    /// <summary>The data attribute that identifies a slot marker element by slot id.</summary>
    public const string AttributeName = "data-duetspad-slot";

    /// <summary>
    /// Wraps <paramref name="child"/> in a marker element tagged with <paramref name="id"/>.
    /// </summary>
    public static Element Wrap(Guid id, ITerminalRenderNode child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        var attributes = new ElementAttributes(
            new KeyValuePair<string, string?>(AttributeName, id.ToString("D")),
            // display:contents keeps the wrapper out of layout while preserving childNodes indexing,
            // so server-side display paths stay aligned with the browser DOM.
            new KeyValuePair<string, string?>("style", "display:contents")
        );
        return new Element("div", attributes, new ElementChildren(child));
    }

    /// <summary>
    /// Returns the display paths (relative to <paramref name="root"/>) of every marker element for
    /// the slot identified by <paramref name="id"/>. A matched marker is not descended into.
    /// </summary>
    public static IReadOnlyList<DisplayPath> Find(ITerminalRenderNode root, Guid id)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var idValue = id.ToString("D");
        var results = new List<DisplayPath>();
        var path = new List<int>();
        Walk(root, idValue, path, results);
        return results;
    }

    /// <summary>
    /// Rebuilds the tree rooted at <paramref name="root"/>, replacing the single child of the marker
    /// element at <paramref name="markerPath"/> with <paramref name="newChild"/>.
    /// </summary>
    public static ITerminalRenderNode ReplaceContent(
        ITerminalRenderNode root,
        DisplayPath markerPath,
        ITerminalRenderNode newChild
    )
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (newChild is null)
        {
            throw new ArgumentNullException(nameof(newChild));
        }

        return Rewrite(root, markerPath.Segments, 0, newChild);
    }

    private static void Walk(
        ITerminalRenderNode node,
        string idValue,
        List<int> path,
        List<DisplayPath> results
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
            return;
        }

        for (var i = 0; i < element.Children.Count; i++)
        {
            path.Add(i);
            Walk(element.Children[i], idValue, path, results);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static ITerminalRenderNode Rewrite(
        ITerminalRenderNode node,
        IReadOnlyList<int> segments,
        int index,
        ITerminalRenderNode newChild
    )
    {
        if (node is not Element element)
        {
            throw new InvalidOperationException("Slot marker path does not resolve to an element.");
        }

        if (index == segments.Count)
        {
            return element.WithChildren(new ElementChildren(newChild));
        }

        var target = segments[index];
        if (target >= element.Children.Count)
        {
            throw new InvalidOperationException("Slot marker path is out of range.");
        }

        var rebuilt = new ITerminalRenderNode[element.Children.Count];
        for (var i = 0; i < element.Children.Count; i++)
        {
            rebuilt[i] =
                i == target
                    ? Rewrite(element.Children[i], segments, index + 1, newChild)
                    : element.Children[i];
        }

        return element.WithChildren([.. rebuilt]);
    }
}
