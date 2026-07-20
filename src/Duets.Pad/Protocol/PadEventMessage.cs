namespace Duets.Pad.Protocol;

/// <summary>
/// Discriminated union of all event types sent on the unified <c>GET /sessions/{sessionId}/events</c>
/// SSE stream. Each variant wraps a projected surface event or an imperative command.
/// </summary>
internal abstract class PadEventMessage
{
    private protected PadEventMessage() { }

    /// <summary>Wraps a <see cref="CanvasEventMessage"/>.</summary>
    internal sealed class Canvas(CanvasEventMessage message) : PadEventMessage
    {
        public CanvasEventMessage Message { get; } = message;
    }

    /// <summary>Wraps a <see cref="TimelineEventMessage"/>.</summary>
    internal sealed class Timeline(TimelineEventMessage message) : PadEventMessage
    {
        public TimelineEventMessage Message { get; } = message;
    }

    /// <summary>Wraps a <see cref="DialogEventMessage"/>.</summary>
    internal sealed class Dialog(DialogEventMessage message) : PadEventMessage
    {
        public DialogEventMessage Message { get; } = message;
    }

    /// <summary>Wraps a <see cref="TypeDeclaration"/>.</summary>
    internal sealed class TypeDeclaration(global::Duets.TypeDeclaration declaration)
        : PadEventMessage
    {
        public global::Duets.TypeDeclaration Declaration { get; } = declaration;
    }

    /// <summary>Wraps a tagged-template completion tag snapshot.</summary>
    internal sealed class TaggedTemplateSnapshot(
        global::Duets.Completions.TaggedTemplateRegistrySnapshot snapshot
    ) : PadEventMessage
    {
        public global::Duets.Completions.TaggedTemplateRegistrySnapshot Snapshot { get; } =
            snapshot;
    }

    /// <summary>
    /// Carries a control command from the server to the browser. Serialised as
    /// <c>{ "type": "control.&lt;Op&gt;", ...Payload }</c>.
    /// </summary>
    internal sealed class Control(string op, IReadOnlyDictionary<string, object?> payload)
        : PadEventMessage
    {
        /// <summary>The operation name (e.g. <c>"reset"</c>), without the <c>control.</c> prefix.</summary>
        public string Op { get; } = op ?? throw new ArgumentNullException(nameof(op));

        /// <summary>Arbitrary key-value payload attached to this control command.</summary>
        public IReadOnlyDictionary<string, object?> Payload { get; } =
            payload ?? throw new ArgumentNullException(nameof(payload));
    }
}
