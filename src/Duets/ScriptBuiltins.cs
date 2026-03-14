namespace Duets;

/// <summary>Extension methods for registering built-in script globals into a <see cref="ScriptEngine"/>.</summary>
public static class ScriptBuiltins
{
    /// <summary>
    /// Registers the <c>typings</c> global object into the engine, which exposes type-declaration
    /// management functions (<c>use</c>, <c>scanAssembly</c>, <c>useAssembly</c>, <c>useNamespace</c>)
    /// to scripts running in the engine.
    /// </summary>
    public static ScriptEngine RegisterTypeBuiltins(this ScriptEngine engine, TypeScriptService ts)
    {
        engine.SetValue("typings", new ScriptTypings(ts));
        return engine;
    }
}
