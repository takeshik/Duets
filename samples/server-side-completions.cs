// Server-side completions with TypeScriptService
//
// BabelTranspiler (the default) does not include a language service; completions
// in the web REPL are handled client-side by Monaco. TypeScriptService runs the
// full TypeScript compiler in-process and exposes GetCompletions() for programmatic
// use — for example, to power completions in a custom editor or a CLI tool.
#:project ../src/Duets.Jint/Duets.Jint.csproj

using Duets;
using Duets.Jint;
using Jint;

// TypeScriptService requires the session-owned TypeDeclarations, so pass a factory.
// injectStdLib: true fetches lib.es5.d.ts from CDN so JS built-in completions
// (Array, Math, string, etc.) are available alongside registered .NET types.
using var session = await DuetsSession.CreateAsync(config => config
    .UseTranspiler(decls => TypeScriptService.CreateAsync(decls, injectStdLib: true))
    .UseJint(opts => opts.AllowClr()));

// Cast to TypeScriptService to access GetCompletions.
var ts = (TypeScriptService)session.Transpiler;

// usingNamespace registers the types AND exposes them as globals, so completions
// work with the short name (Path) rather than the fully-qualified name.
session.Execute("typings.usingNamespace('System.IO')");

var completions = ts.GetCompletions("Path.", 5);
Console.WriteLine($"Path.* ({completions.Count} completions):");
foreach (var c in completions.Take(8))
    Console.WriteLine($"  {c.Name} ({c.Kind})");

// Completions also work for JS built-ins (requires injectStdLib: true).
var mathCompletions = ts.GetCompletions("Math.", 5);
Console.WriteLine($"\nMath.* ({mathCompletions.Count} completions):");
foreach (var c in mathCompletions.Take(8))
    Console.WriteLine($"  {c.Name} ({c.Kind})");
