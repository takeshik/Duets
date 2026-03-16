# ADR-6: Fetch and Cache Runtime JS Assets from CDN

## Status

Deprecated — superseded by [ADR-18](18_pluggable-asset-source-abstraction.md)

## Context

Duets depends on two large JavaScript assets: the TypeScript compiler (`typescript.js`, ~5 MB) and the Monaco Editor loader (`loader.js`). These must be available at runtime. The question is how to distribute them.

## Decision Drivers

- **Assembly size** — Bundling multi-megabyte JS files as embedded resources would significantly inflate the library assembly
- **Version flexibility** — Updating TypeScript or Monaco versions should not require a new library release
- **Offline capability** — Some environments may have restricted or no internet access
- **Simplicity** — Minimize build-time complexity and asset management overhead

## Considered Alternatives

### A: Bundle as embedded resources

- Pro: Works offline; no network dependency; deterministic versions
- Con: Inflates assembly size by several megabytes; version updates require rebuilding and re-releasing the library

### B: Fetch from unpkg at runtime and cache locally

- Pro: Keeps the assembly small; version updates are a code change (URL), not a release; caching provides offline-like behavior after first fetch
- Con: First run requires internet access; depends on unpkg availability; cache invalidation (currently time-based, 7 days)

### C: Require the consumer to provide the assets

- Pro: Maximum flexibility; no opinions on distribution
- Con: Pushes complexity to every consumer; poor out-of-the-box experience

## Decision

Fetch from unpkg at runtime and cache in the system temp directory for 7 days (option B).

## Rationale

Assembly size is the strongest driver for a library intended to be embedded in other applications. Option A makes the assembly impractically large. Option C provides flexibility but degrades the getting-started experience. Option B strikes the best balance: the assembly stays small, the default experience works out of the box, and the caching layer mitigates repeated network requests. The offline limitation is acceptable for a debugging/scripting tool that typically runs in development environments with internet access.

Local caching is necessary regardless — re-fetching multi-megabyte assets on every run is impractical. The relatively long cache duration (7 days) is additionally motivated by unpkg's occasional availability issues, ensuring that transient CDN instability does not disrupt normal usage.

Making the fetch mechanism replaceable via an interface (to support offline environments, air-gapped networks, or pinned versions) is a recognized future need, but is not implemented yet — the current default-fetch-and-cache approach is sufficient for now (YAGNI).

## Consequences

- **Positive**: Library assembly remains small; TypeScript/Monaco versions can be updated without a library release; consumers get a working setup with no asset management; caching absorbs transient unpkg instability
- **Negative / trade-offs**: First run requires internet access; depends on unpkg's availability and URL stability; consumers in restricted network environments cannot use Duets out of the box until the fetch mechanism is made pluggable
