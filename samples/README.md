# Samples

Runnable examples showing standard Duets usage patterns.

## Running a sample

Each file is a self-contained [file-based app](https://learn.microsoft.com/dotnet/csharp/fundamentals/program-structure/top-level-statements).
Run from the repository root:

```bash
dotnet run samples/minimal-eval.cs
dotnet run samples/with-type-registration.cs
dotnet run samples/web-repl.cs
```

Or from within this directory:

```bash
dotnet run minimal-eval.cs
dotnet run with-type-registration.cs
dotnet run web-repl.cs
```

## Samples

| File | Description |
|---|---|
| `minimal-eval.cs` | Minimal setup: `TypeScriptService` + `ScriptEngine` to transpile and evaluate TypeScript |
| `with-type-registration.cs` | Expose .NET types to scripts via `AllowClr` and the `typings` built-in |
| `web-repl.cs` | Browser-based Monaco editor with live completions served over HTTP |
