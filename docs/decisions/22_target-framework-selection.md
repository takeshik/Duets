# ADR-22: Target Framework Selection — `netstandard2.1` and `net8.0`

## Status

Accepted — applies to `Duets`, `Duets.Jint`, and `HttpHarker`.
`Duets.Okojo` targets `net10.0` as a deliberate exception; see ADR-27.

## Context

Duets is designed to be embeddable in a wide range of .NET environments including game engines (Unity, Godot),
mobile platforms (iOS, Android via Xamarin/MAUI), and desktop applications. Choosing the right set of Target
Framework Monikers (TFMs) determines which runtimes can consume the library without recompiling, and which
platform-specific APIs must be shimmed.

The library initially targeted `net10.0` only. After evaluating platform coverage, the decision was made to add
multi-targeting.

### Platform evidence

| TFM | Confirmed platform coverage |
|-----|-----------------------------|
| `netstandard2.1` | .NET Core 3.0+, .NET 5–10, Mono 6.4, Xamarin.iOS 12.16, Xamarin.Android 10.0, Unity 2021.2+ |
| `net8.0` (LTS) | .NET 8 runtime and all runtimes that prefer a specific net8.0 TFM, including Godot 4.4+ |

Sources: [Microsoft Learn — .NET Standard](https://learn.microsoft.com/en-us/dotnet/standard/net-standard),
[Godot docs 4.4 branch](https://raw.githubusercontent.com/godotengine/godot-docs/4.4/tutorials/scripting/c_sharp/c_sharp_basics.rst)
("In Godot, it is implemented with .NET 8.0").

### Why not `netstandard2.0`

`netstandard2.0` adds coverage for .NET Framework and Unity 2018–2021.1.
.NET Framework is not a target environment for Duets. Unity 2021.2 is the oldest current LTS
(Unity 2021.3 LTS, 2022.3 LTS, Unity 6 LTS all support NS2.1). Targeting NS2.0 would require
pervasive async API shims (`IAsyncDisposable`, `IAsyncEnumerable`, `KeyValuePair` deconstruction, etc.)
with no meaningful gain. The decision is to not target NS2.0.

### Why not `net6.0`

Duets has no driver for a `net6.0`-specific TFM. Godot 4.4+, the primary .NET 6-era game engine target,
moved to .NET 8; earlier Godot 4.x versions (which used .NET 6) are covered by `netstandard2.1`.

### Why not `net10.0`

No .NET 10-specific APIs are used. `net10.0` consumers can load the `net8.0` TFM without issue.
`Guid.CreateVersion7()` (.NET 9+), which was briefly used as an SSE client identifier, was replaced with
`Guid.NewGuid()` as the distinction is immaterial for that purpose.
Explicitly targeting `net10.0` would add a build target without a corresponding capability gain.

## Decision

Library projects (`Duets`, `HttpHarker`) target **`netstandard2.1;net8.0`**.
`Duets.Sandbox` and test projects remain on `net10.0` as they are development-only tools with no
embeddability requirement.

## Consequences

- Unity 2021.2+, Xamarin, Mono 6.4+ are covered via `netstandard2.1`.
- Godot 4.4+ is covered via `net8.0`.
- A small number of APIs added after .NET Standard 2.1 require `#if NETSTANDARD2_1` shims:
  `SHA1`/`SHA256.HashData`, `Convert.ToHexString`, `ObjectDisposedException.ThrowIf`,
  `Enum.GetValuesAsUnderlyingType`, and `[UnsafeAccessor]`.
- Removing NS2.1 in the future requires: removing `netstandard2.1` from `<TargetFrameworks>`,
  removing conditional packages, and cleaning up `#if NETSTANDARD2_1` blocks.
