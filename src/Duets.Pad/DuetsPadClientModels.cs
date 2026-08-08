using System.Net;
using System.Text.Json.Nodes;

namespace Duets.Pad;

/// <summary>Represents the HTTP and protocol outcome of a DuetsPad client operation.</summary>
/// <typeparam name="T">The operation-specific payload type.</typeparam>
public sealed class DuetsPadClientResult<T>
{
    internal DuetsPadClientResult(
        HttpStatusCode statusCode,
        bool ok,
        string? error,
        string? sessionId,
        T? value
    )
    {
        this.StatusCode = statusCode;
        this.Ok = ok;
        this.Error = error;
        this.SessionId = sessionId;
        this.Value = value;
    }

    /// <summary>Gets the HTTP response status.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Gets whether the server accepted the operation.</summary>
    public bool Ok { get; }

    /// <summary>Gets the protocol error, or <see langword="null"/> on success.</summary>
    public string? Error { get; }

    /// <summary>Gets the session id returned by the server, when present.</summary>
    public string? SessionId { get; }

    /// <summary>Gets the operation-specific payload, when one was returned.</summary>
    public T? Value { get; }
}

/// <summary>Represents a successful operation with no additional payload.</summary>
public sealed record DuetsPadEmptyResult;

/// <summary>Contains the result string returned by a session evaluation.</summary>
/// <param name="Result">The formatted evaluation result.</param>
public sealed record DuetsPadEvaluationResult(string Result);

/// <summary>Describes a tagged-template completion request.</summary>
/// <param name="Tag">The registered template tag.</param>
/// <param name="TextBeforeCaret">The source text before the template segment.</param>
/// <param name="TextAfterCaret">The source text after the template segment.</param>
/// <param name="CurrentSegmentRaw">The unescaped current template segment.</param>
/// <param name="CaretOffsetInSegment">The caret offset in the current segment.</param>
public sealed record DuetsPadCompletionRequest(
    string Tag,
    string TextBeforeCaret,
    string TextAfterCaret,
    string CurrentSegmentRaw,
    int CaretOffsetInSegment
);

/// <summary>Describes a replacement range within a completion segment.</summary>
/// <param name="Start">The zero-based start offset.</param>
/// <param name="Length">The replacement length.</param>
public sealed record DuetsPadCompletionSpan(int Start, int Length);

/// <summary>Describes one completion item returned by DuetsPad.</summary>
/// <param name="Label">The display label.</param>
/// <param name="InsertText">The optional text to insert; the label is used when omitted.</param>
/// <param name="Kind">The completion kind.</param>
/// <param name="FilterText">The optional filter text.</param>
/// <param name="SortText">The optional sort text.</param>
/// <param name="Detail">The optional detail text.</param>
/// <param name="Documentation">The optional documentation text.</param>
/// <param name="ReplacementSpan">The optional replacement range.</param>
public sealed record DuetsPadCompletionItem(
    string Label,
    string? InsertText,
    string Kind,
    string? FilterText,
    string? SortText,
    string? Detail,
    string? Documentation,
    DuetsPadCompletionSpan? ReplacementSpan
);

/// <summary>Contains a tagged-template completion response.</summary>
/// <param name="Items">The returned completion items.</param>
/// <param name="Stale">Whether the request was superseded.</param>
/// <param name="TimedOut">Whether completion timed out.</param>
public sealed record DuetsPadCompletionResult(
    IReadOnlyList<DuetsPadCompletionItem> Items,
    bool Stale,
    bool TimedOut
);

/// <summary>Contains a server-canonical Canvas snapshot.</summary>
/// <param name="Snapshot">The wire-format Canvas projection.</param>
public sealed record DuetsPadCanvasSnapshot(JsonObject Snapshot);

/// <summary>Describes field and attachment state submitted with an interaction invocation.</summary>
/// <param name="Fields">Committed field values keyed by field id.</param>
/// <param name="Attachments">Attachment revisions keyed by picker id.</param>
public sealed record DuetsPadInteractionSnapshot(
    IReadOnlyDictionary<Guid, string>? Fields = null,
    IReadOnlyDictionary<Guid, long>? Attachments = null
);

/// <summary>Contains the outcome details of an interaction invocation.</summary>
/// <param name="HandlerId">The invoked handler id.</param>
/// <param name="Stale">Whether the handler was stale.</param>
/// <param name="AttachmentConflict">Whether attachment state conflicted.</param>
public sealed record DuetsPadInteractionResult(
    string HandlerId,
    bool Stale,
    bool AttachmentConflict
);

/// <summary>Contains the id of a committed field.</summary>
/// <param name="FieldId">The committed field id.</param>
public sealed record DuetsPadFieldCommitResult(string FieldId);

/// <summary>Describes one file in an attachment selection manifest.</summary>
/// <param name="Name">The file name.</param>
/// <param name="ContentType">The media type, or an empty string when unknown.</param>
/// <param name="Size">The declared byte length.</param>
public sealed record DuetsPadAttachmentFile(string Name, string ContentType, long Size);

/// <summary>Identifies one accepted file in an attachment selection.</summary>
/// <param name="Id">The server-issued file id.</param>
/// <param name="Name">The file name.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="Size">The declared byte length.</param>
public sealed record DuetsPadAcceptedAttachmentFile(
    string Id,
    string Name,
    string ContentType,
    long Size
);

/// <summary>Contains the result of beginning an attachment selection.</summary>
/// <param name="PickerId">The target picker id.</param>
/// <param name="Token">The selection token.</param>
/// <param name="Revision">The accepted picker revision.</param>
/// <param name="Files">The accepted file manifest with server-issued ids.</param>
public sealed record DuetsPadAttachmentSelectionResult(
    string PickerId,
    string? Token,
    long Revision,
    IReadOnlyList<DuetsPadAcceptedAttachmentFile> Files
);

/// <summary>Contains the outcome details of an attachment operation.</summary>
/// <param name="PickerId">The target picker id.</param>
/// <param name="Revision">The resulting picker revision.</param>
/// <param name="Stale">Whether the supplied selection was stale.</param>
public sealed record DuetsPadAttachmentOperationResult(string PickerId, long Revision, bool Stale);

/// <summary>Contains the last committed Editor text.</summary>
/// <param name="Text">The whole Editor document.</param>
public sealed record DuetsPadEditorText(string Text);

/// <summary>Represents one parsed record from the unified DuetsPad SSE stream.</summary>
/// <param name="Kind">Either <c>data</c> or <c>comment</c>.</param>
/// <param name="Event">The optional SSE event name.</param>
/// <param name="Data">The raw data payload.</param>
/// <param name="Json">The parsed JSON payload when the data is JSON.</param>
/// <param name="Comment">The comment text for comment records.</param>
public sealed record DuetsPadSseRecord(
    string Kind,
    string? Event,
    string? Data,
    JsonNode? Json,
    string? Comment
);

/// <summary>Contains records read from an open DuetsPad SSE stream.</summary>
/// <param name="Records">The records received before the limit or timeout.</param>
/// <param name="TimedOut">Whether the read interval elapsed before the record limit was reached.</param>
/// <param name="CommentsSkipped">The number of omitted heartbeat comments.</param>
/// <param name="Ended">Whether the server ended the stream.</param>
public sealed record DuetsPadSseReadResult(
    IReadOnlyList<DuetsPadSseRecord> Records,
    bool TimedOut,
    int CommentsSkipped,
    bool Ended
);
