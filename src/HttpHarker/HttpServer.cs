using System.Net;
using HttpHarker.Middlewares;

namespace HttpHarker;

/// <summary>
/// <see cref="System.Net.HttpListener"/>-based HTTP server with a composable middleware pipeline.
/// </summary>
public class HttpServer(string prefix) : IDisposable
{
    private readonly HttpListener _listener = new()
    {
        Prefixes =
        {
            prefix,
        },
        IgnoreWriteExceptions = true,
    };

    private readonly List<Func<HttpListenerContext, Func<Task>, Task>> _middleware = [];

    public bool IsRunning { get; private set; }

    public HttpServer Use(Func<HttpListenerContext, Func<Task>, Task> middleware)
    {
        if (this.IsRunning) throw new InvalidOperationException("Cannot add middleware while the server is running.");
        this._middleware.Add(middleware);
        return this;
    }

    public HttpServer Use(IMiddleware middleware)
    {
        return this.Use(middleware.InvokeAsync);
    }

    public async Task RunAsync(int workersCount = 8, CancellationToken cancellationToken = default)
    {
        try
        {
            this.IsRunning = true;
            this._listener.Start();

            // BeginGetContext/EndGetContext does not natively support cancellation,
            // so Stop() is called on cancellation to forcibly terminate GetContextAsync.
            await using var _ = cancellationToken.Register(this._listener.Stop);

            var tasks = Enumerable.Range(0, workersCount)
                .Select(_ => WorkerLoopAsync())
                .ToArray();

            await Task.WhenAll(tasks);
        }
        finally
        {
            this.IsRunning = false;
            this._listener.Stop();
        }

        return;

        async Task WorkerLoopAsync()
        {
            while (true)
            {
                try
                {
                    var ctx = await this._listener.GetContextAsync();
                    await this.HandleAsync(ctx);
                }
                catch (HttpListenerException) when (!this._listener.IsListening)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    public void Dispose()
    {
        this._listener.Stop();
        this._listener.Close();
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var index = 0;

        try
        {
            await NextAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HttpServer] {ctx.Request.HttpMethod} {ctx.Request.Url?.AbsolutePath}: {ex}");
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
            catch
            {
                /* ignore */
            }
        }

        return;

        Task NextAsync()
        {
            if (index >= this._middleware.Count)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return Task.CompletedTask;
            }

            return this._middleware[index++](ctx, NextAsync);
        }
    }
}
