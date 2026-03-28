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

using var ts = new TypeScriptService();
await ts.ResetAsync();

using var engine = new ScriptEngine(opts => opts.AllowClr(), ts);
engine.RegisterTypeBuiltins(ts);

// From a script, use `typings` to register types for runtime access AND completions:
//
//   typings.importNamespace("System.IO")                     // import namespace: runtime + completions (recommended)
//   typings.importNamespace(System.IO)                       //   same, but with a namespace reference
//   typings.useType(System.IO.File)                          // single type via CLR reference
//   typings.useType("System.IO.File, System.IO.FileSystem")  // via assembly-qualified name
//   typings.scanAssembly("System.Net.Http")                  // namespace skeletons only
//   typings.scanAssemblyOf(System.IO.File)                   // namespace skeletons from the type's assembly
//   typings.useAssembly("System.Net.Http")                   // all public types
//   typings.useAssemblyOf(System.IO.File)                    // all public types from the type's assembly
//   typings.useNamespace(System.Net.Http)                    // types in one namespace (namespace reference)
//   typings.useNamespace("System.Net.Http")                  // types in one namespace (string form)
//
// Note: the global importNamespace() provided by AllowClr gives runtime access only.
// Use typings.importNamespace() when you also want IntelliSense completions.

// Import System.IO and read a file listing from a script
var result = engine.Evaluate("""
    var IO = typings.importNamespace("System.IO");
    IO.Directory.GetCurrentDirectory()
    """);
Console.WriteLine(result); // e.g. /home/user/myproject
