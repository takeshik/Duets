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
    /// Evaluated on every session-API request (everything under <c>/sessions</c>) to decide whether
    /// the request is authenticated; for the SSE events endpoint it is evaluated once, at connection
    /// establishment. Static UI assets (the pad page, scripts, styles, fonts) are never authenticated.
    /// <see langword="null"/> (the default) means no authentication is performed, which assumes
    /// loopback-only exposure (ADR-49). For fixed-token validation, see
    /// <see cref="DuetsPadAuthenticator.Token(string)"/>.
    /// </summary>
    public Func<DuetsPadAuthenticationContext, ValueTask<bool>>? Authenticate { get; set; }

    /// <summary>
    /// Maximum number of concurrent DuetsPad sessions. <see langword="null"/> means unlimited.
    /// When the cap is reached, <c>POST /sessions</c> attempts to create a new session (as opposed
    /// to reconnecting to an existing one) fail with <c>429</c>.
    /// </summary>
    public int? MaxSessions { get; set; } = 16;

    /// <summary>
    /// Maximum accepted request body size, in bytes, applied to all <c>POST</c> endpoints. Bodies
    /// larger than this are rejected with <c>413</c>. <c>/complete</c> additionally enforces its own
    /// stricter <see cref="TaggedTemplateCompletionMaxRequestBytes"/> cap on top of this one.
    /// </summary>
    public int MaxRequestBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Enables tagged-template completion registration snapshots and the
    /// <c>/sessions/{id}/complete</c> endpoint.
    /// </summary>
    public bool EnableTaggedTemplateCompletions { get; set; } = true;

    /// <summary>Maximum accepted tagged-template completion request body size in bytes.</summary>
    public int TaggedTemplateCompletionMaxRequestBytes { get; set; } = 64 * 1024;

    /// <summary>Maximum accepted tagged-template completion text field length in UTF-16 code units.</summary>
    public int TaggedTemplateCompletionMaxFieldLength { get; set; } = 16 * 1024;

    /// <summary>Maximum tagged-template completion requests per session per one-second window.</summary>
    public int TaggedTemplateCompletionRateLimitPerSecond { get; set; } = 30;

    /// <summary>Maximum time allowed for one tagged-template completion callback.</summary>
    public TimeSpan TaggedTemplateCompletionTimeout { get; set; } = TimeSpan.FromSeconds(2);

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
    /// <para>
    /// The default changed from <see langword="null"/> to 30 minutes per ADR-49, so abandoned
    /// sessions are reclaimed instead of accumulating indefinitely; <see langword="null"/> still
    /// disables cleanup for hosts that rely on eternal sessions.
    /// </para>
    /// </remarks>
    public TimeSpan? IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Testable clock used by the idle-timeout sweep. Defaults to
    /// <see cref="DateTimeOffset.UtcNow"/>. Exposed for testing only; do not set in production code.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Period of the background idle-sweep timer. Only active when
    /// <see cref="IdleTimeout"/> is enabled. Defaults to 30 seconds.
    /// This is a test-only internal hook (like <see cref="Clock"/>) that allows tests to prevent the
    /// background timer from firing during the test run; do not set in production code.
    /// </summary>
    internal TimeSpan CleanupInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Validates all configuration values. Called by
    /// <see cref="DuetsPadServiceExtensions.UseDuetsPad"/> immediately after the configure
    /// delegate is applied so that configuration errors are caught at setup time rather than
    /// deferred to first use.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="TimelineEntryLimit"/>, <see cref="MaxSessions"/>, or
    /// <see cref="MaxRequestBodyBytes"/> is set to a non-positive value.
    /// </exception>
    internal void Validate()
    {
        if (this.TimelineEntryLimit is { } limit && limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(this.TimelineEntryLimit),
                "Timeline entry limit must be positive."
            );
        }

        if (this.MaxSessions is { } maxSessions && maxSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(this.MaxSessions),
                "Max sessions must be positive."
            );
        }

        if (this.MaxRequestBodyBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(this.MaxRequestBodyBytes),
                "Max request body bytes must be positive."
            );
        }

        if (this.TaggedTemplateCompletionMaxRequestBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(this.TaggedTemplateCompletionMaxRequestBytes),
                "Tagged-template completion request byte limit must be positive."
            );
        }

        if (this.TaggedTemplateCompletionMaxFieldLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(this.TaggedTemplateCompletionMaxFieldLength),
                "Tagged-template completion field length limit must be positive."
            );
        }

        if (this.TaggedTemplateCompletionRateLimitPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(this.TaggedTemplateCompletionRateLimitPerSecond),
                "Tagged-template completion rate limit must be positive."
            );
        }

        if (this.TaggedTemplateCompletionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(this.TaggedTemplateCompletionTimeout),
                "Tagged-template completion timeout must be positive."
            );
        }
    }
}
