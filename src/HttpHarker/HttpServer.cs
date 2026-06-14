using System.Net;
using HttpHarker.Middlewares;

namespace HttpHarker;

/// <summary>
/// <see cref="System.Net.HttpListener"/>-based HTTP server with a composable middleware pipeline.
/// </summary>
/// <param name="prefix">The URL prefix the underlying <see cref="System.Net.HttpListener"/> listens on.</param>
/// <param name="maxConcurrentRequests">
/// Maximum number of in-flight request handlers that may run simultaneously. When this limit is
/// reached, additional incoming requests are rejected immediately with HTTP 503 (Service
/// Unavailable) without entering the middleware pipeline. Set this well above the highest
/// expected number of simultaneous connections: long-lived responses (e.g. SSE streams) each
/// hold one slot for their entire lifetime and release it only on teardown. The default (1024)
/// is generous enough that only genuine runaway or abusive clients are rejected.
/// </param>
public class HttpServer(string prefix, int maxConcurrentRequests = 1024) : IDisposable
{
    // IgnoreWriteExceptions is left at its default (false) on purpose. When true, the listener
    // swallows the exception raised by a write to a client that has disconnected, so the write
    // appears to succeed (and, on a chunked stream whose send buffer fills, can block
    // indefinitely). Long-lived streaming handlers (e.g. SSE) rely on that write throwing as
    // their sole disconnect signal: it is what lets their read loop break and run teardown,
    // releasing the subscriber registration, keepalive timer, channel, and response. Swallowing
    // it would leak those resources for the lifetime of the process. Short responses tolerate the
    // thrown exception: it surfaces into HandleAsync's catch, which logs and closes the response.
    private readonly HttpListener _listener = new() { Prefixes = { prefix } };

    private readonly List<Func<HttpListenerContext, Func<Task>, Task>> _middleware = [];

    // Atomic counter of in-flight handlers. Incremented before entering HandleAsync and
    // decremented in its finally block so every code path — including exceptions and 503
    // rejections — decrements exactly once per increment.
    private int _inFlightCount;

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

                    // Reject immediately if the concurrency cap is reached. The check and
                    // increment are performed atomically: if the post-increment value exceeds the
                    // cap we undo it and send 503 without ever entering HandleAsync. This is
                    // non-blocking — the accept loop never waits for a slot to free up.
                    if (Interlocked.Increment(ref this._inFlightCount) > maxConcurrentRequests)
                    {
                        Interlocked.Decrement(ref this._inFlightCount);
                        try
                        {
                            ctx.Response.StatusCode = 503;
                            ctx.Response.Close();
                        }
                        catch
                        {
                            /* ignore — client may have already disconnected */
                        }

                        continue;
                    }

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
        }
        finally
        {
            // Release the in-flight slot. This runs regardless of whether the handler
            // completed normally, threw, or was cancelled — including for long-lived SSE
            // streams that hold the slot open for their entire lifetime.
            Interlocked.Decrement(ref this._inFlightCount);
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
