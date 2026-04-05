# Samples

Runnable examples showing standard Duets usage patterns.

## Running a sample

Each file is a self-contained [file-based app](https://learn.microsoft.com/dotnet/csharp/fundamentals/program-structure/top-level-statements).
Run from the repository root:

```bash
dotnet run samples/<file>.cs
```

## Samples

| File | Description |
|---|---|
| `minimal-eval.cs` | Minimal setup: `DuetsSession.CreateAsync()` to transpile and evaluate TypeScript |
| `with-type-registration.cs` | Expose .NET types to scripts via `AllowClr` and the `typings` built-in |
| `web-repl.cs` | Browser-based Monaco editor with live completions served over HTTP |
| `console.cs` | Route script `console.log/warn/error` output via the `ConsoleLogged` event |
| `inspect-and-dump.cs` | Format values with `util.inspect`; use `dump()` as a non-breaking tap in expression chains |
| `repl-special-vars.cs` | REPL conveniences: `$_` (last result), `$exception`, and `GetGlobalVariables` |
