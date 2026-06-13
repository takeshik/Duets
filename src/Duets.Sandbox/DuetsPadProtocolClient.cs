using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Duets.Sandbox;

internal sealed class DuetsPadProtocolClient(Uri baseUri) : IAsyncDisposable
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

        var path = stream switch
        {
            "canvas" => $"sessions/{sessionId}/canvas-events",
            "timeline" => $"sessions/{sessionId}/timeline-events",
            "type-declarations" => $"type-declaration-events?sessionId={sessionId}",
            _ => throw new ArgumentException(
                "stream must be one of: canvas, timeline, type-declarations",
                nameof(stream)
            ),
        };

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
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        var dataLines = new List<string>();
        string? eventName = null;

        while (records.Count < maxRecords)
        {
            string? line;
            try
            {
                line = await stream.Reader.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
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

    public async ValueTask DisposeAsync()
    {
        foreach (var stream in this._sseStreams.Values)
        {
            stream.Dispose();
        }

        this._sseStreams.Clear();
        this._http.Dispose();
        await ValueTask.CompletedTask;
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

        public void Dispose()
        {
            this.Reader.Dispose();
            response.Dispose();
        }
    }
}
