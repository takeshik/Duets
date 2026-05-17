#!/usr/bin/env dotnet
/*
#MISE description="Format code using dotnet format and csharpier"
#MISE alias="fmt"
*/

run(async () =>
{
    workingDirectory = solutionRoot;
    var dir = args.ElementAtOrDefault(0) ?? ".";
    await start("dotnet", ["format", "style", "Duets.slnx", "--no-restore", "--include", dir]);
    await start("dotnet", ["csharpier", "format", dir]);
});
