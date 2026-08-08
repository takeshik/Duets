using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Duets.Pad;

namespace Duets.Sandbox;

/// <summary>Adapts the public session-scoped client to Sandbox JSONL records.</summary>
internal sealed class DuetsPadProtocolClient : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Dictionary<string, DuetsPadEventStream> _sseStreams = [];
    private DuetsPadClient? _client;
    private Uri? _localServiceUri;
    private string? _localServiceBearerCredential;
    private bool _targetUsesLocalService;

    public bool HasTarget => this._client is not null;

    public string? TargetSessionId => this._client?.SessionId;

    public void SetLocalService(Uri baseUri, string? bearerCredential)
    {
        this._localServiceUri = baseUri;
        this._localServiceBearerCredential = bearerCredential;
    }

    public void ClearLocalService()
    {
        if (this._targetUsesLocalService)
        {
            this.CloseTarget();
        }

        this._localServiceUri = null;
        this._localServiceBearerCredential = null;
    }

    public JsonObject Target(Uri baseUri, string sessionId, string? bearerCredential)
    {
        this.CloseTarget();
        this._client = new DuetsPadClient(baseUri, sessionId, bearerCredential);
        this._targetUsesLocalService = false;
        return new JsonObject
        {
            ["ok"] = true,
            ["baseUri"] = baseUri.ToString(),
            ["sessionId"] = sessionId,
        };
    }

    /// <summary>
    /// Preserves the repository-only local-server creation operation. Creation is deliberately
    /// performed outside the public existing-session client, then the returned id becomes the
    /// retained target for subsequent operations.
    /// </summary>
    public async Task<JsonObject> CreateLocalSessionAsync(string? sessionId)
    {
        var baseUri =
            this._localServiceUri
            ?? throw new InvalidOperationException("The DuetsPad server is not running.");
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "sessions"));
        if (this._localServiceBearerCredential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                this._localServiceBearerCredential
            );
        }

        var body = sessionId is null ? [] : new JsonObject { ["sessionId"] = sessionId };
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        var result = await ReadJsonObjectAsync(response);
        if (
            response.IsSuccessStatusCode
            && result["sessionId"] is JsonValue value
            && value.TryGetValue<string>(out var createdId)
        )
        {
            this.Target(baseUri, createdId, this._localServiceBearerCredential);
            this._targetUsesLocalService = true;
        }

        result["httpOk"] = response.IsSuccessStatusCode;
        result["statusCode"] = (int)response.StatusCode;
        return result;
    }

    public async Task<JsonObject> DeleteSessionAsync()
    {
        var result = await this.RequireClient().DeleteSessionAsync();
        return ToJson(result);
    }

    public async Task<JsonObject> EvaluateAsync(string code, bool appendResult)
    {
        var result = await this.RequireClient().EvaluateAsync(code, appendResult);
        return ToJson(result);
    }

    public async Task<JsonObject> CompleteAsync(DuetsPadCompletionRequest request)
    {
        var result = await this.RequireClient().CompleteAsync(request);
        return ToJson(result);
    }

    public async Task<JsonObject> GetCanvasAsync(string name)
    {
        var result = await this.RequireClient().GetCanvasAsync(name);
        return ToJson(result);
    }

    public async Task<JsonObject> InvokeInteractionAsync(
        string handlerId,
        DuetsPadInteractionSnapshot? snapshot
    )
    {
        var result = await this.RequireClient().InvokeInteractionAsync(handlerId, snapshot);
        return ToJson(result);
    }

    public async Task<JsonObject> CommitFieldAsync(string fieldId, string value)
    {
        var result = await this.RequireClient().CommitFieldAsync(fieldId, value);
        return ToJson(result);
    }

    public async Task<JsonObject> BeginAttachmentSelectionAsync(
        string pickerId,
        Guid clientId,
        long generation,
        IReadOnlyList<DuetsPadAttachmentFile> files
    )
    {
        var result = await this.RequireClient()
            .BeginAttachmentSelectionAsync(pickerId, clientId, generation, files);
        return ToJson(result);
    }

    public async Task<JsonObject> UploadAttachmentFileAsync(
        string pickerId,
        string token,
        string fileId,
        byte[] content,
        string contentType
    )
    {
        using var stream = new MemoryStream(content, writable: false);
        var result = await this.RequireClient()
            .UploadAttachmentFileAsync(pickerId, token, fileId, stream, contentType);
        return ToJson(result);
    }

    public async Task<JsonObject> CommitAttachmentSelectionAsync(string pickerId, string token)
    {
        var result = await this.RequireClient().CommitAttachmentSelectionAsync(pickerId, token);
        return ToJson(result);
    }

    public async Task<JsonObject> CancelAttachmentSelectionAsync(string pickerId, string token)
    {
        var result = await this.RequireClient().CancelAttachmentSelectionAsync(pickerId, token);
        return ToJson(result);
    }

    public async Task<JsonObject> CancelFailedAttachmentSelectionAsync(
        string pickerId,
        long revision
    )
    {
        var result = await this.RequireClient()
            .CancelFailedAttachmentSelectionAsync(pickerId, revision);
        return ToJson(result);
    }

    public async Task<JsonObject> GetEditorTextAsync()
    {
        var result = await this.RequireClient().GetEditorTextAsync();
        return ToJson(result);
    }

    public async Task<JsonObject> ReplaceEditorTextAsync(string text)
    {
        var result = await this.RequireClient().ReplaceEditorTextAsync(text);
        return ToJson(result);
    }

    public async Task<JsonObject> OpenSseAsync(string streamId)
    {
        if (this._sseStreams.ContainsKey(streamId))
        {
            throw new InvalidOperationException($"SSE stream already exists: {streamId}");
        }

        var result = await this.RequireClient().OpenEventsAsync();
        if (!result.Ok || result.Value is null)
        {
            return ToJson(result);
        }

        this._sseStreams.Add(streamId, result.Value);
        return new JsonObject
        {
            ["ok"] = true,
            ["streamId"] = streamId,
            ["stream"] = "events",
            ["statusCode"] = (int)result.StatusCode,
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

        var result = await stream.ReadAsync(
            maxRecords,
            TimeSpan.FromMilliseconds(timeoutMs),
            includeComments
        );
        var node = JsonSerializer.SerializeToNode(result, _jsonOptions)!.AsObject();
        node["ok"] = !result.Ended;
        node["streamId"] = streamId;
        node["stream"] = "events";
        if (result.Ended)
        {
            node["error"] = "SSE stream ended.";
        }

        return node;
    }

    public JsonObject ListSseStreams()
    {
        var streams = new JsonArray();
        foreach (var streamId in this._sseStreams.Keys)
        {
            streams.Add(new JsonObject { ["streamId"] = streamId, ["stream"] = "events" });
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
        this.CloseTarget();
        this.ClearLocalService();
    }

    private static JsonObject ToJson<T>(DuetsPadClientResult<T> result)
    {
        var node = result.Value is null
            ? []
            : JsonSerializer.SerializeToNode(result.Value, _jsonOptions)?.AsObject() ?? [];
        node["ok"] = result.Ok;
        node["error"] = result.Error;
        node["sessionId"] = result.SessionId;
        node["httpOk"] = (int)result.StatusCode is >= 200 and <= 299;
        node["statusCode"] = (int)result.StatusCode;
        return node;
    }

    private static async Task<JsonObject> ReadJsonObjectAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? new JsonObject { ["body"] = text };
        }
        catch (JsonException)
        {
            return new JsonObject { ["body"] = text };
        }
    }

    private DuetsPadClient RequireClient() =>
        this._client
        ?? throw new InvalidOperationException(
            "No DuetsPad target is configured. Use pad-target or pad-session-create first."
        );

    private void CloseTarget()
    {
        foreach (var stream in this._sseStreams.Values)
        {
            stream.Dispose();
        }

        this._sseStreams.Clear();
        this._client?.Dispose();
        this._client = null;
        this._targetUsesLocalService = false;
    }
}
