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

The pad presents five surfaces:

- **Editor** — a Monaco editor with TypeScript completions for the .NET types registered in the
  session. Each browser tab gets its own isolated server-side session.
- **Canvas** — persistent display state, updated in place. A session can hold multiple named
  canvases, each shown as its own tab.
- **Timeline** — append-only execution history: `dump` output, `console.*` output, evaluation
  results from the Immediate bar, and errors.
- **Modal** — server-canonical modal content for multi-step interactions, including form input and
  explicit footer actions.
- **Immediate** — a single-line expression bar; results are recorded in the Timeline.

### Editor keybindings

| Key | Action |
|---|---|
| <kbd>Ctrl+Enter</kbd> | Evaluate the editor code |
| <kbd>F5</kbd> | Evaluate the editor code |

### Output rules

Output goes to the pad's structured surfaces, not back into the editor:

- `value.dump()`, `console.*`, and rendering errors append to the **Timeline**.
- Object dump headers can collapse their own table or their entire nested table subtree.
- `canvas.add(...)`, `canvas.set(...)`, and `canvas.clear()` update the **Canvas**. `canvas` is the
  default canvas; `canvases.get(name)` returns a named canvas (created on first access) with the
  same surface, each shown as its own Canvas tab.
- The **Immediate** bar evaluates a single line on <kbd>Enter</kbd> and shows that result in the
  Timeline; it keeps no result of its own.

The editor's final evaluation result is **not** automatically appended to the Timeline — use
`value.dump()` to record a value there. The fluent method returns the original value with its
concrete type, so chains such as `query.where(...).dump().select(...)` retain completions. The
equivalent global `dump(value)` remains available for `null`, `undefined`, null-prototype objects,
and values whose own `dump` member shadows DuetsPad's method.

## Building UI with `ui.*`

Beyond plain values, the pad exposes a `ui.*` surface for buttons, form inputs, layout, transient
toast notifications, and a mutable `ui.slot` handle whose content can be reassigned in place:

```typescript
const name = ui.textBox({ placeholder: "Your name" });
const greeting = ui.slot(ui.text("..."));
canvas.set(ui.stack([name, ui.button("Greet", () => {
  greeting.content = ui.text(`Hello, ${name.value}!`);
})]));
```

Click and other handlers run server-side, and an input's `.value` always reflects the
server-canonical current value rather than a client-side echo.

`ui.toast(message, options?)` shows a transient browser notification after the current evaluation
or interaction handler completes. Options include `title`, `variant` (`info`, `success`, `warning`,
or `danger`), and `durationMs` (default 5000, accepted range 0–600000; use 0 to keep the toast until
dismissed). Toasts are ephemeral and are not replayed after an SSE reconnect.

`ui.modal(body, onResult, options?)` opens a modal whose body accepts the same `ui.*` content as
Canvas, including inputs, slots, buttons, and file pickers. The opening evaluation does not block;
`onResult` runs in the later interaction turn with `{ reason, actionId }`, after the latest field
snapshot has been committed. Footer `buttons` close the modal, while ordinary buttons in the body
may update its content without closing it. Active modals are restored after an SSE reconnect.
If the body cannot be rendered, DuetsPad appends a `render-error` Timeline entry and returns a
handle whose `isOpen` is already `false`.
An empty `buttons` list combined with `dismissButtonId: null` intentionally creates a
programmatic-only waiting modal; retain the returned handle and call `.close()` to dismiss it.

```typescript
const alias = ui.textBox({ placeholder: "Display name" });
ui.modal(
  ui.stack([ui.label("Display name"), alias]),
  result => {
    if (result.reason === "action" && result.actionId === "save") {
      ({ alias: alias.value }).dump();
    }
  },
  {
    title: "Profile",
    buttons: ["Cancel", { id: "save", label: "Save", variant: "primary" }],
    defaultButtonId: "save",
    dismissButtonId: "Cancel",
  },
);
```

Available builders include text and layout primitives (`ui.text`, `ui.label`, `ui.code`,
`ui.preformatted`, `ui.disclosure`, `ui.stack`, `ui.row`/`ui.col`, `ui.card`, `ui.divider`),
compact diagnostics (`ui.dataGrid`, `ui.emptySpace`), indicators (`ui.badge`, `ui.alert`,
`ui.spinner`, `ui.status`, `ui.icon`, `ui.progress`), tables and links (`ui.table`, `ui.link`),
interactions (`ui.button`), notifications and modal flow (`ui.toast`, `ui.modal`), form inputs (`ui.textBox`, `ui.textArea`, `ui.numberBox`, `ui.checkBox`,
`ui.dropDown`, `ui.slider`, `ui.radioGroup`, `ui.filePicker`), the in-place `ui.slot` handle, and raw escape
hatches (`ui.element`, `ui.rawHtml`). See [`samples/Duets.Pad/duetspad-ui.cs`](../../samples/Duets.Pad/duetspad-ui.cs)
for a copy-pasteable demo script.

`ui.dataGrid([{ label, content }])` accepts any renderable value as item content, including
interactive controls. `ui.emptySpace(title, { message, icon, action })` provides a compact no-data
state without introducing page-level dashboard concepts. `ui.code` renders semantic `<pre><code>`
while `ui.preformatted` renders `<pre>`; both preserve input as text and accept `{ wrap: true }` for
long lines. `ui.disclosure(summary, content, { open })` uses native `<details>` browser-local view
state, so its current open state resets when the enclosing output is fully replaced.

`ui.filePicker({ multiple: true })` uploads each browser selection as one atomic transaction. A
server-side button waits for all current uploads before its handler runs, and the handler sees only
the fully committed selection through `picker.files`. Each file exposes sanitized `name`, untrusted
`contentType`, `size`, a leased `openRead()` .NET stream, `readAllText()`, and `readAllBytes()` (a
`Uint8Array` under Jint); dispose every stream opened directly. The whole-file helpers are convenient
for bounded files, while host streaming APIs should consume larger files through `openRead()`. The
native file input itself is ephemeral, while the committed list survives Canvas patches and
reconnects. If a selection fails, its projected error includes a cancellation button that remains
usable after a browser reload. Attachment quota is released after physical storage deletion succeeds,
so clearing and immediately reselecting at the limit can briefly receive a quota rejection.

## Security

The pad executes whatever the browser sends — evaluation is remote code execution on the host by
design. Decide your exposure deliberately (ADR-49):

- **No authentication configured (the default) assumes loopback-only exposure**, and specifically an
  exact-host prefix: `new HttpServer("http://127.0.0.1:17375/")`. Then anyone who can reach the port
  is you. Do not combine the default with a wildcard prefix (`http://+:17375/`): that accepts any
  `Host` header, which lets a malicious web page reach the pad by DNS rebinding — as a same-origin
  page, so it can read a session id and execute code on your machine.
- **For LAN exposure** (real devices — phones, game consoles — connecting to your dev machine),
  configure an access token:

  ```csharp
  opts.Authenticate = DuetsPadAuthenticator.Token("some-long-random-token");
  ```

  Then open the pad as `http://<host>:<port>/#token=some-long-random-token`. The token travels in
  the URL fragment (never sent to the server, never logged), is kept in `sessionStorage`, and is
  attached to every API request as an `Authorization: Bearer` header. Without a valid token the UI
  loads but every session operation is rejected with `401`, and the pad shows a token prompt.
  See [`samples/Duets.Pad/duetspad-access-token.cs`](../../samples/Duets.Pad/duetspad-access-token.cs)
  for a runnable end-to-end example.

  `Authenticate` accepts any `Func<DuetsPadAuthenticationContext, ValueTask<bool>>` if you need a
  custom scheme (for example validating against your own service). Note that its `RemoteEndPoint`
  is the direct socket peer — behind a reverse proxy every request appears to come from the proxy,
  so do not build an IP allowlist on it without a trusted-proxy story of your own.
- **TLS is not provided.** Over plain HTTP a LAN sniffer can capture the token; terminate TLS in a
  reverse proxy in front of the pad if that matters for your network.
- **The frontend assets are part of the trust boundary once a token is in play.** By default the
  Monaco loader/editor and Tabler assets are fetched from a CDN and run in the pad page, where they
  can read the token. If you authenticate the pad, consider pointing `MonacoLoader`, `MonacoBaseUrl`,
  `TablerCss`, `TablerIconsCss`, and `TablerIconsFont` at self-hosted or pinned copies.

Resource ceilings apply regardless of authentication: `MaxSessions` (default 16) caps concurrent
sessions, `MaxRequestBodyBytes` (default 1 MiB) caps control-message request bodies,
`MaxAttachmentBytesPerFile` (default 16 MiB), `MaxAttachmentBytesPerSession` (default 64 MiB), and
`MaxAttachmentsPerSession` (default 32) bound attachment storage, `MaxActiveModals` (default 8)
caps retained modals, and `IdleTimeout` (default 30 minutes) reclaims abandoned sessions — a
session with a live pad tab is never reclaimed.

## Configuration highlights

`UseDuetsPad(configure: opts => ...)` exposes `DuetsPadServiceOptions`, including:

- `SessionFactory` — creates the `DuetsSession` behind each browser session.
- `Authenticate` — optional request authentication handler; see [Security](#security).
- `MaxSessions` / `MaxActiveModals` / `MaxRequestBodyBytes` — resource ceilings; see
  [Security](#security).
- `MaxAttachmentBytesPerFile` / `MaxAttachmentBytesPerSession` / `MaxAttachmentsPerSession` —
  attachment ceilings; `AttachmentStorageFactory` replaces the per-session temporary-file store.
- `AttachmentStorageDrainTimeout` — bounds synchronous session disposal while a non-responsive
  custom attachment store continues draining in background (default 30 seconds).
- `SessionDisposalErrorHandler` — observes contained session-disposal failures, including attachment
  drain timeouts, with the affected session id.
- `TimelineEntryLimit` — optional cap on retained Timeline entries (`null` = unlimited).
- `IdleTimeout` — automatic reclamation of idle sessions (default 30 minutes; `null` disables).
- `ObjectRenderers` / `DumpOptions` — customize how values are rendered.
- `MonacoLoader`, `TablerCss`, `TablerIconsCss`, `TablerIconsFont`, `MonacoBaseUrl` — pluggable
  asset sources for offline or custom-hosted frontend assets.

## Architecture

Design decisions and the rendering/protocol model are documented in the repository's
[architecture overview](../../docs/architecture.md) and
[decision records](../../docs/decisions/) (ADR-32 onward cover DuetsPad).
