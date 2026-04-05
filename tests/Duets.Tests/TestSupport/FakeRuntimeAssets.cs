using System.Reflection;
using Jint;
using Jint.Native;

namespace Duets.Tests.TestSupport;

internal static class FakeRuntimeAssets
{
    private const string TypeScriptRuntime = """
        var ts = {
            version: "0.test",
            ScriptTarget: { ESNext: 99 },
            ModuleKind: { None: 0 },
            ScriptSnapshot: {
                fromString: function (text) {
                    return {
                        getText: function (start, end) { return text.substring(start, end); },
                        getLength: function () { return text.length; },
                        getChangeRange: function () { return undefined; }
                    };
                }
            },
            transpile: function (input, _, fileName, diagnostics, moduleName) {
                if (diagnostics && input.indexOf("syntaxError") >= 0) {
                    diagnostics.push({
                        start: 0,
                        length: 11,
                        messageText: "Unexpected token",
                        category: 1,
                        code: 1001
                    });
                }

                return "/*" + (fileName || "") + "|" + (moduleName || "") + "*/\n"
                    + input.replace(/: number/g, "");
            },
            createLanguageService: function (host) {
                return {
                    getCompletionsAtPosition: function (fileName, position) {
                        var source = host.readFile(fileName) || "";
                        var before = source.substring(0, position);

                        if (before.lastIndexOf("Math.") === before.length - 5) {
                            if (!host.fileExists("lib.es5.d.ts")) {
                                return null;
                            }

                            return { entries: [{ name: "abs", kind: "method", sortText: "0" }] };
                        }

                        if (before.lastIndexOf("nullResult.") === before.length - 11) {
                            return null;
                        }

                        if (before.lastIndexOf("noEntries.") === before.length - 10) {
                            return {};
                        }

                        return { entries: [] };
                    }
                };
            }
        };
        """;

    private const string LibEs5Declaration = """
        interface Math {
            abs(value: number): number;
        }
        declare const Math: Math;
        """;

    private const string BabelRuntime = """
        var Babel = {
            version: "test",
            transform: function (input, options) {
                if (input.indexOf("syntaxError") >= 0) {
                    throw new Error("Unexpected token");
                }

                return {
                    code: "/*" + options.filename + "*/\n" + input.replace(/: number/g, "")
                };
            }
        };
        """;

    public static Task<TypeScriptService> CreateTypeScriptServiceAsync(TypeDeclarations declarations)
    {
        return TypeScriptService.CreateAsync(
            declarations,
            new TypeScriptServiceOptions
            {
                TypeScriptJs = AssetSources.From(_ => Task.FromResult(TypeScriptRuntime)),
                LibEs5Source = _ => AssetSources.From(_ => Task.FromResult(LibEs5Declaration)),
            },
            false
        );
    }

    public static Task<BabelTranspiler> CreateBabelTranspilerAsync()
    {
        return BabelTranspiler.CreateAsync(
            new BabelTranspilerOptions
            {
                BabelJs = AssetSources.From(_ => Task.FromResult(BabelRuntime)),
            }
        );
    }

    public static async Task<TypeScriptService> CreateInitializedTypeScriptServiceAsync(
        TypeDeclarations declarations,
        bool includeStdLib = false)
    {
        return await TypeScriptService.CreateAsync(
            declarations,
            new TypeScriptServiceOptions
            {
                TypeScriptJs = AssetSources.From(_ => Task.FromResult(TypeScriptRuntime)),
                LibEs5Source = _ => AssetSources.From(_ => Task.FromResult(LibEs5Declaration)),
            },
            includeStdLib
        );
    }

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
