using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duets.Sandbox;

internal sealed class BatchRunner(SandboxContext session)
{
    private static readonly string _help = $$"""
        # Duets Sandbox — Batch Mode

        Batch mode reads JSON Lines (JSONL) from stdin and writes one JSON result per operation to stdout.
        All responses include `"op"` (echoes the operation name) and `"ok"` (boolean success flag).
        On failure, the response includes `"error"` with a message instead of the operation-specific fields.

        ## Operations

        | `op` | Required fields | Optional fields | Description |
        |---|---|---|---|
        | `eval` | `code` | | Evaluate TypeScript code; returns `result` (string) and `logs` (array of `{level, text}`, omitted when empty) |
        | `complete` | `source` | `position` (int, default: end) | Completions at position; returns `completions` array |
        | `register` | `type` | | Register a .NET type by assembly-qualified name; returns `type` (full name) |
        | `types` | | | List registered declaration file names; returns `types` (string array) |
        | `types-dump` | | | Dump registered declaration files; returns `types` array of `{fileName, content}` |
        | `server-start` | | `port` (int, default: 17375) | Start the DuetsPad web server; returns `url` |
        | `server-stop` | | | Stop the web server |
        | `server-status` | | | Returns `running` (boolean) |
        | `pad-session-create` | | `sessionId` (string) | POST `/sessions`; returns the DuetsPad session payload |
        | `pad-session-delete` | `sessionId` | | DELETE `/sessions/{sessionId}` |
        | `pad-eval` | `sessionId`, `code` | `source` (string) | POST `/sessions/{sessionId}/eval` |
        | `pad-interaction-invoke` | `sessionId`, `handlerId` | | POST an interaction handler invocation |
        | `pad-sse-open` | `streamId`, `sessionId`, `stream` | | Open a DuetsPad SSE stream. `stream` is {{string.Join(
            ", ",
            PadStreamKind.AllTokens.Select(t => $"`{t}`")
        )}} |
        | `pad-sse-read` | `streamId` | `maxRecords` (int, default: 1), `timeoutMs` (int, default: 1000), `includeComments` (bool, default: false) | Read SSE data records from an open stream |
        | `pad-sse-close` | `streamId` | | Close an open SSE stream |
        | `pad-sse-list` | | | List open SSE streams |
        | `set-transpiler` | `transpiler` | | Switch transpiler (`typescript` or `babel`); returns `transpiler` (description string) |
        | `reset` | | | Reset all engines and clear script state |
        | `help` | | | Returns this document as `content` (Markdown string) |

        ## Script built-ins (`typings` object)

        The `typings` global object is available inside `eval` code:

        | Call | Description |
        |---|---|
        | `typings.importType("Asm.Qualified.TypeName")` | Register a single type by assembly-qualified name |
        | `typings.importType(System.IO.File)` | Register a single type via CLR type reference |
        | `typings.scanAssembly("AssemblyName")` | Load assembly; register namespace skeletons for TS completions (no type members) |
        | `typings.scanAssemblyOf(System.IO.File)` | Scan the assembly containing the given CLR type reference |
        | `typings.importAssembly("AssemblyName")` | Load assembly; register all public types |
        | `typings.importAssemblyOf(System.IO.File)` | Register all public types from the containing assembly |
        | `typings.importNamespace("System.Net.Http")` | Register all public types in a namespace with completions |
        | `typings.usingNamespace("System.Net.Http")` | Register all public types as globals (C# `using` semantics) |

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
        {"op":"types-dump"}
        {"op":"server-start","port":17375}
        {"op":"pad-session-create"}
        {"op":"pad-sse-open","streamId":"canvas","sessionId":"...","stream":"canvas"}
        {"op":"pad-sse-read","streamId":"canvas","maxRecords":1,"timeoutMs":1000}
        {"op":"pad-eval","sessionId":"...","code":"canvas.add(ui.button('hello', () => dump(Date())))"}
        {"op":"pad-interaction-invoke","sessionId":"...","handlerId":"..."}
        {"op":"pad-sse-close","streamId":"canvas"}
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
        while (Console.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

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
                    "server-status" => new
                    {
                        ok = true,
                        running = session.IsServerRunning,
                        state = session.WebServerState,
                        error = session.WebServerError,
                    },
                    "pad-session-create" => await session.PadProtocolClient.CreateSessionAsync(
                        cmd.TryGetProperty("sessionId", out var sessionIdEl)
                            ? sessionIdEl.GetString()
                            : null
                    ),
                    "pad-session-delete" => await session.PadProtocolClient.DeleteSessionAsync(
                        cmd.GetProperty("sessionId").GetString()!
                    ),
                    "pad-eval" => await session.PadProtocolClient.EvaluateAsync(
                        cmd.GetProperty("sessionId").GetString()!,
                        cmd.GetProperty("code").GetString()!,
                        cmd.TryGetProperty("source", out var sourceEl) ? sourceEl.GetString() : null
                    ),
                    "pad-interaction-invoke" =>
                        await session.PadProtocolClient.InvokeInteractionAsync(
                            cmd.GetProperty("sessionId").GetString()!,
                            cmd.GetProperty("handlerId").GetString()!
                        ),
                    "pad-sse-open" => await session.PadProtocolClient.OpenSseAsync(
                        cmd.GetProperty("streamId").GetString()!,
                        cmd.GetProperty("sessionId").GetString()!,
                        cmd.GetProperty("stream").GetString()!
                    ),
                    "pad-sse-read" => await session.PadProtocolClient.ReadSseAsync(
                        cmd.GetProperty("streamId").GetString()!,
                        cmd.TryGetProperty("maxRecords", out var maxRecordsEl)
                            ? maxRecordsEl.GetInt32()
                            : 1,
                        cmd.TryGetProperty("timeoutMs", out var timeoutMsEl)
                            ? timeoutMsEl.GetInt32()
                            : 1000,
                        cmd.TryGetProperty("includeComments", out var includeCommentsEl)
                            && includeCommentsEl.GetBoolean()
                    ),
                    "pad-sse-close" => session.PadProtocolClient.CloseSse(
                        cmd.GetProperty("streamId").GetString()!
                    ),
                    "pad-sse-list" => session.PadProtocolClient.ListSseStreams(),
                    "types" => new
                    {
                        ok = true,
                        types = session.GetTypeDeclarations().Select(d => d.FileName).ToArray(),
                    },
                    "types-dump" => new
                    {
                        ok = true,
                        types = session
                            .GetTypeDeclarations()
                            .Select(d => new { d.FileName, d.Content })
                            .ToArray(),
                    },
                    "set-transpiler" => await this.SetTranspilerAsync(
                        cmd.GetProperty("transpiler").GetString()!
                    ),
                    "reset" => await this.ResetAsync(),
                    "help" => new { ok = true, content = _help },
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
            var (result, logs) = session.Evaluate(code);
            var logEntries =
                logs.Count > 0
                    ? logs.Select(l => new
                        {
                            level = l.Level.ToString().ToLowerInvariant(),
                            text = l.Text,
                        })
                        .ToArray()
                    : null;
            return new
            {
                ok = true,
                result,
                logs = logEntries,
            };
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
        if (session.IsServerRunning)
        {
            return new { ok = false, error = "Server is already running" };
        }

        var port = cmd.TryGetProperty("port", out var portEl) ? portEl.GetInt32() : 17375;
        session.StartWebServer(port);
        await Task.Delay(100);
        if (!session.IsServerRunning)
        {
            return new
            {
                ok = false,
                state = session.WebServerState,
                error = session.WebServerError ?? "Server stopped during startup.",
            };
        }

        return new { ok = true, url = $"http://127.0.0.1:{port}/" };
    }

    private async Task<object> ServerStopAsync()
    {
        if (!session.IsServerRunning)
        {
            return new { ok = false, error = "Server is not running" };
        }

        await session.StopWebServerAsync();
        return new { ok = true };
    }

    private async Task<object> SetTranspilerAsync(string name)
    {
        await session.SetTranspilerAsync(name);
        return new { ok = true, transpiler = session.TranspilerDescription };
    }

    private async Task<object> ResetAsync()
    {
        await session.ResetAsync();
        return new { ok = true };
    }
}
