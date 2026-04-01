/**
 * Returns a namespace reference that lets you access CLR types within the given namespace.
 * This function is provided by Jint's CLR interop layer when the engine is configured with `AllowClr`.
 *
 * Indexing the returned object by type name gives a CLR type reference, which can be passed to
 * `typings.importType()`, `typings.scanAssemblyOf()`, `typings.importAssemblyOf()`, or `clrTypeOf()`.
 *
 * ```ts
 * var IO = importNamespace('System.IO');
 * IO.File.ReadAllText('/path/to/file')   // call static methods
 * new IO.StreamReader('/path/to/file')   // construct instances
 * typings.importType(IO.File)            // pass as a type reference
 * clrTypeOf(IO.File).FullName            // => "System.IO.File"
 * ```
 *
 * **Note:** This function provides runtime access only. It does **not** register types for
 * TypeScript completions. Use `typings.importNamespace()` for runtime access with completions,
 * or `typings.usingNamespace()` to also scatter types as globals (C# `using` semantics).
 */
declare function importNamespace(ns: string): any;

declare const typings: {
    /**
     * Registers a single .NET type so its members appear in TypeScript completions.
     *
     * **Accepted argument forms**
     * - CLR type reference — index a namespace reference obtained from `importNamespace`:
     *   ```ts
     *   var IO = importNamespace('System.IO');
     *   typings.importType(IO.File);
     *   // Now: IO.File.ReadAllText(...)  ← completions available
     *   ```
     * - Assembly-qualified name string — the format accepted by .NET's `Type.GetType()`:
     *   `"FullTypeName, AssemblyName"` (version/culture/token are optional for most BCL types):
     *   ```ts
     *   typings.importType('System.IO.File, System.Runtime');
     *   ```
     *   A bare type name without an assembly qualifier only resolves types in the core runtime
     *   assembly, so always include the assembly name for non-trivial types.
     *
     * Use `importType` when you need completions for one specific type.
     * Use `importNamespace` or `importAssembly` to register many types at once.
     */
    importType(type: any): void;

    /**
     * Registers namespace skeletons from an assembly so its namespaces appear in
     * completions — but **without** individual type members.
     *
     * After calling this, you can navigate into namespaces (e.g. `System.Net.Http.`)
     * and see sub-namespaces, but classes, methods, and properties are not listed.
     * Use `importAssembly` instead when you need full type members.
     *
     * **Accepted argument forms**
     * - Simple assembly name string — the short name without version or public key token:
     *   ```ts
     *   typings.scanAssembly('System.Net.Http');
     *   ```
     * - Full assembly name string — the value of `Assembly.FullName`:
     *   ```ts
     *   typings.scanAssembly('System.Net.Http, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a');
     *   ```
     * - `Assembly` object — obtain one from `clrTypeOf(type).Assembly` when you already
     *   have a type reference in hand:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.scanAssembly(clrTypeOf(NetHttp.HttpClient).Assembly);
     *   ```
     *
     * Prefer `scanAssembly` / `scanAssemblyOf` over `importAssembly` for large assemblies
     * when you only need namespace-level navigation and want to keep startup fast.
     */
    scanAssembly(assembly: any): void;

    /**
     * Registers namespace skeletons from the assembly that contains the given type —
     * a shorthand for `scanAssembly` when you already have a type reference in hand.
     *
     * **Accepted argument forms**
     * - CLR type reference only — strings are **not** accepted:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.scanAssemblyOf(NetHttp.HttpClient);
     *   // Navigating System.Net.Http. now shows sub-namespaces
     *   ```
     *
     * If you only have an assembly name string, use `scanAssembly('AssemblyName')` instead.
     */
    scanAssemblyOf(type: any): void;

    /**
     * Registers **all public types** from an assembly so their members appear in
     * TypeScript completions.
     *
     * **Accepted argument forms**
     * - Simple assembly name string — the short name without version or public key token:
     *   ```ts
     *   typings.importAssembly('System.Net.Http');
     *   ```
     * - Full assembly name string — the value of `Assembly.FullName`:
     *   ```ts
     *   typings.importAssembly('System.Net.Http, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a');
     *   ```
     * - `Assembly` object — obtain one from `clrTypeOf(type).Assembly` when you already
     *   have a type reference in hand:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.importAssembly(clrTypeOf(NetHttp.HttpClient).Assembly);
     *   ```
     *
     * This registers every exported type, so it may be slow for large assemblies
     * (e.g. the BCL). Use `importType` or `importNamespace` for finer-grained control,
     * or `scanAssembly` if you only need namespace-level navigation.
     */
    importAssembly(assembly: any): void;

    /**
     * Registers all public types from the assembly that contains the given type —
     * a shorthand for `importAssembly` when you already have a type reference in hand.
     *
     * **Accepted argument forms**
     * - CLR type reference only — strings are **not** accepted:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.importAssemblyOf(NetHttp.HttpClient);
     *   // All public types in System.Net.Http.dll are now available with full completions
     *   ```
     *
     * If you only have an assembly name string, use `importAssembly('AssemblyName')` instead.
     */
    importAssemblyOf(type: any): void;

    /**
     * Imports a .NET namespace and registers its public types for TypeScript completions,
     * returning the namespace reference for further use.
     *
     * **Accepted argument forms**
     * - Namespace name string:
     *   ```ts
     *   var IO = typings.importNamespace('System.IO');
     *   // IO.File, IO.StreamReader, ... now have completions
     *   ```
     * - Namespace reference — when you already have one (e.g. from a prior `importNamespace` call):
     *   ```ts
     *   var IO = typings.importNamespace(System.IO);
     *   ```
     *
     * Only types whose `Namespace` exactly matches the given string are registered;
     * nested namespaces are **not** included automatically — call `importNamespace` again
     * for each sub-namespace you need:
     * ```ts
     * typings.importNamespace('System.Net.Http');
     * typings.importNamespace('System.Net.Http.Headers');
     * ```
     *
     * To also scatter types as globals (C# `using` semantics), use `usingNamespace` instead.
     */
    importNamespace(ns: any): any;

    /**
     * C# `using` equivalent — imports a namespace, registers its types for completions,
     * and exposes each type as a global variable so they can be used without a namespace prefix.
     *
     * ```ts
     * typings.usingNamespace('System.IO');
     * // or with a namespace reference:
     * typings.usingNamespace(System.IO);
     *
     * // After this call, types are accessible directly:
     * new FileInfo('/path/to/file')          // no System.IO. prefix needed
     * File.ReadAllText('/path/to/file')
     * // and completions are available for all exposed types
     * ```
     *
     * Only types whose `Namespace` exactly matches the given string are registered;
     * nested namespaces are **not** included automatically — call `usingNamespace` again
     * for each sub-namespace you need:
     * ```ts
     * typings.usingNamespace('System.Net.Http');
     * typings.usingNamespace('System.Net.Http.Headers');
     * ```
     *
     * Use `importNamespace` instead if you want to keep the namespace prefix
     * (i.e. `IO.File` rather than `File`).
     */
    usingNamespace(ns: any): void;
};

/**
 * Returns the underlying `System.Type` object for a CLR type reference.
 *
 * **Accepted argument forms**
 * - CLR type reference — index a namespace reference obtained from `importNamespace`.
 *   Strings are **not** accepted:
 *   ```ts
 *   var IO = importNamespace('System.IO');
 *   clrTypeOf(IO.File).FullName      // => "System.IO.File"
 *   clrTypeOf(IO.File).Namespace     // => "System.IO"
 *   clrTypeOf(IO.File).Assembly      // => Assembly object for System.Runtime
 *   ```
 *
 * The returned `System.Type` object exposes the full .NET reflection API — you can
 * read `FullName`, `Assembly`, `IsAbstract`, call `GetMethods()`, and so on.
 *
 * To pass the assembly to `typings.scanAssembly` or `typings.importAssembly`, read
 * the `Assembly` property:
 * ```ts
 * var IO = importNamespace('System.IO');
 * typings.importAssembly(clrTypeOf(IO.File).Assembly);
 * ```
 */
declare function clrTypeOf(type: any): any;

declare const util: {
    /**
     * Formats a value as a human-readable string, similar to Node.js `util.inspect`.
     *
     * - Objects and arrays are pretty-printed as JSON with 2-space indentation by default.
     * - Strings are JSON-quoted (use `console.log` if you want unquoted strings).
     * - Circular references are replaced with `"[Circular]"`.
     * - Values nested deeper than `opts.depth` (default: `2`) are replaced with
     *   `"[Object]"` or `"[Array]"`.
     *
     * ```ts
     * util.inspect({x: 1, y: [2, 3]})
     * // => '{\n  "x": 1,\n  "y": [\n    2,\n    3\n  ]\n}'
     *
     * util.inspect({x: 1, y: 2}, {compact: true})
     * // => '{"x":1,"y":2}'
     * ```
     */
    inspect(value: unknown, opts?: { depth?: number; compact?: boolean }): string;
};

/**
 * Outputs `value` to the REPL output pane (as a `log`-level entry) and returns it
 * unchanged, so it can be inserted anywhere in an expression chain without breaking it.
 *
 * ```ts
 * dump(someArray)          // prints the array, returns it
 * dump(obj).someProperty   // prints obj, then accesses .someProperty — type is preserved
 * ```
 *
 * The output format is the same as `util.inspect` (pretty-printed JSON).
 * Use `console.log` instead if you want unquoted strings or multiple arguments.
 */
declare function dump<T>(value: T, opts?: { depth?: number; compact?: boolean }): T;
