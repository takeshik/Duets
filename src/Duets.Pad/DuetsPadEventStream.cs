using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Duets.Pad;

/// <summary>Reads the unified server-sent event stream for one DuetsPad session.</summary>
public sealed class DuetsPadEventStream : IDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;
    private readonly Action<DuetsPadEventStream> _onDispose;
    private Task<string?>? _pendingRead;
    private List<string> _pendingDataLines = [];
    private string? _pendingEventName;
    private int _readInProgress;
    private int _disposed;

    internal DuetsPadEventStream(
        HttpResponseMessage response,
        Stream body,
        Action<DuetsPadEventStream> onDispose
    )
    {
        this._response = response;
        this._reader = new StreamReader(body, Encoding.UTF8);
        this._onDispose = onDispose;
    }

    /// <summary>
    /// Reads up to <paramref name="maxRecords"/> records without cancelling an in-flight stream
    /// read when <paramref name="timeout"/> expires.
    /// </summary>
    /// <param name="maxRecords">The maximum number of data or included comment records.</param>
    /// <param name="timeout">The maximum interval to wait.</param>
    /// <param name="includeComments">Whether SSE comments should be returned as records.</param>
    /// <param name="cancellationToken">Cancels the caller's wait.</param>
    /// <returns>The records read during the interval.</returns>
    /// <exception cref="InvalidOperationException">
    /// Another read is already active on this event stream.
    /// </exception>
    public async Task<DuetsPadSseReadResult> ReadAsync(
        int maxRecords,
        TimeSpan timeout,
        bool includeComments = false,
        CancellationToken cancellationToken = default
    )
    {
        if (Volatile.Read(ref this._disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(DuetsPadEventStream));
        }
        if (maxRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRecords));
        }

        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref this._readInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Only one read may be active on a DuetsPad event stream."
            );
        }

        try
        {
            return await this.ReadCoreAsync(maxRecords, timeout, includeComments, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref this._readInProgress, 0);
        }
    }

    private async Task<DuetsPadSseReadResult> ReadCoreAsync(
        int maxRecords,
        TimeSpan timeout,
        bool includeComments,
        CancellationToken cancellationToken
    )
    {
        var records = new List<DuetsPadSseRecord>();
        var commentsSkipped = 0;
        var dataLines = this.TakePendingDataLines();
        var eventName = this.TakePendingEventName();
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var deadline = Task.Delay(timeout, delayCancellation.Token);

        while (records.Count < maxRecords)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                this.SetPendingRecord(eventName, dataLines);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var readTask = this.TakePendingRead() ?? this._reader.ReadLineAsync();
            string? line;
            if (readTask.IsCompleted)
            {
                line = await readTask.ConfigureAwait(false);
            }
            else
            {
                var completed = await Task.WhenAny(readTask, deadline).ConfigureAwait(false);
                if (completed != readTask)
                {
                    // Cancelling the underlying read would break the HttpClient response stream.
                    // Carry it into the next call so no completed line is lost after a timeout.
                    this._pendingRead = readTask;
                    this.SetPendingRecord(eventName, dataLines);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new DuetsPadSseReadResult(
                        records,
                        TimedOut: true,
                        commentsSkipped,
                        Ended: false
                    );
                }

                line = await readTask.ConfigureAwait(false);
            }

            if (line is null)
            {
                delayCancellation.Cancel();
                return new DuetsPadSseReadResult(
                    records,
                    TimedOut: false,
                    commentsSkipped,
                    Ended: true
                );
            }

            if (line.Length == 0)
            {
                if (dataLines.Count == 0)
                {
                    continue;
                }

                var data = string.Join('\n', dataLines);
                records.Add(BuildDataRecord(eventName, data));
                dataLines.Clear();
                eventName = null;
                continue;
            }

            if (line.StartsWith(':'))
            {
                if (includeComments)
                {
                    records.Add(
                        new DuetsPadSseRecord(
                            "comment",
                            null,
                            null,
                            null,
                            RemoveOptionalLeadingSpace(line[1..])
                        )
                    );
                }
                else
                {
                    commentsSkipped++;
                }

                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            var field = separator >= 0 ? line[..separator] : line;
            var value = separator >= 0 ? RemoveOptionalLeadingSpace(line[(separator + 1)..]) : "";
            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "data":
                    dataLines.Add(value);
                    break;
            }
        }

        this.SetPendingRecord(eventName, dataLines);
        delayCancellation.Cancel();
        return new DuetsPadSseReadResult(records, TimedOut: false, commentsSkipped, Ended: false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
        {
            return;
        }

        // Observe the failure that disposing the stream may cause in an in-flight read so it does
        // not later surface as an unobserved task exception.
        this._pendingRead?.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
        this._pendingRead = null;
        this._reader.Dispose();
        this._response.Dispose();
        this._onDispose(this);
    }

    private static DuetsPadSseRecord BuildDataRecord(string? eventName, string data)
    {
        JsonNode? json = null;
        try
        {
            json = JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            // Raw SSE data remains available when it is not JSON.
        }

        return new DuetsPadSseRecord("data", eventName, data, json, null);
    }

    private static string RemoveOptionalLeadingSpace(string value) =>
        value.StartsWith(' ') ? value[1..] : value;

    private Task<string?>? TakePendingRead()
    {
        var pending = this._pendingRead;
        this._pendingRead = null;
        return pending;
    }

    private List<string> TakePendingDataLines()
    {
        var lines = this._pendingDataLines;
        this._pendingDataLines = [];
        return lines;
    }

    private string? TakePendingEventName()
    {
        var eventName = this._pendingEventName;
        this._pendingEventName = null;
        return eventName;
    }

    private void SetPendingRecord(string? eventName, List<string> dataLines)
    {
        this._pendingEventName = eventName;
        this._pendingDataLines = dataLines;
    }
}
