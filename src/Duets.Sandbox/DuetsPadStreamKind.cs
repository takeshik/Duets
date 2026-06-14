namespace Duets.Sandbox;

/// <summary>
/// Represents a typed SSE stream kind for the DuetsPad protocol.
/// Each instance carries its wire token and knows how to build its relative path.
/// </summary>
internal sealed class DuetsPadStreamKind
{
    public static readonly DuetsPadStreamKind Canvas = new(
        "canvas",
        id => $"sessions/{id}/canvas-events"
    );

    public static readonly DuetsPadStreamKind Timeline = new(
        "timeline",
        id => $"sessions/{id}/timeline-events"
    );

    public static readonly DuetsPadStreamKind TypeDeclarations = new(
        "type-declarations",
        id => $"type-declaration-events?sessionId={id}"
    );

    private static readonly Dictionary<string, DuetsPadStreamKind> _byToken = new(StringComparer.Ordinal)
    {
        [Canvas.WireToken] = Canvas,
        [Timeline.WireToken] = Timeline,
        [TypeDeclarations.WireToken] = TypeDeclarations,
    };

    private readonly Func<string, string> _buildRelativePath;

    private DuetsPadStreamKind(string wireToken, Func<string, string> buildRelativePath)
    {
        this.WireToken = wireToken;
        this._buildRelativePath = buildRelativePath;
    }

    /// <summary>The string token used on the wire (e.g. "canvas").</summary>
    public string WireToken { get; }

    /// <summary>All known stream kinds, in declaration order.</summary>
    public static IReadOnlyList<DuetsPadStreamKind> AllKinds => [Canvas, Timeline, TypeDeclarations];

    /// <summary>All known wire tokens, in declaration order.</summary>
    public static IReadOnlyList<string> AllTokens =>
        [Canvas.WireToken, Timeline.WireToken, TypeDeclarations.WireToken];

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
