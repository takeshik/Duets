using System.Net;
using HttpHarker.Tests.TestSupport;

namespace HttpHarker.Tests;

/// <summary>
/// Guards the server-concurrency property: long-lived (never-completing) responses must not
/// starve the accept loop. A regression here reproduces the DuetsPad bug where a handful of
/// open SSE streams left new page loads stuck at "connecting".
/// </summary>
public sealed class ConcurrentRequestDispatchTests
{
    [Fact]
    public async Task Long_lived_responses_do_not_block_new_requests()
    {
        // Block long-lived handlers until the test releases them, so they stay open exactly like
        // an SSE stream awaiting a channel that never completes.
        using var release = new ManualResetEventSlim(false);

        // Open more concurrent long-lived requests than the default worker count (8). Before the
        // fix, the accept loop awaited each handler, so workersCount open streams pinned every
        // worker and no further connection could be accepted.
        const int longLivedCount = 16;

        await ServerFixture.RunAsync(
            s =>
                s.Use(
                    async (ctx, next) =>
                    {
                        var path = ctx.Request.Url?.AbsolutePath ?? "";
                        if (path == "/stream")
                        {
                            ctx.Response.SendChunked = true;
                            // Hold the response open without spinning a thread.
                            await Task.Run(() => release.Wait(TimeSpan.FromSeconds(20)));
                            ctx.Response.Close();
                            return;
                        }

                        if (path == "/ping")
                        {
                            var bytes = "pong"u8.ToArray();
                            ctx.Response.ContentType = "text/plain";
                            await ctx.Response.OutputStream.WriteAsync(bytes);
                            ctx.Response.Close();
                            return;
                        }

                        await next();
                    }
                ),
            async (client, prefix) =>
            {
                using var streamCts = new CancellationTokenSource();

                // Fire off the long-lived requests; do not await them (they never complete until
                // released). ResponseHeadersRead ensures the request reaches the server.
                var streams = Enumerable
                    .Range(0, longLivedCount)
                    .Select(_ =>
                        client.GetAsync(
                            prefix + "stream",
                            HttpCompletionOption.ResponseHeadersRead,
                            streamCts.Token
                        )
                    )
                    .ToArray();

                // Give the server time to accept and begin handling all of them.
                await Task.Delay(500);

                // A fresh request must still be accepted and answered promptly.
                using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var body = await client.GetStringAsync(prefix + "ping", probeCts.Token);
                Assert.Equal("pong", body);

                // Let the long-lived handlers complete so the server can shut down cleanly.
                release.Set();
                streamCts.Cancel();
                foreach (var stream in streams)
                {
                    try
                    {
                        (await stream).Dispose();
                    }
                    catch
                    {
                        /* cancelled or faulted on teardown — irrelevant to the assertion */
                    }
                }
            }
        );
    }

    /// <summary>
    /// When the concurrency cap is reached, new requests must receive HTTP 503 immediately —
    /// not block waiting for a free slot.
    /// </summary>
    [Fact]
    public async Task Requests_beyond_cap_receive_503_immediately()
    {
        const int cap = 2;
        using var release = new ManualResetEventSlim(false);

        void ConfigureServer(HttpServer s) =>
            s.Use(
                async (ctx, next) =>
                {
                    var path = ctx.Request.Url?.AbsolutePath ?? "";
                    if (path == "/stream")
                    {
                        ctx.Response.SendChunked = true;
                        await Task.Run(() => release.Wait(TimeSpan.FromSeconds(20)));
                        ctx.Response.Close();
                        return;
                    }

                    if (path == "/ping")
                    {
                        var bytes = "pong"u8.ToArray();
                        ctx.Response.ContentType = "text/plain";
                        await ctx.Response.OutputStream.WriteAsync(bytes);
                        ctx.Response.Close();
                        return;
                    }

                    await next();
                }
            );

        await ServerFixture.RunAsync(
            ConfigureServer,
            async (client, prefix) =>
            {
                using var streamCts = new CancellationTokenSource();

                // Open exactly cap long-lived requests, filling the concurrency slots.
                var streams = Enumerable
                    .Range(0, cap)
                    .Select(_ =>
                        client.GetAsync(
                            prefix + "stream",
                            HttpCompletionOption.ResponseHeadersRead,
                            streamCts.Token
                        )
                    )
                    .ToArray();

                // Give the server time to accept all of them.
                await Task.Delay(500);

                // The next request must be rejected with 503 promptly — not hang.
                using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await client.GetAsync(prefix + "ping", probeCts.Token);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

                // Release the long-lived handlers so the server shuts down cleanly.
                release.Set();
                streamCts.Cancel();
                foreach (var stream in streams)
                {
                    try
                    {
                        (await stream).Dispose();
                    }
                    catch
                    {
                        /* cancelled or faulted on teardown — irrelevant to the assertion */
                    }
                }
            },
            maxConcurrentRequests: cap
        );
    }

    /// <summary>
    /// After a slot is freed (handler completes), the next request must be accepted normally —
    /// the cap is not permanently reduced by prior overflow.
    /// </summary>
    [Fact]
    public async Task Slot_freed_after_handler_completes_accepts_new_request()
    {
        const int cap = 1;

        // Two separate gate objects: one to hold the first stream open, one to release it.
        using var gate = new SemaphoreSlim(0, 1);

        void ConfigureServer(HttpServer s) =>
            s.Use(
                async (ctx, next) =>
                {
                    var path = ctx.Request.Url?.AbsolutePath ?? "";
                    if (path == "/stream")
                    {
                        ctx.Response.SendChunked = true;
                        // Wait until the test signals release.
                        await gate.WaitAsync(TimeSpan.FromSeconds(20));
                        ctx.Response.Close();
                        return;
                    }

                    if (path == "/ping")
                    {
                        var bytes = "pong"u8.ToArray();
                        ctx.Response.ContentType = "text/plain";
                        await ctx.Response.OutputStream.WriteAsync(bytes);
                        ctx.Response.Close();
                        return;
                    }

                    await next();
                }
            );

        await ServerFixture.RunAsync(
            ConfigureServer,
            async (client, prefix) =>
            {
                // Fill the single slot with a long-lived request.
                var stream = client.GetAsync(
                    prefix + "stream",
                    HttpCompletionOption.ResponseHeadersRead
                );
                await Task.Delay(300);

                // While the slot is occupied the next request must get 503.
                using var overflowCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var overflowResponse = await client.GetAsync(
                    prefix + "ping",
                    overflowCts.Token
                );
                Assert.Equal(HttpStatusCode.ServiceUnavailable, overflowResponse.StatusCode);

                // Release the long-lived handler and wait for the slot to free up.
                gate.Release();
                try
                {
                    (await stream).Dispose();
                }
                catch
                {
                    /* ignore */
                }

                // Give the server time to process the teardown and decrement the counter.
                await Task.Delay(300);

                // A new request must now succeed.
                using var retryCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var body = await client.GetStringAsync(prefix + "ping", retryCts.Token);
                Assert.Equal("pong", body);
            },
            maxConcurrentRequests: cap
        );
    }
}
