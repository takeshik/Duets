// With web REPL
//
// Serves a browser-based Monaco editor with TypeScript completions and live
// CLR type updates via SSE. Open http://127.0.0.1:17375/ after startup.
#:project ../src/Duets.Jint/Duets.Jint.csproj

using Duets;
using Duets.Jint;
using HttpHarker;
using Jint;

using var session = await DuetsSession.CreateAsync(config => config.UseJint(opts => opts.AllowClr()));

using var server = new HttpServer("http://127.0.0.1:17375/");
using var repl = server
    .UseContentTypeDetection()
    .UseRepl(session);

Console.Error.WriteLine("Web REPL started at http://127.0.0.1:17375/ — press Ctrl+C to stop.");
await server.RunAsync();
