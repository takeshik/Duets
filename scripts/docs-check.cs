#!/usr/bin/env dotnet
/*
#MISE description="Check documentation integrity"
#MISE alias="docs-check"
*/

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

var checker = new DocumentationChecker(solutionRoot);
var errors = await checker.CheckAsync();

if (errors.Count == 0)
{
    Console.WriteLine(
        $"Checked {checker.MarkdownFileCount} Markdown files and {checker.SampleFileCount} samples."
    );
    return 0;
}

foreach (var error in errors)
    Console.Error.WriteLine(error);
Console.Error.WriteLine($"Documentation check failed with {errors.Count} error(s).");
return 1;

internal sealed partial class DocumentationChecker(string root)
{
    private readonly string _root = Path.GetFullPath(root);
    private readonly Dictionary<string, IReadOnlySet<string>> _anchorCache = new(
        StringComparer.Ordinal
    );

    public int MarkdownFileCount { get; private set; }

    public int SampleFileCount { get; private set; }

    public async Task<IReadOnlyList<string>> CheckAsync()
    {
        var errors = new List<string>();
        var markdownFiles = (await this.GetRepositoryFilesAsync("*.md", errors))
            .Where(path => !IsExcludedDocumentation(path))
            .ToArray();
        this.MarkdownFileCount = markdownFiles.Length;

        foreach (var relativePath in markdownFiles)
            this.CheckMarkdownFile(relativePath, errors);

        var sampleFiles = await this.GetRepositoryFilesAsync("samples/**/*.cs", errors);
        this.SampleFileCount = sampleFiles.Count;
        this.CheckSampleCatalog(sampleFiles, errors);
        this.CheckRetiredPaths(markdownFiles, errors);
        await this.CheckGitDiffAsync(errors);

        errors.Sort(StringComparer.Ordinal);
        return errors;
    }

    private void CheckMarkdownFile(string relativePath, List<string> errors)
    {
        var fullPath = Path.Combine(this._root, relativePath);
        var lines = File.ReadAllLines(fullPath);
        var inFence = false;
        var fenceMarker = '\0';
        var fenceLength = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (line.EndsWith(' ') || line.EndsWith('\t'))
                errors.Add($"{relativePath}:{lineNumber}: trailing whitespace");

            var fence = FenceRegex().Match(line);
            if (fence.Success)
            {
                var marker = fence.Groups["marker"].Value;
                if (!inFence)
                {
                    inFence = true;
                    fenceMarker = marker[0];
                    fenceLength = marker.Length;
                }
                else if (marker[0] == fenceMarker && marker.Length >= fenceLength)
                {
                    inFence = false;
                }

                continue;
            }

            if (inFence)
                continue;

            var definition = LinkDefinitionRegex().Match(line);
            if (definition.Success)
            {
                this.CheckLink(relativePath, lineNumber, definition.Groups["target"].Value, errors);
            }

            foreach (Match link in InlineLinkRegex().Matches(line))
                this.CheckLink(relativePath, lineNumber, link.Groups["target"].Value, errors);
        }
    }

    private void CheckLink(string sourcePath, int lineNumber, string rawTarget, List<string> errors)
    {
        var target = rawTarget.Trim('<', '>');
        if (target.Length == 0 || ExternalTargetRegex().IsMatch(target) || target.StartsWith('/'))
            return;

        var fragmentIndex = target.IndexOf('#');
        var pathPart = fragmentIndex >= 0 ? target[..fragmentIndex] : target;
        var fragment = fragmentIndex >= 0 ? target[(fragmentIndex + 1)..] : null;
        var queryIndex = pathPart.IndexOf('?');
        if (queryIndex >= 0)
            pathPart = pathPart[..queryIndex];

        var sourceFullPath = Path.Combine(this._root, sourcePath);
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
            errors.Add($"{sourcePath}:{lineNumber}: invalid local link target: {target}");
            return;
        }

        if (!IsWithinRoot(this._root, targetFullPath))
        {
            errors.Add($"{sourcePath}:{lineNumber}: local link escapes the repository: {target}");
            return;
        }

        if (!File.Exists(targetFullPath) && !Directory.Exists(targetFullPath))
        {
            errors.Add($"{sourcePath}:{lineNumber}: local link target does not exist: {target}");
            return;
        }

        if (
            string.IsNullOrEmpty(fragment)
            || !File.Exists(targetFullPath)
            || !Path.GetExtension(targetFullPath).Equals(".md", StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        var anchors = this.GetAnchors(targetFullPath);
        if (!anchors.Contains(fragment))
            errors.Add($"{sourcePath}:{lineNumber}: Markdown anchor does not exist: {target}");
    }

    private IReadOnlySet<string> GetAnchors(string fullPath)
    {
        if (this._anchorCache.TryGetValue(fullPath, out var cached))
            return cached;

        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var slugCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var inFence = false;
        var fenceMarker = '\0';
        var fenceLength = 0;

        foreach (var line in File.ReadLines(fullPath))
        {
            var fence = FenceRegex().Match(line);
            if (fence.Success)
            {
                var marker = fence.Groups["marker"].Value;
                if (!inFence)
                {
                    inFence = true;
                    fenceMarker = marker[0];
                    fenceLength = marker.Length;
                }
                else if (marker[0] == fenceMarker && marker.Length >= fenceLength)
                {
                    inFence = false;
                }

                continue;
            }

            if (inFence)
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

        this._anchorCache.Add(fullPath, anchors);
        return anchors;
    }

    private void CheckSampleCatalog(IReadOnlyList<string> sampleFiles, List<string> errors)
    {
        const string catalogPath = "samples/README.md";
        var catalogFullPath = Path.Combine(this._root, catalogPath);
        if (!File.Exists(catalogFullPath))
        {
            errors.Add($"{catalogPath}: sample catalog does not exist");
            return;
        }

        var indexedSamples = new Dictionary<string, int>(StringComparer.Ordinal);
        string? package = null;
        var lines = File.ReadAllLines(catalogFullPath);

        for (var index = 0; index < lines.Length; index++)
        {
            var heading = PackageHeadingRegex().Match(lines[index]);
            if (heading.Success)
            {
                var candidate = heading.Groups["package"].Value;
                package = Directory.Exists(Path.Combine(this._root, "samples", candidate))
                    ? candidate
                    : null;
                continue;
            }

            var entry = SampleEntryRegex().Match(lines[index]);
            if (!entry.Success)
                continue;

            if (package is null)
            {
                errors.Add(
                    $"{catalogPath}:{index + 1}: sample entry is not under a package heading"
                );
                continue;
            }

            var relativePath = NormalizePath(
                Path.Combine("samples", package, entry.Groups["file"].Value)
            );
            if (!indexedSamples.TryAdd(relativePath, index + 1))
                errors.Add($"{catalogPath}:{index + 1}: duplicate sample entry: {relativePath}");
        }

        var actualSamples = sampleFiles.ToHashSet(StringComparer.Ordinal);
        foreach (var (path, lineNumber) in indexedSamples)
        {
            if (!actualSamples.Contains(path))
                errors.Add($"{catalogPath}:{lineNumber}: indexed sample does not exist: {path}");
        }

        foreach (var path in actualSamples)
        {
            if (!indexedSamples.ContainsKey(path))
                errors.Add($"{catalogPath}: sample is not indexed: {path}");
        }
    }

    private void CheckRetiredPaths(IReadOnlyList<string> markdownFiles, List<string> errors)
    {
        string[] retiredPaths = ["docs/architecture.md", "docs/plans/"];

        foreach (var relativePath in markdownFiles)
        {
            var lines = File.ReadAllLines(Path.Combine(this._root, relativePath));
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var retiredPath in retiredPaths)
                {
                    if (lines[index].Contains(retiredPath, StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"{relativePath}:{index + 1}: retired live path reference: {retiredPath}"
                        );
                    }
                }
            }
        }
    }

    private async Task CheckGitDiffAsync(List<string> errors)
    {
        var result = await this.RunGitAsync(["diff", "--check", "HEAD", "--"]);
        if (result.ExitCode == 0)
            return;

        var output = string.Concat(result.StandardOutput, result.StandardError).Trim();
        if (output.Length == 0)
            errors.Add("git diff --check failed without diagnostic output");
        else
            errors.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<IReadOnlyList<string>> GetRepositoryFilesAsync(
        string pathSpec,
        List<string> errors
    )
    {
        var result = await this.RunGitAsync([
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
            "-z",
            "--",
            pathSpec,
        ]);
        if (result.ExitCode != 0)
        {
            errors.Add($"git ls-files failed for {pathSpec}: {result.StandardError.Trim()}");
            return [];
        }

        return result
            .StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizePath)
            .Where(path => File.Exists(Path.Combine(this._root, path)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
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

    private static bool IsExcludedDocumentation(string path) =>
        path == "CLAUDE.md"
        || path.StartsWith("docs/decisions/", StringComparison.Ordinal)
        || path.StartsWith(".claude/skills/adr/", StringComparison.Ordinal)
        || path.StartsWith(".claude/skills/adr-review/", StringComparison.Ordinal);

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

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    [GeneratedRegex(@"^ {0,3}(?<marker>`{3,}|~{3,})")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target><[^>\r\n]+>|[^\s)>]+)")]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:\s*(?<target><[^>\r\n]+>|[^\s]+)")]
    private static partial Regex LinkDefinitionRegex();

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

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^##\s+(?<package>[^#]+?)\s*$")]
    private static partial Regex PackageHeadingRegex();

    [GeneratedRegex(@"^\|\s*`(?<file>[^`/]+\.cs)`\s*\|")]
    private static partial Regex SampleEntryRegex();
}
