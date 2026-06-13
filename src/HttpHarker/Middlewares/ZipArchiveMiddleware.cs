using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Serves files from a zip archive as static HTTP responses via <see cref="ZipFileProvider"/>.
/// </summary>
public sealed class ZipArchiveMiddleware(
    Stream zipStream,
    string root = "/",
    StaticFileOptions? options = null
) : IMiddleware
{
    private readonly StaticFileMiddleware _inner = new(
        new ZipFileProvider(zipStream),
        root,
        options
    );

    /// <inheritdoc cref="StaticFileMiddleware.InvokeAsync"/>
    public Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        return this._inner.InvokeAsync(context, next);
    }
}
