namespace Duets.Sandbox;

internal sealed class InteractiveRepl(SandboxSession session)
{
    public async Task RunAsync()
    {
        Console.WriteLine($"Duets Sandbox  [TypeScript {session.TypeScriptVersion}]");
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
              :reset                        Reset all engines to initial state
              :help                         Show this help
              :quit                         Exit

            CLI modes (dotnet run --project src/Duets.Sandbox -- <mode>):

              eval <code>                   One-shot TypeScript eval (JSON output)
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
            var result = session.Evaluate(line);
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

                PrintCompletions(session.GetCompletions(src, pos));
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
                var decls = session.GetTypeDeclarations();
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
                        if (session.IsServerRunning)
                        {
                            PrintError("Server is already running.");
                            break;
                        }

                        session.StartWebServer(tokens.Length > 2 ? int.Parse(tokens[2]) : 17375);
                        break;
                    case "stop":
                        await session.StopWebServerAsync();
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

            case "reset":
                Console.Error.Write("  Resetting engines...");
                await session.ResetAsync();
                Console.Error.WriteLine($" done. TypeScript {session.TypeScriptVersion}");
                break;

            default:
                PrintError($"Unknown command: {cmd}  (try :help)");
                break;
        }
    }
}
