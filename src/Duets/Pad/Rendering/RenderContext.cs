using System.Runtime.CompilerServices;

namespace Duets.Pad.Rendering;

/// <summary>
/// Carries the per-recursion state for a single render pass: the caller-configured options,
/// the current nesting depth, and the shared cycle-detection set.
/// </summary>
/// <remarks>
/// <para>
/// Renderers must never recurse directly into the pipeline or into themselves. Instead they call
/// <see cref="RenderChild"/> for every nested value. <see cref="RenderChild"/> re-enters the
/// central dispatch at <c>Depth + 1</c>, reusing the same <see cref="Options"/> and the same
/// visited set, so depth limiting and cycle detection remain consistent across renderer boundaries.
/// </para>
/// <para>
/// The visited set uses reference equality so that value types and strings — which cannot form
/// reference cycles — are never tested against it.
/// </para>
/// </remarks>
public sealed class RenderContext
{
    private readonly HashSet<object> _visited;
    private readonly Func<object?, RenderContext, ITerminalRenderNode> _dispatch;

    internal RenderContext(
        DumpOptions options,
        int depth,
        HashSet<object> visited,
        Func<object?, RenderContext, ITerminalRenderNode> dispatch
    )
    {
        this.Options = options;
        this.Depth = depth;
        this._visited = visited;
        this._dispatch = dispatch;
    }

    /// <summary>Gets the caller-configured options for this render pass.</summary>
    public DumpOptions Options { get; }

    /// <summary>Gets the current nesting depth (0 = root).</summary>
    public int Depth { get; }

    /// <summary>
    /// Renders a nested value at <c>Depth + 1</c> through the central dispatch, reusing the
    /// same cycle-detection set and the same <see cref="Options"/>.
    /// </summary>
    public ITerminalRenderNode RenderChild(object? value)
    {
        var child = new RenderContext(this.Options, this.Depth + 1, this._visited, this._dispatch);
        return this._dispatch(value, child);
    }

    /// <summary>
    /// Attempts to add <paramref name="value"/> to the visited set.
    /// Returns <see langword="true"/> when the value was not already present.
    /// </summary>
    internal bool TryVisit(object value) => this._visited.Add(value);

    /// <summary>Removes <paramref name="value"/> from the visited set.</summary>
    internal void Unvisit(object value) => this._visited.Remove(value);

    /// <summary>
    /// Creates a root <see cref="RenderContext"/> at depth 0 with a fresh visited set.
    /// </summary>
    internal static RenderContext CreateRoot(
        DumpOptions options,
        Func<object?, RenderContext, ITerminalRenderNode> dispatch
    ) =>
        new(
            options,
            depth: 0,
            new HashSet<object>(ReferenceEqualityIdentityComparer.Instance),
            dispatch
        );

    /// <summary>
    /// Reference-equality comparer used for the cycle-detection visited set.
    /// Defined here to remain compatible with targets that do not expose
    /// <c>System.Collections.Generic.ReferenceEqualityComparer</c>.
    /// </summary>
    private sealed class ReferenceEqualityIdentityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityIdentityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
