using System.Text.Json.Nodes;
using Duets.Pad.Modals;
using Duets.Pad.Protocol;
using Duets.Pad.State;

namespace Duets.Pad.Tests.Protocol;

public sealed class ModalProtocolTests
{
    private static ModalProjection CreateProjection(Guid id, long revision = 0) =>
        new(
            id,
            CanvasState.Empty,
            revision,
            new ModalOptions(
                "Title",
                [new ModalButtonDefinition("ok", "OK", "primary")],
                "ok",
                true,
                null,
                "md"
            )
        );

    [Fact]
    public void Open_serializes_a_full_modal_projection()
    {
        var id = Guid.NewGuid();
        var json = JsonNode.Parse(
            SseSerializer.Serialize(ModalEventMessage.Open(CreateProjection(id), []))
        )!;

        Assert.Equal(ModalEventTypes.Open, (string?)json["type"]);
        Assert.Equal(id.ToString("D"), (string?)json["modal"]!["modalId"]);
        Assert.Equal("Title", (string?)json["modal"]!["title"]);
        Assert.Equal("ok", (string?)json["modal"]!["defaultButtonId"]);
        Assert.True((bool?)json["modal"]!["canDismiss"]);
        Assert.False((bool?)json["modal"]!["claimed"]);
        Assert.NotNull(json["modal"]!["state"]);
        Assert.Empty(json["modal"]!["interactions"]!.AsArray());
    }

    [Fact]
    public void Snapshot_preserves_modal_order()
    {
        var first = CreateProjection(Guid.NewGuid());
        var second = CreateProjection(Guid.NewGuid());
        var json = JsonNode.Parse(
            SseSerializer.Serialize(
                ModalEventMessage.Snapshot([
                    new ModalSnapshotItem(first, []),
                    new ModalSnapshotItem(second, []),
                ])
            )
        )!;

        Assert.Equal(ModalEventTypes.Snapshot, (string?)json["type"]);
        var modals = json["modals"]!.AsArray();
        Assert.Equal(first.Id.ToString("D"), (string?)modals[0]!["modalId"]);
        Assert.Equal(second.Id.ToString("D"), (string?)modals[1]!["modalId"]);
    }

    [Fact]
    public void Patch_serializes_revisions_operations_and_interactions()
    {
        var id = Guid.NewGuid();
        var json = JsonNode.Parse(
            SseSerializer.Serialize(ModalEventMessage.Patch(id, 2, 3, [], []))
        )!;

        Assert.Equal(ModalEventTypes.Patch, (string?)json["type"]);
        Assert.Equal(id.ToString("D"), (string?)json["modalId"]);
        Assert.Equal(2, (long?)json["baseRevision"]);
        Assert.Equal(3, (long?)json["revision"]);
        Assert.Empty(json["operations"]!.AsArray());
    }
}
