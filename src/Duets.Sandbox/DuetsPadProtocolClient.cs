using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Duets.Sandbox;

internal sealed class DuetsPadProtocolClient(Uri baseUri) : IDisposable
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = baseUri,
        Timeout = TimeSpan.FromSeconds(10),
    };
    private readonly Dictionary<string, OpenSseStream> _sseStreams = [];

    public async Task<JsonObject> CreateSessionAsync(string? sessionId)
    {
        var body = sessionId is null ? [] : new JsonObject { ["sessionId"] = sessionId };
        using var response = await this._http.PostAsync(
            "sessions",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        );

        return await ReadJsonResponseAsync(response);
    }

    public async Task<JsonObject> DeleteSessionAsync(string sessionId)
    {
        using var response = await this._http.DeleteAsync($"sessions/{sessionId}");
        return await ReadJsonResponseAsync(response);
    }

    public async Task<JsonObject> EvaluateAsync(string sessionId, string code, string? source)
    {
        var path = $"sessions/{sessionId}/eval";
        if (!string.IsNullOrWhiteSpace(source))
        {
            path += $"?source={Uri.EscapeDataString(source)}";
        }

        using var response = await this._http.PostAsync(
            path,
            new StringContent(code, Encoding.UTF8, "text/plain")
        );
        return await ReadJsonResponseAsync(response);
    }

    public async Task<JsonObject> InvokeInteractionAsync(string sessionId, string handlerId)
    {
        using var response = await this._http.PostAsync(
            $"sessions/{sessionId}/interactions/{handlerId}/invoke",
            content: null
        );
        return await ReadJsonResponseAsync(response);
    }

    public async Task<JsonObject> OpenSseAsync(string streamId, string sessionId, string stream)
    {
        if (this._sseStreams.ContainsKey(streamId))
        {
            throw new InvalidOperationException($"SSE stream already exists: {streamId}");
        }

        if (!DuetsPadStreamKind.TryParse(stream, out var streamKind))
        {
            throw new ArgumentException(
                $"stream must be one of: {string.Join(", ", DuetsPadStreamKind.AllTokens)}",
                nameof(stream)
            );
        }

        var path = streamKind.BuildRelativePath(sessionId);

        var response = await this._http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadJsonResponseAsync(response);
            response.Dispose();
            return error;
        }

        var body = await response.Content.ReadAsStreamAsync();
        var reader = new StreamReader(body, Encoding.UTF8);
        this._sseStreams.Add(streamId, new OpenSseStream(streamId, stream, response, reader));

        return new JsonObject
        {
            ["ok"] = true,
            ["streamId"] = streamId,
            ["stream"] = stream,
            ["statusCode"] = (int)response.StatusCode,
        };
    }

    public async Task<JsonObject> ReadSseAsync(
        string streamId,
        int maxRecords,
        int timeoutMs,
        bool includeComments
    )
    {
        if (!this._sseStreams.TryGetValue(streamId, out var stream))
        {
            throw new InvalidOperationException($"SSE stream does not exist: {streamId}");
        }

        if (maxRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRecords),
                "maxRecords must be positive."
            );
        }

        if (timeoutMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutMs),
                "timeoutMs cannot be negative."
            );
        }

        var records = new JsonArray();
        var commentsSkipped = 0;
        var deadline = Task.Delay(TimeSpan.FromMilliseconds(timeoutMs));
        var dataLines = stream.TakePendingDataLines();
        var eventName = stream.TakePendingEventName();

        while (records.Count < maxRecords)
        {
            // Reuse a read started by a previous timed-out call, if any, so no line is dropped.
            // The read task is never cancelled: cancelling it would tear down the underlying
            // HttpClient response stream and make every later read on this stream fail.
            var readTask = stream.TakePendingRead() ?? stream.Reader.ReadLineAsync();

            string? line;
            if (readTask.IsCompleted)
            {
                line = await readTask;
            }
            else
            {
                var completed = await Task.WhenAny(readTask, deadline);
                if (completed != readTask)
                {
                    // Timed out: hand the in-flight read to the next call instead of cancelling it.
                    stream.SetPendingRead(readTask);
                    stream.SetPendingRecord(eventName, dataLines);
                    return new JsonObject
                    {
                        ["ok"] = true,
                        ["streamId"] = streamId,
                        ["stream"] = stream.Stream,
                        ["timedOut"] = true,
                        ["commentsSkipped"] = commentsSkipped,
                        ["records"] = records,
                    };
                }

                line = await readTask;
            }

            if (line is null)
            {
                return new JsonObject
                {
                    ["ok"] = false,
                    ["streamId"] = streamId,
                    ["stream"] = stream.Stream,
                    ["error"] = "SSE stream ended.",
                    ["commentsSkipped"] = commentsSkipped,
                    ["records"] = records,
                };
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
                        new JsonObject { ["kind"] = "comment", ["comment"] = line[1..].TrimStart() }
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
            var value = separator >= 0 ? line[(separator + 1)..].TrimStart() : "";
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

        return new JsonObject
        {
            ["ok"] = true,
            ["streamId"] = streamId,
            ["stream"] = stream.Stream,
            ["timedOut"] = false,
            ["commentsSkipped"] = commentsSkipped,
            ["records"] = records,
        };
    }

    public JsonObject ListSseStreams()
    {
        var streams = new JsonArray();
        foreach (var stream in this._sseStreams.Values)
        {
            streams.Add(
                new JsonObject { ["streamId"] = stream.StreamId, ["stream"] = stream.Stream }
            );
        }

        return new JsonObject { ["ok"] = true, ["streams"] = streams };
    }

    public JsonObject CloseSse(string streamId)
    {
        if (!this._sseStreams.Remove(streamId, out var stream))
        {
            throw new InvalidOperationException($"SSE stream does not exist: {streamId}");
        }

        stream.Dispose();
        return new JsonObject { ["ok"] = true, ["streamId"] = streamId };
    }

    public void Dispose()
    {
        foreach (var stream in this._sseStreams.Values)
        {
            stream.Dispose();
        }

        this._sseStreams.Clear();
        this._http.Dispose();
    }

    private static JsonObject BuildDataRecord(string? eventName, string data)
    {
        var record = new JsonObject
        {
            ["kind"] = "data",
            ["event"] = eventName,
            ["data"] = data,
        };

        try
        {
            record["json"] = JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            // Keep the raw data field for non-JSON SSE payloads.
        }

        return record;
    }

    private static async Task<JsonObject> ReadJsonResponseAsync(HttpResponseMessage response)
    {
        JsonObject body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<JsonObject>() ?? [];
        }
        catch (JsonException)
        {
            body = new JsonObject { ["body"] = await response.Content.ReadAsStringAsync() };
        }

        body["httpOk"] = response.IsSuccessStatusCode;
        body["statusCode"] = (int)response.StatusCode;
        return body;
    }

    private sealed class OpenSseStream(
        string streamId,
        string stream,
        HttpResponseMessage response,
        StreamReader reader
    ) : IDisposable
    {
        public string StreamId { get; } = streamId;

        public string Stream { get; } = stream;

        public StreamReader Reader { get; } = reader;

        // A read started by a timed-out ReadSseAsync call. The underlying read is never
        // cancelled (that would break the HttpClient response stream), so the in-flight task
        // is carried here and resumed by the next call to avoid dropping a line.
        private Task<string?>? _pendingRead;
        private List<string> _pendingDataLines = [];
        private string? _pendingEventName;

        /// <summary>
        /// Returns the in-flight read carried over from a previous timed-out call, clearing it,
        /// or <see langword="null"/> when no read is pending.
        /// </summary>
        public Task<string?>? TakePendingRead()
        {
            var pending = this._pendingRead;
            this._pendingRead = null;
            return pending;
        }

        /// <summary>
        /// Stores an in-flight read so the next call can resume it instead of starting a new one.
        /// </summary>
        public void SetPendingRead(Task<string?> readTask) => this._pendingRead = readTask;

        /// <summary>
        /// Returns data lines parsed before a timeout, clearing the stored partial record.
        /// </summary>
        public List<string> TakePendingDataLines()
        {
            var lines = this._pendingDataLines;
            this._pendingDataLines = [];
            return lines;
        }

        /// <summary>
        /// Returns the event name parsed before a timeout, clearing the stored value.
        /// </summary>
        public string? TakePendingEventName()
        {
            var eventName = this._pendingEventName;
            this._pendingEventName = null;
            return eventName;
        }

        /// <summary>
        /// Stores the partial SSE record parsed before a timed-out read.
        /// </summary>
        public void SetPendingRecord(string? eventName, List<string> dataLines)
        {
            this._pendingEventName = eventName;
            this._pendingDataLines = dataLines;
        }

        public void Dispose()
        {
            // Observe any in-flight read so its eventual failure (e.g. the disposed-stream
            // exception this Dispose triggers) does not surface as an unobserved task exception.
            this._pendingRead?.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
            this._pendingRead = null;

            this.Reader.Dispose();
            response.Dispose();
        }
    }
}
