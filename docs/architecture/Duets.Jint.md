# Duets.Jint Architecture

[`Duets.Jint`](../../src/Duets.Jint/) is the concrete Jint backend for the runtime-neutral
[`Duets`](Duets.md) contracts. The dependency points from `Duets.Jint` to `Duets`; Jint types and setup details do not
enter the core public surface
([ADR-27](../decisions/27_split-javascript-runtime-backends-from-duets-core.md)).

## Runtime integration

- **`JintScriptEngine`** is the concrete `ScriptEngine<JsValue>`. It owns the user-code Jint engine, CLR access, and
  backend execution hooks, and connects the extension-method registry to Jint's member-accessor hook
  ([ADR-4](../decisions/4_use-jint-as-the-javascript-engine.md),
  [ADR-26](../decisions/26_extension-method-support-via-member-accessor-hook.md),
  [ADR-31](../decisions/31_scriptengine-generic-backend-base-and-iscriptengine.md)).
- **`JintScriptValue` and its converter** wrap and unwrap Jint values behind the core `ScriptValue` boundary
  ([ADR-30](../decisions/30_scriptvalue-redesign-abstract-class-and-jstype.md),
  [ADR-31](../decisions/31_scriptengine-generic-backend-base-and-iscriptengine.md)).
- **`ScriptByteBufferObjectConverter`** consumes the core ownership-transfer envelope as a native JavaScript
  `Uint8Array` over the same managed array. Ordinary CLR arrays and `ReadOnlyMemory<byte>` keep their normal interop
  behavior
  ([ADR-51](../decisions/51_explicit-ownership-transfer-for-script-byte-buffers.md)).
- **`ExtensionMethodRegistry`** is the thread-safe backend registry used by Jint's member-accessor hook to dispatch CLR
  extension methods
  ([ADR-26](../decisions/26_extension-method-support-via-member-accessor-hook.md)).

## Transpilation and language services

`TypeScriptService` hosts the official TypeScript compiler in a dedicated Jint engine that is separate from the
user-code engine. It provides transpilation and server-side completions without letting compiler globals or state
leak into evaluated scripts
([ADR-5](../decisions/5_separate-jint-engines-for-typescript-compiler-and-user-code.md),
[ADR-12](../decisions/12_language-service-host-rewrite-and-nolib.md)).

`BabelTranspiler` hosts `@babel/standalone` in Jint and implements the core `ITranspiler` boundary. It is the selected
migration path for TypeScript syntax when the official TypeScript compiler stops emitting JavaScript directly
([ADR-19](../decisions/19_babel-transpiler-as-typescript-7-migration-path.md)).

Compiler scripts and optional standard-library declarations are obtained through pluggable asset sources. Default
sources fetch and cache the assets, while an embedder can provide offline or controlled sources. `lib.es5.d.ts` is
loaded only when the runtime-hosted `TypeScriptService` needs it for server-side completions
([ADR-6](../decisions/6_fetch-and-cache-runtime-js-assets-from-cdn.md),
[ADR-12](../decisions/12_language-service-host-rewrite-and-nolib.md),
[ADR-18](../decisions/18_pluggable-asset-source-abstraction.md)).

## Script-facing typing surface

**`ScriptTypings`** provides the `typings` global object and its type-registration operations, including type and
assembly import, namespace use, and extension-method registration
([ADR-13](../decisions/13_script-built-ins-and-typings-object.md),
[ADR-24](../decisions/24_typings-api-redesign.md)). It coordinates runtime CLR exposure with the core declaration
store so evaluated code and completions see the same registrations.

## Default backend registration

**`DuetsSessionConfigurationExtensions`** exposes `UseJint()` and `UseBabel()` for explicit selection.
**`JintBackendInitializer`** registers the same engine and transpiler factories as defaults through a module
initializer, enabling zero-configuration `DuetsSession.CreateAsync()` when the application references `Duets.Jint`
([ADR-28](../decisions/28_unified-createasync-api-and-backend-autodiscovery.md)).

See the [architecture landing](README.md) for the complete package graph and [`samples/Duets/`](../../samples/Duets/)
for executable usage.
