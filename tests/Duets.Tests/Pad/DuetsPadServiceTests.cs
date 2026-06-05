using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Task RunAsync(string root, Func<HttpClient, string, Task> test)
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
                                DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
                            opts.MonacoLoader = AssetSources.From(_ =>
                                Task.FromResult("// monaco")
                            );
                            opts.TablerCss = AssetSources.From(_ =>
                                Task.FromResult("/* tabler */")
                            );
                            opts.KeepAliveInterval = TimeSpan.FromSeconds(60);
                        }
                    );
            },
            test
        );
    }

    private static Task RunAsync(Func<HttpClient, string, Task> test) => RunAsync("/", test);

    /// <summary>
    /// Reads SSE lines from <paramref name="reader"/> until a <c>data: ...</c> line is found,
    /// parses the JSON payload, and returns it.
    /// </summary>
    private static async Task<JsonElement> ReadNextSseDataAsync(StreamReader reader)
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
            return doc.RootElement.Clone();
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

    // -------------------------------------------------------------------------
    // POST /sessions
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // POST /sessions/{sessionId}/eval — unknown session
    // -------------------------------------------------------------------------

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

                // A subsequent canvas-events request with the same id should also error (session was not created).
                using var canvasResponse = await client.GetAsync(
                    prefix + $"sessions/{unknownId}/canvas-events",
                    HttpCompletionOption.ResponseHeadersRead
                );
                var body = await canvasResponse.Content.ReadAsStringAsync();
                // Either the response is an error JSON body or the headers indicate it is not SSE.
                var contentType = canvasResponse.Content.Headers.ContentType?.MediaType ?? "";
                Assert.DoesNotContain(
                    "text/event-stream",
                    contentType,
                    StringComparison.OrdinalIgnoreCase
                );
            }
        );
    }

    // -------------------------------------------------------------------------
    // POST /sessions/{sessionId}/eval — success and failure
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Timeline SSE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Timeline_first_event_is_timeline_reset_with_reason_initial_and_empty_entries()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/timeline-events"
                );
                using var reader = new StreamReader(stream);

                var first = await ReadNextSseDataAsync(reader);

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
                    prefix + $"sessions/{sessionId}/timeline-events"
                );
                using var reader = new StreamReader(stream);

                // Consume the initial reset.
                var reset = await ReadNextSseDataAsync(reader);
                Assert.Equal(TimelineEventTypes.Reset, reset.GetProperty("type").GetString());

                // Trigger a dump.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("dump(\"x\")", Encoding.UTF8, "text/plain")
                );

                // Next event must be a timeline.append.
                var append = await ReadNextSseDataAsync(reader);
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
                    prefix + $"sessions/{sessionId}/timeline-events"
                );
                using var reader = new StreamReader(stream);

                // Consume the initial reset.
                var reset = await ReadNextSseDataAsync(reader);
                Assert.Equal(TimelineEventTypes.Reset, reset.GetProperty("type").GetString());

                // POST with ?source=immediate.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval?source=immediate",
                    new StringContent("1 + 2", Encoding.UTF8, "text/plain")
                );

                // Must receive a timeline.append event with reason "evaluation" and body text "3".
                var append = await ReadNextSseDataAsync(reader);
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
                    prefix + $"sessions/{sessionId}/timeline-events"
                );
                using var reader = new StreamReader(stream);

                var reset = await ReadNextSseDataAsync(reader);
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

    // -------------------------------------------------------------------------
    // Canvas SSE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Canvas_first_event_is_canvas_snapshot_with_empty_root_children()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/canvas-events"
                );
                using var reader = new StreamReader(stream);

                var first = await ReadNextSseDataAsync(reader);

                Assert.Equal(CanvasEventTypes.Snapshot, first.GetProperty("type").GetString());
                var state = first.GetProperty("state");
                Assert.Equal("element", state.GetProperty("kind").GetString());
                Assert.Empty(state.GetProperty("children").EnumerateArray());
            }
        );
    }

    [Fact]
    public async Task Canvas_add_produces_canvas_replace_with_child()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/canvas-events"
                );
                using var reader = new StreamReader(stream);

                // Consume the initial canvas.snapshot.
                var initial = await ReadNextSseDataAsync(reader);
                Assert.Equal(CanvasEventTypes.Snapshot, initial.GetProperty("type").GetString());

                // Trigger canvas.add.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("canvas.add(ui.label(\"hi\"))", Encoding.UTF8, "text/plain")
                );

                // Next event is canvas.replace with one child.
                var replace = await ReadNextSseDataAsync(reader);
                Assert.Equal(CanvasEventTypes.Replace, replace.GetProperty("type").GetString());
                var children = replace.GetProperty("state").GetProperty("children");
                Assert.Single(children.EnumerateArray());
            }
        );
    }

    // -------------------------------------------------------------------------
    // SSE response headers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Canvas_events_response_has_correct_headers()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.GetAsync(
                    prefix + $"sessions/{sessionId}/canvas-events",
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

    [Fact]
    public async Task Timeline_events_response_has_correct_headers()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.GetAsync(
                    prefix + $"sessions/{sessionId}/timeline-events",
                    HttpCompletionOption.ResponseHeadersRead
                );
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType!.ToString();
                Assert.Contains(
                    "text/event-stream",
                    contentType,
                    StringComparison.OrdinalIgnoreCase
                );

                Assert.True(
                    response.Headers.TryGetValues("Cache-Control", out var cc)
                        && cc.Any(v => v.Contains("no-cache", StringComparison.OrdinalIgnoreCase))
                );
            }
        );
    }

    // -------------------------------------------------------------------------
    // Session isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Session_isolation_timeline_dump_in_A_does_not_appear_in_B()
    {
        await RunAsync(
            async (client, prefix) =>
            {
                var sessionA = await CreateSessionAsync(client, prefix);
                var sessionB = await CreateSessionAsync(client, prefix);

                // Open timeline streams for both sessions.
                await using var streamA = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionA}/timeline-events"
                );
                await using var streamB = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionB}/timeline-events"
                );
                using var readerA = new StreamReader(streamA);
                using var readerB = new StreamReader(streamB);

                // Consume initial resets.
                var snapA = await ReadNextSseDataAsync(readerA);
                Assert.Equal(TimelineEventTypes.Reset, snapA.GetProperty("type").GetString());
                var snapB = await ReadNextSseDataAsync(readerB);
                Assert.Equal(TimelineEventTypes.Reset, snapB.GetProperty("type").GetString());

                // Dump in session A.
                await client.PostAsync(
                    prefix + $"sessions/{sessionA}/eval",
                    new StringContent("dump(\"a\")", Encoding.UTF8, "text/plain")
                );

                // Session A should receive a timeline.append.
                var appendA = await ReadNextSseDataAsync(readerA);
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
                    prefix + $"sessions/{sessionA}/canvas-events"
                );
                await using var streamB = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionB}/canvas-events"
                );
                using var readerA = new StreamReader(streamA);
                using var readerB = new StreamReader(streamB);

                // Consume initial canvas.snapshot events.
                var snapA = await ReadNextSseDataAsync(readerA);
                Assert.Equal(CanvasEventTypes.Snapshot, snapA.GetProperty("type").GetString());
                Assert.Empty(snapA.GetProperty("state").GetProperty("children").EnumerateArray());

                var snapB = await ReadNextSseDataAsync(readerB);
                Assert.Equal(CanvasEventTypes.Snapshot, snapB.GetProperty("type").GetString());
                Assert.Empty(snapB.GetProperty("state").GetProperty("children").EnumerateArray());

                // Add canvas element in session A.
                await client.PostAsync(
                    prefix + $"sessions/{sessionA}/eval",
                    new StringContent("canvas.add(ui.label(\"a\"))", Encoding.UTF8, "text/plain")
                );

                // Session A receives a canvas.replace event.
                var updateA = await ReadNextSseDataAsync(readerA);
                Assert.Equal(CanvasEventTypes.Replace, updateA.GetProperty("type").GetString());
                Assert.Single(
                    updateA.GetProperty("state").GetProperty("children").EnumerateArray()
                );

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

    // -------------------------------------------------------------------------
    // Non-root prefix
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Static assets
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // GET /duetspad-config.js
    // -------------------------------------------------------------------------

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
}
