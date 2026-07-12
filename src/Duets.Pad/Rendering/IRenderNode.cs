namespace Duets.Pad.Rendering;

/// <summary>
/// A node in the DuetsPad rendering model.
/// </summary>
public interface IRenderNode
{
    public bool CanReduce { get; }

    public IRenderNode Reduce();
}
