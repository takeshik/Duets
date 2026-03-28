declare const typings: {
    /** Registers a single .NET type. Accepts a CLR type reference (e.g. System.IO.File) or an assembly-qualified name string. */
    useType(type: any): void;
    /** Registers namespace skeleton declarations from an assembly (name string or Assembly object), so namespaces appear in completions (no type members). */
    scanAssembly(assembly: any): void;
    /** Registers namespace skeleton declarations from the assembly containing the given type reference. */
    scanAssemblyOf(type: any): void;
    /** Loads an assembly (name string or Assembly object) and registers all public types as TypeScript declaration targets. */
    useAssembly(assembly: any): void;
    /** Registers all public types from the assembly containing the given type reference. */
    useAssemblyOf(type: any): void;
    /** Registers all public types in the given namespace. Accepts a namespace reference (e.g. System.Net.Http) or a plain string. */
    useNamespace(ns: any): void;
};

/** Returns the underlying System.Type for a CLR type reference (e.g. clrTypeOf(System.IO.File)). */
declare function clrTypeOf(type: any): any;

declare const util: {
    /** Formats a value as a string for inspection, similar to Node.js util.inspect. */
    inspect(value: unknown, opts?: { depth?: number; compact?: boolean }): string;
};

/** Outputs `value` to the REPL output pane and returns it unchanged, enabling use in the middle of an expression chain. */
declare function dump<T>(value: T, opts?: { depth?: number; compact?: boolean }): T;
