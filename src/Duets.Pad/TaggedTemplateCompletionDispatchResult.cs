using Duets.Completions;

namespace Duets.Pad;

internal sealed record TaggedTemplateCompletionDispatchResult(
    bool Ok,
    IReadOnlyList<TemplateCompletionItem> Items,
    string? Error = null,
    bool Stale = false,
    bool TimedOut = false
)
{
    public static TaggedTemplateCompletionDispatchResult Success(
        IReadOnlyList<TemplateCompletionItem> items
    ) => new(true, items);

    public static TaggedTemplateCompletionDispatchResult Empty() => new(true, []);

    public static TaggedTemplateCompletionDispatchResult Failed(string error) =>
        new(false, [], error);

    public static TaggedTemplateCompletionDispatchResult Timeout() =>
        new(false, [], "Tagged-template completion timed out.", TimedOut: true);

    public static TaggedTemplateCompletionDispatchResult Superseded() => new(true, [], Stale: true);
}
