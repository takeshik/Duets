using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets;

/// <summary>Extension methods for registering built-in script globals into a <see cref="ScriptEngine"/>.</summary>
public static class ScriptBuiltins
{
    private const string TypingsDeclaration = """
        declare const typings: {
            /** Registers a single .NET type by assembly-qualified name as a TypeScript declaration target. */
            use(assemblyQualifiedName: string): void;
            /** Loads an assembly and registers namespace skeleton declarations so that its namespaces appear in completions (no type members). */
            scanAssembly(assemblyName: string): void;
            /** Loads an assembly and registers all public types as TypeScript declaration targets. */
            useAssembly(assemblyName: string): void;
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
