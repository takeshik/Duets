using System.Collections;
using System.Globalization;
using Duets.Pad.Dialogs;
using Duets.Pad.Rendering;

namespace Duets.Pad;

/// <summary>
/// Host object bound to the <c>ui</c> global in script. Provides the structured display surface:
/// <see cref="RawHtml"/>, <see cref="Element"/>, <see cref="Text"/>, <see cref="Label"/>,
/// <see cref="Stack"/>, <see cref="Row"/>, <see cref="Col"/>, <see cref="Card"/>,
/// <see cref="Link"/>, <see cref="Button"/>, <see cref="Divider"/>, Tabler components,
/// <see cref="Table"/>, and the form-input factories (<see cref="TextBox"/>, <see cref="TextArea"/>,
/// <see cref="NumberBox"/>, <see cref="CheckBox"/>, <see cref="DropDown"/>, <see cref="Slider"/>,
/// <see cref="RadioGroup"/>) that return a <see cref="DisplayInput"/> (ADR-47), plus the
/// imperative <see cref="Toast"/> notification command and <see cref="Dialog"/> modal surface.
/// </summary>
internal sealed class UIGlobal(
    DisplayRenderer renderer,
    DumpOptions dumpOptions,
    ISlotHost? slotHost = null,
    IFieldHost? fieldHost = null,
    IToastHost? toastHost = null,
    IDialogHost? dialogHost = null
)
{
    private readonly DisplayRenderer _renderer =
        renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly DumpOptions _dumpOptions =
        dumpOptions ?? throw new ArgumentNullException(nameof(dumpOptions));

    // Null only in rendering-focused unit tests that never call Slot; production always supplies it.
    private readonly ISlotHost? _slotHost = slotHost;

    // Null only in rendering-focused unit tests that never call an input factory; production
    // always supplies it.
    private readonly IFieldHost? _fieldHost = fieldHost;

    // Null only in rendering-focused unit tests that never call Toast; production always supplies it.
    private readonly IToastHost? _toastHost = toastHost;

    // Null only in rendering-focused unit tests that never call Dialog; production always supplies it.
    private readonly IDialogHost? _dialogHost = dialogHost;

    /// <summary>
    /// Returns a mutable <see cref="DisplaySlot"/> whose <c>content</c> can be reassigned to update
    /// the rendered output in place. (JS: <c>ui.slot</c>)
    /// </summary>
    public DisplaySlot Slot(object? initial = null) =>
        new(
            this._slotHost
                ?? throw new InvalidOperationException(
                    "ui.slot is not available because no slot host was provided."
                ),
            initial
        );

    /// <summary>
    /// Returns a single-line text input field. (JS: <c>ui.textBox</c>)
    /// </summary>
    public DisplayInput TextBox(object? options = null)
    {
        var dict = CoerceOptionsDictionary(options);
        var opts = BuildTextBoxOptions(dict);
        var id = Guid.NewGuid();
        return new DisplayInput(
            this.RequireFieldHost(),
            id,
            FieldKind.Text,
            ExtractInitialValue(dict),
            value => DisplayContent.TextBox(id, value, opts)
        );
    }

    /// <summary>
    /// Returns a multi-line text input field. (JS: <c>ui.textArea</c>)
    /// </summary>
    public DisplayInput TextArea(object? options = null)
    {
        var dict = CoerceOptionsDictionary(options);
        var opts = BuildTextAreaOptions(dict);
        var id = Guid.NewGuid();
        return new DisplayInput(
            this.RequireFieldHost(),
            id,
            FieldKind.TextArea,
            ExtractInitialValue(dict),
            value => DisplayContent.TextArea(id, value, opts)
        );
    }

    /// <summary>
    /// Returns a numeric text input field. The value is still a plain string (ADR-47): no coercion
    /// or validation is performed. (JS: <c>ui.numberBox</c>)
    /// </summary>
    public DisplayInput NumberBox(object? options = null)
    {
        var dict = CoerceOptionsDictionary(options);
        var opts = BuildNumberBoxOptions(dict);
        var id = Guid.NewGuid();
        return new DisplayInput(
            this.RequireFieldHost(),
            id,
            FieldKind.Number,
            ExtractInitialValue(dict),
            value => DisplayContent.NumberBox(id, value, opts)
        );
    }

    /// <summary>
    /// Returns a checkbox field. The value is the string <c>"True"</c> or <c>"False"</c> (ADR-47).
    /// (JS: <c>ui.checkBox</c>)
    /// </summary>
    public DisplayInput CheckBox(object? options = null)
    {
        var dict = CoerceOptionsDictionary(options);
        var opts = BuildCheckBoxOptions(dict);
        var initial = ExtractInitialChecked(dict) ? "True" : "False";
        var id = Guid.NewGuid();
        return new DisplayInput(
            this.RequireFieldHost(),
            id,
            FieldKind.CheckBox,
            initial,
            value => DisplayContent.CheckBox(id, value, opts)
        );
    }

    /// <summary>
    /// Returns a single-select dropdown field built from <paramref name="items"/> (each either a
    /// string or a <c>{ value, label }</c> object). A value absent from <paramref name="items"/> is
    /// retained but cannot be displayed as selected (ADR-47). (JS: <c>ui.dropDown</c>)
    /// </summary>
    public DisplayInput DropDown(object? items, object? options = null)
    {
        var dict = CoerceOptionsDictionary(options);
        var opts = BuildDropDownOptions(items, dict);
        var id = Guid.NewGuid();
        return new DisplayInput(
            this.RequireFieldHost(),
            id,
            FieldKind.DropDown,
            ExtractInitialValue(dict),
            value => DisplayContent.DropDown(id, value, opts)
        );
    }

    /// <summary>
    /// Returns a range-slider field. The value is still a plain string (ADR-47): no coercion or
    /// validation is performed. (JS: <c>ui.slider</c>)
    /// </summary>
    public DisplayInput Slider(object? options = null)
    {
        var dict = CoerceOptionsDictionary(options);
        var opts = BuildSliderOptions(dict);
        var id = Guid.NewGuid();
        return new DisplayInput(
            this.RequireFieldHost(),
            id,
            FieldKind.Slider,
            ExtractInitialValue(dict, FormatNumberForValue(opts.Min)),
            value => DisplayContent.Slider(id, value, opts)
        );
    }

    /// <summary>
    /// Returns a radio-button group field built from <paramref name="items"/> (each either a string
    /// or a <c>{ value, label }</c> object). A value absent from <paramref name="items"/> is retained
    /// but leaves every option unchecked (ADR-47). (JS: <c>ui.radioGroup</c>)
    /// </summary>
    public DisplayInput RadioGroup(object? items, object? options = null)
    {
        var dict = CoerceOptionsDictionary(options);
        var opts = BuildRadioGroupOptions(items, dict);
        var id = Guid.NewGuid();
        return new DisplayInput(
            this.RequireFieldHost(),
            id,
            FieldKind.Radio,
            ExtractInitialValue(dict),
            value => DisplayContent.RadioGroup(id, value, opts)
        );
    }

    /// <summary>
    /// Returns a transactional file picker whose committed files are exposed through its
    /// read-only <c>files</c> collection. (JS: <c>ui.filePicker</c>)
    /// </summary>
    public DisplayFilePicker FilePicker(object? options = null)
    {
        var host =
            this._fieldHost as Attachments.IFilePickerHost
            ?? throw new InvalidOperationException(
                "ui.filePicker is not available because no file-picker host was provided."
            );
        return new DisplayFilePicker(
            host,
            Guid.NewGuid(),
            BuildFilePickerOptions(CoerceOptionsDictionary(options))
        );
    }

    private IFieldHost RequireFieldHost() =>
        this._fieldHost
        ?? throw new InvalidOperationException(
            "This ui.* input factory is not available because no field host was provided."
        );

    /// <summary>
    /// Queues a transient browser toast notification. The command is delivered after the current
    /// evaluation or interaction handler completes. (JS: <c>ui.toast</c>)
    /// </summary>
    public void Toast(string message, object? options = null)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var host =
            this._toastHost
            ?? throw new InvalidOperationException(
                "ui.toast is not available because no toast host was provided."
            );
        host.ShowToast(message, BuildToastOptions(options));
    }

    /// <summary>
    /// Opens a server-canonical modal containing arbitrary rendered content and invokes
    /// <paramref name="onResult"/> in a later interaction turn. (JS: <c>ui.dialog</c>)
    /// </summary>
    /// <returns>
    /// A session-bound dialog handle. If <paramref name="body"/> cannot be rendered, the failure is
    /// appended to the Timeline and the returned handle is already closed.
    /// </returns>
    /// <remarks>
    /// An empty button list combined with an explicit <c>dismissButtonId: null</c> creates a
    /// programmatic-only dialog that can be closed only through the returned handle.
    /// </remarks>
    public DisplayDialog Dialog(object? body, Action<DialogResult> onResult, object? options = null)
    {
        if (onResult is null)
        {
            throw new ArgumentNullException(nameof(onResult));
        }

        var host =
            this._dialogHost
            ?? throw new InvalidOperationException(
                "ui.dialog is not available because no dialog host was provided."
            );
        return host.ShowDialog(body, onResult, BuildDialogOptions(options));
    }

    /// <summary>
    /// Returns a <see cref="Rendering.RawHtml"/> node. This is the only raw-HTML escape hatch.
    /// (JS: <c>ui.rawHtml</c>)
    /// </summary>
    public DisplayContent RawHtml(string content) => DisplayContent.RawHtml(content);

    /// <summary>
    /// Builds a structured <see cref="Rendering.Element"/>.
    /// (JS: <c>ui.element</c>)
    /// </summary>
    public DisplayContent Element(string tag, object? attributes = null, object? children = null)
    {
        var elementAttributes = BuildAttributes(attributes);
        var elementChildren = this.BuildChildren(children);
        return DisplayContent.Element(tag, elementAttributes, elementChildren);
    }

    /// <summary>
    /// Returns a <see cref="Rendering.Text"/> node.
    /// (JS: <c>ui.text</c>)
    /// </summary>
    public DisplayContent Text(string value) => DisplayContent.Text(value);

    /// <summary>
    /// Returns a <c>span.duetspad-label</c> element wrapping <paramref name="value"/>.
    /// (JS: <c>ui.label</c>)
    /// </summary>
    public DisplayContent Label(string value) => DisplayContent.Label(value);

    /// <summary>
    /// Returns a Tabler badge element wrapping <paramref name="text"/>.
    /// (JS: <c>ui.badge</c>)
    /// </summary>
    public DisplayContent Badge(string text, object? options = null) =>
        DisplayContent.Badge(text, BuildBadgeOptions(options));

    /// <summary>
    /// Returns a Tabler alert element with an optional title.
    /// (JS: <c>ui.alert</c>)
    /// </summary>
    public DisplayContent Alert(string message, object? options = null) =>
        DisplayContent.Alert(message, BuildAlertOptions(options));

    /// <summary>
    /// Returns a Tabler spinner element.
    /// (JS: <c>ui.spinner</c>)
    /// </summary>
    public DisplayContent Spinner(object? options = null) =>
        DisplayContent.Spinner(BuildSpinnerOptions(options));

    /// <summary>
    /// Returns a Tabler status element wrapping <paramref name="text"/>.
    /// (JS: <c>ui.status</c>)
    /// </summary>
    public DisplayContent Status(string text, object? options = null) =>
        DisplayContent.Status(text, BuildStatusOptions(options));

    /// <summary>
    /// Returns a Tabler icon element for <paramref name="name"/>.
    /// (JS: <c>ui.icon</c>)
    /// </summary>
    public DisplayContent Icon(string name, object? options = null) =>
        DisplayContent.Icon(name, BuildIconOptions(options));

    /// <summary>
    /// Returns a Tabler progress element for a value between 0 and 100.
    /// (JS: <c>ui.progress</c>)
    /// </summary>
    public DisplayContent Progress(object? value, object? options = null) =>
        DisplayContent.Progress(CoerceProgressValue(value), BuildProgressOptions(options));

    /// <summary>
    /// Returns a <c>div.duetspad-stack</c> element containing the rendered <paramref name="children"/>.
    /// (JS: <c>ui.stack</c>)
    /// </summary>
    public DisplayContent Stack(object? children = null, object? options = null) =>
        DisplayContent.Stack(this.BuildChildren(children), BuildStackOptions(options));

    /// <summary>
    /// Builds a Tabler card element. (JS: <c>ui.card</c>)
    /// </summary>
    public DisplayContent Card(object? children = null, object? options = null) =>
        DisplayContent.Card(this.BuildChildren(children), BuildCardOptions(options));

    /// <summary>
    /// Builds a Bootstrap/Tabler grid row container. (JS: <c>ui.row</c>)
    /// </summary>
    public DisplayContent Row(object? children = null, object? options = null) =>
        DisplayContent.Row(this.BuildChildren(children), BuildRowOptions(options));

    /// <summary>
    /// Builds a Bootstrap/Tabler grid column. (JS: <c>ui.col</c>)
    /// </summary>
    public DisplayContent Col(object? children = null, object? options = null) =>
        DisplayContent.Col(this.BuildChildren(children), BuildColOptions(options));

    /// <summary>
    /// Builds a horizontal divider. (JS: <c>ui.divider</c>)
    /// </summary>
    public DisplayContent Divider(object? options = null) =>
        DisplayContent.Divider(BuildDividerOptions(options));

    /// <summary>
    /// Builds a URL link element or an action link element with a click handler. (JS: <c>ui.link</c>)
    /// Pass a <see cref="string"/> for URL navigation or an <see cref="Action"/> for a server-side handler.
    /// </summary>
    public DisplayContent Link(string text, object? urlOrHandler, object? options = null)
    {
        var linkOptions = BuildLinkOptions(options);
        switch (urlOrHandler)
        {
            case string url:
                return DisplayContent.Link(text, url, linkOptions);
            case Action action:
                return DisplayContent.Link(text, action, linkOptions);
            case null:
                throw new ArgumentNullException(nameof(urlOrHandler));
            case Delegate d:
                // Runtime delegate wrappers (e.g., Jint JsCallDelegate from JS function arguments)
                // are invoked with default parameter values so zero-arg action lambdas work correctly.
                var defaultArgs = d
                    .Method.GetParameters()
                    .Select(p =>
                        p.ParameterType.IsValueType
                            ? Activator.CreateInstance(p.ParameterType)
                            : (object?)null
                    )
                    .ToArray();
                return DisplayContent.Link(text, () => d.DynamicInvoke(defaultArgs), linkOptions);
            default:
                throw new ArgumentException(
                    "urlOrHandler must be a URL string or an action handler.",
                    nameof(urlOrHandler)
                );
        }
    }

    /// <summary>
    /// Builds a button element with a click handler. (JS: <c>ui.button</c>)
    /// </summary>
    public DisplayContent Button(string label, Action handler, object? options = null) =>
        DisplayContent.Button(label, handler, BuildButtonOptions(options));

    /// <summary>
    /// Builds a <c>table.duetspad-table</c> element from <paramref name="rows"/>.
    /// Columns default to the keys of the first row; pass <c>options.columns</c> to specify
    /// an explicit ordered list of string column names. (JS: <c>ui.table</c>)
    /// </summary>
    public DisplayContent Table(object? rows, object? options = null)
    {
        if (rows is null or string || rows is not IEnumerable rowsEnumerable)
        {
            throw new ArgumentException("rows must be an array.", nameof(rows));
        }

        var rowList = new List<IDictionary<string, object?>>();
        foreach (var row in rowsEnumerable)
        {
            rowList.Add(CoerceRow(row));
        }

        List<string> columns;
        if (options is IDictionary<string, object?> optionsDict)
        {
            columns = ResolveColumns(optionsDict, rowList);
        }
        else if (options is IDictionary nonGenericOptionsDict)
        {
            var converted = ConvertNonGenericDictionary(nonGenericOptionsDict);
            columns = ResolveColumns(converted, rowList);
        }
        else
        {
            columns = ResolveColumnsFromRows(rowList);
        }

        var projectedRows = rowList
            .Select(r => (IReadOnlyList<KeyValuePair<string, object?>>)[.. r])
            .ToList();
        return TableRenderBuilder.Build(
            columns,
            projectedRows,
            v => this._renderer.Render(v, this._dumpOptions)
        );
    }

    private static IDictionary<string, object?> CoerceRow(object? row)
    {
        if (row is IDictionary<string, object?> genericDict)
        {
            return genericDict;
        }

        if (row is IDictionary nonGenericDict)
        {
            return ConvertNonGenericDictionary(nonGenericDict);
        }

        if (RecordProjector.IsRecordLike(row))
        {
            var projected = RecordProjector.Project(row!);
            var result = new Dictionary<string, object?>(projected.Count);
            foreach (var kv in projected)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
        }

        throw new ArgumentException(
            "invalid row: each row must be an object with key/value pairs.",
            "rows"
        );
    }

    private static IDictionary<string, object?> ConvertNonGenericDictionary(IDictionary dict)
    {
        var result = new Dictionary<string, object?>();
        foreach (DictionaryEntry entry in dict)
        {
            var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "";
            result[key] = entry.Value;
        }

        return result;
    }

    private static List<string> ResolveColumns(
        IDictionary<string, object?> optionsDict,
        List<IDictionary<string, object?>> rowList
    )
    {
        if (!optionsDict.TryGetValue("columns", out var columnsValue))
        {
            return ResolveColumnsFromRows(rowList);
        }

        if (columnsValue is null or string || columnsValue is not IEnumerable columnsEnumerable)
        {
            throw new ArgumentException("options.columns must be an array of strings.", "options");
        }

        var columns = new List<string>();
        foreach (var col in columnsEnumerable)
        {
            if (col is not string colStr)
            {
                throw new ArgumentException(
                    "options.columns must be an array of strings.",
                    "options"
                );
            }

            if (columns.Contains(colStr, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"options.columns contains duplicate column name '{colStr}'.",
                    "options"
                );
            }

            columns.Add(colStr);
        }

        return columns;
    }

    private static List<string> ResolveColumnsFromRows(List<IDictionary<string, object?>> rowList)
    {
        if (rowList.Count == 0)
        {
            return [];
        }

        return [.. rowList[0].Keys];
    }

    private static StackOptions? BuildStackOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new StackOptions();
        if (dict.TryGetValue("direction", out var direction) && direction is not null)
        {
            result = result with
            {
                Direction = Convert.ToString(direction, CultureInfo.InvariantCulture),
            };
        }

        return result;
    }

    private static CardOptions? BuildCardOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new CardOptions();
        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("footer", out var footer) && footer is not null)
        {
            result = result with
            {
                Footer = Convert.ToString(footer, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("color", out var color) && color is not null)
        {
            result = result with { Color = Convert.ToString(color, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static ElementAttributes BuildAttributes(object? attributes)
    {
        if (attributes is null)
        {
            return ElementAttributes.Empty;
        }

        if (attributes is IDictionary<string, object?> genericDict)
        {
            return new ElementAttributes(
                genericDict.Select(kv => new KeyValuePair<string, string?>(
                    kv.Key,
                    kv.Value is null
                        ? null
                        : Convert.ToString(kv.Value, CultureInfo.InvariantCulture)
                ))
            );
        }

        if (attributes is IDictionary nonGenericDict)
        {
            var pairs = new List<KeyValuePair<string, string?>>();
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "";
                var value = entry.Value is null
                    ? null
                    : Convert.ToString(entry.Value, CultureInfo.InvariantCulture);
                pairs.Add(new KeyValuePair<string, string?>(key, value));
            }

            return new ElementAttributes(pairs);
        }

        // Unsupported attributes type — let ElementAttributes constructor surface the error
        // by attempting to iterate; alternatively throw clearly here.
        throw new ArgumentException(
            "attributes must be an object with string keys and string values.",
            nameof(attributes)
        );
    }

    private static LinkOptions? BuildLinkOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new LinkOptions();
        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static ButtonOptions? BuildButtonOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new ButtonOptions();
        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        return result;
    }

    private static ToastOptions BuildToastOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        var title = ExtractOptionalStringOption(dict, "title");
        var variant =
            dict is not null
            && dict.TryGetValue("variant", out var rawVariant)
            && rawVariant is not null
                ? Convert.ToString(rawVariant, CultureInfo.InvariantCulture)
                    ?? ToastOptions.DefaultVariant
                : ToastOptions.DefaultVariant;
        if (!ToastOptions.SupportedVariants.Contains(variant))
        {
            throw new ArgumentException(
                "variant must be \"info\", \"success\", \"warning\", or \"danger\".",
                nameof(options)
            );
        }

        var durationMilliseconds = ToastOptions.DefaultDurationMilliseconds;
        if (
            dict is not null
            && dict.TryGetValue("durationMs", out var rawDuration)
            && rawDuration is not null
        )
        {
            durationMilliseconds = CoerceInteger(rawDuration, "durationMs");
        }

        if (durationMilliseconds is < 0 or > ToastOptions.MaximumDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"durationMs must be between 0 and {ToastOptions.MaximumDurationMilliseconds}."
            );
        }

        return new ToastOptions(title, variant, durationMilliseconds);
    }

    private static DialogOptions BuildDialogOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        var title = ExtractOptionalStringOption(dict, "title")?.Trim();
        if (title?.Length == 0)
        {
            title = null;
        }

        var buttons = BuildDialogButtons(dict);
        var defaultButtonId = ExtractOptionalStringOption(dict, "defaultButtonId")?.Trim();
        if (defaultButtonId?.Length == 0)
        {
            throw new ArgumentException("defaultButtonId cannot be empty.", nameof(options));
        }

        object? dismissValue = null;
        var hasDismissOption =
            dict is not null && dict.TryGetValue("dismissButtonId", out dismissValue);
        var canDismiss = !hasDismissOption || dismissValue is not null;
        var dismissButtonId = dismissValue is null
            ? null
            : Convert.ToString(dismissValue, CultureInfo.InvariantCulture)?.Trim();
        if (canDismiss && dismissButtonId?.Length == 0)
        {
            throw new ArgumentException("dismissButtonId cannot be empty.", nameof(options));
        }

        var buttonIds = buttons.Select(button => button.Id).ToHashSet(StringComparer.Ordinal);
        if (defaultButtonId is not null && !buttonIds.Contains(defaultButtonId))
        {
            throw new ArgumentException(
                "defaultButtonId must reference a dialog button.",
                nameof(options)
            );
        }

        if (dismissButtonId is not null && !buttonIds.Contains(dismissButtonId))
        {
            throw new ArgumentException(
                "dismissButtonId must reference a dialog button.",
                nameof(options)
            );
        }

        var size = ExtractOptionalStringOption(dict, "size")?.Trim() ?? "md";
        if (size is not ("sm" or "md" or "lg" or "xl"))
        {
            throw new ArgumentException(
                "size must be \"sm\", \"md\", \"lg\", or \"xl\".",
                nameof(options)
            );
        }

        return new DialogOptions(
            title,
            buttons,
            defaultButtonId,
            canDismiss,
            dismissButtonId,
            size
        );
    }

    private static IReadOnlyList<DialogButtonDefinition> BuildDialogButtons(
        IDictionary<string, object?>? options
    )
    {
        if (options is null || !options.TryGetValue("buttons", out var value) || value is null)
        {
            return [];
        }

        if (value is string || value is not IEnumerable values)
        {
            throw new ArgumentException("buttons must be an array.", nameof(options));
        }

        var result = new List<DialogButtonDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            string id;
            string label;
            string variant;
            if (item is string text)
            {
                id = text.Trim();
                label = id;
                variant = "default";
            }
            else
            {
                var dict =
                    CoerceOptionsDictionary(item)
                    ?? throw new ArgumentException(
                        "Each button must be a string or an object.",
                        nameof(options)
                    );
                id = ExtractRequiredDialogButtonString(dict, "id", options);
                label = ExtractRequiredDialogButtonString(dict, "label", options);
                variant = ExtractOptionalStringOption(dict, "variant")?.Trim() ?? "default";
            }

            if (id.Length == 0 || label.Length == 0)
            {
                throw new ArgumentException(
                    "Dialog button ids and labels cannot be empty.",
                    nameof(options)
                );
            }

            if (!ids.Add(id))
            {
                throw new ArgumentException(
                    $"Dialog button id '{id}' is duplicated.",
                    nameof(options)
                );
            }

            if (variant is not ("default" or "primary" or "danger"))
            {
                throw new ArgumentException(
                    "Dialog button variant must be \"default\", \"primary\", or \"danger\".",
                    nameof(options)
                );
            }

            result.Add(new DialogButtonDefinition(id, label, variant));
        }

        return result;
    }

    private static string ExtractRequiredDialogButtonString(
        IDictionary<string, object?> button,
        string name,
        object? options
    ) =>
        button.TryGetValue(name, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""
            : throw new ArgumentException($"Dialog button {name} is required.", nameof(options));

    private static string? ExtractOptionalStringOption(
        IDictionary<string, object?>? options,
        string name
    ) =>
        options is not null && options.TryGetValue(name, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    private static BadgeOptions? BuildBadgeOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new BadgeOptions();
        if (dict.TryGetValue("color", out var color) && color is not null)
        {
            result = result with { Color = Convert.ToString(color, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("pill", out var pill) && pill is not null)
        {
            result = result with { Pill = Convert.ToBoolean(pill, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("outline", out var outline) && outline is not null)
        {
            result = result with
            {
                Outline = Convert.ToBoolean(outline, CultureInfo.InvariantCulture),
            };
        }

        return result;
    }

    private static AlertOptions? BuildAlertOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new AlertOptions();
        if (dict.TryGetValue("variant", out var variant) && variant is not null)
        {
            result = result with
            {
                Variant = Convert.ToString(variant, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static SpinnerOptions? BuildSpinnerOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new SpinnerOptions();
        if (dict.TryGetValue("color", out var color) && color is not null)
        {
            result = result with { Color = Convert.ToString(color, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("small", out var small) && small is not null)
        {
            result = result with { Small = Convert.ToBoolean(small, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static StatusOptions? BuildStatusOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new StatusOptions();
        if (dict.TryGetValue("color", out var color) && color is not null)
        {
            result = result with { Color = Convert.ToString(color, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("animated", out var animated) && animated is not null)
        {
            result = result with
            {
                Animated = Convert.ToBoolean(animated, CultureInfo.InvariantCulture),
            };
        }

        return result;
    }

    private static IconOptions? BuildIconOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new IconOptions();
        if (dict.TryGetValue("size", out var size) && size is not null)
        {
            result = result with { Size = Convert.ToDouble(size, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("color", out var color) && color is not null)
        {
            result = result with { Color = Convert.ToString(color, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static ProgressOptions? BuildProgressOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new ProgressOptions();
        if (dict.TryGetValue("color", out var color) && color is not null)
        {
            result = result with { Color = Convert.ToString(color, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("label", out var label) && label is not null)
        {
            result = result with { Label = Convert.ToString(label, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static RowOptions? BuildRowOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new RowOptions();
        if (dict.TryGetValue("gutter", out var gutter) && gutter is not null)
        {
            result = result with { Gutter = ResolveGutter(gutter) };
        }

        return result;
    }

    // Resolves the JS-side gutter value ("sm"/"md"/"lg" alias or a number) to a
    // Tabler gutter step. This is the script-boundary coercion; range validation
    // (0-5) lives in DisplayContent.BuildRowAttributes.
    private static int ResolveGutter(object gutter)
    {
        if (gutter is string s)
        {
            return s switch
            {
                "sm" => 1,
                "md" => 3,
                "lg" => 5,
                _ => throw new ArgumentException(
                    $"gutter must be \"sm\", \"md\", \"lg\", or a number (0-5). Got \"{s}\".",
                    nameof(gutter)
                ),
            };
        }

        return CoerceInteger(gutter, "gutter");
    }

    // Converts a JS-side numeric value to an int, rejecting fractional numbers
    // rather than silently rounding them (JS has no integer type, so a span or
    // gutter literal arrives as a double). Range validation lives in DisplayContent.
    private static int CoerceInteger(object value, string paramName)
    {
        var raw = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (raw != Math.Truncate(raw))
        {
            throw new ArgumentException($"{paramName} must be an integer. Got {raw}.", paramName);
        }

        return (int)raw;
    }

    private static ColOptions? BuildColOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new ColOptions();
        if (dict.TryGetValue("span", out var span) && span is not null)
        {
            result = result with { Span = CoerceInteger(span, "span") };
        }

        if (dict.TryGetValue("sm", out var sm) && sm is not null)
        {
            result = result with { Sm = CoerceInteger(sm, "sm") };
        }

        if (dict.TryGetValue("md", out var md) && md is not null)
        {
            result = result with { Md = CoerceInteger(md, "md") };
        }

        if (dict.TryGetValue("lg", out var lg) && lg is not null)
        {
            result = result with { Lg = CoerceInteger(lg, "lg") };
        }

        if (dict.TryGetValue("xl", out var xl) && xl is not null)
        {
            result = result with { Xl = CoerceInteger(xl, "xl") };
        }

        return result;
    }

    private static DividerOptions? BuildDividerOptions(object? options)
    {
        var dict = CoerceOptionsDictionary(options);
        if (dict is null)
        {
            return null;
        }

        var result = new DividerOptions();
        if (dict.TryGetValue("text", out var text) && text is not null)
        {
            result = result with { Text = Convert.ToString(text, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("color", out var color) && color is not null)
        {
            result = result with { Color = Convert.ToString(color, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static string ExtractInitialValue(
        IDictionary<string, object?>? dict,
        string fallback = ""
    )
    {
        if (dict is not null && dict.TryGetValue("value", out var value) && value is not null)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        return fallback;
    }

    private static bool ExtractInitialChecked(IDictionary<string, object?>? dict)
    {
        if (dict is not null && dict.TryGetValue("checked", out var value) && value is not null)
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        return false;
    }

    private static string FormatNumberForValue(double value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static TextBoxOptions BuildTextBoxOptions(IDictionary<string, object?>? dict)
    {
        var result = new TextBoxOptions();
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("name", out var name) && name is not null)
        {
            result = result with { Name = Convert.ToString(name, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("placeholder", out var placeholder) && placeholder is not null)
        {
            result = result with
            {
                Placeholder = Convert.ToString(placeholder, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static FilePickerOptions BuildFilePickerOptions(IDictionary<string, object?>? dict)
    {
        var result = new FilePickerOptions();
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("accept", out var accept) && accept is not null)
        {
            result = result with
            {
                Accept = Convert.ToString(accept, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("multiple", out var multiple) && multiple is not null)
        {
            result = result with
            {
                Multiple = Convert.ToBoolean(multiple, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        return result;
    }

    private static TextAreaOptions BuildTextAreaOptions(IDictionary<string, object?>? dict)
    {
        var result = new TextAreaOptions();
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("name", out var name) && name is not null)
        {
            result = result with { Name = Convert.ToString(name, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("placeholder", out var placeholder) && placeholder is not null)
        {
            result = result with
            {
                Placeholder = Convert.ToString(placeholder, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("rows", out var rows) && rows is not null)
        {
            result = result with { Rows = CoerceInteger(rows, "rows") };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static NumberBoxOptions BuildNumberBoxOptions(IDictionary<string, object?>? dict)
    {
        var result = new NumberBoxOptions();
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("name", out var name) && name is not null)
        {
            result = result with { Name = Convert.ToString(name, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("min", out var min) && min is not null)
        {
            result = result with { Min = Convert.ToDouble(min, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("max", out var max) && max is not null)
        {
            result = result with { Max = Convert.ToDouble(max, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("step", out var step) && step is not null)
        {
            result = result with { Step = Convert.ToDouble(step, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static CheckBoxOptions BuildCheckBoxOptions(IDictionary<string, object?>? dict)
    {
        var result = new CheckBoxOptions();
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("label", out var label) && label is not null)
        {
            result = result with { Label = Convert.ToString(label, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static DropDownOptions BuildDropDownOptions(
        object? items,
        IDictionary<string, object?>? dict
    )
    {
        var result = new DropDownOptions { Items = CoerceFieldOptions(items) };
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("name", out var name) && name is not null)
        {
            result = result with { Name = Convert.ToString(name, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static SliderOptions BuildSliderOptions(IDictionary<string, object?>? dict)
    {
        var result = new SliderOptions();
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("name", out var name) && name is not null)
        {
            result = result with { Name = Convert.ToString(name, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("min", out var min) && min is not null)
        {
            result = result with { Min = Convert.ToDouble(min, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("max", out var max) && max is not null)
        {
            result = result with { Max = Convert.ToDouble(max, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("step", out var step) && step is not null)
        {
            result = result with { Step = Convert.ToDouble(step, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static RadioGroupOptions BuildRadioGroupOptions(
        object? items,
        IDictionary<string, object?>? dict
    )
    {
        var result = new RadioGroupOptions { Items = CoerceFieldOptions(items) };
        if (dict is null)
        {
            return result;
        }

        if (dict.TryGetValue("name", out var name) && name is not null)
        {
            result = result with { Name = Convert.ToString(name, CultureInfo.InvariantCulture) };
        }

        if (dict.TryGetValue("className", out var className) && className is not null)
        {
            result = result with
            {
                ClassName = Convert.ToString(className, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("disabled", out var disabled) && disabled is not null)
        {
            result = result with
            {
                Disabled = Convert.ToBoolean(disabled, CultureInfo.InvariantCulture),
            };
        }

        if (dict.TryGetValue("title", out var title) && title is not null)
        {
            result = result with { Title = Convert.ToString(title, CultureInfo.InvariantCulture) };
        }

        return result;
    }

    private static IReadOnlyList<FieldOption> CoerceFieldOptions(object? items)
    {
        if (items is null or string || items is not IEnumerable itemsEnumerable)
        {
            throw new ArgumentException("items must be an array.", nameof(items));
        }

        var result = new List<FieldOption>();
        foreach (var item in itemsEnumerable)
        {
            result.Add(CoerceFieldOption(item));
        }

        return result;
    }

    private static FieldOption CoerceFieldOption(object? item)
    {
        if (item is string s)
        {
            return new FieldOption(s, s);
        }

        var dict =
            CoerceOptionsDictionary(item)
            ?? throw new ArgumentException(
                "invalid item: each item must be a string or a { value, label } object.",
                "items"
            );

        var value =
            dict.TryGetValue("value", out var v) && v is not null
                ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
                : throw new ArgumentException("item.value is required.", "items");
        var label =
            dict.TryGetValue("label", out var l) && l is not null
                ? Convert.ToString(l, CultureInfo.InvariantCulture) ?? ""
                : value;
        return new FieldOption(value, label);
    }

    private static IDictionary<string, object?>? CoerceOptionsDictionary(object? options)
    {
        return options switch
        {
            null => null,
            IDictionary<string, object?> generic => generic,
            IDictionary nonGeneric => ConvertNonGenericDictionary(nonGeneric),
            _ => throw new ArgumentException("options must be an object.", nameof(options)),
        };
    }

    private static double CoerceProgressValue(object? value)
    {
        if (value is null or string)
        {
            throw new ArgumentException("value must be a number.", nameof(value));
        }

        return value switch
        {
            byte
            or sbyte
            or short
            or ushort
            or int
            or uint
            or long
            or ulong
            or float
            or double
            or decimal => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("value must be a number.", nameof(value)),
        };
    }

    private IReadOnlyList<DisplayContent> BuildChildren(object? children)
    {
        if (children is null)
        {
            return [];
        }

        if (children is string)
        {
            throw new ArgumentException(
                "children must be an array, not a string.",
                nameof(children)
            );
        }

        if (children is IEnumerable childrenEnumerable)
        {
            return
            [
                .. childrenEnumerable
                    .Cast<object?>()
                    .Select(v => this._renderer.Render(v, this._dumpOptions)),
            ];
        }

        throw new ArgumentException("children must be an array.", nameof(children));
    }
}
