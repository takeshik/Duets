using System.Globalization;
using Duets.Pad.Attachments;

namespace Duets.Pad.Rendering;

public sealed record DisplayContent
{
    private static readonly ElementAttributes LabelAttributes = new(
        new KeyValuePair<string, string?>("class", "duetspad-label")
    );

    private static readonly ElementAttributes StackAttributes = new(
        new KeyValuePair<string, string?>("class", "duetspad-stack")
    );

    private static readonly ElementAttributes StackHorizontalAttributes = new(
        new KeyValuePair<string, string?>("class", "duetspad-stack duetspad-stack-horizontal")
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

    /// <summary>
    /// Builds a preformatted text block that preserves whitespace without interpreting markup.
    /// </summary>
    public static DisplayContent Preformatted(string value, PreformattedOptions? options = null)
    {
        options ??= new PreformattedOptions();
        return new DisplayContent(
            new Element(
                "pre",
                BuildPreformattedAttributes("duetspad-preformatted", options),
                new ElementChildren(new Text(value))
            )
        );
    }

    /// <summary>
    /// Builds a semantic code block that preserves whitespace without interpreting markup.
    /// </summary>
    public static DisplayContent Code(string value, PreformattedOptions? options = null)
    {
        options ??= new PreformattedOptions();
        return new DisplayContent(
            new Element(
                "pre",
                BuildPreformattedAttributes("duetspad-preformatted duetspad-code", options),
                new ElementChildren(
                    new Element(
                        "code",
                        ElementAttributes.Empty,
                        new ElementChildren(new Text(value))
                    )
                )
            )
        );
    }

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

    /// <summary>
    /// Builds a Tabler data grid from labeled rendered values.
    /// </summary>
    public static DisplayContent DataGrid(IEnumerable<DataGridItem> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var renderedItems = new List<DisplayContent>();
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("Data grid items cannot contain null.", nameof(items));
            }

            if (string.IsNullOrWhiteSpace(item.Label))
            {
                throw new ArgumentException(
                    "Data grid item labels cannot be empty.",
                    nameof(items)
                );
            }

            var title = new DisplayContent(
                new Element(
                    "div",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "datagrid-title")
                    ),
                    new ElementChildren(new Text(item.Label))
                )
            );
            var content = FromElement(
                "div",
                new ElementAttributes(
                    new KeyValuePair<string, string?>("class", "datagrid-content")
                ),
                [item.Content]
            );
            renderedItems.Add(
                FromElement(
                    "div",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "datagrid-item")
                    ),
                    [title, content]
                )
            );
        }

        return FromElement(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "datagrid")),
            renderedItems
        );
    }

    /// <summary>
    /// Builds a Tabler empty-space component with optional icon, message, and action content.
    /// </summary>
    public static DisplayContent EmptySpace(string title, EmptySpaceOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Empty-space title cannot be empty.", nameof(title));
        }

        options ??= new EmptySpaceOptions();
        var children = new List<DisplayContent>();

        if (options.Icon is not null)
        {
            children.Add(
                FromElement(
                    "div",
                    new ElementAttributes(new KeyValuePair<string, string?>("class", "empty-icon")),
                    [Icon(options.Icon)]
                )
            );
        }

        children.Add(
            new DisplayContent(
                new Element(
                    "p",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "empty-title")
                    ),
                    new ElementChildren(new Text(title))
                )
            )
        );

        if (!string.IsNullOrWhiteSpace(options.Message))
        {
            children.Add(
                new DisplayContent(
                    new Element(
                        "p",
                        new ElementAttributes(
                            new KeyValuePair<string, string?>(
                                "class",
                                "empty-subtitle text-secondary"
                            )
                        ),
                        new ElementChildren(new Text(options.Message))
                    )
                )
            );
        }

        if (options.Action is not null)
        {
            children.Add(
                FromElement(
                    "div",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "empty-action")
                    ),
                    [options.Action]
                )
            );
        }

        return FromElement(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "empty")),
            children
        );
    }

    /// <summary>
    /// Builds a native disclosure whose open state is browser-local view state.
    /// </summary>
    public static DisplayContent Disclosure(
        string summary,
        DisplayContent content,
        DisclosureOptions? options = null
    )
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Disclosure summary cannot be empty.", nameof(summary));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        options ??= new DisclosureOptions();
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new("class", "duetspad-disclosure"),
        };
        if (options.Open)
        {
            attributes.Add(new KeyValuePair<string, string?>("open", null));
        }

        var summaryContent = new DisplayContent(
            new Element(
                "summary",
                new ElementAttributes(
                    new KeyValuePair<string, string?>("class", "duetspad-disclosure-summary")
                ),
                new ElementChildren(new Text(summary))
            )
        );
        var bodyContent = FromElement(
            "div",
            new ElementAttributes(
                new KeyValuePair<string, string?>("class", "duetspad-disclosure-content")
            ),
            [content]
        );

        return FromElement(
            "details",
            new ElementAttributes(attributes),
            [summaryContent, bodyContent]
        );
    }

    /// <summary>
    /// Builds a Bootstrap/Tabler grid row container.
    /// </summary>
    public static DisplayContent Row(
        IEnumerable<DisplayContent> children,
        RowOptions? options = null
    )
    {
        options ??= new RowOptions();
        return FromElement("div", BuildRowAttributes(options), children);
    }

    /// <summary>
    /// Builds a Bootstrap/Tabler grid column.
    /// </summary>
    public static DisplayContent Col(
        IEnumerable<DisplayContent> children,
        ColOptions? options = null
    )
    {
        options ??= new ColOptions();
        return FromElement("div", BuildColAttributes(options), children);
    }

    /// <summary>
    /// Builds a horizontal divider. When <see cref="DividerOptions.Text"/> is set,
    /// renders as Tabler's labeled divider; otherwise a plain <c>&lt;hr&gt;</c>.
    /// </summary>
    public static DisplayContent Divider(DividerOptions? options = null)
    {
        options ??= new DividerOptions();
        if (!string.IsNullOrWhiteSpace(options.Text))
        {
            return new DisplayContent(
                new Element(
                    "div",
                    BuildLabeledDividerAttributes(options),
                    new ElementChildren(new Text(options.Text))
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(options.Color))
        {
            return new DisplayContent(
                new Element("hr", BuildDividerAttributes(options), ElementChildren.Empty)
            );
        }

        return new DisplayContent(
            new Element("hr", ElementAttributes.Empty, ElementChildren.Empty)
        );
    }

    public static DisplayContent Element(
        string tag,
        ElementAttributes? attributes = null,
        IEnumerable<DisplayContent>? children = null
    ) => FromElement(tag, attributes ?? ElementAttributes.Empty, children ?? []);

    public static DisplayContent Stack(
        IEnumerable<DisplayContent> children,
        StackOptions? options = null
    )
    {
        var direction = options?.Direction;
        if (direction is not (null or "vertical" or "horizontal"))
        {
            throw new ArgumentException(
                "Stack direction must be \"vertical\" or \"horizontal\".",
                nameof(options)
            );
        }

        var attributes = direction == "horizontal" ? StackHorizontalAttributes : StackAttributes;
        return FromElement(
            "div",
            attributes,
            children ?? throw new ArgumentNullException(nameof(children))
        );
    }

    /// <summary>
    /// Builds a Tabler card component.
    /// </summary>
    public static DisplayContent Card(
        IEnumerable<DisplayContent> children,
        CardOptions? options = null
    )
    {
        options ??= new CardOptions();
        var parts = new List<DisplayContent>();

        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            var headerTitle = new Element(
                "h3",
                new ElementAttributes(new KeyValuePair<string, string?>("class", "card-title")),
                new ElementChildren(new Text(options.Title))
            );
            parts.Add(
                new DisplayContent(
                    new Element(
                        "div",
                        new ElementAttributes(
                            new KeyValuePair<string, string?>("class", "card-header")
                        ),
                        new ElementChildren(headerTitle)
                    )
                )
            );
        }

        parts.Add(
            FromElement(
                "div",
                new ElementAttributes(new KeyValuePair<string, string?>("class", "card-body")),
                children
            )
        );

        if (!string.IsNullOrWhiteSpace(options.Footer))
        {
            parts.Add(
                new DisplayContent(
                    new Element(
                        "div",
                        new ElementAttributes(
                            new KeyValuePair<string, string?>("class", "card-footer")
                        ),
                        new ElementChildren(new Text(options.Footer))
                    )
                )
            );
        }

        return FromElement("div", BuildCardAttributes(options), parts);
    }

    /// <summary>
    /// Builds a URL link element.
    /// </summary>
    public static DisplayContent Link(string text, string url, LinkOptions? options = null)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (url is null)
        {
            throw new ArgumentNullException(nameof(url));
        }

        // Security invariant: link URLs render into an href and open with
        // target="_blank". Block the schemes that can execute script or smuggle
        // active content in that context (javascript:, vbscript:, data:). This is
        // the single enforcement point for ui.link URL safety.
        var scheme = url.TrimStart();
        if (
            scheme.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || scheme.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            || scheme.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException(
                "javascript:, vbscript:, and data: URLs are not allowed.",
                nameof(url)
            );
        }

        options ??= new LinkOptions();
        return new DisplayContent(
            new Element(
                "a",
                BuildUrlLinkAttributes(url, options),
                new ElementChildren(new Text(text))
            )
        );
    }

    /// <summary>
    /// Builds an action link element with a click handler.
    /// </summary>
    public static DisplayContent Link(string text, Action handler, LinkOptions? options = null)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        options ??= new LinkOptions();
        var body = new Element(
            "a",
            BuildActionLinkAttributes(options),
            new ElementChildren(new Text(text))
        );
        var interactions = new PendingInteractions([
            new PendingInteraction(DisplayPath.Root, InteractionEvent.Click, handler),
        ]);
        return new DisplayContent(body, interactions);
    }

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

    /// <summary>Builds a server-canonical file-picker wrapper. (JS: <c>ui.filePicker</c>)</summary>
    internal static DisplayContent FilePicker(
        Guid id,
        AttachmentPickerSnapshot snapshot,
        FilePickerOptions options
    )
    {
        var wrapperAttributes = new List<KeyValuePair<string, string?>>
        {
            new(FieldMarker.AttributeName, id.ToString("D")),
            new(FieldMarker.KindAttributeName, FieldKind.File.ToAttributeValue()),
            new(
                "data-duetspad-attachment-revision",
                snapshot.Revision.ToString(CultureInfo.InvariantCulture)
            ),
            new("data-duetspad-attachment-status", snapshot.Status.ToString().ToLowerInvariant()),
            new("class", ResolveClassName(options.ClassName, "duetspad-file-picker")),
        };
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            wrapperAttributes.Add(new("data-duetspad-attachment-error", snapshot.Error));
        }

        var inputAttributes = new List<KeyValuePair<string, string?>>
        {
            new("type", "file"),
            new("class", "form-control"),
            new("data-duetspad-file-input", null),
        };
        if (!string.IsNullOrWhiteSpace(options.Accept))
        {
            inputAttributes.Add(new("accept", options.Accept));
        }

        if (options.Multiple)
        {
            inputAttributes.Add(new("multiple", null));
        }

        if (options.Disabled)
        {
            inputAttributes.Add(new("disabled", null));
        }

        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            inputAttributes.Add(new("title", options.Title));
        }

        var fileRows = snapshot.Files.Select(file =>
            (ITerminalRenderNode)
                new Element(
                    "div",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "duetspad-file-picker-item"),
                        new KeyValuePair<string, string?>("data-duetspad-file-id", file.Id)
                    ),
                    new ElementChildren(
                        new Element(
                            "span",
                            new ElementAttributes(
                                new KeyValuePair<string, string?>(
                                    "class",
                                    "duetspad-file-picker-name"
                                )
                            ),
                            new ElementChildren(new Text(file.Name))
                        ),
                        new Element(
                            "span",
                            new ElementAttributes(
                                new KeyValuePair<string, string?>(
                                    "class",
                                    "duetspad-file-picker-size"
                                )
                            ),
                            new ElementChildren(
                                new Text(
                                    file.Size.ToString(CultureInfo.InvariantCulture) + " bytes"
                                )
                            )
                        )
                    )
                )
        );
        var listChildren =
            snapshot.Files.Count == 0
                ? new ElementChildren(
                    new Element(
                        "div",
                        new ElementAttributes(
                            new KeyValuePair<string, string?>("class", "duetspad-file-picker-empty")
                        ),
                        new ElementChildren(new Text("No files attached."))
                    )
                )
                : [.. fileRows];

        var children = new List<ITerminalRenderNode>
        {
            new Element("input", new ElementAttributes(inputAttributes), ElementChildren.Empty),
            new Element(
                "div",
                new ElementAttributes(
                    new KeyValuePair<string, string?>("class", "duetspad-file-picker-list")
                ),
                listChildren
            ),
        };
        if (snapshot.Status == AttachmentSelectionStatus.Uploading)
        {
            children.Add(
                new Element(
                    "div",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "duetspad-file-picker-status")
                    ),
                    new ElementChildren(new Text("Uploading files…"))
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            children.Add(
                new Element(
                    "div",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "duetspad-file-picker-error")
                    ),
                    new ElementChildren(new Text(snapshot.Error))
                )
            );
        }

        if (snapshot.Status == AttachmentSelectionStatus.Failed)
        {
            children.Add(
                new Element(
                    "button",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("type", "button"),
                        new KeyValuePair<string, string?>(
                            "class",
                            "btn btn-sm btn-outline-secondary duetspad-file-picker-cancel"
                        ),
                        new KeyValuePair<string, string?>("data-duetspad-attachment-cancel", null)
                    ),
                    new ElementChildren(new Text("Cancel failed selection"))
                )
            );
        }

        return new DisplayContent(
            new Element("div", new ElementAttributes(wrapperAttributes), [.. children])
        );
    }

    /// <summary>
    /// Builds a single-line text input field marked with <paramref name="id"/>. (JS: <c>ui.textBox</c>)
    /// </summary>
    public static DisplayContent TextBox(Guid id, string value, TextBoxOptions? options = null)
    {
        options ??= new TextBoxOptions();
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new(FieldMarker.AttributeName, id.ToString("D")),
            new(FieldMarker.KindAttributeName, FieldKind.Text.ToAttributeValue()),
            new("type", "text"),
            new("class", ResolveClassName(options.ClassName, "form-control")),
            new(FieldMarker.ValueAttributeName, value),
        };
        AddCommonFieldAttributes(
            attributes,
            options.Name,
            options.Placeholder,
            options.Title,
            options.Disabled
        );
        return new DisplayContent(
            new Element("input", new ElementAttributes(attributes), ElementChildren.Empty)
        );
    }

    /// <summary>
    /// Builds a multi-line text input field marked with <paramref name="id"/>. (JS: <c>ui.textArea</c>)
    /// </summary>
    public static DisplayContent TextArea(Guid id, string value, TextAreaOptions? options = null)
    {
        options ??= new TextAreaOptions();
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new(FieldMarker.AttributeName, id.ToString("D")),
            new(FieldMarker.KindAttributeName, FieldKind.TextArea.ToAttributeValue()),
            new("class", ResolveClassName(options.ClassName, "form-control")),
            new(FieldMarker.ValueAttributeName, value),
        };
        if (options.Rows is { } rows)
        {
            attributes.Add(
                new KeyValuePair<string, string?>(
                    "rows",
                    rows.ToString(CultureInfo.InvariantCulture)
                )
            );
        }

        AddCommonFieldAttributes(
            attributes,
            options.Name,
            options.Placeholder,
            options.Title,
            options.Disabled
        );
        return new DisplayContent(
            new Element("textarea", new ElementAttributes(attributes), ElementChildren.Empty)
        );
    }

    /// <summary>
    /// Builds a numeric text input field marked with <paramref name="id"/>. The value is still a
    /// plain string (ADR-47): no coercion or validation is performed. (JS: <c>ui.numberBox</c>)
    /// </summary>
    public static DisplayContent NumberBox(Guid id, string value, NumberBoxOptions? options = null)
    {
        options ??= new NumberBoxOptions();
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new(FieldMarker.AttributeName, id.ToString("D")),
            new(FieldMarker.KindAttributeName, FieldKind.Number.ToAttributeValue()),
            new("type", "number"),
            new("class", ResolveClassName(options.ClassName, "form-control")),
            new(FieldMarker.ValueAttributeName, value),
        };
        if (options.Min is { } min)
        {
            attributes.Add(new KeyValuePair<string, string?>("min", FormatNumber(min)));
        }

        if (options.Max is { } max)
        {
            attributes.Add(new KeyValuePair<string, string?>("max", FormatNumber(max)));
        }

        if (options.Step is { } step)
        {
            attributes.Add(new KeyValuePair<string, string?>("step", FormatNumber(step)));
        }

        AddCommonFieldAttributes(
            attributes,
            options.Name,
            placeholder: null,
            options.Title,
            options.Disabled
        );
        return new DisplayContent(
            new Element("input", new ElementAttributes(attributes), ElementChildren.Empty)
        );
    }

    /// <summary>
    /// Builds a checkbox field marked with <paramref name="id"/>, whose value is the string
    /// <c>"True"</c> or <c>"False"</c> (ADR-47). (JS: <c>ui.checkBox</c>)
    /// </summary>
    public static DisplayContent CheckBox(Guid id, string value, CheckBoxOptions? options = null)
    {
        options ??= new CheckBoxOptions();
        var inputAttributes = new List<KeyValuePair<string, string?>>
        {
            new(FieldMarker.AttributeName, id.ToString("D")),
            new(FieldMarker.KindAttributeName, FieldKind.CheckBox.ToAttributeValue()),
            new("type", "checkbox"),
            new("class", ResolveClassName(options.ClassName, "form-check-input")),
        };
        if (string.Equals(value, "True", StringComparison.Ordinal))
        {
            inputAttributes.Add(
                new KeyValuePair<string, string?>(FieldMarker.CheckedAttributeName, null)
            );
        }

        if (options.Disabled)
        {
            inputAttributes.Add(new KeyValuePair<string, string?>("disabled", null));
        }

        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            inputAttributes.Add(new KeyValuePair<string, string?>("title", options.Title));
        }

        var input = new Element(
            "input",
            new ElementAttributes(inputAttributes),
            ElementChildren.Empty
        );
        if (string.IsNullOrWhiteSpace(options.Label))
        {
            return new DisplayContent(
                new Element(
                    "div",
                    new ElementAttributes(new KeyValuePair<string, string?>("class", "form-check")),
                    new ElementChildren(input)
                )
            );
        }

        var label = new Element(
            "label",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "form-check-label")),
            new ElementChildren(new Text(options.Label))
        );
        return new DisplayContent(
            new Element(
                "div",
                new ElementAttributes(new KeyValuePair<string, string?>("class", "form-check")),
                new ElementChildren(input, label)
            )
        );
    }

    /// <summary>
    /// Builds a single-select dropdown field marked with <paramref name="id"/>. A <paramref name="value"/>
    /// absent from <paramref name="options"/>'s items is retained but cannot be displayed as selected
    /// (ADR-47). (JS: <c>ui.dropDown</c>)
    /// </summary>
    public static DisplayContent DropDown(Guid id, string value, DropDownOptions? options = null)
    {
        options ??= new DropDownOptions();
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new(FieldMarker.AttributeName, id.ToString("D")),
            new(FieldMarker.KindAttributeName, FieldKind.DropDown.ToAttributeValue()),
            new("class", ResolveClassName(options.ClassName, "form-select")),
            new(FieldMarker.ValueAttributeName, value),
        };
        AddCommonFieldAttributes(
            attributes,
            options.Name,
            placeholder: null,
            options.Title,
            options.Disabled
        );

        var items = options
            .Items.Select(item =>
                (ITerminalRenderNode)
                    new Element(
                        "option",
                        new ElementAttributes(
                            new KeyValuePair<string, string?>("value", item.Value)
                        ),
                        new ElementChildren(new Text(item.Label))
                    )
            )
            .ToList();
        return new DisplayContent(
            new Element("select", new ElementAttributes(attributes), [.. items])
        );
    }

    /// <summary>
    /// Builds a range-slider field marked with <paramref name="id"/>. The value is still a plain
    /// string (ADR-47): no coercion or validation is performed. (JS: <c>ui.slider</c>)
    /// </summary>
    public static DisplayContent Slider(Guid id, string value, SliderOptions? options = null)
    {
        options ??= new SliderOptions();
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new(FieldMarker.AttributeName, id.ToString("D")),
            new(FieldMarker.KindAttributeName, FieldKind.Slider.ToAttributeValue()),
            new("type", "range"),
            new("class", ResolveClassName(options.ClassName, "form-range")),
            new("min", FormatNumber(options.Min)),
            new("max", FormatNumber(options.Max)),
            new(FieldMarker.ValueAttributeName, value),
        };
        if (options.Step is { } step)
        {
            attributes.Add(new KeyValuePair<string, string?>("step", FormatNumber(step)));
        }

        AddCommonFieldAttributes(
            attributes,
            options.Name,
            placeholder: null,
            options.Title,
            options.Disabled
        );
        return new DisplayContent(
            new Element("input", new ElementAttributes(attributes), ElementChildren.Empty)
        );
    }

    /// <summary>
    /// Builds a radio-button group field marked with <paramref name="id"/>: one marked
    /// <c>&lt;input type="radio"&gt;</c> per option, all sharing the field's identity. A
    /// <paramref name="value"/> absent from <paramref name="options"/>'s items is retained but
    /// leaves every option unchecked (ADR-47). (JS: <c>ui.radioGroup</c>)
    /// </summary>
    public static DisplayContent RadioGroup(
        Guid id,
        string value,
        RadioGroupOptions? options = null
    )
    {
        options ??= new RadioGroupOptions();
        var groupName = string.IsNullOrWhiteSpace(options.Name)
            ? $"duetspad-radio-{id:N}"
            : options.Name!;

        var children = options
            .Items.Select(item =>
            {
                var inputAttributes = new List<KeyValuePair<string, string?>>
                {
                    new(FieldMarker.AttributeName, id.ToString("D")),
                    new(FieldMarker.KindAttributeName, FieldKind.Radio.ToAttributeValue()),
                    new("type", "radio"),
                    new("name", groupName),
                    new("value", item.Value),
                    new("class", "form-check-input"),
                };
                if (string.Equals(item.Value, value, StringComparison.Ordinal))
                {
                    inputAttributes.Add(
                        new KeyValuePair<string, string?>(FieldMarker.CheckedAttributeName, null)
                    );
                }

                if (options.Disabled)
                {
                    inputAttributes.Add(new KeyValuePair<string, string?>("disabled", null));
                }

                var input = new Element(
                    "input",
                    new ElementAttributes(inputAttributes),
                    ElementChildren.Empty
                );
                var label = new Element(
                    "label",
                    new ElementAttributes(
                        new KeyValuePair<string, string?>("class", "form-check-label")
                    ),
                    new ElementChildren(new Text(item.Label))
                );
                return (ITerminalRenderNode)
                    new Element(
                        "div",
                        new ElementAttributes(
                            new KeyValuePair<string, string?>("class", "form-check")
                        ),
                        new ElementChildren(input, label)
                    );
            })
            .ToList();

        var wrapperAttributes = new List<KeyValuePair<string, string?>>();
        if (!string.IsNullOrWhiteSpace(options.ClassName))
        {
            wrapperAttributes.Add(new KeyValuePair<string, string?>("class", options.ClassName));
        }

        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            wrapperAttributes.Add(new KeyValuePair<string, string?>("title", options.Title));
        }

        return new DisplayContent(
            new Element("div", new ElementAttributes(wrapperAttributes), [.. children])
        );
    }

    private static string ResolveClassName(string? className, string fallback) =>
        string.IsNullOrWhiteSpace(className) ? fallback : className!;

    private static void AddCommonFieldAttributes(
        List<KeyValuePair<string, string?>> attributes,
        string? name,
        string? placeholder,
        string? title,
        bool disabled
    )
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            attributes.Add(new KeyValuePair<string, string?>("name", name));
        }

        if (!string.IsNullOrWhiteSpace(placeholder))
        {
            attributes.Add(new KeyValuePair<string, string?>("placeholder", placeholder));
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            attributes.Add(new KeyValuePair<string, string?>("title", title));
        }

        if (disabled)
        {
            attributes.Add(new KeyValuePair<string, string?>("disabled", null));
        }
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

    private static ElementAttributes BuildUrlLinkAttributes(string url, LinkOptions options)
    {
        var attributes = new List<KeyValuePair<string, string?>>
        {
            new("href", url),
            new("target", "_blank"),
            new("rel", "noopener noreferrer"),
        };
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            attributes.Add(new KeyValuePair<string, string?>("title", options.Title));
        }

        return new ElementAttributes(attributes);
    }

    private static ElementAttributes BuildPreformattedAttributes(
        string baseClass,
        PreformattedOptions options
    )
    {
        var className = options.Wrap ? $"{baseClass} duetspad-preformatted-wrap" : baseClass;
        return new ElementAttributes(new KeyValuePair<string, string?>("class", className));
    }

    private static ElementAttributes BuildActionLinkAttributes(LinkOptions options)
    {
        var attributes = new List<KeyValuePair<string, string?>> { new("role", "button") };
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            attributes.Add(new KeyValuePair<string, string?>("title", options.Title));
        }

        return new ElementAttributes(attributes);
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

    private static ElementAttributes BuildCardAttributes(CardOptions options)
    {
        var classes = new List<string> { "card" };
        AddClassIfPresent(classes, options.Color, color => $"card-{color}");
        return ClassOnly(classes);
    }

    private static ElementAttributes BuildRowAttributes(RowOptions options)
    {
        var classes = new List<string> { "row" };
        if (options.Gutter is { } gutter)
        {
            if (gutter is < 0 or > 5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    gutter,
                    "Gutter must be between 0 and 5."
                );
            }

            classes.Add($"g-{gutter}");
        }

        return ClassOnly(classes);
    }

    private static ElementAttributes BuildColAttributes(ColOptions options)
    {
        var classes = new List<string>();
        var hasBreakpoint = false;

        if (options.Span is { } span)
        {
            ValidateColSpan(span, nameof(options.Span));
            classes.Add($"col-{span}");
            hasBreakpoint = true;
        }

        if (options.Sm is { } sm)
        {
            ValidateColSpan(sm, nameof(options.Sm));
            classes.Add($"col-sm-{sm}");
            hasBreakpoint = true;
        }

        if (options.Md is { } md)
        {
            ValidateColSpan(md, nameof(options.Md));
            classes.Add($"col-md-{md}");
            hasBreakpoint = true;
        }

        if (options.Lg is { } lg)
        {
            ValidateColSpan(lg, nameof(options.Lg));
            classes.Add($"col-lg-{lg}");
            hasBreakpoint = true;
        }

        if (options.Xl is { } xl)
        {
            ValidateColSpan(xl, nameof(options.Xl));
            classes.Add($"col-xl-{xl}");
            hasBreakpoint = true;
        }

        if (!hasBreakpoint)
        {
            classes.Add("col");
        }

        return ClassOnly(classes);
    }

    private static ElementAttributes BuildLabeledDividerAttributes(DividerOptions options)
    {
        var classes = new List<string> { "hr-text" };
        AddClassIfPresent(classes, options.Color, color => $"text-{color}");
        return ClassOnly(classes);
    }

    private static ElementAttributes BuildDividerAttributes(DividerOptions options)
    {
        var classes = new List<string>();
        AddClassIfPresent(classes, options.Color, color => $"text-{color}");
        return ClassOnly(classes);
    }

    private static void ValidateColSpan(int span, string paramName)
    {
        if (span is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                span,
                "Column span must be between 1 and 12."
            );
        }
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
