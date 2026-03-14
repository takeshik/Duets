using ConsoleAppFramework;
using Duets.Sandbox;

var app = ConsoleApp.Create();
app.Add<Commands>();
await app.RunAsync(args.Length == 0 ? ["repl"] : args);
