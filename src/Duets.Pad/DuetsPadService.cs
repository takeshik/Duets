using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Duets.Completions;
using Duets.Pad.Protocol;
using HttpHarker;

namespace Duets.Pad;

/// <summary>
/// Web service that provides the DuetsPad UI and multi-session HTTP/SSE API.
/// Attach to an <see cref="HttpServer"/> via <see cref="DuetsPadServiceExtensions.UseDuetsPad"/>.
/// </summary>
public sealed class DuetsPadService : IDisposable
{
    // How long an oversized request body may be drained before the 413 attempt goes ahead anyway.
    // Short on purpose: the drain is a courtesy to cooperative clients, not a service an abusive
    // uploader may extend at will.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly DuetsPadServiceOptions _options;
    private readonly AssetProvider _assets;
    private readonly SessionRegistry _registry;

    // Route prefix the pad is mounted at, normalized exactly as SimpleRoutingMiddleware normalizes
    // it. The authentication middleware must derive request paths the same way the router does, or
    // the two could disagree about what counts as a session-API path.
    private readonly string _routePrefix;

    internal DuetsPadService(HttpServer server, string root, DuetsPadServiceOptions options)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));
        this._assets = new AssetProvider(options);
        this._registry = new SessionRegistry(options);
        this._routePrefix = root.TrimEnd('/');

        server
            // Registered ahead of the router so the gate covers the whole /sessions subtree by
            // path, not route by route: a session-API route added later is authenticated whether or
            // not its author remembers to opt in (ADR-49).
            .Use(this.AuthenticateSessionApiAsync)
            .UseSimpleRouting(
                root,
                routes =>
                    routes
                        .MapGet("/", this._assets.HandleIndexAsync)
                        .MapGet("/duetspad-config.js", this._assets.HandleDuetsPadConfigJsAsync)
                        .MapGet("/duetspad.js", this._assets.HandleDuetsPadJsAsync)
                        .MapGet("/monaco-loader.js", this._assets.HandleMonacoLoaderAsync)
                        .MapGet("/tabler.css", this._assets.HandleTablerCssAsync)
                        .MapGet("/tabler-icons.css", this._assets.HandleTablerIconsCssAsync)
                        .MapGet("/tabler-icons.woff2", this._assets.HandleTablerIconsFontAsync)
                        .MapPost("/sessions", this.HandlePostSessionAsync)
                        .MapDelete("/sessions/{sessionId}", this.HandleDeleteSessionAsync)
                        .MapPost("/sessions/{sessionId}/eval", this.HandleEvalAsync)
                        .MapPost("/sessions/{sessionId}/complete", this.HandleCompleteAsync)
                        .MapGet("/sessions/{sessionId}/canvas", this.HandleCanvasSnapshotAsync)
                        .MapPost(
                            "/sessions/{sessionId}/interactions/{handlerId}/invoke",
                            this.HandleInvokeInteractionAsync
                        )
                        .MapPost(
                            "/sessions/{sessionId}/fields/{fieldId}/commit",
                            this.HandleCommitFieldAsync
                        )
                        .MapGet("/sessions/{sessionId}/events", this.HandleEventsAsync)
            )
            .UseEmbeddedResources(
                typeof(DuetsPadService).Assembly,
                "Duets.Pad.Resources.StaticFiles",
                root
            );
    }

    // POST /sessions/{sessionId}/complete

    private async Task HandleCompleteAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (!this._options.EnableTaggedTemplateCompletions)
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Tagged-template completions are disabled.",
                    ["sessionId"] = sessionId ?? "",
                }.ToJsonString()
            );
            return;
        }

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        // The endpoint-specific cap never overrides the global one: whichever is stricter wins, so
        // configuring a large TaggedTemplateCompletionMaxRequestBytes cannot smuggle a body past
        // MaxRequestBodyBytes (ADR-49).
        var completeMaxBytes = Math.Min(
            this._options.TaggedTemplateCompletionMaxRequestBytes,
            this._options.MaxRequestBodyBytes
        );

        if (ctx.Request.ContentLength64 > completeMaxBytes)
        {
            await this.RespondCompleteBodyTooLargeAsync(ctx, session.Id, completeMaxBytes);
            return;
        }

        var body = await ReadRequestBodyWithinLimitAsync(
            ctx.Request.InputStream,
            ctx.Request.ContentEncoding,
            completeMaxBytes
        );
        if (body is null)
        {
            await this.RespondCompleteBodyTooLargeAsync(ctx, session.Id, completeMaxBytes);
            return;
        }

        CompleteRequest? request;
        try
        {
            request = ParseCompleteRequest(body);
        }
        catch (JsonException)
        {
            await this.RespondCompleteErrorAsync(
                ctx,
                session.Id,
                "Malformed tagged-template completion request."
            );
            return;
        }

        if (request is null || !this.TryBuildCompletionContext(request, out var context))
        {
            await this.RespondCompleteErrorAsync(
                ctx,
                session.Id,
                "Invalid tagged-template completion request."
            );
            return;
        }

        var result = await session.CompleteTaggedTemplateAsync(context);
        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            SerializeCompleteResponse(session.Id, result).ToJsonString()
        );
    }

    /// <inheritdoc/>
    public void Dispose() => this._registry.Dispose();

    // POST /sessions

    private async Task HandlePostSessionAsync(HttpActionContext ctx)
    {
        if (await this.ReadBodyWithinLimitOrRespondAsync(ctx, sessionId: null) is not { } body)
        {
            return;
        }

        Guid? existingId = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (
                    root.TryGetProperty("sessionId", out var sessionIdEl)
                    && sessionIdEl.ValueKind == JsonValueKind.String
                    && Guid.TryParse(sessionIdEl.GetString(), out var parsedId)
                )
                {
                    existingId = parsedId;
                }
            }
            catch (JsonException)
            {
                // Malformed body — treat as if no sessionId was provided.
            }
        }

        if (await this._registry.GetOrCreateSessionAsync(existingId) is not { } created)
        {
            ctx.Response.StatusCode = 429;
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Session limit reached.",
                }.ToJsonString()
            );
            return;
        }

        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            new JsonObject { ["sessionId"] = created.Id.ToString() }.ToJsonString()
        );
    }

    // DELETE /sessions/{sessionId}

    private async Task HandleDeleteSessionAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (Guid.TryParse(sessionId, out var id) && this._registry.TryDeleteSession(id))
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject { ["ok"] = true, ["sessionId"] = id.ToString() }.ToJsonString()
            );
        }
        else
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Unknown session.",
                    ["sessionId"] = sessionId,
                }.ToJsonString()
            );
        }
    }

    // Idle session cleanup

    /// <summary>
    /// Returns the <see cref="DuetsPadSession"/> identified by <paramref name="id"/>, or
    /// <see langword="null"/> if no such session exists. Exposed for testing only.
    /// </summary>
    internal DuetsPadSession? TryGetSession(Guid id) => this._registry.TryGetSession(id);

    /// <summary>
    /// Removes and disposes sessions that have been idle longer than
    /// <see cref="DuetsPadServiceOptions.IdleTimeout"/>. Does nothing when
    /// <see cref="DuetsPadServiceOptions.IdleTimeout"/> is <see langword="null"/> or non-positive.
    /// Called by the background cleanup timer; also directly callable by tests.
    /// </summary>
    internal void RemoveIdleSessions() => this._registry.RemoveIdleSessions();

    // POST /sessions/{sessionId}/eval

    private async Task HandleEvalAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        if (
            await this.ReadBodyWithinLimitOrRespondAsync(ctx, session.Id.ToString()) is not { } code
        )
        {
            return;
        }

        var source = ctx.Request.QueryString["source"];
        var appendResult = string.Equals(source, "immediate", StringComparison.OrdinalIgnoreCase);
        var result = await session.EvaluateAsync(code, appendResult);

        var response = result.Ok
            ? new JsonObject
            {
                ["ok"] = true,
                ["result"] = result.Result,
                ["sessionId"] = session.Id.ToString(),
            }
            : new JsonObject
            {
                ["ok"] = false,
                ["error"] = result.Error,
                ["sessionId"] = session.Id.ToString(),
            };

        await ctx.CloseAsync("application/json; charset=utf-8", response.ToJsonString());
    }

    // GET /sessions/{sessionId}/canvas

    private async Task HandleCanvasSnapshotAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        var canvasName = ctx.Request.QueryString["name"];
        if (string.IsNullOrEmpty(canvasName))
        {
            canvasName = "default";
        }

        if (!session.TryGetCanvasSnapshot(canvasName, out var snapshot))
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Unknown canvas.",
                    ["sessionId"] = session.Id.ToString(),
                    ["name"] = canvasName,
                }.ToJsonString()
            );
            return;
        }

        await ctx.CloseAsync("application/json; charset=utf-8", SseSerializer.Serialize(snapshot));
    }

    private async Task HandleInvokeInteractionAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];
        var handlerId = ctx.Args["handlerId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        if (!Guid.TryParse(handlerId, out var parsedHandlerId))
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Invalid interaction handler id.",
                    ["sessionId"] = session.Id.ToString(),
                    ["handlerId"] = handlerId,
                }.ToJsonString()
            );
            return;
        }

        // Read unconditionally rather than gating on ContentLength64 > 0: a chunked request reports
        // a length of -1, and skipping the read for those would both drop the field snapshot and
        // let the body past MaxRequestBodyBytes entirely. An absent body simply reads as empty,
        // which ParseFieldSnapshot maps to "no snapshot".
        if (
            await this.ReadBodyWithinLimitOrRespondAsync(ctx, session.Id.ToString()) is not { } body
        )
        {
            return;
        }

        var fieldSnapshot = ParseFieldSnapshot(body);

        var result = await session.InvokeInteractionAsync(parsedHandlerId, fieldSnapshot);
        var response = new JsonObject
        {
            ["ok"] = result.Ok,
            ["error"] = result.Error,
            ["stale"] = result.Stale,
            ["sessionId"] = session.Id.ToString(),
            ["handlerId"] = parsedHandlerId.ToString(),
        };

        await ctx.CloseAsync("application/json; charset=utf-8", response.ToJsonString());
    }

    // POST /sessions/{sessionId}/fields/{fieldId}/commit

    /// <summary>
    /// Browser-originated field-value commit (ADR-47): stores the raw request body as the field's
    /// value and updates the authoritative Canvas/Timeline state in place, but never broadcasts —
    /// the committing browser already reflects the value it is sending, so echoing it back would be
    /// redundant (and updating the authoritative state without a broadcast is what lets a later SSE
    /// reconnect see the committed value instead of reverting to the pre-commit projection).
    /// </summary>
    private async Task HandleCommitFieldAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];
        var fieldId = ctx.Args["fieldId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        if (!Guid.TryParse(fieldId, out var parsedFieldId))
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Invalid field id.",
                    ["sessionId"] = session.Id.ToString(),
                }.ToJsonString()
            );
            return;
        }

        if (
            await this.ReadBodyWithinLimitOrRespondAsync(ctx, session.Id.ToString())
            is not { } value
        )
        {
            return;
        }

        await session.CommitFieldValue(parsedFieldId, value);

        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            new JsonObject
            {
                ["ok"] = true,
                ["sessionId"] = session.Id.ToString(),
                ["fieldId"] = parsedFieldId.ToString(),
            }.ToJsonString()
        );
    }

    /// <summary>
    /// Parses the optional <c>{ fields: { fieldId: value } }</c> snapshot from an interaction-invoke
    /// request body (ADR-47). Returns <see langword="null"/> when the body is empty, malformed, or
    /// carries no recognizable field entries — the invoke proceeds without merging a snapshot rather
    /// than failing.
    /// </summary>
    private static IReadOnlyDictionary<Guid, string>? ParseFieldSnapshot(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (
                doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("fields", out var fieldsEl)
                || fieldsEl.ValueKind != JsonValueKind.Object
            )
            {
                return null;
            }

            var snapshot = new Dictionary<Guid, string>();
            foreach (var property in fieldsEl.EnumerateObject())
            {
                if (
                    Guid.TryParse(property.Name, out var fieldId)
                    && property.Value.ValueKind == JsonValueKind.String
                )
                {
                    snapshot[fieldId] = property.Value.GetString() ?? "";
                }
            }

            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // GET /sessions/{sessionId}/events

    private async Task HandleEventsAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        var declarations = session.DuetsSession.Declarations;

        await SseTransport.RunAsync<PadEventMessage>(
            ctx,
            session,
            this._options.KeepAliveInterval,
            setup: writer => session.SubscribeEvents(writer, declarations),
            teardown: session.UnsubscribeEvents,
            formatData: SseSerializer.Serialize
        );
    }

    // Helpers

    /// <summary>
    /// Middleware that gates the whole session API with
    /// <see cref="DuetsPadServiceOptions.Authenticate"/>. Requests outside the <c>/sessions</c>
    /// subtree — the pad page and its static assets — are forwarded untouched, because the page
    /// must load before it can present the token prompt (ADR-49).
    /// </summary>
    private async Task AuthenticateSessionApiAsync(HttpListenerContext context, Func<Task> next)
    {
        if (this._options.Authenticate is not { } authenticate)
        {
            await next();
            return;
        }

        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (!this.IsSessionApiPath(path))
        {
            await next();
            return;
        }

        var credential = ExtractBearerCredential(context.Request.Headers["Authorization"]);
        var authContext = new DuetsPadAuthenticationContext(
            credential,
            path,
            context.Request.RemoteEndPoint
        );

        if (await authenticate(authContext))
        {
            await next();
            return;
        }

        // Deliberately no WWW-Authenticate header: sending one would make browsers pop their
        // native credential prompt on a 401, which ADR-49 avoids in favor of the pad's own
        // in-page token input.
        var response = context.Response;
        response.StatusCode = 401;
        response.ContentType = "application/json; charset=utf-8";
        var payload = Encoding.UTF8.GetBytes(
            new JsonObject { ["ok"] = false, ["error"] = "Unauthorized." }.ToJsonString()
        );
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload);
        response.Close();
    }

    /// <summary>
    /// Reports whether <paramref name="absolutePath"/> addresses the session API (the
    /// <c>/sessions</c> subtree beneath the pad's mount point).
    /// </summary>
    /// <remarks>
    /// The comparison is case-insensitive so the gate can never be narrower than the router that
    /// follows it: were the router ever to match a route case-insensitively, a case-varied path
    /// would still be authenticated here rather than slipping through ungated.
    /// </remarks>
    private bool IsSessionApiPath(string absolutePath)
    {
        if (
            this._routePrefix.Length > 0
            && !absolutePath.StartsWith(this._routePrefix, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var relative = absolutePath[this._routePrefix.Length..];
        const string sessions = "/sessions";
        return relative.StartsWith(sessions, StringComparison.OrdinalIgnoreCase)
            && (relative.Length == sessions.Length || relative[sessions.Length] == '/');
    }

    private static string? ExtractBearerCredential(string? authorizationHeader)
    {
        const string scheme = "Bearer ";
        if (
            authorizationHeader is null
            || !authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        return authorizationHeader[scheme.Length..].Trim();
    }

    /// <summary>
    /// Reads the request body, enforcing <see cref="DuetsPadServiceOptions.MaxRequestBodyBytes"/>.
    /// On overflow, writes a <c>413</c> JSON error response (including <paramref name="sessionId"/>
    /// when non-null) and returns <see langword="null"/>; callers must return immediately in that case.
    /// </summary>
    private async Task<string?> ReadBodyWithinLimitOrRespondAsync(
        HttpActionContext ctx,
        string? sessionId
    )
    {
        var maxBytes = this._options.MaxRequestBodyBytes;

        var body =
            ctx.Request.ContentLength64 > maxBytes
                ? null
                : await ReadRequestBodyWithinLimitAsync(
                    ctx.Request.InputStream,
                    ctx.Request.ContentEncoding,
                    maxBytes
                );
        if (body is null)
        {
            await DrainRequestBodyAsync(ctx.Request.InputStream, maxBytes);
            await RespondBodyTooLargeAsync(ctx, sessionId);
            return null;
        }

        return body;
    }

    /// <summary>
    /// Drains (and discards) the remaining request body, up to a bounded number of bytes and a
    /// bounded amount of time, before a <c>413</c> response is written.
    /// </summary>
    /// <remarks>
    /// Closing the connection while unread request data is still pending makes
    /// <see cref="System.Net.HttpListener"/> reset the connection, and the client then surfaces a
    /// network error instead of the <c>413</c>; draining a moderately-oversized body lets a
    /// cooperative client actually read the response. Both bounds matter: the byte cap stops the
    /// drain from becoming free bandwidth for an attacker, and the time cap stops a slow-trickle
    /// upload from parking a request slot (the byte cap alone bounds volume, not duration). When
    /// either bound is hit the drain simply stops — the client gets a connection reset instead of a
    /// readable <c>413</c>, which is the safer failure mode for an abusive request.
    /// </remarks>
    private static async Task DrainRequestBodyAsync(Stream stream, int maxBytes)
    {
        // 4x the configured limit covers the realistic accidental overshoot; the 1 MiB floor keeps
        // the cap meaningful when the configured limit is very small.
        var drainCap = Math.Max((long)maxBytes * 4, 1024 * 1024);
        using var timeout = new CancellationTokenSource(DrainTimeout);
        var buffer = new byte[81920];
        long drained = 0;
        try
        {
            while (drained < drainCap)
            {
                var readSize = (int)Math.Min(buffer.Length, drainCap - drained);
                var read = await stream.ReadAsync(buffer.AsMemory(0, readSize), timeout.Token);
                if (read == 0)
                {
                    return;
                }

                drained += read;
            }
        }
        catch (OperationCanceledException)
        {
            // Drain deadline hit: stop reading and let the response attempt proceed (it will most
            // likely surface as a reset to the client, which is acceptable here).
        }
        catch (IOException)
        {
            // The client aborted mid-drain; the 413 will not be deliverable, which is fine.
        }
    }

    private static Task RespondBodyTooLargeAsync(HttpActionContext ctx, string? sessionId)
    {
        ctx.Response.StatusCode = 413;
        var response = new JsonObject { ["ok"] = false, ["error"] = "Request body too large." };
        if (sessionId is not null)
        {
            response["sessionId"] = sessionId;
        }

        return ctx.CloseAsync("application/json; charset=utf-8", response.ToJsonString());
    }

    /// <summary>
    /// Resolves the <see cref="DuetsPadSession"/> for <paramref name="sessionId"/>.
    /// Returns the session when found; writes an <c>{ ok:false, error:"Unknown session." }</c>
    /// JSON response and returns <see langword="null"/> when the session cannot be resolved.
    /// </summary>
    private async Task<DuetsPadSession?> ResolveSessionOrRespondAsync(
        HttpActionContext ctx,
        string? sessionId
    )
    {
        if (this._registry.TryAcquireSession(sessionId) is { } session)
        {
            return session;
        }

        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            new JsonObject
            {
                ["ok"] = false,
                ["error"] = "Unknown session.",
                ["sessionId"] = sessionId ?? "",
            }.ToJsonString()
        );
        return null;
    }

    private bool TryBuildCompletionContext(
        CompleteRequest request,
        out TemplateCompletionContext context
    )
    {
        context = null!;
        if (
            request.Tag is null
            || request.TextBeforeCaret is null
            || request.TextAfterCaret is null
            || request.CurrentSegmentRaw is null
            || !TaggedTemplateRegistry.IsValidTag(request.Tag)
        )
        {
            return false;
        }

        if (
            IsTooLong(request.Tag)
            || IsTooLong(request.TextBeforeCaret)
            || IsTooLong(request.TextAfterCaret)
            || IsTooLong(request.CurrentSegmentRaw)
        )
        {
            return false;
        }

        if (
            request.SegmentIndex != 0
            || request.CaretOffsetInSegment < 0
            || request.CaretOffsetInSegment > request.CurrentSegmentRaw.Length
        )
        {
            return false;
        }

        context = new TemplateCompletionContext(
            request.Tag,
            request.TextBeforeCaret,
            request.TextAfterCaret,
            request.CurrentSegmentRaw,
            SegmentIndex: 0,
            request.CaretOffsetInSegment,
            [request.CurrentSegmentRaw]
        );
        return true;

        bool IsTooLong(string value) =>
            value.Length > this._options.TaggedTemplateCompletionMaxFieldLength;
    }

    private static CompleteRequest? ParseCompleteRequest(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CompleteRequest
        {
            Tag = GetString(root, "tag"),
            TextBeforeCaret = GetString(root, "textBeforeCaret"),
            TextAfterCaret = GetString(root, "textAfterCaret"),
            CurrentSegmentRaw = GetString(root, "currentSegmentRaw"),
            SegmentIndex = GetInt32(root, "segmentIndex"),
            CaretOffsetInSegment = GetInt32(root, "caretOffsetInSegment"),
        };
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt32(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var result)
            ? result
            : 0;

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static JsonObject SerializeCompleteResponse(
        Guid sessionId,
        TaggedTemplateCompletionDispatchResult result
    )
    {
        var items = new JsonArray();
        foreach (var item in result.Items)
        {
            var itemObj = new JsonObject
            {
                ["label"] = item.Label,
                ["insertText"] = item.InsertText,
                ["kind"] = item.Kind.ToString(),
                ["filterText"] = item.FilterText,
                ["sortText"] = item.SortText,
                ["detail"] = item.Detail,
                ["documentation"] = item.Documentation,
            };
            if (item.ReplacementSpan is { } span)
            {
                itemObj["replacementSpan"] = new JsonObject
                {
                    ["start"] = span.Start,
                    ["length"] = span.Length,
                };
            }

            items.Add(itemObj);
        }

        return new JsonObject
        {
            ["ok"] = result.Ok,
            ["items"] = items,
            ["error"] = result.Error,
            ["stale"] = result.Stale,
            ["timedOut"] = result.TimedOut,
            ["sessionId"] = sessionId.ToString(),
        };
    }

    private static async Task<string?> ReadRequestBodyWithinLimitAsync(
        Stream stream,
        Encoding encoding,
        int maxBytes
    )
    {
        // Widened to long throughout: maxBytes may be int.MaxValue, and the "+ 1" probe byte that
        // detects an over-limit body would otherwise overflow to a negative buffer length.
        var buffer = new byte[Math.Min(8192L, (long)maxBytes + 1)];
        using var memory = new MemoryStream(capacity: Math.Min(maxBytes, 8192));
        var total = 0L;

        while (true)
        {
            var remainingBeforeLimit = (long)maxBytes - total;
            var readSize = (int)Math.Min(buffer.Length, remainingBeforeLimit + 1);
            var read = await stream.ReadAsync(buffer, 0, readSize);
            if (read == 0)
            {
                // Decode through StreamReader after the bounded byte read so the previous request
                // semantics are preserved: in particular, BOM detection strips a UTF-8/UTF-16
                // preamble before JSON parsing or script evaluation.
                memory.Position = 0;
                using var reader = new StreamReader(
                    memory,
                    encoding,
                    detectEncodingFromByteOrderMarks: true
                );
                return await reader.ReadToEndAsync();
            }

            total += read;
            if (total > maxBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }
    }

    private async Task RespondCompleteBodyTooLargeAsync(
        HttpActionContext ctx,
        Guid sessionId,
        int maxBytes
    )
    {
        await DrainRequestBodyAsync(ctx.Request.InputStream, maxBytes);
        ctx.Response.StatusCode = 413;
        await this.RespondCompleteErrorAsync(
            ctx,
            sessionId,
            "Tagged-template completion request is too large."
        );
    }

    private async Task RespondCompleteErrorAsync(
        HttpActionContext ctx,
        Guid sessionId,
        string error
    )
    {
        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            new JsonObject
            {
                ["ok"] = false,
                ["items"] = new JsonArray(),
                ["error"] = error,
                ["stale"] = false,
                ["timedOut"] = false,
                ["sessionId"] = sessionId.ToString(),
            }.ToJsonString()
        );
    }

    private sealed record CompleteRequest
    {
        public string? Tag { get; init; }
        public string? TextBeforeCaret { get; init; }
        public string? TextAfterCaret { get; init; }
        public string? CurrentSegmentRaw { get; init; }
        public int SegmentIndex { get; init; }
        public int CaretOffsetInSegment { get; init; }
    }
}
