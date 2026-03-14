using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duets.Sandbox;

internal sealed class BatchRunner(SandboxSession session)
{
    private const string Help = """
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

        ## Script built-ins (`typings` object)

        The `typings` global object is available inside `eval` code:

        | Call | Description |
        |---|---|
        | `typings.use("Asm.Qualified.TypeName")` | Register a single type by assembly-qualified name |
        | `typings.scanAssembly("AssemblyName")` | Load assembly; register namespace skeletons for TS completions (no type members) |
        | `typings.useAssembly("AssemblyName")` | Load assembly; register all public types |
        | `typings.useNamespace(System.Net.Http)` | Register all public types in the given namespace (pass namespace reference, not string) |
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

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task RunAsync()
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
                    "register" => this.Register(cmd.GetProperty("type").GetString()!),
                    "server-start" => await this.ServerStartAsync(cmd),
                    "server-stop" => await this.ServerStopAsync(),
                    "server-status" => new { ok = true, running = session.IsServerRunning },
                    "types" => new
                    {
                        ok = true,
                        types = session.GetTypeDeclarations().Select(d => d.FileName).ToArray(),
                    },
                    "reset" => await this.ResetAsync(),
                    "help" => new { ok = true, content = Help },
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

    private static void OutputJsonWithOp(string op, object result)
    {
        var node = JsonSerializer.SerializeToNode(result, JsonOptions)!.AsObject();
        node["op"] = op;
        Console.WriteLine(node.ToJsonString(JsonOptions));
    }

    private object Eval(string code)
    {
        try
        {
            return new { ok = true, result = session.Evaluate(code) };
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
            var completions = session.GetCompletions(source, position);
            return new { ok = true, completions };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private object Register(string typeName)
    {
        try
        {
            var fullName = session.RegisterType(typeName);
            return new { ok = true, type = fullName };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private async Task<object> ServerStartAsync(JsonElement cmd)
    {
        if (session.IsServerRunning) return new { ok = false, error = "Server is already running" };
        var port = cmd.TryGetProperty("port", out var portEl) ? portEl.GetInt32() : 17375;
        session.StartWebServer(port);
        await Task.CompletedTask;
        return new { ok = true, url = $"http://127.0.0.1:{port}/" };
    }

    private async Task<object> ServerStopAsync()
    {
        if (!session.IsServerRunning) return new { ok = false, error = "Server is not running" };
        await session.StopWebServerAsync();
        return new { ok = true };
    }

    private async Task<object> ResetAsync()
    {
        await session.ResetAsync();
        return new { ok = true };
    }
}
