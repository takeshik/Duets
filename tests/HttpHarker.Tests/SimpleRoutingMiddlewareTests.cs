using System.Net;
using HttpHarker.Tests.TestSupport;

namespace HttpHarker.Tests;

public sealed class SimpleRoutingMiddlewareTests
{
    private static Task RunAsync(
        Action<HttpServer> configure,
        Func<HttpClient, string, Task> test)
    {
        return ServerFixture.RunAsync(configure, test);
    }

    // Helper: write a plain-text response body.
    private static Task Reply(HttpActionContext ctx, string body)
    {
        return ctx.CloseAsync(body);
    }

    // ---------------------------------------------------------------------------
    // HTTP methods
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Correct_method_is_dispatched(string method)
    {
        await RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                {
                    b.MapGet("/r", ctx => Reply(ctx, "get"));
                    b.MapPost("/r", ctx => Reply(ctx, "post"));
                    b.MapPut("/r", ctx => Reply(ctx, "put"));
                    b.MapDelete("/r", ctx => Reply(ctx, "delete"));
                }
            ),
            async (client, prefix) =>
            {
                var req = new HttpRequestMessage(new HttpMethod(method), prefix + "r");
                var resp = await client.SendAsync(req);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal(method.ToLowerInvariant(), await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Catch-all parameters
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Catch_all_captures_remaining_path_segments()
    {
        await RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                    b.MapGet("/files/{*path}", ctx => Reply(ctx, ctx.Args["path"]))
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "files/a/b/c.txt");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("a/b/c.txt", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    [Fact]
    public void Catch_all_not_last_segment_throws_at_construction()
    {
        Assert.Throws<ArgumentException>(() =>
            new HttpServer("http://localhost:9999/")
                .UseSimpleRouting("/", b => b.MapGet("/{*rest}/extra", _ => Task.CompletedTask))
        );
    }

    [Fact]
    public void Empty_catch_all_name_throws_at_construction()
    {
        Assert.Throws<ArgumentException>(() =>
            new HttpServer("http://localhost:9999/")
                .UseSimpleRouting("/", b => b.MapGet("/files/{*}", _ => Task.CompletedTask))
        );
    }

    // ---------------------------------------------------------------------------
    // Template validation at construction
    // ---------------------------------------------------------------------------

    [Fact]
    public void Empty_parameter_name_throws_at_construction()
    {
        Assert.Throws<ArgumentException>(() =>
            new HttpServer("http://localhost:9999/")
                .UseSimpleRouting("/", b => b.MapGet("/users/{}", _ => Task.CompletedTask))
        );
    }

    // ---------------------------------------------------------------------------
    // Literal routes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Literal_route_matched_exactly()
    {
        await RunAsync(
            s => s.UseSimpleRouting("/", b => b.MapGet("/hello", ctx => Reply(ctx, "hi"))),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "hello");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("hi", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Route priority: literal beats parameter
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Literal_segment_takes_priority_over_parameter()
    {
        await RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                {
                    b.MapGet("/users/me", ctx => Reply(ctx, "me"));
                    b.MapGet("/users/{id}", ctx => Reply(ctx, ctx.Args["id"]));
                }
            ),
            async (client, prefix) =>
            {
                var meResp = await client.GetAsync(prefix + "users/me");
                Assert.Equal("me", await meResp.Content.ReadAsStringAsync());

                var idResp = await client.GetAsync(prefix + "users/123");
                Assert.Equal("123", await idResp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Map() fluent builder
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Map_with_explicit_method_registers_route()
    {
        await RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                    b.Map(HttpMethod.Patch, "/item", ctx => Reply(ctx, "patched"))
            ),
            async (client, prefix) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, prefix + "item");
                var resp = await client.SendAsync(req);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("patched", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    [Fact]
    public async Task Method_mismatch_passes_to_next()
    {
        await RunAsync(
            s => s.UseSimpleRouting("/", b => b.MapGet("/r", ctx => Reply(ctx, "ok"))),
            async (client, prefix) =>
            {
                var resp = await client.PostAsync(prefix + "r", null);
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Multiple_parameters_all_captured()
    {
        await RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                    b.MapGet("/a/{x}/b/{y}", ctx => Reply(ctx, $"{ctx.Args["x"]},{ctx.Args["y"]}"))
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "a/foo/b/bar");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("foo,bar", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Root prefix scoping
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Routes_scoped_to_configured_root_prefix()
    {
        await RunAsync(
            s => s.UseSimpleRouting(
                "/api",
                b =>
                    b.MapGet("/ping", ctx => Reply(ctx, "pong"))
            ),
            async (client, prefix) =>
            {
                var ok = await client.GetAsync(prefix + "api/ping");
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

                var notFound = await client.GetAsync(prefix + "ping");
                Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
            }
        );
    }

    // ---------------------------------------------------------------------------
    // Route parameters
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Single_parameter_segment_captured_in_args()
    {
        await RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                    b.MapGet("/users/{id}", ctx => Reply(ctx, ctx.Args["id"]))
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "users/42");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("42", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    [Fact]
    public async Task Unmatched_route_passes_to_next_and_returns_404()
    {
        await RunAsync(
            s => s.UseSimpleRouting("/", b => b.MapGet("/hello", ctx => Reply(ctx, "hi"))),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "other");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
        );
    }
}
