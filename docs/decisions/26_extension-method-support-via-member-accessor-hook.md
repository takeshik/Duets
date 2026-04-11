# ADR-26: Extension Method Support via `MemberAccessor` Hook and `ExtensionMethodRegistry`

## Status

Accepted

## Context

Script authors who import .NET namespaces and work with CLR objects expect extension methods
to be callable as instance methods in JavaScript, mirroring how C# callers use them. This
applies to standard library extensions (e.g. `IEnumerable<T>` LINQ operators) and to
application-defined extension methods the host wants to expose to scripts. Without this,
callers must invoke static methods manually (`Enumerable.Select(xs, x => x * 2)`), which is
unergonomic.

Two requirements must be met simultaneously:
1. **Runtime dispatch** — calling `xs.Select(x => x * 2)` on a CLR-wrapped object must
   invoke the correct .NET extension method.
2. **Completion declarations** — the Monaco editor must offer `select`, `where`, etc. as
   method completions on the object's TypeScript type.

## Decision Drivers

- Extension methods must be registerable **after** `ScriptEngine` is constructed (the
  `typings.addExtensionMethods(type)` call happens at script runtime, not at engine
  construction time).
- Jint's built-in `Options.AddExtensionMethods(Type[])` is construction-time only and cannot
  accept new types after the engine is created.
- `Engine._extensionMethods` (Jint's internal extension method cache) is `internal readonly`
  and not accessible from outside.
- The solution must handle generic extension methods (e.g. `Select<TResult>`) with JS
  closures as arguments.

## Considered Alternatives

### A: Jint's `Options.AddExtensionMethods`

Jint has built-in support for CLR extension methods, registered via `Options.AddExtensionMethods(types)`.

- Pro: No extra code; Jint handles dispatch and overload resolution automatically.
- Con: Registration is construction-time only. `typings.addExtensionMethods` is called at
  script runtime, after `ScriptEngine` is already built. This alternative is therefore
  **not usable** for the runtime-registration use case.

### B: JavaScript prototype injection with static-call wrappers

Inject JavaScript wrapper methods that delegate to static CLR calls:

```js
// generated and executed when addExtensionMethods(Enumerable) is called
Array.prototype.Select = function(selector) {
    return System.Linq.Enumerable.Select(this, selector);
};
```

- Pro: No special Jint hook required; the wrapper is plain JavaScript.
- Con: **Cannot cover arbitrary CLR receivers without unacceptable prototype pollution.**
  Jint's CLR wrappers can still observe `Object.prototype`, so making wrapper methods
  available on every possible CLR receiver would require injecting methods into
  `Object.prototype`, which would also affect every plain JavaScript object. Narrower
  targets such as `Array.prototype` are not sufficient for the general extension-method
  use case: they do not cover arbitrary CLR objects, and many CLR receivers of interest
  (for example general `IEnumerable<T>` implementations) are not meaningfully modeled as
  JavaScript arrays.

### C: `Options.Interop.MemberAccessor` hook + `ExtensionMethodRegistry`

Jint exposes `Options.Interop.MemberAccessor`: a delegate called on every CLR-object member
access before the default type resolver. This hook can close over mutable state.

The `ExtensionMethodRegistry` class maintains a thread-safe dictionary of extension methods
grouped by their target type. `ScriptEngine` configures the hook at construction, chaining
the host's existing accessor with the registry lookup. Calling `typings.addExtensionMethods`
adds to the registry at any time.

- Pro: Fully dynamic — new extension methods are visible immediately after registration.
- Pro: No Jint internals accessed; `MemberAccessor` is a documented, public API.
- Pro: Works for all target types (concrete, generic, interfaces, base types).
- Con: The hook is called on every CLR-object member access that misses the normal
  resolver, adding minor overhead.
- Con: Generic method type arguments must be inferred or substituted at dispatch time.

## Decision

**Alternative C** was chosen.

`ExtensionMethodRegistry` is a new internal component that:
- Holds a snapshot-replaced `Dictionary<Type, MethodInfo[]>` (keyed by open-generic or
  concrete target type) for lock-free reads.
- `Register(Type containerType)` scans the container for `[ExtensionAttribute]` methods
  and merges them atomically using `Interlocked.CompareExchange`.
- `CreateMemberValue(Engine, object, string)` returns a `ClrFunction` that dispatches to
  matching overloads by walking the target's type hierarchy (interfaces and base types).

### Generic method dispatch

For generic extension methods (e.g. `Map<TResult>(this Item, Func<Item, TResult>)`), the
delegate parameter type is open (`Func<Item, TResult>`) and cannot be used as the target of
Jint's `TypeConverter.Convert`. The dispatch strategy is therefore:

1. **Make the method concrete first**, inferring generic type arguments from the `this`
   argument's runtime type. Any type arguments that cannot be inferred (e.g. `TResult`, which
   appears only in the delegate's return type) are substituted with `typeof(object)`.
2. **Convert JS arguments against the concrete parameter types** (`Func<Item, object>` in the
   example). Jint's `DefaultTypeConverter` recognises that the argument is a `JsCallDelegate`
   and creates a typed CLR delegate that calls the JS function when invoked.
3. **Invoke the concrete method** and wrap the result in a `JsValue`.

This means that for return-only type parameters, the returned CLR type will be `object`; the
JS-side result is correctly typed because `JsValue.FromObject` wraps the actual runtime value.

### CLR arrays

Jint has special handling for arrays and would normally expose many CLR arrays as JavaScript
arrays, bypassing the CLR-member pipeline that `MemberAccessor` depends on. To keep extension
methods operating on CLR arrays, `ScriptEngine` registers a narrow `IObjectConverter` that
wraps `System.Array` instances as `ObjectWrapper`s instead of letting them become plain JS arrays.

This keeps CLR arrays in general inside the same extension-method mechanism as other CLR
objects, including values supplied by the host and arrays returned from CLR method calls.

Support that specifically depends on `T[]` semantics remains limited to one-dimensional arrays:
- runtime generic-array receiver matching in `ExtensionMethodRegistry`
- projected TypeScript `Array<T>` augmentations for array-shaped receivers

Pure JavaScript arrays remain out of scope for this feature. If script code needs a CLR
collection, it must convert explicitly rather than relying on extension-method dispatch over
JS-native array values. Duets therefore provides `util.toJsArray(value)` as an explicit
escape hatch when a CLR array needs to become a native JavaScript array.

### TypeScript declarations

`ClrDeclarationGenerator.GenerateExtensionMethodsTs(Type containerType)` produces TypeScript
interface augmentation declarations. Declaration generation now uses the same CLR-to-TypeScript
projection rules as ordinary type mapping:

```ts
declare namespace MyApp {
    interface Item {
        describe(): string;
        withValue(value: number): Item;
        map<TResult>(selector: (arg0: Item) => TResult): TResult;
    }
}
```

This keeps completion declarations aligned with how CLR values are exposed elsewhere in
generated `.d.ts` files:

- The **declared CLR receiver type** is always augmented when it has a named TypeScript
  declaration.
- If the receiver is exposed through a **lossy projection** in value positions
  (for example `IEnumerable<T>`/`List<T>` as `T[]`), the corresponding projected TypeScript
  surface is also augmented.
- Other named CLR types in the same lossy projection family that are assignable to the
  receiver are augmented too, so completions remain visible on directly-declared CLR types
  such as `List<T>` and `Dictionary<TKey, TValue>`, not only on their projected forms.
- For ordinary interface receivers, generated CLR class declarations include their implemented
  interfaces and a merged interface bridge, so augmentations on the interface surface remain
  visible on implementing CLR classes without special-casing individual interface families.

Type parameters that appear in extension methods targeting generic receivers (e.g.
`TSource` in `Select(this IEnumerable<TSource> source, ...)`) are substituted with the
augmented TypeScript surface's own type parameter names, preserving correct TS signatures
across both direct CLR declarations and projected surfaces.

When a receiver cannot be represented soundly in TypeScript's global `Array<T>` surface
(for example an extension method that specifically targets `byte[]`), declaration generation
does **not** emit a projected array augmentation. Those methods remain available at runtime on
CLR values, but their array-specific completions are intentionally unsupported.

`ScriptBuiltins` wires both runtime registration and declaration generation together behind
the `typings.addExtensionMethods(type)` script API.
