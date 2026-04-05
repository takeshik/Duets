# Duets

Embeddable TypeScript console for .NET applications.

Duets lets you drop a fully-featured TypeScript REPL into **any** .NET application — desktop, mobile (iOS / Android / MAUI), game engines (Unity, Godot), servers, and everything in between. Use it for live debugging, runtime scripting, or as an in-app scripting language.

## Features

- **TypeScript transpilation & execution** — powered by [Jint](https://github.com/sebastienros/jint) running Babel (default) or the official TypeScript compiler in-process. No Node.js required.
- **Auto-generated type declarations** — expose .NET types to the editor and get IntelliSense-style completions via automatically generated `.d.ts` files.
- **Web-based REPL UI** — a Monaco Editor frontend served over a built-in HTTP server, with SSE-based live type declaration updates.
- **Zero heavy dependencies** — deliberately avoids ASP.NET Core / Kestrel. The built-in HTTP layer ([HttpHarker](src/HttpHarker/)) is a thin wrapper around `System.Net.HttpListener`, keeping the footprint minimal for embedding.

## Prerequisites

The library targets .NET Standard 2.1 and .NET 8.0 or later.
This repository requires [.NET 10 SDK](https://dotnet.microsoft.com/) or later.

## Quick Start

Runnable examples live in [`samples/`](samples/). Each file is a self-contained file-based app:

```bash
dotnet run samples/minimal-eval.cs          # transpile and evaluate
dotnet run samples/with-type-registration.cs # expose .NET types to scripts
dotnet run samples/console.cs              # script console output via ConsoleLogged
dotnet run samples/inspect-and-dump.cs     # util.inspect and dump
dotnet run samples/web-repl.cs              # browser-based Monaco editor
```

### Minimal: transpile and evaluate

[`samples/minimal-eval.cs`](samples/minimal-eval.cs) — `DuetsSession` is the single entry point. `CreateAsync` downloads and caches the transpiler on first run.

```csharp
using var session = await DuetsSession.CreateAsync();
var result = session.Evaluate("Math.sqrt(2)");
Console.WriteLine(result); // 1.4142135623730951
```

### With .NET type registration

[`samples/with-type-registration.cs`](samples/with-type-registration.cs) — Add `AllowClr` and call `RegisterTypeBuiltins()` to expose .NET types to scripts and get IntelliSense-style completions:

```csharp
using var session = await DuetsSession.CreateAsync(opts => opts.AllowClr());
session.RegisterTypeBuiltins();

// From a script:
//   typings.usingNamespace("System.IO")    // C# using semantics: scatter types as globals + completions
//   typings.importNamespace("System.IO")   // keep namespace prefix
//   typings.importType(System.IO.File)     // single type via CLR reference
//   typings.scanAssembly("System.Net.Http") // namespace skeletons only
//   typings.importAssembly("System.Net.Http") // all public types
```

### With web REPL

[`samples/web-repl.cs`](samples/web-repl.cs) — Serve a browser-based Monaco editor with live completions:

```csharp
using var session = await DuetsSession.CreateAsync(opts => opts.AllowClr());
session.RegisterTypeBuiltins();

using var server = new HttpServer("http://127.0.0.1:17375/");
using var repl = server
    .UseContentTypeDetection()
    .UseRepl(session);

await server.RunAsync();
```

Open `http://127.0.0.1:17375/` in a browser to access the TypeScript console.

### Editor Keybindings

| Key | Action |
|---|---|
| <kbd>Ctrl+Enter</kbd> | Evaluate and append result |
| <kbd>F5</kbd> | Clear output and evaluate |
| <kbd>Ctrl+L</kbd> | Clear output pane |

## Project Structure

- `src/`
  - `Duets/` — Core library
    - `DuetsSession.cs` — Top-level entry point; owns transpiler, engine, and declarations
    - `TypeScriptService.cs` — TypeScript compiler integration (transpile, completions)
    - `ScriptEngine.cs` — Jint engine wrapper for user code execution
    - `ClrDeclarationGenerator.cs` — .NET → `.d.ts` type declaration generator
    - `ReplService.cs` — Web REPL (Monaco UI, SSE, eval endpoint)
  - `HttpHarker/` — Lightweight `HttpListener`-based HTTP server
  - `Duets.Sandbox/` — Multi-mode debugging CLI (run with `--help` or `batch` → `{"op":"help"}` for usage)
- `samples/` — Runnable file-based app examples
- `docs/` — [Architecture overview](docs/architecture.md) and [design decision records](docs/decisions/)
- `tests/`
  - `Duets.Tests/` — Unit tests

## HttpHarker

A minimal HTTP server library built on `System.Net.HttpListener` with a middleware pipeline. See [src/HttpHarker/README.md](src/HttpHarker/README.md) for details.
