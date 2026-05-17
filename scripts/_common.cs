using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cysharp.Diagnostics;

public class Util
{
    public static string scriptRoot([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;

    public static string solutionRoot => Path.GetDirectoryName(scriptRoot())!;

    public static void run(Func<Task> action)
    {
        if (!Console.IsOutputRedirected)
            envVars["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";

        RunAsync().Wait();

        async Task RunAsync()
        {
            try
            {
                await action();
            }
            catch (ProcessErrorException ex)
            {
                Environment.Exit(ex.ExitCode);
            }
        }
    }

    public static async Task start(string file, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(file);
        if (workingDirectory != null)
            psi.WorkingDirectory = workingDirectory;
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        foreach (var (k, v) in envVars)
            psi.EnvironmentVariables[k] = v;

        var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync(terminateToken);
        if (proc.ExitCode != 0)
            throw new ProcessErrorException(proc.ExitCode, []);
    }
}
