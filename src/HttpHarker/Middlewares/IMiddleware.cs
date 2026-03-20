using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Middleware component in the <see cref="HttpHarker.HttpServer"/> pipeline.
/// </summary>
public interface IMiddleware
{
    Task InvokeAsync(HttpListenerContext context, Func<Task> next);
}
