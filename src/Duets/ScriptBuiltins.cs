using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets;

/// <summary>Extension methods for registering built-in script globals into a <see cref="ScriptEngine"/>.</summary>
public static class ScriptBuiltins
{
    private const string TypingsDeclaration = """
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
        """;

    /// <summary>
    /// Registers the <c>typings</c> global object and the <c>clrTypeOf</c> function into the engine.
    /// </summary>
    public static ScriptEngine RegisterTypeBuiltins(this ScriptEngine engine, TypeScriptService ts)
    {
        engine.SetValue("typings", new ScriptTypings(ts));
        engine.SetValue(
            "clrTypeOf",
            new Func<JsValue, object>(jsValue =>
                {
                    if (jsValue is TypeReference tr) return tr.ReferenceType;
                    throw new ArgumentException(
                        "Expected a CLR type reference (e.g., clrTypeOf(System.IO.File)). " +
                        "Make sure AllowClr is configured on the engine."
                    );
                }
            )
        );
        ts.RegisterDeclaration(TypingsDeclaration);
        return engine;
    }
}
