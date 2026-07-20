using Duets.Pad.Interactions;
using Duets.Pad.Rendering;

namespace Duets.Pad.Tests.Interactions;

public sealed class InteractionStoreDialogTests
{
    [Fact]
    public void ClearDialogInteractions_unregisters_only_the_selected_dialog()
    {
        var store = new InteractionStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var pending = new PendingInteractions([
            new PendingInteraction(DisplayPath.Root, InteractionEvent.Click, () => { }),
        ]);
        var first = store.CommitDialogInteractions(
            store.PrepareSetDialogInteractions(firstId, pending)
        );
        var second = store.CommitDialogInteractions(
            store.PrepareSetDialogInteractions(secondId, pending)
        );

        store.ClearDialogInteractions(firstId);

        Assert.Empty(store.GetDialogInteractions(firstId));
        Assert.NotEmpty(store.GetDialogInteractions(secondId));
        Assert.False(store.TryGetHandler(Assert.Single(first).HandlerId, out _));
        Assert.True(store.TryGetHandler(Assert.Single(second).HandlerId, out _));
    }
}
