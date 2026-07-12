// HttpHarker: minimal HTTP server with routing middleware
//
// Serves a tiny JSON/text API on http://127.0.0.1:17380/ using HttpHarker's
// middleware pipeline: content-type detection plus simple routing. HttpHarker
// is the HttpListener-based server library that also hosts DuetsPad; it has no
// dependency on Duets and can be used standalone.
#:project ../../src/HttpHarker/HttpHarker.csproj

using HttpHarker;

using var server = new HttpServer("http://127.0.0.1:17380/");

server
    .UseContentTypeDetection()
    .UseSimpleRouting(
        "/",
        routes =>
            routes
                .MapGet("/hello", async ctx => await ctx.CloseAsync("text/plain", "Hello, world!"))
                .MapGet(
                    "/greet/{name}",
                    async ctx => await ctx.CloseAsync("text/plain", $"Hello, {ctx.Args["name"]}!")
                )
                .MapPost(
                    "/echo",
                    async ctx =>
                    {
                        using var reader = new StreamReader(ctx.Request.InputStream);
                        var body = await reader.ReadToEndAsync();
                        await ctx.CloseAsync("text/plain", body);
                    }
                )
    );

Console.Error.WriteLine("HttpHarker started at http://127.0.0.1:17380/ — press Ctrl+C to stop.");
Console.Error.WriteLine("Try: curl http://127.0.0.1:17380/hello");
Console.Error.WriteLine("     curl http://127.0.0.1:17380/greet/duets");
Console.Error.WriteLine("     curl -d 'ping' http://127.0.0.1:17380/echo");
await server.RunAsync();
