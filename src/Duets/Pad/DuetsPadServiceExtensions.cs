using HttpHarker;

namespace Duets.Pad;

/// <summary>
/// Extension methods for attaching <see cref="DuetsPadService"/> to an <see cref="HttpServer"/>.
/// </summary>
public static class DuetsPadServiceExtensions
{
    /// <summary>
    /// Attaches <see cref="DuetsPadService"/> to <paramref name="server"/> at the given
    /// <paramref name="root"/> path and returns the service instance.
    /// </summary>
    /// <param name="server">The HTTP server to attach to.</param>
    /// <param name="root">The URL root under which all DuetsPad routes are registered.</param>
    /// <param name="configure">Optional delegate to configure <see cref="DuetsPadServiceOptions"/>.</param>
    /// <returns>The newly created <see cref="DuetsPadService"/>.</returns>
    public static DuetsPadService UseDuetsPad(
        this HttpServer server,
        string root = "/",
        Action<DuetsPadServiceOptions>? configure = null
    )
    {
        if (server is null)
        {
            throw new ArgumentNullException(nameof(server));
        }

        var options = new DuetsPadServiceOptions();
        configure?.Invoke(options);
        options.Validate();

        return new DuetsPadService(server, root, options);
    }
}
