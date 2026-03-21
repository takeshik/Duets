using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Serves files from a zip archive as static HTTP responses via <see cref="ZipFileProvider"/>.
/// </summary>
public sealed class ZipArchiveMiddleware : IMiddleware
{
    public ZipArchiveMiddleware(Stream zipStream, string root = "/", EmbeddedResourceOptions? options = null)
    {
        this._inner = new StaticFileMiddleware(new ZipFileProvider(zipStream), root, options);
    }

    private readonly StaticFileMiddleware _inner;

    public Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        return this._inner.InvokeAsync(context, next);
    }
}
