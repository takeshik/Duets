#!/usr/bin/env dotnet
/*
#MISE description="Check documentation integrity"
#MISE alias="docs-check"
*/

using System.Diagnostics;
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

        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].EndsWith(' ') || lines[index].EndsWith('\t'))
                errors.Add($"{relativePath}:{index + 1}: trailing whitespace");
        }

        foreach (var (lineNumber, label) in MarkdownLinks.UndefinedReferences(lines))
            errors.Add($"{relativePath}:{lineNumber}: undefined reference label: [{label}]");
        foreach (var (lineNumber, message) in MarkdownLinks.FootnoteErrors(lines))
            errors.Add($"{relativePath}:{lineNumber}: {message}");

        // A reference usage takes its target from its definition, which is resolved on its own.
        foreach (var link in MarkdownLinks.Enumerate(lines))
        {
            if (link.Kind == MarkdownLinkKind.Malformed)
            {
                errors.Add(
                    $"{relativePath}:{link.LineNumber}: link is outside the supported Markdown "
                        + $"subset (see CONTRIBUTING.md): {link.RawTarget}"
                );
                continue;
            }

            if (link.Kind == MarkdownLinkKind.ReferenceUsage)
                continue;

            var resolution = MarkdownLinks.Resolve(
                this._root,
                relativePath,
                link.RawTarget,
                this._anchorCache
            );
            if (resolution.Error is not null)
                errors.Add($"{relativePath}:{link.LineNumber}: {resolution.Error}");
        }
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

    private static bool IsExcludedDocumentation(string path) =>
        path == "CLAUDE.md" || AdrRecordRegex().IsMatch(path);

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    [GeneratedRegex(@"^##\s+(?<package>[^#]+?)\s*$")]
    private static partial Regex PackageHeadingRegex();

    [GeneratedRegex(@"^\|\s*`(?<file>[^`/]+\.cs)`\s*\|")]
    private static partial Regex SampleEntryRegex();

    [GeneratedRegex(@"^docs/decisions/[0-9]+_[^/]*\.md$")]
    private static partial Regex AdrRecordRegex();
}
