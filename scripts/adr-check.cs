#!/usr/bin/env dotnet
/*
#MISE description="Check ADR structure and change rules (structural only; no semantic validation)"
#MISE alias="adr-check"
*/

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// Deterministic check of docs/decisions/ against the structural rules in docs/decisions/README.md.
// Each rule reaches what its kind reaches (README, "How the rules apply"): standing rules
// (numbering, headings, status vocabulary, relation syntax and reciprocity, Maintenance Note
// placement, links, index rows) every record; writing rules (section layout, title-derived
// filename, references to later records, no Maintenance Note) the records that are open in the
// change; and, with --base, change rules the records the change modifies. It does not and cannot
// verify semantic correctness: whether a decision is single, alternatives real, context
// contemporaneous, or an edit meaning-preserving is decided only by review.
//
// This is a separate script rather than an extension of docs-check.cs because its change rules
// need a base revision (whether a record that has left Proposed changed and whether a status
// transition is allowed are questions about a diff, not about a tree), while
// docs-check.cs applies tree-wide rules; keeping them apart also keeps each failure identifiable as
// one kind of problem.
//
// The base revision is read fail-closed: a record whose base Status cannot be established fails
// the change rules instead of passing them.

string? baseRevision = null;
var root = solutionRoot;
var selfTest = false;
var noProposed = false;
for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--base":
            baseRevision = args[++index];
            break;
        case "--root":
            root = Path.GetFullPath(args[++index]);
            break;
        case "--no-proposed":
            noProposed = true;
            break;
        case "--self-test":
            selfTest = true;
            break;
        default:
            Console.Error.WriteLine(
                "usage: adr-check.cs [--base <revision>] [--root <dir>] [--no-proposed] "
                    + "| --self-test"
            );
            return 2;
    }
}

Console.WriteLine("ADR check: structural rules only; semantic correctness is not verified.");

if (selfTest)
    return await AdrSelfTest.RunAsync();

var checker = new AdrChecker(root, noProposed: noProposed);
var report = await checker.CheckAsync(baseRevision);

foreach (var error in report.Errors)
    Console.Error.WriteLine($"error: {error}");

if (report.Errors.Count == 0)
{
    Console.WriteLine(
        $"Checked {report.RecordCount} ADR record(s) and {report.IndexRowCount} index row(s)"
            + (baseRevision is null ? "." : $" against base {baseRevision}.")
    );
    return 0;
}

Console.Error.WriteLine($"ADR check failed with {report.Errors.Count} error(s).");
return 1;

internal sealed record AdrRelation(
    string Label,
    int Target,
    string TargetFile,
    string? Scope,
    int LineNumber
)
{
    public (string Label, int Target, string? Scope) Key => (this.Label, this.Target, this.Scope);
}

internal sealed class AdrRecord
{
    public required int Number { get; init; }
    public required string FileName { get; init; }
    public required string Text { get; init; }
    public string? Title { get; set; }

    // The status word, or null when it cannot be established. A base record with a null status
    // is frozen (README, "Body immutability"; the check is fail-closed).
    public string? Status { get; set; }

    public List<AdrRelation> Relations { get; } = [];

    public List<string> Sections { get; } = [];

    // The decision-bearing body: from "## Context" to the end (README, "Body immutability").
    public string? Body { get; set; }

    // Historical text outside the body: neither the title line, the Status section, the
    // Maintenance Note section, nor the body. As immutable as the body.
    public string Protected { get; set; } = "";

    // Append-only entries of the "## Maintenance Note" section, in file order; empty when the
    // record has no such section (README, "Maintenance Note").
    public List<string> MaintenanceNote { get; } = [];

    // Whether the section exists at all, which an empty entry list alone cannot express.
    public bool HasMaintenanceNote { get; set; }

    // Line index (0-based) of "## Context", or the line count when absent.
    public int ContextStart { get; set; }

    // Whether the record is open in the change: absent from the base or Proposed there, or, with no
    // base, Proposed now. Writing rules reach open records only (README, "How the rules apply").
    public bool IsOpen { get; set; }

    // Proposed, Rejected, and Withdrawn records only declare relations; a relation exists once the
    // later record is accepted (README, "Status and relation metadata").
    public bool IsDeclaringOnly => this.Status is "Proposed" or "Rejected" or "Withdrawn";

    public bool IsInForce => this.Status is "Accepted" or "Superseded" or "Deprecated";
}

internal sealed record IndexRow(
    int Number,
    string File,
    string TitleCell,
    string Keywords,
    string Abstract,
    int LineNumber
);

internal sealed class AdrCorpus
{
    public Dictionary<int, AdrRecord> Records { get; } = [];
    public List<IndexRow> IndexRows { get; } = [];
    public string? IndexText { get; set; }
}

internal sealed class AdrReport
{
    public List<string> Errors { get; } = [];
    public int RecordCount { get; set; }
    public int IndexRowCount { get; set; }
}

internal sealed partial class AdrChecker(string root, bool noProposed = false)
{
    private const string DecisionsDirectory = "docs/decisions";
    private const string IndexFile = "index.md";

    private static readonly string[] GovernanceFiles = ["README.md", "_template.md"];

    private static readonly string[] StatusVocabulary =
    [
        "Proposed",
        "Accepted",
        "Rejected",
        "Withdrawn",
        "Superseded",
        "Deprecated",
    ];

    private static readonly string[] CoreSections =
    [
        "Status",
        "Context",
        "Decision",
        "Rationale",
        "Consequences",
    ];

    // The template's sections in their required relative order; sections the template does not
    // name may appear anywhere after Context.
    private static readonly string[] SectionOrder =
    [
        "Status",
        "Maintenance Note",
        "Context",
        "Decision Drivers",
        "Considered Alternatives",
        "Decision",
        "Rationale",
        "Consequences",
        "Confirmation",
        "Revisit Conditions",
    ];

    private static readonly Dictionary<string, string> InverseRelation = new(StringComparer.Ordinal)
    {
        ["Supersedes"] = "Superseded by",
        ["Superseded by"] = "Supersedes",
        ["Amends"] = "Amended by",
        ["Amended by"] = "Amends",
    };

    private static readonly Dictionary<string, string[]> AllowedTransitions = new(
        StringComparer.Ordinal
    )
    {
        ["Proposed"] = ["Accepted", "Rejected", "Withdrawn"],
        ["Accepted"] = ["Superseded", "Deprecated"],
        ["Rejected"] = [],
        ["Withdrawn"] = [],
        ["Superseded"] = [],
        ["Deprecated"] = ["Superseded"],
    };

    private readonly string _root = Path.GetFullPath(root);

    // main carries no Proposed record (README, "Transitions"); CI sets this on a push to main.
    private readonly bool _noProposed = noProposed;
    private readonly AdrReport _report = new();
    private readonly Dictionary<string, IReadOnlySet<string>> _anchorCache = new(
        StringComparer.Ordinal
    );

    private string DecisionsFullPath => Path.Combine(this._root, DecisionsDirectory);

    public async Task<AdrReport> CheckAsync(string? baseRevision)
    {
        var corpus = this.LoadWorkingTree();
        var baseCorpus = baseRevision is null ? null : await this.LoadRevisionAsync(baseRevision);
        foreach (var record in corpus.Records.Values)
        {
            record.IsOpen = baseCorpus is null
                ? record.Status == "Proposed"
                : !baseCorpus.Records.TryGetValue(record.Number, out var baseRecord)
                    || baseRecord.Status == "Proposed";
        }

        this.CheckStatic(corpus);
        if (baseCorpus is not null)
            this.CheckDiff(baseCorpus, corpus);

        this._report.Errors.Sort(StringComparer.Ordinal);
        this._report.RecordCount = corpus.Records.Count;
        this._report.IndexRowCount = corpus.IndexRows.Count;
        return this._report;
    }

    private void Error(string message) => this._report.Errors.Add(message);

    private AdrCorpus LoadWorkingTree()
    {
        var directory = this.DecisionsFullPath;
        if (!Directory.Exists(directory))
        {
            this.Error($"{DecisionsDirectory}: directory does not exist");
            return new AdrCorpus();
        }

        var files = Directory
            .EnumerateFiles(directory, "*.md")
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return this.LoadCorpus(
            files,
            name => File.ReadAllText(Path.Combine(directory, name)),
            "working tree",
            isBase: false
        );
    }

    private async Task<AdrCorpus?> LoadRevisionAsync(string revision)
    {
        var listing = await this.RunGitAsync([
            "ls-tree",
            "--name-only",
            $"{revision}:{DecisionsDirectory}",
        ]);
        if (listing.ExitCode != 0)
        {
            this.Error($"base revision {revision} cannot be read: {listing.StandardError.Trim()}");
            return null;
        }

        var files = listing
            .StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(name => name.EndsWith(".md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in files)
        {
            var show = await this.RunGitAsync(["show", $"{revision}:{DecisionsDirectory}/{name}"]);
            contents[name] = show.StandardOutput;
        }

        // The base is history and is not validated; its parsing diagnostics are discarded. What
        // cannot be parsed stays unknown, and unknown is handled fail-closed by the diff rules.
        var baseChecker = new AdrChecker(this._root);
        return baseChecker.LoadCorpus(
            files,
            name => contents[name],
            $"base {revision}",
            isBase: true
        );
    }

    private AdrCorpus LoadCorpus(
        IReadOnlyList<string> fileNames,
        Func<string, string> read,
        string label,
        bool isBase
    )
    {
        var corpus = new AdrCorpus();
        foreach (var name in fileNames)
        {
            var match = RecordFileRegex().Match(name);
            if (match.Success)
            {
                var number = int.Parse(match.Groups["number"].Value);
                var record = new AdrRecord
                {
                    Number = number,
                    FileName = name,
                    Text = read(name),
                };
                if (!corpus.Records.TryAdd(record.Number, record))
                {
                    this.Error(
                        $"{DecisionsDirectory}/{name}: ADR number {record.Number} is also used by "
                            + $"{corpus.Records[record.Number].FileName} ({label})"
                    );
                    continue;
                }

                this.ParseRecord(record, label, isBase);
            }
            else if (name == IndexFile)
            {
                corpus.IndexText = read(name);
                this.ParseIndex(corpus, label);
            }
            else if (!GovernanceFiles.Contains(name, StringComparer.Ordinal))
            {
                this.Error(
                    $"{DecisionsDirectory}/{name}: is not a valid ADR record filename "
                        + $"(N_kebab-case-title.md) ({label})"
                );
            }
        }

        return corpus;
    }

    private void ParseRecord(AdrRecord record, string label, bool isBase)
    {
        var path = $"{DecisionsDirectory}/{record.FileName}";
        var lines = record.Text.Split('\n');
        var heading = TitleRegex().Match(lines[0]);
        if (!heading.Success)
        {
            this.Error($"{path}:1: first line must be '# ADR-{record.Number}: <Title>' ({label})");
        }
        else
        {
            record.Title = heading.Groups["title"].Value.Trim();
            if (int.Parse(heading.Groups["number"].Value) != record.Number)
                this.Error($"{path}:1: heading number does not match the filename ({label})");
        }

        var sectionStarts = new List<(string Name, int Index)>();
        var tracker = new FenceTracker();
        for (var index = 0; index < lines.Length; index++)
        {
            if (tracker.Consume(lines[index]) || tracker.InFence)
                continue;

            if (SectionRegex().Match(lines[index]) is { Success: true } section)
                sectionStarts.Add((section.Groups["name"].Value.Trim(), index));
        }

        record.Sections.AddRange(sectionStarts.Select(section => section.Name));

        var statusIndex = sectionStarts.FindIndex(section => section.Name == "Status");
        var contextIndex = sectionStarts.FindIndex(section => section.Name == "Context");
        if (statusIndex < 0 && !isBase)
            this.Error($"{path}: '## Status' section is missing ({label})");
        if (contextIndex < 0)
            this.Error($"{path}: '## Context' section is missing ({label})");

        var statusStart = statusIndex < 0 ? -1 : sectionStarts[statusIndex].Index;
        var statusEnd =
            statusIndex < 0 ? -1
            : statusIndex + 1 < sectionStarts.Count ? sectionStarts[statusIndex + 1].Index
            : lines.Length;
        if (statusIndex >= 0)
            this.ParseStatus(record, lines, statusStart + 1, statusEnd, path, label, isBase);

        var contextStart = contextIndex < 0 ? lines.Length : sectionStarts[contextIndex].Index;
        record.ContextStart = contextStart;
        record.Body = contextIndex < 0 ? null : string.Join('\n', lines.Skip(contextStart));

        // The Maintenance Note is append-only metadata, not historical text: it is excluded from
        // the protected regions and compared entry by entry instead (README, "Maintenance Note").
        var noteIndex = sectionStarts.FindIndex(section => section.Name == "Maintenance Note");
        var noteStart = noteIndex < 0 ? -1 : sectionStarts[noteIndex].Index;
        var noteEnd =
            noteIndex < 0 ? -1
            : noteIndex + 1 < sectionStarts.Count ? sectionStarts[noteIndex + 1].Index
            : lines.Length;
        record.HasMaintenanceNote = noteIndex >= 0;
        if (noteIndex >= 0)
        {
            if (
                !isBase
                && (statusIndex < 0 || noteIndex != statusIndex + 1 || contextIndex < noteIndex)
            )
                this.Error(
                    $"{path}: '## Maintenance Note' must sit immediately after '## Status', before "
                        + $"'## Context' ({label})"
                );
            this.ParseMaintenanceNote(record, lines, noteStart + 1, noteEnd, path, label, isBase);
        }
        // Two regions, kept apart: before Status and between Status and Context. Only the blank
        // lines at a region's edges are normalized; interior lines, blank or not, are text.
        var indexed = lines.Select((line, index) => (Line: line, Index: index)).ToArray();
        bool IsText((string Line, int Index) entry) =>
            entry.Index != 0
            && entry.Index < contextStart
            && !(noteIndex >= 0 && entry.Index >= noteStart && entry.Index < noteEnd);
        var beforeStatus =
            statusIndex < 0
                ? []
                : indexed
                    .Where(entry => entry.Index < statusStart && IsText(entry))
                    .Select(entry => entry.Line);
        var afterStatus =
            statusIndex < 0
                ? indexed.Where(IsText).Select(entry => entry.Line)
                : indexed
                    .Where(entry => entry.Index >= statusEnd && IsText(entry))
                    .Select(entry => entry.Line);
        record.Protected = TrimRegion(beforeStatus) + "\u0000" + TrimRegion(afterStatus);
    }

    // Splits the section into entries. An entry starts at a bullet and runs to the next one;
    // continuation lines belong to the entry they follow.
    private void ParseMaintenanceNote(
        AdrRecord record,
        string[] lines,
        int start,
        int end,
        string path,
        string label,
        bool isBase
    )
    {
        var current = new List<string>();
        var stray = new List<int>();

        void Flush()
        {
            if (current.Count == 0)
                return;
            record.MaintenanceNote.Add(string.Join('\n', current).TrimEnd());
            current.Clear();
        }

        for (var index = start; index < end; index++)
        {
            var line = lines[index];
            if (MaintenanceEntryRegex().IsMatch(line))
            {
                Flush();
                current.Add(line);
            }
            else if (current.Count > 0)
                current.Add(line);
            else if (line.Trim().Length > 0)
                stray.Add(index + 1);
        }

        Flush();

        if (isBase)
            return;

        foreach (var lineNumber in stray)
        {
            this.Error(
                $"{path}:{lineNumber}: '## Maintenance Note' holds only entries, and an entry is "
                    + $"a bullet ({label})"
            );
        }

        if (record.MaintenanceNote.Count == 0 && stray.Count == 0)
            this.Error($"{path}: '## Maintenance Note' is present but empty ({label})");
    }

    // Compares the append-only Maintenance Note across the change. Returns true when the record
    // gained an entry, which is what permits its body to change (README, "Editorial revision").
    private bool CheckMaintenanceNoteChange(
        AdrRecord baseRecord,
        AdrRecord record,
        string path,
        bool bodyChanged
    )
    {
        var baseEntries = baseRecord.MaintenanceNote;
        var entries = record.MaintenanceNote;
        if (
            entries.Count < baseEntries.Count
            || !baseEntries.SequenceEqual(entries.Take(baseEntries.Count), StringComparer.Ordinal)
        )
        {
            this.Error(
                $"{path}: '## Maintenance Note' entries were removed, reworded, or reordered; the "
                    + "section is append-only"
            );
            return false;
        }

        if (entries.Count > baseEntries.Count && !bodyChanged)
        {
            this.Error(
                $"{path}: a '## Maintenance Note' entry was added although the body did not "
                    + "change; an entry records an editorial revision that happened"
            );
        }

        return entries.Count > baseEntries.Count;
    }

    private static string TrimRegion(IEnumerable<string> lines)
    {
        var region = lines.ToList();
        while (region.Count > 0 && region[0].Trim().Length == 0)
            region.RemoveAt(0);
        while (region.Count > 0 && region[^1].Trim().Length == 0)
            region.RemoveAt(region.Count - 1);
        return string.Join('\n', region);
    }

    private void ParseStatus(
        AdrRecord record,
        string[] lines,
        int start,
        int end,
        string path,
        string label,
        bool isBase
    )
    {
        var statusSeen = false;
        var proseSeen = false;
        for (var index = start; index < end; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            if (line.Trim().Length == 0)
                continue;

            if (!statusSeen)
            {
                statusSeen = true;
                var word = line.Trim();
                // A base Status word outside the vocabulary stays unknown; the change rules treat
                // unknown as a failure (fail-closed).
                if (StatusVocabulary.Contains(word, StringComparer.Ordinal))
                {
                    record.Status = word;
                }
                else if (!isBase)
                {
                    this.Error(
                        $"{path}:{lineNumber}: first Status paragraph must be one of "
                            + $"{string.Join(", ", StatusVocabulary)}; found '{word}' ({label})"
                    );
                }

                if (!isBase && index + 1 < end && lines[index + 1].Trim().Length > 0)
                    this.Error(
                        $"{path}:{lineNumber + 1}: the first Status paragraph is the status word "
                            + $"alone; separate what follows with a blank line ({label})"
                    );

                continue;
            }

            // The relation list follows the status word; prose, if any, comes after the list
            // (README, "Status and relation metadata").
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                proseSeen = true;
                continue;
            }

            if (proseSeen && !isBase)
                this.Error(
                    $"{path}:{lineNumber}: a relation entry follows prose; the relation list comes "
                        + $"right after the status word and prose after it ({label})"
                );

            var relation = RelationRegex().Match(line);
            if (!relation.Success)
            {
                this.Error(
                    $"{path}:{lineNumber}: Status bullet is not a relation of the form "
                        + "'- <Supersedes|Superseded by|Amends|Amended by>: [ADR-N](file)"
                        + " — <scope>' ({label})"
                );
                continue;
            }

            var scope = relation.Groups["scope"].Success
                ? relation.Groups["scope"].Value.Trim()
                : null;
            record.Relations.Add(
                new AdrRelation(
                    relation.Groups["label"].Value,
                    int.Parse(relation.Groups["number"].Value),
                    relation.Groups["file"].Value,
                    scope,
                    lineNumber
                )
            );
        }

        if (!statusSeen)
            this.Error($"{path}: '## Status' section is empty ({label})");
    }

    private void ParseIndex(AdrCorpus corpus, string label)
    {
        var path = $"{DecisionsDirectory}/{IndexFile}";
        var lines = corpus.IndexText!.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var row = IndexRowRegex().Match(lines[index]);
            if (!row.Success)
            {
                if (lines[index].StartsWith("| [", StringComparison.Ordinal))
                    this.Error($"{path}:{index + 1}: index row has an unexpected shape ({label})");
                continue;
            }

            corpus.IndexRows.Add(
                new IndexRow(
                    int.Parse(row.Groups["number"].Value),
                    row.Groups["file"].Value,
                    row.Groups["title"].Value.Trim(),
                    row.Groups["keywords"].Value.Trim(),
                    row.Groups["abstract"].Value.Trim(),
                    index + 1
                )
            );
        }
    }

    private void CheckStatic(AdrCorpus corpus)
    {
        this.CheckNumbering(corpus);
        foreach (var record in corpus.Records.Values.OrderBy(record => record.Number))
        {
            this.CheckRecord(corpus, record);
            if (this._noProposed && record.Status == "Proposed")
            {
                this.Error(
                    $"{DecisionsDirectory}/{record.FileName}: Status is 'Proposed'; a record reaches "
                        + "main only once it has been accepted, rejected, or withdrawn"
                );
            }
        }

        this.CheckIndex(corpus);
    }

    private void CheckNumbering(AdrCorpus corpus)
    {
        if (corpus.Records.Count == 0)
        {
            this.Error($"{DecisionsDirectory}: no ADR records found");
            return;
        }

        var max = corpus.Records.Keys.Max();
        for (var number = 1; number <= max; number++)
        {
            if (!corpus.Records.ContainsKey(number))
                this.Error(
                    $"{DecisionsDirectory}: ADR-{number} is missing; numbers are sequential and "
                        + "never reused"
                );
        }
    }

    private void CheckRecord(AdrCorpus corpus, AdrRecord record)
    {
        var path = $"{DecisionsDirectory}/{record.FileName}";

        if (record.IsOpen && record.Title is not null)
            this.CheckDerivedFileName(record, path);

        this.CheckSections(record, path);

        foreach (var relation in record.Relations)
            this.CheckRelation(corpus, record, relation, path);

        var hasSupersededBy = record.Relations.Any(relation => relation.Label == "Superseded by");
        if (hasSupersededBy && record.Status is not null and not "Superseded")
            this.Error(
                $"{path}: a record with 'Superseded by' must have Status 'Superseded', "
                    + $"found '{record.Status}'"
            );
        if (record.Status == "Superseded" && !hasSupersededBy)
            this.Error($"{path}: Status 'Superseded' requires a 'Superseded by' relation");

        if (record.IsDeclaringOnly)
        {
            foreach (
                var incoming in record.Relations.Where(relation =>
                    relation.Label is "Superseded by" or "Amended by"
                )
            )
                this.Error(
                    $"{path}:{incoming.LineNumber}: a {record.Status} record cannot carry "
                        + $"'{incoming.Label}'; a relation is established only against an accepted record"
                );
        }

        this.CheckLinks(corpus, record, path);

        if (!record.IsOpen)
            return;
        foreach (var reference in this.ForwardReferences(record).Distinct())
        {
            this.Error(
                $"{path}: body references ADR-{reference}, which is later than this record; "
                    + "bodies reference only earlier ADRs"
            );
        }
    }

    private void CheckDerivedFileName(AdrRecord record, string path)
    {
        var expected = $"{record.Number}_{Slugify(record.Title!)}.md";
        if (record.FileName != expected)
            this.Error($"{path}: filename should be derived from the title: {expected}");
    }

    private void CheckSections(AdrRecord record, string path)
    {
        var sections = record.Sections;
        var duplicates = sections
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var duplicate in duplicates)
            this.Error($"{path}: section '## {duplicate}' appears more than once");

        if (!record.IsOpen)
            return;

        foreach (var name in CoreSections)
        {
            if (!sections.Contains(name, StringComparer.Ordinal))
                this.Error($"{path}: core section '## {name}' is missing");
        }

        if (sections.Count > 0 && sections[0] != "Status")
            this.Error($"{path}: '## Status' must be the first section");
        if (sections.Count > 1 && sections[0] == "Status" && sections[1] != "Context")
            this.Error($"{path}: '## Context' must follow '## Status' directly");
        if (record.HasMaintenanceNote)
            this.Error(
                $"{path}: an open record carries no '## Maintenance Note'; the section records an "
                    + "editorial revision of a record that has left Proposed"
            );
        if (record.Protected.Replace("\u0000", "").Trim().Length > 0)
            this.Error($"{path}: nothing but the title line may precede '## Status'");

        var known = sections
            .Where(name => SectionOrder.Contains(name, StringComparer.Ordinal))
            .ToArray();
        for (var index = 1; index < known.Length; index++)
        {
            var previous = Array.IndexOf(SectionOrder, known[index - 1]);
            var current = Array.IndexOf(SectionOrder, known[index]);
            if (current < previous)
                this.Error(
                    $"{path}: section '## {known[index]}' must come before '## {known[index - 1]}' "
                        + "(README, \"Sections\")"
                );
        }
    }

    private void CheckRelation(
        AdrCorpus corpus,
        AdrRecord record,
        AdrRelation relation,
        string path
    )
    {
        var location = $"{path}:{relation.LineNumber}";
        var isAmendment = relation.Label is "Amends" or "Amended by";
        if (isAmendment && string.IsNullOrEmpty(relation.Scope))
            this.Error($"{location}: '{relation.Label}' requires a scope after ' — '");
        if (!isAmendment && !string.IsNullOrEmpty(relation.Scope))
            this.Error(
                $"{location}: '{relation.Label}' takes no scope; the whole decision is replaced"
            );

        if (relation.Target == record.Number)
        {
            this.Error($"{location}: a record cannot relate to itself");
            return;
        }

        if (!corpus.Records.TryGetValue(relation.Target, out var target))
        {
            this.Error($"{location}: relation target ADR-{relation.Target} does not exist");
            return;
        }

        if (relation.TargetFile != target.FileName)
            this.Error(
                $"{location}: relation link '{relation.TargetFile}' does not match "
                    + $"ADR-{relation.Target}'s file '{target.FileName}'"
            );

        var declares = relation.Label is "Supersedes" or "Amends";
        if (declares && relation.Target > record.Number)
            this.Error($"{location}: '{relation.Label}' must point at an earlier record");
        if (!declares && relation.Target < record.Number)
            this.Error($"{location}: '{relation.Label}' must point at a later record");

        var inverse = InverseRelation[relation.Label];
        var counterpart = target.Relations.FirstOrDefault(candidate =>
            candidate.Label == inverse && candidate.Target == record.Number
        );

        if (declares)
        {
            // The later record declares; the earlier record carries the counterpart only once the
            // later record is in force. A rejected or withdrawn proposal keeps its declaration and
            // the earlier record stays untouched.
            if (record.IsDeclaringOnly && counterpart is not null)
                this.Error(
                    $"{location}: ADR-{relation.Target} carries '{inverse}: ADR-{record.Number}' "
                        + $"although ADR-{record.Number} is {record.Status}; the earlier record "
                        + "changes only when the later one is accepted"
                );
            if (!record.IsDeclaringOnly && counterpart is null)
                this.Error(
                    $"{location}: ADR-{relation.Target} lacks the reciprocal "
                        + $"'{inverse}: ADR-{record.Number}'"
                );
        }
        else
        {
            if (counterpart is null)
                this.Error(
                    $"{location}: ADR-{relation.Target} lacks the reciprocal "
                        + $"'{inverse}: ADR-{record.Number}'"
                );
            if (target.IsDeclaringOnly)
                this.Error(
                    $"{location}: '{relation.Label}' points at ADR-{relation.Target}, which is "
                        + $"{target.Status}; only an accepted record establishes a relation"
                );
        }
    }

    private void CheckLinks(AdrCorpus corpus, AdrRecord record, string path)
    {
        var lines = record.Text.Split('\n');
        foreach (var (lineNumber, label) in MarkdownLinks.UndefinedReferences(lines))
            this.Error($"{path}:{lineNumber}: undefined reference label: [{label}]");
        foreach (var (lineNumber, message) in MarkdownLinks.FootnoteErrors(lines))
            this.Error($"{path}:{lineNumber}: {message}");

        foreach (var link in MarkdownLinks.Enumerate(lines))
        {
            var location = $"{path}:{link.LineNumber}";
            if (link.Kind == MarkdownLinkKind.Malformed)
            {
                this.Error(
                    $"{location}: link is outside the supported Markdown subset (see "
                        + $"CONTRIBUTING.md): {link.RawTarget}"
                );
                continue;
            }

            var resolution = MarkdownLinks.Resolve(
                this._root,
                path,
                link.RawTarget,
                this._anchorCache
            );
            if (resolution.Error is not null)
            {
                // A reference usage shares its definition's target; the definition reports it.
                if (link.Kind != MarkdownLinkKind.ReferenceUsage)
                    this.Error($"{location}: {resolution.Error}");
                continue;
            }

            var linkedNumber = this.LinkedRecordNumber(link.RawTarget);
            if (linkedNumber is null)
                continue;

            // Only displayed text is compared with the target; a definition's label is not text.
            var labelled =
                link.Kind == MarkdownLinkKind.Definition
                    ? Match.Empty
                    : LinkedAdrLabelRegex().Match(link.Text);
            if (labelled.Success && int.Parse(labelled.Groups["number"].Value) != linkedNumber)
                this.Error(
                    $"{location}: link text names ADR-{labelled.Groups["number"].Value} but the "
                        + $"target is ADR-{linkedNumber}'s file"
                );

            var fileName = Path.GetFileName(
                MarkdownLinks.DenotedPath(this.DecisionsFullPath, link.RawTarget)!
            );
            if (
                !corpus.Records.TryGetValue(linkedNumber.Value, out var linked)
                || linked.FileName != fileName
            )
                this.Error($"{location}: link to '{fileName}' does not resolve to an ADR record");
        }
    }

    // The ADR number a link denotes: only when the target denotes a record file directly in the
    // decisions directory, whatever the fragment, query, encoding, or link text.
    private int? LinkedRecordNumber(string rawTarget)
    {
        var denoted = MarkdownLinks.DenotedPath(this.DecisionsFullPath, rawTarget);
        if (denoted is null || Path.GetDirectoryName(denoted) != this.DecisionsFullPath)
            return null;

        var match = RecordFileRegex().Match(Path.GetFileName(denoted));
        return match.Success ? int.Parse(match.Groups["number"].Value) : null;
    }

    // Every occurrence of a reference from the body to a later record, by name (ADR-N) or through a
    // link usage that appears in the body, whichever way the link is written and wherever its
    // definition lives.
    private IEnumerable<int> ForwardReferences(AdrRecord record)
    {
        if (record.Body is null)
            return [];

        var lines = record.Text.Split('\n');
        var byName = AdrReferenceRegex()
            .Matches(record.Body)
            .Select(match => int.Parse(match.Groups["number"].Value));
        var byLink = MarkdownLinks
            .Enumerate(lines)
            .Where(link =>
                link.Kind is MarkdownLinkKind.Inline or MarkdownLinkKind.ReferenceUsage
                && link.LineNumber - 1 >= record.ContextStart
            )
            .Select(link => this.LinkedRecordNumber(link.RawTarget))
            .Where(number => number is not null)
            .Select(number => number!.Value);
        return byName
            .Concat(byLink)
            .Where(reference => reference > record.Number)
            .Order()
            .ToArray();
    }

    private void CheckIndex(AdrCorpus corpus)
    {
        var path = $"{DecisionsDirectory}/{IndexFile}";
        if (corpus.IndexText is null)
        {
            this.Error($"{path}: index does not exist");
            return;
        }

        var seen = new Dictionary<int, IndexRow>();
        var previous = 0;
        foreach (var row in corpus.IndexRows)
        {
            var location = $"{path}:{row.LineNumber}";
            if (!seen.TryAdd(row.Number, row))
                this.Error($"{location}: duplicate index row for ADR-{row.Number}");
            if (row.Number < previous)
                this.Error($"{location}: index rows must be in ascending ADR order");
            previous = row.Number;

            if (!corpus.Records.TryGetValue(row.Number, out var record))
            {
                this.Error($"{location}: index row for ADR-{row.Number}, which does not exist");
                continue;
            }

            if (row.File != record.FileName)
                this.Error(
                    $"{location}: index link '{row.File}' does not match '{record.FileName}'"
                );

            var unannotated = IndexAnnotationRegex().Replace(row.TitleCell, "").Trim();
            var struckMatch = StruckTitleRegex().Match(unannotated);
            var struck = struckMatch.Success;
            if (!struck && unannotated.Contains("~~", StringComparison.Ordinal))
                this.Error($"{location}: strike-through must wrap the whole title as ~~Title~~");
            var title = struck ? struckMatch.Groups["title"].Value.Trim() : unannotated;
            if (record.Title is not null && title != record.Title)
                this.Error(
                    $"{location}: index title '{title}' does not match the record title "
                        + $"'{record.Title}'"
                );

            var retired = record.Status is "Superseded" or "Deprecated";
            if (struck && !retired)
                this.Error(
                    $"{location}: title is struck through but Status is '{record.Status}'; "
                        + "strike through only Superseded and Deprecated records"
                );
            if (!struck && retired)
                this.Error(
                    $"{location}: Status is '{record.Status}' but the title is not struck through"
                );

            if (row.Abstract.Length == 0)
                this.Error($"{location}: abstract is empty");
        }

        foreach (var record in corpus.Records.Values)
        {
            if (!seen.ContainsKey(record.Number))
                this.Error($"{path}: ADR-{record.Number} has no index row");
        }
    }

    private void CheckDiff(AdrCorpus baseCorpus, AdrCorpus corpus)
    {
        foreach (var baseRecord in baseCorpus.Records.Values.OrderBy(record => record.Number))
        {
            if (!corpus.Records.TryGetValue(baseRecord.Number, out var record))
            {
                this.Error(
                    $"{DecisionsDirectory}/{baseRecord.FileName}: ADR-{baseRecord.Number} was "
                        + "removed; records are never deleted"
                );
                continue;
            }

            var path = $"{DecisionsDirectory}/{record.FileName}";
            if (
                baseRecord.Status is not null
                && record.Status is not null
                && baseRecord.Status != record.Status
                && !AllowedTransitions[baseRecord.Status]
                    .Contains(record.Status, StringComparer.Ordinal)
            )
                this.Error(
                    $"{path}: Status transition '{baseRecord.Status}' -> '{record.Status}' is not "
                        + "allowed"
                );

            // Fail-closed: a base whose Status cannot be read cannot be validated at all.
            if (baseRecord.Status is null)
                this.Error(
                    $"{path}: Status at the base cannot be read, so the change rules cannot be "
                        + "applied to this record"
                );

            var changes = new List<string>();
            if (baseRecord.FileName != record.FileName)
                changes.Add("filename");

            if (baseRecord.Title != record.Title)
                changes.Add("title");
            if (
                baseRecord.Body is not null
                && record.Body is not null
                && baseRecord.Body != record.Body
            )
                changes.Add("body");

            // Fail-closed: only a base record positively known to be Proposed is editable.
            var frozen = baseRecord.Status != "Proposed";
            var bodyChanged = changes.Contains("body");
            // The note is compared only for a record that has left Proposed; an open record
            // carries none, which the writing rules report.
            var revised =
                frozen && this.CheckMaintenanceNoteChange(baseRecord, record, path, bodyChanged);
            var identity = changes.Where(change => change != "body").ToArray();
            if (frozen && identity.Length > 0)
                this.Error(
                    $"{path}: {string.Join(", ", identity)} changed although Status was "
                        + $"'{baseRecord.Status ?? "not readable"}' at the base; the identity of a "
                        + "record that has left Proposed is fixed"
                );
            if (frozen && bodyChanged && !revised)
                this.Error(
                    $"{path}: body changed although Status was "
                        + $"'{baseRecord.Status ?? "not readable"}' at the base and no "
                        + "'## Maintenance Note' entry was added; a body that has left Proposed "
                        + "changes only through a recorded editorial revision, and a decision that "
                        + "no longer holds is superseded or amended by a new ADR"
                );
            if (frozen && baseRecord.Protected != record.Protected)
                this.Error(
                    $"{path}: historical text outside the body changed although Status was "
                        + $"'{baseRecord.Status ?? "not readable"}' at the base; it is as immutable "
                        + "as the body"
                );

            // An editorial revision keeps the template's sections and adds no reference to a later
            // record (README, "Editorial revision"; "References inside a body"); what the body
            // already held stays as it was written.
            if (frozen && bodyChanged)
            {
                if (
                    !TemplateBodySections(baseRecord)
                        .SequenceEqual(TemplateBodySections(record), StringComparer.Ordinal)
                )
                    this.Error(
                        $"{path}: sections named by the template changed although Status was "
                            + $"'{baseRecord.Status ?? "not readable"}' at the base; an editorial "
                            + "revision keeps each of them, in order, and adds none"
                    );

                // Occurrences are counted, so a revision that keeps an earlier reference passes while
                // one that adds another mention of the same later record does not.
                var baseCounts = this.ForwardReferences(baseRecord)
                    .CountBy(reference => reference)
                    .ToDictionary();
                foreach (var (reference, count) in this.ForwardReferences(record).CountBy(r => r))
                {
                    if (count > baseCounts.GetValueOrDefault(reference))
                        this.Error(
                            $"{path}: body newly references ADR-{reference}, which is later than "
                                + "this record; bodies reference only earlier ADRs"
                        );
                }
            }

            this.CheckRelationChanges(baseCorpus, corpus, baseRecord, record, path);
        }

        foreach (var record in corpus.Records.Values)
        {
            if (baseCorpus.Records.ContainsKey(record.Number))
                continue;
            var path = $"{DecisionsDirectory}/{record.FileName}";
            // A record that is new in a change starts as Proposed or, with the review performed,
            // Accepted; it may also arrive already Rejected or Withdrawn, because main carries no
            // Proposed record and a proposal settled before it reaches main lands in its outcome
            // state (README, "Transitions"). It never appears first in a state that requires a
            // prior accepted life.
            if (
                record.Status
                is not null
                    and not ("Proposed" or "Accepted" or "Rejected" or "Withdrawn")
            )
                this.Error(
                    $"{path}: a new record is Proposed, Accepted, Rejected, or Withdrawn, never "
                        + $"'{record.Status}' from the "
                        + "start"
                );
            this.CheckRelationChanges(baseCorpus, corpus, null, record, path);
        }
    }

    // Relation entries follow the lifecycle (README, "Status and relation metadata"): a Proposed
    // record edits its declarations freely; a record that has left Proposed keeps every entry it
    // had, and gains entries only when a relation is established in this change: an outgoing
    // "Supersedes"/"Amends" when the record itself goes in force now, an incoming "Superseded
    // by"/"Amended by" when the later record goes in force now. An entry written in error is not
    // repaired: like the body, it is what the record says.
    private void CheckRelationChanges(
        AdrCorpus baseCorpus,
        AdrCorpus corpus,
        AdrRecord? baseRecord,
        AdrRecord record,
        string path
    )
    {
        var current = record.Relations;
        var goesInForceNow =
            record.IsInForce && (baseRecord is null || baseRecord.Status == "Proposed");

        // Target state at establishment: an amendment needs an Accepted target, a supersession
        // one that is Superseded now and was Accepted or Deprecated before (or new in this change).
        if (goesInForceNow)
        {
            foreach (
                var relation in current.Where(relation =>
                    relation.Label is "Supersedes" or "Amends"
                )
            )
            {
                if (!corpus.Records.TryGetValue(relation.Target, out var target))
                    continue;

                var baseTarget = baseCorpus.Records.GetValueOrDefault(relation.Target);
                var ok =
                    relation.Label == "Amends"
                        ? target.Status == "Accepted"
                            && (baseTarget is null || baseTarget.Status is "Accepted" or "Proposed")
                        : target.Status == "Superseded"
                            && (
                                baseTarget is null
                                || baseTarget.Status is "Accepted" or "Deprecated"
                            );
                if (!ok)
                    this.Error(
                        $"{path}:{relation.LineNumber}: '{relation.Label}' establishes a relation to "
                            + $"ADR-{relation.Target}, which is {target.Status ?? "not readable"} now"
                            + (
                                baseTarget is null
                                    ? ""
                                    : $" and was {baseTarget.Status ?? "not readable"} at the base"
                            )
                            + (
                                relation.Label == "Amends"
                                    ? "; only an Accepted record can be amended"
                                    : "; only an Accepted or Deprecated record can be superseded, and it becomes Superseded"
                            )
                    );
            }
        }

        // Editable declarations: a record still Proposed, or one that was Proposed at the base and
        // leaves it now, decides its final declarations in this change.
        if (baseRecord is null || baseRecord.Status == "Proposed")
            return;

        // The relation set a frozen record must carry now: every base entry is kept, and an entry
        // is added only when a later record establishes it in this change.
        var baseKeys = baseRecord.Relations.Select(relation => relation.Key).ToHashSet();
        var currentKeys = current.Select(relation => relation.Key).ToHashSet();
        var baseLabel = baseRecord.Status ?? "not readable";

        foreach (var key in baseKeys.Except(currentKeys))
        {
            this.Error(
                $"{path}: relation '{key.Label}: ADR-{key.Target}' was removed or changed although "
                    + $"Status was '{baseLabel}' at the base; relation entries are kept, and "
                    + "an entry written in error is not repaired"
            );
        }

        foreach (var relation in current.Where(relation => !baseKeys.Contains(relation.Key)))
        {
            var incoming = relation.Label is "Superseded by" or "Amended by";
            var establishedByLater =
                incoming
                && corpus.Records.TryGetValue(relation.Target, out var later)
                && later.IsInForce
                && (
                    !baseCorpus.Records.TryGetValue(relation.Target, out var laterAtBase)
                    || laterAtBase.Status == "Proposed"
                );
            if (establishedByLater)
                continue;

            this.Error(
                $"{path}:{relation.LineNumber}: relation '{relation.Label}: ADR-{relation.Target}' was "
                    + $"added although Status was '{baseLabel}' at the base; "
                    + (
                        incoming
                            ? "an incoming relation is added only when the later record is accepted in the same change"
                            : "a declaration is added only while the record is Proposed"
                    )
            );
        }
    }

    // The body sections the template names, in file order (README, "Sections").
    private static IEnumerable<string> TemplateBodySections(AdrRecord record) =>
        record.Sections.Where(name =>
            SectionOrder.Contains(name, StringComparer.Ordinal)
            && name is not ("Status" or "Maintenance Note")
        );

    private static string Slugify(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var character in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(character);
            else if (char.IsWhiteSpace(character) || character is '-' or '—')
                builder.Append('-');
        }

        return MultipleHyphenRegex().Replace(builder.ToString(), "-").Trim('-');
    }

    private async Task<ProcessResult> RunGitAsync(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = this._root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    [GeneratedRegex(@"^(?<number>[1-9][0-9]*)_[a-z0-9]+(?:-[a-z0-9]+)*\.md$")]
    private static partial Regex RecordFileRegex();

    [GeneratedRegex(@"^# ADR-(?<number>[0-9]+): (?<title>.+)$")]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^- \S")]
    private static partial Regex MaintenanceEntryRegex();

    [GeneratedRegex(@"^## (?<name>[^#].*)$")]
    private static partial Regex SectionRegex();

    [GeneratedRegex(
        @"^- (?<label>Supersedes|Superseded by|Amends|Amended by): \[ADR-(?<number>[0-9]+)\]\((?<file>[^)]+)\)(?: — (?<scope>.+))?$"
    )]
    private static partial Regex RelationRegex();

    [GeneratedRegex(
        @"^\| \[ADR-(?<number>[0-9]+)\]\((?<file>[^)]+)\) \| (?<title>.*?) \| (?<keywords>.*?) \| (?<abstract>.*?) \|$"
    )]
    private static partial Regex IndexRowRegex();

    [GeneratedRegex(@"\s*\*\([^)]*\)\*\s*$")]
    private static partial Regex IndexAnnotationRegex();

    [GeneratedRegex(@"^~~(?<title>[^~]+)~~$")]
    private static partial Regex StruckTitleRegex();

    [GeneratedRegex(@"^ADR-(?<number>[0-9]+)$")]
    private static partial Regex LinkedAdrLabelRegex();

    [GeneratedRegex(@"\bADR-(?<number>[0-9]+)\b")]
    private static partial Regex AdrReferenceRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultipleHyphenRegex();
}

// Runs the change-rule scenarios against small fixture corpora in temporary Git repositories.
// Each scenario mutates a fresh checkout of a fixture and checks it against the fixture commit.
// A scenario passes only when every expected fragment is reported and every reported error is
// expected, so that an unrelated error can never make a scenario pass.
internal static class AdrSelfTest
{
    private const string Decisions = "docs/decisions";
    private const string One = "1_fixture-decision-one.md";
    private const string Two = "2_fixture-decision-two.md";
    private const string Three = "3_fixture-decision-three.md";
    private const string Four = "4_fixture-decision-four.md";
    private const string Five = "5_fixture-decision-five.md";
    private const string Six = "6_fixture-decision-six.md";

    private enum Fixture
    {
        Main,
        Unreadable,
    }

    // WithBase false checks the working tree alone, as a run without --base does.
    private sealed record Scenario(
        string Name,
        Action<string> Mutate,
        string[] Expected,
        Fixture Fixture = Fixture.Main,
        bool NoProposed = false,
        bool WithBase = true
    );

    public static async Task<int> RunAsync()
    {
        var failures = 0;
        foreach (var fixture in new[] { Fixture.Main, Fixture.Unreadable })
        {
            var temp = Path.Combine(Path.GetTempPath(), $"adr-check-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temp);
            try
            {
                await CreateFixtureAsync(temp, fixture);
                foreach (var scenario in Scenarios().Where(scenario => scenario.Fixture == fixture))
                {
                    await GitAsync(temp, "reset", "--hard", "--quiet", "HEAD");
                    await GitAsync(temp, "clean", "-fdq");
                    scenario.Mutate(temp);
                    var checker = new AdrChecker(temp, scenario.NoProposed);
                    var report = await checker.CheckAsync(scenario.WithBase ? "HEAD" : null);
                    var expected = scenario.Expected;
                    var passed =
                        expected.All(fragment =>
                            report.Errors.Any(error =>
                                error.Contains(fragment, StringComparison.Ordinal)
                            )
                        )
                        && report.Errors.All(error =>
                            expected.Any(fragment =>
                                error.Contains(fragment, StringComparison.Ordinal)
                            )
                        );
                    Console.WriteLine($"{(passed ? "pass" : "FAIL")}: {scenario.Name}");
                    if (!passed)
                    {
                        failures++;
                        Console.WriteLine(
                            $"  expected {(expected.Length == 0 ? "no errors" : string.Join(" | ", expected))}"
                        );
                        foreach (var error in report.Errors)
                            Console.WriteLine($"  got: {error}");
                    }
                }
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        Console.WriteLine(
            failures == 0 ? "Self-test passed." : $"Self-test failed: {failures} scenario(s)."
        );
        return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<Scenario> Scenarios()
    {
        yield return new("unchanged fixture passes", _ => { }, []);

        // Lifecycle metadata
        yield return new(
            "status-only change on an accepted record passes and its established amendment stays",
            root =>
            {
                Replace(root, One, "Accepted\n", "Deprecated\n");
                Replace(
                    root,
                    "index.md",
                    "| Fixture Decision One |",
                    "| ~~Fixture Decision One~~ *(Deprecated)* |"
                );
            },
            []
        );
        yield return new(
            "moving an accepted record back to proposed fails",
            root => Replace(root, One, "Accepted\n", "Proposed\n"),
            [
                "Status transition 'Accepted' -> 'Proposed' is not allowed",
                "a Proposed record cannot carry 'Amended by'",
            ]
        );
        yield return new(
            "deprecated record without a struck-through index title fails",
            root =>
                Replace(
                    root,
                    "index.md",
                    "| ~~Fixture Decision Two~~ *(Deprecated)* |",
                    "| Fixture Decision Two *(Deprecated)* |"
                ),
            ["Status is 'Deprecated' but the title is not struck through"]
        );
        yield return new(
            "struck-through index title on an accepted record fails",
            root =>
                Replace(
                    root,
                    "index.md",
                    "| Fixture Decision One |",
                    "| ~~Fixture Decision One~~ |"
                ),
            ["title is struck through but Status is 'Accepted'"]
        );
        yield return new(
            "malformed strike-through fails",
            root =>
                Replace(
                    root,
                    "index.md",
                    "| ~~Fixture Decision Two~~ *(Deprecated)* |",
                    "| ~~Fixture Decision Two *(Deprecated)* |"
                ),
            [
                "strike-through must wrap the whole title",
                "does not match the record title",
                "not struck through",
            ]
        );
        yield return new(
            "index title that does not match the record fails",
            root =>
                Replace(root, "index.md", "| Fixture Decision Four |", "| Fixture Decision 4 |"),
            ["index title 'Fixture Decision 4' does not match"]
        );

        // Relation state matrix: declaration
        yield return new(
            "proposed record declaring a supersession leaves the earlier record untouched",
            root => Declare(root, Four, "Proposed", $"Supersedes: [ADR-1]({One})"),
            []
        );
        yield return new(
            "proposed record may change its declarations",
            root =>
                Replace(
                    root,
                    Five,
                    $"Amends: [ADR-1]({One}) — draft scope",
                    $"Amends: [ADR-2]({Two}) — another scope"
                ),
            []
        );
        yield return new(
            "rejected record keeps its declaration and the earlier record untouched",
            root => Declare(root, Four, "Rejected", $"Supersedes: [ADR-1]({One})"),
            []
        );
        yield return new(
            "withdrawn record keeps its declaration and the earlier record untouched",
            root => Declare(root, Four, "Withdrawn", $"Amends: [ADR-1]({One}) — anything"),
            []
        );
        yield return new(
            "earlier record updated while the later record is still proposed fails",
            root =>
            {
                Declare(root, Four, "Proposed", $"Supersedes: [ADR-1]({One})");
                Supersede(root, One, "Accepted");
            },
            [
                "although ADR-4 is Proposed",
                "points at ADR-4, which is Proposed",
                "relation 'Superseded by: ADR-4' was added",
            ]
        );
        yield return new(
            "earlier record updated for a rejected proposal fails",
            root =>
            {
                Declare(root, Four, "Rejected", $"Supersedes: [ADR-1]({One})");
                Supersede(root, One, "Accepted");
            },
            [
                "although ADR-4 is Rejected",
                "points at ADR-4, which is Rejected",
                "relation 'Superseded by: ADR-4' was added",
            ]
        );

        // Relation state matrix: establishment
        yield return new(
            "acceptance with reciprocal supersession passes and Status may reference a later record",
            root =>
            {
                Declare(root, Four, "Accepted", $"Supersedes: [ADR-1]({One})");
                Supersede(root, One, "Accepted");
            },
            []
        );
        yield return new(
            "deprecated record superseded by an accepted record passes",
            root =>
            {
                Declare(root, Four, "Accepted", $"Supersedes: [ADR-2]({Two})");
                Replace(
                    root,
                    Two,
                    "Deprecated\n",
                    $"Superseded\n\n- Superseded by: [ADR-4]({Four})\n"
                );
                Replace(root, "index.md", "*(Deprecated)*", "*(Superseded by ADR-4)*");
            },
            []
        );
        yield return new(
            "superseding a rejected record fails",
            root =>
            {
                Declare(root, Four, "Accepted", $"Supersedes: [ADR-3]({Three})");
                Replace(
                    root,
                    Three,
                    "Rejected\n",
                    $"Superseded\n\n- Superseded by: [ADR-4]({Four})\n"
                );
                Replace(
                    root,
                    "index.md",
                    "| Fixture Decision Three (Draft) |",
                    "| ~~Fixture Decision Three (Draft)~~ *(Superseded by ADR-4)* |"
                );
            },
            [
                "Status transition 'Rejected' -> 'Superseded' is not allowed",
                "was Rejected at the base",
                "ADR-1 lacks the reciprocal 'Superseded by: ADR-3'",
            ]
        );
        yield return new(
            "acceptance without the reciprocal relation fails",
            root => Declare(root, Four, "Accepted", $"Supersedes: [ADR-1]({One})"),
            ["lacks the reciprocal 'Superseded by: ADR-4'", "which is Accepted now"]
        );
        yield return new(
            "amendment without a scope fails",
            root => Declare(root, Four, "Proposed", $"Amends: [ADR-1]({One})"),
            ["requires a scope"]
        );
        yield return new(
            "new amendment of an accepted record with reciprocal scope passes",
            root =>
            {
                Declare(root, Four, "Accepted", $"Amends: [ADR-1]({One}) — the decision wording");
                Replace(
                    root,
                    One,
                    "Accepted\n",
                    $"Accepted\n\n- Amended by: [ADR-4]({Four}) — the decision wording\n"
                );
            },
            []
        );
        yield return new(
            "new amendment of a deprecated record fails",
            root =>
            {
                Declare(root, Four, "Accepted", $"Amends: [ADR-2]({Two}) — anything");
                Replace(
                    root,
                    Two,
                    "Deprecated\n",
                    $"Deprecated\n\n- Amended by: [ADR-4]({Four}) — anything\n"
                );
            },
            [
                "which is Deprecated now and was Deprecated at the base; only an Accepted record can be amended",
            ]
        );
        yield return new(
            "new amendment of a rejected record fails",
            root =>
            {
                Declare(root, Four, "Accepted", $"Amends: [ADR-3]({Three}) — anything");
                Replace(
                    root,
                    Three,
                    "Rejected\n",
                    $"Rejected\n\n- Amended by: [ADR-4]({Four}) — anything\n"
                );
            },
            [
                "which is Rejected now and was Rejected at the base; only an Accepted record can be amended",
                "a Rejected record cannot carry 'Amended by'",
            ]
        );
        yield return new(
            "amending a record that is new and rejected in the same change fails",
            root =>
            {
                Write(
                    root,
                    Six,
                    Record(
                        6,
                        "Fixture Decision Six",
                        $"Rejected\n\n- Amended by: [ADR-7](7_fixture-decision-seven.md) — anything",
                        "The sixth forces."
                    )
                );
                Write(
                    root,
                    "7_fixture-decision-seven.md",
                    Record(
                        7,
                        "Fixture Decision Seven",
                        $"Accepted\n\n- Amends: [ADR-6]({Six}) — anything",
                        "The seventh forces."
                    )
                );
                Append(
                    root,
                    "index.md",
                    $"| [ADR-6]({Six}) | Fixture Decision Six | six | Decides six because six. |\n| [ADR-7](7_fixture-decision-seven.md) | Fixture Decision Seven | seven | Decides seven because seven. |\n"
                );
            },
            [
                "which is Rejected now; only an Accepted record can be amended",
                "a Rejected record cannot carry 'Amended by'",
            ]
        );
        yield return new(
            "amending a record that is new and accepted in the same change passes",
            root =>
            {
                Write(
                    root,
                    Six,
                    Record(
                        6,
                        "Fixture Decision Six",
                        $"Accepted\n\n- Amends: [ADR-5]({Five}) — anything",
                        "The sixth forces."
                    )
                );
                Replace(
                    root,
                    Five,
                    "Proposed\n\n- Amends",
                    $"Accepted\n\n- Amended by: [ADR-6]({Six}) — anything\n- Amends"
                );
                Replace(
                    root,
                    One,
                    "Accepted\n",
                    $"Accepted\n\n- Amended by: [ADR-5]({Five}) — draft scope\n"
                );
                Append(
                    root,
                    "index.md",
                    $"| [ADR-6]({Six}) | Fixture Decision Six | six | Decides six because six. |\n"
                );
            },
            []
        );

        // Relation state matrix: retention and post-hoc changes
        yield return new(
            "removing an established amendment from both records fails",
            root =>
            {
                Replace(root, One, $"\n\n- Amended by: [ADR-2]({Two}) — the first forces", "");
                Replace(root, Two, $"\n\n- Amends: [ADR-1]({One}) — the first forces", "");
            },
            [
                "relation 'Amended by: ADR-2' was removed or changed",
                "relation 'Amends: ADR-1' was removed or changed",
            ]
        );
        yield return new(
            "changing the scope of an established amendment fails",
            root =>
            {
                Replace(root, One, "— the first forces", "— everything");
                Replace(root, Two, "— the first forces", "— everything");
            },
            [
                "relation 'Amended by: ADR-2' was removed or changed",
                "relation 'Amends: ADR-1' was removed or changed",
                "relation 'Amended by: ADR-2' was added",
                "relation 'Amends: ADR-1' was added",
            ]
        );
        yield return new(
            "removing a declaration from a rejected record fails",
            root => Replace(root, Three, $"\n\n- Supersedes: [ADR-1]({One})", ""),
            ["relation 'Supersedes: ADR-1' was removed or changed although Status was 'Rejected'"]
        );
        yield return new(
            "adding a declaration to an already accepted record fails",
            root =>
            {
                Replace(root, Two, "Deprecated\n", $"Deprecated\n\n- Supersedes: [ADR-1]({One})\n");
                Supersede(root, One, "Accepted", by: 2);
            },
            [
                "relation 'Supersedes: ADR-1' was added although Status was 'Deprecated'",
                "relation 'Superseded by: ADR-2' was added although Status was 'Accepted'",
            ]
        );
        yield return new(
            "adding an amendment between two already accepted records fails",
            root =>
            {
                Replace(root, Two, "Deprecated\n", "Accepted\n");
                Replace(
                    root,
                    "index.md",
                    "| ~~Fixture Decision Two~~ *(Deprecated)* |",
                    "| Fixture Decision Two |"
                );
            },
            ["Status transition 'Deprecated' -> 'Accepted' is not allowed"]
        );
        yield return new(
            "a new record that starts as deprecated fails",
            root =>
            {
                Write(
                    root,
                    Six,
                    Record(6, "Fixture Decision Six", "Deprecated", "The sixth forces.")
                );
                Append(
                    root,
                    "index.md",
                    $"| [ADR-6]({Six}) | ~~Fixture Decision Six~~ *(Deprecated)* | six | Decides six because six. |\n"
                );
            },
            [
                "a new record is Proposed, Accepted, Rejected, or Withdrawn, never 'Deprecated' "
                    + "from the start",
            ]
        );
        yield return new(
            "a Proposed record on main fails under --no-proposed",
            _ => { },
            [
                "Status is 'Proposed'; a record reaches main only once it has been accepted, "
                    + "rejected, or withdrawn",
            ],
            NoProposed: true
        );
        yield return new(
            "a settled corpus passes under --no-proposed",
            root =>
            {
                Replace(root, Four, "## Status\n\nProposed\n", "## Status\n\nWithdrawn\n");
                Replace(root, Five, "## Status\n\nProposed\n", "## Status\n\nRejected\n");
            },
            [],
            NoProposed: true
        );
        yield return new(
            "accepted body edit without a maintenance note fails",
            root => Replace(root, One, "The first forces.", "The first forces, reworded."),
            ["body changed although Status was 'Accepted'", "no '## Maintenance Note' entry"]
        );
        yield return new(
            "accepted body edit recorded as an editorial revision passes",
            root =>
            {
                Replace(root, One, "The first forces.", "The first forces, reworded.");
                Replace(root, One, "## Context\n", Note("an API inventory") + "## Context\n");
            },
            []
        );
        yield return new(
            "maintenance note entry without a body change fails",
            root => Replace(root, One, "## Context\n", Note("an API inventory") + "## Context\n"),
            ["entry was added although the body did not change"]
        );
        yield return new(
            "removing a maintenance note entry fails",
            root => Replace(root, Two, Note("a wire format"), ""),
            ["entries were removed, reworded, or reordered"]
        );
        yield return new(
            "rewording a maintenance note entry fails",
            root => Replace(root, Two, "a wire format", "a field inventory"),
            ["entries were removed, reworded, or reordered"]
        );
        yield return new(
            "maintenance note outside its place fails",
            root =>
                Replace(
                    root,
                    Four,
                    "## Consequences\n",
                    "## Consequences\n\n- Neutral: none.\n\n"
                        + Note("an API inventory").TrimEnd()
                        + "\n"
                ),
            [
                "'## Maintenance Note' must sit immediately after '## Status'",
                "'## Maintenance Note' must come before '## Consequences'",
                "an open record carries no '## Maintenance Note'",
            ]
        );
        yield return new(
            "open record with a maintenance note fails",
            root => Replace(root, Four, "## Context\n", Note("an API inventory") + "## Context\n"),
            [
                "an open record carries no '## Maintenance Note'",
                "'## Context' must follow '## Status' directly",
            ]
        );
        yield return new(
            "status word followed by text in the same paragraph fails",
            root =>
                Replace(
                    root,
                    Four,
                    "## Status\n\nProposed\n",
                    "## Status\n\nProposed\nStill under discussion.\n"
                ),
            ["the first Status paragraph is the status word alone"]
        );
        yield return new(
            "relation entry after prose in the Status section fails",
            root =>
                Replace(
                    root,
                    Four,
                    "## Status\n\nProposed\n",
                    $"## Status\n\nProposed\n\nDrafted for review.\n\n- Supersedes: [ADR-1]({One})\n"
                ),
            ["a relation entry follows prose"]
        );
        yield return new(
            "empty maintenance note fails",
            root => Replace(root, One, "## Context\n", "## Maintenance Note\n\n## Context\n"),
            ["'## Maintenance Note' is present but empty"]
        );
        yield return new(
            "non-entry text in a maintenance note fails",
            root =>
                Replace(
                    root,
                    One,
                    "## Context\n",
                    "## Maintenance Note\n\nTidied up.\n\n## Context\n"
                ),
            ["holds only entries, and an entry is a bullet"]
        );
        yield return new(
            "historical text outside the body of a closed record changed fails",
            root => Replace(root, Three, "An old abstract.", "An old abstract, reworded."),
            ["historical text outside the body changed although Status was 'Rejected'"]
        );
        yield return new(
            "an added paragraph break in protected non-body text is a change",
            root =>
                Replace(
                    root,
                    Three,
                    "## Abstract\n\nAn old abstract.\n",
                    "## Abstract\n\n\nAn old abstract.\n"
                ),
            ["historical text outside the body changed although Status was 'Rejected'"]
        );
        yield return new(
            "edge blank lines of a protected region are not a change",
            root =>
                Replace(
                    root,
                    Three,
                    "## Abstract\n\nAn old abstract.\n\n## Context",
                    "\n## Abstract\n\nAn old abstract.\n\n\n## Context"
                ),
            []
        );
        yield return new(
            "editorial revision of a closed record keeps its earlier reference to a later record",
            root =>
            {
                Replace(
                    root,
                    Three,
                    "The third forces, as ADR-5 later showed.",
                    "The third forces, reworded, as ADR-5 later showed."
                );
                Replace(root, Three, "## Abstract\n", Note("a field inventory") + "## Abstract\n");
            },
            []
        );
        yield return new(
            "editorial revision that adds another mention of an already referenced later record fails",
            root =>
            {
                Replace(
                    root,
                    Three,
                    "The third forces, as ADR-5 later showed.",
                    "The third forces, as ADR-5 later showed and as ADR-5 confirmed."
                );
                Replace(root, Three, "## Abstract\n", Note("a field inventory") + "## Abstract\n");
            },
            ["body newly references ADR-5"]
        );
        yield return new(
            "editorial revision that adds a reference to a later record fails",
            root =>
            {
                Replace(root, One, "The first forces.", "The first forces, see ADR-4.");
                Replace(root, One, "## Context\n", Note("an API inventory") + "## Context\n");
            },
            ["body newly references ADR-4"]
        );
        yield return new(
            "editorial revision that removes a section the template names fails",
            root =>
            {
                Replace(root, One, "## Rationale\n\nBecause one.\n\n", "");
                Replace(root, One, "## Context\n", Note("an API inventory") + "## Context\n");
            },
            ["sections named by the template changed although Status was 'Accepted'"]
        );
        yield return new(
            "editorial revision that adds a section the template names fails",
            root =>
            {
                Replace(
                    root,
                    One,
                    "## Decision\n",
                    "## Decision Drivers\n\nSpeed.\n\n## Decision\n"
                );
                Replace(root, One, "## Context\n", Note("an API inventory") + "## Context\n");
            },
            ["sections named by the template changed although Status was 'Accepted'"]
        );
        yield return new(
            "editorial revision may remove a section the template does not name",
            root =>
            {
                Replace(root, One, "\n## Wire Format\n\nBytes.\n", "");
                Replace(root, One, "## Context\n", Note("a wire format") + "## Context\n");
            },
            []
        );
        yield return new(
            "proposed body edit passes",
            root => Replace(root, Four, "The fourth forces.", "The fourth forces, reworded."),
            []
        );
        yield return new(
            "renaming an accepted record fails",
            root => Rename(root, One, "1_fixture-decision-uno.md", "Fixture Decision Uno"),
            ["filename, title changed although Status was 'Accepted'"]
        );
        yield return new(
            "open record whose filename is not derived from its title fails",
            root =>
            {
                Replace(
                    root,
                    Four,
                    "# ADR-4: Fixture Decision Four",
                    "# ADR-4: Fixture Decision Quatre"
                );
                Replace(
                    root,
                    "index.md",
                    "| Fixture Decision Four |",
                    "| Fixture Decision Quatre |"
                );
            },
            ["filename should be derived from the title: 4_fixture-decision-quatre.md"]
        );
        yield return new(
            "removing a record fails",
            root =>
            {
                File.Delete(Path.Combine(root, Decisions, Four));
                Replace(
                    root,
                    "index.md",
                    $"| [ADR-4]({Four}) | Fixture Decision Four | four | Decides four because four. |\n",
                    ""
                );
            },
            ["ADR-4 was removed", "ADR-4 is missing"]
        );
        yield return new(
            "file that is not a valid record name fails",
            root =>
                File.WriteAllText(Path.Combine(root, Decisions, "6_Bad Name.md"), "# ADR-6: Bad\n"),
            ["is not a valid ADR record filename"]
        );
        yield return new(
            "file with a non-kebab-case name fails",
            root =>
                File.WriteAllText(
                    Path.Combine(root, Decisions, "6_-bad--name-.md"),
                    "# ADR-6: Bad\n"
                ),
            ["is not a valid ADR record filename"]
        );

        // Writing rules reach open records only (README, "How the rules apply")
        yield return new(
            "missing core section in an open record fails",
            root => Replace(root, Four, "## Rationale\n\nBecause four.\n\n", ""),
            ["core section '## Rationale' is missing"]
        );
        yield return new(
            "accepting a proposed record still holds it to the writing rules",
            root =>
            {
                Replace(root, Four, "Proposed\n", "Accepted\n");
                Replace(root, Four, "## Rationale\n\nBecause four.\n\n", "");
            },
            ["core section '## Rationale' is missing"]
        );
        yield return new(
            "record that arrives accepted is held to the writing rules",
            root =>
            {
                Write(
                    root,
                    Six,
                    Record(6, "Fixture Decision Six", "Accepted", "The sixth forces.")
                        .Replace("## Rationale\n\nBecause six.\n\n", "", StringComparison.Ordinal)
                );
                Append(
                    root,
                    "index.md",
                    $"| [ADR-6]({Six}) | Fixture Decision Six | six | Decides six because six. |\n"
                );
            },
            ["core section '## Rationale' is missing"]
        );
        yield return new(
            "without a base, a closed record is not held to the writing rules",
            _ => { },
            [],
            WithBase: false
        );
        yield return new(
            "without a base, a proposed record is held to the writing rules",
            root => Replace(root, Four, "## Rationale\n\nBecause four.\n\n", ""),
            ["core section '## Rationale' is missing"],
            WithBase: false
        );
        yield return new(
            "section the template does not name passes after Context",
            root =>
                Replace(
                    root,
                    Four,
                    "## Consequences\n",
                    "## Migration Notes\n\nNone.\n\n## Consequences\n"
                ),
            []
        );
        yield return new(
            "section between Status and Context fails",
            root => Replace(root, Four, "## Context\n", "## Abstract\n\nNone.\n\n## Context\n"),
            [
                "'## Context' must follow '## Status' directly",
                "nothing but the title line may precede",
            ]
        );
        yield return new(
            "text before Status in an open record fails",
            root =>
                Replace(
                    root,
                    Four,
                    "# ADR-4: Fixture Decision Four\n",
                    "# ADR-4: Fixture Decision Four\n\nA preamble.\n"
                ),
            ["nothing but the title line may precede '## Status'"]
        );
        yield return new(
            "text before Status in a closed record is protected non-body text",
            root =>
                Replace(
                    root,
                    One,
                    "# ADR-1: Fixture Decision One\n",
                    "# ADR-1: Fixture Decision One\n\nA preamble.\n"
                ),
            ["historical text outside the body changed although Status was 'Accepted'"]
        );
        yield return new(
            "duplicate section fails",
            root =>
                Replace(
                    root,
                    Four,
                    "## Consequences\n",
                    "## Migration Notes\n\nA.\n\n## Migration Notes\n\nB.\n\n## Consequences\n"
                ),
            ["section '## Migration Notes' appears more than once"]
        );
        yield return new(
            "conditional sections out of order fail",
            root =>
                Replace(
                    root,
                    Four,
                    "## Decision\n",
                    "## Considered Alternatives\n\nNone.\n\n## Decision Drivers\n\nNone.\n\n## Decision\n"
                ),
            ["section '## Decision Drivers' must come before '## Considered Alternatives'"]
        );

        // Fences
        yield return new(
            "a heading inside a longer fence is not a section",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces.\n\n````\n```\n## Not A Section\n```\n````"
                ),
            []
        );
        yield return new(
            "a tilde fence does not close a backtick fence",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces.\n\n```\n~~~\n## Not A Section\n```"
                ),
            []
        );
        yield return new(
            "a fence marker followed by text does not close a fence",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces.\n\n```\n``` not a closer\n## Not A Section\n```"
                ),
            []
        );
        yield return new(
            "a backtick run with a backtick in its info string does not open a fence",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces.\n\n``` a`b\n## Real Section After Context\n"
                ),
            []
        );

        // References and links
        yield return new(
            "body reference to a later record fails",
            root => Replace(root, Four, "The fourth forces.", "The fourth forces, see ADR-5."),
            ["references ADR-5, which is later than this record"]
        );
        yield return new(
            "body link to a later record's file fails whatever the link text",
            root =>
                Replace(
                    root,
                    One,
                    "The first forces.",
                    $"The first forces, see [the successor]({Four})."
                ),
            ["body newly references ADR-4", "body changed although Status was 'Accepted'"]
        );
        yield return new(
            "body link to a later record's file with a fragment fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [five]({Five}#context)."
                ),
            ["body references ADR-5, which is later"]
        );
        yield return new(
            "body link to a later record's file with a query fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [five]({Five}?view=1)."
                ),
            ["body references ADR-5, which is later"]
        );
        yield return new(
            "body reference usage whose definition sits in Status fails",
            root =>
            {
                Replace(root, Four, "Proposed\n", "Proposed\n\n[next]: " + Five + "\n");
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [successor][next]."
                );
            },
            ["body references ADR-5, which is later"]
        );
        yield return new(
            "definition in the body used only from Status is not a body reference",
            root =>
            {
                Replace(root, Four, "Proposed\n", "Proposed\n\nSee [the successor][next].\n");
                Replace(
                    root,
                    Four,
                    "- Neutral: none.\n",
                    "- Neutral: none.\n\n[next]: " + Five + "\n"
                );
            },
            []
        );
        yield return new(
            "absolute-path link to a record fails as non-relative and is not counted",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [five](/docs/decisions/{Five})."
                ),
            ["local link must be a relative path"]
        );
        yield return new(
            "record-like basename outside the decisions directory is not an ADR reference",
            root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "docs", "notes"));
                File.WriteAllText(
                    Path.Combine(root, "docs", "notes", "5_fixture-decision-five.md"),
                    "# Notes\n"
                );
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [notes](../notes/5_fixture-decision-five.md)."
                );
            },
            []
        );
        yield return new(
            "link whose text and target disagree fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [ADR-1]({Two})."
                ),
            ["link text names ADR-1 but the target is ADR-2's file"]
        );
        yield return new(
            "reference-style link whose text and target disagree fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [ADR-1][t].\n\n[t]: {Two}"
                ),
            ["link text names ADR-1 but the target is ADR-2's file"]
        );
        yield return new(
            "definition label is not displayed text",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [ADR-2][ADR-1].\n\n[ADR-1]: {Two}"
                ),
            []
        );
        yield return new(
            "inline link to a missing file fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [notes](notes.md)."
                ),
            ["local link target does not exist: notes.md"]
        );
        yield return new(
            "reference definition with a missing file fails once",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [notes][n].\n\n[n]: notes.md"
                ),
            ["local link target does not exist: notes.md"]
        );
        yield return new(
            "undefined reference label fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [notes][missing]."
                ),
            ["undefined reference label: [missing]"]
        );
        yield return new(
            "collapsed and shortcut references resolve through their definition",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [one][] and [one].\n\n[one]: {One}"
                ),
            []
        );
        yield return new(
            "link to a missing anchor fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [one]({One}#missing)."
                ),
            ["Markdown anchor does not exist"]
        );
        yield return new(
            "link that escapes the repository fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [out](../../../outside.md)."
                ),
            ["local link escapes the repository"]
        );
        yield return new(
            "footnote whose text holds a link passes",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces.[^1]\n\n[^1]: Recorded in [ADR-1]({One})."
                ),
            []
        );
        yield return new(
            "footnote text with a broken link fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces.[^1]\n\n[^1]: See [notes](notes.md)."
                ),
            ["local link target does not exist: notes.md"]
        );
        yield return new(
            "footnote reference without a definition fails",
            root => Replace(root, Four, "The fourth forces.", "The fourth forces.[^1]"),
            ["footnote [^1] has no definition"]
        );
        yield return new(
            "footnote definition that nothing references fails",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces.\n\n[^1]: An unused note."
                ),
            ["footnote definition [^1] is never referenced"]
        );
        yield return new(
            "link syntax inside a code span is not a link",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, write `[x](missing.md)` literally."
                ),
            []
        );
        yield return new(
            "escaped brackets are not a link",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, \\[x\\](missing.md) literally."
                ),
            []
        );
        yield return new(
            "link destination with parentheses is outside the subset",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [spec](file_(version).md)."
                ),
            ["link is outside the supported Markdown subset"]
        );
        yield return new(
            "link without its closing parenthesis is outside the subset",
            root =>
                Replace(root, Four, "The fourth forces.", $"The fourth forces, see [one]({One}"),
            ["link is outside the supported Markdown subset"]
        );
        yield return new(
            "link with a title is outside the subset",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    $"The fourth forces, see [one]({One} \"title\")."
                ),
            ["link is outside the supported Markdown subset"]
        );

        yield return new(
            "angle-bracket destination with a space is outside the subset",
            root =>
            {
                File.WriteAllText(Path.Combine(root, Decisions, "foo bar.md"), "# Foo\n");
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [x](<foo bar.md>)."
                );
            },
            ["link is outside the supported Markdown subset", "is not a valid ADR record filename"]
        );
        yield return new(
            "empty destination is outside the subset",
            root => Replace(root, Four, "The fourth forces.", "The fourth forces, see [x]()."),
            ["link is outside the supported Markdown subset"]
        );
        yield return new(
            "reference definition with a title is outside the subset",
            root =>
                Replace(
                    root,
                    Four,
                    "- Neutral: none.\n",
                    "- Neutral: none.\n\n[n]: notes.md \"title\"\n"
                ),
            ["link is outside the supported Markdown subset"]
        );
        yield return new(
            "full reference usage whose second bracket is not closed is outside the subset",
            root => Replace(root, Four, "The fourth forces.", "The fourth forces, see [one][label"),
            ["link is outside the supported Markdown subset"]
        );
        yield return new(
            "reference usage with an opened empty second bracket is outside the subset",
            root =>
                Replace(
                    root,
                    Four,
                    "The fourth forces.",
                    "The fourth forces, see [one][ and more."
                ),
            ["link is outside the supported Markdown subset"]
        );
        yield return new(
            "unreadable base: a record whose base Status cannot be read fails",
            root => Replace(root, One, "Under review by the team", "Accepted"),
            ["Status at the base cannot be read"],
            Fixture: Fixture.Unreadable
        );
    }

    private static async Task CreateFixtureAsync(string root, Fixture fixture)
    {
        var directory = Path.Combine(root, Decisions);
        Directory.CreateDirectory(directory);
        if (fixture == Fixture.Main)
        {
            // ADR-1 is amended by ADR-2, an established relation whose amender was later deprecated,
            // and carries a section the template does not name. ADR-3 is a rejected proposal that
            // kept its declaration and was closed under other writing rules: it has a section
            // between Status and Context, a filename not derived from its title, and a body
            // reference to a later record. ADR-4 is proposed; ADR-5 is a proposal that declares an
            // amendment.
            Write(
                root,
                One,
                Record(
                    1,
                    "Fixture Decision One",
                    $"Accepted\n\n- Amended by: [ADR-2]({Two}) — the first forces",
                    "The first forces."
                ) + "\n## Wire Format\n\nBytes.\n"
            );
            Write(
                root,
                Two,
                Record(
                    2,
                    "Fixture Decision Two",
                    $"Deprecated\n\n- Amends: [ADR-1]({One}) — the first forces",
                    "The second forces.",
                    Note("a wire format")
                )
            );
            Write(
                root,
                Three,
                Record(
                    3,
                    "Fixture Decision Three (Draft)",
                    $"Rejected\n\n- Supersedes: [ADR-1]({One})",
                    "The third forces, as ADR-5 later showed.",
                    "## Abstract\n\nAn old abstract.\n\n"
                )
            );
            Write(root, Four, Record(4, "Fixture Decision Four", "Proposed", "The fourth forces."));
            Write(
                root,
                Five,
                Record(
                    5,
                    "Fixture Decision Five",
                    $"Proposed\n\n- Amends: [ADR-1]({One}) — draft scope",
                    "The fifth forces."
                )
            );
            Write(
                root,
                "index.md",
                "# ADR Index\n\n| # | Title | Keywords | Abstract |\n|---|-------|----------|----------|\n"
                    + $"| [ADR-1]({One}) | Fixture Decision One | one | Decides one because one. |\n"
                    + $"| [ADR-2]({Two}) | ~~Fixture Decision Two~~ *(Deprecated)* | two | Decides two because two. |\n"
                    + $"| [ADR-3]({Three}) | Fixture Decision Three (Draft) | three | Decides three because three. |\n"
                    + $"| [ADR-4]({Four}) | Fixture Decision Four | four | Decides four because four. |\n"
                    + $"| [ADR-5]({Five}) | Fixture Decision Five | five | Decides five because five. |\n"
            );
        }
        else
        {
            // A single record whose Status word is outside the vocabulary.
            Write(
                root,
                One,
                Record(1, "Fixture Decision One", "Under review by the team", "The first forces.")
            );
            Write(
                root,
                "index.md",
                "# ADR Index\n\n| # | Title | Keywords | Abstract |\n|---|-------|----------|----------|\n"
                    + $"| [ADR-1]({One}) | Fixture Decision One | one | Decides one because one. |\n"
            );
        }

        // The fixture repository must not depend on the user's Git configuration: no signing, no
        // hooks, a fixed branch name, and an explicit identity.
        var hooks = Path.Combine(root, ".no-hooks");
        Directory.CreateDirectory(hooks);
        await GitAsync(root, "init", "--quiet", "--initial-branch=main");
        await GitAsync(root, "config", "commit.gpgsign", "false");
        await GitAsync(root, "config", "core.hooksPath", hooks);
        await GitAsync(root, "config", "user.name", "adr-check");
        await GitAsync(root, "config", "user.email", "adr-check@example.invalid");
        await GitAsync(root, "add", "--all");
        await GitAsync(root, "commit", "--quiet", "-m", "fixture");
    }

    private static string Record(
        int number,
        string title,
        string status,
        string context,
        string between = ""
    ) =>
        $"# ADR-{number}: {title}\n\n## Status\n\n{status}\n\n{between}## Context\n\n{context}\n\n"
        + $"## Decision\n\nDecide {number}.\n\n## Rationale\n\nBecause {NumberWord(number)}.\n\n"
        + $"## Consequences\n\n- Neutral: none.\n";

    // One Maintenance Note section holding a single entry, as an editorial revision leaves it.
    private static string Note(string what) =>
        $"## Maintenance Note\n\n- Moved {what} to the protocol page; the decision, drivers, "
        + "alternatives, and consequences were retained. Authorised in advance by the owner.\n\n";

    private static string NumberWord(int number) =>
        number switch
        {
            1 => "one",
            2 => "two",
            3 => "three",
            4 => "four",
            5 => "five",
            _ => "six",
        };

    private static void Write(string root, string name, string content) =>
        File.WriteAllText(Path.Combine(root, Decisions, name), content);

    private static void Replace(string root, string name, string from, string to)
    {
        var path = Path.Combine(root, Decisions, name);
        var text = File.ReadAllText(path);
        if (!text.Contains(from, StringComparison.Ordinal))
            throw new InvalidOperationException($"fixture text not found in {name}: {from}");
        File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal));
    }

    private static void Append(string root, string name, string content) =>
        File.AppendAllText(Path.Combine(root, Decisions, name), content);

    // Sets the status word of a Proposed fixture record and adds one declared relation.
    private static void Declare(string root, string name, string status, string relation) =>
        Replace(root, name, "Proposed\n", $"{status}\n\n- {relation}\n");

    // Marks ADR-1 superseded by the given record and strikes its index title.
    private static void Supersede(string root, string name, string currentStatus, int by = 4)
    {
        var successor = by switch
        {
            2 => Two,
            4 => Four,
            _ => throw new ArgumentOutOfRangeException(nameof(by)),
        };
        Replace(
            root,
            name,
            $"{currentStatus}\n",
            $"Superseded\n\n- Superseded by: [ADR-{by}]({successor})\n"
        );
        Replace(
            root,
            "index.md",
            "| Fixture Decision One |",
            $"| ~~Fixture Decision One~~ *(Superseded by ADR-{by})* |"
        );
    }

    private static void Rename(string root, string from, string to, string newTitle)
    {
        var directory = Path.Combine(root, Decisions);
        File.Move(Path.Combine(directory, from), Path.Combine(directory, to));
        Replace(root, to, "# ADR-1: Fixture Decision One", $"# ADR-1: {newTitle}");
        Replace(
            root,
            "index.md",
            $"[ADR-1]({from}) | Fixture Decision One",
            $"[ADR-1]({to}) | {newTitle}"
        );
        foreach (var other in new[] { Two, Three, Five })
            Replace(root, other, $"[ADR-1]({from})", $"[ADR-1]({to})");
    }

    private static async Task GitAsync(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var standardError = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {await standardError}"
            );
    }
}
