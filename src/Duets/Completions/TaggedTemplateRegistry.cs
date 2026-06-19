namespace Duets.Completions;

/// <summary>Thread-safe registry of host-provided tagged-template completion callbacks.</summary>
public sealed class TaggedTemplateRegistry
{
    /// <summary>Default maximum number of completion items returned to a client.</summary>
    public const int DefaultMaxItems = 1000;

    private readonly object _sync = new();
    private readonly Dictionary<string, Registration> _registrations = new(StringComparer.Ordinal);
    private int _version;

    /// <summary>Fires after the registered tag set changes.</summary>
    public event Action<TaggedTemplateRegistrySnapshot>? Changed;

    /// <summary>Registers or replaces the completion callback for <paramref name="tag"/>.</summary>
    public void Register(
        string tag,
        TemplateCompletionCallback complete,
        TemplateCompletionKind defaultKind = TemplateCompletionKind.Value
    )
    {
        ValidateTag(tag);
        if (complete is null)
        {
            throw new ArgumentNullException(nameof(complete));
        }

        TaggedTemplateRegistrySnapshot snapshot;
        lock (this._sync)
        {
            this._registrations[tag] = new Registration(tag, defaultKind, complete);
            snapshot = this.CreateSnapshotCore();
        }

        this.Changed?.Invoke(snapshot);
    }

    /// <summary>Removes the completion callback registered for <paramref name="tag"/>.</summary>
    public void Unregister(string tag)
    {
        ValidateTag(tag);

        TaggedTemplateRegistrySnapshot? snapshot = null;
        lock (this._sync)
        {
            if (this._registrations.Remove(tag))
            {
                snapshot = this.CreateSnapshotCore();
            }
        }

        if (snapshot is not null)
        {
            this.Changed?.Invoke(snapshot);
        }
    }

    /// <summary>Returns the callback registration for <paramref name="tag"/>, if present.</summary>
    public bool TryGet(string tag, out TaggedTemplateCompletionRegistration registration)
    {
        lock (this._sync)
        {
            if (this._registrations.TryGetValue(tag, out var stored))
            {
                registration = stored.ToPublic();
                return true;
            }
        }

        registration = default;
        return false;
    }

    /// <summary>Returns a versioned snapshot of registered completion tag names.</summary>
    public TaggedTemplateRegistrySnapshot GetSnapshot()
    {
        lock (this._sync)
        {
            return new TaggedTemplateRegistrySnapshot(
                this._version,
                [.. this._registrations.Keys.OrderBy(tag => tag, StringComparer.Ordinal)]
            );
        }
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="tag"/> is a supported tag name.</summary>
    public static bool IsValidTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return false;
        }

        if (!IsTagStart(tag[0]))
        {
            return false;
        }

        for (var i = 1; i < tag.Length; i++)
        {
            if (!IsTagPart(tag[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws when <paramref name="tag"/> is not a supported simple identifier.</summary>
    public static void ValidateTag(string tag)
    {
        if (!IsValidTag(tag))
        {
            throw new ArgumentException(
                "Tagged-template tags must be non-empty ASCII identifiers using letters, digits, and underscore, and must not start with a digit.",
                nameof(tag)
            );
        }
    }

    /// <summary>Validates a completion item independent of segment length.</summary>
    public static bool Validate(TemplateCompletionItem item)
    {
        if (item is null || string.IsNullOrEmpty(item.Label))
        {
            return false;
        }

        if (item.ReplacementSpan is { } span && (span.Start < 0 || span.Length < 0))
        {
            return false;
        }

        return true;
    }

    /// <summary>Returns whether <paramref name="span"/> lies inside a segment of <paramref name="segmentLength"/> UTF-16 code units.</summary>
    public static bool IsSpanWithinSegment(TextSpan span, int segmentLength)
    {
        if (segmentLength < 0 || span.Start < 0 || span.Length < 0)
        {
            return false;
        }

        var end = (long)span.Start + span.Length;
        return end <= segmentLength;
    }

    /// <summary>Caps completion results to the configured maximum.</summary>
    public static IReadOnlyList<TemplateCompletionItem> Cap(
        IEnumerable<TemplateCompletionItem> items,
        int max = DefaultMaxItems
    )
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (max < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max));
        }

        return [.. items.Take(max)];
    }

    private static bool IsTagStart(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static bool IsTagPart(char c) => IsTagStart(c) || c is >= '0' and <= '9';

    private TaggedTemplateRegistrySnapshot CreateSnapshotCore()
    {
        this._version++;
        return new TaggedTemplateRegistrySnapshot(
            this._version,
            [.. this._registrations.Keys.OrderBy(tag => tag, StringComparer.Ordinal)]
        );
    }

    private sealed record Registration(
        string Tag,
        TemplateCompletionKind DefaultKind,
        TemplateCompletionCallback Complete
    )
    {
        public TaggedTemplateCompletionRegistration ToPublic() =>
            new(this.Tag, this.DefaultKind, this.Complete);
    }
}

/// <summary>A completion callback registration.</summary>
public readonly record struct TaggedTemplateCompletionRegistration(
    string Tag,
    TemplateCompletionKind DefaultKind,
    TemplateCompletionCallback Complete
);

/// <summary>A versioned snapshot of registered tagged-template completion tag names.</summary>
public sealed record TaggedTemplateRegistrySnapshot(int Version, IReadOnlyList<string> Tags);
