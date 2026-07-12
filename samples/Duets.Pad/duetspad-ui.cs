// DuetsPad: the ui.* structured display / form-input surface
//
// Serves the same DuetsPad browser debug pad as duetspad.cs (Editor / Canvas /
// Timeline / Immediate). The interesting part of this sample is not the C#
// host below — it is identical in shape to duetspad.cs — but the TypeScript
// demo script below, meant to be pasted into the pad's Editor and evaluated
// with Ctrl+Enter or F5.
//
// Open http://127.0.0.1:17376/ after startup, paste the script into the
// Editor, and evaluate it. Reminders on the pad's output surfaces:
// - `dump(value)` appends a rendered value to the Timeline.
// - `canvas` is the default Canvas surface (`canvas.add`/`canvas.set`/
//   `canvas.clear`); `canvases.get(name)` returns a named Canvas tab with the
//   same surface, created on first access.
//
// TypeScript demo — paste into the Editor:
/*
const nameBox = ui.textBox({ name: "name", placeholder: "Your name", value: "World" });
const subscribe = ui.checkBox({ label: "Subscribe to updates", checked: false });
const greeting = ui.slot(ui.text("(click Greet to render a greeting here)"));

const greet = () => {
  // Handlers run server-side; reading .value here always reflects the
  // server-canonical current value of the input, not a client-side echo.
  greeting.content = ui.text(`Hello, ${nameBox.value}! Subscribed: ${subscribe.value}`);
};

canvas.set(
  ui.card(
    [
      ui.row([ui.col([ui.label("Name"), nameBox]), ui.col([subscribe])]),
      ui.button("Greet", greet),
      greeting,
    ],
    { title: "ui.* demo" }
  )
);
*/
#:project ../../src/Duets.Jint/Duets.Jint.csproj
#:project ../../src/Duets.Pad/Duets.Pad.csproj

using Duets;
using Duets.Jint;
using Duets.Pad;
using HttpHarker;
using Jint;

using var server = new HttpServer("http://127.0.0.1:17376/");
using var pad = server
    .UseContentTypeDetection()
    .UseDuetsPad(configure: opts =>
        opts.SessionFactory = () => DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()))
    );

Console.Error.WriteLine("DuetsPad started at http://127.0.0.1:17376/ — press Ctrl+C to stop.");
await server.RunAsync();
