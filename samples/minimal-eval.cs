// Minimal: transpile and evaluate
//
// The core of Duets is two classes:
//   TypeScriptService — TypeScript compiler (transpile, completions)
//   ScriptEngine      — Jint-based executor for transpiled code
#:project ../src/Duets/Duets.csproj

using Duets;

using var ts = new TypeScriptService();
await ts.ResetAsync(); // downloads & caches typescript.js on first run

using var engine = new ScriptEngine(null, ts);

var result = engine.Evaluate("Math.sqrt(2)");
Console.WriteLine(result); // 1.4142135623730951
