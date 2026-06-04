#!/usr/bin/env dotnet
/*
#MISE description="Format code using dotnet format and csharpier"
#MISE alias="fmt"
*/

run(async () =>
{
    workingDirectory = solutionRoot;
    var dir = args.ElementAtOrDefault(0) ?? ".";
    await start(
        "dotnet",
        [
            "format",
            "style",
            "Duets.slnx",
            "--no-restore",
            // https://github.com/dotnet/format/issues/348
            "--exclude-diagnostics",
            "IDE1006",
            "IDE0060",
            .. StyleScope(dir),
        ]
    );
    await start("dotnet", ["csharpier", "format", dir]);
});

// dotnet format only applies analyzer code fixes (e.g. `this.` qualification, IDE0003/IDE0009)
// for the whole solution or for explicit file paths; a folder passed via --include is silently
// skipped. `--severity info` is required because those rules are configured at suggestion level.
static IEnumerable<string> StyleScope(string dir)
{
    yield return "--severity";
    yield return "info";
    if (dir == ".")
        yield break;
    var root = Path.Combine(solutionRoot, dir);
    foreach (
        var file in Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
    )
    {
        // dotnet format matches --include against solution-relative paths; absolute paths never match.
        yield return "--include";
        yield return Path.GetRelativePath(solutionRoot, file);
    }
}
