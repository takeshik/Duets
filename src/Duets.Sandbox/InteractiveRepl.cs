using TypeScriptService = Duets.Jint.TypeScriptService;

namespace Duets.Sandbox;

internal sealed class InteractiveRepl(SandboxContext session)
{
    public async Task RunAsync()
    {
        Console.WriteLine($"Duets Sandbox  [{session.ActiveBackend.Name} / {session.TranspilerDescription}]");
        Console.WriteLine("Enter TypeScript code to evaluate, or :help for commands.\n");

        await this.HandleCommandAsync("server start");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null) break; // EOF / Ctrl+D

            line = line.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith(':'))
            {
                await this.HandleCommandAsync(line[1..]);
            }
            else
            {
                this.Eval(line);
            }
        }
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

    private static void PrintError(string msg)
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
              :set transpiler <name>         Switch transpiler (typescript | babel)
              :set backend <name>           Switch backend (jint | okojo); clears script state
              :reset                        Reset all engines to initial state
              :help                         Show this help
              :quit                         Exit

            CLI modes (dotnet run --project src/Duets.Sandbox -- <mode>):

              complete <src> [--position n] One-shot completions (JSON output)
              serve [--port n]              Start web server (blocks until Ctrl+C)
              batch                         JSONL batch mode — send {"op":"help"} for details

            """
        );
    }

    private void Eval(string line)
    {
        try
        {
            var (result, logs) = session.Evaluate(line);
            foreach (var log in logs)
            {
                Console.ForegroundColor = log.Level switch
                {
                    ConsoleLogLevel.Error => ConsoleColor.Red,
                    ConsoleLogLevel.Warn => ConsoleColor.Yellow,
                    ConsoleLogLevel.Info => ConsoleColor.Blue,
                    _ => ConsoleColor.DarkGray,
                };
                Console.WriteLine($"   {log.Text}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=> {result}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            PrintError(ex.Message);
        }
    }

    private async Task HandleCommandAsync(string input)
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
                    PrintError("Usage: :complete <source> [position]");
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

                try
                {
                    PrintCompletions(session.GetCompletions(src, pos));
                }
                catch (Exception ex)
                {
                    PrintError(ex.Message);
                }

                break;
            }

            case "register":
                if (string.IsNullOrEmpty(rest))
                {
                    PrintError("Usage: :register <assembly-qualified-type-name>");
                    break;
                }

                try
                {
                    var fullName = session.RegisterType(rest);
                    Console.WriteLine($"  Registered: {fullName}");
                }
                catch (Exception ex)
                {
                    PrintError(ex.Message);
                }

                break;

            case "types":
            {
                try
                {
                    var decls = session.GetTypeDeclarations();
                    Console.WriteLine($"  {decls.Count} type declaration(s) registered.");
                    foreach (var d in decls)
                    {
                        Console.WriteLine($"  • {d.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    PrintError(ex.Message);
                }

                break;
            }

            case "server":
            {
                var sub = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "status";
                switch (sub)
                {
                    case "start":
                        if (session.IsServerRunning)
                        {
                            PrintError("Server is already running.");
                            break;
                        }

                        var startPort = 17375;
                        if (tokens.Length > 2 && !int.TryParse(tokens[2], out startPort))
                        {
                            PrintError($"Invalid port number: {tokens[2]}");
                            break;
                        }

                        try
                        {
                            session.StartWebServer(startPort);
                        }
                        catch (Exception ex)
                        {
                            PrintError(ex.Message);
                        }

                        break;
                    case "stop":
                        try
                        {
                            await session.StopWebServerAsync();
                        }
                        catch (Exception ex)
                        {
                            PrintError(ex.Message);
                        }

                        break;
                    case "status":
                        Console.WriteLine(session.IsServerRunning ? "  Server: running" : "  Server: stopped");
                        break;
                    default:
                        PrintError($"Unknown server subcommand: {sub}  (start | stop | status)");
                        break;
                }

                break;
            }

            case "set":
            {
                if (tokens.Length < 3)
                {
                    PrintError("Usage: :set transpiler <typescript|babel>  |  :set backend <jint|okojo>");
                    break;
                }

                try
                {
                    switch (tokens[1].ToLowerInvariant())
                    {
                        case "transpiler":
                            await Console.Error.WriteAsync($"  Switching to {tokens[2]} transpiler...");
                            await session.SetTranspilerAsync(tokens[2]);
                            await Console.Error.WriteLineAsync($" done. {session.TranspilerDescription}");
                            break;
                        case "backend":
                            await Console.Error.WriteAsync($"  Switching to {tokens[2]} backend...");
                            await session.SetBackendAsync(tokens[2]);
                            await Console.Error.WriteLineAsync(" done.");
                            break;
                        default:
                            PrintError($"Unknown setting: {tokens[1]}  (transpiler | backend)");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    PrintError(ex.Message);
                }

                break;
            }

            case "reset":
                await Console.Error.WriteAsync("  Resetting...");
                await session.ResetAsync();
                await Console.Error.WriteLineAsync($" done. [{session.ActiveBackend.Name} / {session.TranspilerDescription}]");
                break;

            default:
                PrintError($"Unknown command: {cmd}  (try :help)");
                break;
        }
    }
}
