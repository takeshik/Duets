using System.Reflection;
using Duets.Jint;
using Jint;
using Jint.Native;

namespace Duets.Tests.TestSupport;

internal static class TypeScriptServiceTestHelpers
{
    public static IReadOnlyDictionary<string, string> GetLanguageServiceFiles(TypeScriptService service)
    {
        var host = GetEngine(service).GetValue("$$host");
        var fileNames = ((JsArray) host.Get("getScriptFileNames").Call(host))
            .Select(value => value.AsString())
            .ToList();

        return fileNames.ToDictionary(
            fileName => fileName,
            fileName => host.Get("readFile").Call(host, [fileName]).AsString()
        );
    }

    public static Engine GetEngine(TypeScriptService service)
    {
        return (Engine) typeof(TypeScriptService)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;
    }
}
