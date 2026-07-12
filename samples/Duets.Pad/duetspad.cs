// DuetsPad: Editor / Canvas / Timeline / Immediate browser debug pad
//
// Serves a browser-based debug pad with a Monaco editor (Editor), a live
// output canvas (Canvas), an execution history pane (Timeline), and an
// immediate-mode expression viewer (Immediate).
// Open http://127.0.0.1:17375/ after startup.
#:project ../../src/Duets.Jint/Duets.Jint.csproj
#:project ../../src/Duets.Pad/Duets.Pad.csproj

using Duets;
using Duets.Jint;
using Duets.Pad;
using HttpHarker;
using Jint;

using var server = new HttpServer("http://127.0.0.1:17375/");
using var pad = server
    .UseContentTypeDetection()
    .UseDuetsPad(configure: opts =>
        opts.SessionFactory = () => DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()))
    );

Console.Error.WriteLine("DuetsPad started at http://127.0.0.1:17375/ — press Ctrl+C to stop.");
await server.RunAsync();
