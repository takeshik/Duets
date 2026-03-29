using System.Net;
using System.Net.Sockets;

namespace HttpHarker.Tests.TestSupport;

/// <summary>
/// Starts an <see cref="HttpServer"/> on a random ephemeral port, runs a test action against it,
/// then stops the server. Each call is fully isolated.
/// </summary>
internal static class ServerFixture
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

        await Task.Delay(50); // allow listener to start

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            await test(client, prefix);
        }
        finally
        {
            cts.Cancel();
            try
            {
                await serverTask;
            }
            catch
            {
                /* expected on cancellation */
            }
        }
    }

    private static int PickPort()
    {
        // Use a TcpListener on port 0 to get an OS-assigned free port.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
