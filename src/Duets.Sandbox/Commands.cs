using System.Security.Cryptography;
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

    /// <param name="port">TCP port to listen on.</param>
    /// <param name="auth">
    /// Require an access token (ADR-49). The token is generated and printed as part of the pad URL
    /// rather than accepted as an option value, so an RCE-equivalent credential never lands in
    /// shell history or a process listing.
    /// </param>
    public async Task Serve(
        int port = 17375,
        bool auth = false,
        CancellationToken cancellationToken = default
    )
    {
        var token = auth ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) : null;
        await using var ctx = await SandboxContext.CreateAsync();
        ctx.StartWebServer(port, token);
        if (token is not null)
        {
            await Console.Error.WriteLineAsync(
                $"Access token required — open: http://127.0.0.1:{port}/#token={token}"
            );
        }

        await Console.Error.WriteLineAsync("Press Ctrl+C to stop.");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

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
