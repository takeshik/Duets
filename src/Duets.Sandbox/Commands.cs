using System.Text.Json;
using ConsoleAppFramework;

namespace Duets.Sandbox;

public class Commands
{
    public async Task Complete([Argument] string source, int? position = null)
    {
        await using var ctx = await SandboxContext.CreateAsync();
        OutputJson(CompleteOnce(ctx, source, position ?? source.Length));
    }

    public async Task Serve(int port = 17375, CancellationToken cancellationToken = default)
    {
        await using var ctx = await SandboxContext.CreateAsync();
        ctx.StartWebServer(port);
        await Console.Error.WriteLineAsync("Press Ctrl+C to stop.");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await ctx.StopWebServerAsync();
    }

    public async Task Batch()
    {
        await using var ctx = await SandboxContext.CreateAsync();
        await new BatchRunner(ctx).RunAsync();
    }

    public async Task Repl()
    {
        await using var ctx = await SandboxContext.CreateAsync();
        await new InteractiveRepl(ctx).RunAsync();
    }

    private static object CompleteOnce(SandboxContext ctx, string source, int position)
    {
        try
        {
            var completions = ctx.GetCompletions(source, position);
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
