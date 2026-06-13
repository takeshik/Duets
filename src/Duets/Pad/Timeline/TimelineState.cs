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

    public TimelineState Append(string reason, ITerminalRenderNode body, DateTimeOffset timestamp)
    {
        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        var entry = new TimelineEntry(this.NextId, reason, body, timestamp);
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

    public TimelineState Trim(long removeBeforeId)
    {
        var firstRetain = Array.FindIndex(this.entries, e => e.Id >= removeBeforeId);
        if (firstRetain == 0)
        {
            // Boundary is at or below the lowest id: nothing to remove.
            return this;
        }

        if (firstRetain < 0)
        {
            // No entry qualifies to be retained: remove all entries but preserve NextId.
            return new TimelineState([], this.NextId);
        }

        var trimmed = new TimelineEntry[this.entries.Length - firstRetain];
        Array.Copy(this.entries, firstRetain, trimmed, 0, trimmed.Length);
        return new TimelineState(trimmed, this.NextId);
    }

    /// <summary>
    /// Trims the timeline to at most <paramref name="max"/> entries (from the end) and returns
    /// the trimmed state, the boundary id (first retained entry id), and the ids of the removed
    /// entries.
    /// </summary>
    /// <param name="max">Maximum number of entries to retain. Must be positive.</param>
    /// <returns>
    /// The trimmed <see cref="TimelineState"/>, the id boundary used (first retained entry id),
    /// and the ids of entries that were removed. When no trimming is necessary (count is already
    /// within the limit), returns <c>this</c> with a zero boundary and an empty removed-ids list.
    /// </returns>
    internal (TimelineState Next, long RemoveBeforeId, IReadOnlyList<long> RemovedIds) TrimToLimit(
        int max
    )
    {
        if (this.entries.Length <= max)
        {
            return (this, 0L, []);
        }

        // The boundary is the id of the entry that becomes the new first retained entry.
        var firstRetainIndex = this.entries.Length - max;
        var removeBeforeId = this.entries[firstRetainIndex].Id;

        var removedIds = new long[firstRetainIndex];
        for (var i = 0; i < firstRetainIndex; i++)
        {
            removedIds[i] = this.entries[i].Id;
        }

        var trimmed = new TimelineEntry[max];
        Array.Copy(this.entries, firstRetainIndex, trimmed, 0, max);
        return (new TimelineState(trimmed, this.NextId), removeBeforeId, removedIds);
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
