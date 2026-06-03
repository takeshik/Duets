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
    /// Optional Monaco loader asset source. When omitted, DuetsPad uses the same default CDN-backed source as ReplService.
    /// </summary>
    public IAssetSource? MonacoLoader { get; set; }

    /// <summary>
    /// Optional Tabler CSS asset source. When omitted, DuetsPad uses the default Tabler CDN-backed source.
    /// </summary>
    public IAssetSource? TablerCss { get; set; }

    /// <summary>
    /// Keepalive interval for DuetsPad SSE streams.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
}
