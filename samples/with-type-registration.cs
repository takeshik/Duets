// With .NET type registration
//
// AllowClr() enables CLR interop in scripts.
// RegisterTypeBuiltins() adds the `typings` global, which exposes .NET types
// to both scripts and the Monaco IntelliSense completions.
#:project ../src/Duets/Duets.csproj

using Duets;
using Jint;

using var ts = new TypeScriptService();
await ts.ResetAsync();

using var engine = new ScriptEngine(opts => opts.AllowClr(), ts);
engine.RegisterTypeBuiltins(ts);

// From a script, use `typings` to register types for CLR interop and completions:
//
//   typings.useType(System.IO.File)                          // single type via CLR reference
//   typings.useType("System.IO.File, System.IO.FileSystem")  // via assembly-qualified name
//   typings.scanAssembly("System.Net.Http")                  // namespace skeletons only
//   typings.scanAssemblyOf(System.IO.File)                   // namespace skeletons from the type's assembly
//   typings.useAssembly("System.Net.Http")                   // all public types
//   typings.useAssemblyOf(System.IO.File)                    // all public types from the type's assembly
//   typings.useNamespace(System.Net.Http)                    // types in one namespace (namespace reference)
//   typings.useNamespace("System.Net.Http")                  // types in one namespace (string form)

// Register System.Environment and read a property from a script
var result = engine.Evaluate("""
    typings.useType(System.Environment);
    System.Environment.MachineName
    """);
Console.WriteLine(result); // e.g. my-machine
