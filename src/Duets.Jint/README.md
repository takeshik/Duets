# Duets.Jint

Duets.Jint provides the Jint runtime integration for Duets. It supplies the JavaScript engine,
Babel-based TypeScript transpilation, the optional TypeScript compiler and language service, and
CLR interop support used by a `DuetsSession`.

The package references the runtime-neutral `Duets` core. Jint-specific types remain in this
package; scripts and hosts interact primarily through `DuetsSession`.

## Creating a session

Referencing `Duets.Jint` registers Jint and Babel as the default backend, so the minimal host needs
no explicit backend configuration:

```csharp
using var session = await DuetsSession.CreateAsync();
Console.WriteLine(session.Evaluate("Math.sqrt(2)"));
```

Call `UseJint` when Jint options are required. CLR interop, including the script-side `typings`
helpers, is opt-in:

```csharp
using var session = await DuetsSession.CreateAsync(config =>
    config.UseJint(options => options.AllowClr())
);
```

`UseBabel` is only necessary when replacing Babel's default asset source or cache behavior. The
core `UseEngine` and `UseTranspiler` methods remain available for advanced combinations.

## Choosing a transpiler

`BabelTranspiler` is the default. It removes TypeScript syntax and supports runtime constructs such
as enums, namespaces, and constructor parameter properties. It does not provide a TypeScript
language service.

Use `TypeScriptService` when the host needs server-side completions or transpilation backed by the
official TypeScript compiler:

```csharp
using var session = await DuetsSession.CreateAsync(config =>
    config
        .UseTranspiler(async declarations =>
            await TypeScriptService.CreateAsync(declarations, injectStdLib: true))
        .UseJint(options => options.AllowClr())
);

var typeScript = (TypeScriptService)session.Transpiler;
var completions = typeScript.GetCompletions("Math.", 5);
```

Passing the session-owned `TypeDeclarations` keeps registered CLR declarations synchronized with
the language service. `injectStdLib: true` additionally loads ES5 library declarations for
JavaScript built-ins. DuetsPad normally uses Monaco's browser-side language service, so it does not
require `TypeScriptService` merely to show editor completions.

See the runnable
[server-side completions sample](https://github.com/takeshik/Duets/blob/main/samples/Duets/server-side-completions.cs)
for a complete host.

## CLR interop and `typings`

Jint's `AllowClr()` enables runtime CLR access. It also lets Duets register the `typings` global,
which keeps runtime exposure and generated TypeScript declarations aligned. Common operations are:

| Script API | Effect |
|---|---|
| `typings.usingNamespace(...)` | Import a namespace, register its types, and expose its non-nested types as globals. |
| `typings.importNamespace(...)` | Import and register a namespace while retaining the namespace-qualified access style. |
| `typings.importType(...)` | Register one CLR type from a type reference or assembly-qualified name. |
| `typings.scanAssembly(...)` / `scanAssemblyOf(...)` | Register namespace placeholders without all type members. |
| `typings.importAssembly(...)` / `importAssemblyOf(...)` | Register every public type in an assembly. |
| `typings.addExtensionMethods(...)` | Make a static extension-method container callable with instance syntax and add its declarations. |

The global Jint `importNamespace()` affects runtime access only; prefer the corresponding
`typings` operation when completions must change too. Runnable examples cover
[type registration](https://github.com/takeshik/Duets/blob/main/samples/Duets/with-type-registration.cs)
and
[extension methods](https://github.com/takeshik/Duets/blob/main/samples/Duets/extension-methods.cs).

Other built-ins include `clrTypeOf(typeReference)` for obtaining the underlying `System.Type`,
`util.inspect(value, options)` for readable value formatting, and `util.toJsArray(value)` for
converting CLR arrays to native JavaScript arrays. See the
[inspection sample](https://github.com/takeshik/Duets/blob/main/samples/Duets/inspect-and-dump.cs)
for `util.inspect`; the injected TypeScript declarations document the accepted arguments for all
script built-ins in the editor.

## Runtime assets and offline hosts

Babel and `TypeScriptService` load their compiler scripts through `IAssetSource`. Defaults fetch
versioned assets from unpkg and wrap them in a seven-day disk cache under the system temporary
directory. A first uncached run therefore requires network access unless the host replaces the
sources.

Use `BabelTranspilerOptions.BabelJs`, `TypeScriptServiceOptions.TypeScriptJs`, and
`TypeScriptServiceOptions.LibEs5Source` to supply controlled or offline assets. `AssetSources`
provides HTTP, unpkg, embedded-resource, delegate-backed, and disk-cache adapters. For example:

```csharp
config.UseBabel(new BabelTranspilerOptions
{
    BabelJs = AssetSources.EmbeddedResource(
        typeof(Program).Assembly,
        "MyApp.Assets.babel.js"
    ),
});
```

The embedded or custom content must match the role of the replaced asset. When constructing a
`TypeScriptService`, keep `lib.es5.d.ts` matched to the loaded compiler version.

## Further documentation

- [Repository quick start](https://github.com/takeshik/Duets#quick-start)
- [Runnable Duets samples](https://github.com/takeshik/Duets/tree/main/samples/Duets)
- [Jint integration architecture](https://github.com/takeshik/Duets/blob/main/docs/architecture/Duets.Jint.md)
- [Architecture decision records](https://github.com/takeshik/Duets/tree/main/docs/decisions)

The package includes XML documentation for IDE tooltips and API browsers.
