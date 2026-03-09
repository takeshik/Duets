using System.Net;

namespace HttpHarker.Middlewares;

public interface IMiddleware
{
    Task InvokeAsync(HttpListenerContext context, Func<Task> next);
}
