# ADR-31: ScriptEngine Generic Backend Base and IScriptEngine Interface

**Status:** Accepted  
**Keywords:** ScriptEngine, IScriptEngine, ScriptEngine<TValue>, IScriptValueConverter, backend, generics, interface, SetValue, Evaluate

## Abstract

Replaces the non-generic abstract `ScriptEngine` class with a two-layer design:
`IScriptEngine` (interface — public contract held by callers) and
`ScriptEngine<TValue>` (generic abstract class — the base backends inherit from).
`ScriptEngine<TValue>` holds an `IScriptValueConverter<TValue>` and uses it to
implement `SetValue(string, ScriptValue)` and the `Evaluate*` return-value wrapping
concretely, removing these responsibilities from backend subclasses.

## Context

After ADR-30 made `ScriptValue` an abstract class with typed backend subclasses,
`JintScriptEngine` had to implement `SetValue(string, ScriptValue)` by pattern-matching
on `ScriptValue`'s internal type hierarchy — reaching into the abstraction it was
supposed to treat as opaque.

Introducing `IScriptValueConverter<T>` (ADR companion to this record) exposed a
deeper structural opportunity: because the converter knows how to map any `ScriptValue`
to the engine's internal value type `TValue`, the base class itself can implement the
conversion-dependent methods once, without knowing `TValue` statically. This requires
making `ScriptEngine` generic.

The existing non-generic `ScriptEngine` class was also the type held by callers
(`DuetsSession`, `ReplService`, factory delegates). Keeping it as a class while
introducing a generic subclass would allow unintended direct inheritance from the
non-generic layer. Promoting the public contract to an interface makes the intended
inheritance path explicit.

## Decision

**`IScriptEngine`** replaces the non-generic abstract `ScriptEngine` class as the
type held by callers. It declares all public-facing methods and events:
`SetValue`, `Evaluate`, `Execute`, `GetGlobalVariables`, `RegisterTypeBuiltins`,
`CanRegisterTypeBuiltins`, `ConsoleLogged`, `Dispose`.

**`ScriptEngine<TValue>`** is the abstract base all backends must inherit from. It:
- Accepts an `IScriptValueConverter<TValue>` in its constructor (stored as
  `protected IScriptValueConverter<TValue> Converter`).
- Provides a **concrete** `SetValue(string, ScriptValue)` by calling
  `Converter.Unwrap(value)` then the backend hook `SetValue(string, TValue)`.
- Provides **concrete** `Evaluate` / `EvaluateAsync` by calling the backend hooks
  `EvaluateJs` / `EvaluateJsAsync` (returning `TValue`) and wrapping the result
  with `Converter.Wrap(TValue)`.
- Declares the following **protected abstract** backend hooks:
  `SetValue(string, TValue)`, `ExecuteJs`, `ExecuteJsAsync`, `EvaluateJs`,
  `EvaluateJsAsync`.
- `SetValue(string, object)`, `GetGlobalVariables`, `RegisterTypeBuiltins`, and
  `Dispose` remain abstract (backends implement them directly).

`SetValue(string, object)` stays abstract because the conversion from arbitrary CLR
objects to `TValue` is engine-specific (e.g. Jint's `Engine.SetValue(string, object)`
handles `Type → TypeReference` internally; this cannot be replicated at the
`IScriptValueConverter<T>` level without leaking engine internals).

## Considered Alternatives

**Keep non-generic `ScriptEngine` as an additional non-generic intermediate layer**  
A non-generic `ScriptEngine` base that `ScriptEngine<TValue>` extends would preserve
the class hierarchy without requiring callers to change. However, it would allow
unintended direct inheritance from the non-generic layer, and it does not express the
design intent clearly — backends are supposed to inherit from the generic class only.
An interface separates the public contract from the implementation hierarchy cleanly.

**Make `IScriptValueConverter<T>` split into `IScriptValueEncoder<out T>` and
`IScriptValueDecoder<in T>` for variance**  
Variance benefits arise when substituting along the `T` hierarchy. Because `T` is fixed
to one concrete type per backend (`JsValue` for Jint), no such substitution occurs in
practice. The split would double the number of objects and injection points for no
benefit; the unified interface is preferred.
