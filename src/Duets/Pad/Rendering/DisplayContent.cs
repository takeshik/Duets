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

    /// <summary>
    /// Builds a Tabler badge component.
    /// </summary>
    public static DisplayContent Badge(string text, BadgeOptions? options = null)
    {
        options ??= new BadgeOptions();
        return new DisplayContent(
            new Element("span", BuildBadgeAttributes(options), new ElementChildren(new Text(text)))
        );
    }

    /// <summary>
    /// Builds a Tabler alert component.
    /// </summary>
    public static DisplayContent Alert(string message, AlertOptions? options = null)
    {
        options ??= new AlertOptions();
        var children = new List<ITerminalRenderNode>();
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            children.Add(
                new Element(
                    "div",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "alert-title")
                    ),
                    new ElementChildren(new Text(options.Title))
                )
            );
        }

        children.Add(new Text(message));

        return new DisplayContent(new Element("div", BuildAlertAttributes(options), [.. children]));
    }

    /// <summary>
    /// Builds a Tabler spinner component.
    /// </summary>
    public static DisplayContent Spinner(SpinnerOptions? options = null)
    {
        options ??= new SpinnerOptions();
        return new DisplayContent(
            new Element("div", BuildSpinnerAttributes(options), ElementChildren.Empty)
        );
    }

    /// <summary>
    /// Builds a Tabler status component.
    /// </summary>
    public static DisplayContent Status(string text, StatusOptions? options = null)
    {
        options ??= new StatusOptions();
        var dotClass = options.Animated ? "status-dot status-dot-animated" : "status-dot";
        return new DisplayContent(
            new Element(
                "span",
                BuildStatusAttributes(options),
                new ElementChildren(
                    new Element(
                        "span",
                        new ElementAttributes(new KeyValuePair<string, string?>("class", dotClass))
                    ),
                    new Text(text)
                )
            )
        );
    }

    /// <summary>
    /// Builds a Tabler icon component.
    /// </summary>
    public static DisplayContent Icon(string name, IconOptions? options = null)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Icon name cannot be empty.", nameof(name));
        }

        options ??= new IconOptions();
        return new DisplayContent(
            new Element("i", BuildIconAttributes(name, options), ElementChildren.Empty)
        );
    }

    /// <summary>
    /// Builds a Tabler progress component for a value between 0 and 100.
    /// </summary>
    public static DisplayContent Progress(double value, ProgressOptions? options = null)
    {
        if (value is < 0 or > 100 || double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Progress value must be between 0 and 100."
            );
        }

        options ??= new ProgressOptions();
        var bar = new Element(
            "div",
            BuildProgressBarAttributes(value, options),
            string.IsNullOrEmpty(options.Label)
                ? ElementChildren.Empty
                : new ElementChildren(new Text(options.Label))
        );

        return new DisplayContent(
            new Element(
                "div",
                new ElementAttributes(new KeyValuePair<string, string?>("class", "progress")),
                new ElementChildren(bar)
            )
        );
    }

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

    private static ElementAttributes BuildBadgeAttributes(BadgeOptions options)
    {
        var classes = new List<string> { "badge" };
        AddClassIfPresent(classes, options.Color, color => $"bg-{color}-lt");
        AddClassIf(classes, options.Pill, "badge-pill");
        AddClassIf(classes, options.Outline, "badge-outline");

        return ClassOnly(classes);
    }

    private static ElementAttributes BuildAlertAttributes(AlertOptions options)
    {
        var variant = string.IsNullOrWhiteSpace(options.Variant) ? "info" : options.Variant!;
        if (variant is not ("success" or "danger" or "warning" or "info"))
        {
            throw new ArgumentException(
                "Alert variant must be success, danger, warning, or info.",
                nameof(options)
            );
        }

        return new ElementAttributes(
            new KeyValuePair<string, string?>("class", $"alert alert-{variant}"),
            new KeyValuePair<string, string?>("role", "alert")
        );
    }

    private static ElementAttributes BuildSpinnerAttributes(SpinnerOptions options)
    {
        var classes = new List<string> { "spinner-border" };
        AddClassIfPresent(classes, options.Color, color => $"text-{color}");
        AddClassIf(classes, options.Small, "spinner-border-sm");

        return new ElementAttributes(
            new KeyValuePair<string, string?>("class", string.Join(" ", classes)),
            new KeyValuePair<string, string?>("role", "status")
        );
    }

    private static ElementAttributes BuildStatusAttributes(StatusOptions options)
    {
        var classes = new List<string> { "status" };
        AddClassIfPresent(classes, options.Color, color => $"status-{color}");

        return ClassOnly(classes);
    }

    private static ElementAttributes BuildIconAttributes(string name, IconOptions options)
    {
        var classes = new List<string> { "ti", $"ti-{name}" };
        AddClassIfPresent(classes, options.Color, color => $"text-{color}");
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new("class", string.Join(" ", classes)),
        };

        if (options.Size is not null)
        {
            attributes.Add(
                new KeyValuePair<string, string?>(
                    "style",
                    $"font-size: {FormatNumber(options.Size.Value)}px"
                )
            );
        }

        return new ElementAttributes(attributes);
    }

    private static ElementAttributes BuildProgressBarAttributes(
        double value,
        ProgressOptions options
    )
    {
        var classes = new List<string> { "progress-bar" };
        AddClassIfPresent(classes, options.Color, color => $"bg-{color}");

        return new ElementAttributes(
            new KeyValuePair<string, string?>("class", string.Join(" ", classes)),
            new KeyValuePair<string, string?>("style", $"width: {FormatNumber(value)}%"),
            new KeyValuePair<string, string?>("role", "progressbar"),
            new KeyValuePair<string, string?>("aria-valuenow", FormatNumber(value)),
            new KeyValuePair<string, string?>("aria-valuemin", "0"),
            new KeyValuePair<string, string?>("aria-valuemax", "100")
        );
    }

    private static ElementAttributes ClassOnly(IEnumerable<string> classes) =>
        new(new KeyValuePair<string, string?>("class", string.Join(" ", classes)));

    private static void AddClassIf(List<string> classes, bool condition, string className)
    {
        if (condition)
        {
            classes.Add(className);
        }
    }

    private static void AddClassIfPresent(
        List<string> classes,
        string? value,
        Func<string, string> buildClassName
    )
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            classes.Add(buildClassName(value!));
        }
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
}
