namespace Duets.Pad.Rendering;

/// <summary>
/// Configures the depth and item limits applied during a single <c>dump</c> render pass.
/// </summary>
/// <remarks>
/// <para>
/// An instance may be supplied per-call to <c>dump(value, opts?)</c> and is merged over the
/// session default. Other render entry points (canvas, ui) use the session default.
/// </para>
/// <para>
/// <see cref="MaxDepth"/> is the number of nesting levels rendered before the dispatch substitutes
/// a truncation marker; the root value is level <c>0</c>. <see cref="MaxItems"/> bounds the number
/// of collection items materialized per collection node.
/// </para>
/// </remarks>
public sealed record DumpOptions
{
    /// <summary>Gets the shared default instance with all fields at their default values.</summary>
    public static DumpOptions Default { get; } = new();

    /// <summary>
    /// Number of nesting levels rendered before the render dispatch substitutes a truncation
    /// marker, with the root value at level <c>0</c>. Defaults to <c>5</c> (levels 0–4 render,
    /// level 5 is truncated). Must be non-negative; because the dispatch truncates at
    /// <c>Depth &gt;= MaxDepth</c>, <c>0</c> truncates the root value itself, and <c>1</c> renders
    /// only the root value with its children truncated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when set to a negative value.
    /// </exception>
    public int MaxDepth
    {
        get;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(this.MaxDepth),
                    value,
                    "MaxDepth must be non-negative."
                );
            }

            field = value;
        }
    } = 5;

    /// <summary>
    /// Maximum number of collection items materialized per collection node.
    /// Defaults to <c>1000</c>. Must be non-negative; <c>0</c> shows no items.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when set to a negative value.
    /// </exception>
    public int MaxItems
    {
        get;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(this.MaxItems),
                    value,
                    "MaxItems must be non-negative."
                );
            }

            field = value;
        }
    } = 1000;
}
