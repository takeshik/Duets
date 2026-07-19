using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Duets.Pad;
using Duets.Pad.Tests.TestSupport;
using Duets.Tests.TestSupport;
using HttpHarker;

namespace Duets.Pad.Tests;

/// <summary>HTTP-level coverage for the transactional attachment protocol (ADR-50).</summary>
public sealed class DuetsPadAttachmentHttpTests
{
    private static Task RunAsync(
        Action<DuetsPadServiceOptions>? configure,
        Func<HttpClient, string, Task> test
    ) =>
        DuetsServerFixture.RunAsync(
            server =>
                server.UseDuetsPad(
                    "/",
                    options =>
                    {
                        options.SessionFactory = () => JintTestRuntime.CreateSessionAsync();
                        options.MonacoLoader = AssetSources.From(_ => Task.FromResult("// monaco"));
                        options.TablerCss = AssetSources.From(_ => Task.FromResult("/* tabler */"));
                        options.TablerIconsCss = AssetSources.From(_ => Task.FromResult(""));
                        options.TablerIconsFont = AssetSources.FromBytes(_ =>
                            Task.FromResult(Array.Empty<byte>())
                        );
                        options.KeepAliveInterval = TimeSpan.FromSeconds(60);
                        configure?.Invoke(options);
                    }
                ),
            test
        );

    private static async Task<string> CreateSessionAsync(HttpClient client, string prefix)
    {
        using var response = await client.PostAsync(
            prefix + "sessions",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        return payload.GetProperty("sessionId").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        return payload;
    }

    [Fact]
    public async Task Upload_commit_and_invoke_precondition_round_trip_over_http()
    {
        await RunAsync(
            null,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var sessionRoot = prefix + $"sessions/{sessionId}";
                using (
                    var eval = await client.PostAsync(
                        sessionRoot + "/eval",
                        new StringContent(
                            "var picker = ui.filePicker(); var invoked = 0; canvas.add(ui.stack([picker, ui.button('Run', () => { invoked++; })]));",
                            Encoding.UTF8,
                            "text/plain"
                        ),
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    eval.EnsureSuccessStatusCode();
                }

                JsonElement canvas;
                using (
                    var response = await client.GetAsync(
                        sessionRoot + "/canvas",
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    response.EnsureSuccessStatusCode();
                    canvas = await ReadJsonAsync(response);
                }

                var pickerNode = canvas
                    .GetProperty("state")
                    .GetProperty("children")[0]
                    .GetProperty("children")[0];
                var pickerId = pickerNode
                    .GetProperty("attributes")
                    .GetProperty("data-duetspad-field")
                    .GetString()!;
                var handlerId = canvas
                    .GetProperty("interactions")[0]
                    .GetProperty("handlerId")
                    .GetString()!;
                var selections = sessionRoot + $"/attachments/{pickerId}/selections";

                var attachmentClientId = Guid.NewGuid();
                JsonElement begin;
                using (
                    var response = await client.PostAsJsonAsync(
                        selections,
                        new
                        {
                            clientId = attachmentClientId,
                            generation = 2,
                            files = new[]
                            {
                                new
                                {
                                    name = "hello.txt",
                                    contentType = "text/plain",
                                    size = 5,
                                },
                            },
                        },
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    response.EnsureSuccessStatusCode();
                    begin = await ReadJsonAsync(response);
                }

                var token = begin.GetProperty("token").GetString()!;
                var revision = begin.GetProperty("revision").GetInt64();
                var fileId = begin.GetProperty("files")[0].GetProperty("id").GetString()!;
                using (
                    var delayedOlder = await client.PostAsJsonAsync(
                        selections,
                        new
                        {
                            clientId = attachmentClientId,
                            generation = 1,
                            files = new[]
                            {
                                new
                                {
                                    name = "old.txt",
                                    contentType = "text/plain",
                                    size = 3,
                                },
                            },
                        },
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    Assert.Equal(System.Net.HttpStatusCode.Conflict, delayedOlder.StatusCode);
                    Assert.Contains(
                        "superseded",
                        (await ReadJsonAsync(delayedOlder)).GetProperty("error").GetString()
                    );
                }

                var invokeUrl = sessionRoot + $"/interactions/{handlerId}/invoke";
                using (
                    var response = await client.PostAsJsonAsync(
                        invokeUrl,
                        new
                        {
                            attachments = new Dictionary<string, long> { [pickerId] = revision },
                        },
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    var conflict = await ReadJsonAsync(response);
                    Assert.False(conflict.GetProperty("ok").GetBoolean());
                    Assert.True(conflict.GetProperty("attachmentConflict").GetBoolean());
                }

                using (
                    var response = await client.PostAsync(
                        selections + $"/{token}/files/{fileId}",
                        new ByteArrayContent("hello"u8.ToArray()),
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    response.EnsureSuccessStatusCode();
                    Assert.True((await ReadJsonAsync(response)).GetProperty("ok").GetBoolean());
                }

                using (
                    var response = await client.PostAsync(
                        selections + $"/{token}/commit",
                        content: null,
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    response.EnsureSuccessStatusCode();
                    Assert.True((await ReadJsonAsync(response)).GetProperty("ok").GetBoolean());
                }

                using (
                    var response = await client.PostAsJsonAsync(
                        invokeUrl,
                        new
                        {
                            attachments = new Dictionary<string, long> { [pickerId] = revision },
                        },
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    response.EnsureSuccessStatusCode();
                    Assert.True((await ReadJsonAsync(response)).GetProperty("ok").GetBoolean());
                }

                using var verify = await client.PostAsync(
                    sessionRoot + "/eval",
                    new StringContent(
                        "invoked + ':' + picker.files[0].name",
                        Encoding.UTF8,
                        "text/plain"
                    ),
                    TestContext.Current.CancellationToken
                );
                verify.EnsureSuccessStatusCode();
                Assert.Equal(
                    "1:hello.txt",
                    (await ReadJsonAsync(verify)).GetProperty("result").GetString()
                );
            }
        );
    }

    [Fact]
    public async Task Failed_begin_can_be_cancelled_after_reload_by_expected_revision()
    {
        await RunAsync(
            options =>
            {
                options.MaxAttachmentBytesPerFile = 2;
                options.MaxAttachmentBytesPerSession = 4;
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var root = prefix + $"sessions/{sessionId}";
                using (
                    var eval = await client.PostAsync(
                        root + "/eval",
                        new StringContent(
                            "var picker = ui.filePicker({ disabled: true }); canvas.add(picker);",
                            Encoding.UTF8,
                            "text/plain"
                        ),
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    eval.EnsureSuccessStatusCode();
                }

                JsonElement canvas;
                using (
                    var response = await client.GetAsync(
                        root + "/canvas",
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    canvas = await ReadJsonAsync(response);
                }

                var pickerId = canvas
                    .GetProperty("state")
                    .GetProperty("children")[0]
                    .GetProperty("attributes")
                    .GetProperty("data-duetspad-field")
                    .GetString()!;
                var selections = root + $"/attachments/{pickerId}/selections";
                using var begin = await client.PostAsJsonAsync(
                    selections,
                    new
                    {
                        clientId = Guid.NewGuid(),
                        generation = 1,
                        files = new[]
                        {
                            new
                            {
                                name = "large.bin",
                                contentType = "application/octet-stream",
                                size = 3,
                            },
                        },
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.Equal(System.Net.HttpStatusCode.RequestEntityTooLarge, begin.StatusCode);
                var failed = await ReadJsonAsync(begin);
                Assert.False(failed.GetProperty("ok").GetBoolean());
                var revision = failed.GetProperty("revision").GetInt64();

                using (
                    var response = await client.GetAsync(
                        root + "/canvas",
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    var failedCanvas = await ReadJsonAsync(response);
                    var failedPicker = failedCanvas.GetProperty("state").GetProperty("children")[0];
                    Assert.Equal(
                        "failed",
                        failedPicker
                            .GetProperty("attributes")
                            .GetProperty("data-duetspad-attachment-status")
                            .GetString()
                    );
                    Assert.Contains("data-duetspad-attachment-cancel", failedPicker.GetRawText());
                    var cancelButton = failedPicker
                        .GetProperty("children")
                        .EnumerateArray()
                        .Single(child =>
                            child
                                .GetProperty("attributes")
                                .TryGetProperty("data-duetspad-attachment-cancel", out _)
                        );
                    Assert.False(
                        cancelButton.GetProperty("attributes").TryGetProperty("disabled", out _)
                    );
                }

                using (
                    var stale = await client.DeleteAsync(
                        selections + $"/failed?revision={revision + 1}",
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
                }

                using (
                    var cancel = await client.DeleteAsync(
                        selections + $"/failed?revision={revision}",
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    cancel.EnsureSuccessStatusCode();
                    Assert.True((await ReadJsonAsync(cancel)).GetProperty("ok").GetBoolean());
                }

                using (
                    var response = await client.GetAsync(
                        root + "/canvas",
                        TestContext.Current.CancellationToken
                    )
                )
                {
                    var stableCanvas = await ReadJsonAsync(response);
                    var stablePicker = stableCanvas.GetProperty("state").GetProperty("children")[0];
                    Assert.Equal(
                        "stable",
                        stablePicker
                            .GetProperty("attributes")
                            .GetProperty("data-duetspad-attachment-status")
                            .GetString()
                    );
                    Assert.DoesNotContain(
                        "data-duetspad-attachment-cancel",
                        stablePicker.GetRawText()
                    );
                }
            }
        );
    }

    [Fact]
    public async Task Browser_client_waits_for_stable_upload_generations_before_invoke()
    {
        await RunAsync(
            null,
            async (client, prefix) =>
            {
                var script = await client.GetStringAsync(
                    prefix + "duetspad.js",
                    TestContext.Current.CancellationToken
                );
                var invokeStart = script.IndexOf(
                    "async function invokeInteraction",
                    StringComparison.Ordinal
                );
                Assert.True(invokeStart >= 0);
                var wait = script.IndexOf(
                    "await awaitAttachmentUploads()",
                    invokeStart,
                    StringComparison.Ordinal
                );
                Assert.True(wait > invokeStart);
                var request = script.IndexOf(
                    "const res = await padFetch(",
                    wait,
                    StringComparison.Ordinal
                );

                Assert.True(request > wait);
                Assert.Contains("attachments: collectAttachmentSnapshot()", script);
                Assert.Contains("entry.superseded || entry.reconciledStable", script);
                Assert.Contains("clientId: attachmentClientId", script);
                Assert.Contains("generation: entry.generation", script);
                Assert.Contains("if (attachmentUploadMap.size === 0) return", script);
                Assert.Contains("revision > current.revision", script);
                Assert.Contains(
                    "await waitForAttachmentProjectionChange(50 * 2 ** attempt)",
                    script
                );
                Assert.Contains("entry.controller.abort();", script);
                Assert.Contains("async function cancelFailedAttachmentSelection", script);
                Assert.Contains("/failed?revision=", script);
            }
        );
    }
}
