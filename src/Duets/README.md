# Duets

Duets is the runtime-neutral core of an embeddable TypeScript console for .NET applications. It
owns session lifecycle, script execution contracts, TypeScript declarations, completion metadata,
and engine-neutral script values without depending on a specific JavaScript runtime.

Applications normally pair this package with a runtime integration such as `Duets.Jint`. A
`DuetsSession` is the primary host-facing entry point; lower-level interfaces remain available for
custom runtime integrations.

## Session lifecycle

Create and dispose one session for each isolated evaluation environment:

```csharp
using var session = await DuetsSession.CreateAsync();

session.SetValue("offset", 40);
var result = session.Evaluate("offset + 2");
Console.WriteLine(result.ToObject()); // 42
```

`CreateAsync()` uses the defaults registered by the referenced backend package. Use the
configuration callback when a backend needs explicit options; the
[`Duets.Jint` guide](https://github.com/takeshik/Duets/blob/main/src/Duets.Jint/README.md) shows the
built-in backend choices.

A session is stateful: globals defined by one execution remain available to later executions.
It is not safe to run overlapping operations on the same session. Create separate sessions for
concurrent work, and dispose each session to release its engine and transpiler.

## Executing and inspecting scripts

The main host operations are:

| API | Use |
|---|---|
| `Execute` / `ExecuteAsync` | Transpile and run statements without returning a script result. |
| `Evaluate` / `EvaluateAsync` | Transpile TypeScript code and return its completion value as an engine-neutral `ScriptValue`; async variants await a top-level promise. |
| `SetValue` | Expose a host value as a script global. |
| `GetGlobalVariables` | Read a snapshot of user-defined globals, excluding built-ins. |
| `ConsoleLogged` | Observe calls to script-side `console.*` methods. |

`ScriptValue.ToObject()` converts a result to its closest CLR representation. `$_` contains the
last evaluated result, while `$exception` contains the last thrown script exception. See the
[minimal evaluation](https://github.com/takeshik/Duets/blob/main/samples/Duets/minimal-eval.cs),
[console](https://github.com/takeshik/Duets/blob/main/samples/Duets/console.cs), and
[special-variable](https://github.com/takeshik/Duets/blob/main/samples/Duets/repl-special-vars.cs)
samples for complete runnable hosts.

## Type declarations and API documentation

The session-owned `TypeDeclarations` store supplies `.d.ts` content to completion consumers:

```csharp
await session.JsDocProviders.AddAsync(typeof(MyApi).Assembly);
session.Declarations.RegisterType(typeof(MyApi));
session.Declarations.RegisterDeclaration("declare const buildName: string;");
```

`RegisterType` reflects a CLR type into TypeScript declarations. `RegisterDeclaration` accepts raw
declaration text, and `RegisterNamespace` creates a namespace placeholder. Declarations describe
types to tooling; exposing a runtime object is a separate operation such as `SetValue`, or a
backend-specific CLR import.

`JsDocProviders` enriches generated declarations from an adjacent XML documentation file, a NuGet
package, raw XML, or a custom `IJsDocProvider`. Providers are consulted in registration order, and
adding one refreshes declarations already registered in the session.

## Tagged templates

`RegisterTaggedTemplate` can install both a runtime evaluator and a host-provided completion
callback for a simple tag such as `` path`...` ``. Runtime evaluation is delegated to a capable
backend; completion registrations remain backend-neutral and can be consumed by DuetsPad.

See the runnable
[tagged-template completion sample](https://github.com/takeshik/Duets/blob/main/samples/Duets.Pad/tagged-template-completion.cs)
for `TemplateCompletionContext`, replacement spans, and completion kinds.

## Package boundary

This package deliberately does not reference Jint, DuetsPad, HttpHarker, or browser assets. Use
`Duets.Jint` for the built-in runtime and transpilers, and `Duets.Pad` for the browser UI. Implement
`IScriptEngine`, `ITranspiler`, and the script-value conversion contracts only when building a
custom backend.

## Further documentation

- [Repository quick start](https://github.com/takeshik/Duets#quick-start)
- [Runnable Duets samples](https://github.com/takeshik/Duets/tree/main/samples/Duets)
- [Core architecture](https://github.com/takeshik/Duets/blob/main/docs/architecture/Duets.md)
- [Architecture decision records](https://github.com/takeshik/Duets/tree/main/docs/decisions)

The package includes XML documentation for IDE tooltips and API browsers.
