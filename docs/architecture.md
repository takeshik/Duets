# Architecture Overview

Duets is an embeddable TypeScript console for .NET. It is designed to be added to any .NET application — including
mobile, game engines, and other constrained environments — for live debugging and runtime scripting. The scripting
language is TypeScript ([ADR-2](decisions/2_use-typescript-as-the-scripting-language.md)), which transpiles to
JavaScript at eval time.

## Core Design Constraint

**No ASP.NET Core / Kestrel dependency.** Duets must remain embeddable in hosts that cannot or should not pull in the ASP.NET Core stack (e.g. Unity, Godot, .NET iOS/Android). The HTTP layer is built on `System.Net.HttpListener` via the HttpHarker library ([ADR-3](decisions/3_use-httplistener-instead-of-asp-net-core-kestrel.md), [ADR-9](decisions/9_wrap-httplistener-in-a-dedicated-middleware-library.md)).

## Module Structure

### Duets (core library)

The main library consists of the following components:

- **DuetsSession** — Canonical entry point and top-level context ([ADR-25](decisions/25_session-as-canonical-entry-point.md), [ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)). Owns `TypeDeclarations`, the active `ITranspiler`, an `IScriptEngine`, a `JsDocProviders` instance, and a tagged-template completion registry as a unit. `CreateAsync(Action<DuetsSessionConfiguration>?)` accepts an optional configuration callback; when neither engine nor transpiler is specified, defaults registered in `DuetsBackendRegistry` are used automatically ([ADR-28](decisions/28_unified-createasync-api-and-backend-autodiscovery.md)). `RegisterTaggedTemplate` records host completion callbacks in the core registry and independently asks a supporting backend to install a runtime tagged-template function only when an evaluator is provided ([ADR-44](decisions/44_tagged-template-completion-rpc-boundary.md)).
- **DuetsBackendRegistry** — Static registry for default engine and transpiler factories ([ADR-28](decisions/28_unified-createasync-api-and-backend-autodiscovery.md)). Backend packages register their defaults via `[ModuleInitializer]`-annotated methods on assembly load. `DuetsSession.CreateAsync` falls back to these defaults when no explicit configuration is provided.
- **TypeDeclarations** — Thread-safe, transpiler-agnostic runtime store for type declarations ([ADR-25](decisions/25_session-as-canonical-entry-point.md)). Owns CLR type registration, namespace placeholders, raw `.d.ts` registration, and change notifications. Exposes two narrow views: `ITypeDeclarationProvider` (snapshot + change events) and `ITypeDeclarationRegistrar` (registration commands). Uses `ClrDeclarationGenerator` internally.
- **ClrDeclarationGenerator** — Uses reflection to generate TypeScript type declarations (`.d.ts`) from .NET types. Accepts an optional `IJsDocProvider` to annotate members with prose documentation sourced from .NET XML doc comments. Called by `TypeDeclarations` when a CLR type is registered ([ADR-8](decisions/8_use-addextralib-to-inject-dts-declarations-for-completions.md), [ADR-29](decisions/29_jsdoc-provider-abstraction.md)).
- **JsDocProviders / IJsDocProvider** — Composite registry of documentation providers ([ADR-29](decisions/29_jsdoc-provider-abstraction.md)). Tries registered providers in order and returns the first non-null result; isolates per-provider exceptions. Raises `ProviderAdded` so that `DuetsSession` can trigger `TypeDeclarations.RefreshDeclarations` when new documentation becomes available.
- **XmlDocumentationProvider** — `IJsDocProvider` backed by a .NET XML documentation file ([ADR-29](decisions/29_jsdoc-provider-abstraction.md)). Can download and cache a NuGet nupkg to extract the XML file, selecting the best TFM match and a specific assembly name in multi-assembly packages.
- **ITranspiler** — Engine-neutral transpilation boundary ([ADR-10](decisions/10_extract-itranspiler-interface-for-scriptengine.md), [ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)). Concrete implementations may be hosted by different JavaScript runtimes or replaced by future wasm-backed approaches.
- **IScriptEngine** — Runtime-neutral execution contract ([ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md), [ADR-31](decisions/31_scriptengine-generic-backend-base-and-iscriptengine.md)). Interface held by callers (`DuetsSession`, `DuetsPadSession`, factory delegates). `Execute` and `Evaluate` always transpile before running, track `$_` and `$exception`, expose console events, and surface runtime values through `ScriptValue` instead of engine-specific value types. Backend packages implement `ScriptEngine<TValue>`, not `IScriptEngine` directly.
- **ScriptEngine&lt;TValue&gt;** — Generic abstract base for backend implementations ([ADR-31](decisions/31_scriptengine-generic-backend-base-and-iscriptengine.md)). Holds an `IScriptValueConverter<TValue>` and uses it to implement `SetValue(string, ScriptValue)` and `Evaluate*` result wrapping concretely; backends only implement the engine-specific hooks (`SetValue(string, TValue)`, `EvaluateJs`, `ExecuteJs`, etc.).
- **IScriptValueConverter&lt;T&gt;** — Bidirectional converter between `ScriptValue` and a backend's internal value type ([ADR-31](decisions/31_scriptengine-generic-backend-base-and-iscriptengine.md)). `Wrap(T)` produces a `ScriptValue`; `Unwrap(ScriptValue)` recovers the internal value. The two directions are kept in one interface because `T` is fixed per backend and variance provides no practical benefit.
- **ScriptValue** — Runtime-neutral wrapper around a JavaScript value ([ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md), [ADR-30](decisions/30_scriptvalue-redesign-abstract-class-and-jstype.md)). Abstract class; backend packages provide concrete subclasses (e.g. `JintScriptValue`). Exposes `ToObject` and `ToString`. `==`/`!=` operators work correctly across sentinel (`ScriptValue.Undefined`, `ScriptValue.Null`) and engine-backed values; cross-backend comparisons throw.
- **ScriptByteBuffer** — Explicit, single-use ownership-transfer envelope for host-produced binary data ([ADR-51](decisions/51_explicit-ownership-transfer-for-script-byte-buffers.md)). Backends may consume its exclusively owned `byte[]` into a native mutable byte-buffer representation without changing the default interop behavior of ordinary CLR arrays or `ReadOnlyMemory<byte>` and without making a defensive copy.

### Duets.Pad (browser debug pad)

DuetsPad ships as its own package, `Duets.Pad`, which depends on `Duets` and HttpHarker; the core library
carries neither the HTTP layer nor the pad's embedded web assets
([ADR-48](decisions/48_extract-duets-pad-into-its-own-package.md)). The `Duets.Pad` namespace predates the
split and is unchanged.

DuetsPad supersedes `ReplService`, reuses the Monaco and TypeScript infrastructure from the browser REPL, and provides the top-level surfaces defined by ADR-32 ([ADR-32](decisions/32_duetspad-supersede-replservice-with-new-output-model.md), [ADR-7](decisions/7_use-monaco-editor-as-the-browser-based-repl-ui.md)). `DuetsPadService` is a thin HTTP router that attaches to an `HttpServer` via `UseDuetsPad` and delegates to three collaborators: **`SessionRegistry`** owns the session table, server-issued identifiers, create/lookup/delete, disposal, and idle reclamation (cleanup timer + idle sweep); **`AssetProvider`** owns static-asset acquisition, caching, serving, and the Tabler Icons CSS rewrite; and **`SseTransport`** is the single SSE streaming primitive behind the one multiplexed per-session event stream (`GET /sessions/{sessionId}/events`) that carries the canvas, timeline, dialog, type-declaration, tagged-template tag snapshots, and control events, rather than one stream per surface ([ADR-36](decisions/36_duetspad-server-canonical-output-protocol.md), [ADR-44](decisions/44_tagged-template-completion-rpc-boundary.md)). `/sessions/{sessionId}/complete` is a bounded RPC endpoint to registered tagged-template completion providers: the Monaco client uses Monaco tokenization plus narrow helper logic to decide when to ask and sends explicit segment context, while the server validates the registered tag, normalizes the single accepted raw segment, enforces resource limits, caps results, and checks segment-relative replacement spans without parsing JavaScript source ([ADR-44](decisions/44_tagged-template-completion-rpc-boundary.md)). Each `DuetsPadSession` is slimmed to the eval gate, state nucleus, and subscriber fan-out: it wraps one `DuetsSession` and owns its named canvases (the `canvases` script global exposes `get(name)` with getOrAdd semantics; the `canvas` global aliases the always-present `"default"` canvas), Timeline, dialogs, object renderers, script globals, SSE subscribers, and a per-session interaction store (projected interactions keyed by their owning surface), while construction-time script-global wiring is performed by **`SessionBootstrap`** and rendering funnels through a single `TryRenderContent` entry point ([ADR-34](decisions/34_duetspad-session-ownership-and-isolation.md)). Sessions are disposed explicitly via `DELETE /sessions/{sessionId}` and reclaimed by the registry after a configurable idle timeout (evaluation and SSE keepalive/stream activity count as activity); disposed identifiers are never reused and browser disconnect alone does not dispose a session ([ADR-38](decisions/38_duetspad-session-lifecycle.md)). Canvas, Timeline, and Dialog state is authoritative on the server, represented as reduced render nodes, and projected to the browser over namespaced SSE protocol events ([ADR-35](decisions/35_duetspad-rendering-model.md), [ADR-36](decisions/36_duetspad-server-canonical-output-protocol.md), [ADR-52](decisions/52_duetspad-modal-dialog-surface.md)). A session may hold multiple named canvases; the `canvas.snapshot`/`canvas.replace` events carry a `name` field and the initial burst emits one snapshot per canvas, while the browser Canvas pane is tabbed (sub-tabs in split view, promoted to flat top-level tabs in tabbed view) ([ADR-43](decisions/43_duetspad-named-multi-canvas.md)). CLR values are rendered through a `RenderContext` that carries per-call dump options (`DumpOptions` — `MaxDepth`=5/`MaxItems`=1000 limits) and centralizes depth limiting and cycle detection in the dispatch step; rendering produces `DisplayContent` (a terminal body plus its interactions), keeping the stored render-node tree display-only; `dump` is a DuetsPad-only global (core Duets surfaces values as the evaluation result plus `console`/`util.inspect`) ([ADR-35](decisions/35_duetspad-rendering-model.md)). `ui.button` and similar attach server-side handlers that the browser triggers by opaque handler id via `POST /sessions/{sessionId}/interactions/{handlerId}/invoke` (answered `stale` when the handler's output has already been retired), and the interaction store releases handlers when their Canvas, Timeline, or Dialog output is replaced, trimmed, closed, or the session is disposed ([ADR-41](decisions/41_duetspad-interaction-model.md), [ADR-52](decisions/52_duetspad-modal-dialog-surface.md)). A `pad` script global lets scripts operate the pad itself: `pad.resetSession`, `pad.openText`, and `pad.setEditorText` enqueue `control.*` commands buffered during a run and flushed afterward under the eval gate, over the same multiplexed stream; reset is browser-driven as a no-reload session swap, and `openText` presents an open action (the text handed off out-of-band via a one-shot key, never in the URL) with a popup-block toast fallback ([ADR-42](decisions/42_duetspad-pad-control-surface-and-command-channel.md)). The Timeline is bounded by an opt-in entry-count quota (`TimelineEntryLimit`, `null` = unlimited): the server drops oldest entries after an append and emits `timeline.append` then `timeline.trim`, preserving entry-id monotonicity so the browser projection converges ([ADR-39](decisions/39_duetspad-timeline-quota-policy.md)).

DuetsPad Canvas projection is revisioned per canvas. Full-state events establish baselines;
each `canvas.patch` advances the revision by exactly one (contiguous) and carries incremental operations plus the full visible interaction set;
the browser applies patches with preflight-then-mutate atomicity; and canvas-scoped resync uses the
same snapshot payload shape when a gap or malformed event is detected ([ADR-45](decisions/45_duetspad-canvas-incremental-patch-protocol.md)).

Mutable content that updates in place (currently the `ui.slot` handle: a `DisplaySlot` whose `content` can be reassigned) locates its placements by **marker search over authoritative state** rather than a maintained placement index ([ADR-46](decisions/46_placement-discovery-for-mutable-projected-content.md)). Such content renders as a `data-duetspad-slot` marker element; an update searches every `CanvasState` tree and Timeline entry body for the marker, replaces the marked subtree, and re-projects via the existing ADR-45 Canvas projection path (a `canvas.patch`, or a full replace when the patch would not be smaller) and the ADR-36 `timeline.update` path, rebasing any interactions inside it. Identity lives on the handle, so render nodes stay immutable.

Form-input values are **server-canonical session state** held in a per-session field store keyed by an identity on the input handle (the same handle-owned identity model as `ui.slot`), and are the one state class where the browser is not a pure projection but a **second writer**: it commits a control's value to the server on focus-out, and folds a field-value snapshot into the ADR-41 invoke POST body so a click handler observes the latest edit regardless of blur timing, without an HTML form submit ([ADR-47](decisions/47_duetspad-form-input-state-model.md)). Script-side `handle.value` is read-write — reads are session-scoped, and writes mutate the store and project through the ADR-45 `canvas.patch` path (ADR-46 marker search), with the browser projection extended to set live DOM properties (`value`/`checked`/`selectedIndex`), not only attributes. Values are strings with no coercion or validation (a checkbox is `"True"`/`"False"`), never guaranteed valid for their control (a dropdown/radio value outside the current options is retained though a `<select>` cannot display it), and retained across incremental patches, SSE reconnect, and option-list changes but reset on a full canvas rebuild — an input shares the lifetime of its rendered output, as an interaction does (ADR-41). This amends ADR-36's browser-is-a-projection invariant for this one state class and extends the ADR-41 invoke body.

File attachments use a dedicated `ui.filePicker` handle and a server-canonical attachment store rather than ADR-47's string field value ([ADR-50](decisions/50_duetspad-file-attachment-state-and-upload-protocol.md)). Each browser selection is a server-revisioned transaction: a metadata manifest carries a persisted browser-client ordering generation and reserves bounded resources, file bodies stream individually into staging without holding the eval gate, and a token-checked final commit atomically replaces the prior selection. Rejecting an older generation before it invalidates pending state makes rapid reselection deterministic even when concurrent begin requests reach the eval gate out of order. Before any server-side interaction runs, the browser waits until its current local-generation map is empty and the server prunes unreachable picker state, requires a complete retained-picker revision snapshot, and validates every revision under the eval gate, so handlers never observe partial or superseded uploads. A failed projection remains blocking but exposes a revision-conditioned cancellation action that works after reload without persisting its opaque selection token. Picker retention reuses the existing `data-duetspad-field` Canvas/Timeline/Dialog reachability scan; rendering a picker establishes its store entry, and prune-before-invoke removes abandoned speculative entries. Already-open reads hold path-hiding stream leases. `openRead()` exposes the ordinary generated `System.IO.Stream` declaration, while `readAllText()` and `readAllBytes()` provide opt-in whole-file convenience paths; the latter transfers one allocation to Jint as a native `Uint8Array` through ADR-51. Storage is pluggable with a temporary-file default and finite per-file byte, per-session byte, and per-session file-count ceilings; quota remains charged until physical deletion succeeds, and transient deletion failures retry while the session is live. Session disposal drains storage in a background cleanup task and bounds the synchronous wait with `AttachmentStorageDrainTimeout`, never force-disposing storage against an outstanding operation; contained disposal failures remain observable through `SessionDisposalErrorHandler`. Native file-input contents remain ephemeral, and only the committed attachment list survives projection or reconnect.

Modal dialogs are a third server-canonical projected surface rather than transient `control.*` commands ([ADR-52](decisions/52_duetspad-modal-dialog-surface.md)). `ui.dialog` accepts arbitrary `DisplayContent` and continues through a later result callback because the synchronous evaluation gate cannot wait for a future browser request. Active dialogs own their render trees, interactions, fields, slots, and attachments; revisioned `dialog.snapshot`/`open`/`patch`/`replace`/`close` events preserve them across reconnects and mutable updates. The browser presents the ordered active set as one accessible FIFO modal queue, while the server atomically claims each result once and retires all dialog-owned state on close, reset, or disposal.

Because evaluation is remote code execution by design, DuetsPad's intended LAN exposure is governed by an explicit threat model — hostile LAN peers, CSRF drive-by, DNS rebinding, and compromised CDN-backed frontend assets ([ADR-49](decisions/49_duetspad-access-control-and-resource-hardening.md)). Authentication is a single pluggable `Authenticate` handler on `DuetsPadServiceOptions` (`null` = no authentication, documented as loopback-only with an exact-host prefix; `DuetsPadAuthenticator.Token` supplies the built-in constant-time fixed-token handler). It is applied by a middleware ahead of the router that gates the whole `/sessions` subtree **by path** — never the static UI assets, which must load to present the token prompt — so that a route added later cannot be fail-open. The credential is carried explicitly in an `Authorization: Bearer` header on every request, chosen over Basic/cookie schemes as defence in depth: their browser-automatic (ambient) credential attachment would leave session-id secrecy as the sole barrier against drive-by RCE, and DNS rebinding removes that barrier outright. This required replacing `EventSource` with a fetch-based SSE reader in the browser client (manual reconnection; token delivered via the URL fragment into `sessionStorage`, never sent to the server). Because the token is worth host RCE, the default CDN-backed Monaco/Tabler assets — which execute in the pad page — are inside the trust boundary of an authenticated deployment. Resource ceilings hold independently of authentication: `MaxSessions` caps concurrent sessions through an atomic reservation taken before the async session factory (429 beyond), `MaxActiveDialogs` caps retained modal surfaces per session, a bounded body reader enforces `MaxRequestBodyBytes` on control-message POST bodies by streaming rather than trusting `Content-Length` (413 beyond, after a byte- and time-bounded drain so the status is deliverable), raw attachment uploads use ADR-50's dedicated per-file and aggregate bounds, and `IdleTimeout` defaults to 30 minutes. TLS is a reverse-proxy responsibility; Host-header validation is deferred with rationale.

### Duets.Jint

The Jint integration package provides the Jint-backed runtime implementation
([ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)):

- **JintScriptEngine** — Concrete `ScriptEngine<JsValue>` backed by Jint. Manages the user script execution environment, CLR interop via `AllowClr`, and wires `ExtensionMethodRegistry` into the Jint `MemberAccessor` hook ([ADR-26](decisions/26_extension-method-support-via-member-accessor-hook.md)).
- **ScriptByteBufferObjectConverter** — Consumes the explicit core ownership envelope as a JavaScript `Uint8Array` over the same managed array, avoiding a defensive copy while leaving ordinary CLR arrays and `ReadOnlyMemory<byte>` unchanged ([ADR-51](decisions/51_explicit-ownership-transfer-for-script-byte-buffers.md)).
- **TypeScriptService** — Hosts the official TypeScript compiler (`typescript.js`) in a dedicated Jint engine instance separate from user code, providing both transpilation and server-side completions ([ADR-5](decisions/5_separate-jint-engines-for-typescript-compiler-and-user-code.md), [ADR-12](decisions/12_language-service-host-rewrite-and-nolib.md)).
- **BabelTranspiler** — `ITranspiler` implementation backed by `@babel/standalone` running in Jint; the forward-compatibility path for TypeScript 7 ([ADR-19](decisions/19_babel-transpiler-as-typescript-7-migration-path.md)).
- **ScriptTypings** — Provides the `typings` global object in the script environment, exposing type registration APIs (`importType`, `importAssembly`, `usingNamespace`, `addExtensionMethods`, etc.) ([ADR-13](decisions/13_script-built-ins-and-typings-object.md), [ADR-24](decisions/24_typings-api-redesign.md)).
- **ExtensionMethodRegistry** — Thread-safe registry for runtime extension method dispatch via Jint's `MemberAccessor` hook ([ADR-26](decisions/26_extension-method-support-via-member-accessor-hook.md)).
- **DuetsSessionConfigurationExtensions** — Provides `UseJint()` and `UseBabel()` on `DuetsSessionConfiguration` ([ADR-28](decisions/28_unified-createasync-api-and-backend-autodiscovery.md)). `UseJint()` selects the Jint engine; `UseBabel()` selects the Babel transpiler. Both are optional when `JintBackendInitializer` has already registered the defaults.
- **JintBackendInitializer** — Registers `JintScriptEngine` and `BabelTranspiler` as the default engine and transpiler in `DuetsBackendRegistry` via `[ModuleInitializer]`, enabling zero-configuration `DuetsSession.CreateAsync()` for any caller that references `Duets.Jint` ([ADR-28](decisions/28_unified-createasync-api-and-backend-autodiscovery.md)).

### HttpHarker (HTTP server library)

A lightweight HTTP server built on `System.Net.HttpListener` with a middleware pipeline ([ADR-9](decisions/9_wrap-httplistener-in-a-dedicated-middleware-library.md)). It is a separate library with its own namespace and may be extracted into its own repository in the future. See [../src/HttpHarker/README.md](../src/HttpHarker/README.md) for details.

### Duets.Sandbox (developer / agent debugging CLI)

An internal console application for end-to-end verification of the Duets stack.
It is not intended for end users or as a deliverable ([ADR-11](decisions/11_sandbox-multi-mode-debugging-cli.md), [ADR-16](decisions/16_samples-directory-and-sandbox-role-clarification.md)). All commands run against a fully-initialized TypeScript engine with stdlib, `typings` built-ins, and `AllowClr`. Modes:

| Mode | Invocation | Description |
|---|---|---|
| `repl` | *(default)* | Interactive REPL; TypeScript lines are evaluated, `:commands` manage state |
| `complete` | `complete <src> [--position n]` | One-shot completions at position; outputs a JSON object |
| `serve` | `serve [--port n] [--auth]` | Starts the DuetsPad web server; `--auth` generates and prints an access-token URL; blocks until Ctrl+C |
| `batch` | `batch` | JSONL in → JSONL out; agent-friendly stateful session |

The batch mode is designed for use by AI coding agents: the agent writes a sequence of JSON operation objects to stdin and reads JSON results from stdout, with no background process management required.

### samples/ (usage examples)

Runnable file-based app examples showing standard library usage, grouped per package — `samples/Duets/`, `samples/Duets.Pad/`, and `samples/HttpHarker/` ([ADR-16](decisions/16_samples-directory-and-sandbox-role-clarification.md), layout refined by [ADR-48](decisions/48_extract-duets-pad-into-its-own-package.md)). Each file is self-contained and executable via `dotnet run samples/<package>/<file>.cs`. These are the recommended starting point for new users.

## Data Flow

### Eval (`POST /sessions/{sessionId}/eval`)

```mermaid
flowchart LR
    U["User\n(Monaco Editor)"]
    PS[DuetsPadService]
    TS["ITranspiler\n(runtime-hosted)"]
    SE["IScriptEngine\n(runtime backend)"]

    U -->|"POST /sessions/{id}/eval\nTypeScript source"| PS
    PS -->|Transpile| TS
    TS -->|JavaScript source| PS
    PS -->|Evaluate| SE
    SE -->|result / error| PS
    PS -->|"JSON { result, ok }"| U
```

### Type Registration (`type.declaration` events on `SSE /sessions/{sessionId}/events`)

```mermaid
flowchart LR
    Host["Host app\nRegisterType(typeof(T))"] -->|register| TD[TypeDeclarations]
    TD -->|generate| CG["ClrDeclarationGenerator\n.NET type → .d.ts"]
    TD -->|"change event"| Monaco["Monaco Editor\n(addExtraLib)"]
    TD -->|"optional mirror"| TS["TypeScriptService\n(language service)"]
```

## Runtime Dependencies

TypeScript compiler (`typescript.js`), Monaco Editor loader (`loader.js`), and optionally the ES5 standard library
(`lib.es5.d.ts`) are fetched from unpkg on first use and cached in the system temp directory for 7 days
([ADR-6](decisions/6_fetch-and-cache-runtime-js-assets-from-cdn.md), [ADR-18](decisions/18_pluggable-asset-source-abstraction.md)).
This avoids bundling large JS files in the library assembly. `lib.es5.d.ts` is only fetched when a runtime-hosted
`TypeScriptService` injects it for server-side completions ([ADR-12](decisions/12_language-service-host-rewrite-and-nolib.md)).

## Versioning and CI

Versions are managed by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) ([ADR-23](decisions/23_ci-and-package-publishing.md)). Releases are triggered by `v{major}.{minor}.{patch}` Git tags and publish a NuGet package to GitHub Packages. Development builds carry a `-dev.{height}+g{commit}` prerelease suffix (SemVer 2.0).

## Key Dependencies

| Package | Role |
|---|---|
| [Jint](https://github.com/sebastienros/jint) | JavaScript runtime backend used by `Duets.Jint` ([ADR-4](decisions/4_use-jint-as-the-javascript-engine.md), [ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)) |
| [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) | Automated versioning from Git history and tags ([ADR-23](decisions/23_ci-and-package-publishing.md)) |
