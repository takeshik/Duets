# Samples

Runnable examples showing standard usage patterns, grouped per package.

## Running a sample

Each file is a self-contained [file-based app](https://learn.microsoft.com/dotnet/csharp/fundamentals/program-structure/top-level-statements).
Run from the repository root:

```bash
dotnet run samples/<package>/<file>.cs
```

## Duets

Core library and the Jint backend.

| File | Description |
|---|---|
| `minimal-eval.cs` | Minimal setup: `DuetsSession.CreateAsync()` to transpile and evaluate TypeScript |
| `with-type-registration.cs` | Expose .NET types to scripts via `AllowClr` and the `typings` built-in |
| `extension-methods.cs` | Register and call CLR extension methods; convert CLR arrays to native JS arrays with `util.toJsArray()` |
| `server-side-completions.cs` | Server-side TypeScript completions without a browser |
| `console.cs` | Route script `console.log/warn/error` output via the `ConsoleLogged` event |
| `inspect-and-dump.cs` | Format values with `util.inspect`; use a local `tap` helper for non-breaking chain inspection |
| `repl-special-vars.cs` | REPL conveniences: `$_` (last result), `$exception`, and `GetGlobalVariables` |

## Duets.Pad

The DuetsPad browser debug pad. See [src/Duets.Pad/README.md](../src/Duets.Pad/README.md) for the pad's surfaces and `ui.*` API.

| File | Description |
|---|---|
| `duetspad.cs` | DuetsPad browser debug pad (Editor / Canvas / Timeline / Immediate) served over HTTP |
| `duetspad-ui.cs` | DuetsPad `ui.*` display and form-input surface: buttons, `ui.slot`, text/checkbox inputs, layout (header comment holds a copy-pasteable TypeScript demo) |
| `tagged-template-completion.cs` | Register a host-backed `path` tagged template with DuetsPad completions |

## HttpHarker

The standalone HTTP server library. See [src/HttpHarker/README.md](../src/HttpHarker/README.md) for the full middleware catalog.

| File | Description |
|---|---|
| `hello-http.cs` | Minimal HTTP server: routing middleware with path parameters and a POST echo endpoint |
