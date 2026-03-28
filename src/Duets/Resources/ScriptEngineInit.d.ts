declare const typings: {
    /**
     * Registers a single .NET type so its members appear in TypeScript completions.
     *
     * **Calling styles**
     * - CLR type reference — use the type directly after importing its namespace:
     *   ```ts
     *   var IO = importNamespace('System.IO');
     *   typings.useType(IO.File);
     *   // Now: IO.File.ReadAllText(...)  ← completions available
     *   ```
     * - Assembly-qualified name string — useful when the type is not yet accessible as a reference:
     *   ```ts
     *   typings.useType('System.IO.File, System.Runtime');
     *   ```
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
     * **Calling styles**
     * - Assembly name string (the value of `Assembly.FullName`):
     *   ```ts
     *   typings.scanAssembly('System.Net.Http, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a');
     *   ```
     * - `Assembly` object obtained at runtime:
     *   ```ts
     *   var clr = importNamespace('System.Reflection');
     *   var asm = clr.Assembly.Load('System.Net.Http');
     *   typings.scanAssembly(asm);
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
     * ```ts
     * var NetHttp = importNamespace('System.Net.Http');
     * typings.scanAssemblyOf(NetHttp.HttpClient);
     * // Navigating System.Net.Http. now shows sub-namespaces
     * ```
     *
     * Only type references (CLR objects) are accepted; strings are not supported here.
     * Use `scanAssembly('Assembly.FullName, ...')` if you only have a name string.
     */
    scanAssemblyOf(type: any): void;

    /**
     * Registers **all public types** from an assembly so their members appear in
     * TypeScript completions.
     *
     * **Calling styles**
     * - Assembly name string:
     *   ```ts
     *   typings.useAssembly('System.Net.Http, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a');
     *   ```
     * - `Assembly` object:
     *   ```ts
     *   var clr = importNamespace('System.Reflection');
     *   var asm = clr.Assembly.Load('System.Net.Http');
     *   typings.useAssembly(asm);
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
     * ```ts
     * var NetHttp = importNamespace('System.Net.Http');
     * typings.useAssemblyOf(NetHttp.HttpClient);
     * // All public types in System.Net.Http.dll are now available with full completions
     * ```
     *
     * Only type references (CLR objects) are accepted; strings are not supported here.
     * Use `useAssembly('Assembly.FullName, ...')` if you only have a name string.
     */
    useAssemblyOf(type: any): void;

    /**
     * Registers all public types in the given namespace so their members appear in
     * TypeScript completions.
     *
     * **Calling styles**
     * - Namespace reference — use after importing the namespace with `importNamespace`:
     *   ```ts
     *   var NetHttp = importNamespace('System.Net.Http');
     *   typings.useNamespace(NetHttp);
     *   // Now: new NetHttp.HttpClient()  ← completions available
     *   ```
     * - Plain string — use when you have not imported the namespace yet:
     *   ```ts
     *   typings.useNamespace('System.Net.Http');
     *   ```
     *
     * Only types whose `Namespace` exactly matches the given string are registered;
     * nested namespaces (e.g. `System.Net.Http.Headers`) are **not** included automatically.
     * Call `useNamespace` again for each sub-namespace you need.
     *
     * Types are searched across all assemblies already loaded in the current AppDomain.
     * If the target assembly has not been loaded yet, load it first:
     * ```ts
     * importNamespace('System.Net.Http');  // triggers assembly load
     * typings.useNamespace('System.Net.Http');
     * ```
     */
    useNamespace(ns: any): void;
};

/**
 * Returns the underlying `System.Type` object for a CLR type reference.
 *
 * ```ts
 * var IO = importNamespace('System.IO');
 * clrTypeOf(IO.File).FullName   // => "System.IO.File"
 * clrTypeOf(IO.File).Assembly   // => the assembly object
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
