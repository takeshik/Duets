# Duets.Jint

Duets.Jint provides the Jint runtime integration for Duets. It supplies the JavaScript engine,
Babel-based TypeScript transpilation, the optional TypeScript compiler and language service, and
CLR interop support used by a `DuetsSession`.

The package references the runtime-neutral `Duets` core. Hosts normally create a `DuetsSession`
and select or configure this backend through `UseJint` and `UseBabel`; lower-level engine and
transpiler types remain available for specialized integrations.

## Documentation

- [Repository quick start](https://github.com/takeshik/Duets#quick-start)
- [Runnable Duets samples](https://github.com/takeshik/Duets/tree/main/samples/Duets)
- [Jint integration architecture](https://github.com/takeshik/Duets/blob/main/docs/architecture/Duets.Jint.md)
- [Architecture decision records](https://github.com/takeshik/Duets/tree/main/docs/decisions)

The package includes XML documentation for IDE tooltips and API browsers.
