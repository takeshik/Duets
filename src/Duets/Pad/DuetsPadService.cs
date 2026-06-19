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
    private readonly DuetsPadServiceOptions _options;
    private readonly AssetProvider _assets;
    private readonly SessionRegistry _registry;

    internal DuetsPadService(HttpServer server, string root, DuetsPadServiceOptions options)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));
        this._assets = new AssetProvider(options);
        this._registry = new SessionRegistry(options);

        server
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
                        .MapPost(
                            "/sessions/{sessionId}/interactions/{handlerId}/invoke",
                            this.HandleInvokeInteractionAsync
                        )
                        .MapGet("/sessions/{sessionId}/events", this.HandleEventsAsync)
            )
            .UseEmbeddedResources(
                typeof(DuetsPadService).Assembly,
                "Duets.Resources.DuetsPadStaticFiles",
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

        if (ctx.Request.ContentLength64 > this._options.TaggedTemplateCompletionMaxRequestBytes)
        {
            await this.RespondCompleteErrorAsync(
                ctx,
                session.Id,
                "Tagged-template completion request is too large."
            );
            return;
        }

        var body = await ReadRequestBodyWithinLimitAsync(
            ctx.Request.InputStream,
            ctx.Request.ContentEncoding,
            this._options.TaggedTemplateCompletionMaxRequestBytes
        );
        if (body is null)
        {
            await this.RespondCompleteErrorAsync(
                ctx,
                session.Id,
                "Tagged-template completion request is too large."
            );
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
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        var body = await reader.ReadToEndAsync();

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

        var (_, id) = await this._registry.GetOrCreateSessionAsync(existingId);

        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            new JsonObject { ["sessionId"] = id.ToString() }.ToJsonString()
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

        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        var code = await reader.ReadToEndAsync();

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

    // POST /sessions/{sessionId}/interactions/{handlerId}/invoke

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

        var result = await session.InvokeInteractionAsync(parsedHandlerId);
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
    /// Resolves the <see cref="DuetsPadSession"/> for <paramref name="sessionId"/>.
    /// Returns the session when found; writes an <c>{ ok:false, error:"Unknown session." }</c>
    /// JSON response and returns <see langword="null"/> when the session cannot be resolved.
    /// </summary>
    private async Task<DuetsPadSession?> ResolveSessionOrRespondAsync(
        HttpActionContext ctx,
        string? sessionId
    )
    {
        if (this._registry.TryGetSession(sessionId) is { } session)
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
        var buffer = new byte[Math.Min(8192, maxBytes + 1)];
        using var memory = new MemoryStream(capacity: Math.Min(maxBytes, 8192));
        var total = 0;

        while (true)
        {
            var remainingBeforeLimit = maxBytes - total;
            var readSize = Math.Min(buffer.Length, remainingBeforeLimit + 1);
            var read = await stream.ReadAsync(buffer, 0, readSize);
            if (read == 0)
            {
                return encoding.GetString(memory.ToArray());
            }

            total += read;
            if (total > maxBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }
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
        public IReadOnlyList<string>? RawSegments { get; init; }
    }
}
