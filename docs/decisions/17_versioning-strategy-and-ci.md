# ADR-17: Versioning Strategy and CI Pipeline

## Status

Superseded by [ADR-23](23_ci-and-package-publishing.md)

## Context

As the project approaches a publishable state, automated versioning and a
continuous integration pipeline are needed. The goals are:

- Assembly and NuGet package versions are set automatically from source
  control without manual scripts or generated files checked into the repo.
- Non-release (development) builds carry enough metadata to identify the
  exact source revision.
- Releases are triggered by a Git tag and publish a NuGet package to
  GitHub Packages.
- The CI pipeline builds and runs tests on every push and pull request.

## Decision Drivers

- **Zero-maintenance versioning**: no PowerShell scripts, no hand-edited
  version files per build.
- **Standard NuGet consumer experience**: version numbers follow SemVer so
  package consumers can reason about compatibility.
- **Traceability**: non-release builds embed commit identity so issues can
  be traced back to source.
- **Broad adoption and long-term maintenance**: tooling should be widely
  used and backed by a durable organization.

## Considered Alternatives

### A: MinVer

- Pro: Extremely simple; zero configuration.
- Con: Hosted by an individual maintainer rather than a durable org;
  fewer built-in features (no `ThisAssembly` class, no cloud build
  integration).

### B: Nerdbank.GitVersioning (NBGV)

- Pro: Under the `dotnet` GitHub org; high adoption across Microsoft and
  .NET Foundation projects; built-in `ThisAssembly` class; supports
  SemVer 2.0 with commit hash in build metadata.
- Con: Requires a `version.json` configuration file; slightly more
  setup than MinVer.

### C: Manual script (legacy AppVeyor approach)

- Pro: Full control over every field.
- Con: Scripts and generated files require maintenance; CI-specific
  environment variables make local builds diverge.

## Decision

Use **Nerdbank.GitVersioning (NBGV)** with the following configuration:

**Branching and release model**

- Single `main` branch + feature branches (GitHub Flow).
- Releases are triggered by pushing a Git tag matching `v{major}.{minor}.{patch}`
  (e.g., `v0.1.0`). This is the only `publicReleaseRefSpec`.
- All other builds (main, PRs) are treated as development snapshots.

**Version format**

| Context | Format | Example |
|---|---|---|
| Release (tag) | `{major}.{minor}.0` | `0.1.0` |
| Development build | `{major}.{minor}.0-dev.{height}.g{commit}` | `0.1.0-dev.5.gabc1234` |

Releases always have patch=0; only the minor version increments between
releases. Development builds embed both commit height (for sortability)
and the commit hash (for traceability) in the prerelease identifier.

**How release versions are produced**

NBGV's `publicReleaseRefSpec` only suppresses the automatic commit-hash
suffix; it does not strip a prerelease label embedded in `version.json`.
Therefore, release NuGet packages are built by overriding the version
with the tag name at pack time:

```bash
dotnet pack /p:Version=0.1.0   # version extracted from the git tag
```

`version.json` is never modified by the release job itself. It is updated
manually (minor bump) at the start of the next development cycle:

```
v0.1.0 released
  → version.json: "0.1-dev.{height}" → "0.2-dev.{height}"  (manual PR)
  → dev builds become 0.2.0-dev.1.g...
```

**`version.json`**

```json
{
  "version": "0.1-dev.{height}",
  "nugetPackageVersion": { "semVer": 2 },
  "publicReleaseRefSpec": [ "^refs/tags/v\\d+\\.\\d+\\.\\d+$" ]
}
```

**CI (GitHub Actions)**

- CI workflow: triggers on push and pull request to `main`; steps:
  restore → build → test. `fetch-depth: 0` required for NBGV.
- Release workflow: triggered by `v*.*.*` tag push; extracts version
  from the tag, packs with `/p:Version=...`, publishes to GitHub Packages,
  and creates a GitHub Release.

## Rationale

NBGV is chosen over MinVer for durability (`dotnet` org) and for the
built-in `ThisAssembly` class, which makes build metadata (version,
commit, branch) available as compile-time constants — useful for
`--version` output in `Duets.Sandbox` and for diagnostic purposes.

SemVer 2.0 is preferred over SemVer 1.0 because GitHub Packages supports
it and allows a longer commit hash in the prerelease identifier. In
practice NBGV places both height and commit hash in the prerelease
(e.g., `0.1.0-dev.5.gabc1234`) rather than in build metadata (`+`),
which is actually preferable for NuGet because build metadata is ignored
by version ordering.

A single `main` branch with tag-based releases is preferred over
`main`/`develop` GitFlow because the added ceremony of two long-lived
branches is not justified for a project of this scale.

## Consequences

- **Positive**: Version numbers are fully automated; no manual edits
  required between releases.
- **Positive**: Development builds are clearly distinguishable from
  releases and traceable to a specific commit.
- **Positive**: `ThisAssembly` class provides version constants for use
  in application code.
- **Negative / trade-offs**: `fetch-depth: 0` in CI checkout fetches the
  full Git history, which grows over time. For most projects this remains
  fast enough to be negligible.
- **Negative / trade-offs**: SemVer 2.0 build metadata (`+g{commit}`) is
  ignored by some older NuGet clients, but all modern tooling handles it
  correctly.
