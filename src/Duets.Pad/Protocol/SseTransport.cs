using System.Text;
using System.Threading.Channels;
using HttpHarker;
using Timer = System.Timers.Timer;

namespace Duets.Pad.Protocol;

/// <summary>
/// Shared SSE streaming transport for DuetsPad event streams.
/// Owns the mechanical parts of an SSE response: setting response headers, creating
/// an unbounded channel, running a keepalive timer, reading from the channel until
/// it is completed or the client disconnects, and tearing down on exit.
/// </summary>
/// <remarks>
/// Each stream supplies its own subscribe/replay hook and unsubscribe teardown via
/// <see cref="RunAsync{TItem}"/>. <see langword="null"/> items produce <c>": keepalive\n\n"</c>;
/// non-null items produce <c>"data: {formatData(item)}\n\n"</c>.
/// </remarks>
internal static class SseTransport
{
    /// <summary>
    /// Runs an SSE streaming loop for <typeparamref name="TItem"/> messages.
    /// </summary>
    /// <typeparam name="TItem">The non-null message type written to the channel. Null
    /// sentinels written to the channel produce keepalive comments.</typeparam>
    /// <param name="ctx">The HTTP action context whose response receives the SSE stream.</param>
    /// <param name="session">The pad session; <see cref="DuetsPadSession.Touch"/> is called on
    /// each keepalive tick and once immediately after the stream setup hook returns.</param>
    /// <param name="keepAliveInterval">How often the keepalive timer fires.</param>
    /// <param name="setup">
    /// Stream-specific setup hook. Called with the channel writer after the SSE headers are
    /// set but before the keepalive timer starts. Responsible for subscribing to the event
    /// source (and, if applicable, replaying any initial state into the channel). Returns a
    /// correlation key passed verbatim to <paramref name="teardown"/>.
    /// </param>
    /// <param name="teardown">
    /// Stream-specific teardown hook. Called in the <see langword="finally"/> block with
    /// the key returned by <paramref name="setup"/>. Responsible for unsubscribing from
    /// the event source.
    /// </param>
    /// <param name="formatData">Serializes a non-null <typeparamref name="TItem"/> to the
    /// JSON string placed on the <c>data:</c> line (without the <c>data: </c> prefix).</param>
    internal static async Task RunAsync<TItem>(
        HttpActionContext ctx,
        DuetsPadSession session,
        TimeSpan keepAliveInterval,
        Func<ChannelWriter<TItem?>, Guid> setup,
        Action<Guid> teardown,
        Func<TItem, string> formatData
    )
        where TItem : class
    {
        var res = ctx.Response;
        res.ContentType = "text/event-stream; charset=utf-8";
        res.Headers["Cache-Control"] = "no-cache";
        res.SendChunked = true;

        var channel = Channel.CreateUnbounded<TItem?>();
        var key = setup(channel.Writer);

        // Touch on attach so SSE connections reset the idle clock.
        session.Touch();

        using var timer = new Timer(keepAliveInterval.TotalMilliseconds);
        timer.Elapsed += (_, _) =>
        {
            session.Touch();
            channel.Writer.TryWrite(null);
        };
        timer.Start();

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                var sseData = item is null ? ": keepalive\n\n" : $"data: {formatData(item)}\n\n";
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
            teardown(key);
            channel.Writer.TryComplete();
            res.Close();
        }
    }
}
