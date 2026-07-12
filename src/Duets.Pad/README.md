# DuetsPad

A browser debug pad for [Duets](../../README.md): a Monaco Editor frontend served over HTTP with
live .NET type completions, a persistent output canvas, and an execution timeline. Attach it to any
application that hosts a `DuetsSession` and debug it from a browser — no ASP.NET Core required
(the HTTP layer is [HttpHarker](../HttpHarker/README.md)).

```
dotnet add package Duets.Pad
```

`Duets.Pad` depends on `Duets` and `HttpHarker`. You also need a runtime backend such as
`Duets.Jint`.

## Getting started

```csharp
using var server = new HttpServer("http://127.0.0.1:17375/");
using var pad = server.UseContentTypeDetection().UseDuetsPad(configure: opts =>
    opts.SessionFactory = () => DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr())));
await server.RunAsync(); // open http://127.0.0.1:17375/
```

Runnable examples live in [`samples/Duets.Pad/`](../../samples/Duets.Pad/).

## Surfaces

The pad presents four surfaces:

- **Editor** — a Monaco editor with TypeScript completions for the .NET types registered in the
  session. Each browser tab gets its own isolated server-side session.
- **Canvas** — persistent display state, updated in place. A session can hold multiple named
  canvases, each shown as its own tab.
- **Timeline** — append-only execution history: `dump` output, `console.*` output, evaluation
  results from the Immediate bar, and errors.
- **Immediate** — a single-line expression bar; results are recorded in the Timeline.

### Editor keybindings

| Key | Action |
|---|---|
| <kbd>Ctrl+Enter</kbd> | Evaluate the editor code |
| <kbd>F5</kbd> | Evaluate the editor code |

### Output rules

Output goes to the pad's structured surfaces, not back into the editor:

- `dump(value)`, `console.*`, and rendering errors append to the **Timeline**.
- `canvas.add(...)`, `canvas.set(...)`, and `canvas.clear()` update the **Canvas**. `canvas` is the
  default canvas; `canvases.get(name)` returns a named canvas (created on first access) with the
  same surface, each shown as its own Canvas tab.
- The **Immediate** bar evaluates a single line on <kbd>Enter</kbd> and shows that result in the
  Timeline; it keeps no result of its own.

The editor's final evaluation result is **not** automatically appended to the Timeline — use
`dump(value)` to record a value there.

## Building UI with `ui.*`

Beyond plain values, the pad exposes a `ui.*` builder surface for buttons, form inputs, layout, and
a mutable `ui.slot` handle whose content can be reassigned in place:

```typescript
const name = ui.textBox({ placeholder: "Your name" });
const greeting = ui.slot(ui.text("..."));
canvas.set(ui.stack([name, ui.button("Greet", () => {
  greeting.content = ui.text(`Hello, ${name.value}!`);
})]));
```

Click and other handlers run server-side, and an input's `.value` always reflects the
server-canonical current value rather than a client-side echo.

Available builders include text and layout primitives (`ui.text`, `ui.label`, `ui.stack`,
`ui.row`/`ui.col`, `ui.card`, `ui.divider`), indicators (`ui.badge`, `ui.alert`, `ui.spinner`,
`ui.status`, `ui.icon`, `ui.progress`), tables and links (`ui.table`, `ui.link`), interactions
(`ui.button`), form inputs (`ui.textBox`, `ui.textArea`, `ui.numberBox`, `ui.checkBox`,
`ui.dropDown`, `ui.slider`, `ui.radioGroup`), the in-place `ui.slot` handle, and raw escape
hatches (`ui.element`, `ui.rawHtml`). See [`samples/Duets.Pad/duetspad-ui.cs`](../../samples/Duets.Pad/duetspad-ui.cs)
for a copy-pasteable demo script.

## Configuration highlights

`UseDuetsPad(configure: opts => ...)` exposes `DuetsPadServiceOptions`, including:

- `SessionFactory` — creates the `DuetsSession` behind each browser session.
- `TimelineEntryLimit` — optional cap on retained Timeline entries (`null` = unlimited).
- `IdleTimeout` — optional automatic reclamation of idle sessions.
- `ObjectRenderers` / `DumpOptions` — customize how values are rendered.
- `MonacoLoader`, `TablerCss`, `TablerIconsCss`, `TablerIconsFont`, `MonacoBaseUrl` — pluggable
  asset sources for offline or custom-hosted frontend assets.

## Architecture

Design decisions and the rendering/protocol model are documented in the repository's
[architecture overview](../../docs/architecture.md) and
[decision records](../../docs/decisions/) (ADR-32 onward cover DuetsPad).
