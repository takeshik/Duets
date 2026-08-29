<div align="center">
  <img src="assets/duets-logo.svg" height="96" alt="Duets logo">
  &nbsp;
  <img src="assets/duets-text.svg" height="96" alt="Duets">
  <br>
  A library of Embeddable TypeScript console for .NET applications.
</div>

<br>
Duets lets you drop a fully-featured TypeScript REPL into **any** .NET application — desktop, mobile
(iOS / Android / MAUI), game engines (Unity, Godot), servers, and everything in between. Use it for
live debugging, runtime scripting, or as an in-app scripting language.

## Features

- **TypeScript transpilation & execution** — powered by [Jint](https://github.com/sebastienros/jint)
  running Babel (default) or the official TypeScript compiler in-process. No Node.js required.
- **Auto-generated type declarations** — expose .NET types to the editor and get IntelliSense-style
  completions via automatically generated `.d.ts` files. Attach .NET XML documentation
  (`JsDocProviders`) to include prose summaries, `@param`, and `@returns` annotations in the editor.
- **Browser debug pad (DuetsPad)** — a Monaco Editor frontend served over a built-in HTTP server,
  with an output canvas, execution timeline, and SSE-based live type declaration updates.
- **Zero heavy dependencies** — deliberately avoids ASP.NET Core / Kestrel. The built-in HTTP layer
  ([HttpHarker](src/HttpHarker/)) is a thin wrapper around `System.Net.HttpListener`, keeping the
  footprint minimal for embedding.

## Packages

Most users need `Duets.Jint`, which pulls in `Duets` automatically:

```
dotnet add package Duets.Jint
```

| Package | Targets | Description |
|---------|---------|-------------|
| [`Duets`](https://www.nuget.org/packages/Duets) | netstandard2.1; net8.0 | Core library: session, declarations, transpiler interface |
| [`Duets.Jint`](https://www.nuget.org/packages/Duets.Jint) | netstandard2.1; net8.0 | [Jint](https://github.com/sebastienros/jint) backend; depends on `Duets` |
| [`Duets.Pad`](https://www.nuget.org/packages/Duets.Pad) | netstandard2.1; net8.0 | DuetsPad browser debug pad; depends on `Duets` and `HttpHarker` |
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

To serve the DuetsPad browser debug pad (package `Duets.Pad`) with Monaco editor, live .NET type
completions, Canvas, and Timeline:

```csharp
using var server = new HttpServer("http://127.0.0.1:17375/");
using var pad = server.UseContentTypeDetection().UseDuetsPad(configure: opts =>
    opts.SessionFactory = () => DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr())));
await server.RunAsync(); // open http://127.0.0.1:17375/
```

More examples in [`samples/`](samples/).

## Project Structure

- `src/`
  - `Duets/` — Core library: session, declarations, transpiler interface
  - `Duets.Pad/` — DuetsPad browser debug pad (Editor / Canvas / Timeline / Immediate)
  - `Duets.Jint/` — Jint backend: `JintScriptEngine`, `BabelTranspiler`, `TypeScriptService`,
    `ExtensionMethodRegistry`
  - `HttpHarker/` — Lightweight `HttpListener`-based HTTP server with middleware pipeline
  - `Duets.Sandbox/` — Multi-mode debugging CLI (run with `--help` or `batch` → `{"op":"help"}` for usage)
- `samples/` — Runnable file-based app examples, grouped per package
- `docs/` — [Documentation](docs/) including [current architecture](docs/architecture/) and
  [design decision records](docs/decisions/)
- `tests/`
  - `Duets.Tests/` — Unit tests
  - `Duets.Pad.Tests/` — Unit tests for `Duets.Pad`
  - `HttpHarker.Tests/` — Unit tests for `HttpHarker`

## DuetsPad

A browser debug pad served over HTTP: Monaco editor with live .NET type completions, output canvas,
execution timeline, and a `ui.*` builder surface for interactive controls. See
[src/Duets.Pad/README.md](src/Duets.Pad/README.md) for surfaces, keybindings, and the `ui.*` API.

## HttpHarker

A minimal HTTP server library built on `System.Net.HttpListener` with a middleware pipeline. See
[src/HttpHarker/README.md](src/HttpHarker/README.md) for details.
