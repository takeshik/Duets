#!/usr/bin/env dotnet
/*
#MISE description="Run Duets.Sandbox"
#MISE alias="sbx"
*/

run(async () =>
{
    workingDirectory = solutionRoot;
    await start("dotnet", ["run", "--project", "src/Duets.Sandbox", .. args]);
});
