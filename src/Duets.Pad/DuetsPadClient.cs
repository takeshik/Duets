using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Duets.Pad.Protocol;

namespace Duets.Pad;

/// <summary>
/// Operates one caller-supplied existing DuetsPad session through its authenticated HTTP/SSE API.
/// This client never creates a session, and disposing it never deletes the target session.
/// </summary>
public sealed class DuetsPadClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseUri;
    private readonly string? _bearerCredential;
    private readonly object _streamsLock = new();
    private readonly HashSet<DuetsPadEventStream> _streams = [];
    private int _disposed;

    /// <summary>Creates a client for one existing session using an internally owned HTTP client.</summary>
    /// <param name="baseUri">The DuetsPad mount URI, including any route prefix.</param>
    /// <param name="sessionId">The existing session id supplied by the caller.</param>
    /// <param name="bearerCredential">The Bearer credential when authentication is configured.</param>
    public DuetsPadClient(Uri baseUri, string sessionId, string? bearerCredential = null)
        : this(CreateOwnedHttpClient(), baseUri, sessionId, bearerCredential, ownsHttpClient: true)
    { }

    /// <summary>Creates a client for one existing session using a caller-owned HTTP client.</summary>
    /// <param name="httpClient">The HTTP client used for requests.</param>
    /// <param name="baseUri">The DuetsPad mount URI, including any route prefix.</param>
    /// <param name="sessionId">The existing session id supplied by the caller.</param>
    /// <param name="bearerCredential">The Bearer credential when authentication is configured.</param>
    public DuetsPadClient(
        HttpClient httpClient,
        Uri baseUri,
        string sessionId,
        string? bearerCredential = null
    )
        : this(httpClient, baseUri, sessionId, bearerCredential, ownsHttpClient: false) { }

    private DuetsPadClient(
        HttpClient httpClient,
        Uri baseUri,
        string sessionId,
        string? bearerCredential,
        bool ownsHttpClient
    )
    {
        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (baseUri is null)
        {
            throw new ArgumentNullException(nameof(baseUri));
        }

        EnsureNotBlank(sessionId, nameof(sessionId));
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The DuetsPad base URI must be absolute.", nameof(baseUri));
        }
        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ArgumentException(
                "The DuetsPad base URI cannot contain a query or fragment.",
                nameof(baseUri)
            );
        }

        this._http = httpClient;
        this._ownsHttpClient = ownsHttpClient;
        this._baseUri = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        this.SessionId = sessionId;
        this._bearerCredential = bearerCredential;
    }

    /// <summary>Gets the caller-supplied target session id.</summary>
    public string SessionId { get; }

    /// <summary>Evaluates code in the target session.</summary>
    /// <param name="code">The TypeScript code to evaluate.</param>
    /// <param name="appendResult">Whether to append the immediate result to the Timeline.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadEvaluationResult>> EvaluateAsync(
        string code,
        bool appendResult = false,
        CancellationToken cancellationToken = default
    )
    {
        if (code is null)
        {
            throw new ArgumentNullException(nameof(code));
        }
        var path = this.SessionPath("eval");
        if (appendResult)
        {
            path += "?source=immediate";
        }

        using var request = this.CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(code, Encoding.UTF8, "text/plain");
        return await this.SendJsonAsync(
                request,
                (obj, accepted) =>
                    accepted && TryGetString(obj, "result", out var result)
                        ? new DuetsPadEvaluationResult(result)
                        : null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Requests registered tagged-template completions from the target session.</summary>
    /// <param name="request">The template context.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadCompletionResult>> CompleteAsync(
        DuetsPadCompletionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        var body = new JsonObject
        {
            ["tag"] = request.Tag,
            ["textBeforeCaret"] = request.TextBeforeCaret,
            ["textAfterCaret"] = request.TextAfterCaret,
            ["currentSegmentRaw"] = request.CurrentSegmentRaw,
            ["segmentIndex"] = 0,
            ["caretOffsetInSegment"] = request.CaretOffsetInSegment,
        };
        using var httpRequest = this.CreateRequest(HttpMethod.Post, this.SessionPath("complete"));
        httpRequest.Content = JsonContent(body);
        return await this.SendJsonAsync(httpRequest, ParseCompletion, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Retrieves a server-canonical Canvas snapshot.</summary>
    /// <param name="name">The Canvas name.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadCanvasSnapshot>> GetCanvasAsync(
        string name = "default",
        CancellationToken cancellationToken = default
    )
    {
        EnsureNotBlank(name, nameof(name));
        using var request = this.CreateRequest(
            HttpMethod.Get,
            this.SessionPath("canvas") + "?name=" + Uri.EscapeDataString(name)
        );
        return await this.SendJsonAsync(
                request,
                (obj, accepted) =>
                    accepted && IsCanvasSnapshot(obj)
                        ? new DuetsPadCanvasSnapshot((JsonObject)obj.DeepClone())
                        : null,
                cancellationToken,
                assumeOkWithoutFlag: true
            )
            .ConfigureAwait(false);
    }

    /// <summary>Invokes an opaque interaction handler in the target session.</summary>
    /// <param name="handlerId">The handler id from a projected surface.</param>
    /// <param name="snapshot">Optional field and attachment state submitted with the invocation.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadInteractionResult>> InvokeInteractionAsync(
        string handlerId,
        DuetsPadInteractionSnapshot? snapshot = null,
        CancellationToken cancellationToken = default
    )
    {
        EnsureNotBlank(handlerId, nameof(handlerId));
        using var request = this.CreateRequest(
            HttpMethod.Post,
            this.SessionPath($"interactions/{Uri.EscapeDataString(handlerId)}/invoke")
        );
        request.Content = JsonContent(BuildInteractionSnapshot(snapshot));
        return await this.SendJsonAsync(request, ParseInteraction, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Commits a field value using server-order last-write-wins semantics.</summary>
    /// <param name="fieldId">The projected field id.</param>
    /// <param name="value">The whole field value.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadFieldCommitResult>> CommitFieldAsync(
        string fieldId,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        EnsureNotBlank(fieldId, nameof(fieldId));
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
        using var request = this.CreateRequest(
            HttpMethod.Post,
            this.SessionPath($"fields/{Uri.EscapeDataString(fieldId)}/commit")
        );
        request.Content = new StringContent(value, Encoding.UTF8, "text/plain");
        return await this.SendJsonAsync(
                request,
                (obj, accepted) =>
                    accepted && TryGetString(obj, "fieldId", out var committedFieldId)
                        ? new DuetsPadFieldCommitResult(committedFieldId)
                        : null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Begins a transactional attachment selection.</summary>
    /// <param name="pickerId">The projected file-picker id.</param>
    /// <param name="clientId">A stable non-empty id for the submitting client.</param>
    /// <param name="generation">A monotonically increasing positive generation.</param>
    /// <param name="files">The selected file manifest.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<
        DuetsPadClientResult<DuetsPadAttachmentSelectionResult>
    > BeginAttachmentSelectionAsync(
        string pickerId,
        Guid clientId,
        long generation,
        IReadOnlyList<DuetsPadAttachmentFile> files,
        CancellationToken cancellationToken = default
    )
    {
        EnsureNotBlank(pickerId, nameof(pickerId));
        if (files is null)
        {
            throw new ArgumentNullException(nameof(files));
        }
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException(
                "The attachment client id cannot be empty.",
                nameof(clientId)
            );
        }

        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        var fileArray = new JsonArray();
        foreach (var file in files)
        {
            fileArray.Add(
                new JsonObject
                {
                    ["name"] = file.Name,
                    ["contentType"] = file.ContentType,
                    ["size"] = file.Size,
                }
            );
        }

        var body = new JsonObject
        {
            ["clientId"] = clientId.ToString("D"),
            ["generation"] = generation,
            ["files"] = fileArray,
        };
        using var request = this.CreateRequest(
            HttpMethod.Post,
            this.AttachmentSelectionsPath(pickerId)
        );
        request.Content = JsonContent(body);
        return await this.SendJsonAsync(request, ParseAttachmentSelection, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Streams one attachment file into an open selection.</summary>
    /// <param name="pickerId">The projected file-picker id.</param>
    /// <param name="token">The selection token.</param>
    /// <param name="fileId">The server-issued file id.</param>
    /// <param name="content">The file stream, which remains owned by the caller.</param>
    /// <param name="contentType">The media type sent with the upload.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<
        DuetsPadClientResult<DuetsPadAttachmentOperationResult>
    > UploadAttachmentFileAsync(
        string pickerId,
        string token,
        string fileId,
        Stream content,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }
        using var request = this.CreateRequest(
            HttpMethod.Post,
            this.AttachmentSelectionPath(pickerId, token) + "/files/" + Uri.EscapeDataString(fileId)
        );
        request.Content = new StreamContent(new NonDisposingStream(content));
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return await this.SendJsonAsync(request, ParseAttachmentOperation, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Commits an attachment selection after all files have been uploaded.</summary>
    /// <param name="pickerId">The projected file-picker id.</param>
    /// <param name="token">The selection token.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public Task<
        DuetsPadClientResult<DuetsPadAttachmentOperationResult>
    > CommitAttachmentSelectionAsync(
        string pickerId,
        string token,
        CancellationToken cancellationToken = default
    ) =>
        this.SendAttachmentOperationAsync(
            HttpMethod.Post,
            this.AttachmentSelectionPath(pickerId, token) + "/commit",
            cancellationToken,
            includeEmptyBody: true
        );

    /// <summary>Cancels an open attachment selection.</summary>
    /// <param name="pickerId">The projected file-picker id.</param>
    /// <param name="token">The selection token.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public Task<
        DuetsPadClientResult<DuetsPadAttachmentOperationResult>
    > CancelAttachmentSelectionAsync(
        string pickerId,
        string token,
        CancellationToken cancellationToken = default
    ) =>
        this.SendAttachmentOperationAsync(
            HttpMethod.Delete,
            this.AttachmentSelectionPath(pickerId, token),
            cancellationToken
        );

    /// <summary>Cancels the failed attachment selection at an expected revision.</summary>
    /// <param name="pickerId">The projected file-picker id.</param>
    /// <param name="revision">The expected failed revision.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public Task<
        DuetsPadClientResult<DuetsPadAttachmentOperationResult>
    > CancelFailedAttachmentSelectionAsync(
        string pickerId,
        long revision,
        CancellationToken cancellationToken = default
    )
    {
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        return this.SendAttachmentOperationAsync(
            HttpMethod.Delete,
            this.AttachmentSelectionsPath(pickerId)
                + "/failed?revision="
                + revision.ToString(CultureInfo.InvariantCulture),
            cancellationToken
        );
    }

    /// <summary>Retrieves the last committed Editor text.</summary>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadEditorText>> GetEditorTextAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var request = this.CreateRequest(HttpMethod.Get, this.SessionPath("editor"));
        return await this.SendJsonAsync(
                request,
                (obj, accepted) =>
                    accepted && TryGetString(obj, "text", out var text)
                        ? new DuetsPadEditorText(text)
                        : null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Replaces the whole Editor document using server-order last-write-wins semantics.</summary>
    /// <param name="text">The complete replacement document.</param>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadEmptyResult>> ReplaceEditorTextAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }
        using var request = this.CreateRequest(HttpMethod.Put, this.SessionPath("editor"));
        request.Content = new StringContent(text, Encoding.UTF8, "text/plain");
        return await this.SendJsonAsync(
                request,
                (_, accepted) => accepted ? new DuetsPadEmptyResult() : null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Explicitly deletes the target session.</summary>
    /// <param name="cancellationToken">Cancels the HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadEmptyResult>> DeleteSessionAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var request = this.CreateRequest(HttpMethod.Delete, this.SessionPath());
        return await this.SendJsonAsync(
                request,
                (_, accepted) => accepted ? new DuetsPadEmptyResult() : null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Opens the target session's optional unified SSE stream.</summary>
    /// <param name="cancellationToken">Cancels the stream-opening HTTP operation.</param>
    public async Task<DuetsPadClientResult<DuetsPadEventStream>> OpenEventsAsync(
        CancellationToken cancellationToken = default
    )
    {
        this.ThrowIfDisposed();
        using var request = this.CreateRequest(HttpMethod.Get, this.SessionPath("events"));
        var response = await this
            ._http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (
            !response.IsSuccessStatusCode
            || !string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "text/event-stream",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            using (response)
            {
                return await ReadJsonResultAsync<DuetsPadEventStream>(
                        response,
                        (_, _) => null,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        }

        Stream body;
        try
        {
            body = await ReadContentAsStreamAsync(response.Content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
        var stream = new DuetsPadEventStream(response, body, this.RemoveStream);
        lock (this._streamsLock)
        {
            if (Volatile.Read(ref this._disposed) == 1)
            {
                stream.Dispose();
                throw new ObjectDisposedException(nameof(DuetsPadClient));
            }

            this._streams.Add(stream);
        }

        return new DuetsPadClientResult<DuetsPadEventStream>(
            response.StatusCode,
            ok: true,
            error: null,
            this.SessionId,
            stream
        );
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
        {
            return;
        }

        DuetsPadEventStream[] streams;
        lock (this._streamsLock)
        {
            streams = [.. this._streams];
            this._streams.Clear();
        }

        foreach (var stream in streams)
        {
            stream.Dispose();
        }

        if (this._ownsHttpClient)
        {
            this._http.Dispose();
        }
    }

    private async Task<
        DuetsPadClientResult<DuetsPadAttachmentOperationResult>
    > SendAttachmentOperationAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        bool includeEmptyBody = false
    )
    {
        using var request = this.CreateRequest(method, path);
        if (includeEmptyBody)
        {
            request.Content = new StringContent("", Encoding.UTF8, "application/json");
        }

        return await this.SendJsonAsync(request, ParseAttachmentOperation, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DuetsPadClientResult<T>> SendJsonAsync<T>(
        HttpRequestMessage request,
        Func<JsonObject, bool, T?> parseValue,
        CancellationToken cancellationToken,
        bool assumeOkWithoutFlag = false
    )
    {
        this.ThrowIfDisposed();
        using var response = await this
            ._http.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonResultAsync(
                response,
                parseValue,
                cancellationToken,
                assumeOkWithoutFlag
            )
            .ConfigureAwait(false);
    }

    private static async Task<DuetsPadClientResult<T>> ReadJsonResultAsync<T>(
        HttpResponseMessage response,
        Func<JsonObject, bool, T?> parseValue,
        CancellationToken cancellationToken,
        bool assumeOkWithoutFlag = false
    )
    {
        var text = await ReadContentAsStringAsync(response.Content, cancellationToken)
            .ConfigureAwait(false);
        JsonObject? body = null;
        try
        {
            body = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            // Report a protocol error below while preserving the HTTP status.
        }

        if (body is null)
        {
            return new DuetsPadClientResult<T>(
                response.StatusCode,
                ok: false,
                "DuetsPad returned a malformed JSON response.",
                sessionId: null,
                value: default
            );
        }

        var ok = false;
        var hasOk = body["ok"] is JsonValue okValue && okValue.TryGetValue(out ok);
        var accepted = response.IsSuccessStatusCode && (hasOk ? ok : assumeOkWithoutFlag);
        var value = parseValue(body, accepted);
        if (accepted && value is null)
        {
            return new DuetsPadClientResult<T>(
                response.StatusCode,
                ok: false,
                "DuetsPad returned an invalid operation payload.",
                GetString(body, "sessionId"),
                value: default
            );
        }

        return new DuetsPadClientResult<T>(
            response.StatusCode,
            accepted,
            GetString(body, "error"),
            GetString(body, "sessionId"),
            value
        );
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(this._baseUri, relativePath));
        if (this._bearerCredential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                this._bearerCredential
            );
        }

        return request;
    }

    private string SessionPath(string? suffix = null)
    {
        var path = "sessions/" + Uri.EscapeDataString(this.SessionId);
        return string.IsNullOrEmpty(suffix) ? path : path + "/" + suffix;
    }

    private string AttachmentSelectionsPath(string pickerId)
    {
        EnsureNotBlank(pickerId, nameof(pickerId));
        return this.SessionPath($"attachments/{Uri.EscapeDataString(pickerId)}/selections");
    }

    private string AttachmentSelectionPath(string pickerId, string token)
    {
        EnsureNotBlank(token, nameof(token));
        return this.AttachmentSelectionsPath(pickerId) + "/" + Uri.EscapeDataString(token);
    }

    private void RemoveStream(DuetsPadEventStream stream)
    {
        lock (this._streamsLock)
        {
            this._streams.Remove(stream);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref this._disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(DuetsPadClient));
        }
    }

    private static void EnsureNotBlank(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty or whitespace.", parameterName);
        }
    }

    private static StringContent JsonContent(JsonObject value) =>
        new(value.ToJsonString(), Encoding.UTF8, "application/json");

    private static HttpClient CreateOwnedHttpClient() =>
        new() { Timeout = Timeout.InfiniteTimeSpan };

    private static Task<Stream> ReadContentAsStreamAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
#if NET8_0_OR_GREATER
        return content.ReadAsStreamAsync(cancellationToken);
#else
        return WaitWithCancellationAsync(content.ReadAsStreamAsync(), cancellationToken);
#endif
    }

    private static Task<string> ReadContentAsStringAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
#if NET8_0_OR_GREATER
        return content.ReadAsStringAsync(cancellationToken);
#else
        return WaitWithCancellationAsync(content.ReadAsStringAsync(), cancellationToken);
#endif
    }

#if !NET8_0_OR_GREATER
    private static async Task<T> WaitWithCancellationAsync<T>(
        Task<T> operation,
        CancellationToken cancellationToken
    )
    {
        if (!cancellationToken.CanBeCanceled || operation.IsCompleted)
        {
            return await operation.ConfigureAwait(false);
        }

        var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (await Task.WhenAny(operation, cancellation).ConfigureAwait(false) != operation)
        {
            _ = operation.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously
                    | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await operation.ConfigureAwait(false);
    }
#endif

    private static JsonObject BuildInteractionSnapshot(DuetsPadInteractionSnapshot? snapshot)
    {
        var body = new JsonObject();
        if (snapshot?.Fields is { } fields)
        {
            var fieldObject = new JsonObject();
            foreach (var pair in fields)
            {
                fieldObject[pair.Key.ToString("D")] = pair.Value;
            }

            body["fields"] = fieldObject;
        }

        if (snapshot?.Attachments is { } attachments)
        {
            var attachmentObject = new JsonObject();
            foreach (var pair in attachments)
            {
                attachmentObject[pair.Key.ToString("D")] = pair.Value;
            }

            body["attachments"] = attachmentObject;
        }

        return body;
    }

    private static DuetsPadCompletionResult? ParseCompletion(JsonObject obj, bool _)
    {
        if (
            obj["items"] is not JsonArray array
            || !TryGetBoolean(obj, "stale", out var stale)
            || !TryGetBoolean(obj, "timedOut", out var timedOut)
        )
        {
            return null;
        }

        var items = new List<DuetsPadCompletionItem>();
        foreach (var itemNode in array)
        {
            if (
                itemNode is not JsonObject node
                || !TryGetString(node, "label", out var label)
                || !TryGetString(node, "kind", out var kind)
                || !HasOptionalString(node, "insertText")
            )
            {
                return null;
            }

            DuetsPadCompletionSpan? span = null;
            if (node["replacementSpan"] is not null)
            {
                if (
                    node["replacementSpan"] is not JsonObject spanObject
                    || !TryGetInt32(spanObject, "start", out var start)
                    || !TryGetInt32(spanObject, "length", out var length)
                )
                {
                    return null;
                }

                span = new DuetsPadCompletionSpan(start, length);
            }

            items.Add(
                new DuetsPadCompletionItem(
                    label,
                    GetString(node, "insertText"),
                    kind,
                    GetString(node, "filterText"),
                    GetString(node, "sortText"),
                    GetString(node, "detail"),
                    GetString(node, "documentation"),
                    span
                )
            );
        }

        return new DuetsPadCompletionResult(items, stale, timedOut);
    }

    private static DuetsPadInteractionResult? ParseInteraction(JsonObject obj, bool _)
    {
        if (
            !TryGetString(obj, "handlerId", out var handlerId)
            || !TryGetBoolean(obj, "stale", out var stale)
            || !TryGetBoolean(obj, "attachmentConflict", out var attachmentConflict)
        )
        {
            return null;
        }

        return new DuetsPadInteractionResult(handlerId, stale, attachmentConflict);
    }

    private static DuetsPadAttachmentSelectionResult? ParseAttachmentSelection(
        JsonObject obj,
        bool accepted
    )
    {
        if (
            !TryGetString(obj, "pickerId", out var pickerId)
            || !TryGetInt64(obj, "revision", out var revision)
            || obj["files"] is not JsonArray array
        )
        {
            return null;
        }

        var token = GetString(obj, "token");
        if (accepted && string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var files = new List<DuetsPadAcceptedAttachmentFile>();
        foreach (var fileNode in array)
        {
            if (
                fileNode is not JsonObject node
                || !TryGetString(node, "id", out var id)
                || !TryGetString(node, "name", out var name)
                || !TryGetString(node, "contentType", out var contentType)
                || !TryGetInt64(node, "size", out var size)
            )
            {
                return null;
            }

            files.Add(new DuetsPadAcceptedAttachmentFile(id, name, contentType, size));
        }

        return new DuetsPadAttachmentSelectionResult(pickerId, token, revision, files);
    }

    private static DuetsPadAttachmentOperationResult? ParseAttachmentOperation(
        JsonObject obj,
        bool _
    )
    {
        return
            TryGetString(obj, "pickerId", out var pickerId)
            && TryGetInt64(obj, "revision", out var revision)
            && TryGetBoolean(obj, "stale", out var stale)
            ? new DuetsPadAttachmentOperationResult(pickerId, revision, stale)
            : null;
    }

    private static bool IsCanvasSnapshot(JsonObject obj) =>
        TryGetString(obj, "type", out var type)
        && string.Equals(type, CanvasEventTypes.Snapshot, StringComparison.Ordinal)
        && TryGetString(obj, "name", out _)
        && TryGetInt64(obj, "revision", out _)
        && obj["state"] is JsonObject
        && obj["interactions"] is JsonArray;

    private static string? GetString(JsonObject obj, string propertyName) =>
        obj[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : null;

    private static bool HasOptionalString(JsonObject obj, string propertyName) =>
        obj.ContainsKey(propertyName)
        && (obj[propertyName] is null || TryGetString(obj, propertyName, out _));

    private static bool TryGetString(JsonObject obj, string propertyName, out string result)
    {
        if (
            obj[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var candidate)
            && candidate is not null
        )
        {
            result = candidate;
            return true;
        }

        result = "";
        return false;
    }

    private static bool TryGetBoolean(JsonObject obj, string propertyName, out bool result)
    {
        result = false;
        return obj[propertyName] is JsonValue value && value.TryGetValue(out result);
    }

    private static bool TryGetInt32(JsonObject obj, string propertyName, out int result)
    {
        result = 0;
        return obj[propertyName] is JsonValue value && value.TryGetValue(out result);
    }

    private static bool TryGetInt64(JsonObject obj, string propertyName, out long result)
    {
        result = 0;
        return obj[propertyName] is JsonValue value && value.TryGetValue(out result);
    }

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => inner.ReadAsync(buffer, cancellationToken);

        public override Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken
        ) => inner.CopyToAsync(destination, bufferSize, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
