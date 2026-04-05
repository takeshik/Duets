using System.Net;
using System.Net.Sockets;
using HttpHarker;

namespace Duets.Tests.TestSupport;

internal static class DuetsServerFixture
{
    public static async Task RunAsync(
        Action<HttpServer> configure,
        Func<HttpClient, string, Task> test)
    {
        var port = PickPort();
        var prefix = $"http://127.0.0.1:{port}/";

        using var server = new HttpServer(prefix);
        configure(server);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serverTask = server.RunAsync(cancellationToken: cts.Token);

        await Task.Delay(50);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        try
        {
            await test(client, prefix);
        }
        finally
        {
            cts.Cancel();
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // The listener throws during normal cancellation.
            }
        }
    }

    private static int PickPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
