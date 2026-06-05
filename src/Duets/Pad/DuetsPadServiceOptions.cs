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
    /// Keepalive interval for DuetsPad SSE streams.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
}
