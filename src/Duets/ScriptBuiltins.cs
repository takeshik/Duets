using System.Reflection;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets;

/// <summary>Extension methods for registering built-in script globals into a <see cref="ScriptEngine"/>.</summary>
public static class ScriptBuiltins
{
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
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Duets.Resources.ScriptEngineInit.d.ts")!;
        using var reader = new StreamReader(stream);
        ts.RegisterDeclaration(reader.ReadToEnd());
        return engine;
    }
}
