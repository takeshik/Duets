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

    private CancellationTokenSource? _cts;

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

    public void Start(int workersCount = 8)
    {
        if (this.IsRunning) throw new InvalidOperationException("Server is already running.");
        this._cts = new CancellationTokenSource();
        this.RunAsync(workersCount, this._cts.Token).Forget();
    }

    public void Stop()
    {
        var cts = Interlocked.Exchange(ref this._cts, null);
        cts?.Cancel();
        cts?.Dispose();
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
                catch (InvalidOperationException) when (!this._listener.IsListening)
                {
                    break;
                }
                catch
                {
                    if (!this._listener.IsListening)
                    {
                        break;
                    }

                    // ignored
                }
            }
        }
    }

    public void Dispose()
    {
        this.Stop();
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

            return;
        }

        // Close the response if no middleware committed it.
        // If status was never changed from the default (200), treat as 404.
        try
        {
            if (ctx.Response.StatusCode == 200)
            {
                ctx.Response.StatusCode = 404;
            }

            ctx.Response.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed by middleware — nothing to do.
        }

        return;

        Task NextAsync()
        {
            if (index >= this._middleware.Count)
            {
                // Do not close here; let HandleAsync close after the full pipeline returns.
                return Task.CompletedTask;
            }

            return this._middleware[index++](ctx, NextAsync);
        }
    }
}
