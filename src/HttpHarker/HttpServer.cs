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
        Prefixes = { prefix },
        IgnoreWriteExceptions = true,
    };

    private readonly List<Func<HttpListenerContext, Func<Task>, Task>> _middleware = [];

    private CancellationTokenSource? _cts;

    /// <summary>Indicates whether the server is currently listening for requests.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Appends a raw middleware delegate to the pipeline.
    /// </summary>
    /// <param name="middleware">
    /// A delegate that receives the listener context and a <c>next</c> continuation.
    /// Call <c>next()</c> to pass control to the following middleware; omit it to short-circuit the pipeline.
    /// </param>
    /// <returns>This instance, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">The server is already running.</exception>
    public HttpServer Use(Func<HttpListenerContext, Func<Task>, Task> middleware)
    {
        if (this.IsRunning)
        {
            throw new InvalidOperationException(
                "Cannot add middleware while the server is running."
            );
        }

        this._middleware.Add(middleware);
        return this;
    }

    /// <summary>
    /// Appends an <see cref="IMiddleware"/> to the pipeline.
    /// </summary>
    /// <param name="middleware">The middleware component to add.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">The server is already running.</exception>
    public HttpServer Use(IMiddleware middleware)
    {
        return this.Use(middleware.InvokeAsync);
    }

    /// <summary>
    /// Starts the server in the background using <paramref name="workersCount"/> concurrent worker loops.
    /// Returns immediately; use <see cref="Stop"/> to halt.
    /// </summary>
    /// <param name="workersCount">Number of concurrent workers polling for incoming connections.</param>
    /// <exception cref="InvalidOperationException">The server is already running.</exception>
    public void Start(int workersCount = 8)
    {
        if (this.IsRunning)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        this._cts = new CancellationTokenSource();
        this.RunAsync(workersCount, this._cts.Token).Forget();
    }

    /// <summary>Signals the background workers started by <see cref="Start"/> to stop and releases the cancellation token source.</summary>
    public void Stop()
    {
        var cts = Interlocked.Exchange(ref this._cts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    /// <summary>
    /// Starts listening and runs the request loop until <paramref name="cancellationToken"/> is cancelled.
    /// Unlike <see cref="Start"/>, this method does not return until the server has stopped.
    /// </summary>
    /// <param name="workersCount">Number of concurrent workers polling for incoming connections.</param>
    /// <param name="cancellationToken">Token that stops the server when cancelled.</param>
    public async Task RunAsync(int workersCount = 8, CancellationToken cancellationToken = default)
    {
        try
        {
            this.IsRunning = true;
            this._listener.Start();

            // BeginGetContext/EndGetContext does not natively support cancellation,
            // so Stop() is called on cancellation to forcibly terminate GetContextAsync.
            await using var _ = cancellationToken.Register(() =>
            {
                try
                {
                    this._listener.Stop();
                }
                catch (ObjectDisposedException)
                {
                    // Listener was already closed externally — nothing to do.
                }
            });

            var tasks = Enumerable.Range(0, workersCount).Select(_ => WorkerLoopAsync()).ToArray();

            await Task.WhenAll(tasks);
        }
        finally
        {
            this.IsRunning = false;
            try
            {
                this._listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // Listener was already closed externally (e.g. via Dispose()) — nothing to do.
            }
        }

        return;

        async Task WorkerLoopAsync()
        {
            while (true)
            {
                try
                {
                    var ctx = await this._listener.GetContextAsync();

                    // Dispatch the request without awaiting it so the worker returns to
                    // GetContextAsync immediately. Awaiting here would pin the worker for the
                    // entire response lifetime; a long-lived response (e.g. an SSE stream) would
                    // then occupy a worker indefinitely, and once all workers were so occupied no
                    // new connection could be accepted. HandleAsync already catches and reports
                    // its own exceptions, so the request handling is self-contained.
                    this.HandleAsync(ctx).Forget();
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

    /// <summary>Stops the server and releases the underlying <see cref="System.Net.HttpListener"/>.</summary>
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
            Console.WriteLine(
                $"[HttpServer] {ctx.Request.HttpMethod} {ctx.Request.Url?.AbsolutePath}: {ex}"
            );
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
