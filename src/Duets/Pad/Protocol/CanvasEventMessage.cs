using Duets.Pad.Interactions;
using Duets.Pad.State;

namespace Duets.Pad.Protocol;

/// <summary>
/// Server-to-browser Canvas event before JSON serialization.
/// </summary>
internal sealed record CanvasEventMessage
{
    private readonly CanvasState? state;

    private CanvasEventMessage(
        string name,
        string type,
        CanvasState? state,
        long revision,
        long? baseRevision,
        IReadOnlyList<CanvasPatchOperation> operations,
        IReadOnlyList<CommittedInteraction> interactions
    )
    {
        this.Name = !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new ArgumentException("Canvas name cannot be empty.", nameof(name));
        this.Type = !string.IsNullOrWhiteSpace(type)
            ? type
            : throw new ArgumentException("Canvas event type cannot be empty.", nameof(type));
        this.Revision =
            revision >= 0
                ? revision
                : throw new ArgumentOutOfRangeException(
                    nameof(revision),
                    "Canvas revision must be non-negative."
                );
        this.BaseRevision = baseRevision is null or >= 0
            ? baseRevision
            : throw new ArgumentOutOfRangeException(
                nameof(baseRevision),
                "Canvas base revision must be non-negative."
            );
        this.state = state;
        this.Operations = operations ?? throw new ArgumentNullException(nameof(operations));
        this.Interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));

        if (this.Type == CanvasEventTypes.Patch)
        {
            if (this.state is not null)
            {
                throw new ArgumentException("Canvas patch events cannot carry full state.");
            }

            if (this.BaseRevision is not long baseValue || this.Revision != baseValue + 1)
            {
                throw new ArgumentException(
                    "Canvas patch revision must equal baseRevision + 1.",
                    nameof(revision)
                );
            }
        }
        else
        {
            if (this.state is null)
            {
                throw new ArgumentException("Canvas full-state events must carry state.");
            }

            if (this.BaseRevision is not null || this.Operations.Count != 0)
            {
                throw new ArgumentException(
                    "Canvas full-state events cannot carry patch fields.",
                    nameof(operations)
                );
            }
        }
    }

    public string Name { get; }

    public string Type { get; }

    public CanvasState State =>
        this.state
        ?? throw new InvalidOperationException("Canvas patch events do not carry full state.");

    public long Revision { get; }

    public long? BaseRevision { get; }

    public IReadOnlyList<CanvasPatchOperation> Operations { get; }

    public IReadOnlyList<CommittedInteraction> Interactions { get; }

    public static CanvasEventMessage Snapshot(
        string name,
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions,
        long revision = 0
    ) => new(name, CanvasEventTypes.Snapshot, state, revision, null, [], interactions);

    public static CanvasEventMessage Replace(
        string name,
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions,
        long revision = 0
    ) => new(name, CanvasEventTypes.Replace, state, revision, null, [], interactions);

    public static CanvasEventMessage Patch(
        string name,
        long baseRevision,
        long revision,
        IReadOnlyList<CanvasPatchOperation> operations,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new(name, CanvasEventTypes.Patch, null, revision, baseRevision, operations, interactions);
}
