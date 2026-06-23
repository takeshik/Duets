using Duets.Pad.Protocol;
using Duets.Pad.Rendering;

namespace Duets.Pad.State;

/// <summary>
/// Computes positional Canvas patch operations between two projected Canvas states.
/// </summary>
internal sealed class CanvasDiffer
{
    public IReadOnlyList<CanvasPatchOperation> Diff(CanvasState oldState, CanvasState newState)
    {
        if (oldState is null)
        {
            throw new ArgumentNullException(nameof(oldState));
        }

        if (newState is null)
        {
            throw new ArgumentNullException(nameof(newState));
        }

        var operations = new List<CanvasPatchOperation>();
        DiffElement(
            oldState.Root,
            newState.Root,
            DisplayPath.Root,
            operations,
            allowReplace: false
        );
        return Canonicalize(operations);
    }

    private static void DiffNode(
        ITerminalRenderNode oldNode,
        ITerminalRenderNode newNode,
        DisplayPath path,
        List<CanvasPatchOperation> operations
    )
    {
        if (ReferenceEquals(oldNode, newNode) || Equals(oldNode, newNode))
        {
            return;
        }

        switch (oldNode, newNode)
        {
            case (Element oldElement, Element newElement):
                DiffElement(oldElement, newElement, path, operations, allowReplace: true);
                break;
            case (Text, Text newText):
                operations.Add(new ReplaceTextOperation(path, newText.Value));
                break;
            case (RawHtml, RawHtml):
                operations.Add(new ReplaceNodeOperation(path, newNode));
                break;
            default:
                operations.Add(new ReplaceNodeOperation(path, newNode));
                break;
        }
    }

    private static void DiffElement(
        Element oldElement,
        Element newElement,
        DisplayPath path,
        List<CanvasPatchOperation> operations,
        bool allowReplace
    )
    {
        if (oldElement.Tag != newElement.Tag)
        {
            if (!allowReplace)
            {
                throw new InvalidOperationException("Canvas root tag cannot be replaced.");
            }

            operations.Add(new ReplaceNodeOperation(path, newElement));
            return;
        }

        DiffAttributes(oldElement, newElement, path, operations);
        DiffChildren(oldElement, newElement, path, operations);
    }

    private static void DiffAttributes(
        Element oldElement,
        Element newElement,
        DisplayPath path,
        List<CanvasPatchOperation> operations
    )
    {
        foreach (var oldAttribute in oldElement.Attributes)
        {
            if (!newElement.Attributes.ContainsKey(oldAttribute.Key))
            {
                operations.Add(new RemoveAttributeOperation(path, oldAttribute.Key));
            }
        }

        foreach (var newAttribute in newElement.Attributes)
        {
            if (
                !oldElement.Attributes.TryGetValue(newAttribute.Key, out var oldValue)
                || !StringComparer.Ordinal.Equals(oldValue, newAttribute.Value)
            )
            {
                operations.Add(
                    new SetAttributeOperation(path, newAttribute.Key, newAttribute.Value)
                );
            }
        }
    }

    private static void DiffChildren(
        Element oldElement,
        Element newElement,
        DisplayPath path,
        List<CanvasPatchOperation> operations
    )
    {
        var sharedCount = Math.Min(oldElement.Children.Count, newElement.Children.Count);
        for (var i = 0; i < sharedCount; i++)
        {
            DiffNode(oldElement.Children[i], newElement.Children[i], path.Append(i), operations);
        }

        for (var i = oldElement.Children.Count - 1; i >= newElement.Children.Count; i--)
        {
            operations.Add(new RemoveChildOperation(path, i));
        }

        for (var i = oldElement.Children.Count; i < newElement.Children.Count; i++)
        {
            operations.Add(new InsertChildOperation(path, i, newElement.Children[i]));
        }
    }

    private static IReadOnlyList<CanvasPatchOperation> Canonicalize(
        IReadOnlyList<CanvasPatchOperation> operations
    )
    {
        var replaceNode = operations
            .OfType<ReplaceNodeOperation>()
            .OrderByDescending(o => o.Path.Segments.Count)
            .ThenBy(o => PathKey(o.Path), StringComparer.Ordinal);
        var scalar = operations
            .Where(o =>
                o is SetAttributeOperation or RemoveAttributeOperation or ReplaceTextOperation
            )
            .OrderBy(OperationPathKey, StringComparer.Ordinal)
            .ThenBy(OperationKindOrder);
        var removeChild = operations
            .OfType<RemoveChildOperation>()
            .OrderBy(o => PathKey(o.ParentPath), StringComparer.Ordinal)
            .ThenByDescending(o => o.Index);
        var insertChild = operations
            .OfType<InsertChildOperation>()
            .OrderBy(o => PathKey(o.ParentPath), StringComparer.Ordinal)
            .ThenBy(o => o.Index);

        return [.. replaceNode, .. scalar, .. removeChild, .. insertChild];
    }

    private static string OperationPathKey(CanvasPatchOperation operation) =>
        operation switch
        {
            SetAttributeOperation op => PathKey(op.Path),
            RemoveAttributeOperation op => PathKey(op.Path),
            ReplaceTextOperation op => PathKey(op.Path),
            _ => "",
        };

    private static int OperationKindOrder(CanvasPatchOperation operation) =>
        operation switch
        {
            SetAttributeOperation => 0,
            RemoveAttributeOperation => 1,
            ReplaceTextOperation => 2,
            _ => 3,
        };

    private static string PathKey(DisplayPath path) =>
        string.Join("/", path.Segments.Select(segment => segment.ToString("D10")));
}
