using System.Net;
using System.Reflection;

namespace HttpHarker.Middlewares;

/// <summary>
/// Serves assembly manifest embedded resources as static files, with optional ETag caching and SPA fallback support.
/// </summary>
public sealed class EmbeddedResourceMiddleware : IMiddleware
{
    public EmbeddedResourceMiddleware(
        Assembly assembly,
        string resourcePrefix,
        string root = "/",
        StaticFileOptions? options = null)
    {
        this._inner = new StaticFileMiddleware(
            new EmbeddedResourceFileProvider(assembly, resourcePrefix),
            root,
            options
        );
    }

    private readonly StaticFileMiddleware _inner;

    public Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        return this._inner.InvokeAsync(context, next);
    }
}
