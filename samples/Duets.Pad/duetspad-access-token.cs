// DuetsPad with access-token authentication (ADR-49)
//
// Serves the pad with a generated access token: every session-API request must
// carry it, so only clients holding the token can evaluate code. Open the
// printed #token=... URL — the token travels in the URL fragment (never sent to
// the server, never logged), and the page stores it in sessionStorage and
// attaches it to every request as an Authorization: Bearer header.
//
// Things to try once the pad is open:
// - Evaluate code and watch the Timeline update: the SSE event stream is also
//   authenticated (it runs over fetch, not EventSource, precisely so it can
//   carry the header).
// - Open the plain URL (without #token=...) in a private window: the UI loads
//   (static assets are not gated), but the pad shows a token prompt because
//   every session operation is rejected with 401.
//
// The sample binds to loopback. For real LAN exposure bind a reachable address
// instead, and note that TLS is deliberately out of scope for the pad itself —
// terminate TLS in a reverse proxy if token sniffing matters on your network.
#:project ../../src/Duets.Jint/Duets.Jint.csproj
#:project ../../src/Duets.Pad/Duets.Pad.csproj

using System.Security.Cryptography;
using Duets;
using Duets.Jint;
using Duets.Pad;
using HttpHarker;
using Jint;

var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

using var server = new HttpServer("http://127.0.0.1:17375/");
using var pad = server
    .UseContentTypeDetection()
    .UseDuetsPad(configure: opts =>
    {
        opts.SessionFactory = () => DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
        opts.Authenticate = DuetsPadAuthenticator.Token(token);
    });

Console.Error.WriteLine("DuetsPad started with access-token authentication.");
Console.Error.WriteLine($"Open: http://127.0.0.1:17375/#token={token}");
Console.Error.WriteLine("Press Ctrl+C to stop.");
await server.RunAsync();
