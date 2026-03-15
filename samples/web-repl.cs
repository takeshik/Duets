// With web REPL
//
// Serves a browser-based Monaco editor with TypeScript completions and live
// CLR type updates via SSE. Open http://127.0.0.1:17375/ after startup.
#:project ../src/Duets/Duets.csproj

using Duets;
using HttpHarker;
using Jint;

using var ts = new TypeScriptService();
await ts.ResetAsync();

using var engine = new ScriptEngine(opts => opts.AllowClr(), ts);
engine.RegisterTypeBuiltins(ts);

using var server = new HttpServer("http://127.0.0.1:17375/");
using var repl = server
    .UseContentTypeDetection()
    .UseRepl(ts, engine);

Console.Error.WriteLine("Web REPL started at http://127.0.0.1:17375/ — press Ctrl+C to stop.");
await server.RunAsync();
