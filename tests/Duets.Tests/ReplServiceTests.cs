using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Duets.Tests.TestSupport;

namespace Duets.Tests;

public sealed class ReplServiceTests
{
    private static async Task<JsonElement> ReadNextDataEventAsync(StreamReader reader)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (line is null)
            {
                throw new EndOfStreamException("The SSE stream ended before the next data event was received.");
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line["data: ".Length..]);
            return document.RootElement.Clone();
        }
    }

    [Fact]
    public async Task Eval_endpoint_returns_a_failed_payload_when_script_execution_throws()
    {
        var declarations = new TypeDeclarations();
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        await DuetsServerFixture.RunAsync(
            server =>
            {
                _ = new ReplService(declarations, engine, server, monacoLoader: AssetSources.From(_ => Task.FromResult("// fake loader")));
            },
            async (client, prefix) =>
            {
                using var response = await client.PostAsync(
                    prefix + "eval",
                    new StringContent("null.prop", Encoding.UTF8, "text/plain")
                );

                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.False(payload.GetProperty("ok").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("result").GetString()));
            }
        );
    }

    [Fact]
    public async Task Eval_endpoint_returns_the_evaluation_result_and_console_logs()
    {
        var declarations = new TypeDeclarations();
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        await DuetsServerFixture.RunAsync(
            server =>
            {
                _ = new ReplService(declarations, engine, server, monacoLoader: AssetSources.From(_ => Task.FromResult("// fake loader")));
            },
            async (client, prefix) =>
            {
                using var response = await client.PostAsync(
                    prefix + "eval",
                    new StringContent("console.log('hello'); 1 + 2", Encoding.UTF8, "text/plain")
                );

                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

                Assert.True(payload.GetProperty("ok").GetBoolean());
                Assert.Equal("3", payload.GetProperty("result").GetString());
                var log = Assert.Single(payload.GetProperty("logs").EnumerateArray());
                Assert.Equal("log", log.GetProperty("level").GetString());
                Assert.Equal("hello", log.GetProperty("text").GetString());
            }
        );
    }

    [Fact]
    public async Task MonacoLoader_endpoint_returns_the_injected_asset_content()
    {
        var declarations = new TypeDeclarations();
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        await DuetsServerFixture.RunAsync(
            server =>
            {
                _ = new ReplService(
                    declarations,
                    engine,
                    server,
                    monacoLoader: AssetSources.From(_ => Task.FromResult("// fake loader"))
                );
            },
            async (client, prefix) =>
            {
                var content = await client.GetStringAsync(prefix + "monaco-loader.js");
                Assert.Equal("// fake loader", content);
            }
        );
    }

    [Fact]
    public async Task TypeDeclarationEvents_endpoint_emits_existing_declarations_and_live_updates()
    {
        var declarations = new TypeDeclarations();
        declarations.RegisterDeclaration("declare const existing: number;");
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        await DuetsServerFixture.RunAsync(
            server =>
            {
                _ = new ReplService(declarations, engine, server, monacoLoader: AssetSources.From(_ => Task.FromResult("// fake loader")));
            },
            async (client, prefix) =>
            {
                await using var stream = await client.GetStreamAsync(prefix + "type-declaration-events");
                using var reader = new StreamReader(stream);

                var existing = await ReadNextDataEventAsync(reader);
                declarations.RegisterDeclaration("declare const later: string;");
                var later = await ReadNextDataEventAsync(reader);

                Assert.Equal("declare const existing: number;", existing.GetProperty("content").GetString());
                Assert.Equal("declare const later: string;", later.GetProperty("content").GetString());
            }
        );
    }
}
