namespace Duets.Completions;

/// <summary>Client-provided context passed to a tagged-template completion callback.</summary>
/// <param name="Tag">The registered tag name.</param>
/// <param name="TextBeforeCaret">Template-segment text before the caret.</param>
/// <param name="TextAfterCaret">Template-segment text after the caret.</param>
/// <param name="CurrentSegmentRaw">The full current raw template segment.</param>
/// <param name="SegmentIndex">The current segment index.</param>
/// <param name="CaretOffsetInSegment">The UTF-16 caret offset within the current segment.</param>
/// <param name="RawSegments">All raw template segments available to the client.</param>
public sealed record TemplateCompletionContext(
    string Tag,
    string TextBeforeCaret,
    string TextAfterCaret,
    string CurrentSegmentRaw,
    int SegmentIndex,
    int CaretOffsetInSegment,
    IReadOnlyList<string> RawSegments
);
