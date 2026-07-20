using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Tests.TestSupport;
using Duets.Tests.TestSupport;
using HttpHarker;
using Jint;

namespace Duets.Pad.Tests;

/// <summary>
/// HTTP-level integration tests for DuetsPad access control and resource hardening (ADR-49):
/// bearer-token authentication, the session-count cap, and the request-body size cap.
/// </summary>
public sealed class DuetsPadAccessControlTests
{
    // Helpers

    private static Task RunAsync(
        Action<DuetsPadServiceOptions>? extraConfigure,
        Func<HttpClient, string, Task> test
    ) => RunAsync("/", extraConfigure, test);

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
                                    "@font-face{font-family:\"tabler-icons\";src:url(\"./fonts/tabler-icons.woff2?v3.44.0\") format(\"woff2\")}"
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

    /// <summary>
    /// POSTs to <c>/sessions</c>, optionally attaching an <c>Authorization: Bearer</c> header.
    /// </summary>
    private static async Task<HttpResponseMessage> PostSessionsAsync(
        HttpClient client,
        string prefix,
        string? bearerToken = null,
        string body = "{}"
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, prefix + "sessions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        // The request (and its StringContent) must not be disposed until the send completes;
        // awaiting here rather than returning the task directly keeps the `using` alive for that
        // long, avoiding an ObjectDisposedException on the content mid-send.
        var response = await client.SendAsync(request);
        return response;
    }

    /// <summary>
    /// POSTs to <c>/sessions</c> with an empty body, optionally attaching an
    /// <c>Authorization: Bearer</c> header, and returns the sessionId string.
    /// </summary>
    private static async Task<string> CreateSessionAsync(
        HttpClient client,
        string prefix,
        string? bearerToken = null
    )
    {
        using var response = await PostSessionsAsync(client, prefix, bearerToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("sessionId").GetString()!;
    }

    /// <summary>
    /// An <see cref="HttpContent"/> that reports no computable length, forcing the client to send
    /// the body without a <c>Content-Length</c> header (chunked transfer encoding). Mirrors the
    /// server-side code path that once skipped the bounded-read entirely when
    /// <c>ContentLength64 &lt;= 0</c>.
    /// </summary>
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

    // Authentication

    [Fact]
    public async Task Authenticate_with_correct_bearer_token_allows_session_creation()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var response = await PostSessionsAsync(client, prefix, bearerToken: "secret");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(Guid.TryParse(payload.GetProperty("sessionId").GetString(), out _));
            }
        );
    }

    [Fact]
    public async Task Authenticate_with_wrong_token_returns_401()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var response = await PostSessionsAsync(client, prefix, bearerToken: "wrong");
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Authenticate_with_no_header_returns_401()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var response = await PostSessionsAsync(client, prefix);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Authenticate_401_response_has_no_www_authenticate_header()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var response = await PostSessionsAsync(client, prefix);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.False(response.Headers.Contains("WWW-Authenticate"));
            }
        );
    }

    [Fact]
    public async Task Authenticate_does_not_gate_static_index_asset()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var response = await client.GetAsync(prefix);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Authenticate_respects_non_root_mount_boundary()
    {
        await RunAsync(
            "/pad/",
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var indexResponse = await client.GetAsync(prefix + "pad/");
                Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);

                using var gatedResponse = await PostSessionsAsync(client, prefix + "pad/");
                Assert.Equal(HttpStatusCode.Unauthorized, gatedResponse.StatusCode);

                // A path that merely begins with the mount text is outside both the router and the
                // authentication gate. This keeps the gate exactly aligned with routing boundaries.
                using var outsideResponse = await client.PostAsync(
                    prefix + "padding/sessions",
                    new StringContent("{}", Encoding.UTF8, "application/json")
                );
                Assert.Equal(HttpStatusCode.NotFound, outsideResponse.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Authenticate_custom_delegate_receives_expected_credential_and_path()
    {
        string? capturedCredential = null;
        string? capturedPath = null;

        await RunAsync(
            opts =>
                opts.Authenticate = context =>
                {
                    capturedCredential = context.Credential;
                    capturedPath = context.Path;
                    return ValueTask.FromResult(true);
                },
            async (client, prefix) =>
            {
                using var response = await PostSessionsAsync(client, prefix, bearerToken: "abc123");
                response.EnsureSuccessStatusCode();

                Assert.Equal("abc123", capturedCredential);
                Assert.Equal("/sessions", capturedPath);
            }
        );
    }

    [Fact]
    public async Task Authenticate_sse_events_without_token_returns_401()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var createResponse = await PostSessionsAsync(
                    client,
                    prefix,
                    bearerToken: "secret"
                );
                createResponse.EnsureSuccessStatusCode();
                var payload = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
                var sessionId = payload.GetProperty("sessionId").GetString()!;

                using var response = await client.GetAsync(
                    prefix + $"sessions/{sessionId}/events",
                    HttpCompletionOption.ResponseHeadersRead
                );
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task Authenticate_sse_events_with_token_streams_the_initial_burst()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var createResponse = await PostSessionsAsync(
                    client,
                    prefix,
                    bearerToken: "secret"
                );
                createResponse.EnsureSuccessStatusCode();
                var payload = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
                var sessionId = payload.GetProperty("sessionId").GetString()!;

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    prefix + $"sessions/{sessionId}/events"
                );
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead
                );
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                // Read the first data event of the initial burst to prove the authenticated
                // stream is actually flowing, not merely accepted.
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
                string? dataLine = null;
                for (var i = 0; i < 50 && dataLine is null; i++)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                    {
                        break;
                    }

                    if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        dataLine = line["data: ".Length..];
                    }
                }

                Assert.NotNull(dataLine);
                using var doc = JsonDocument.Parse(dataLine);
                Assert.True(doc.RootElement.TryGetProperty("type", out _));
            }
        );
    }

    // All-route gating

    /// <summary>
    /// Proves every session-API route is gated by <see cref="DuetsPadServiceOptions.Authenticate"/>,
    /// not just the two routes the other tests happen to exercise. The gate is a middleware
    /// registered in front of the router (ADR-49), so this must hold regardless of which handler a
    /// route dispatches to; a route added later without updating its own auth check must still be
    /// rejected here.
    /// </summary>
    [Theory]
    [InlineData("POST", "sessions")]
    [InlineData("DELETE", "sessions/{sessionId}")]
    [InlineData("POST", "sessions/{sessionId}/eval")]
    [InlineData("POST", "sessions/{sessionId}/complete")]
    [InlineData("GET", "sessions/{sessionId}/canvas")]
    [InlineData("POST", "sessions/{sessionId}/interactions/{guid}/invoke")]
    [InlineData("POST", "sessions/{sessionId}/fields/{guid}/commit")]
    [InlineData("GET", "sessions/{sessionId}/events")]
    public async Task Authenticate_gates_every_session_api_route(string method, string pathTemplate)
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix, bearerToken: "secret");
                var path = pathTemplate
                    .Replace("{sessionId}", sessionId)
                    .Replace("{guid}", Guid.NewGuid().ToString());

                using var request = new HttpRequestMessage(new HttpMethod(method), prefix + path);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead
                );

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        );
    }

    // Bearer credential parsing

    [Fact]
    public async Task Authenticate_lowercase_bearer_scheme_is_accepted()
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, prefix + "sessions")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("Authorization", "bearer secret");

                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        );
    }

    [Theory]
    [InlineData("Basic xyz")]
    [InlineData("Bearer")]
    [InlineData("")]
    public async Task Authenticate_malformed_authorization_header_returns_401_never_a_bypass(
        string headerValue
    )
    {
        await RunAsync(
            opts => opts.Authenticate = DuetsPadAuthenticator.Token("secret"),
            async (client, prefix) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, prefix + "sessions")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("Authorization", headerValue);

                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        );
    }

    [Fact]
    public void Token_throws_ArgumentException_for_empty_token()
    {
        Assert.Throws<ArgumentException>(() => DuetsPadAuthenticator.Token(""));
    }

    [Fact]
    public void Token_throws_ArgumentException_for_null_token()
    {
        Assert.Throws<ArgumentException>(() => DuetsPadAuthenticator.Token(null!));
    }

    // MaxSessions

    [Fact]
    public async Task MaxSessions_cap_returns_429_for_new_session_but_reconnect_still_succeeds()
    {
        await RunAsync(
            opts => opts.MaxSessions = 1,
            async (client, prefix) =>
            {
                var firstId = await CreateSessionAsync(client, prefix);

                using var secondResponse = await PostSessionsAsync(client, prefix);
                Assert.Equal((HttpStatusCode)429, secondResponse.StatusCode);

                using var reconnectResponse = await PostSessionsAsync(
                    client,
                    prefix,
                    body: $"{{\"sessionId\":\"{firstId}\"}}"
                );
                Assert.Equal(HttpStatusCode.OK, reconnectResponse.StatusCode);
                var payload = await reconnectResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(firstId, payload.GetProperty("sessionId").GetString());
            }
        );
    }

    /// <summary>
    /// Regression test for the reservation counter (ADR-49): the cap is claimed atomically before
    /// the (asynchronous) session factory runs, so a burst of concurrent creates cannot collectively
    /// overshoot it. A sequential test cannot exercise this — every request would observe the
    /// previous one's already-committed count — so this fires a burst of concurrent creates via
    /// <see cref="Task.WhenAll"/> and asserts the admitted count is exactly the cap.
    /// </summary>
    /// <remarks>
    /// The burst size is deliberately modest (half a dozen, not dozens) rather than the largest
    /// number that would still stress the reservation logic: a burst of simultaneous new TCP
    /// connections to the same loopback listener can outrun the test host's accept backlog and
    /// surface as spurious connection-refused failures unrelated to the cap logic under test. A
    /// burst comfortably above the cap is sufficient to prove no overshoot.
    /// </remarks>
    [Fact]
    public async Task MaxSessions_concurrent_creates_admit_exactly_the_cap_and_reject_the_rest()
    {
        const int cap = 4;
        const int burst = 6;

        await RunAsync(
            opts => opts.MaxSessions = cap,
            async (client, prefix) =>
            {
                var responses = await Task.WhenAll(
                    Enumerable.Range(0, burst).Select(_ => PostSessionsAsync(client, prefix))
                );
                try
                {
                    var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
                    var rejectedCount = responses.Count(r => r.StatusCode == (HttpStatusCode)429);

                    Assert.Equal(cap, okCount);
                    Assert.Equal(burst - cap, rejectedCount);
                }
                finally
                {
                    foreach (var response in responses)
                    {
                        response.Dispose();
                    }
                }
            }
        );
    }

    [Fact]
    public async Task MaxSessions_after_deleting_a_session_a_new_create_succeeds_again_at_the_cap()
    {
        await RunAsync(
            opts => opts.MaxSessions = 4,
            async (client, prefix) =>
            {
                var firstId = await CreateSessionAsync(client, prefix);
                await CreateSessionAsync(client, prefix);
                await CreateSessionAsync(client, prefix);
                await CreateSessionAsync(client, prefix);

                using var overCapResponse = await PostSessionsAsync(client, prefix);
                Assert.Equal((HttpStatusCode)429, overCapResponse.StatusCode);

                using var deleteResponse = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, prefix + $"sessions/{firstId}")
                );
                deleteResponse.EnsureSuccessStatusCode();

                using var afterDeleteResponse = await PostSessionsAsync(client, prefix);
                Assert.Equal(HttpStatusCode.OK, afterDeleteResponse.StatusCode);
            }
        );
    }

    // MaxRequestBodyBytes

    [Fact]
    public async Task MaxRequestBodyBytes_oversized_eval_body_returns_413()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 64,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent(new string('x', 1024), Encoding.UTF8, "text/plain")
                );

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task MaxRequestBodyBytes_oversized_body_413_is_readable_by_the_client()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 64,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                // A body far larger than the socket buffers: without the server-side bounded
                // drain, HttpListener would reset the connection on close (unread request data
                // pending) and this read of the response would fail with a network error
                // instead of observing the 413.
                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent(new string('x', 512 * 1024), Encoding.UTF8, "text/plain")
                );

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(payload.GetProperty("ok").GetBoolean());
            }
        );
    }

    [Fact]
    public async Task MaxRequestBodyBytes_body_of_exactly_the_limit_succeeds()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 64,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent(new string('1', 64), Encoding.UTF8, "text/plain")
                );

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task MaxRequestBodyBytes_body_one_byte_over_the_limit_returns_413()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 64,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new StringContent(new string('1', 65), Encoding.UTF8, "text/plain")
                );

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task MaxRequestBodyBytes_bounded_reader_preserves_utf8_bom_detection()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 1024,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var json = JsonSerializer.Serialize(new { sessionId });
                var preamble = Encoding.UTF8.GetPreamble();
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var body = new byte[preamble.Length + jsonBytes.Length];
                preamble.CopyTo(body, 0);
                jsonBytes.CopyTo(body, preamble.Length);

                using var content = new ByteArrayContent(body);
                content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
                using var response = await client.PostAsync(prefix + "sessions", content);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(sessionId, payload.GetProperty("sessionId").GetString());
            }
        );
    }

    /// <summary>
    /// Regression test: the bounded-body reader used to be skipped entirely whenever
    /// <c>ContentLength64 &lt;= 0</c> (the value <see cref="System.Net.HttpListenerRequest"/> reports
    /// for a chunked/unknown-length body), letting an oversized chunked upload bypass
    /// <see cref="DuetsPadServiceOptions.MaxRequestBodyBytes"/> entirely.
    /// </summary>
    [Fact]
    public async Task MaxRequestBodyBytes_unknown_length_oversized_eval_body_returns_413()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 64,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/eval",
                    new UnknownLengthStringContent(new string('x', 1024))
                );

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task MaxRequestBodyBytes_oversized_field_commit_body_returns_413()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 64,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var fieldId = Guid.NewGuid();

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/fields/{fieldId}/commit",
                    new StringContent(new string('x', 1024), Encoding.UTF8, "text/plain")
                );

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
            }
        );
    }

    [Fact]
    public async Task MaxRequestBodyBytes_oversized_interaction_invoke_body_returns_413()
    {
        await RunAsync(
            opts => opts.MaxRequestBodyBytes = 64,
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var handlerId = Guid.NewGuid();

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/interactions/{handlerId}/invoke",
                    new StringContent(new string('x', 1024), Encoding.UTF8, "text/plain")
                );

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
            }
        );
    }

    /// <summary>
    /// <c>/complete</c> uses <c>Math.Min(TaggedTemplateCompletionMaxRequestBytes,
    /// MaxRequestBodyBytes)</c>, so the global cap is never overridden by a looser endpoint-specific
    /// one. The endpoint preserves its completion-specific JSON error shape while returning the
    /// global <c>413</c> status required for every oversized POST body.
    /// </summary>
    [Fact]
    public async Task MaxRequestBodyBytes_complete_endpoint_is_still_bounded_by_the_global_cap()
    {
        await RunAsync(
            opts =>
            {
                opts.MaxRequestBodyBytes = 64;
                opts.TaggedTemplateCompletionMaxRequestBytes = 1024 * 1024;
            },
            async (client, prefix) =>
            {
                var sessionId = await CreateSessionAsync(client, prefix);
                var body = JsonSerializer.Serialize(
                    new { tag = "sql", textBeforeCaret = new string('a', 4096) }
                );

                using var response = await client.PostAsync(
                    prefix + $"sessions/{sessionId}/complete",
                    new StringContent(body, Encoding.UTF8, "application/json")
                );

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(payload.GetProperty("ok").GetBoolean());
            }
        );
    }

    // Options validation

    [Fact]
    public void UseDuetsPad_throws_ArgumentOutOfRangeException_when_MaxSessions_is_zero()
    {
        using var server = new HttpServer("http://127.0.0.1:0/");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            server.UseDuetsPad("/", opts => opts.MaxSessions = 0)
        );
    }

    [Fact]
    public void UseDuetsPad_does_not_throw_when_MaxSessions_is_null()
    {
        using var server = new HttpServer("http://127.0.0.1:0/");
        using var service = server.UseDuetsPad("/", opts => opts.MaxSessions = null);
        Assert.NotNull(service);
    }

    [Fact]
    public void UseDuetsPad_throws_ArgumentOutOfRangeException_when_MaxActiveDialogs_is_zero()
    {
        using var server = new HttpServer("http://127.0.0.1:0/");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            server.UseDuetsPad("/", opts => opts.MaxActiveDialogs = 0)
        );
    }

    [Fact]
    public void UseDuetsPad_throws_ArgumentOutOfRangeException_when_MaxRequestBodyBytes_is_zero()
    {
        using var server = new HttpServer("http://127.0.0.1:0/");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            server.UseDuetsPad("/", opts => opts.MaxRequestBodyBytes = 0)
        );
    }
}
