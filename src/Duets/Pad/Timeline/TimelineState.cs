using System.Collections;
using Duets.Pad.Rendering;

namespace Duets.Pad.Timeline;

/// <summary>
/// Server-side Timeline state for structured history output.
/// </summary>
public sealed class TimelineState : IReadOnlyList<TimelineEntry>, IEquatable<TimelineState>
{
    public static TimelineState Empty { get; } = new([], nextId: 0);

    private readonly TimelineEntry[] entries;

    private TimelineState(IEnumerable<TimelineEntry> entries, long nextId)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        this.entries = [.. entries];
        if (this.entries.Any(entry => entry is null))
        {
            throw new ArgumentException("Timeline entries cannot contain null.", nameof(entries));
        }

        this.NextId = nextId >= 0 ? nextId : throw new ArgumentOutOfRangeException(nameof(nextId));
    }

    public int Count => this.entries.Length;

    public long NextId { get; }

    public TimelineEntry this[int index] => this.entries[index];

    public TimelineState Append(string reason, ITerminalRenderNode body)
    {
        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        var entry = new TimelineEntry(this.NextId, reason, body);
        var next = new TimelineEntry[this.entries.Length + 1];
        Array.Copy(this.entries, next, this.entries.Length);
        next[^1] = entry;
        return new TimelineState(next, this.NextId + 1);
    }

    public TimelineState Replace(TimelineEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var index = Array.FindIndex(this.entries, existing => existing.Id == entry.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Timeline entry '{entry.Id}' was not found.");
        }

        var next = (TimelineEntry[])this.entries.Clone();
        next[index] = entry;
        return new TimelineState(next, Math.Max(this.NextId, entry.Id + 1));
    }

    public TimelineState Clear() => Empty;

    public bool Equals(TimelineState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (
            other is null
            || this.NextId != other.NextId
            || this.entries.Length != other.entries.Length
        )
        {
            return false;
        }

        for (var i = 0; i < this.entries.Length; i++)
        {
            if (this.entries[i] != other.entries[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is TimelineState other && this.Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.NextId);
        foreach (var entry in this.entries)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<TimelineEntry> GetEnumerator() =>
        ((IEnumerable<TimelineEntry>)this.entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
