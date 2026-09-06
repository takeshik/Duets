namespace Duets.Pad.Rendering;

/// <summary>
/// A node in the DuetsPad rendering model.
/// </summary>
public interface IRenderNode
{
    /// <summary>Gets whether this node must be reduced before serialization.</summary>
    public bool CanReduce { get; }

    /// <summary>Reduces this node by one step toward a terminal render node.</summary>
    /// <returns>The reduced node.</returns>
    public IRenderNode Reduce();
}
