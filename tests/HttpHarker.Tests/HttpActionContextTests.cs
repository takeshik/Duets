using System.Net;
using HttpHarker.Tests.TestSupport;

namespace HttpHarker.Tests;

/// <summary>
/// Tests for <see cref="HttpActionContext.CloseAsync"/> overloads.
/// </summary>
public sealed class HttpActionContextTests
{
    [Fact]
    public async Task CloseAsync_contentType_and_body_sets_response()
    {
        await ServerFixture.RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                    b.MapGet("/data", ctx => ctx.CloseAsync("text/plain", "hello"))
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "data");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("text/plain", resp.Content.Headers.ContentType?.MediaType);
                Assert.Equal("hello", await resp.Content.ReadAsStringAsync());
            }
        );
    }

    [Fact]
    public async Task CloseAsync_contentType_with_charset_parameter_does_not_throw()
    {
        // Regression: passing "application/json; charset=utf-8" must not throw FormatException.
        await ServerFixture.RunAsync(
            s => s.UseSimpleRouting(
                "/",
                b =>
                    b.MapGet("/json", ctx => ctx.CloseAsync("application/json; charset=utf-8", "{}"))
            ),
            async (client, prefix) =>
            {
                var resp = await client.GetAsync(prefix + "json");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
                Assert.Equal("{}", await resp.Content.ReadAsStringAsync());
            }
        );
    }
}
