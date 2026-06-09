using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;
using HttpHarker;
using Jint;

namespace Duets.Tests.Pad;

/// <summary>
/// Tests for object-renderer configuration at both the session-constructor level and the
/// service-options level (<see cref="DuetsPadServiceOptions.ObjectRenderers"/>).
/// </summary>
public sealed class ObjectRenderersOptionsTests
{
    // -------------------------------------------------------------------------
    // Helper: sentinel renderer
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders a specific string value to a fixed text output; all other values are rejected.
    /// </summary>
    private sealed class SentinelRenderer(string match, string output) : IObjectRenderer
    {
        public bool CanRender(object value) => value is string s && s == match;

        public IRenderNode Render(object value, RenderContext context) => new Text(output);
    }

    // -------------------------------------------------------------------------
    // Helper: create a real Jint-backed DuetsSession
    // -------------------------------------------------------------------------

    private static Task<DuetsSession> CreateDuetsSessionAsync() =>
        DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));

    // -------------------------------------------------------------------------
    // Session-constructor-level tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Session_ctor_renderer_is_used_by_dump()
    {
        var duetsSession = await CreateDuetsSessionAsync();
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            [new SentinelRenderer("SENTINEL", "RENDERED")]
        );

        var result = await session.EvaluateAsync("dump(\"SENTINEL\")");

        Assert.True(result.Ok);
        var entry = Assert.Single(session.Timeline);
        Assert.Equal("dump", entry.Reason);
        var body = Assert.IsType<Text>(entry.Body);
        Assert.Equal("RENDERED", body.Value);
    }

    [Fact]
    public async Task Session_ctor_renderer_is_used_by_canvas_add()
    {
        var duetsSession = await CreateDuetsSessionAsync();
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            [new SentinelRenderer("SENTINEL", "RENDERED")]
        );

        await session.EvaluateAsync("canvas.add(\"SENTINEL\")");

        var child = Assert.Single(session.Canvas.Root.Children);
        var text = Assert.IsType<Text>(child);
        Assert.Equal("RENDERED", text.Value);
    }

    [Fact]
    public async Task Session_ctor_last_wins_when_multiple_renderers_can_handle_value()
    {
        var duetsSession = await CreateDuetsSessionAsync();
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            [new SentinelRenderer("SENTINEL", "FIRST"), new SentinelRenderer("SENTINEL", "SECOND")]
        );

        await session.EvaluateAsync("dump(\"SENTINEL\")");

        var entry = Assert.Single(session.Timeline);
        var body = Assert.IsType<Text>(entry.Body);
        Assert.Equal("SECOND", body.Value);
    }

    [Fact]
    public async Task Session_ctor_no_renderers_yields_plain_text()
    {
        var duetsSession = await CreateDuetsSessionAsync();
        using var session = new DuetsPadSession(Guid.NewGuid(), duetsSession);

        await session.EvaluateAsync("dump(\"plain\")");

        var entry = Assert.Single(session.Timeline);
        var body = Assert.IsType<Text>(entry.Body);
        Assert.Equal("plain", body.Value);
    }

    [Fact]
    public async Task Session_ctor_snapshots_renderer_list_so_post_construction_mutation_is_isolated()
    {
        var duetsSession = await CreateDuetsSessionAsync();
        var renderers = new List<IObjectRenderer> { new SentinelRenderer("SENTINEL", "ORIGINAL") };
        using var session = new DuetsPadSession(Guid.NewGuid(), duetsSession, renderers);

        // Mutate the source list after session construction.
        renderers.Clear();

        // The session must still use the originally-registered renderer.
        var result = await session.EvaluateAsync("dump(\"SENTINEL\")");

        Assert.True(result.Ok);
        var entry = Assert.Single(session.Timeline);
        Assert.Equal("dump", entry.Reason);
        var body = Assert.IsType<Text>(entry.Body);
        Assert.Equal("ORIGINAL", body.Value);
    }

    // -------------------------------------------------------------------------
    // Service-options-level tests (via DuetsPadServiceOptions.ObjectRenderers)
    // -------------------------------------------------------------------------

    private static Task RunAsync(
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
                        "/",
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

    [Fact]
    public async Task ObjectRenderers_dump_uses_registered_renderer()
    {
        await RunAsync(
            opts => opts.ObjectRenderers = [new SentinelRenderer("SENTINEL", "RENDERED")],
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

                // Dump a value that the sentinel renderer handles.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("dump(\"SENTINEL\")", Encoding.UTF8, "text/plain")
                );

                // The timeline.append entry body must show the renderer's output, not the raw value.
                var append = await ReadNextSseDataAsync(reader);
                Assert.Equal(TimelineEventTypes.Append, append.GetProperty("type").GetString());

                var entry = append.GetProperty("entry");
                Assert.Equal("dump", entry.GetProperty("reason").GetString());

                var body = entry.GetProperty("body");
                Assert.Equal("text", body.GetProperty("kind").GetString());
                Assert.Equal("RENDERED", body.GetProperty("value").GetString());
            }
        );
    }

    [Fact]
    public async Task ObjectRenderers_canvas_uses_registered_renderer()
    {
        await RunAsync(
            opts => opts.ObjectRenderers = [new SentinelRenderer("SENTINEL", "RENDERED")],
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

                // Add a value that the sentinel renderer handles.
                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("canvas.add(\"SENTINEL\")", Encoding.UTF8, "text/plain")
                );

                // The canvas.replace root's first child must be the renderer's text node.
                var replace = await ReadNextSseDataAsync(reader);
                Assert.Equal(CanvasEventTypes.Replace, replace.GetProperty("type").GetString());

                var children = replace.GetProperty("state").GetProperty("children");
                var childArray = children.EnumerateArray().ToList();
                Assert.Single(childArray);

                var child = childArray[0];
                Assert.Equal("text", child.GetProperty("kind").GetString());
                Assert.Equal("RENDERED", child.GetProperty("value").GetString());
            }
        );
    }

    [Fact]
    public async Task ObjectRenderers_last_wins_when_multiple_renderers_can_handle_value()
    {
        await RunAsync(
            opts =>
                opts.ObjectRenderers = [
                    new SentinelRenderer("SENTINEL", "FIRST"),
                    new SentinelRenderer("SENTINEL", "SECOND"),
                ],
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/timeline-events"
                );
                using var reader = new StreamReader(stream);

                var reset = await ReadNextSseDataAsync(reader);
                Assert.Equal(TimelineEventTypes.Reset, reset.GetProperty("type").GetString());

                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("dump(\"SENTINEL\")", Encoding.UTF8, "text/plain")
                );

                var append = await ReadNextSseDataAsync(reader);
                Assert.Equal(TimelineEventTypes.Append, append.GetProperty("type").GetString());

                var body = append.GetProperty("entry").GetProperty("body");
                Assert.Equal("text", body.GetProperty("kind").GetString());
                Assert.Equal("SECOND", body.GetProperty("value").GetString());
            }
        );
    }

    [Fact]
    public async Task ObjectRenderers_default_behavior_preserved_when_options_are_empty()
    {
        // Default options (empty ObjectRenderers): dump a plain string; default renderer returns it as-is.
        await RunAsync(
            null,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                await using var stream = await client.GetStreamAsync(
                    prefix + $"sessions/{sessionId}/timeline-events"
                );
                using var reader = new StreamReader(stream);

                var reset = await ReadNextSseDataAsync(reader);
                Assert.Equal(TimelineEventTypes.Reset, reset.GetProperty("type").GetString());

                await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent("dump(\"plain\")", Encoding.UTF8, "text/plain")
                );

                var append = await ReadNextSseDataAsync(reader);
                Assert.Equal(TimelineEventTypes.Append, append.GetProperty("type").GetString());

                var body = append.GetProperty("entry").GetProperty("body");
                Assert.Equal("text", body.GetProperty("kind").GetString());
                Assert.Equal("plain", body.GetProperty("value").GetString());
            }
        );
    }
}
