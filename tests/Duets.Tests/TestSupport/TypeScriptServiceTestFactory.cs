using System.Reflection;
using Jint;

namespace Duets.Tests.TestSupport;

internal static class TypeScriptServiceTestFactory
{
    public static TypeScriptService CreateInitializedService()
    {
        var service = new TypeScriptService();
        var engine = new Engine();
        engine.Execute(
            """
            globalThis.__addedFiles = {};
            globalThis.$$host = {
                addFile(name, content) {
                    globalThis.__addedFiles[name] = content;
                }
            };
            """
        );

        typeof(TypeScriptService)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, engine);

        return service;
    }
}
