using System.Reflection;
using Jint;
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
        // Capture the Jint-provided importNamespace before overriding it.
        var jintEngine = engine.JintEngine;
        var originalImportNs = engine.GetValue("importNamespace");
        Func<JsValue, JsValue>? importNsFn = !originalImportNs.IsUndefined()
            ? ns => jintEngine.Call(originalImportNs, ns)
            : null;

        Action<string, Type> exposeGlobal = (name, type) =>
        {
            var typeRef = TypeReference.CreateTypeReference(jintEngine, type);
            jintEngine.SetValue(name, typeRef);
        };

        var typings = new ScriptTypings(ts, importNsFn, exposeGlobal);
        engine.SetValue("typings", typings);

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
