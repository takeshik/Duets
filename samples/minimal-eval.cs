// Minimal: transpile and evaluate
//
// DuetsSession is the entry point. Referencing Duets.Jint is enough; CreateAsync
// discovers the Jint engine and BabelTranspiler automatically and downloads and
// caches the transpiler bundle on first run.
//
// To pass engine options (e.g. AllowClr for CLR interop):
//   await DuetsSession.CreateAsync(config => config.UseJint(opts => opts.AllowClr()))
//
// To use TypeScriptService for server-side completions instead of BabelTranspiler:
//   await DuetsSession.CreateAsync(config => config
//       .UseTranspiler(decls => TypeScriptService.CreateAsync(decls, injectStdLib: true))
//       .UseJint(opts => opts.AllowClr()))
#:project ../src/Duets.Jint/Duets.Jint.csproj

using Duets;

using var session = await DuetsSession.CreateAsync();

var result = session.Evaluate("Math.sqrt(2)");
Console.WriteLine(result); // 1.4142135623730951
