using System.Text.Json;
using Duets.Sandbox;
using Duets.Tests.TestSupport;

namespace Duets.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollectionDefinition
{
}

[Collection("Console")]
public sealed class BatchRunnerTests
{
    private static Task<SandboxContext> CreateContextAsync()
    {
        return SandboxContext.CreateAsync(
            declarations => FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations, true),
            FakeRuntimeAssets.CreateBabelTranspilerAsync
        );
    }

    [Fact]
    public async Task RunAsync_types_dump_returns_declaration_contents()
    {
        await using var ctx = await CreateContextAsync();
        var input = new StringReader("{\"op\":\"types-dump\"}\n");
        var output = new StringWriter();
        var originalIn = Console.In;
        var originalOut = Console.Out;

        try
        {
            Console.SetIn(input);
            Console.SetOut(output);

            await new BatchRunner(ctx).RunAsync();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }

        var line = Assert.Single(
            output.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        );
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("types-dump", root.GetProperty("op").GetString());

        var types = root.GetProperty("types");
        Assert.NotEmpty(types.EnumerateArray());
        Assert.Contains(
            types.EnumerateArray(),
            entry => entry.GetProperty("content").GetString()!.Contains("declare const typings:")
        );
    }
}
