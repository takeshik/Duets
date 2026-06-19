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
| `extension-methods.cs` | Register and call CLR extension methods; convert CLR arrays to native JS arrays with `util.toJsArray()` |
| `duetspad.cs` | DuetsPad browser debug pad (Editor / Canvas / Timeline / Immediate) served over HTTP |
| `tagged-template-completion.cs` | Register a host-backed `path` tagged template with DuetsPad completions |
| `console.cs` | Route script `console.log/warn/error` output via the `ConsoleLogged` event |
| `inspect-and-dump.cs` | Format values with `util.inspect`; use a local `tap` helper for non-breaking chain inspection |
| `repl-special-vars.cs` | REPL conveniences: `$_` (last result), `$exception`, and `GetGlobalVariables` |
