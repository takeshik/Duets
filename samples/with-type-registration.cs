// With .NET type registration
//
// AllowClr() enables CLR interop in scripts.
// RegisterTypeBuiltins() adds the `typings` global, which exposes .NET types
// to both scripts and the Monaco IntelliSense completions.
//
// Alternative transpiler: swap TypeScriptService for BabelTranspiler if you need
// a lighter-weight transpiler that does not rely on the official TypeScript compiler.
// BabelTranspiler supports most TypeScript syntax but does not provide completions.
//
//   using var babel = new BabelTranspiler();
//   await babel.InitializeAsync();
//   using var engine = new ScriptEngine(opts => opts.AllowClr(), babel);
#:project ../src/Duets/Duets.csproj

using Duets;
using Jint;

var declarations = new TypeDeclarations();
using var ts = new TypeScriptService(declarations);
await ts.ResetAsync();

using var engine = new ScriptEngine(opts => opts.AllowClr(), ts);
engine.RegisterTypeBuiltins(declarations);

// From a script, use `typings` to register types for runtime access AND completions:
//
//   typings.usingNamespace("System.IO")                      // C# `using` semantics: scatter types as globals + completions
//   typings.usingNamespace(System.IO)                        //   same, but with a namespace reference
//   typings.importNamespace("System.IO")                     // import namespace: runtime + completions, keeps namespace prefix
//   typings.importNamespace(System.IO)                       //   same, but with a namespace reference
//   typings.importType(System.IO.File)                       // single type via CLR reference
//   typings.importType("System.IO.File, System.IO.FileSystem") // via assembly-qualified name
//   typings.scanAssembly("System.Net.Http")                  // namespace skeletons only
//   typings.scanAssemblyOf(System.IO.File)                   // namespace skeletons from the type's assembly
//   typings.importAssembly("System.Net.Http")                // all public types
//   typings.importAssemblyOf(System.IO.File)                 // all public types from the type's assembly
//
// Note: the global importNamespace() provided by AllowClr gives runtime access only.
// Use typings.importNamespace() or typings.usingNamespace() when you also want IntelliSense completions.

// Use usingNamespace to bring types into scope like C#'s `using System.IO;`
var result = engine.Evaluate("""
    typings.usingNamespace("System.IO");
    Directory.GetCurrentDirectory()
    """);
Console.WriteLine(result); // e.g. /home/user/myproject
