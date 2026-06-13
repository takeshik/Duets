using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Middleware component in the <see cref="HttpHarker.HttpServer"/> pipeline.
/// </summary>
public interface IMiddleware
{
    /// <summary>Processes the HTTP request represented by <paramref name="context"/>.</summary>
    /// <param name="context">The current HTTP listener context.</param>
    /// <param name="next">Continuation that invokes the next middleware in the pipeline; call to pass control, omit to short-circuit.</param>
    public Task InvokeAsync(HttpListenerContext context, Func<Task> next);
}
