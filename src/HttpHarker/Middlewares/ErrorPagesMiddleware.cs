using System.Net;

namespace HttpHarker.Middlewares;

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
        // Requests that reach this middleware were not handled by any previous middleware.
        // StatusCode 200 (default, unset) means "no route matched" → treat as 404.
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
