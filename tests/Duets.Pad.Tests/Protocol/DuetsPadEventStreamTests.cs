using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Duets.Completions;
using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Tests.TestSupport;
using Duets.Tests.TestSupport;
using HttpHarker;
using Jint;

namespace Duets.Pad.Tests.Protocol;

/// <summary>
/// End-to-end tests for <see cref="DuetsPadClient"/>'s SSE reader against a real
/// DuetsPad server, focused on the continuity of a single stream across read timeouts.
/// </summary>
public sealed class DuetsPadEventStreamTests
{
    private static Task RunWithServerAsync(
        Func<HttpClient, string, Uri, Task> test,
        Action<DuetsPadServiceOptions>? configure = null
    )
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
                            configure?.Invoke(opts);
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
    public async Task ReadSse_with_tagged_template_registration_keeps_stream_open_after_initial_burst()
    {
        await RunWithServerAsync(
            async (client, prefix, baseUri) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var padClient = new DuetsPadClient(baseUri, sessionId);
                var open = await padClient.OpenEventsAsync();
                Assert.True(open.Ok, open.Error);
                using var stream = open.Value!;

                var initial = await stream.ReadAsync(
                    maxRecords: 64,
                    timeout: TimeSpan.FromMilliseconds(1000),
                    includeComments: false
                );
                Assert.True(
                    initial.Records.Any(record =>
                        record.Json?["type"]?.GetValue<string>() == "taggedTemplate.snapshot"
                    ),
                    "Expected tagged-template snapshot in the initial SSE records."
                );

                var timedOut = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromMilliseconds(50),
                    includeComments: false
                );

                Assert.True(
                    timedOut.TimedOut,
                    "The stream must stay open after the tagged-template snapshot."
                );
            },
            opts =>
                opts.SessionFactory = async () =>
                {
                    var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                    session.RegisterTaggedTemplate(
                        "path",
                        invocation => string.Concat(invocation.Raw),
                        complete: (_, _) => new ValueTask<IReadOnlyList<TemplateCompletionItem>>([])
                    );
                    return session;
                }
        );
    }

    [Fact]
    public async Task ReadSse_after_timeout_can_still_read_subsequent_events_on_same_stream()
    {
        await RunWithServerAsync(
            async (client, prefix, baseUri) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var padClient = new DuetsPadClient(baseUri, sessionId);
                var open = await padClient.OpenEventsAsync();
                Assert.True(open.Ok, open.Error);
                using var stream = open.Value!;

                // Drain the initial snapshot/reset events so the stream is quiet before the
                // timeout read under test. A generous timeout here just collects what is buffered.
                var initial = await stream.ReadAsync(
                    maxRecords: 16,
                    timeout: TimeSpan.FromMilliseconds(1000),
                    includeComments: false
                );
                Assert.False(initial.Ended);

                // (a) Force a read timeout: nothing new is arriving, short timeout.
                var timedOut = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromMilliseconds(50),
                    includeComments: false
                );
                Assert.True(timedOut.TimedOut, "The read with no available events must time out.");

                // (b) Trigger a server-side event on the same session after the timeout.
                using var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("dump(\"after-timeout\")", Encoding.UTF8, "text/plain")
                );
                evalResponse.EnsureSuccessStatusCode();

                // (c) Reading the same stream again must surface the next event, not a dead stream.
                // This eval emits one data record; reading one record avoids waiting for timeout.
                var afterTimeout = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromMilliseconds(2000),
                    includeComments: false
                );

                Assert.Contains(
                    afterTimeout.Records,
                    record =>
                        record.Data is not null
                        && record.Data.Contains("after-timeout", StringComparison.Ordinal)
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
                using var padClient = new DuetsPadClient(baseUri, Guid.NewGuid().ToString());
                var open = await padClient.OpenEventsAsync();
                Assert.True(open.Ok, open.Error);
                using var stream = open.Value!;

                var timedOut = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromMilliseconds(100),
                    includeComments: false
                );
                Assert.True(timedOut.TimedOut);
                Assert.Empty(timedOut.Records);

                var completed = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromMilliseconds(2000),
                    includeComments: false
                );

                Assert.False(completed.TimedOut);
                var record = Assert.Single(completed.Records);
                Assert.Equal("""{"value":1}""", record.Data);
            }
        );
    }

    [Fact]
    public async Task ReadSse_after_cancellation_preserves_partial_data_record()
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
                                    Encoding.UTF8.GetBytes("data: retained-after-cancel\n")
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
                using var padClient = new DuetsPadClient(
                    new Uri(prefix),
                    Guid.NewGuid().ToString("D")
                );
                var open = await padClient.OpenEventsAsync();
                Assert.True(open.Ok, open.Error);
                using var stream = open.Value!;
                using var cancellation = new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(100)
                );

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    stream.ReadAsync(
                        1,
                        TimeSpan.FromSeconds(2),
                        cancellationToken: cancellation.Token
                    )
                );

                var completed = await stream.ReadAsync(
                    1,
                    TimeSpan.FromSeconds(2),
                    cancellationToken: TestContext.Current.CancellationToken
                );
                Assert.Equal("retained-after-cancel", Assert.Single(completed.Records).Data);
            }
        );
    }

    [Fact]
    public async Task ReadSse_comment_limit_preserves_the_partial_data_record_for_the_next_read()
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
                                    Encoding.UTF8.GetBytes(
                                        """
                                        data: {"value":1}
                                        : heartbeat


                                        """
                                    )
                                );
                                await ctx.Response.OutputStream.FlushAsync();
                                ctx.Response.Close();
                            }
                        )
                );
            },
            async (_, prefix) =>
            {
                using var padClient = new DuetsPadClient(
                    new Uri(prefix),
                    Guid.NewGuid().ToString("D")
                );
                var open = await padClient.OpenEventsAsync();
                Assert.True(open.Ok, open.Error);
                using var stream = open.Value!;

                var comment = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromSeconds(2),
                    includeComments: true
                );
                var commentRecord = Assert.Single(comment.Records);
                Assert.Equal("comment", commentRecord.Kind);
                Assert.Equal("heartbeat", commentRecord.Comment);

                var data = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromSeconds(2),
                    includeComments: true
                );
                var dataRecord = Assert.Single(data.Records);
                Assert.Equal("data", dataRecord.Kind);
                Assert.Equal("""{"value":1}""", dataRecord.Data);
            }
        );
    }

    [Fact]
    public async Task ReadSse_rejects_an_overlapping_read_without_corrupting_the_stream()
    {
        await RunWithServerAsync(
            async (client, prefix, baseUri) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var padClient = new DuetsPadClient(baseUri, sessionId);
                var open = await padClient.OpenEventsAsync();
                Assert.True(open.Ok, open.Error);
                using var stream = open.Value!;

                var firstRead = stream.ReadAsync(
                    maxRecords: 64,
                    timeout: TimeSpan.FromMilliseconds(200),
                    includeComments: false,
                    cancellationToken: TestContext.Current.CancellationToken
                );

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    stream.ReadAsync(
                        maxRecords: 1,
                        timeout: TimeSpan.FromSeconds(1),
                        includeComments: false,
                        cancellationToken: TestContext.Current.CancellationToken
                    )
                );
                Assert.Contains("Only one read", exception.Message, StringComparison.Ordinal);

                var first = await firstRead;
                Assert.True(first.TimedOut);

                var next = await stream.ReadAsync(
                    maxRecords: 1,
                    timeout: TimeSpan.FromMilliseconds(20),
                    includeComments: false,
                    cancellationToken: TestContext.Current.CancellationToken
                );
                Assert.True(next.TimedOut);
            }
        );
    }

    [Fact]
    public async Task ReadSse_removes_only_the_single_optional_space_after_a_field_separator()
    {
        using var response = new HttpResponseMessage();
        using var body = new MemoryStream(
            Encoding.UTF8.GetBytes("data:   padded\n\n:   comment\n\n")
        );
        using var stream = new DuetsPadEventStream(response, body, _ => { });

        var result = await stream.ReadAsync(
            maxRecords: 2,
            timeout: TimeSpan.FromSeconds(1),
            includeComments: true,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Collection(
            result.Records,
            record => Assert.Equal("  padded", record.Data),
            record => Assert.Equal("  comment", record.Comment)
        );
    }

    [Fact]
    public async Task ReadSse_honors_an_already_cancelled_wait_without_consuming_input()
    {
        using var response = new HttpResponseMessage();
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("data: retained\n\n"));
        using var stream = new DuetsPadEventStream(response, body, _ => { });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            stream.ReadAsync(1, TimeSpan.FromSeconds(1), cancellationToken: cancelled.Token)
        );

        var result = await stream.ReadAsync(
            1,
            TimeSpan.FromSeconds(1),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Equal("retained", Assert.Single(result.Records).Data);
    }
}
