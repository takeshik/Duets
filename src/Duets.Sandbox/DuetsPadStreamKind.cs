namespace Duets.Sandbox;

/// <summary>
/// Represents the unified SSE stream kind for the DuetsPad protocol.
/// </summary>
internal sealed class DuetsPadStreamKind
{
    public static readonly DuetsPadStreamKind Events = new("events", id => $"sessions/{id}/events");

    private static readonly Dictionary<string, DuetsPadStreamKind> _byToken = new(
        StringComparer.Ordinal
    )
    {
        [Events.WireToken] = Events,
    };

    private readonly Func<string, string> _buildRelativePath;

    private DuetsPadStreamKind(string wireToken, Func<string, string> buildRelativePath)
    {
        this.WireToken = wireToken;
        this._buildRelativePath = buildRelativePath;
    }

    /// <summary>The string token used on the wire (e.g. "events").</summary>
    public string WireToken { get; }

    /// <summary>All known stream kinds, in declaration order.</summary>
    public static IReadOnlyList<DuetsPadStreamKind> AllKinds => [Events];

    /// <summary>All known wire tokens, in declaration order.</summary>
    public static IReadOnlyList<string> AllTokens => [Events.WireToken];

    /// <summary>
    /// Parses a wire token string to its <see cref="DuetsPadStreamKind"/>.
    /// Returns <see langword="false"/> when the token is not recognised.
    /// </summary>
    public static bool TryParse(string? token, out DuetsPadStreamKind result)
    {
        if (token is not null && _byToken.TryGetValue(token, out var kind))
        {
            result = kind;
            return true;
        }

        result = null!;
        return false;
    }

    /// <summary>Builds the relative URL path for this stream kind given a session ID.</summary>
    public string BuildRelativePath(string sessionId) => this._buildRelativePath(sessionId);

    public override string ToString() => this.WireToken;
}
