// REPL special variables: $_, $exception, GetGlobalVariables
//
// ScriptEngine maintains three REPL conveniences that mirror interactive
// shell workflows:
//
//   $_            — the value returned by the last Evaluate call (like $_ in
//                   PowerShell or _ in Python/Node.js REPL).  Cleared to
//                   undefined after Execute or on any thrown exception.
//
//   $exception    — the exception object from the last thrown error.  Lets
//                   you inspect error details without re-running the failing
//                   expression.  Cleared to undefined on the next successful
//                   call.
//
//   GetGlobalVariables() — returns a snapshot of every name the user has
//                   defined, excluding built-ins and engine internals.
#:project ../src/Duets/Duets.csproj

using Duets;

using var ts = new TypeScriptService();
await ts.ResetAsync();

using var engine = new ScriptEngine(null, ts);

// $_ — last evaluated value
engine.Evaluate("Math.PI * 2");
var lastResult = engine.Evaluate("$_");
Console.WriteLine($"$_ = {lastResult}"); // $_ = 6.283185307179586

// $_ is cleared after Execute (statement, not expression)
engine.Execute("const greeting = 'hello';");
var afterExec = engine.Evaluate("$_");
Console.WriteLine($"$_ after Execute = {afterExec}"); // $_ after Execute = undefined

// $exception — captures the last thrown error
try
{
    engine.Evaluate("null.missingProperty");
}
catch
{
    // swallow; inspect via $exception instead
}

var exMessage = engine.Evaluate("$exception?.message ?? String($exception)");
Console.WriteLine($"$exception.message = {exMessage}");
// $exception.message = Cannot read properties of null (reading 'missingProperty')

// $exception is cleared after the next successful call
engine.Evaluate("1 + 1");
var clearedEx = engine.Evaluate("$exception");
Console.WriteLine($"$exception after success = {clearedEx}"); // $exception after success = undefined

// GetGlobalVariables — snapshot of user-defined names only
engine.Execute("var alpha = 1; var beta = 'two'; var gamma = [3];");
var globals = engine.GetGlobalVariables();

Console.WriteLine("User-defined globals:");
foreach (var (key, value) in globals)
{
    Console.WriteLine($"  {key} = {value}");
}
// User-defined globals:
//   greeting = hello
//   alpha = 1
//   beta = two
//   gamma = 3       (Jint renders single-element arrays as their element)
