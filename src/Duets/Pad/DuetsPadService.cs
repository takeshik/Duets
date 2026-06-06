using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Duets.Pad.Protocol;
using HttpHarker;
using Timer = System.Timers.Timer;

namespace Duets.Pad;

/// <summary>
/// Web service that provides the DuetsPad UI and multi-session HTTP/SSE API.
/// Attach to an <see cref="HttpServer"/> via <see cref="DuetsPadServiceExtensions.UseDuetsPad"/>.
/// </summary>
public sealed class DuetsPadService : IDisposable
{
    private const string TablerIconsPackageVersion = "3.44.0";
    private const string TablerCorePackageVersion = "1.4.0";

    private readonly DuetsPadServiceOptions _options;
    private readonly ConcurrentDictionary<Guid, DuetsPadSession> _sessions = new();
    private readonly Timer? _cleanupTimer;
    private readonly Lazy<Task<string>> _monaco;
    private readonly Lazy<Task<string>> _tabler;
    private readonly Lazy<Task<string>> _tablerIconsCss;
    private readonly Lazy<Task<byte[]>> _tablerIconsFont;

    internal DuetsPadService(HttpServer server, string root, DuetsPadServiceOptions options)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));

        var monacoSource =
            options.MonacoLoader
            ?? AssetSources
                .Unpkg("monaco-editor", "0.55.1", "min/vs/loader.js")
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-monaco-loader.js"));
        this._monaco = new Lazy<Task<string>>(() => monacoSource.GetStringAsync());

        var tablerSource =
            options.TablerCss
            ?? AssetSources
                .Unpkg("@tabler/core", TablerCorePackageVersion, "dist/css/tabler.min.css")
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-tabler.css"));
        this._tabler = new Lazy<Task<string>>(() => tablerSource.GetStringAsync());

        var tablerIconsCssSource =
            options.TablerIconsCss
            ?? AssetSources
                .Unpkg(
                    "@tabler/icons-webfont",
                    TablerIconsPackageVersion,
                    "dist/tabler-icons.min.css"
                )
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-tabler-icons.css"));
        this._tablerIconsCss = new Lazy<Task<string>>(async () =>
            RewriteTablerIconsCss(await tablerIconsCssSource.GetStringAsync().ConfigureAwait(false))
        );

        var tablerIconsFontSource =
            options.TablerIconsFont
            ?? AssetSources
                .Unpkg(
                    "@tabler/icons-webfont",
                    TablerIconsPackageVersion,
                    "dist/fonts/tabler-icons.woff2"
                )
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "duetspad-tabler-icons.woff2"));
        this._tablerIconsFont = new Lazy<Task<byte[]>>(() => tablerIconsFontSource.GetBytesAsync());

        server
            .UseSimpleRouting(
                root,
                routes =>
                    routes
                        .MapGet("/", this.HandleIndexAsync)
                        .MapGet("/duetspad-config.js", this.HandleDuetsPadConfigJsAsync)
                        .MapGet("/duetspad.js", this.HandleDuetsPadJsAsync)
                        .MapGet("/monaco-loader.js", this.HandleMonacoLoaderAsync)
                        .MapGet("/tabler.css", this.HandleTablerCssAsync)
                        .MapGet("/tabler-icons.css", this.HandleTablerIconsCssAsync)
                        .MapGet("/tabler-icons.woff2", this.HandleTablerIconsFontAsync)
                        .MapGet("/type-declaration-events", this.HandleTypeDeclarationEventsAsync)
                        .MapPost("/sessions", this.HandlePostSessionAsync)
                        .MapDelete("/sessions/{sessionId}", this.HandleDeleteSessionAsync)
                        .MapPost("/sessions/{sessionId}/eval", this.HandleEvalAsync)
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

        // Start the idle-cleanup sweep timer only when IdleTimeout is enabled.
        if (options.IdleTimeout is { } timeout && timeout > TimeSpan.Zero)
        {
            this._cleanupTimer = new Timer(options.CleanupInterval.TotalMilliseconds);
            this._cleanupTimer.Elapsed += (_, _) => this.RemoveIdleSessions();
            this._cleanupTimer.Start();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this._cleanupTimer is not null)
        {
            this._cleanupTimer.Stop();
            this._cleanupTimer.Dispose();
        }

        foreach (var (_, session) in this._sessions)
        {
            session.Dispose();
        }

        this._sessions.Clear();
    }

    // -------------------------------------------------------------------------
    // Static asset handlers
    // -------------------------------------------------------------------------

    private async Task HandleIndexAsync(HttpActionContext ctx)
    {
        using var stream = typeof(DuetsPadService).Assembly.GetManifestResourceStream(
            "Duets.Resources.DuetsPadStaticFiles.index.html"
        );
        if (stream is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();
        await ctx.CloseAsync("text/html; charset=utf-8", html);
    }

    private async Task HandleDuetsPadConfigJsAsync(HttpActionContext ctx)
    {
        var url = JsonSerializer.Serialize(this._options.MonacoBaseUrl);
        var js = $"window.DUETSPAD_MONACO_VS = {url};";
        await ctx.CloseAsync("text/javascript; charset=utf-8", js);
    }

    private async Task HandleDuetsPadJsAsync(HttpActionContext ctx)
    {
        using var stream = typeof(DuetsPadService).Assembly.GetManifestResourceStream(
            "Duets.Resources.DuetsPadStaticFiles.duetspad.js"
        );
        if (stream is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var js = await reader.ReadToEndAsync();
        await ctx.CloseAsync("text/javascript; charset=utf-8", js);
    }

    private async Task HandleMonacoLoaderAsync(HttpActionContext ctx)
    {
        var content = await this._monaco.Value;
        await ctx.CloseAsync("text/javascript", content);
    }

    private async Task HandleTablerCssAsync(HttpActionContext ctx)
    {
        var content = await this._tabler.Value;
        await ctx.CloseAsync("text/css; charset=utf-8", content);
    }

    private async Task HandleTablerIconsCssAsync(HttpActionContext ctx)
    {
        var content = await this._tablerIconsCss.Value;
        await ctx.CloseAsync("text/css; charset=utf-8", content);
    }

    private async Task HandleTablerIconsFontAsync(HttpActionContext ctx)
    {
        var bytes = await this._tablerIconsFont.Value;
        await ctx.CloseAsync(
            new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("font/woff2") },
            }
        );
    }

    /// <summary>
    /// Rewrites each <c>@font-face</c> <c>src:</c> declaration in the Tabler Icons CSS so it
    /// references only the local woff2 route, dropping the upstream woff/ttf fallback entries
    /// that have no route in DuetsPad (ADR-33).
    /// </summary>
    internal static string RewriteTablerIconsCss(string css)
    {
        // DuetsPad serves only tabler-icons.woff2 (ADR-33). Replace each @font-face src list with a
        // single local woff2 reference, dropping the upstream woff/ttf fallbacks that have no route.
        // (In the Tabler Icons stylesheet, "src:" appears only inside @font-face, and is the last
        // declaration before the block's closing brace.)
        const string canonicalSrc = "src:url(\"tabler-icons.woff2\") format(\"woff2\")";
        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (true)
        {
            var srcIdx = css.IndexOf("src:", i, StringComparison.Ordinal);
            if (srcIdx < 0)
            {
                sb.Append(css, i, css.Length - i);
                break;
            }

            var brace = css.IndexOf('}', srcIdx);
            if (brace < 0)
            {
                sb.Append(css, i, css.Length - i);
                break;
            }

            sb.Append(css, i, srcIdx - i); // text before "src:"
            sb.Append(canonicalSrc); // canonical single-source replacement
            i = brace; // resume at the '}' (kept)
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // POST /sessions
    // -------------------------------------------------------------------------

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

        if (existingId.HasValue && this._sessions.TryGetValue(existingId.Value, out _))
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject { ["sessionId"] = existingId.Value.ToString() }.ToJsonString()
            );
            return;
        }

        // Create a new session.
        var duetsSession = await this._options.SessionFactory();
        var newId = Guid.NewGuid();
        this._sessions[newId] = new DuetsPadSession(
            newId,
            duetsSession,
            this._options.ObjectRenderers,
            this._options.Clock,
            this._options.TimelineEntryLimit
        );

        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            new JsonObject { ["sessionId"] = newId.ToString() }.ToJsonString()
        );
    }

    // -------------------------------------------------------------------------
    // DELETE /sessions/{sessionId}
    // -------------------------------------------------------------------------

    private async Task HandleDeleteSessionAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (Guid.TryParse(sessionId, out var id) && this._sessions.TryRemove(id, out var session))
        {
            // The session is already removed from the dictionary; a dispose failure must not
            // escape into the HTTP handler. There is no logger here, so observe and continue.
            try
            {
                session.Dispose();
            }
            catch
            {
                // Swallow: the session is orphaned but unreachable; nothing more to do.
            }

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

    // -------------------------------------------------------------------------
    // Idle session cleanup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the <see cref="DuetsPadSession"/> identified by <paramref name="id"/>, or
    /// <see langword="null"/> if no such session exists. Exposed for testing only.
    /// </summary>
    internal DuetsPadSession? TryGetSession(Guid id) =>
        this._sessions.TryGetValue(id, out var session) ? session : null;

    /// <summary>
    /// Removes and disposes sessions that have been idle longer than
    /// <see cref="DuetsPadServiceOptions.IdleTimeout"/>. Does nothing when
    /// <see cref="DuetsPadServiceOptions.IdleTimeout"/> is <see langword="null"/> or non-positive.
    /// Called by the background cleanup timer; also directly callable by tests.
    /// </summary>
    internal void RemoveIdleSessions()
    {
        if (this._options.IdleTimeout is not { } timeout || timeout <= TimeSpan.Zero)
        {
            return;
        }

        var now = this._options.Clock();
        foreach (var (id, session) in this._sessions)
        {
            // Never evict a session that has a live SSE stream; the subscriber guard is
            // timing-independent and takes precedence over the LastActivity check.
            if (session.HasActiveSubscribers)
            {
                continue;
            }

            if (now - session.LastActivityUtc > timeout)
            {
                if (this._sessions.TryRemove(id, out var removed))
                {
                    // One session's dispose failure must not abort the sweep over the others,
                    // nor kill the cleanup timer. There is no logger here, so observe and continue.
                    try
                    {
                        removed.Dispose();
                    }
                    catch
                    {
                        // Swallow and proceed to the next idle session.
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // POST /sessions/{sessionId}/eval
    // -------------------------------------------------------------------------

    private async Task HandleEvalAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (
            !Guid.TryParse(sessionId, out var id)
            || !this._sessions.TryGetValue(id, out var session)
        )
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
                ["sessionId"] = id.ToString(),
            }
            : new JsonObject
            {
                ["ok"] = false,
                ["error"] = result.Error,
                ["sessionId"] = id.ToString(),
            };

        await ctx.CloseAsync("application/json; charset=utf-8", response.ToJsonString());
    }

    // -------------------------------------------------------------------------
    // GET /sessions/{sessionId}/canvas-events
    // -------------------------------------------------------------------------

    private async Task HandleCanvasEventsAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (
            !Guid.TryParse(sessionId, out var id)
            || !this._sessions.TryGetValue(id, out var session)
        )
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
            return;
        }

        var res = ctx.Response;
        res.ContentType = "text/event-stream; charset=utf-8";
        res.Headers["Cache-Control"] = "no-cache";
        res.SendChunked = true;

        var channel = Channel.CreateUnbounded<CanvasEventMessage>();
        var key = session.AddCanvasSubscriber(channel.Writer);

        using var gate = new SemaphoreSlim(1, 1);
        using var timer = new Timer(this._options.KeepAliveInterval.TotalMilliseconds);
        timer.Elapsed += (_, _) =>
        {
            session.Touch();
            _ = WriteKeepAliveAsync(res.OutputStream, gate);
        };
        timer.Start();

        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync())
            {
                var sseData = $"data: {SseSerializer.Serialize(msg)}\n\n";
                await gate.WaitAsync();
                try
                {
                    await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(sseData));
                    await res.OutputStream.FlushAsync();
                }
                finally
                {
                    gate.Release();
                }
            }
        }
        catch
        {
            /* Client disconnected. */
        }
        finally
        {
            timer.Stop();
            session.RemoveCanvasSubscriber(key);
            channel.Writer.TryComplete();
            res.Close();
        }
    }

    // -------------------------------------------------------------------------
    // GET /sessions/{sessionId}/timeline-events
    // -------------------------------------------------------------------------

    private async Task HandleTimelineEventsAsync(HttpActionContext ctx)
    {
        var sessionId = ctx.Args["sessionId"];

        if (
            !Guid.TryParse(sessionId, out var id)
            || !this._sessions.TryGetValue(id, out var session)
        )
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
            return;
        }

        var res = ctx.Response;
        res.ContentType = "text/event-stream; charset=utf-8";
        res.Headers["Cache-Control"] = "no-cache";
        res.SendChunked = true;

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        var key = session.AddTimelineSubscriber(channel.Writer);

        using var gate = new SemaphoreSlim(1, 1);
        using var timer = new Timer(this._options.KeepAliveInterval.TotalMilliseconds);
        timer.Elapsed += (_, _) =>
        {
            session.Touch();
            _ = WriteKeepAliveAsync(res.OutputStream, gate);
        };
        timer.Start();

        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync())
            {
                var sseData = $"data: {SseSerializer.Serialize(msg)}\n\n";
                await gate.WaitAsync();
                try
                {
                    await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(sseData));
                    await res.OutputStream.FlushAsync();
                }
                finally
                {
                    gate.Release();
                }
            }
        }
        catch
        {
            /* Client disconnected. */
        }
        finally
        {
            timer.Stop();
            session.RemoveTimelineSubscriber(key);
            channel.Writer.TryComplete();
            res.Close();
        }
    }

    // -------------------------------------------------------------------------
    // GET /type-declaration-events?sessionId=...
    // -------------------------------------------------------------------------

    private async Task HandleTypeDeclarationEventsAsync(HttpActionContext ctx)
    {
        var sessionIdStr = ctx.Request.QueryString["sessionId"];

        if (
            string.IsNullOrEmpty(sessionIdStr)
            || !Guid.TryParse(sessionIdStr, out var id)
            || !this._sessions.TryGetValue(id, out var session)
        )
        {
            await ctx.CloseAsync(
                "application/json; charset=utf-8",
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Unknown session.",
                    ["sessionId"] = sessionIdStr ?? "",
                }.ToJsonString()
            );
            return;
        }

        var res = ctx.Response;
        res.ContentType = "text/event-stream; charset=utf-8";
        res.Headers["Cache-Control"] = "no-cache";
        res.SendChunked = true;

        var declarations = session.DuetsSession.Declarations;
        var channel = Channel.CreateUnbounded<TypeDeclaration?>();

        void OnDeclarationChanged(TypeDeclaration decl) => channel.Writer.TryWrite(decl);

        // Subscribe before enumerating existing declarations so no declaration registered
        // between the two steps is lost. A declaration added during this window may be
        // delivered twice; that is harmless because Monaco addExtraLib is keyed by fileName
        // and is therefore idempotent.
        declarations.DeclarationChanged += OnDeclarationChanged;

        // Register with the session so that Dispose() completes the channel, which
        // terminates the read loop below and allows the finally block to run.
        var key = session.AddTypeDeclarationSubscriber(channel.Writer);

        foreach (var decl in declarations.GetDeclarations())
        {
            channel.Writer.TryWrite(decl);
        }

        // Touch on attach (session lookup already happened; record SSE attach as activity).
        session.Touch();

        using var timer = new Timer(this._options.KeepAliveInterval.TotalMilliseconds);
        timer.Elapsed += (_, _) =>
        {
            session.Touch();
            channel.Writer.TryWrite(null);
        };
        timer.Start();

        try
        {
            await foreach (var decl in channel.Reader.ReadAllAsync())
            {
                var sseData = decl is null
                    ? ": keepalive\n\n"
                    : $"data: {new JsonObject { ["fileName"] = decl.FileName, ["content"] = decl.Content }.ToJsonString()}\n\n";
                await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(sseData));
                await res.OutputStream.FlushAsync();
            }
        }
        catch
        {
            /* Client disconnected. */
        }
        finally
        {
            timer.Stop();
            session.RemoveTypeDeclarationSubscriber(key);
            declarations.DeclarationChanged -= OnDeclarationChanged;
            channel.Writer.TryComplete();
            res.Close();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task WriteKeepAliveAsync(Stream stream, SemaphoreSlim gate)
    {
        await gate.WaitAsync();
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(": keepalive\n\n"));
            await stream.FlushAsync();
        }
        catch
        {
            /* Stream may be closed or client disconnected. */
        }
        finally
        {
            gate.Release();
        }
    }
}
