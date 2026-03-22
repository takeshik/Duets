using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Middleware that intercepts responses with configured status codes and delegates rendering to registered error-page handlers.
/// </summary>
/// <remarks>
/// <para>
/// This middleware must be registered <b>before</b> any terminal middleware (such as
/// <see cref="SimpleRoutingMiddleware"/>) in the pipeline. It works by calling <c>next()</c>
/// and inspecting the response status code after the rest of the pipeline has completed.
/// If it is placed after a terminal middleware, it is unreachable for requests handled by that
/// middleware — analogous to placing <c>UseStatusCodePages</c> after <c>UseEndpoints</c> in ASP.NET Core.
/// </para>
/// <para>
/// A status code of 200 (the default unset value) is treated as 404, so unmatched requests
/// are handled without upstream middleware having to set the code explicitly.
/// </para>
/// </remarks>
public sealed class ErrorPagesMiddleware : IMiddleware
{
    public ErrorPagesMiddleware(Action<Builder>? configure = null)
    {
        var builder = new Builder();
        configure?.Invoke(builder);
        this._handlers = builder.Handlers;
    }

    private static readonly IReadOnlyDictionary<string, string> _emptyArgs =
        new Dictionary<string, string>();

    private readonly Dictionary<int, Func<HttpActionContext, Task>> _handlers;

    public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        await next();

        // If the response was already committed (route handler closed it), nothing left to do.
        try
        {
            if (!context.Response.OutputStream.CanWrite) return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        // StatusCode 200 (default, unset) means no route matched → treat as 404.
        var statusCode = context.Response.StatusCode is 200 or 0
            ? 404
            : context.Response.StatusCode;

        context.Response.StatusCode = statusCode;
        var actionCtx = new HttpActionContext(context.Request, context.Response, _emptyArgs);

        if (this._handlers.TryGetValue(statusCode, out var handler))
        {
            await handler(actionCtx);
        }
        else
        {
            context.Response.Close();
        }
    }

    public sealed class Builder
    {
        internal Dictionary<int, Func<HttpActionContext, Task>> Handlers { get; } = [];

        public Builder On(int statusCode, Func<HttpActionContext, Task> handler)
        {
            this.Handlers[statusCode] = handler;
            return this;
        }
    }
}
