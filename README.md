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

Add a project reference to the `Duets` library, then wire up the services:

```csharp
using Duets;
using HttpHarker;

// Initialize the TypeScript service (downloads & caches the TS compiler on first run)
using var ts = new TypeScriptService();
await ts.ResetAsync();

// Create a script engine with access to selected .NET assemblies and the TypeScript transpiler
using var scriptEngine = new ScriptEngine(
    opts => opts.AllowClr(
        typeof(Math).Assembly,
        typeof(Enumerable).Assembly
    ),
    ts
);

// Expose .NET types to the TypeScript editor for completions
scriptEngine.SetValue("importTypeDefs", new Action<string>(typeName =>
{
    var type = Type.GetType(typeName)
        ?? throw new InvalidOperationException($"Type not found: {typeName}");
    ts.RegisterType(type);
}));

// Start the REPL web server
using var server = new HttpServer("http://127.0.0.1:17375/");
using var repl = server
    .UseContentTypeDetection()
    .UseRepl(ts, scriptEngine);

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
  - `Duets.Sandbox/` — Sample application
- `docs/` — [Architecture overview](docs/architecture.md) and [design decision records](docs/decisions/)
- `tests/`
  - `Duets.Tests/` — Unit tests

## HttpHarker

A minimal HTTP server library built on `System.Net.HttpListener` with a middleware pipeline. See [src/HttpHarker/README.md](src/HttpHarker/README.md) for details.
