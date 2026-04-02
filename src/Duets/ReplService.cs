using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HttpHarker;
using Timer = System.Timers.Timer;

namespace Duets;

/// <summary>
/// Extension methods for attaching <see cref="ReplService"/> to an <see cref="HttpHarker.HttpServer"/>.
/// </summary>
public static class ReplServiceExtensions
{
    public static ReplService UseRepl(
        this HttpServer server,
        TypeScriptService svc, ScriptEngine scriptEngine, string root = "/",
        IAssetSource? monacoLoader = null)
    {
        return new ReplService(svc, scriptEngine, server, root, monacoLoader);
    }
}

/// <summary>
/// Web-based TypeScript REPL service. Serves the Monaco editor UI, proxies eval requests to
/// <see cref="ScriptEngine"/>, and streams type declaration updates to the browser via SSE.
/// </summary>
public class ReplService : IDisposable
{
    public ReplService(
        TypeScriptService svc, ScriptEngine scriptEngine, HttpServer server, string root = "/",
        IAssetSource? monacoLoader = null)
    {
        this._svc = svc;
        this._scriptEngine = scriptEngine;
        var monacoLoaderSource = monacoLoader
            ?? AssetSources.Unpkg("monaco-editor", "0.55.1", "min/vs/loader.js")
                .WithDiskCache(Path.Combine(Path.GetTempPath(), "monaco-loader.js"));
        this._monacoLoader = new Lazy<Task<string>>(() => monacoLoaderSource.GetAsync());
        svc.TypeDeclarationAdded += this.OnTypeDeclarationAdded;
        server
            .UseSimpleRouting(
                root,
                routes =>
                    routes.MapGet("/monaco-loader.js", this.HandleMonacoLoaderAsync)
                        .MapGet("/type-declaration-events", this.HandleSseAsync)
                        .MapPost("/eval", this.HandleEvalAsync)
            )
            .UseEmbeddedResources(typeof(ReplService).Assembly, "Duets.Resources.ReplStaticFiles", root);
    }

    private readonly TypeScriptService _svc;
    private readonly ScriptEngine _scriptEngine;
    private readonly ConcurrentDictionary<Guid, ChannelWriter<TypeScriptService.TypeDeclaration?>> _sseWriters = new();
    private readonly Lazy<Task<string>> _monacoLoader;

    public void Dispose()
    {
        this._svc.TypeDeclarationAdded -= this.OnTypeDeclarationAdded;
    }

    private void OnTypeDeclarationAdded(TypeScriptService.TypeDeclaration decl)
    {
        foreach (var (id, writer) in this._sseWriters)
        {
            if (!writer.TryWrite(decl))
            {
                this._sseWriters.TryRemove(id, out _);
            }
        }
    }

    private async Task HandleMonacoLoaderAsync(HttpActionContext ctx)
    {
        await ctx.CloseAsync(await this._monacoLoader.Value);
    }

    private async Task HandleSseAsync(HttpActionContext ctx)
    {
        var res = ctx.Response;
        res.ContentType = "text/event-stream; charset=utf-8";
        res.Headers["Cache-Control"] = "no-cache";
        res.SendChunked = true;

        var channel = Channel.CreateUnbounded<TypeScriptService.TypeDeclaration?>();
        var id = Guid.NewGuid();
        this._sseWriters[id] = channel.Writer;

        // Send already-registered TypeDeclarations first
        foreach (var decl in this._svc.GetTypeDeclarations())
        {
            channel.Writer.TryWrite(decl);
        }

        // Send a keepalive comment every 15 seconds to detect client disconnection
        using var timer = new Timer(15_000);
        timer.Elapsed += (_, _) => channel.Writer.TryWrite(null);
        timer.Start();

        try
        {
            await foreach (var decl in channel.Reader.ReadAllAsync())
            {
                var sseData = decl is null
                    ? ": keepalive\n\n"
                    : $"data: {JsonSerializer.Serialize(new { fileName = decl.FileName, content = decl.Content })}\n\n";
                await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(sseData));
                await res.OutputStream.FlushAsync();
            }
        }
        catch
        {
            /* Client disconnected */
        }
        finally
        {
            timer.Stop();
            this._sseWriters.TryRemove(id, out _);
            channel.Writer.TryComplete();
            res.Close();
        }
    }

    private async Task HandleEvalAsync(HttpActionContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        var code = await reader.ReadToEndAsync();
        string resultStr;
        bool ok;
        var logs = new List<ScriptConsoleEntry>();

        void OnLog(ScriptConsoleEntry e)
        {
            logs.Add(e);
        }

        this._scriptEngine.ConsoleLogged += OnLog;
        try
        {
            resultStr = this._scriptEngine.Evaluate(code).ToString();
            ok = true;
        }
        catch (Exception ex)
        {
            resultStr = ex.Message;
            ok = false;
        }
        finally
        {
            this._scriptEngine.ConsoleLogged -= OnLog;
        }

        await ctx.CloseAsync(
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                new
                {
                    result = resultStr,
                    ok,
                    logs = logs.Select(l => new
                            {
                                level = l.Level.ToString().ToLowerInvariant(),
                                text = l.Text,
                            }
                        )
                        .ToArray(),
                }
            )
        );
    }
}
