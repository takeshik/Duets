# ADR-51: Explicit Ownership Transfer for Script Byte Buffers

## Status

Accepted

## Context

Host APIs sometimes need to return binary data as a JavaScript-native mutable byte array. The first
consumer is `DuetsPadFile.readAllBytes()` from ADR-50. Jint can construct a `Uint8Array` over a
managed `byte[]`, but choosing which CLR values receive that projection affects every Duets.Jint
consumer, not only DuetsPad.

The existing `ClrArrayObjectConverter` deliberately preserves CLR arrays as host objects so they keep
CLR identity and participate in extension-method dispatch. Globally converting every `byte[]` would
break that behavior. Globally converting `ReadOnlyMemory<byte>` to `Uint8Array` would also introduce
new behavior for unrelated host APIs and would require a defensive copy: JavaScript could otherwise
mutate memory that the host still owns. For a whole-file read that has already allocated a fresh
array, that copy doubles the peak managed allocation without protecting any continuing host owner.

## Decision Drivers

- Preserve existing CLR array and `ReadOnlyMemory<byte>` interop behavior by default.
- Make host-to-script mutability and ownership transfer explicit at the API boundary.
- Let Jint produce a real `Uint8Array` without a second managed allocation.
- Keep Duets core independent of Jint while allowing each backend to choose its native projection.
- Prevent accidental reuse or double consumption of transferred storage.

## Considered Alternatives

### A: Globally project every CLR `byte[]` as `Uint8Array`

- Pro: Host methods can return an ordinary array and receive a natural JavaScript binary value.
- Pro: Jint can use the array directly without copying.
- Con: Changes existing Duets.Jint behavior for every byte-array value and bypasses CLR-array
  identity and extension-method dispatch.
- Con: The return type does not communicate that JavaScript may mutate storage still referenced by
  the host.

### B: Globally project `ReadOnlyMemory<byte>` as a copied `Uint8Array`

- Pro: The read-only type indicates that script mutation must not affect host-owned memory.
- Pro: Existing `byte[]` interop remains unchanged.
- Con: Introduces a global conversion rule for unrelated host APIs.
- Con: Requires a full defensive copy and therefore doubles peak allocation when the producer has
  already allocated an exclusively owned array.

### C: Use an explicit single-use ownership-transfer envelope *(chosen)*

- Pro: Only host APIs that intentionally transfer storage receive native byte-array projection.
- Pro: The backend can take the original array without copying because the host has relinquished it.
- Pro: Normal CLR arrays and read-only memory retain their existing interop behavior.
- Con: Adds a specialized core type and a backend converter.
- Con: Returning the envelope is a stronger contract than returning ordinary byte storage: the
  caller must not retain or use the source array.

## Decision

Add `ScriptByteBuffer` to the engine-neutral `Duets` package. A host creates it with
`ScriptByteBuffer.TakeOwnership(byte[], producer)`, after which the host must not read or modify the
source array or reuse the envelope. The optional producer name defaults to the calling member and is
included in the fail-fast error if the buffer is consumed twice. The buffer is consumable exactly
once.

Duets.Jint registers a converter specifically for `ScriptByteBuffer`. The converter consumes the
owned array, gives it to Jint's `ArrayBuffer`, and returns a `Uint8Array` view spanning the complete
buffer. This path performs no defensive copy because ownership was transferred explicitly. The
existing CLR-array converter remains in place, and neither `byte[]` nor `ReadOnlyMemory<byte>` gains
an implicit native-array conversion.

`ScriptByteBuffer` is a backend-neutral transfer request, not a guarantee that every backend uses the
same JavaScript representation. A backend that supports native mutable byte buffers should consume it
into that representation; otherwise its normal host-object interop rules apply. APIs whose declared
script surface requires a native byte array, such as the current Jint-backed DuetsPad surface, must be
tested against the selected backend.

## Rationale

The ownership boundary is the information required to avoid both unsafe sharing and unnecessary
copying. Neither `byte[]` nor `ReadOnlyMemory<byte>` expresses that the producer has permanently
relinquished the backing storage. A dedicated envelope makes that uncommon operation reviewable at
the host API, while a backend-specific converter keeps the engine-neutral core free of Jint types.

This also preserves compatibility. Existing hosts returning arrays or read-only memory continue to
receive the same CLR interop behavior; only code that explicitly constructs `ScriptByteBuffer`
requests native binary projection.

## Consequences

- **Positive**: Jint can expose host-produced bytes as a real `Uint8Array` with one managed
  allocation total.
- **Positive**: Existing byte-array identity, extension methods, and `ReadOnlyMemory<byte>` behavior
  remain unchanged.
- **Positive**: Ownership transfer is visible in the producer's type signature and enforced as a
  single-use operation; double consumption identifies the producing host API.
- **Negative / trade-offs**: Host code must obey the no-reuse contract after calling
  `TakeOwnership`.
- **Negative / trade-offs**: Each runtime backend must implement its own native projection if a
  consumer requires one.
