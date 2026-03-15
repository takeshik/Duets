# Duets

Embeddable TypeScript console for .NET applications.

Duets lets you drop a fully-featured TypeScript REPL into **any** .NET application — desktop, mobile (iOS / Android / MAUI), game engines (Unity, Godot), servers, and everything in between. Use it for live debugging, runtime scripting, or as an in-app scripting language.

## Features

- **TypeScript transpilation & execution** — powered by [Jint](https://github.com/sebastienros/jint) running the official TypeScript compiler in-process. No Node.js required.
- **Auto-generated type declarations** — expose .NET types to the editor and get IntelliSense-style completions via automatically generated `.d.ts` files.
- **Web-based REPL UI** — a Monaco Editor frontend served over a built-in HTTP server, with SSE-based live type declaration updates.
- **Zero heavy dependencies** — deliberately avoids ASP.NET Core / Kestrel. The built-in HTTP layer ([HttpHarker](src/HttpHarker/)) is a thin wrapper around `System.Net.HttpListener`, keeping the footprint minimal for embedding.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) or later

## Quick Start

Runnable examples live in [`samples/`](samples/). Each file is a self-contained file-based app:

```bash
dotnet run samples/minimal-eval.cs          # transpile and evaluate
dotnet run samples/with-type-registration.cs # expose .NET types to scripts
dotnet run samples/web-repl.cs              # browser-based Monaco editor
```

### Minimal: transpile and evaluate

[`samples/minimal-eval.cs`](samples/minimal-eval.cs) — The core of Duets is two classes: `TypeScriptService` (transpiler) and `ScriptEngine` (executor).

```csharp
using var ts = new TypeScriptService();
await ts.ResetAsync(); // downloads & caches typescript.js on first run

using var engine = new ScriptEngine(null, ts);

var result = engine.Evaluate("Math.sqrt(2)");
Console.WriteLine(result); // 1.4142135623730951
```

### With .NET type registration

[`samples/with-type-registration.cs`](samples/with-type-registration.cs) — Add `AllowClr` and register the `typings` built-in to expose .NET types to scripts and get IntelliSense-style completions:

```csharp
using var engine = new ScriptEngine(opts => opts.AllowClr(), ts);
engine.RegisterTypeBuiltins(ts); // registers the typings global

// From a script:
//   typings.useType(System.IO.File)                          // single type via CLR reference
//   typings.useType("System.IO.File, System.IO.FileSystem")  // via assembly-qualified name
//   typings.scanAssembly("System.Net.Http")                  // namespace skeletons only
//   typings.useAssembly("System.Net.Http")                   // all public types
//   typings.useNamespace(System.Net.Http)                    // types in one namespace
```

### With web REPL

[`samples/web-repl.cs`](samples/web-repl.cs) — Serve a browser-based Monaco editor with live completions:

```csharp
using var server = new HttpServer("http://127.0.0.1:17375/");
using var repl = server
    .UseContentTypeDetection()
    .UseRepl(ts, engine);

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
    - `TypeScriptService.cs` — TS compiler integration (transpile, completions)
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
