using Duets.Pad.Interactions;
using Duets.Pad.Rendering;

namespace Duets.Pad.Tests.Interactions;

public sealed class InteractionStoreModalTests
{
    [Fact]
    public void ClearModalInteractions_unregisters_only_the_selected_modal()
    {
        var store = new InteractionStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var pending = new PendingInteractions([
            new PendingInteraction(DisplayPath.Root, InteractionEvent.Click, () => { }),
        ]);
        var first = store.CommitModalInteractions(
            store.PrepareSetModalInteractions(firstId, pending)
        );
        var second = store.CommitModalInteractions(
            store.PrepareSetModalInteractions(secondId, pending)
        );

        store.ClearModalInteractions(firstId);

        Assert.Empty(store.GetModalInteractions(firstId));
        Assert.NotEmpty(store.GetModalInteractions(secondId));
        Assert.False(store.TryGetHandler(Assert.Single(first).HandlerId, out _));
        Assert.True(store.TryGetHandler(Assert.Single(second).HandlerId, out _));
    }
}
