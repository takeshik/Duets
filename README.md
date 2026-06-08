<div align="center">
  <img src="assets/duets-logo.svg" height="96" alt="Duets logo">
  &nbsp;
  <img src="assets/duets-text.svg" height="96" alt="Duets">
  <br>
  A library of Embeddable TypeScript console for .NET applications.
</div>

<br>
Duets lets you drop a fully-featured TypeScript REPL into **any** .NET application — desktop, mobile (iOS / Android / MAUI), game engines (Unity, Godot), servers, and everything in between. Use it for live debugging, runtime scripting, or as an in-app scripting language.

## Features

- **TypeScript transpilation & execution** — powered by [Jint](https://github.com/sebastienros/jint) running Babel (default) or the official TypeScript compiler in-process. No Node.js required.
- **Auto-generated type declarations** — expose .NET types to the editor and get IntelliSense-style completions via automatically generated `.d.ts` files. Attach .NET XML documentation (`JsDocProviders`) to include prose summaries, `@param`, and `@returns` annotations in the editor.
- **Browser debug pad (DuetsPad)** — a Monaco Editor frontend served over a built-in HTTP server, with an output canvas, execution timeline, and SSE-based live type declaration updates.
- **Zero heavy dependencies** — deliberately avoids ASP.NET Core / Kestrel. The built-in HTTP layer ([HttpHarker](src/HttpHarker/)) is a thin wrapper around `System.Net.HttpListener`, keeping the footprint minimal for embedding.

## Packages

Most users need `Duets.Jint`, which pulls in `Duets` automatically:

```
dotnet add package Duets.Jint
```

| Package | Targets | Description |
|---------|---------|-------------|
| [`Duets`](https://www.nuget.org/packages/Duets) | netstandard2.1; net8.0 | Core library: session, declarations, DuetsPad |
| [`Duets.Jint`](https://www.nuget.org/packages/Duets.Jint) | netstandard2.1; net8.0 | [Jint](https://github.com/sebastienros/jint) backend; depends on `Duets` |
| [`HttpHarker`](https://www.nuget.org/packages/HttpHarker) | netstandard2.1; net8.0 | Lightweight HTTP server (also available standalone) |

Pre-release builds are available on [nuget.tksk.io](https://nuget.tksk.io/).

To build from source, the repository requires the [.NET 10 SDK](https://dotnet.microsoft.com/) or later.

## Quick Start

Add `Duets.Jint` — no Node.js required:

```csharp
using var session = await DuetsSession.CreateAsync();
Console.WriteLine(session.Evaluate("Math.sqrt(2)")); // 1.4142135623730951
```

To call .NET types from TypeScript, enable CLR interop:

```csharp
using var session = await DuetsSession.CreateAsync(config => config
    .UseJint(opts => opts.AllowClr()));

session.Execute("typings.usingNamespace('System.IO')");

Console.WriteLine(session.Evaluate("""
    const files: string[] = Directory.GetFiles('.');
    files.map(f => Path.GetFileName(f)).join(', ')
    """));
```

To serve the DuetsPad browser debug pad with Monaco editor, live .NET type completions, Canvas, and Timeline:

```csharp
using var server = new HttpServer("http://127.0.0.1:17375/");
using var pad = server.UseContentTypeDetection().UseDuetsPad(configure: opts =>
    opts.SessionFactory = () => DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr())));
await server.RunAsync(); // open http://127.0.0.1:17375/
```

More examples in [`samples/`](samples/).

### Editor keybindings and output

| Key | Action |
|---|---|
| <kbd>Ctrl+Enter</kbd> | Evaluate the editor code |
| <kbd>F5</kbd> | Evaluate the editor code |

Output goes to DuetsPad's structured surfaces, not back into the editor:

- `dump(value)`, `console.*`, and rendering errors append to the **Timeline**.
- `canvas.add(...)`, `canvas.set(...)`, and `canvas.clear()` update the **Canvas**.
- The **Immediate** bar evaluates a single line on <kbd>Enter</kbd> and shows that result in the Timeline; it keeps no result of its own.

The editor's final evaluation result is **not** automatically appended to the Timeline — use `dump(value)` to record a value there.

## Project Structure

- `src/`
  - `Duets/` — Core library: session, declarations, transpiler interface, DuetsPad browser debug pad
  - `Duets.Jint/` — Jint backend: `JintScriptEngine`, `BabelTranspiler`, `TypeScriptService`, `ExtensionMethodRegistry`
  - `HttpHarker/` — Lightweight `HttpListener`-based HTTP server with middleware pipeline
  - `Duets.Sandbox/` — Multi-mode debugging CLI (run with `--help` or `batch` → `{"op":"help"}` for usage)
- `samples/` — Runnable file-based app examples
- `docs/` — [Architecture overview](docs/architecture.md) and [design decision records](docs/decisions/)
- `tests/`
  - `Duets.Tests/` — Unit tests
  - `HttpHarker.Tests/` — Unit tests for `HttpHarker`

## HttpHarker

A minimal HTTP server library built on `System.Net.HttpListener` with a middleware pipeline. See [src/HttpHarker/README.md](src/HttpHarker/README.md) for details.
