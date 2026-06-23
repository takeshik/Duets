using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Duets.Completions;
using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Protocol;
using Duets.Tests.TestSupport;
using HttpHarker;
using Jint;

namespace Duets.Tests.Pad;

/// <summary>
/// HTTP-level integration tests for <see cref="DuetsPadService"/>.
/// </summary>
public sealed class DuetsPadServiceTests
{
    // Helpers

    private static Task RunAsync(
        string root,
        Action<DuetsPadServiceOptions>? extraConfigure,
        Func<HttpClient, string, Task> test
    )
    {
        return DuetsServerFixture.RunAsync(
            server =>
            {
                server
                    .UseContentTypeDetection()
                    .UseDuetsPad(
                        root,
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
                                Task.FromResult(
                                    "@font-face{font-family:\"tabler-icons\";src:url(\"./fonts/tabler-icons.woff2?v3.44.0\") format(\"woff2\"),url(\"./fonts/tabler-icons.woff?\") format(\"woff\"),url(\"./fonts/tabler-icons.ttf?v3.44.0\") format(\"truetype\")}"
                                )
                            );
                            opts.TablerIconsFont = AssetSources.FromBytes(_ =>
                                Task.FromResult("wOF2"u8.ToArray())
                            );
                            opts.KeepAliveInterval = TimeSpan.FromSeconds(60);
                            extraConfigure?.Invoke(opts);
                        }
                    );
            },
            test
        );
    }

    private static Task RunAsync(string root, Func<HttpClient, string, Task> test) =>
        RunAsync(root, null, test);

    private static Task RunAsync(Func<HttpClient, string, Task> test) => RunAsync("/", null, test);

    /// <summary>
    /// Reads SSE lines from <paramref name="reader"/> until a <c>data: ...</c> line is found
    /// whose <c>type</c> field starts with <paramref name="typePrefix"/> (or any data line when
    /// <paramref name="typePrefix"/> is <see langword="null"/>), parses the JSON payload, and
    /// returns it.
    /// </summary>
    private static async Task<JsonElement> ReadNextSseDataAsync(
        StreamReader reader,
        string? typePrefix = null
    )
    {
        while (true)
        {
            var line =
                await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))
                ?? throw new EndOfStreamException(
                    "The SSE stream ended before the next data event was received."
                );
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line["data: ".Length..]);
            var element = doc.RootElement.Clone();

            if (
                typePrefix is null
                || (
                    element.TryGetProperty("type", out var typeProp)
                    && typeProp.GetString()?.StartsWith(typePrefix, StringComparison.Ordinal)
                        == true
                )
            )
            {
                return element;
            }
        }
    }

    /// <summary>
    /// POST /sessions with empty body and returns the sessionId string.
    /// </summary>
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

    private sealed class UnknownLengthStringContent(string content) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(content);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(this._bytes, 0, this._bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    // POST /sessions

    [Fact]
    public async Task Post_sessions_with_empty_body_returns_new_session_id()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.PostAsync(
                    prefix + "sessions",
                    new StringContent("", Encoding.UTF8, "application/json")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                var sessionId = payload.GetProperty("sessionId").GetString();
                Assert.False(string.IsNullOrWhiteSpace(sessionId));
                Assert.True(Guid.TryParse(sessionId, out _));
            }
        );
    }

    [Fact]
    public async Task Post_sessions_twice_with_same_id_reuses_it()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var id1 = await CreateSessionAsync(client, prefix);

                using var response2 = await client.PostAsync(
                    prefix + "sessions",
                    new StringContent(
                        $"{{\"sessionId\":\"{id1}\"}}",
                        Encoding.UTF8,
                        "application/json"
                    )
                );
                response2.EnsureSuccessStatusCode();
                var payload2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
                var id2 = payload2.GetProperty("sessionId").GetString();

                Assert.Equal(id1, id2);
            }
        );
    }

    [Fact]
    public async Task Post_sessions_with_stale_guid_returns_different_new_id()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var staleGuid = Guid.NewGuid().ToString();

                using var response = await client.PostAsync(
                    prefix + "sessions",
                    new StringContent(
                        $"{{\"sessionId\":\"{staleGuid}\"}}",
                        Encoding.UTF8,
                        "application/json"
                    )
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                var newId = payload.GetProperty("sessionId").GetString();

                Assert.False(string.IsNullOrWhiteSpace(newId));
                Assert.NotEqual(staleGuid, newId);
            }
        );
    }

    // DELETE /sessions/{sessionId}

    [Fact]
    public async Task Delete_existing_session_returns_ok_and_subsequent_eval_returns_unknown_error()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                // DELETE the session.
                using var deleteResponse = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, prefix + $"sessions/{sessionId}")
                );
                deleteResponse.EnsureSuccessStatusCode();
                var deletePayload = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(deletePayload.GetProperty("ok").GetBoolean());
                Assert.Equal(sessionId, deletePayload.GetProperty("sessionId").GetString());

                // Eval on the deleted session must return unknown-session error.
                using var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 2", Encoding.UTF8, "text/plain")
                );
                evalResponse.EnsureSuccessStatusCode();
                var evalPayload = await evalResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(evalPayload.GetProperty("ok").GetBoolean());
                Assert.Equal("Unknown session.", evalPayload.GetProperty("error").GetString());
            }
        );
    }

    [Fact]
    public async Task Delete_unknown_session_returns_ok_false_with_unknown_session_error()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var unknownId = Guid.NewGuid().ToString();

                using var response = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, prefix + $"sessions/{unknownId}")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.Equal("Unknown session.", payload.GetProperty("error").GetString());
                Assert.Equal(unknownId, payload.GetProperty("sessionId").GetString());
            }
        );
    }

    // Idle timeout: RemoveIdleSessions with injected clock

    private static DuetsPadService BuildServiceWithIdleTimeout(
        HttpServer server,
        string root,
        DuetsPadServiceOptions opts
    )
    {
        // DuetsPadService constructor is internal; call it directly (InternalsVisibleTo).
        return new DuetsPadService(server, root, opts);
    }

    [Fact]
    public async Task IdleTimeout_disabled_by_default_does_not_evict_sessions()
    {
        // Build a service with a clock we can advance, but IdleTimeout left at its default null.
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;

        var opts = new DuetsPadServiceOptions
        {
            SessionFactory = () => JintTestRuntime.CreateSessionAsync(o => o.AllowClr()),
            Clock = () => current,
            // IdleTimeout intentionally not set — remains null.
        };

        // We need a real server to call HandlePostSessionAsync. Spin up the full stack.
        await RunAsync(
            "/",
            o =>
            {
                o.SessionFactory = opts.SessionFactory;
                o.Clock = opts.Clock;
                // IdleTimeout: null (default)
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                // Advance the clock well beyond any reasonable idle threshold.
                current = t0.AddHours(24);

                // Eval must still succeed because IdleTimeout is disabled.
                using var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 1", Encoding.UTF8, "text/plain")
                );
                evalResponse.EnsureSuccessStatusCode();
                var payload = await evalResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(payload.GetProperty("ok").GetBoolean());
            }
        );
    }

    [Fact]
    public async Task IdleTimeout_enabled_RemoveIdleSessions_evicts_session_and_eval_returns_unknown()
    {
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;

        // UseDuetsPad returns DuetsPadService; capture it so we can call RemoveIdleSessions().
        DuetsPadService? padService = null;

        await DuetsServerFixture.RunAsync(
            server =>
            {
                padService = server
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
                                Task.FromResult(
                                    "@font-face{font-family:\"tabler-icons\";src:url(\"./fonts/tabler-icons.woff2\") format(\"woff2\")}"
                                )
                            );
                            opts.TablerIconsFont = AssetSources.FromBytes(_ =>
                                Task.FromResult("wOF2"u8.ToArray())
                            );
                            opts.KeepAliveInterval = TimeSpan.FromSeconds(60);
                            opts.IdleTimeout = TimeSpan.FromMinutes(5);
                            // Large CleanupInterval: background timer must not fire during the test.
                            opts.CleanupInterval = TimeSpan.FromHours(1);
                            opts.Clock = () => current;
                        }
                    );
            },
            async (client, prefix) =>
            {
                Assert.NotNull(padService);

                var sessionId = await CreateSessionAsync(client, prefix);

                // Confirm session is alive.
                var evalOk = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 1", Encoding.UTF8, "text/plain")
                );
                evalOk.EnsureSuccessStatusCode();
                var okPayload = await evalOk.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(okPayload.GetProperty("ok").GetBoolean());

                // Advance clock past the idle threshold and trigger the sweep.
                current = t0.AddMinutes(6);
                padService!.RemoveIdleSessions();

                // Subsequent eval must return unknown-session.
                var evalAfter = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 1", Encoding.UTF8, "text/plain")
                );
                var evalPayload = await evalAfter.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(evalPayload.GetProperty("ok").GetBoolean());
                Assert.Equal("Unknown session.", evalPayload.GetProperty("error").GetString());
            }
        );
    }

    [Fact]
    public async Task IdleTimeout_eval_touch_prevents_premature_eviction_then_evicts_after_threshold()
    {
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;

        DuetsPadService? padService = null;

        await DuetsServerFixture.RunAsync(
            server =>
            {
                padService = server
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
                                Task.FromResult(
                                    "@font-face{font-family:\"tabler-icons\";src:url(\"./fonts/tabler-icons.woff2\") format(\"woff2\")}"
                                )
                            );
                            opts.TablerIconsFont = AssetSources.FromBytes(_ =>
                                Task.FromResult("wOF2"u8.ToArray())
                            );
                            opts.KeepAliveInterval = TimeSpan.FromSeconds(60);
                            opts.IdleTimeout = TimeSpan.FromMinutes(5);
                            opts.CleanupInterval = TimeSpan.FromHours(1);
                            opts.Clock = () => current;
                        }
                    );
            },
            async (client, prefix) =>
            {
                Assert.NotNull(padService);

                var sessionId = await CreateSessionAsync(client, prefix);

                // Advance to t0+4min (within IdleTimeout of 5min from creation).
                current = t0.AddMinutes(4);
                padService!.RemoveIdleSessions();

                // Session must still be alive.
                var evalMid = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 1", Encoding.UTF8, "text/plain")
                );
                var midPayload = await evalMid.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(
                    midPayload.GetProperty("ok").GetBoolean(),
                    "Session should still be alive at t0+4min"
                );

                // Eval at t0+4min counts as a Touch; advance another 4min (t0+8min).
                // That is only 4min since last activity, so must NOT be evicted.
                current = t0.AddMinutes(8);
                padService.RemoveIdleSessions();

                var evalStillAlive = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("2 + 2", Encoding.UTF8, "text/plain")
                );
                var alivePayload = await evalStillAlive.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(
                    alivePayload.GetProperty("ok").GetBoolean(),
                    "Session should still be alive 4min after last eval"
                );

                // Now advance to 6min past the last eval (t0+10min), exceeding the 5min threshold.
                current = t0.AddMinutes(14);
                padService.RemoveIdleSessions();

                var evalEvicted = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("3 + 3", Encoding.UTF8, "text/plain")
                );
                var evictedPayload = await evalEvicted.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(
                    evictedPayload.GetProperty("ok").GetBoolean(),
                    "Session should be evicted 6min after last activity"
                );
            }
        );
    }

    [Fact]
    public async Task RemoveIdleSessions_does_not_evict_session_with_active_subscriber()
    {
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;

        DuetsPadService? padService = null;

        await DuetsServerFixture.RunAsync(
            server =>
            {
                padService = server
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
                                Task.FromResult(
                                    "@font-face{font-family:\"tabler-icons\";src:url(\"./fonts/tabler-icons.woff2\") format(\"woff2\")}"
                                )
                            );
                            opts.TablerIconsFont = AssetSources.FromBytes(_ =>
                                Task.FromResult("wOF2"u8.ToArray())
                            );
                            opts.KeepAliveInterval = TimeSpan.FromSeconds(60);
                            opts.IdleTimeout = TimeSpan.FromMinutes(5);
                            opts.CleanupInterval = TimeSpan.FromHours(1);
                            opts.Clock = () => current;
                        }
                    );
            },
            async (client, prefix) =>
            {
                Assert.NotNull(padService);

                var sessionId = await CreateSessionAsync(client, prefix);
                var sessionGuid = Guid.Parse(sessionId);

                // Attach a subscriber directly at the session level — no real sleep required.
                var session = padService!.TryGetSession(sessionGuid);
                Assert.NotNull(session);

                var subChannel = Channel.CreateUnbounded<PadEventMessage?>();
                var subKey = session!.SubscribeEvents(
                    subChannel.Writer,
                    session.DuetsSession.Declarations
                );

                Assert.True(session.HasActiveSubscribers);

                // Advance the clock well past IdleTimeout.
                current = t0.AddMinutes(30);
                padService.RemoveIdleSessions();

                // Session must still be alive because the subscriber is attached.
                var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 1", Encoding.UTF8, "text/plain")
                );
                evalResponse.EnsureSuccessStatusCode();
                var evalPayload = await evalResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(
                    evalPayload.GetProperty("ok").GetBoolean(),
                    "Session with active subscriber must not be evicted."
                );

                // Detach the subscriber. HasActiveSubscribers is now false.
                session.UnsubscribeEvents(subKey);
                Assert.False(session.HasActiveSubscribers);

                // Now with no active subscriber, sweeping past the threshold must evict it.
                current = t0.AddMinutes(60);
                padService.RemoveIdleSessions();

                var evalAfter = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 1", Encoding.UTF8, "text/plain")
                );
                var afterPayload = await evalAfter.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(
                    afterPayload.GetProperty("ok").GetBoolean(),
                    "Session must be evicted after subscriber is removed and clock exceeds IdleTimeout."
                );
            }
        );
    }

    // SSE disconnect reaping

    [Fact]
    public async Task Sse_subscriber_is_reaped_after_client_disconnect()
    {
        DuetsPadService? padService = null;

        await DuetsServerFixture.RunAsync(
            server =>
            {
                padService = server
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
                            // Short keepalive so the write-driven disconnect detector fires
                            // promptly: the dead client is only observed on the next keepalive write.
                            opts.KeepAliveInterval = TimeSpan.FromMilliseconds(50);
                        }
                    );
            },
            async (client, prefix) =>
            {
                Assert.NotNull(padService);

                var sessionId = await CreateSessionAsync(client, prefix);
                var sessionGuid = Guid.Parse(sessionId);
                var session = padService!.TryGetSession(sessionGuid);
                Assert.NotNull(session);

                // Open a real SSE stream on its own client so dropping it does not disturb others.
                using var sseClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                var sseResponse = await sseClient.GetAsync(
                    prefix + $"sessions/{sessionId}/events",
                    HttpCompletionOption.ResponseHeadersRead
                );
                sseResponse.EnsureSuccessStatusCode();

                // Read the initial snapshot so we know the subscriber is registered and streaming.
                var sseStream = await sseResponse.Content.ReadAsStreamAsync();
                var sseReader = new StreamReader(sseStream);
                var snapshot = await ReadNextSseDataAsync(sseReader, typePrefix: "canvas.");
                Assert.Equal(CanvasEventTypes.Snapshot, snapshot.GetProperty("type").GetString());
                Assert.True(session!.HasActiveSubscribers);

                // Simulate the browser tab closing/reloading: dispose the response and stream so
                // the underlying TCP connection is torn down (FIN), exactly as a closed tab does.
                sseReader.Dispose();
                await sseStream.DisposeAsync();
                sseResponse.Dispose();

                // The subscriber must be reaped without any further client activity. With
                // write-driven detection this happens on the next keepalive write, which throws
                // a broken-pipe exception that ends the SSE loop and runs the canvas
                // Unsubscribe in its finally. Poll a bounded number of keepalive intervals.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (session.HasActiveSubscribers && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10);
                }

                Assert.False(
                    session.HasActiveSubscribers,
                    "SSE subscriber must be reaped after the client disconnects; otherwise the "
                        + "subscriber, keepalive timer, channel, and response leak for the process "
                        + "lifetime."
                );
            }
        );
    }

    // POST /sessions/{sessionId}/eval — unknown session

    [Fact]
    public async Task Eval_unknown_session_returns_error_and_does_not_create_session()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var unknownId = Guid.NewGuid().ToString();

                using var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{unknownId}/eval",
                    new StringContent("1 + 2", Encoding.UTF8, "text/plain")
                );
                evalResponse.EnsureSuccessStatusCode();
                var evalPayload = await evalResponse.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(evalPayload.GetProperty("ok").GetBoolean());
                Assert.False(
                    string.IsNullOrWhiteSpace(evalPayload.GetProperty("error").GetString())
                );

                // A subsequent events request with the same id should also error (session was not created).
                using var eventsResponse = await client.GetAsync(
                    prefix + $"sessions/{unknownId}/events",
                    HttpCompletionOption.ResponseHeadersRead
                );
                var body = await eventsResponse.Content.ReadAsStringAsync();
                // Either the response is an error JSON body or the headers indicate it is not SSE.
                var contentType = eventsResponse.Content.Headers.ContentType?.MediaType ?? "";
                Assert.DoesNotContain(
                    "text/event-stream",
                    contentType,
                    StringComparison.OrdinalIgnoreCase
                );
            }
        );
    }

    // POST /sessions/{sessionId}/eval — success and failure

    [Fact]
    public async Task Eval_success_returns_ok_result_and_session_id()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 2", Encoding.UTF8, "text/plain")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.True(payload.GetProperty("ok").GetBoolean());
                Assert.Equal("3", payload.GetProperty("result").GetString());
                Assert.Equal(sessionId, payload.GetProperty("sessionId").GetString());
            }
        );
    }

    [Fact]
    public async Task Eval_throwing_expression_returns_error_and_session_id()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("null.prop", Encoding.UTF8, "text/plain")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("error").GetString()));
                Assert.Equal(sessionId, payload.GetProperty("sessionId").GetString());
            }
        );
    }

    // Timeline SSE

    [Fact]
    public async Task Timeline_first_event_is_timeline_reset_with_reason_initial_and_empty_entries()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/events"
                );
                using var reader = new StreamReader(stream);

                var first = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");

                Assert.Equal(TimelineEventTypes.Reset, first.GetProperty("type").GetString());
                Assert.Equal("initial", first.GetProperty("reason").GetString());
                Assert.Empty(first.GetProperty("entries").EnumerateArray());
            }
        );
    }

    [Fact]
    public async Task Timeline_dump_produces_timeline_append_event_with_correct_shape()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/events"
                );
                using var reader = new StreamReader(stream);

                // Consume the initial reset.
                var reset = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Reset, reset.GetProperty("type").GetString());

                // Trigger a dump.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("dump(\"x\")", Encoding.UTF8, "text/plain")
                );

                // Next event must be a timeline.append.
                var append = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Append, append.GetProperty("type").GetString());

                var entry = append.GetProperty("entry");
                Assert.Equal("dump", entry.GetProperty("reason").GetString());

                var body = entry.GetProperty("body");
                Assert.Equal("text", body.GetProperty("kind").GetString());
                Assert.Equal("x", body.GetProperty("value").GetString());
            }
        );
    }

    [Fact]
    public async Task Eval_with_source_immediate_appends_timeline_append_with_reason_evaluation()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/events"
                );
                using var reader = new StreamReader(stream);

                // Consume the initial reset.
                var reset = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Reset, reset.GetProperty("type").GetString());

                // POST with ?source=immediate.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval?source=immediate",
                    new StringContent("1 + 2", Encoding.UTF8, "text/plain")
                );

                // Must receive a timeline.append event with reason "evaluation" and body text "3".
                var append = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Append, append.GetProperty("type").GetString());

                var entry = append.GetProperty("entry");
                Assert.Equal("evaluation", entry.GetProperty("reason").GetString());

                var body = entry.GetProperty("body");
                Assert.Equal("text", body.GetProperty("kind").GetString());
                Assert.Equal("3", body.GetProperty("value").GetString());
            }
        );
    }

    [Fact]
    public async Task Eval_without_source_does_not_append_evaluation_entry()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                // POST without source query param.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("1 + 2", Encoding.UTF8, "text/plain")
                );

                // Connect a fresh subscriber and verify the reset has no entries.
                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/events"
                );
                using var reader = new StreamReader(stream);

                var reset = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Reset, reset.GetProperty("type").GetString());
                Assert.Empty(reset.GetProperty("entries").EnumerateArray());
            }
        );
    }

    [Fact]
    public async Task DuetsPadJs_contains_source_immediate_and_does_not_set_immediate_result_on_success()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var js = await client.GetStringAsync(prefix + "duetspad.js");

                // Must contain the source=immediate query string.
                Assert.Contains("source=immediate", js, StringComparison.Ordinal);

                // Must NOT permanently assign the eval result into an immediate result element on success.
                Assert.DoesNotContain(
                    "setImmediateResult(data.result",
                    js,
                    StringComparison.Ordinal
                );
            }
        );
    }

    [Fact]
    public async Task DuetsPadJs_handles_all_namespaced_protocol_types_and_has_no_bare_cases()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var js = await client.GetStringAsync(prefix + "duetspad.js");

                // Must declare the PAD_EVENTS constants object.
                Assert.Contains("PAD_EVENTS", js, StringComparison.Ordinal);

                // All namespaced protocol type strings must be present (defined in PAD_EVENTS).
                Assert.Contains(CanvasEventTypes.Snapshot, js, StringComparison.Ordinal);
                Assert.Contains(CanvasEventTypes.Replace, js, StringComparison.Ordinal);
                Assert.Contains(TimelineEventTypes.Reset, js, StringComparison.Ordinal);
                Assert.Contains(TimelineEventTypes.Append, js, StringComparison.Ordinal);
                Assert.Contains(TimelineEventTypes.Update, js, StringComparison.Ordinal);
                Assert.Contains(TimelineEventTypes.Trim, js, StringComparison.Ordinal);

                // Must NOT contain bare (unnamespaced) switch cases.
                Assert.DoesNotContain("case 'snapshot'", js, StringComparison.Ordinal);
                Assert.DoesNotContain("case 'append'", js, StringComparison.Ordinal);
                Assert.DoesNotContain("case 'update'", js, StringComparison.Ordinal);
                Assert.DoesNotContain("case 'replace'", js, StringComparison.Ordinal);
                Assert.DoesNotContain("case 'clear'", js, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task DuetsPadJs_open_text_fallback_does_not_depend_on_bootstrap_js()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var js = await client.GetStringAsync(prefix + "duetspad.js");

                Assert.Contains("toastEl.classList.add(\"show\")", js, StringComparison.Ordinal);
                Assert.Contains(
                    "window.setTimeout(closeToast, 8000)",
                    js,
                    StringComparison.Ordinal
                );
                Assert.DoesNotContain("bootstrap.Toast", js, StringComparison.Ordinal);
                Assert.DoesNotContain("data-bs-dismiss", js, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task DuetsPadJs_handoff_tab_creates_fresh_session()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var js = await client.GetStringAsync(prefix + "duetspad.js");

                Assert.Contains(
                    "const hasHandoff = new URLSearchParams(window.location.search).has(",
                    js,
                    StringComparison.Ordinal
                );
                Assert.Contains("\"handoff\"", js, StringComparison.Ordinal);
                Assert.Contains("const stored = hasHandoff", js, StringComparison.Ordinal);
                Assert.Contains("? null", js, StringComparison.Ordinal);
                Assert.Contains(
                    ": sessionStorage.getItem(\"duetspad.sessionId\")",
                    js,
                    StringComparison.Ordinal
                );
            }
        );
    }

    // Canvas SSE

    [Fact]
    public async Task Canvas_first_event_is_canvas_snapshot_with_empty_root_children()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/events"
                );
                using var reader = new StreamReader(stream);

                var first = await ReadNextSseDataAsync(reader, typePrefix: "canvas.");

                Assert.Equal(CanvasEventTypes.Snapshot, first.GetProperty("type").GetString());
                var state = first.GetProperty("state");
                Assert.Equal("element", state.GetProperty("kind").GetString());
                Assert.Empty(state.GetProperty("children").EnumerateArray());
            }
        );
    }

    [Fact]
    public async Task Canvas_add_produces_canvas_patch_with_insert_child()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/events"
                );
                using var reader = new StreamReader(stream);

                // Consume the initial canvas.snapshot.
                var initial = await ReadNextSseDataAsync(reader, typePrefix: "canvas.");
                Assert.Equal(CanvasEventTypes.Snapshot, initial.GetProperty("type").GetString());

                // Trigger canvas.add.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("canvas.add(ui.label(\"hi\"))", Encoding.UTF8, "text/plain")
                );

                // Next event is canvas.patch with one inserted child.
                var patch = await ReadNextSseDataAsync(reader, typePrefix: "canvas.");
                Assert.Equal(CanvasEventTypes.Patch, patch.GetProperty("type").GetString());
                Assert.Equal(0, patch.GetProperty("baseRevision").GetInt64());
                Assert.Equal(1, patch.GetProperty("revision").GetInt64());
                var operation = Assert.Single(patch.GetProperty("operations").EnumerateArray());
                Assert.Equal("insert-child", operation.GetProperty("op").GetString());
                Assert.Equal(0, operation.GetProperty("index").GetInt32());
            }
        );
    }

    [Fact]
    public async Task Canvas_snapshot_route_returns_current_canvas_revision_and_state()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("canvas.add(ui.label(\"hi\"))", Encoding.UTF8, "text/plain")
                );
                evalResponse.EnsureSuccessStatusCode();

                using var snapshotResponse = await client.GetAsync(
                    prefix + $"sessions/{sessionId}/canvas?name=default"
                );
                snapshotResponse.EnsureSuccessStatusCode();
                var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();

                Assert.Equal(CanvasEventTypes.Snapshot, snapshot.GetProperty("type").GetString());
                Assert.Equal("default", snapshot.GetProperty("name").GetString());
                Assert.Equal(1, snapshot.GetProperty("revision").GetInt64());
                Assert.Single(
                    snapshot.GetProperty("state").GetProperty("children").EnumerateArray()
                );
            }
        );
    }

    [Fact]
    public async Task Interaction_invoke_route_runs_canvas_button_handler()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/events"
                );
                using var reader = new StreamReader(stream);

                var initialCanvas = await ReadNextSseDataAsync(reader, typePrefix: "canvas.");
                Assert.Equal(
                    CanvasEventTypes.Snapshot,
                    initialCanvas.GetProperty("type").GetString()
                );

                var initialTimeline = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");
                Assert.Equal(
                    TimelineEventTypes.Reset,
                    initialTimeline.GetProperty("type").GetString()
                );

                using var evalResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent(
                        """canvas.add(ui.button("Run", () => dump("clicked")))""",
                        Encoding.UTF8,
                        "text/plain"
                    )
                );
                evalResponse.EnsureSuccessStatusCode();

                var patch = await ReadNextSseDataAsync(reader, typePrefix: "canvas.");
                Assert.Equal(CanvasEventTypes.Patch, patch.GetProperty("type").GetString());
                var interaction = Assert.Single(patch.GetProperty("interactions").EnumerateArray());
                var handlerId = interaction.GetProperty("handlerId").GetString();
                Assert.False(string.IsNullOrWhiteSpace(handlerId));

                using var invokeResponse = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/interactions/{handlerId}/invoke",
                    content: null
                );
                invokeResponse.EnsureSuccessStatusCode();
                var invokePayload = await invokeResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(invokePayload.GetProperty("ok").GetBoolean());

                var append = await ReadNextSseDataAsync(reader, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Append, append.GetProperty("type").GetString());
                var entry = append.GetProperty("entry");
                Assert.Equal("dump", entry.GetProperty("reason").GetString());
                Assert.Equal("clicked", entry.GetProperty("body").GetProperty("value").GetString());
            }
        );
    }

    // SSE response headers

    [Fact]
    public async Task Events_response_has_correct_headers()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.GetAsync(
                    prefix + $"sessions/{sessionId}/events",
                    HttpCompletionOption.ResponseHeadersRead
                );
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType!.ToString();
                Assert.Contains(
                    "text/event-stream",
                    contentType,
                    StringComparison.OrdinalIgnoreCase
                );
                Assert.Contains("utf-8", contentType, StringComparison.OrdinalIgnoreCase);

                Assert.True(
                    response.Headers.TryGetValues("Cache-Control", out var cc)
                        && cc.Any(v => v.Contains("no-cache", StringComparison.OrdinalIgnoreCase))
                );
            }
        );
    }

    // Session isolation

    [Fact]
    public async Task Session_isolation_timeline_dump_in_A_does_not_appear_in_B()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionA = await CreateSessionAsync(client, prefix);
                var sessionB = await CreateSessionAsync(client, prefix);

                // Open event streams for both sessions.
                await using var streamA = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionA}/events"
                );
                await using var streamB = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionB}/events"
                );
                using var readerA = new StreamReader(streamA);
                using var readerB = new StreamReader(streamB);

                // Consume initial resets.
                var snapA = await ReadNextSseDataAsync(readerA, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Reset, snapA.GetProperty("type").GetString());
                var snapB = await ReadNextSseDataAsync(readerB, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Reset, snapB.GetProperty("type").GetString());

                // Dump in session A.
                await client.PostAsync(
                    prefix + $"sessions/{sessionA}/eval",
                    new StringContent("dump(\"a\")", Encoding.UTF8, "text/plain")
                );

                // Session A should receive a timeline.append.
                var appendA = await ReadNextSseDataAsync(readerA, typePrefix: "timeline.");
                Assert.Equal(TimelineEventTypes.Append, appendA.GetProperty("type").GetString());

                // Session B should NOT receive any event in a short window — verify with a timeout.
                var readBTask = readerB.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(500));
                string? lineB = null;
                try
                {
                    lineB = await readBTask;
                }
                catch (TimeoutException)
                {
                    lineB = null;
                }

                // If we got a line it should not be a data event (it could be a keepalive comment or null).
                if (lineB is not null)
                {
                    Assert.DoesNotMatch(@"^data:", lineB);
                }
            }
        );
    }

    [Fact]
    public async Task Session_isolation_canvas_add_in_A_does_not_appear_in_B()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionA = await CreateSessionAsync(client, prefix);
                var sessionB = await CreateSessionAsync(client, prefix);

                await using var streamA = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionA}/events"
                );
                await using var streamB = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionB}/events"
                );
                using var readerA = new StreamReader(streamA);
                using var readerB = new StreamReader(streamB);

                // Consume initial canvas.snapshot events.
                var snapA = await ReadNextSseDataAsync(readerA, typePrefix: "canvas.");
                Assert.Equal(CanvasEventTypes.Snapshot, snapA.GetProperty("type").GetString());
                Assert.Empty(snapA.GetProperty("state").GetProperty("children").EnumerateArray());

                var snapB = await ReadNextSseDataAsync(readerB, typePrefix: "canvas.");
                Assert.Equal(CanvasEventTypes.Snapshot, snapB.GetProperty("type").GetString());
                Assert.Empty(snapB.GetProperty("state").GetProperty("children").EnumerateArray());

                // Add canvas element in session A.
                await client.PostAsync(
                    prefix + $"sessions/{sessionA}/eval",
                    new StringContent("canvas.add(ui.label(\"a\"))", Encoding.UTF8, "text/plain")
                );

                // Session A receives a canvas.patch event.
                var updateA = await ReadNextSseDataAsync(readerA, typePrefix: "canvas.");
                Assert.Equal(CanvasEventTypes.Patch, updateA.GetProperty("type").GetString());
                Assert.Single(updateA.GetProperty("operations").EnumerateArray());

                // Session B should receive no data event within a short window.
                var readBTask = readerB.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(500));
                string? lineB = null;
                try
                {
                    lineB = await readBTask;
                }
                catch (TimeoutException)
                {
                    lineB = null;
                }

                if (lineB is not null)
                {
                    Assert.DoesNotMatch(@"^data:", lineB);
                }
            }
        );
    }

    // Non-root prefix

    [Fact]
    public async Task Non_root_prefix_routes_work()
    {
        await RunAsync(
            "/pad",
            async (client, prefix) =>
            {
                // POST /pad/sessions
                using var sessResponse = await client.PostAsync(
                    prefix + "pad/sessions",
                    new StringContent("{}", Encoding.UTF8, "application/json")
                );
                sessResponse.EnsureSuccessStatusCode();
                var sessPayload = await sessResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(Guid.TryParse(sessPayload.GetProperty("sessionId").GetString(), out _));

                // GET /pad/ (the root html)
                using var htmlResponse = await client.GetAsync(prefix + "pad/");
                Assert.Equal(System.Net.HttpStatusCode.OK, htmlResponse.StatusCode);
            }
        );
    }

    // Static assets

    [Fact]
    public async Task Static_index_html_returns_200()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Contains("DuetsPad", body, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task Static_duetspad_js_returns_200()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix + "duetspad.js");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Static_duetspad_ui_js_returns_200()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix + "duetspad-ui.js");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task DuetsPadUIJs_uses_modern_bindings_and_dom_construction()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var js = await client.GetStringAsync(prefix + "duetspad-ui.js");

                Assert.DoesNotMatch(@"\bvar\b", js);
                Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task Tabler_css_returns_stub_content()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var content = await client.GetStringAsync(prefix + "tabler.css");
                Assert.Equal("/* tabler */", content);
            }
        );
    }

    [Fact]
    public async Task Monaco_loader_js_returns_stub_content()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var content = await client.GetStringAsync(prefix + "monaco-loader.js");
                Assert.Equal("// monaco", content);
            }
        );
    }

    // GET /duetspad-config.js

    [Fact]
    public async Task DuetsPadConfigJs_returns_200_with_javascript_content_type()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix + "duetspad-config.js");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                Assert.Equal("text/javascript", contentType, ignoreCase: true);
            }
        );
    }

    [Fact]
    public async Task DuetsPadConfigJs_body_contains_DUETSPAD_MONACO_VS_and_default_url()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var body = await client.GetStringAsync(prefix + "duetspad-config.js");
                Assert.Contains("window.DUETSPAD_MONACO_VS", body, StringComparison.Ordinal);
                Assert.Contains("monaco-editor@0.55.1/min/vs", body, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task Static_index_html_references_duetspad_config_js()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var body = await client.GetStringAsync(prefix);
                Assert.Contains("duetspad-config.js", body, StringComparison.Ordinal);
            }
        );
    }

    // Tabler Icons routes

    [Fact]
    public async Task TablerIconsCss_returns_200_with_css_content_type_and_rewritten_url()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix + "tabler-icons.css");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                Assert.Contains("text/css", contentType, StringComparison.OrdinalIgnoreCase);
                var body = await response.Content.ReadAsStringAsync();
                // The src must reference only the canonical local woff2 route (no ./fonts/ paths).
                Assert.Contains("url(\"tabler-icons.woff2\")", body, StringComparison.Ordinal);
                Assert.DoesNotContain("./fonts/", body, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task TablerIconsFont_returns_200_with_woff2_content_type_and_stub_bytes()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix + "tabler-icons.woff2");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                Assert.Equal("font/woff2", contentType, ignoreCase: true);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal("wOF2"u8.ToArray(), bytes);
            }
        );
    }

    [Fact]
    public async Task Static_index_html_references_tabler_icons_css()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var body = await client.GetStringAsync(prefix);
                Assert.Contains("tabler-icons.css", body, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task TablerCss_still_returns_200_after_icons_routes_added()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix + "tabler.css");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }
        );
    }

    // POST /sessions/{sessionId}/complete

    [Fact]
    public async Task Complete_returns_items_for_registered_tag_without_source_parsing()
    {
        await RunAsync(
            "/",
            opts =>
            {
                opts.SessionFactory = async () =>
                {
                    var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                    session.RegisterTaggedTemplate(
                        "path",
                        complete: (context, _) =>
                            new ValueTask<IReadOnlyList<TemplateCompletionItem>>([
                                new TemplateCompletionItem(
                                    "/foo/bar",
                                    ReplacementSpan: new TextSpan(
                                        0,
                                        context.CurrentSegmentRaw.Length
                                    ),
                                    Kind: TemplateCompletionKind.Folder
                                ),
                            ])
                    );
                    return session;
                };
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var request =
                    "{\"tag\":\"path\",\"textBeforeCaret\":\"/foo/ba\",\"textAfterCaret\":\"\",\"currentSegmentRaw\":\"/foo/ba\",\"caretOffsetInSegment\":7}";

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.True(payload.GetProperty("ok").GetBoolean());
                var item = Assert.Single(payload.GetProperty("items").EnumerateArray());
                Assert.Equal("/foo/bar", item.GetProperty("label").GetString());
                Assert.Equal("Folder", item.GetProperty("kind").GetString());
                Assert.Equal(
                    0,
                    item.GetProperty("replacementSpan").GetProperty("start").GetInt32()
                );
            }
        );
    }

    [Fact]
    public async Task Complete_returns_unknown_session_for_missing_session()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = Guid.NewGuid();
                var request =
                    "{\"tag\":\"path\",\"textBeforeCaret\":\"x\",\"textAfterCaret\":\"\",\"currentSegmentRaw\":\"x\",\"caretOffsetInSegment\":1}";

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.Equal("Unknown session.", payload.GetProperty("error").GetString());
            }
        );
    }

    [Fact]
    public async Task Complete_returns_disabled_error_when_feature_is_disabled()
    {
        await RunAsync(
            "/",
            opts => opts.EnableTaggedTemplateCompletions = false,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var request =
                    "{\"tag\":\"path\",\"textBeforeCaret\":\"x\",\"textAfterCaret\":\"\",\"currentSegmentRaw\":\"x\",\"caretOffsetInSegment\":1}";

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.Equal(
                    "Tagged-template completions are disabled.",
                    payload.GetProperty("error").GetString()
                );
            }
        );
    }

    [Fact]
    public async Task Complete_rejects_oversize_body_without_requiring_content_length()
    {
        await RunAsync(
            "/",
            opts => opts.TaggedTemplateCompletionMaxRequestBytes = 16,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                using var content = new UnknownLengthStringContent(new string('x', 128));
                content.Headers.ContentType = new("application/json");

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    content
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.Equal(
                    "Tagged-template completion request is too large.",
                    payload.GetProperty("error").GetString()
                );
            }
        );
    }

    [Fact]
    public async Task Complete_enforces_rate_limit_after_registered_tag_check()
    {
        await RunAsync(
            "/",
            opts =>
            {
                opts.TaggedTemplateCompletionRateLimitPerSecond = 1;
                opts.SessionFactory = async () =>
                {
                    var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                    session.RegisterTaggedTemplate(
                        "path",
                        complete: (_, _) => new ValueTask<IReadOnlyList<TemplateCompletionItem>>([])
                    );
                    return session;
                };
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var request =
                    "{\"tag\":\"path\",\"textBeforeCaret\":\"x\",\"textAfterCaret\":\"\",\"currentSegmentRaw\":\"x\",\"caretOffsetInSegment\":1}";

                using var first = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                first.EnsureSuccessStatusCode();

                using var second = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                var payload = await second.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.Equal(
                    "Tagged-template completion rate limit exceeded.",
                    payload.GetProperty("error").GetString()
                );
            }
        );
    }

    [Fact]
    public async Task Complete_returns_timeout_when_callback_exceeds_timeout()
    {
        await RunAsync(
            "/",
            opts =>
            {
                opts.TaggedTemplateCompletionTimeout = TimeSpan.FromMilliseconds(50);
                opts.SessionFactory = async () =>
                {
                    var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                    session.RegisterTaggedTemplate(
                        "path",
                        complete: async (_, cancellationToken) =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                            return [];
                        }
                    );
                    return session;
                };
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var request =
                    "{\"tag\":\"path\",\"textBeforeCaret\":\"x\",\"textAfterCaret\":\"\",\"currentSegmentRaw\":\"x\",\"caretOffsetInSegment\":1}";

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.True(payload.GetProperty("timedOut").GetBoolean());
            }
        );
    }

    [Fact]
    public async Task Complete_returns_empty_items_for_unknown_tag()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var request =
                    "{\"tag\":\"missing\",\"textBeforeCaret\":\"x\",\"textAfterCaret\":\"\",\"currentSegmentRaw\":\"x\",\"caretOffsetInSegment\":1}";

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.True(payload.GetProperty("ok").GetBoolean());
                Assert.Empty(payload.GetProperty("items").EnumerateArray());
            }
        );
    }

    [Fact]
    public async Task Complete_filters_items_with_out_of_segment_replacement_span()
    {
        await RunAsync(
            "/",
            opts =>
            {
                opts.SessionFactory = async () =>
                {
                    var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                    session.RegisterTaggedTemplate(
                        "path",
                        complete: (_, _) =>
                            new ValueTask<IReadOnlyList<TemplateCompletionItem>>([
                                new TemplateCompletionItem(
                                    "valid",
                                    ReplacementSpan: new TextSpan(0, 1)
                                ),
                                new TemplateCompletionItem(
                                    "invalid",
                                    ReplacementSpan: new TextSpan(10, 1)
                                ),
                            ])
                    );
                    return session;
                };
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var request =
                    "{\"tag\":\"path\",\"textBeforeCaret\":\"x\",\"textAfterCaret\":\"\",\"currentSegmentRaw\":\"x\",\"caretOffsetInSegment\":1}";

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(request, Encoding.UTF8, "application/json")
                );
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                var item = Assert.Single(payload.GetProperty("items").EnumerateArray());
                Assert.Equal("valid", item.GetProperty("label").GetString());
            }
        );
    }

    [Fact]
    public async Task Events_initial_burst_includes_tagged_template_snapshot()
    {
        await RunAsync(
            "/",
            opts =>
            {
                opts.SessionFactory = async () =>
                {
                    var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                    session.RegisterTaggedTemplate(
                        "path",
                        complete: (_, _) => new ValueTask<IReadOnlyList<TemplateCompletionItem>>([])
                    );
                    return session;
                };
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                using var response = await client.GetAsync(
                    prefix + $"sessions/{sessionId}/events",
                    HttpCompletionOption.ResponseHeadersRead
                );
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var snapshot = await ReadNextSseDataAsync(reader, "taggedTemplate.");

                Assert.Equal("taggedTemplate.snapshot", snapshot.GetProperty("type").GetString());
                Assert.Contains(
                    snapshot.GetProperty("tags").EnumerateArray(),
                    tag => tag.GetString() == "path"
                );
            }
        );
    }

    // RewriteTablerIconsCss unit tests

    [Fact]
    public void RewriteTablerIconsCss_rewrites_font_face_src_to_single_woff2_dropping_fallbacks()
    {
        const string input =
            "@font-face{font-family:\"tabler-icons\";font-style:normal;font-weight:400;src:url(\"./fonts/tabler-icons.woff2?v3.44.0\") format(\"woff2\"),url(\"./fonts/tabler-icons.woff?\") format(\"woff\"),url(\"./fonts/tabler-icons.ttf?v3.44.0\") format(\"truetype\")}";

        var result = AssetProvider.RewriteTablerIconsCss(input);

        // The src must reference only the canonical local woff2 route.
        Assert.Contains(
            "src:url(\"tabler-icons.woff2\") format(\"woff2\")",
            result,
            StringComparison.Ordinal
        );

        // No ./fonts/ references must remain (woff2 prefix stripped, woff/ttf fallbacks dropped).
        Assert.DoesNotContain("./fonts/", result, StringComparison.Ordinal);
        Assert.DoesNotContain("format(\"woff\")", result, StringComparison.Ordinal);
        Assert.DoesNotContain("format(\"truetype\")", result, StringComparison.Ordinal);
        Assert.DoesNotContain(".ttf", result, StringComparison.Ordinal);
    }

    // Options validation (early failure on UseDuetsPad)

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void UseDuetsPad_throws_ArgumentOutOfRangeException_when_TimelineEntryLimit_is_non_positive(
        int invalidLimit
    )
    {
        using var server = new HttpServer("http://127.0.0.1:0/");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            server.UseDuetsPad(
                "/",
                opts =>
                {
                    opts.TimelineEntryLimit = invalidLimit;
                }
            )
        );
    }

    [Fact]
    public void UseDuetsPad_does_not_throw_when_TimelineEntryLimit_is_null()
    {
        using var server = new HttpServer("http://127.0.0.1:0/");
        // Should not throw.
        using var service = server.UseDuetsPad("/", opts => opts.TimelineEntryLimit = null);
        Assert.NotNull(service);
    }

    [Fact]
    public void UseDuetsPad_does_not_throw_when_TimelineEntryLimit_is_positive()
    {
        using var server = new HttpServer("http://127.0.0.1:0/");
        // Should not throw.
        using var service = server.UseDuetsPad("/", opts => opts.TimelineEntryLimit = 10);
        Assert.NotNull(service);
    }
}
