using System.Net;
using HttpHarker.Tests.TestSupport;

namespace HttpHarker.Tests;

public sealed class ErrorPagesMiddlewareTests
{
    private static Task RunAsync(
        Action<HttpServer> configure,
        Func<HttpClient, string, Task> test)
    {
        return ServerFixture.RunAsync(configure, test);
    }

    // ---------------------------------------------------------------------------
    // Already-committed response is not re-processed
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Already_committed_response_is_not_touched()
    {
        await RunAsync(
            s =>
            {
                s.UseErrorPages(b =>
                    b.On(200, ctx => ctx.CloseAsync("text/plain", "error-page"))
                );
                // A route handler that commits the response normally.
                s.UseSimpleRouting(
                    "/",
                    b =>
                        b.MapGet("/ok", ctx => ctx.CloseAsync("text/plain", "ok"))
                );
            },
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "ok");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                // Body should be the route handler's response, not the error page body.
                Assert.Equal("ok", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Default (200 / 0) status code treated as 404
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Default_status_code_treated_as_404()
    {
        await RunAsync(
            s => s.UseErrorPages(b =>
                b.On(404, ctx => ctx.CloseAsync("text/plain", "404"))
            ),
            async (client, prefix) =>
            {
                // No route matched → server leaves status at 200 default → ErrorPages treats as 404.
                var resp = await client.GetAsync(prefix);
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                Assert.Equal("404", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Ordering: ErrorPages placed AFTER routing is not invoked for matched routes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ErrorPages_after_routing_not_invoked_for_matched_route()
    {
        // SimpleRoutingMiddleware does NOT call next() after handling a matched route,
        // so ErrorPagesMiddleware registered after it is unreachable for those requests.
        // Correct usage: UseErrorPages must be registered BEFORE UseSimpleRouting.
        await RunAsync(
            s =>
            {
                s.UseSimpleRouting(
                    "/",
                    b =>
                        b.MapGet(
                            "/forbidden",
                            ctx =>
                            {
                                ctx.Response.StatusCode = 403;
                                return Task.CompletedTask;
                            }
                        )
                );
                // ErrorPages registered AFTER routing: unreachable for matched routes.
                s.UseErrorPages(b =>
                    b.On(403, ctx => ctx.CloseAsync("text/plain", "forbidden page"))
                );
            },
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "forbidden");
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
                // Error page body is NOT written; routing returned without invoking next().
                Assert.Empty(await resp.Content.ReadAsByteArrayAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Multiple handlers
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Multiple_handlers_each_invoked_for_correct_code()
    {
        // UseErrorPages must be outermost (first). Route handlers set the status code
        // without closing the response, then pass control to next so ErrorPages can render.
        await RunAsync(
            s =>
            {
                s.UseErrorPages(b =>
                    {
                        b.On(403, ctx => ctx.CloseAsync("text/plain", "forbidden page"));
                        b.On(404, ctx => ctx.CloseAsync("text/plain", "not found page"));
                    }
                );
                s.UseSimpleRouting(
                    "/",
                    b =>
                    {
                        b.MapGet(
                            "/forbidden",
                            ctx =>
                            {
                                ctx.Response.StatusCode = 403;
                                // Set status but do not close; ErrorPagesMiddleware handles the response.
                                return Task.CompletedTask;
                            }
                        );
                    }
                );
            },
            async (client, prefix) =>
            {
                var forbidden = await client.GetAsync(prefix + "forbidden");
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                Assert.Equal("forbidden page", await forbidden.Content.ReadAsStringAsync());

                var notFound = await client.GetAsync(prefix + "missing");
                Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
                Assert.Equal("not found page", await notFound.Content.ReadAsStringAsync());
            }
        );
    }

    [Fact]
    public async Task Registered_handler_invoked_for_500()
    {
        await RunAsync(
            s =>
            {
                // Middleware that explicitly sets 500 then passes to next.
                s.Use(async (ctx, next) =>
                    {
                        ctx.Response.StatusCode = 500;
                        await next();
                    }
                );
                s.UseErrorPages(b =>
                    b.On(500, ctx => ctx.CloseAsync("text/plain", "server error page"))
                );
            },
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix);
                Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
                Assert.Equal("server error page", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Registered status code handler is invoked
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Registered_handler_invoked_for_matching_status_code()
    {
        await RunAsync(
            s => s.UseErrorPages(b =>
                b.On(404, ctx => ctx.CloseAsync("text/plain", "not found page"))
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "missing");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                Assert.Equal("not found page", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Unregistered status code: response is closed without custom body
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Unregistered_status_code_response_closed_with_no_custom_body()
    {
        await RunAsync(
            s => s.UseErrorPages(), // no handlers registered
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "missing");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                Assert.Empty(await resp.Content.ReadAsByteArrayAsync());
            }
        );
    }
}
