// Minimal: transpile and evaluate
//
// DuetsSession is the entry point. CreateAsync downloads and caches the
// transpiler bundle (BabelTranspiler by default) on first run.
#:project ../src/Duets/Duets.csproj

using Duets;

using var session = await DuetsSession.CreateAsync();

var result = session.Evaluate("Math.sqrt(2)");
Console.WriteLine(result); // 1.4142135623730951
