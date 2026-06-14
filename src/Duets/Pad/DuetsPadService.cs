using System.Text.Json;
using System.Text.Json.Nodes;
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
                        .MapGet("/type-declaration-events", this.HandleTypeDeclarationEventsAsync)
                        .MapPost("/sessions", this.HandlePostSessionAsync)
                        .MapDelete("/sessions/{sessionId}", this.HandleDeleteSessionAsync)
                        .MapPost("/sessions/{sessionId}/eval", this.HandleEvalAsync)
                        .MapPost(
                            "/sessions/{sessionId}/interactions/{handlerId}/invoke",
                            this.HandleInvokeInteractionAsync
                        )
                        .MapGet("/sessions/{sessionId}/canvas-events", this.HandleCanvasEventsAsync)
                        .MapGet(
                            "/sessions/{sessionId}/timeline-events",
                            this.HandleTimelineEventsAsync
                        )
            )
            .UseEmbeddedResources(
                typeof(DuetsPadService).Assembly,
                "Duets.Resources.DuetsPadStaticFiles",
                root
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

    // GET /sessions/{sessionId}/canvas-events

    private async Task HandleCanvasEventsAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        await SseTransport.RunAsync<CanvasEventMessage>(
            ctx,
            session,
            this._options.KeepAliveInterval,
            setup: session.AddCanvasSubscriber,
            teardown: session.RemoveCanvasSubscriber,
            formatData: SseSerializer.Serialize
        );
    }

    // GET /sessions/{sessionId}/timeline-events

    private async Task HandleTimelineEventsAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionId) is not { } session)
        {
            return;
        }

        await SseTransport.RunAsync<TimelineEventMessage>(
            ctx,
            session,
            this._options.KeepAliveInterval,
            setup: session.AddTimelineSubscriber,
            teardown: session.RemoveTimelineSubscriber,
            formatData: SseSerializer.Serialize
        );
    }

    // GET /type-declaration-events?sessionId=...

    private async Task HandleTypeDeclarationEventsAsync(HttpActionContext ctx)
    {
        var sessionIdStr = ctx.Request.QueryString["sessionId"];

        if (await this.ResolveSessionOrRespondAsync(ctx, sessionIdStr) is not { } session)
        {
            return;
        }

        var declarations = session.DuetsSession.Declarations;

        // Captured by both setup and teardown closures so the handler instance can be removed.
        Action<TypeDeclaration>? handler = null;

        await SseTransport.RunAsync<TypeDeclaration>(
            ctx,
            session,
            this._options.KeepAliveInterval,
            setup: writer =>
            {
                handler = decl => writer.TryWrite(decl);

                // Subscribe before enumerating existing declarations so no declaration registered
                // between the two steps is lost. A declaration added during this window may be
                // delivered twice; that is harmless because Monaco addExtraLib is keyed by fileName
                // and is therefore idempotent.
                declarations.DeclarationChanged += handler;

                // Register with the session so that Dispose() completes the channel, which
                // terminates the read loop and allows the finally block to run.
                var key = session.AddTypeDeclarationSubscriber(writer);

                foreach (var decl in declarations.GetDeclarations())
                {
                    writer.TryWrite(decl);
                }

                return key;
            },
            teardown: key =>
            {
                session.RemoveTypeDeclarationSubscriber(key);
                // Unhook after removing the session subscriber so that Dispose() completing
                // the channel writer does not race with a final handler invocation.
                if (handler is not null)
                {
                    declarations.DeclarationChanged -= handler;
                }
            },
            formatData: decl =>
                new JsonObject
                {
                    ["fileName"] = decl.FileName,
                    ["content"] = decl.Content,
                }.ToJsonString()
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
}
