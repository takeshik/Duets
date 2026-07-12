using Duets.Pad.Interactions;
using Duets.Pad.Rendering;

namespace Duets.Pad.Tests.Interactions;

public sealed class InteractionStoreCanvasTests
{
    // GetCanvasInteractions

    [Fact]
    public void GetCanvasInteractions_returns_empty_for_unknown_name()
    {
        var store = new InteractionStore();

        var result = store.GetCanvasInteractions("unknown");

        Assert.Empty(result);
    }

    // SetCanvasInteractions isolation

    [Fact]
    public void SetCanvasInteractions_for_one_name_does_not_affect_another_name()
    {
        var store = new InteractionStore();
        var pending = new PendingInteractions([
            new PendingInteraction(DisplayPath.Root, InteractionEvent.Click, () => { }),
        ]);

        store.SetCanvasInteractions("canvasA", pending);

        Assert.NotEmpty(store.GetCanvasInteractions("canvasA"));
        Assert.Empty(store.GetCanvasInteractions("canvasB"));
    }

    [Fact]
    public void SetCanvasInteractions_with_empty_pending_leaves_other_canvas_unchanged()
    {
        var store = new InteractionStore();
        var pending = new PendingInteractions([
            new PendingInteraction(DisplayPath.Root, InteractionEvent.Click, () => { }),
        ]);

        store.SetCanvasInteractions("canvasA", pending);
        store.SetCanvasInteractions("canvasB", PendingInteractions.Empty);

        Assert.NotEmpty(store.GetCanvasInteractions("canvasA"));
        Assert.Empty(store.GetCanvasInteractions("canvasB"));
    }

    // ClearCanvasInteractions isolation

    [Fact]
    public void ClearCanvasInteractions_clears_only_the_named_canvas()
    {
        var store = new InteractionStore();
        var pending = new PendingInteractions([
            new PendingInteraction(DisplayPath.Root, InteractionEvent.Click, () => { }),
        ]);

        store.SetCanvasInteractions("canvasA", pending);
        store.SetCanvasInteractions("canvasB", pending);

        store.ClearCanvasInteractions("canvasA");

        Assert.Empty(store.GetCanvasInteractions("canvasA"));
        Assert.NotEmpty(store.GetCanvasInteractions("canvasB"));
    }

    [Fact]
    public void ClearCanvasInteractions_on_unknown_name_does_not_throw()
    {
        var store = new InteractionStore();

        // Should complete without exception.
        store.ClearCanvasInteractions("nonexistent");

        Assert.Empty(store.GetCanvasInteractions("nonexistent"));
    }
}
