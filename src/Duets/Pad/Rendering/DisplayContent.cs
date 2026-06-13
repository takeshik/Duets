namespace Duets.Pad.Rendering;

public sealed record DisplayContent
{
    private static readonly ElementAttributes LabelAttributes = new(
        new KeyValuePair<string, string?>("class", "duetspad-label")
    );

    private static readonly ElementAttributes StackAttributes = new(
        new KeyValuePair<string, string?>("class", "duetspad-stack")
    );

    private static readonly RenderTreeReducer Reducer = new();

    public DisplayContent(ITerminalRenderNode body)
        : this(body, PendingInteractions.Empty) { }

    internal DisplayContent(ITerminalRenderNode body, PendingInteractions interactions)
    {
        this.Body = body ?? throw new ArgumentNullException(nameof(body));
        this.Interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    public ITerminalRenderNode Body { get; }

    internal PendingInteractions Interactions { get; }

    public static DisplayContent Text(string value) => new(new Text(value));

    public static DisplayContent Label(string value) =>
        new(new Element("span", LabelAttributes, new ElementChildren(new Text(value))));

    public static DisplayContent RawHtml(string content) => new(new RawHtml(content));

    public static DisplayContent Element(
        string tag,
        ElementAttributes? attributes = null,
        IEnumerable<DisplayContent>? children = null
    ) => FromElement(tag, attributes ?? ElementAttributes.Empty, children ?? []);

    public static DisplayContent Stack(IEnumerable<DisplayContent> children) =>
        FromElement(
            "div",
            StackAttributes,
            children ?? throw new ArgumentNullException(nameof(children))
        );

    public static DisplayContent Button(string label, Action handler, ButtonOptions? options = null)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        options ??= new ButtonOptions();
        var attributes = BuildButtonAttributes(options);
        var body = new Element("button", attributes, new ElementChildren(new Text(label)));
        var interactions = options.Disabled
            ? PendingInteractions.Empty
            : new PendingInteractions([
                new PendingInteraction(DisplayPath.Root, InteractionEvent.Click, handler),
            ]);
        return new DisplayContent(body, interactions);
    }

    internal static DisplayContent FromNode(IRenderNode body) => new(Reducer.Reduce(body));

    private static DisplayContent FromElement(
        string tag,
        ElementAttributes attributes,
        IEnumerable<DisplayContent> children
    )
    {
        var childList = children.ToList();
        var body = new Element(tag, attributes, [.. childList.Select(c => c.Body)]);
        var interactions = PendingInteractions.Merge(
            childList.Select((child, index) => child.Interactions.PrependPath(index))
        );
        return new DisplayContent(body, interactions);
    }

    private static ElementAttributes BuildButtonAttributes(ButtonOptions options)
    {
        var className = string.IsNullOrWhiteSpace(options.ClassName)
            ? "btn btn-primary"
            : options.ClassName!;
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new("type", "button"),
            new("class", className),
        };

        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            attributes.Add(new KeyValuePair<string, string?>("title", options.Title));
        }

        if (options.Disabled)
        {
            attributes.Add(new KeyValuePair<string, string?>("disabled", null));
        }

        return new ElementAttributes(attributes);
    }
}
