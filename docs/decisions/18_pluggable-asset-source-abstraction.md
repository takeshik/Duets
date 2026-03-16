# ADR-18: Pluggable Asset Source Abstraction (`IAssetSource`)

## Status

Accepted

## Context

[ADR-6](6_fetch-and-cache-runtime-js-assets-from-cdn.md) established the pattern of
fetching runtime JS assets (TypeScript compiler, Monaco Editor loader) from unpkg CDN
and caching them locally for 7 days. ADR-6 explicitly deferred making the fetch mechanism
pluggable ("YAGNI") but identified it as a recognized future need for offline or
air-gapped environments.

That deferral is now lifted. Consumers in restricted network environments (corporate
firewalls, air-gapped machines, CI/CD pipelines with no internet access) have no way to
provide these assets without forking the library. Additionally, the three hard-coded
`new HttpClient().GetStringAsync(url)` calls inside `TypeScriptService` and `ReplService`
are untestable in isolation and violate the library's own dependency-injection idiom
(cf. `ITranspiler`).

## Decision Drivers

- **Restricted environments** — consumers on air-gapped or firewalled machines must be
  able to supply assets from embedded resources or an internal mirror
- **Testability** — asset fetching must be replaceable with stubs without network access
- **Backward compatibility** — existing code that passes no options must continue to work
  identically (same CDN URLs, same 7-day temp-file cache)
- **Consistency** — the mechanism should follow the `ITranspiler` pattern already
  established in the codebase

## Considered Alternatives

### A: Raw `Func<bool, Task<string>>` delegate per asset

- Pro: No new types; minimal API surface
- Con: Unnamed functions convey no intent; composing caching on top of a raw delegate is
  awkward; no discoverable factory methods to guide consumers toward safe defaults

### B: `IAssetSource` interface + `AssetSources` factory (chosen)

- Pro: Named abstraction; internal implementations hidden behind a factory; composable
  via `WithDiskCache` extension; ad-hoc `From(delegate)` escape hatch preserves
  delegate ergonomics when needed
- Con: One new public interface and one public factory class

### C: Single `AssetProvider` class with virtual methods

- Pro: Subclassable without an interface
- Con: Class inheritance is not idiomatic for this in .NET; harder to compose

## Decision

Introduce `IAssetSource` (interface) and `AssetSources` (factory class) in
`src/Duets/AssetSource.cs`. Concrete implementations are `internal sealed` and exposed
exclusively through the factory.

**`IAssetSource`:**
```csharp
public interface IAssetSource
{
    Task<string> GetAsync(bool force = false);
}
```

**`AssetSources` factory:**
- `Http(string url, HttpClient? httpClient = null)` — arbitrary HTTP URL; security is caller's responsibility
- `Unpkg(string package, string version, string path, HttpClient? httpClient = null)` — typed unpkg helper
- `EmbeddedResource(Assembly assembly, string resourceName)` — manifest embedded resources
- `From(Func<bool, Task<string>> factory)` — ad-hoc delegate wrapper (testing, custom scenarios)
- `WithDiskCache(this IAssetSource, FilePath cacheFilePath, TimeSpan? ttl)` — extension method decorator

`HttpClient? httpClient = null` falls back to a shared `static readonly HttpClient`
inside `AssetSources`, avoiding socket exhaustion.

`FilePath cacheFilePath` (Mio type) rather than a bare filename gives callers full control
over the cache directory without an additional parameter.

**`TypeScriptServiceOptions`** record added to `TypeScriptService.cs`:
- `TypeScriptJs: IAssetSource` — defaults to `Unpkg("typescript","5.9.3","lib/typescript.js").WithDiskCache(...)`
- `LibEs5Source: Func<string, IAssetSource>` — factory receiving the detected TS version string;
  defaults to `Unpkg("typescript", tsVersion, "lib/lib.es5.d.ts").WithDiskCache(...)`

**`ReplService`** constructor gains `IAssetSource? monacoLoader = null`; defaults to
`Unpkg("monaco-editor","0.55.1","min/vs/loader.js").WithDiskCache(...)`.

## Rationale

`Func<string, IAssetSource>` for the ES5 library is necessary because the TypeScript
version is not known until after `typescript.js` is loaded and `ts.version` is evaluated —
it cannot be expressed as a static `IAssetSource` property.

The `Lazy<Task<string>>` wrapper in `ReplService` is retained alongside the new
`IAssetSource` field. `CachedAssetSource` handles disk-level caching, but `Lazy` provides
a separate, in-memory deduplication guarantee: only one HTTP request is in-flight for
the Monaco loader even if multiple requests arrive before the disk cache is warm.

Return type is `string` (not `byte[]` / `ReadOnlyMemory<byte>`) because all three assets
are UTF-8 text consumed as strings — the TypeScript compiler and ES5 lib are both executed
or registered as strings in Jint, and the re-encoding cost when serving Monaco loader over
HTTP is negligible.

## Consequences

- **Positive**:
  - Consumers in air-gapped environments can supply assets via `EmbeddedResource` or `From`
  - Tests can stub asset fetching with `From(_ => Task.FromResult("..."))` without network
  - Cache directory is now configurable via `FilePath` argument to `WithDiskCache`
  - `HttpClient` injection prevents socket exhaustion in long-running hosts
  - Default behavior is identical — no breaking changes for existing callers
- **Negative / trade-offs**:
  - Two new public types (`IAssetSource`, `AssetSources`) added to the public API surface
  - `TypeScriptServiceOptions` defaults embed `DirectoryPath.GetTempDirectory()` at type
    initialization time; this is consistent with the prior behavior
