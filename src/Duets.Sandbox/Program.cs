using System.Text.Json;
using System.Text.Json.Serialization;
using Duets;
using HttpHarker;
using Jint;

internal sealed class Program
{
    private Program()
    {
        this._ts = new TypeScriptService();
        this._scriptEngine = CreateScriptEngine(this._ts);
    }

    private const string BatchHelp = """
        # Duets Sandbox — Batch Mode

        Batch mode reads JSON Lines (JSONL) from stdin and writes one JSON result per operation to stdout.
        All responses include `"op"` (echoes the operation name) and `"ok"` (boolean success flag).
        On failure, the response includes `"error"` with a message instead of the operation-specific fields.

        ## Operations

        | `op` | Required fields | Optional fields | Description |
        |---|---|---|---|
        | `eval` | `code` | | Evaluate TypeScript code; returns `result` (string) |
        | `complete` | `source` | `position` (int, default: end) | Completions at position; returns `completions` array |
        | `register` | `type` | | Register a .NET type by assembly-qualified name; returns `type` (full name) |
        | `types` | | | List registered declaration file names; returns `types` (string array) |
        | `server-start` | | `port` (int, default: 17375) | Start the web REPL server; returns `url` |
        | `server-stop` | | | Stop the web server |
        | `server-status` | | | Returns `running` (boolean) |
        | `reset` | | | Reset all engines and clear script state |
        | `help` | | | Returns this document as `content` (Markdown string) |

        ## Completion entry fields

        Each entry in the `completions` array has:
        - `name` — symbol name
        - `kind` — e.g. `"method"`, `"property"`, `"keyword"`
        - `sortText` — ordering hint (may be null)

        ## Examples

        ```jsonl
        {"op":"eval","code":"1 + 2"}
        {"op":"eval","code":"const xs = [1,2,3]; xs.map(x => x * 2)"}
        {"op":"complete","source":"[1,2,3].","position":8}
        {"op":"complete","source":"System.Math.","position":12}
        {"op":"register","type":"System.IO.File, System.IO.FileSystem"}
        {"op":"types"}
        {"op":"server-start","port":17375}
        {"op":"server-stop"}
        {"op":"server-status"}
        {"op":"reset"}
        {"op":"help"}
        ```

        ## Notes

        - Script state (variables, registered types) persists across operations within a session.
        - Diagnostic output (initialization messages, server status) goes to stderr; stdout contains only JSONL results.
        """;

    // ── JSON ───────────────────────────────────────────────────────────────────
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Engine state ───────────────────────────────────────────────────────────
    private TypeScriptService _ts;
    private ScriptEngine _scriptEngine;

    // ── Server state ───────────────────────────────────────────────────────────
    private HttpServer? _webServer;
    private ReplService? _replService;
    private CancellationTokenSource? _webServerCts;
    private Task? _webServerTask;

    // ── Entry point ────────────────────────────────────────────────────────────
    public static async Task Main(string[] args)
    {
        var program = new Program();
        await program.InitEnginesAsync();
        await program.RunAsync(args);
    }

    // ── Engine helpers ─────────────────────────────────────────────────────────
    private static ScriptEngine CreateScriptEngine(TypeScriptService ts)
    {
        return new ScriptEngine(
            opts => opts.AllowClr(
                typeof(Math).Assembly,
                typeof(Enumerable).Assembly,
                typeof(HttpClient).Assembly
            ),
            ts
        );
    }

    private static void PrintCompletions(IReadOnlyList<TypeScriptService.CompletionEntry> completions)
    {
        if (completions.Count == 0)
        {
            Console.WriteLine("  (no completions)");
            return;
        }

        const int maxShown = 30;
        foreach (var c in completions.Take(maxShown))
        {
            Console.WriteLine($"  {c.Name,-32} {c.Kind}");
        }

        if (completions.Count > maxShown)
        {
            Console.WriteLine($"  ... and {completions.Count - maxShown} more");
        }
    }

    private static void ReplError(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"  ! {msg}");
        Console.ResetColor();
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Duets Sandbox — Interactive REPL Commands

              <typescript>                  Evaluate TypeScript code
              :complete <src> [pos]         Completions at position (default: end of src)
              :register <type-name>         Register .NET type (assembly-qualified name)
              :types                        List registered type declarations
              :server start [port]          Start web REPL server (default port: 17375)
              :server stop                  Stop web server
              :server status                Show web server status
              :reset                        Reset all engines to initial state
              :help                         Show this help
              :quit                         Exit

            CLI modes (dotnet run --project src/Duets.Sandbox -- <mode>):

              eval <code>                   One-shot TypeScript eval (JSON output)
              complete <src> [pos]          One-shot completions (JSON output)
              serve [port]                  Start web server (blocks until Ctrl+C)
              batch                         JSONL batch mode — send {"op":"help"} for details

            """
        );
    }

    // ── Utilities ──────────────────────────────────────────────────────────────
    private static string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    private static void OutputJson(object obj)
    {
        Console.WriteLine(Serialize(obj));
    }

    private static void OutputJsonWithOp(string op, object result)
    {
        var node = JsonSerializer.SerializeToNode(result, JsonOptions)!.AsObject();
        node["op"] = op;
        Console.WriteLine(node.ToJsonString(JsonOptions));
    }

    private static void Die(string msg)
    {
        Console.Error.WriteLine(msg);
        Environment.Exit(1);
    }

    private async Task InitEnginesAsync()
    {
        Console.Error.Write("Initializing TypeScript engine...");
        await this._ts.ResetAsync();
        await this._ts.InjectStdLibAsync();
        Console.Error.WriteLine($" TypeScript {this._ts.Version}");
        this.RegisterBuiltins();
    }

    private async Task RunAsync(string[] args)
    {
        try
        {
            switch (args.Length > 0 ? args[0] : "repl")
            {
                case "eval":
                    if (args.Length < 2) Die("Usage: eval <typescript-code>");
                    OutputJson(this.Eval(string.Join(" ", args[1..])));
                    break;

                case "complete":
                    if (args.Length < 2) Die("Usage: complete <source> [<position>]");
                    var completeSrc = args[1];
                    var completePos = args.Length > 2 ? int.Parse(args[2]) : completeSrc.Length;
                    OutputJson(this.Complete(completeSrc, completePos));
                    break;

                case "batch":
                    await this.RunBatchAsync();
                    break;

                case "serve":
                    var servePort = args.Length > 1 ? int.Parse(args[1]) : 17375;
                    this.StartWebServer(servePort);
                    Console.Error.WriteLine("Press Ctrl+C to stop.");
                    Console.CancelKeyPress += (_, e) =>
                    {
                        e.Cancel = true;
                        this._webServerCts?.Cancel();
                    };
                    await (this._webServerTask ?? Task.CompletedTask);
                    break;

                case "repl":
                default:
                    await this.RunReplAsync();
                    break;
            }
        }
        finally
        {
            this._webServerCts?.Cancel();
            this._replService?.Dispose();
            this._webServer?.Dispose();
            this._scriptEngine.Dispose();
            this._ts.Dispose();
        }
    }

    private void RegisterBuiltins()
    {
        this._ts.RegisterType(typeof(Math));
        this._ts.RegisterType(typeof(Enumerable));
        this._scriptEngine.SetValue(
            "importTypeDefs",
            new Action<string>(typeName =>
                {
                    var type = Type.GetType(typeName)
                        ?? throw new InvalidOperationException($"Type not found: {typeName}");
                    this._ts.RegisterType(type);
                }
            )
        );
    }

    // ── Operations ─────────────────────────────────────────────────────────────
    private object Eval(string code)
    {
        try
        {
            var result = this._scriptEngine.Evaluate(code);
            return new { ok = true, result = result.ToString() };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private object Complete(string source, int position)
    {
        try
        {
            var completions = this._ts.GetCompletions(source, position);
            return new { ok = true, completions };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private object RegisterType(string typeName)
    {
        try
        {
            var type = Type.GetType(typeName)
                ?? throw new InvalidOperationException($"Type not found: {typeName}");
            this._ts.RegisterType(type);
            return new { ok = true, type = type.FullName };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private void StartWebServer(int port = 17375)
    {
        if (this._webServer != null) return;
        this._webServerCts = new CancellationTokenSource();
        this._webServer = new HttpServer($"http://127.0.0.1:{port}/");
        this._replService = this._webServer.UseContentTypeDetection().UseRepl(this._ts, this._scriptEngine);
        this._webServerTask = this._webServer.RunAsync(cancellationToken: this._webServerCts.Token);
        Console.Error.WriteLine($"Web REPL server started at http://127.0.0.1:{port}/");
    }

    private async Task StopWebServerAsync()
    {
        if (this._webServer == null) return;
        this._webServerCts!.Cancel();
        try
        {
            await this._webServerTask!;
        }
        catch (OperationCanceledException)
        {
        }

        this._replService?.Dispose();
        this._replService = null;
        this._webServer.Dispose();
        this._webServer = null;
        this._webServerCts = null;
        this._webServerTask = null;
        Console.Error.WriteLine("Web server stopped.");
    }

    private async Task<object> ResetAsync()
    {
        if (this._webServer != null) await this.StopWebServerAsync();
        this._scriptEngine.Dispose();
        this._ts.Dispose();
        this._ts = new TypeScriptService();
        await this._ts.ResetAsync();
        await this._ts.InjectStdLibAsync();
        this._scriptEngine = CreateScriptEngine(this._ts);
        this.RegisterBuiltins();
        return new { ok = true };
    }

    // ── Batch mode (JSONL) ─────────────────────────────────────────────────────
    // Agent-friendly mode: reads JSON Lines from stdin, writes JSON Lines to stdout.
    //
    // Input operations (one JSON object per line):
    //   {"op":"eval","code":"..."}
    //   {"op":"complete","source":"...","position":5}
    //   {"op":"register","type":"System.IO.File, System.IO.FileSystem"}
    //   {"op":"types"}
    //   {"op":"server-start","port":17375}
    //   {"op":"server-stop"}
    //   {"op":"server-status"}
    //   {"op":"reset"}
    private async Task RunBatchAsync()
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var op = "?";
            try
            {
                var cmd = JsonSerializer.Deserialize<JsonElement>(line);
                op = cmd.GetProperty("op").GetString() ?? "";
                var result = op switch
                {
                    "eval" => this.Eval(cmd.GetProperty("code").GetString()!),
                    "complete" => this.Complete(
                        cmd.GetProperty("source").GetString()!,
                        cmd.TryGetProperty("position", out var posEl)
                            ? posEl.GetInt32()
                            : cmd.GetProperty("source").GetString()!.Length
                    ),
                    "register" => this.RegisterType(cmd.GetProperty("type").GetString()!),
                    "server-start" => await this.BatchServerStartAsync(cmd),
                    "server-stop" => await this.BatchServerStopAsync(),
                    "server-status" => new { ok = true, running = this._webServer != null },
                    "types" => new { ok = true, types = this._ts.GetTypeDeclarations().Select(d => d.FileName).ToArray() },
                    "reset" => await this.ResetAsync(),
                    "help" => new { ok = true, content = BatchHelp },
                    _ => new { ok = false, error = $"Unknown op: {op}" },
                };
                OutputJsonWithOp(op, result);
            }
            catch (Exception ex)
            {
                OutputJsonWithOp(op, new { ok = false, error = ex.Message });
            }
        }
    }

    private async Task<object> BatchServerStartAsync(JsonElement cmd)
    {
        if (this._webServer != null) return new { ok = false, error = "Server is already running" };
        var port = cmd.TryGetProperty("port", out var portEl) ? portEl.GetInt32() : 17375;
        this.StartWebServer(port);
        await Task.CompletedTask;
        return new { ok = true, url = $"http://127.0.0.1:{port}/" };
    }

    private async Task<object> BatchServerStopAsync()
    {
        if (this._webServer == null) return new { ok = false, error = "Server is not running" };
        await this.StopWebServerAsync();
        return new { ok = true };
    }

    // ── Interactive REPL ───────────────────────────────────────────────────────
    private async Task RunReplAsync()
    {
        Console.WriteLine($"Duets Sandbox  [TypeScript {this._ts.Version}]");
        Console.WriteLine("Enter TypeScript code to evaluate, or :help for commands.\n");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null) break; // EOF / Ctrl+D

            line = line.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith(':'))
            {
                await this.HandleReplCommandAsync(line[1..]);
            }
            else
            {
                this.ReplEval(line);
            }
        }
    }

    private void ReplEval(string line)
    {
        try
        {
            var result = this._scriptEngine.Evaluate(line);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=> {result}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            ReplError(ex.Message);
        }
    }

    private async Task HandleReplCommandAsync(string input)
    {
        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return;
        var cmd = tokens[0].ToLowerInvariant();
        var rest = tokens.Length > 1 ? string.Join(' ', tokens[1..]) : "";

        switch (cmd)
        {
            case "h":
            case "help":
                PrintHelp();
                break;

            case "q":
            case "quit":
            case "exit":
                Environment.Exit(0);
                break;

            case "complete":
            {
                if (tokens.Length < 2)
                {
                    ReplError("Usage: :complete <source> [position]");
                    break;
                }

                string src;
                int pos;
                if (tokens.Length > 2 && int.TryParse(tokens[^1], out var explicitPos))
                {
                    src = string.Join(' ', tokens[1..^1]);
                    pos = explicitPos;
                }
                else
                {
                    src = string.Join(' ', tokens[1..]);
                    pos = src.Length;
                }

                PrintCompletions(this._ts.GetCompletions(src, pos));
                break;
            }

            case "register":
                if (string.IsNullOrEmpty(rest))
                {
                    ReplError("Usage: :register <assembly-qualified-type-name>");
                    break;
                }

                Console.WriteLine("  " + Serialize(this.RegisterType(rest)));
                break;

            case "types":
            {
                var decls = this._ts.GetTypeDeclarations();
                Console.WriteLine($"  {decls.Count} type declaration(s) registered.");
                foreach (var d in decls)
                {
                    Console.WriteLine($"  • {d.FileName}");
                }

                break;
            }

            case "server":
            {
                var sub = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "status";
                switch (sub)
                {
                    case "start":
                        if (this._webServer != null)
                        {
                            ReplError("Server is already running.");
                            break;
                        }

                        this.StartWebServer(tokens.Length > 2 ? int.Parse(tokens[2]) : 17375);
                        break;
                    case "stop":
                        await this.StopWebServerAsync();
                        break;
                    case "status":
                        Console.WriteLine(this._webServer != null ? "  Server: running" : "  Server: stopped");
                        break;
                    default:
                        ReplError($"Unknown server subcommand: {sub}  (start | stop | status)");
                        break;
                }

                break;
            }

            case "reset":
                Console.Error.Write("  Resetting engines...");
                await this.ResetAsync();
                Console.Error.WriteLine($" done. TypeScript {this._ts.Version}");
                break;

            default:
                ReplError($"Unknown command: {cmd}  (try :help)");
                break;
        }
    }
}
