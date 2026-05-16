#!/usr/bin/env dotnet
#:package ProcessX

using System.Runtime.CompilerServices;
using Cysharp.Diagnostics;
using Zx;
using static Zx.Env;

workingDirectory = Path.GetDirectoryName(ScriptRoot())!;
if (!Console.IsOutputRedirected)
    envVars["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";

try
{
    await $"dotnet format style Duets.slnx --no-restore 2>&1";
    await $"dotnet csharpier format . 2>&1";
}
catch (ProcessErrorException ex)
{
    Environment.Exit(ex.ExitCode);
}

static string ScriptRoot([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
