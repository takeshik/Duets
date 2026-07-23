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
// - `value.dump()` appends a rendered value to the Timeline and returns that value.
// - Object dump headers can collapse one table or its entire nested subtree.
// - `canvas` is the default Canvas surface (`canvas.add`/`canvas.set`/
//   `canvas.clear`); `canvases.get(name)` returns a named Canvas tab with the
//   same surface, created on first access.
//
// TypeScript demo — paste into the Editor:
/*
const nameBox = ui.textBox({ name: "name", placeholder: "Your name", value: "World" });
const subscribe = ui.checkBox({ label: "Subscribe to updates", checked: false });
const greeting = ui.slot(ui.text("(click Greet to render a greeting here)"));
const attachments = ui.filePicker({ accept: "text/plain", multiple: true });
const attachmentSummary = ui.slot(ui.text("No files attached."));

const greet = () => {
  // Handlers run server-side; reading .value here always reflects the
  // server-canonical current value of the input, not a client-side echo.
  greeting.content = ui.text(`Hello, ${nameBox.value}! Subscribed: ${subscribe.value}`);
  ui.toast("Greeting updated.", { variant: "success" });
};

const summarizeAttachments = () => {
  const files = attachments.files;
  attachmentSummary.content = ui.text(
    files.length === 0
      ? "No files attached."
      : files.map((file) => `${file.name} (${file.size} bytes)`).join(", ")
  );
};

const openProfileModal = () => {
  const alias = ui.textBox({ placeholder: "Display name", value: nameBox.value });
  const preview = ui.slot(ui.text("Edit the value, then update this preview."));
  ui.modal(
    ui.stack([
      ui.label("Display name"),
      alias,
      ui.button("Update preview", () => {
        preview.content = ui.badge(alias.value || "(empty)", { color: "blue" });
      }),
      preview,
    ]),
    result => {
      if (result.reason === "action" && result.actionId === "save") {
        nameBox.value = alias.value;
        greet();
      } else {
        ui.toast(`Modal closed: ${result.actionId ?? result.reason}`);
      }
    },
    {
      title: "Edit profile",
      buttons: [
        { id: "cancel", label: "Cancel" },
        { id: "save", label: "Save", variant: "primary" },
      ],
      defaultButtonId: "save",
      dismissButtonId: "cancel",
      size: "md",
    }
  );
};

canvas.set(
  ui.card(
    [
      ui.row([ui.col([ui.label("Name"), nameBox]), ui.col([subscribe])]),
      ui.button("Open profile modal", openProfileModal),
      greeting,
      ui.divider({ text: "Diagnostics" }),
      ui.dataGrid([
        { label: "Runtime", content: ui.badge("Jint", { color: "blue" }) },
        { label: "State", content: ui.status("Ready", { color: "green" }) },
      ]),
      ui.emptySpace("No warnings", {
        message: "The latest check completed without warnings.",
        icon: "circle-check",
      }),
      ui.disclosure(
        "Diagnostic details",
        ui.stack([
          ui.code("const state = inspect(runtime);", { wrap: true }),
          ui.preformatted("worker-1: ready\nworker-2: idle"),
        ])
      ),
      ui.divider({ text: "Attachments" }),
      attachments,
      ui.button("Summarize attachments", summarizeAttachments),
      attachmentSummary,
    ],
    {
      title: "ui.* demo",
      footer: ui.button("Greet", greet, {
        variant: "green",
        outline: true,
        size: "sm",
      }),
    }
  )
);

({
  profile: {
    name: nameBox.value,
    subscribed: subscribe.value,
  },
  attachments: {
    count: attachments.files.length,
  },
}).dump();
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
