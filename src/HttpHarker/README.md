# HttpHarker

A minimal HTTP server library built on `System.Net.HttpListener` with a middleware pipeline. Designed to be lightweight enough to embed in any .NET application without pulling in ASP.NET Core.

## Usage

```csharp
using HttpHarker;

using var server = new HttpServer("http://127.0.0.1:8080/");

server
    .UseContentTypeDetection()
    .UseSimpleRouting("/api", routes =>
        routes.MapGet("/hello", async ctx =>
            await ctx.CloseAsync("text/plain", "Hello, world!"))
              .MapPost("/echo", async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            await ctx.CloseAsync("text/plain", body);
        }))
    .UseEmbeddedResources(typeof(Program).Assembly, "MyApp.StaticFiles", "/")
    .UseErrorPages(errors =>
        errors.On(404, async ctx =>
            await ctx.CloseAsync("text/plain", "Not Found")));

await server.RunAsync(workersCount: 4);
```

## Middleware Pipeline

Requests flow through middleware in registration order. Each middleware receives the `HttpListenerContext` and a `next` delegate. Call `next()` to pass to the next middleware, or handle the response directly to short-circuit.

```csharp
server.Use(async (ctx, next) =>
{
    Console.WriteLine($"{ctx.Request.HttpMethod} {ctx.Request.Url?.AbsolutePath}");
    await next();
});
```

## Built-in Middleware

### SimpleRoutingMiddleware

Template-based routing with parameter and catch-all segment support.

```
/users/{id}        → parameter segment
/files/{*path}     → catch-all segment (must be last)
```

Routes are matched in priority order: literal segments first, then parameters, then catch-all. Route handlers receive an `HttpActionContext` with typed access to matched arguments via `ctx.Args`.

### EmbeddedResourceMiddleware

Serves files from .NET embedded resources. Maps URL paths to resource names by replacing `/` with `.`. Falls back to `index.html` for the root path.

### ErrorPagesMiddleware

Catches unhandled requests (no prior middleware responded) and maps status codes to custom handlers.

### ContentTypeDetection

An inline middleware (via `UseContentTypeDetection()`) that sets `Content-Type` based on the request URL file extension.

## Key Types

| Type | Description |
|---|---|
| `HttpServer` | Core server — manages `HttpListener`, middleware pipeline, and worker loop |
| `HttpActionContext` | Wraps `HttpListenerRequest`, `HttpListenerResponse`, and route arguments |
| `IMiddleware` | Interface for middleware classes |
| `ContentTypeProvider` | Extension-to-MIME-type mapping |

## Design Notes

- Uses `System.Net.HttpListener` directly — no ASP.NET Core dependency.
- Multiple concurrent worker tasks process requests from a shared listener.
- The middleware pipeline uses a simple delegate chain (`Func<HttpListenerContext, Func<Task>, Task>`).
