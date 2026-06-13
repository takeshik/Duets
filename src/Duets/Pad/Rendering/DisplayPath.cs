namespace Duets.Pad.Rendering;

internal sealed class DisplayPath : IEquatable<DisplayPath>
{
    public static DisplayPath Root { get; } = new([]);

    private readonly int[] segments;

    public DisplayPath(IEnumerable<int> segments)
    {
        if (segments is null)
        {
            throw new ArgumentNullException(nameof(segments));
        }

        this.segments = [.. segments];
        if (this.segments.Any(segment => segment < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(segments),
                "Display path segments must be non-negative."
            );
        }
    }

    public IReadOnlyList<int> Segments => this.segments;

    public DisplayPath Prepend(int segment)
    {
        if (segment < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segment));
        }

        var next = new int[this.segments.Length + 1];
        next[0] = segment;
        Array.Copy(this.segments, 0, next, 1, this.segments.Length);
        return new DisplayPath(next);
    }

    public DisplayPath Prepend(IEnumerable<int> prefix)
    {
        if (prefix is null)
        {
            throw new ArgumentNullException(nameof(prefix));
        }

        var prefixArray = prefix.ToArray();
        if (prefixArray.Any(segment => segment < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(prefix),
                "Display path segments must be non-negative."
            );
        }

        var next = new int[prefixArray.Length + this.segments.Length];
        Array.Copy(prefixArray, 0, next, 0, prefixArray.Length);
        Array.Copy(this.segments, 0, next, prefixArray.Length, this.segments.Length);
        return new DisplayPath(next);
    }

    public bool Equals(DisplayPath? other) =>
        ReferenceEquals(this, other)
        || (other is not null && this.segments.SequenceEqual(other.segments));

    public override bool Equals(object? obj) => obj is DisplayPath other && this.Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in this.segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }
}
