using System.Text.Json;
using ConsoleAppFramework;

namespace Duets.Sandbox;

public class Commands
{
    public async Task Complete([Argument] string source, int? position = null)
    {
        await using var session = new SandboxSession();
        await session.EnsureInitializedAsync();
        OutputJson(CompleteOnce(session, source, position ?? source.Length));
    }

    public async Task Serve(int port = 17375, CancellationToken cancellationToken = default)
    {
        await using var session = new SandboxSession();
        await session.EnsureInitializedAsync();
        session.StartWebServer(port);
        Console.Error.WriteLine("Press Ctrl+C to stop.");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await session.StopWebServerAsync();
    }

    public async Task Batch()
    {
        await using var session = new SandboxSession();
        await session.EnsureInitializedAsync();
        await new BatchRunner(session).RunAsync();
    }

    public async Task Repl()
    {
        await using var session = new SandboxSession();
        await session.EnsureInitializedAsync();
        await new InteractiveRepl(session).RunAsync();
    }

    private static object CompleteOnce(SandboxSession s, string source, int position)
    {
        try
        {
            var completions = s.GetCompletions(source, position);
            return new { ok = true, completions };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private static void OutputJson(object obj)
    {
        Console.WriteLine(JsonSerializer.Serialize(obj, BatchRunner.JsonOptions));
    }
}
