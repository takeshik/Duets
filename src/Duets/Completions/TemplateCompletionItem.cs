namespace Duets.Completions;

/// <summary>A single host-provided completion candidate for a tagged-template body.</summary>
public sealed record TemplateCompletionItem(
    string Label,
    string? InsertText = null,
    TextSpan? ReplacementSpan = null,
    TemplateCompletionKind Kind = TemplateCompletionKind.Value,
    string? FilterText = null,
    string? SortText = null,
    string? Detail = null,
    string? Documentation = null
);
