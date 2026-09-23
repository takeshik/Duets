using System.Net;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>How a link appears in the source; see <see cref="MarkdownLinks"/> for the subset.</summary>
internal enum MarkdownLinkKind
{
    /// <summary>An inline link <c>[text](target)</c>; <c>Text</c> is the displayed text.</summary>
    Inline,

    /// <summary>A reference definition <c>[label]: target</c>; <c>Text</c> is the label, not displayed text.</summary>
    Definition,

    /// <summary>A reference usage resolved through its definition; <c>Text</c> is the displayed text.</summary>
    ReferenceUsage,

    /// <summary>A construct that starts like a link but is outside the supported subset.</summary>
    Malformed,
}

/// <summary>A Markdown link found outside fenced code and code spans.</summary>
internal sealed record MarkdownLink(
    int LineNumber,
    MarkdownLinkKind Kind,
    string Text,
    string RawTarget
);

/// <summary>
/// Tracks fenced-code state line by line with CommonMark's rules: an opener is a run of three or
/// more backticks or tildes whose info string (for backticks) contains no backtick; a closer is a
/// run of the same character, at least as long, followed only by whitespace.
/// </summary>
internal sealed partial class FenceTracker
{
    private char _marker;
    private int _length;

    /// <summary>Whether the current position is inside a fence.</summary>
    public bool InFence { get; private set; }

    /// <summary>Consumes a line; returns true when the line is a fence delimiter (never content).</summary>
    public bool Consume(string line)
    {
        if (!this.InFence)
        {
            var opener = OpenerRegex().Match(line);
            if (!opener.Success)
                return false;

            var marker = opener.Groups["marker"].Value;
            if (marker[0] == '`' && opener.Groups["info"].Value.Contains('`'))
                return false;

            this.InFence = true;
            this._marker = marker[0];
            this._length = marker.Length;
            return true;
        }

        var closer = CloserRegex().Match(line);
        if (!closer.Success)
            return false;

        var closerMarker = closer.Groups["marker"].Value;
        if (closerMarker[0] != this._marker || closerMarker.Length < this._length)
            return false;

        this.InFence = false;
        return true;
    }

    [GeneratedRegex(@"^ {0,3}(?<marker>`{3,}|~{3,})(?<info>.*)$")]
    private static partial Regex OpenerRegex();

    [GeneratedRegex(@"^ {0,3}(?<marker>`{3,}|~{3,})[ \t]*$")]
    private static partial Regex CloserRegex();
}

/// <summary>
/// Markdown link enumeration and local-target resolution shared by the documentation checks.
/// The supported subset is deliberate and is stated in CONTRIBUTING.md: inline links whose
/// destination is one non-empty token without whitespace, parentheses, or angle brackets
/// (optionally wrapped in angle brackets), closed by ")" on the same line and without a title;
/// reference definitions "[label]: target" alone on their line, without a title; full, collapsed,
/// and shortcut reference usages; footnote references "[^label]" and footnote definitions
/// "[^label]: text" whose text may itself hold links. Code spans and backslash-escaped brackets are
/// not links. Anything else that starts like an inline link, a definition, or a full or collapsed
/// reference usage is reported as malformed rather than ignored.
/// </summary>
internal static partial class MarkdownLinks
{
    /// <summary>Enumerates links, definitions, reference usages, and malformed links, skipping fences and code spans.</summary>
    public static IEnumerable<MarkdownLink> Enumerate(IReadOnlyList<string> lines)
    {
        var definitions = Definitions(lines);
        var tracker = new FenceTracker();
        for (var index = 0; index < lines.Count; index++)
        {
            if (tracker.Consume(lines[index]) || tracker.InFence)
                continue;

            var line = Scrub(lines[index]);
            var definition = LinkDefinitionRegex().Match(line);
            if (definition.Success)
            {
                yield return new MarkdownLink(
                    index + 1,
                    MarkdownLinkKind.Definition,
                    definition.Groups["label"].Value,
                    definition.Groups["target"].Value
                );
                continue;
            }

            if (DefinitionStartRegex().IsMatch(line))
            {
                yield return new MarkdownLink(
                    index + 1,
                    MarkdownLinkKind.Malformed,
                    "",
                    line.Trim()
                );
                continue;
            }

            var unclosed = UnclosedReferenceRegex().Match(line);
            if (unclosed.Success)
                yield return new MarkdownLink(
                    index + 1,
                    MarkdownLinkKind.Malformed,
                    "",
                    line[unclosed.Index..].Trim()
                );

            var inlineStarts = InlineStartRegex()
                .Matches(line)
                .Select(match => match.Index)
                .ToHashSet();
            foreach (Match link in InlineLinkRegex().Matches(line))
            {
                inlineStarts.Remove(link.Index);
                yield return new MarkdownLink(
                    index + 1,
                    MarkdownLinkKind.Inline,
                    link.Groups["text"].Value,
                    link.Groups["target"].Value
                );
            }

            foreach (var start in inlineStarts.Order())
                yield return new MarkdownLink(
                    index + 1,
                    MarkdownLinkKind.Malformed,
                    "",
                    line[start..]
                );

            foreach (Match usage in ReferenceUsageRegex().Matches(line))
            {
                var label =
                    usage.Groups["label"].Success && usage.Groups["label"].Value.Length > 0
                        ? usage.Groups["label"].Value
                        : usage.Groups["text"].Value;
                if (definitions.TryGetValue(NormalizeLabel(label), out var target))
                    yield return new MarkdownLink(
                        index + 1,
                        MarkdownLinkKind.ReferenceUsage,
                        usage.Groups["text"].Value,
                        target
                    );
            }
        }
    }

    /// <summary>
    /// Finds full and collapsed reference usages whose label has no definition; a shortcut
    /// reference without a definition is literal text in CommonMark and is not reported.
    /// </summary>
    public static IEnumerable<(int LineNumber, string Label)> UndefinedReferences(
        IReadOnlyList<string> lines
    )
    {
        var definitions = Definitions(lines);
        var tracker = new FenceTracker();
        for (var index = 0; index < lines.Count; index++)
        {
            if (tracker.Consume(lines[index]) || tracker.InFence)
                continue;

            var line = Scrub(lines[index]);
            if (LinkDefinitionRegex().IsMatch(line) || DefinitionStartRegex().IsMatch(line))
                continue;

            foreach (Match usage in ReferenceUsageRegex().Matches(line))
            {
                if (!usage.Groups["label"].Success)
                    continue;

                var label =
                    usage.Groups["label"].Value.Length > 0
                        ? usage.Groups["label"].Value
                        : usage.Groups["text"].Value;
                if (!definitions.ContainsKey(NormalizeLabel(label)))
                    yield return (index + 1, label);
            }
        }
    }

    /// <summary>
    /// Finds footnote references without a definition and footnote definitions that nothing
    /// references; either is a footnote that does not render as written.
    /// </summary>
    public static IEnumerable<(int LineNumber, string Message)> FootnoteErrors(
        IReadOnlyList<string> lines
    )
    {
        var defined = new Dictionary<string, int>(StringComparer.Ordinal);
        var used = new List<(int LineNumber, string Label)>();
        var tracker = new FenceTracker();
        for (var index = 0; index < lines.Count; index++)
        {
            if (tracker.Consume(lines[index]) || tracker.InFence)
                continue;

            var line = ScrubCode(lines[index]);
            var definition = FootnoteDefinitionRegex().Match(line);
            if (definition.Success)
            {
                defined.TryAdd(NormalizeLabel(definition.Groups["label"].Value), index + 1);
                line = line[definition.Length..];
            }

            foreach (Match reference in FootnoteReferenceRegex().Matches(line))
                used.Add((index + 1, reference.Groups["label"].Value));
        }

        foreach (var (lineNumber, label) in used)
        {
            if (!defined.ContainsKey(NormalizeLabel(label)))
                yield return (lineNumber, $"footnote [^{label}] has no definition");
        }

        var referenced = used.Select(entry => NormalizeLabel(entry.Label)).ToHashSet();
        foreach (var (label, lineNumber) in defined)
        {
            if (!referenced.Contains(label))
                yield return (lineNumber, $"footnote definition [^{label}] is never referenced");
        }
    }

    /// <summary>
    /// Resolves a link target relative to the file that contains it. Returns null for external
    /// targets, which are not checked; otherwise returns the diagnostic (or null when the target
    /// resolves) and the full path of the resolved local file or directory.
    /// </summary>
    public static (string? Error, string? FullPath) Resolve(
        string root,
        string sourceRelativePath,
        string rawTarget,
        Dictionary<string, IReadOnlySet<string>> anchorCache
    )
    {
        var target = rawTarget.Trim('<', '>');
        if (IsExternal(target))
            return (null, null);
        if (target.StartsWith('/'))
            return ($"local link must be a relative path: {target}", null);

        var (pathPart, fragment) = SplitTarget(target);
        var sourceFullPath = Path.Combine(root, sourceRelativePath);
        string targetFullPath;
        try
        {
            pathPart = Uri.UnescapeDataString(pathPart);
            fragment = fragment is null ? null : Uri.UnescapeDataString(fragment);
            targetFullPath =
                pathPart.Length == 0
                    ? sourceFullPath
                    : Path.GetFullPath(
                        Path.Combine(Path.GetDirectoryName(sourceFullPath)!, pathPart)
                    );
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or NotSupportedException
                        or PathTooLongException
                        or UriFormatException
            )
        {
            return ($"invalid local link target: {target}", null);
        }

        if (!IsWithinRoot(root, targetFullPath))
            return ($"local link escapes the repository: {target}", null);

        if (!File.Exists(targetFullPath) && !Directory.Exists(targetFullPath))
            return ($"local link target does not exist: {target}", null);

        if (
            string.IsNullOrEmpty(fragment)
            || !File.Exists(targetFullPath)
            || !Path.GetExtension(targetFullPath).Equals(".md", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (null, targetFullPath);
        }

        var anchors = GetAnchors(targetFullPath, anchorCache);
        return anchors.Contains(fragment)
            ? (null, targetFullPath)
            : ($"Markdown anchor does not exist: {target}", targetFullPath);
    }

    /// <summary>
    /// The normalized full path a relative local target denotes, computed from strings alone (no
    /// filesystem access), with fragment and query removed and percent-encoding decoded; null for
    /// external, absolute, empty, or undecodable targets.
    /// </summary>
    public static string? DenotedPath(string sourceDirectoryFullPath, string rawTarget)
    {
        var target = rawTarget.Trim('<', '>');
        if (IsExternal(target) || target.StartsWith('/'))
            return null;

        var (pathPart, _) = SplitTarget(target);
        if (pathPart.Length == 0)
            return null;

        try
        {
            return Path.GetFullPath(
                Path.Combine(sourceDirectoryFullPath, Uri.UnescapeDataString(pathPart))
            );
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or NotSupportedException
                        or PathTooLongException
                        or UriFormatException
            )
        {
            return null;
        }
    }

    private static bool IsExternal(string target) =>
        target.Length == 0 || ExternalTargetRegex().IsMatch(target);

    private static (string PathPart, string? Fragment) SplitTarget(string target)
    {
        var fragmentIndex = target.IndexOf('#');
        var pathPart = fragmentIndex >= 0 ? target[..fragmentIndex] : target;
        var fragment = fragmentIndex >= 0 ? target[(fragmentIndex + 1)..] : null;
        var queryIndex = pathPart.IndexOf('?');
        if (queryIndex >= 0)
            pathPart = pathPart[..queryIndex];
        return (pathPart, fragment);
    }

    // Code spans, backslash-escaped brackets, and footnote syntax are not link syntax; blank them
    // out while keeping column positions stable. A footnote definition's text stays, so the links
    // it holds are read like any other.
    private static string Scrub(string line)
    {
        var scrubbed = ScrubCode(line);
        scrubbed = FootnoteDefinitionRegex()
            .Replace(scrubbed, match => new string(' ', match.Length));
        return FootnoteReferenceRegex().Replace(scrubbed, match => new string(' ', match.Length));
    }

    private static string ScrubCode(string line)
    {
        var scrubbed = CodeSpanRegex().Replace(line, match => new string(' ', match.Length));
        return EscapedBracketRegex().Replace(scrubbed, "  ");
    }

    private static Dictionary<string, string> Definitions(IReadOnlyList<string> lines)
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var tracker = new FenceTracker();
        foreach (var raw in lines)
        {
            if (tracker.Consume(raw) || tracker.InFence)
                continue;

            var definition = LinkDefinitionRegex().Match(Scrub(raw));
            if (definition.Success)
                definitions.TryAdd(
                    NormalizeLabel(definition.Groups["label"].Value),
                    definition.Groups["target"].Value
                );
        }

        return definitions;
    }

    // CommonMark matches labels case-insensitively with interior whitespace collapsed.
    private static string NormalizeLabel(string label) =>
        WhitespaceRegex().Replace(label.Trim(), " ").ToLowerInvariant();

    private static IReadOnlySet<string> GetAnchors(
        string fullPath,
        Dictionary<string, IReadOnlySet<string>> anchorCache
    )
    {
        if (anchorCache.TryGetValue(fullPath, out var cached))
            return cached;

        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var slugCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var tracker = new FenceTracker();
        foreach (var line in File.ReadLines(fullPath))
        {
            if (tracker.Consume(line) || tracker.InFence)
                continue;

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var baseSlug = CreateHeadingSlug(heading.Groups["text"].Value);
                if (baseSlug.Length > 0)
                {
                    slugCounts.TryGetValue(baseSlug, out var duplicateCount);
                    var slug = duplicateCount == 0 ? baseSlug : $"{baseSlug}-{duplicateCount}";
                    slugCounts[baseSlug] = duplicateCount + 1;
                    anchors.Add(slug);
                }
            }

            foreach (Match anchor in HtmlAnchorRegex().Matches(line))
                anchors.Add(anchor.Groups["anchor"].Value);
        }

        anchorCache.Add(fullPath, anchors);
        return anchors;
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return relativePath != ".."
            && !relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal
            )
            && !Path.IsPathRooted(relativePath);
    }

    private static string CreateHeadingSlug(string heading)
    {
        var text = WebUtility.HtmlDecode(HtmlTagRegex().Replace(heading, ""));
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
        {
            if (
                char.IsLetterOrDigit(character)
                || character is '-' or '_'
                || char.IsWhiteSpace(character)
            )
                builder.Append(character);
        }

        return WhitespaceRegex().Replace(builder.ToString().Trim(), "-");
    }

    // An inline link in the supported subset: a bare destination without whitespace or
    // parentheses, or an angle-bracketed one, closed by ")" on the same line, no title.
    [GeneratedRegex(@"!?\[(?<text>[^\[\]]*)\]\((?<target><[^\s()<>]+>|[^\s()]+)\)")]
    private static partial Regex InlineLinkRegex();

    // A full or collapsed reference usage whose second bracket never closes on the line.
    [GeneratedRegex(@"\[[^\[\]]+\]\[[^\]]*$")]
    private static partial Regex UnclosedReferenceRegex();

    // Anything that begins like a reference definition; those not matched by the definition
    // grammar (for example with a title) are malformed.
    [GeneratedRegex(@"^ {0,3}\[[^\[\]]+\]:")]
    private static partial Regex DefinitionStartRegex();

    // Anything that begins like an inline link; those not matched above are malformed.
    [GeneratedRegex(@"!?\[[^\[\]]*\]\(")]
    private static partial Regex InlineStartRegex();

    // A reference usage: [text][label], [text][] (collapsed), or [text] (shortcut) that is not
    // immediately followed by "(" (inline link) or ":" (definition).
    [GeneratedRegex(@"(?<!\!)\[(?<text>[^\[\]]+)\](?:\[(?<label>[^\[\]]*)\]|(?![\(\[:]))")]
    private static partial Regex ReferenceUsageRegex();

    [GeneratedRegex(@"^ {0,3}\[(?<label>[^\[\]]+)\]:\s*(?<target><[^>\r\n]+>|[^\s]+)\s*$")]
    private static partial Regex LinkDefinitionRegex();

    // The label-and-colon prefix of a footnote definition, "[^label]:" at the start of its line.
    [GeneratedRegex(@"^ {0,3}\[\^(?<label>[^\[\]\s]+)\]:")]
    private static partial Regex FootnoteDefinitionRegex();

    // A footnote reference "[^label]".
    [GeneratedRegex(@"\[\^(?<label>[^\[\]\s]+)\]")]
    private static partial Regex FootnoteReferenceRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalTargetRegex();

    [GeneratedRegex(@"^ {0,3}#{1,6}\s+(?<text>.*?\S)(?:\s+#+\s*)?$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(
        """<a\s+(?:[^>]*?\s)?(?:id|name)=["'](?<anchor>[^"']+)["'][^>]*>""",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex HtmlAnchorRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"(?<ticks>`+)(?:(?!\k<ticks>).)+\k<ticks>")]
    private static partial Regex CodeSpanRegex();

    [GeneratedRegex(@"\\[\[\]()]")]
    private static partial Regex EscapedBracketRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
