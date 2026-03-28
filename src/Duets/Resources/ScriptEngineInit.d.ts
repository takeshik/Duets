/**
 * Returns a namespace reference that lets you access CLR types within the given namespace.
 * This function is provided by Jint's CLR interop layer when the engine is configured with `AllowClr`.
 *
 * Indexing the returned object by type name gives a CLR type reference, which can be passed to
 * `typings.useType()`, `typings.scanAssemblyOf()`, `typings.useAssemblyOf()`, or `clrTypeOf()`.
 *
 * ```ts
 * var IO = importNamespace('System.IO');
 * IO.File.ReadAllText('/path/to/file')   // call static methods
 * new IO.StreamReader('/path/to/file')   // construct instances
 * typings.useType(IO.File)               // pass as a type reference
 * clrTypeOf(IO.File).FullName            // => "System.IO.File"
 * ```
 *
 * **Note:** This function provides runtime access only. It does **not** register types for
 * TypeScript completions. Use `typings.importNamespace()` instead when you want both
 * runtime access and editor completions in one call.
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
     *   typings.useType(IO.File);
     *   // Now: IO.File.ReadAllText(...)  ← completions available
     *   ```
     * - Assembly-qualified name string — the format accepted by .NET's `Type.GetType()`:
     *   `"FullTypeName, AssemblyName"` (version/culture/token are optional for most BCL types):
     *   ```ts
     *   typings.useType('System.IO.File, System.Runtime');
     *   ```
     *   A bare type name without an assembly qualifier only resolves types in the core runtime
     *   assembly, so always include the assembly name for non-trivial types.
     *
     * Use `useType` when you need completions for one specific type.
     * Use `useNamespace` or `useAssembly` to register many types at once.
     */
    useType(type: any): void;

    /**
     * Registers namespace skeletons from an assembly so its namespaces appear in
     * completions — but **without** individual type members.
     *
     * After calling this, you can navigate into namespaces (e.g. `System.Net.Http.`)
     * and see sub-namespaces, but classes, methods, and properties are not listed.
     * Use `useAssembly` instead when you need full type members.
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
     * Prefer `scanAssembly` / `scanAssemblyOf` over `useAssembly` for large assemblies
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
     *   typings.useAssembly('System.Net.Http');
     *   ```
     * - Full assembly name string — the value of `Assembly.FullName`:
     *   ```ts
     *   typings.useAssembly('System.Net.Http, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a');
     *   ```
     * - `Assembly` object — obtain one from `clrTypeOf(type).Assembly` when you already
     *   have a type reference in hand:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.useAssembly(clrTypeOf(NetHttp.HttpClient).Assembly);
     *   ```
     *
     * This registers every exported type, so it may be slow for large assemblies
     * (e.g. the BCL). Use `useType` or `useNamespace` for finer-grained control,
     * or `scanAssembly` if you only need namespace-level navigation.
     */
    useAssembly(assembly: any): void;

    /**
     * Registers all public types from the assembly that contains the given type —
     * a shorthand for `useAssembly` when you already have a type reference in hand.
     *
     * **Accepted argument forms**
     * - CLR type reference only — strings are **not** accepted:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.useAssemblyOf(NetHttp.HttpClient);
     *   // All public types in System.Net.Http.dll are now available with full completions
     *   ```
     *
     * If you only have an assembly name string, use `useAssembly('AssemblyName')` instead.
     */
    useAssemblyOf(type: any): void;

    /**
     * Imports a .NET namespace into the script environment and registers its public types
     * for TypeScript completions — combining `importNamespace` and `useNamespace` in one call.
     *
     * **Accepted argument forms**
     * - Namespace name string:
     *   ```ts
     *   var IO = typings.importNamespace('System.IO');
     *   // IO.File, IO.StreamReader, ... now have completions
     *   ```
     * - Namespace reference — when you already have one (e.g. from a prior `importNamespace` call):
     *   ```ts
     *   typings.importNamespace(System.Net.Http);
     *   ```
     *
     * Only types whose `Namespace` exactly matches the given string are registered;
     * nested namespaces are **not** included automatically — call `importNamespace` again
     * for each sub-namespace you need:
     * ```ts
     * typings.importNamespace('System.Net.Http');
     * typings.importNamespace('System.Net.Http.Headers');
     * ```
     */
    importNamespace(ns: any): any;

    /**
     * Registers all public types in the given namespace so their members appear in
     * TypeScript completions.
     *
     * **Accepted argument forms**
     * - Namespace reference — the object returned by `importNamespace`:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.useNamespace(NetHttp);
     *   // Now: new NetHttp.HttpClient()  ← completions available
     *   ```
     * - Plain string — the namespace name as a string literal:
     *   ```ts
     *   typings.useNamespace('System.Net.Http');
     *   ```
     *
     * Only types whose `Namespace` exactly matches the given string are registered;
     * nested namespaces (e.g. `System.Net.Http.Headers`) are **not** included automatically.
     * Call `useNamespace` again for each sub-namespace you need.
     *
     * Types are searched across all assemblies already loaded in the current AppDomain.
     * If the target assembly has not been loaded yet, trigger a load first — for example,
     * by calling `importNamespace` or referencing a type from that assembly:
     * ```ts
     * importNamespace('System.Net.Http');        // triggers assembly load
     * typings.useNamespace('System.Net.Http');
     * ```
     */
    useNamespace(ns: any): void;
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
 * To pass the assembly to `typings.scanAssembly` or `typings.useAssembly`, read
 * the `Assembly` property:
 * ```ts
 * var IO = importNamespace('System.IO');
 * typings.useAssembly(clrTypeOf(IO.File).Assembly);
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
