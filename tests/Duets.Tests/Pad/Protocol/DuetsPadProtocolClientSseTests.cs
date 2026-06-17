using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Duets.Jint;
using Duets.Pad;
using Duets.Sandbox;
using Duets.Tests.TestSupport;
using HttpHarker;
using Jint;

namespace Duets.Tests.Pad.Protocol;

/// <summary>
/// End-to-end tests for <see cref="DuetsPadProtocolClient"/>'s SSE harness against a real
/// DuetsPad server, focused on the continuity of a single stream across read timeouts.
/// </summary>
public sealed class DuetsPadProtocolClientSseTests
{
    private static Task RunWithServerAsync(Func<HttpClient, string, Uri, Task> test)
    {
        return DuetsServerFixture.RunAsync(
            server =>
            {
                server
                    .UseContentTypeDetection()
                    .UseDuetsPad(
                        "/",
                        opts =>
                        {
                            opts.SessionFactory = () =>
                                JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                            opts.MonacoLoader = AssetSources.From(_ =>
                                Task.FromResult("// monaco")
                            );
                            opts.TablerCss = AssetSources.From(_ =>
                                Task.FromResult("/* tabler */")
                            );
                            opts.TablerIconsCss = AssetSources.From(_ =>
                                Task.FromResult("/* icons */")
                            );
                            opts.TablerIconsFont = AssetSources.FromBytes(_ =>
                                Task.FromResult("wOF2"u8.ToArray())
                            );
                            // Long keepalive so the timeout under test is not satisfied by a
                            // keepalive comment arriving on the stream.
                            opts.KeepAliveInterval = TimeSpan.FromSeconds(60);
                        }
                    );
            },
            (client, prefix) => test(client, prefix, new Uri(prefix))
        );
    }

    private static async Task<string> CreateSessionAsync(HttpClient client, string prefix)
    {
        using var response = await client.PostAsync(
            prefix + "sessions",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("sessionId").GetString()!;
    }

    [Fact]
    public async Task ReadSse_after_timeout_can_still_read_subsequent_events_on_same_stream()
    {
        await RunWithServerAsync(
            async (client, prefix, baseUri) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var padClient = new DuetsPadProtocolClient(baseUri);

                var open = await padClient.OpenSseAsync("s1", sessionId, "events");
                Assert.True(open["ok"]!.GetValue<bool>());

                // Drain the initial snapshot/reset events so the stream is quiet before the
                // timeout read under test. A generous timeout here just collects what is buffered.
                var initial = await padClient.ReadSseAsync(
                    "s1",
                    maxRecords: 16,
                    timeoutMs: 1000,
                    includeComments: false
                );
                Assert.True(initial["ok"]!.GetValue<bool>());

                // (a) Force a read timeout: nothing new is arriving, short timeout.
                var timedOut = await padClient.ReadSseAsync(
                    "s1",
                    maxRecords: 1,
                    timeoutMs: 50,
                    includeComments: false
                );
                Assert.True(timedOut["ok"]!.GetValue<bool>());
                Assert.True(
                    timedOut["timedOut"]!.GetValue<bool>(),
                    "The read with no available events must time out."
                );

                // (b) Trigger a server-side event on the same session after the timeout.
                using var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("dump(\"after-timeout\")", Encoding.UTF8, "text/plain")
                );
                evalResponse.EnsureSuccessStatusCode();

                // (c) Reading the same stream again must surface the next event, not a dead stream.
                // This eval emits one data record; reading one record avoids waiting for timeout.
                var afterTimeout = await padClient.ReadSseAsync(
                    "s1",
                    maxRecords: 1,
                    timeoutMs: 2000,
                    includeComments: false
                );

                Assert.True(
                    afterTimeout["ok"]!.GetValue<bool>(),
                    $"Subsequent read after a timeout must succeed, got: {afterTimeout.ToJsonString()}"
                );

                var records = afterTimeout["records"]!.AsArray();
                Assert.Contains(
                    records,
                    record =>
                        record is JsonObject obj
                        && obj.TryGetPropertyValue("data", out var data)
                        && data is not null
                        && data.GetValue<string>()
                            .Contains("after-timeout", StringComparison.Ordinal)
                );
            }
        );
    }

    [Fact]
    public async Task ReadSse_after_timeout_preserves_partial_data_record()
    {
        await DuetsServerFixture.RunAsync(
            server =>
            {
                server.UseSimpleRouting(
                    "/",
                    routes =>
                        routes.MapGet(
                            "/sessions/{sessionId}/events",
                            async ctx =>
                            {
                                ctx.Response.ContentType = "text/event-stream; charset=utf-8";
                                ctx.Response.SendChunked = true;

                                await ctx.Response.OutputStream.WriteAsync(
                                    Encoding.UTF8.GetBytes("""data: {"value":1}""" + "\n")
                                );
                                await ctx.Response.OutputStream.FlushAsync();

                                await Task.Delay(500);

                                await ctx.Response.OutputStream.WriteAsync(
                                    Encoding.UTF8.GetBytes("\n")
                                );
                                await ctx.Response.OutputStream.FlushAsync();
                                ctx.Response.Close();
                            }
                        )
                );
            },
            async (_, prefix) =>
            {
                var baseUri = new Uri(prefix);
                using var padClient = new DuetsPadProtocolClient(baseUri);

                var open = await padClient.OpenSseAsync("s1", Guid.NewGuid().ToString(), "events");
                Assert.True(open["ok"]!.GetValue<bool>());

                var timedOut = await padClient.ReadSseAsync(
                    "s1",
                    maxRecords: 1,
                    timeoutMs: 100,
                    includeComments: false
                );
                Assert.True(timedOut["ok"]!.GetValue<bool>());
                Assert.True(timedOut["timedOut"]!.GetValue<bool>());
                Assert.Empty(timedOut["records"]!.AsArray());

                var completed = await padClient.ReadSseAsync(
                    "s1",
                    maxRecords: 1,
                    timeoutMs: 2000,
                    includeComments: false
                );

                Assert.True(completed["ok"]!.GetValue<bool>());
                Assert.False(completed["timedOut"]!.GetValue<bool>());
                var record = Assert.IsType<JsonObject>(
                    Assert.Single(completed["records"]!.AsArray())
                );
                Assert.Equal("""{"value":1}""", record["data"]!.GetValue<string>());
            }
        );
    }
}
