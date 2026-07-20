using System.Text.Json.Nodes;
using Duets.Pad.Dialogs;
using Duets.Pad.Protocol;
using Duets.Pad.State;

namespace Duets.Pad.Tests.Protocol;

public sealed class DialogProtocolTests
{
    private static DialogProjection CreateProjection(Guid id, long revision = 0) =>
        new(
            id,
            CanvasState.Empty,
            revision,
            new DialogOptions(
                "Title",
                [new DialogButtonDefinition("ok", "OK", "primary")],
                "ok",
                true,
                null,
                "md"
            )
        );

    [Fact]
    public void Open_serializes_a_full_dialog_projection()
    {
        var id = Guid.NewGuid();
        var json = JsonNode.Parse(
            SseSerializer.Serialize(DialogEventMessage.Open(CreateProjection(id), []))
        )!;

        Assert.Equal(DialogEventTypes.Open, (string?)json["type"]);
        Assert.Equal(id.ToString("D"), (string?)json["dialog"]!["dialogId"]);
        Assert.Equal("Title", (string?)json["dialog"]!["title"]);
        Assert.Equal("ok", (string?)json["dialog"]!["defaultButtonId"]);
        Assert.True((bool?)json["dialog"]!["canDismiss"]);
        Assert.False((bool?)json["dialog"]!["claimed"]);
        Assert.NotNull(json["dialog"]!["state"]);
        Assert.Empty(json["dialog"]!["interactions"]!.AsArray());
    }

    [Fact]
    public void Snapshot_preserves_dialog_order()
    {
        var first = CreateProjection(Guid.NewGuid());
        var second = CreateProjection(Guid.NewGuid());
        var json = JsonNode.Parse(
            SseSerializer.Serialize(
                DialogEventMessage.Snapshot([
                    new DialogSnapshotItem(first, []),
                    new DialogSnapshotItem(second, []),
                ])
            )
        )!;

        Assert.Equal(DialogEventTypes.Snapshot, (string?)json["type"]);
        var dialogs = json["dialogs"]!.AsArray();
        Assert.Equal(first.Id.ToString("D"), (string?)dialogs[0]!["dialogId"]);
        Assert.Equal(second.Id.ToString("D"), (string?)dialogs[1]!["dialogId"]);
    }

    [Fact]
    public void Patch_serializes_revisions_operations_and_interactions()
    {
        var id = Guid.NewGuid();
        var json = JsonNode.Parse(
            SseSerializer.Serialize(DialogEventMessage.Patch(id, 2, 3, [], []))
        )!;

        Assert.Equal(DialogEventTypes.Patch, (string?)json["type"]);
        Assert.Equal(id.ToString("D"), (string?)json["dialogId"]);
        Assert.Equal(2, (long?)json["baseRevision"]);
        Assert.Equal(3, (long?)json["revision"]);
        Assert.Empty(json["operations"]!.AsArray());
    }
}
