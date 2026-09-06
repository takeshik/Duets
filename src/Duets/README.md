# Duets

Duets is the runtime-neutral core of an embeddable TypeScript console for .NET applications. It
owns session lifecycle, script execution contracts, TypeScript declarations, completion metadata,
and engine-neutral script values without depending on a specific JavaScript runtime.

Applications normally pair this package with a runtime integration such as `Duets.Jint`. A
`DuetsSession` is the primary host-facing entry point; lower-level interfaces remain available for
custom runtime integrations.

## Documentation

- [Repository quick start](https://github.com/takeshik/Duets#quick-start)
- [Runnable Duets samples](https://github.com/takeshik/Duets/tree/main/samples/Duets)
- [Core architecture](https://github.com/takeshik/Duets/blob/main/docs/architecture/Duets.md)
- [Architecture decision records](https://github.com/takeshik/Duets/tree/main/docs/decisions)

The package includes XML documentation for IDE tooltips and API browsers.
