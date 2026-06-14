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
}
