# ADR-14: Assembly Derivation from TypeReference via `scanAssemblyOf` / `useAssemblyOf`

## Status

Accepted

## Context

`typings.scanAssembly` and `typings.useAssembly` originally accepted only an assembly name string (e.g., `"System.Net.Http"`). In practice, users already have a CLR type reference in scope (e.g., `System.IO.File`) and should not need to look up the corresponding assembly name string separately. The natural ergonomic extension was to allow passing a `TypeReference` directly to these methods.

One candidate approach—accepting `TypeReference` in the existing `scanAssembly` / `useAssembly`—was rejected on two grounds:

1. **Semantic mismatch**: the method name says "assembly" but the argument would be a type. The call `typings.scanAssembly(System.IO.File)` is misleading; it implies scanning the type `File`, not the assembly that contains it.
2. **`AllowSystemReflection` constraint**: accessing `Type.Assembly` from within a Jint script requires `Jint.Options.Interop.AllowSystemReflection = true`. The library must not impose this requirement. Therefore, `clrTypeOf(System.IO.File).Assembly` is not a usable pattern for callers who have not opted into `AllowSystemReflection`.

Handling the assembly derivation in C# rather than in script avoids the `AllowSystemReflection` constraint entirely, because `TypeReference.ReferenceType.Assembly` is accessed by library code, not by the script.

## Decision Drivers

- **Ergonomics**: callers should not need to supply assembly name strings when a type reference is already available.
- **`AllowSystemReflection` neutrality**: the library must not require this Jint setting. Assembly derivation from a type reference must happen on the C# side.
- **Naming clarity**: method names must unambiguously reflect what they accept. A method named `scanAssembly` should accept something that represents an assembly, not a type.

## Considered Alternatives

### A: Accept `TypeReference` in the existing `scanAssembly` / `useAssembly`

- Pro: fewer methods; simpler surface area.
- Con: semantic mismatch — `scanAssembly(System.IO.File)` reads as "scan the type File", not "scan the assembly containing File".
- Con: inconsistent: the method accepts both "an assembly" and "a type that happens to live in an assembly", blurring what the argument means.

### B: Separate `scanAssemblyOf` / `useAssemblyOf` methods with `Of` suffix

- Pro: `Of` suffix cleanly signals "derive the assembly from this argument". The distinction between `scanAssembly(asm)` and `scanAssemblyOf(type)` is explicit at the call site.
- Pro: `scanAssembly` / `useAssembly` retain unambiguous semantics (they accept assemblies).
- Pro: assembly derivation via `TypeReference.ReferenceType.Assembly` happens in C#, so `AllowSystemReflection` is not required.
- Con: the API grows by two methods.

### C: Accept `ObjectWrapper<Assembly>` only, via `clrTypeOf(T).Assembly`

- Pro: no new method names; callers chain `clrTypeOf` explicitly.
- Con: `Type.Assembly` requires `AllowSystemReflection`. This approach fails silently for callers who have not enabled it, which is the common case.

## Decision

Add `scanAssemblyOf(JsValue typeRef)` and `useAssemblyOf(JsValue typeRef)` to `ScriptTypings` (Alternative B). Both methods accept a `TypeReference`, extract the assembly via `TypeReference.ReferenceType.Assembly` in C#, and delegate to `ScanAssembly` / `UseAssembly` respectively.

The existing `scanAssembly` and `useAssembly` are extended to accept either an assembly name string or an `ObjectWrapper<Assembly>`, but TypeReference is not accepted there.

## Rationale

The `Of` suffix is a recognized English convention for "produce a result derived from the given thing" (cf. `KeyValuePair.Create`, `Task.FromResult`, `Path.GetDirectoryName`). `scanAssemblyOf(System.IO.File)` reads as "scan the assembly of System.IO.File", which is accurate and unambiguous. The separation keeps each method's argument type consistent with its name, and moves the `AllowSystemReflection`-dependent operation out of the script layer entirely.

## Consequences

- **Positive**: callers with a type reference in scope can derive assembly scanning/registration from it without knowing or typing the assembly name string.
- **Positive**: `AllowSystemReflection` is not required for any `typings` operation.
- **Negative / trade-offs**: two additional methods increase the API surface. The distinction between `scanAssembly` and `scanAssemblyOf` must be understood by users.
