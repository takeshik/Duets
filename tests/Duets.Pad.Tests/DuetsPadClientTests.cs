using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Duets.Completions;
using Duets.Jint;
using Duets.Pad.Tests.TestSupport;
using Duets.Tests.TestSupport;
using HttpHarker;
using Jint;

namespace Duets.Pad.Tests;

/// <summary>Integration tests for the public existing-session DuetsPad client.</summary>
public sealed class DuetsPadClientTests
{
    private static Task RunAsync(
        Action<DuetsPadServiceOptions>? configure,
        Func<HttpClient, string, Task> test
    )
    {
        return DuetsServerFixture.RunAsync(
            server =>
                server
                    .UseContentTypeDetection()
                    .UseDuetsPad(
                        "/pad/",
                        options =>
                        {
                            options.SessionFactory = () =>
                                JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                            options.MonacoLoader = AssetSources.From(_ =>
                                Task.FromResult("// monaco")
                            );
                            options.TablerCss = AssetSources.From(_ =>
                                Task.FromResult("/* tabler */")
                            );
                            options.TablerIconsCss = AssetSources.From(_ =>
                                Task.FromResult("/* icons */")
                            );
                            options.TablerIconsFont = AssetSources.FromBytes(_ =>
                                Task.FromResult("wOF2"u8.ToArray())
                            );
                            options.KeepAliveInterval = TimeSpan.FromSeconds(60);
                            configure?.Invoke(options);
                        }
                    ),
            test
        );
    }

    private static async Task<string> CreateSessionAsync(
        HttpClient client,
        string serverPrefix,
        string? bearerCredential = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, serverPrefix + "pad/sessions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        if (bearerCredential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                bearerCredential
            );
        }

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("sessionId").GetString()!;
    }

    private static string? FindAttribute(JsonNode? node, string attributeName)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        if (
            obj["attributes"] is JsonObject attributes
            && attributes[attributeName] is JsonValue value
            && value.TryGetValue<string>(out var found)
        )
        {
            return found;
        }

        if (obj["children"] is JsonArray children)
        {
            foreach (var child in children)
            {
                if (FindAttribute(child, attributeName) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    [Fact]
    public async Task Client_operates_one_known_session_without_an_attach_or_create_request()
    {
        await RunAsync(
            configure: null,
            async (http, serverPrefix) =>
            {
                var sessionId = await CreateSessionAsync(http, serverPrefix);
                using var client = new DuetsPadClient(
                    http,
                    new Uri(serverPrefix + "pad/"),
                    sessionId
                );

                var write = await client.ReplaceEditorTextAsync("shared text");
                var read = await client.GetEditorTextAsync();
                var eval = await client.EvaluateAsync("pad.editorText + ':' + pad.sessionId");

                Assert.True(write.Ok, write.Error);
                Assert.True(read.Ok, read.Error);
                Assert.Equal("shared text", read.Value!.Text);
                Assert.True(eval.Ok, eval.Error);
                Assert.Equal($"shared text:{sessionId}", eval.Value!.Result);
            }
        );
    }

    [Fact]
    public async Task Unknown_session_operation_fails_without_allocating_a_replacement()
    {
        await RunAsync(
            configure: null,
            async (http, serverPrefix) =>
            {
                var unknownId = Guid.NewGuid().ToString();
                using var client = new DuetsPadClient(
                    http,
                    new Uri(serverPrefix + "pad/"),
                    unknownId
                );

                var result = await client.GetEditorTextAsync();
                var events = await client.OpenEventsAsync();

                Assert.False(result.Ok);
                Assert.Equal("Unknown session.", result.Error);
                Assert.Equal(unknownId, result.SessionId);
                Assert.Null(result.Value);
                Assert.False(events.Ok);
                Assert.Equal("Unknown session.", events.Error);
                Assert.Null(events.Value);
            }
        );
    }

    [Fact]
    public async Task Client_and_stream_disposal_do_not_delete_the_session()
    {
        await RunAsync(
            configure: null,
            async (http, serverPrefix) =>
            {
                var sessionId = await CreateSessionAsync(http, serverPrefix);
                using (
                    var client = new DuetsPadClient(http, new Uri(serverPrefix + "pad/"), sessionId)
                )
                {
                    var opened = await client.OpenEventsAsync();
                    Assert.True(opened.Ok, opened.Error);
                    var read = await opened.Value!.ReadAsync(1, TimeSpan.FromSeconds(2));
                    Assert.Single(read.Records);
                    opened.Value.Dispose();
                }

                using var response = await http.GetAsync(
                    serverPrefix + $"pad/sessions/{sessionId}/editor"
                );
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(payload.GetProperty("ok").GetBoolean());
            }
        );
    }

    [Fact]
    public async Task Editor_replacement_does_not_emit_an_sse_event()
    {
        await RunAsync(
            configure: null,
            async (http, serverPrefix) =>
            {
                var sessionId = await CreateSessionAsync(http, serverPrefix);
                using var client = new DuetsPadClient(
                    http,
                    new Uri(serverPrefix + "pad/"),
                    sessionId
                );
                var opened = await client.OpenEventsAsync();
                Assert.True(opened.Ok, opened.Error);
                using var stream = opened.Value!;
                _ = await stream.ReadAsync(64, TimeSpan.FromMilliseconds(200));

                var replaced = await client.ReplaceEditorTextAsync("pull only");
                var afterReplace = await stream.ReadAsync(1, TimeSpan.FromMilliseconds(100));

                Assert.True(replaced.Ok, replaced.Error);
                Assert.True(afterReplace.TimedOut);
                Assert.Empty(afterReplace.Records);
            }
        );
    }

    [Fact]
    public async Task Client_covers_canvas_field_interaction_and_explicit_deletion()
    {
        await RunAsync(
            configure: null,
            async (http, serverPrefix) =>
            {
                var sessionId = await CreateSessionAsync(http, serverPrefix);
                using var client = new DuetsPadClient(
                    http,
                    new Uri(serverPrefix + "pad/"),
                    sessionId
                );

                var setup = await client.EvaluateAsync(
                    """
                    var t = ui.textBox({ value: "initial" });
                    canvas.add(ui.stack([t, ui.button("Read", () => dump(t.value))]));
                    """
                );
                Assert.True(setup.Ok, setup.Error);

                var canvas = await client.GetCanvasAsync();
                Assert.True(canvas.Ok, canvas.Error);
                var snapshot = canvas.Value!.Snapshot;
                var fieldId = FindAttribute(snapshot["state"], "data-duetspad-field");
                Assert.NotNull(fieldId);
                var interaction = Assert.IsType<JsonObject>(
                    Assert.Single(Assert.IsType<JsonArray>(snapshot["interactions"]))
                );
                var handlerId = interaction["handlerId"]!.GetValue<string>();

                var field = await client.CommitFieldAsync(fieldId, "headless");
                var invoke = await client.InvokeInteractionAsync(handlerId);
                var read = await client.EvaluateAsync("t.value");
                Assert.True(field.Ok, field.Error);
                Assert.True(invoke.Ok, invoke.Error);
                Assert.Equal("headless", read.Value!.Result);

                var deleted = await client.DeleteSessionAsync();
                var afterDelete = await client.GetEditorTextAsync();
                Assert.True(deleted.Ok, deleted.Error);
                Assert.False(afterDelete.Ok);
                Assert.Equal("Unknown session.", afterDelete.Error);
            }
        );
    }

    [Fact]
    public async Task Client_covers_completion_and_streaming_attachment_transactions()
    {
        await RunAsync(
            options =>
                options.SessionFactory = async () =>
                {
                    var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
                    session.RegisterTaggedTemplate(
                        "path",
                        complete: (_, _) =>
                            new ValueTask<IReadOnlyList<TemplateCompletionItem>>([
                                new TemplateCompletionItem("/tmp/example"),
                            ])
                    );
                    return session;
                },
            async (http, serverPrefix) =>
            {
                var sessionId = await CreateSessionAsync(http, serverPrefix);
                using var client = new DuetsPadClient(
                    http,
                    new Uri(serverPrefix + "pad/"),
                    sessionId
                );

                var completion = await client.CompleteAsync(
                    new DuetsPadCompletionRequest("path", "", "", "", 0)
                );
                Assert.True(completion.Ok, completion.Error);
                Assert.Equal("/tmp/example", Assert.Single(completion.Value!.Items).Label);

                var setup = await client.EvaluateAsync(
                    "var picker = ui.filePicker(); canvas.add(picker);"
                );
                Assert.True(setup.Ok, setup.Error);
                var canvas = await client.GetCanvasAsync();
                var pickerId = FindAttribute(
                    canvas.Value!.Snapshot["state"],
                    "data-duetspad-field"
                );
                Assert.NotNull(pickerId);

                var begin = await client.BeginAttachmentSelectionAsync(
                    pickerId,
                    Guid.NewGuid(),
                    generation: 1,
                    [new DuetsPadAttachmentFile("note.txt", "text/plain", 5)]
                );
                Assert.True(begin.Ok, begin.Error);
                var selection = begin.Value!;
                var file = Assert.Single(selection.Files);
                using var content = new MemoryStream("hello"u8.ToArray());
                var upload = await client.UploadAttachmentFileAsync(
                    pickerId,
                    selection.Token!,
                    file.Id,
                    content,
                    "text/plain"
                );
                var commit = await client.CommitAttachmentSelectionAsync(
                    pickerId,
                    selection.Token!
                );
                var read = await client.EvaluateAsync("picker.files[0].readAllText()");

                Assert.True(upload.Ok, upload.Error);
                Assert.True(commit.Ok, commit.Error);
                Assert.True(read.Ok, read.Error);
                Assert.Equal("hello", read.Value!.Result);
            }
        );
    }

    [Fact]
    public async Task Client_sends_the_same_bearer_credential_for_direct_and_sse_operations()
    {
        await RunAsync(
            options => options.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (http, serverPrefix) =>
            {
                var sessionId = await CreateSessionAsync(http, serverPrefix, "secret");
                using var unauthorized = new DuetsPadClient(
                    http,
                    new Uri(serverPrefix + "pad/"),
                    sessionId
                );
                var denied = await unauthorized.GetEditorTextAsync();
                Assert.False(denied.Ok);
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

                using var authorized = new DuetsPadClient(
                    http,
                    new Uri(serverPrefix + "pad/"),
                    sessionId,
                    "secret"
                );
                var editor = await authorized.GetEditorTextAsync();
                var events = await authorized.OpenEventsAsync();
                Assert.True(editor.Ok, editor.Error);
                Assert.True(events.Ok, events.Error);
                events.Value!.Dispose();
            }
        );
    }

    [Fact]
    public async Task Client_rejects_a_success_response_with_a_missing_operation_payload()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        using var http = new HttpClient(
            new DelegateHandler(
                (_, _) =>
                    Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""{"ok":true,"sessionId":"{{sessionId}}"}""",
                                Encoding.UTF8,
                                "application/json"
                            ),
                        }
                    )
            )
        );
        using var client = new DuetsPadClient(http, new Uri("http://127.0.0.1/pad/"), sessionId);

        var result = await client.GetEditorTextAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal("DuetsPad returned an invalid operation payload.", result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Event_open_cancellation_applies_while_reading_an_error_response_body()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        using var http = new HttpClient(
            new DelegateHandler(
                (_, _) =>
                    Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new DelayedJsonContent(),
                        }
                    )
            )
        );
        using var client = new DuetsPadClient(http, new Uri("http://127.0.0.1/pad/"), sessionId);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.OpenEventsAsync(cancellation.Token)
        );
    }

    [Fact]
    public async Task Attachment_upload_delegates_async_reads_and_preserves_caller_stream_ownership()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var pickerId = Guid.NewGuid().ToString("D");
        var received = Array.Empty<byte>();
        using var http = new HttpClient(
            new DelegateHandler(
                async (request, cancellationToken) =>
                {
                    using var destination = new MemoryStream();
                    await request.Content!.CopyToAsync(destination, cancellationToken);
                    received = destination.ToArray();
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""{"ok":true,"sessionId":"{{sessionId}}","pickerId":"{{pickerId}}","revision":1,"stale":false}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    };
                }
            )
        );
        using var client = new DuetsPadClient(http, new Uri("http://127.0.0.1/pad/"), sessionId);
        var content = new AsyncOnlyReadStream("streamed"u8.ToArray());

        var result = await client.UploadAttachmentFileAsync(
            pickerId,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            content,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.True(result.Ok, result.Error);
        Assert.Equal("streamed"u8.ToArray(), received);
        Assert.False(content.Disposed);
        content.Dispose();
    }

    [Fact]
    public void Public_client_surface_has_no_session_creation_or_discovery_operation()
    {
        var methodNames = typeof(DuetsPadClient)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain("CreateSessionAsync", methodNames);
        Assert.DoesNotContain("ListSessionsAsync", methodNames);
        Assert.DoesNotContain("AttachAsync", methodNames);
    }

    [Theory]
    [InlineData("http://127.0.0.1/pad/?source=test")]
    [InlineData("http://127.0.0.1/pad/#fragment")]
    public void Client_rejects_a_base_uri_with_non_path_components(string baseUri)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DuetsPadClient(new Uri(baseUri), Guid.NewGuid().ToString("D"))
        );

        Assert.Equal("baseUri", exception.ParamName);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }

    private sealed class AsyncOnlyReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content, writable: false);

        public bool Disposed { get; private set; }

        public override bool CanRead => this._inner.CanRead;

        public override bool CanSeek => this._inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => this._inner.Length;

        public override long Position
        {
            get => this._inner.Position;
            set => this._inner.Position = value;
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("Synchronous reads are not supported.");

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => this._inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => this._inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            this._inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Disposed = true;
                this._inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DelayedJsonContent : HttpContent
    {
        public DelayedJsonContent() =>
            this.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            this.SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await stream.WriteAsync("{}"u8.ToArray(), cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
