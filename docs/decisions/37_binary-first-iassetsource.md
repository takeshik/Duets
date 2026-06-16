# ADR-37: Binary-First `IAssetSource` Asset Content

## Status

Accepted

## Context

[ADR-18](18_pluggable-asset-source-abstraction.md) introduced `IAssetSource` and
the `AssetSources` factory as the pluggable abstraction for fetching runtime
assets, with a text-only contract:

```csharp
public interface IAssetSource
{
    Task<string> GetAsync(bool force = false);
}
```

ADR-18 explicitly justified the `string` return type: at that time every asset
served through the abstraction (the TypeScript compiler `typescript.js`, the
`lib.es5.d.ts` standard library, and the Monaco loader) was UTF-8 text consumed
as a string.

That assumption no longer holds. [ADR-33](33_tabler-css-framework-for-duetspad.md)
adopts Tabler for DuetsPad and decides that DuetsPad serves the Tabler Icons web
font (`tabler-icons.woff2`) by default through `IAssetSource`, the same delivery
path used for Monaco and the TypeScript compiler. A web font is binary; a
text-only contract cannot carry it without a lossy or awkward encoding hop.

## Decision Drivers

- Serve binary assets (web fonts) through the same pluggable abstraction as text
  assets, so the offline / air-gapped / mirror story from ADR-18 covers fonts too
- Preserve the existing text ergonomics for the JS/CSS/d.ts assets
- Keep a single asset abstraction rather than a parallel binary mechanism
- Preserve ADR-18's defaults (same CDN sources, same disk cache, same testability)
- Minimize churn for existing text consumers

## Considered Alternatives

### A: Keep text-only `IAssetSource`; add a separate binary mechanism for fonts

- Pro: no breaking change to the existing interface
- Con: two parallel abstractions, with duplicated factory, caching, and
  offline/override stories; consumers must learn which one applies to which asset

### B: Carry binary content as a base64 (or otherwise encoded) string

- Pro: the interface signature is unchanged
- Con: lossy/awkward contract; every binary consumer must encode and decode;
  defeats the purpose of a clean asset abstraction; needless allocation and CPU

### C: Make `IAssetSource` binary-first with a text convenience extension

- Pro: one abstraction serves every asset; `byte[]` is the lossless common
  denominator; text consumers decode with a thin UTF-8 helper; the factory,
  disk cache, and offline/override story from ADR-18 are unchanged
- Con: breaking change to the public interface method; text assets pay a
  negligible UTF-8 decode hop

## Decision

Choose **Alternative C**. `IAssetSource` becomes byte-oriented:

```csharp
public interface IAssetSource
{
    Task<byte[]> GetBytesAsync(bool force = false);
}
```

Text is read through an extension method that defaults to UTF-8:

```csharp
public static class AssetSourceExtensions
{
    public static Task<string> GetStringAsync(
        this IAssetSource source,
        bool force = false,
        Encoding? encoding = null
    );
}
```

The `AssetSources` factory stays the entry point and gains explicit text/binary
ad-hoc factories:

- `Http`, `Unpkg`, `EmbeddedResource`, `WithDiskCache` — unchanged surface, now
  byte-oriented; the disk cache stores bytes
- `FromString(Func<bool, Task<string>>)` — ad-hoc text source (UTF-8 encoded)
- `FromBytes(Func<bool, Task<byte[]>>)` — ad-hoc binary source
- `From(Func<bool, Task<string>>)` — retained as a compatibility alias for `FromString`

Text consumers (the TypeScript compiler, `lib.es5.d.ts`, Babel, the Monaco
loader, Tabler CSS) read via `GetStringAsync`. Binary consumers (the Tabler
Icons `tabler-icons.woff2` font from ADR-33) read via `GetBytesAsync`.

DuetsPad's consumer of this contract is **`AssetProvider`**, which uses
`GetBytesAsync` for the binary `tabler-icons.woff2` font and `GetStringAsync`
for all text assets (Tabler core CSS, Tabler Icons CSS, Monaco loader). The
`Lazy`-wrapped caches in `AssetProvider` hold the resolved values so each
asset is fetched at most once per server lifetime.

This amends the `string`-return decision and its rationale in ADR-18. The rest
of ADR-18 — the pluggable abstraction, the factory model, default CDN sources,
and the composable disk cache — stands unchanged.

## Rationale

`byte[]` is the lossless representation for any asset; text is one UTF-8 decode
away, and that cost is incurred only for text assets, one direction, once per
fetch (and the result is typically cached). Keeping a single abstraction avoids
duplicating the offline, mirror, and stub-in-tests capabilities that ADR-18
deliberately centralized. The text extension keeps the common case ergonomic, and
the optional `Encoding` parameter leaves room for non-UTF-8 text without changing
the contract.

The change is a breaking one at the interface boundary, which is acceptable: the
asset abstraction is young, the default behavior for existing callers is
identical, and the alternative — a second, parallel binary abstraction — would
impose a larger long-term cost.

## Consequences

- **Positive**:
  - Web fonts and any future binary assets flow through the same pluggable,
    cached, offline-capable abstraction as text assets
  - Text ergonomics are preserved via `GetStringAsync`, with optional encoding
  - Default behavior is unchanged for existing callers (same CDN sources, same
    disk cache); tests stub text with `FromString(...)` and binary with `FromBytes(...)`
- **Negative / trade-offs**:
  - Breaking change to the public `IAssetSource` interface: the method is renamed
    and its return type changes from `string` to `byte[]`; any external code
    implementing `IAssetSource` directly must update
  - Text assets incur a negligible UTF-8 decode step that the text-only contract
    avoided
