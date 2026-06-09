using Duets.Pad.Rendering;

namespace Duets.Pad;

/// <summary>
/// Configuration for attaching DuetsPad to an HTTP server.
/// </summary>
public sealed class DuetsPadServiceOptions
{
    /// <summary>
    /// Creates a fresh DuetsSession for each DuetsPad browser session.
    /// </summary>
    public Func<Task<DuetsSession>> SessionFactory { get; set; } = () => DuetsSession.CreateAsync();

    /// <summary>
    /// Optional Monaco loader asset source. When omitted, DuetsPad fetches the Monaco loader from its default CDN-backed source.
    /// </summary>
    public IAssetSource? MonacoLoader { get; set; }

    /// <summary>
    /// Base URL of the Monaco Editor <c>min/vs</c> directory, injected into the browser as
    /// <c>window.DUETSPAD_MONACO_VS</c> via <c>/duetspad-config.js</c>. The default points to
    /// <c>monaco-editor@0.55.1</c> on unpkg, matching the version served by the default
    /// <see cref="MonacoLoader"/> source. Embedders can override this for offline or
    /// custom-hosted Monaco installations.
    /// </summary>
    public string MonacoBaseUrl { get; set; } = "https://unpkg.com/monaco-editor@0.55.1/min/vs";

    /// <summary>
    /// Optional Tabler CSS asset source. When omitted, DuetsPad uses the default Tabler CDN-backed source.
    /// </summary>
    public IAssetSource? TablerCss { get; set; }

    /// <summary>
    /// Optional Tabler Icons CSS asset source (<c>tabler-icons.min.css</c> from
    /// <c>@tabler/icons-webfont</c>). When omitted, DuetsPad fetches the stylesheet from unpkg
    /// and caches it on disk.
    /// </summary>
    public IAssetSource? TablerIconsCss { get; set; }

    /// <summary>
    /// Optional Tabler Icons woff2 font asset source (<c>tabler-icons.woff2</c> from
    /// <c>@tabler/icons-webfont</c>). When omitted, DuetsPad fetches the font from unpkg
    /// and caches it on disk.
    /// </summary>
    public IAssetSource? TablerIconsFont { get; set; }

    /// <summary>
    /// Maximum number of Timeline entries retained per session.
    /// <see langword="null"/> (the default) means unlimited. When set to a positive value,
    /// entries older than the limit are dropped after each append and a <c>timeline.trim</c>
    /// event is emitted to all live subscribers so the browser converges to the same
    /// bounded state. A non-null value must be positive.
    /// </summary>
    public int? TimelineEntryLimit { get; set; } = null;

    /// <summary>
    /// Default <see cref="DumpOptions" /> applied to every DuetsPad browser session.
    /// Individual <c>dump(value, opts?)</c> calls may supply a per-call override merged over
    /// this value. Defaults to <see cref="DumpOptions.Default" />.
    /// </summary>
    public DumpOptions DumpOptions { get; set; } = DumpOptions.Default;

    /// <summary>
    /// Object renderers applied to every DuetsPad browser session, consulted in last-wins order
    /// (a later renderer overrides an earlier one that can render the same value) before the
    /// built-in default renderer. Empty by default, preserving default rendering behavior.
    /// </summary>
    public IReadOnlyList<IObjectRenderer> ObjectRenderers { get; set; } = [];

    /// <summary>
    /// Keepalive interval for DuetsPad SSE streams.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a session may be idle before it is automatically reclaimed.
    /// When <see langword="null"/> or non-positive, idle cleanup is disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A session that has at least one active SSE subscriber (Canvas, Timeline, or type-declaration
    /// stream) is never evicted regardless of its last-activity timestamp; the subscriber-presence
    /// guard takes precedence. Only sessions with no live stream and no activity within this
    /// threshold are reclaimed. <see cref="KeepAliveInterval"/> keepalive pings also count as
    /// activity and provide a redundant secondary signal for sessions whose streams are open.
    /// </para>
    /// <para>A non-positive or <see langword="null"/> value disables automatic cleanup entirely.</para>
    /// </remarks>
    public TimeSpan? IdleTimeout { get; set; } = null;

    /// <summary>
    /// Testable clock used by the idle-timeout sweep. Defaults to
    /// <see cref="DateTimeOffset.UtcNow"/>. Exposed for testing only; do not set in production code.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Period of the background idle-sweep timer. Only active when
    /// <see cref="IdleTimeout"/> is enabled. Defaults to 30 seconds.
    /// </summary>
    internal TimeSpan CleanupInterval { get; set; } = TimeSpan.FromSeconds(30);
}
