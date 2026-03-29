# ADR-23: CI Pipeline and NuGet Package Publishing Strategy

## Status

Accepted — supersedes [ADR-17](17_versioning-strategy-and-ci.md)

## Context

ADR-17 described the versioning strategy and intended CI pipeline at a high level but left two concrete
questions open:

1. Should `HttpHarker` be published as an independent NuGet package, or bundled inside `Duets.nupkg` as a
   private DLL?
2. Should every push to `main` publish a snapshot package, or only tag-based releases?

These questions became actionable once multi-targeting was introduced (ADR-22).

## Decision

### Package topology

Publish **two independent NuGet packages**: `Duets` and `HttpHarker`.

`Duets.nupkg` declares a NuGet dependency on `HttpHarker` at the matching version. Consumers who install
`Duets` receive `HttpHarker` transitively with no extra configuration.

Bundling `HttpHarker` as a private DLL (`IsPackable=false`) was rejected because ADR-9 explicitly designed it
as an independent library with no dependency on `Duets` and noted it may be extracted into its own repository.
Bundling would make that extraction a breaking change and prevent independent use.

### CI and publish workflow

A single workflow file (`publish.yml`) covers all events:

| Trigger | Steps |
|---|---|
| Pull request to `main` | restore → build → test |
| Push to `main` | restore → build → test → pack (snapshot) → publish |
| Push of `v*.*.*` tag | restore → build → test → pack (release) → publish |

Pack and publish steps run only when `github.event_name == 'push'`.

### Version production

| Event | Version | Example |
|---|---|---|
| Push to `main` | NBGV auto-generates from `version.json` + commit height | `0.1.0-dev.12.gabc1234` |
| Push of `v*.*.*` tag | Extracted from tag name, passed as `/p:Version=` | `0.1.0` |

`version.json` is never modified by the release workflow. After each release, the minor version is bumped
manually (e.g., `0.1-dev.{height}` → `0.2-dev.{height}`).

### Publish target

All packages are published to **GitHub Packages**
(`https://nuget.pkg.github.com/takeshik/index.json`). GitHub Packages requires authentication to install
packages; consumers need a GitHub PAT with `read:packages` scope.

### `IsPackable` policy

`Directory.Build.props` sets `IsPackable=false` as the solution-wide default. `Duets` and `HttpHarker` opt in
with `IsPackable=true` explicitly. This prevents `Duets.Sandbox` and test projects from being accidentally
included in a `dotnet pack` run.

### Assembly signing

Release builds of both `Duets` and `HttpHarker` are strong-named using `key.snk`, which is committed to the
repository. `SignAssembly=true` is set for the `Release` configuration in each library project.
`AssemblyOriginatorKeyFile` is set solution-wide via `Directory.Build.props`. Because `Duets` is
strong-named, its `InternalsVisibleTo` declaration for `Duets.Tests` includes the full public key.

## Consequences

- `Duets` and `HttpHarker` are published as two separate NuGet packages; `HttpHarker` is a declared
  dependency of `Duets`.
- Every push to `main` publishes a snapshot package to GitHub Packages.
- Tag pushes publish a release package; `version.json` is bumped manually after each release.
- Consuming packages from GitHub Packages requires a GitHub PAT with `read:packages` scope.
- `HttpHarker` can be used independently of `Duets`.
- Release assemblies are strong-named; consumers in environments that enforce assembly identity (e.g.,
  certain Unity configurations) are supported.
