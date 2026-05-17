#!/usr/bin/env dotnet
/*
#MISE description="Check code formatting and style"
#MISE alias="fmt-check"
*/

run(async () =>
{
    workingDirectory = solutionRoot;
    var dir = args.ElementAtOrDefault(0) ?? ".";
    await start(
        "dotnet",
        ["format", "style", "Duets.slnx", "--no-restore", "--include", dir, "--verify-no-changes"]
    );
    await start("dotnet", ["csharpier", "check", dir]);
});
