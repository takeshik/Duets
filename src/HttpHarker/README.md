# HttpHarker

A minimal HTTP server library built on `System.Net.HttpListener` with a middleware pipeline.
HttpHarker is small enough to embed in a .NET application without adopting ASP.NET Core, and it has
no dependency on Duets.

## Usage

```csharp
using HttpHarker;

using var server = new HttpServer("http://127.0.0.1:8080/");

server
    .UseContentTypeDetection()
    .UseErrorPages(errors =>
        errors.On(404, async ctx =>
            await ctx.CloseAsync("text/plain", "Not Found")))
    .UseSimpleRouting("/api", routes =>
        routes.MapGet("/hello", async ctx =>
            await ctx.CloseAsync("text/plain", "Hello, world!"))
              .MapPost("/echo", async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            await ctx.CloseAsync("text/plain", body);
        }))
    .UseEmbeddedResources(typeof(Program).Assembly, "MyApp.StaticFiles", "/", options =>
    {
        options.EnableSpaFallback = true;
        options.EnableETag = true;
        options.CacheControlSelector = suffix =>
            suffix == "index.html"
                ? "no-cache"
                : "public, max-age=31536000, immutable";
    });

await server.RunAsync(workersCount: 8);
```

The runnable
[standalone server sample](https://github.com/takeshik/Duets/blob/main/samples/HttpHarker/hello-http.cs)
shows routing and response helpers in a complete file-based application.

## Middleware order

Requests enter middleware in registration order. Each middleware receives the
`HttpListenerContext` and a `next` delegate. Await `next()` to pass control to the rest of the
pipeline; omit it after handling and closing the response to short-circuit.

```csharp
server.Use(async (context, next) =>
{
    Console.WriteLine($"{context.Request.HttpMethod} {context.Request.Url?.AbsolutePath}");
    await next();
});
```

Code before `next()` runs on the way into the pipeline, and code after it runs on the way out.
`UseSimpleRouting` is terminal for a matched route: after its handler runs it does not call the next
middleware. Register wrapping middleware such as `UseErrorPages` before routing and other terminal
handlers. An unmatched route continues down the pipeline; if nothing handles a request, the server
closes it as 404.

Middleware must be registered before the server starts. `Use(...)` throws while the server is
running.

## Built-in middleware

| Registration | Role |
|---|---|
| `UseContentTypeDetection(...)` | Set the response content type from the request URL before continuing. A custom `ContentTypeProvider` can replace the extension map and fallback. |
| `UseErrorPages(...)` | Run the downstream pipeline, then handle configured status codes if the response is still writable. Register it before terminal middleware. |
| `UseSimpleRouting(root, ...)` | Match GET, POST, and other mapped handlers under a URL root. Literal segments outrank parameters, which outrank a terminal catch-all. |
| `UseStaticFiles(provider, root, ...)` | Serve bytes from any `IFileProvider`, continuing only when the path is outside the root or no file exists. |
| `UseEmbeddedResources(assembly, prefix, root, ...)` | Serve manifest resources whose slash-delimited URL suffix maps to a dot-delimited resource name. |
| `UseZipArchive(stream, root, ...)` | Read a zip stream at registration time and serve its entries. An overload opens the stream from an assembly resource. |

`UseStaticFiles`, `UseEmbeddedResources`, and `UseZipArchive` share `StaticFileOptions`. The options
cover a default document, optional single-page-application fallback and predicate, ETag generation,
cache-control selection, and content-type resolution. Static responses support GET/HEAD behavior;
missing files continue to later middleware.

## File providers

`IFileProvider.GetFileContent(relativePath)` is the extension point for serving host-owned file
bytes. Paths are normalized, forward-slash-delimited, and relative to the middleware root. Return
`null` when a file is absent so the pipeline can continue.

The built-in providers are:

- `EmbeddedResourceFileProvider`, which reads assembly manifest resources under a prefix.
- `ZipFileProvider`, which copies the archive into memory once and opens an independent reader per
  request for safe concurrent access.

The convenience middleware registrations construct these providers for the common cases. Pass a
custom provider to `UseStaticFiles` for another source such as generated or application-managed
content.

## Routing

Routes are matched relative to the root supplied to `UseSimpleRouting`:

```text
/users/{id}       parameter segment
/files/{*path}    catch-all segment; must be last
```

Route handlers receive an `HttpActionContext` with the underlying request and response plus matched
arguments in `ctx.Args`. A matched handler owns and normally closes its response. Because matched
routing is terminal, middleware registered after routing is reachable only for unmatched requests.

## Concurrency and lifecycle

`workersCount` and `maxConcurrentRequests` control different parts of the server:

- `workersCount` on `Start` or `RunAsync` is the number of loops accepting connections from the
  shared listener. Accepted requests are dispatched without occupying that loop for their whole
  lifetime.
- `maxConcurrentRequests` on the `HttpServer` constructor caps in-flight request handlers. Requests
  above the cap receive HTTP 503 without entering the middleware pipeline. Long-lived responses
  such as SSE streams hold a slot until they end.

For an awaited lifetime, call `RunAsync(workersCount, cancellationToken)`; cancelling the token
stops the listener and completes the call. For a background lifetime, call `Start(workersCount)` and
later `Stop()`. `Dispose()` stops a background run and permanently closes the underlying listener,
so keep the server in a `using` statement.

## Public surface

| Type | Description |
|---|---|
| `HttpServer` | Owns the `HttpListener`, middleware list, accept loops, and request concurrency cap. |
| `HttpActionContext` | Wraps the request, response, and route arguments and provides response helpers. |
| `IMiddleware` | Class-based middleware contract used by `HttpServer.Use`. |
| `IFileProvider` | Supplies static bytes by normalized relative path. |
| `StaticFileOptions` | Configures default documents, SPA fallback, caching, and content types. |
| `ContentTypeProvider` | Resolves content types from request-derived keys with a configurable fallback. |

## Architecture

See the repository's
[HttpHarker architecture](https://github.com/takeshik/Duets/blob/main/docs/architecture/HttpHarker.md)
for its dependency boundary and role in Duets, and the
[decision records](https://github.com/takeshik/Duets/tree/main/docs/decisions) for the underlying
trade-offs.
