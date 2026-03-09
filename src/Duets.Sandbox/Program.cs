using Duets;
using HttpHarker;
using Jint;

using var ts = new TypeScriptService();
await ts.ResetAsync();

using var scriptEngine = new ScriptEngine(
    opts => opts.AllowClr(
        typeof(Math).Assembly,
        typeof(Enumerable).Assembly,
        typeof(HttpClient).Assembly
    ),
    ts
);

// Pass a type name string (e.g. importTypeDefs("System.Math")) to
// generate and register a TypeDeclaration, which is immediately delivered to connected SSE clients
scriptEngine.SetValue(
    "importTypeDefs",
    new Action<string>(typeName =>
        {
            var type = Type.GetType(typeName)
                ?? throw new InvalidOperationException($"Type not found: {typeName}");
            ts.RegisterType(type);
        }
    )
);

Console.WriteLine($"TypeScript {ts.Version}");

using var server = new HttpServer("http://127.0.0.1:17375/");
using var repl = server
    .UseContentTypeDetection()
    .UseRepl(ts, scriptEngine);
Console.WriteLine("Listening on http://127.0.0.1:17375/");
server.RunAsync().Forget();

while (true)
{
    var line = Console.ReadLine();
    if (line is null) break;
    try
    {
        Console.WriteLine(scriptEngine.Evaluate(line));
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
    }
}
