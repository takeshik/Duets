// With web REPL
//
// Serves a browser-based Monaco editor with TypeScript completions and live
// CLR type updates via SSE. Open http://127.0.0.1:17375/ after startup.
//
// TypeScriptService is selected via the factory overload so the session owns
// the TypeDeclarations instance that both the language service and ReplService
// share — no manual wiring required.
#:project ../src/Duets/Duets.csproj

using Duets;
using HttpHarker;
using Jint;

using var session = await DuetsSession.CreateAsync(
    decls => TypeScriptService.CreateAsync(decls),
    opts => opts.AllowClr());
session.RegisterTypeBuiltins();

using var server = new HttpServer("http://127.0.0.1:17375/");
using var repl = server
    .UseContentTypeDetection()
    .UseRepl(session);

Console.Error.WriteLine("Web REPL started at http://127.0.0.1:17375/ — press Ctrl+C to stop.");
await server.RunAsync();
