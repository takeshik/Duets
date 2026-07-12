using System.Runtime.CompilerServices;

namespace Duets.Pad.Rendering;

internal sealed class RenderTreeReducer
{
    private const int MaxReductionDepth = 256;

    public ITerminalRenderNode Reduce(IRenderNode node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return Reduce(node, new HashSet<IRenderNode>(ReferenceComparer.Instance), depth: 0);
    }

    private static ITerminalRenderNode Reduce(
        IRenderNode node,
        HashSet<IRenderNode> path,
        int depth
    )
    {
        if (node is ITerminalRenderNode terminal)
        {
            if (terminal.CanReduce)
            {
                throw new InvalidOperationException(
                    $"{node.GetType().FullName} implements ITerminalRenderNode but reports CanReduce."
                );
            }

            return terminal;
        }

        if (!node.CanReduce)
        {
            throw new InvalidOperationException(
                $"{node.GetType().FullName} is not terminal and cannot reduce."
            );
        }

        if (depth >= MaxReductionDepth)
        {
            throw new InvalidOperationException(
                $"Render node reduction exceeded the maximum depth of {MaxReductionDepth}."
            );
        }

        if (!path.Add(node))
        {
            throw new InvalidOperationException(
                $"Render node reduction cycle detected at {node.GetType().FullName}."
            );
        }

        try
        {
            var reduced =
                node.Reduce()
                ?? throw new InvalidOperationException(
                    $"{node.GetType().FullName}.Reduce() returned null."
                );
            if (ReferenceEquals(node, reduced))
            {
                throw new InvalidOperationException(
                    $"{node.GetType().FullName}.Reduce() returned itself while CanReduce is true."
                );
            }

            return Reduce(reduced, path, depth + 1);
        }
        finally
        {
            path.Remove(node);
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<IRenderNode>
    {
        public static ReferenceComparer Instance { get; } = new();

        public bool Equals(IRenderNode? x, IRenderNode? y) => ReferenceEquals(x, y);

        public int GetHashCode(IRenderNode obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
