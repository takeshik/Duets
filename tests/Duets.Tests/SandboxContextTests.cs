using System.Net;
using System.Net.Sockets;
using Duets.Sandbox;
using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.NamespaceTargets;

namespace Duets.Tests;

[Collection("TranspilerAssets")]
public sealed class SandboxContextTests
{
    public SandboxContextTests(TranspilerAssetsFixture assets, ITestOutputHelper output)
    {
        this._assets = assets;
        this._output = output;
        this._output.WriteLine(
            $"TypeScript {assets.TypeScriptVersion}, Babel {assets.BabelVersion}"
        );
    }

    private readonly TranspilerAssetsFixture _assets;
    private readonly ITestOutputHelper _output;

    private Task<SandboxContext> CreateContextAsync()
    {
        return SandboxContext.CreateAsync(
            declarations => this._assets.CreateTypeScriptServiceAsync(declarations, true),
            this._assets.CreateBabelTranspilerAsync
        );
    }

    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task CreateAsync_enables_typescript_completions_and_registers_typings_builtins()
    {
        await using var ctx = await this.CreateContextAsync();

        var completions = ctx.GetCompletions("Math.", 5);
        var (result, _) = ctx.Evaluate("typeof typings");

        Assert.Contains(completions, entry => entry.Name == "abs");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task GetCompletions_requires_the_typescript_transpiler()
    {
        await using var ctx = await this.CreateContextAsync();
        await ctx.SetTranspilerAsync(TranspilerKind.Babel);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ctx.GetCompletions("Math.", 5)
        );

        Assert.Contains("require the TypeScript transpiler", exception.Message);
    }

    [Fact]
    public async Task RegisterType_returns_the_full_name_and_records_declarations()
    {
        await using var ctx = await this.CreateContextAsync();

        var fullName = ctx.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        Assert.Equal(typeof(NamespaceAlpha).FullName, fullName);
        Assert.Contains(
            ctx.GetTypeDeclarations(),
            declaration => declaration.Content.Contains("class NamespaceAlpha")
        );
    }

    [Fact]
    public async Task ResetAsync_clears_previously_registered_type_declarations()
    {
        await using var ctx = await this.CreateContextAsync();
        ctx.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        await ctx.ResetAsync();

        Assert.DoesNotContain(
            ctx.GetTypeDeclarations(),
            declaration => declaration.Content.Contains("class NamespaceAlpha")
        );
    }

    [Fact]
    public async Task SetTranspilerAsync_switches_to_babel_and_still_allows_type_registration()
    {
        await using var ctx = await this.CreateContextAsync();

        await ctx.SetTranspilerAsync(TranspilerKind.Babel);
        var (result, _) = ctx.Evaluate("const answer: number = 40 + 2; answer");
        var fullName = ctx.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        Assert.Equal("42", result);
        Assert.Equal(typeof(NamespaceAlpha).FullName, fullName);
        Assert.StartsWith("Babel", ctx.TranspilerDescription);
        Assert.Contains(
            ctx.GetTypeDeclarations(),
            declaration => declaration.Content.Contains("class NamespaceAlpha")
        );
    }

    [Fact]
    public async Task StopWebServerAsync_preserves_an_external_pad_target()
    {
        await using var ctx = await this.CreateContextAsync();
        var sessionId = Guid.NewGuid().ToString("D");
        ctx.PadProtocolClient.Target(new Uri("http://127.0.0.1:1/"), sessionId, null);
        ctx.StartWebServer(ReserveFreePort());

        await ctx.StopWebServerAsync();

        Assert.True(ctx.PadProtocolClient.HasTarget);
        Assert.Equal(sessionId, ctx.PadProtocolClient.TargetSessionId);
    }

    [Fact]
    public async Task StopWebServerAsync_closes_a_target_created_by_the_local_server()
    {
        await using var ctx = await this.CreateContextAsync();
        ctx.StartWebServer(ReserveFreePort());
        var creation = await ctx.PadProtocolClient.CreateLocalSessionAsync(null);
        Assert.True(creation["httpOk"]?.GetValue<bool>());
        Assert.True(ctx.PadProtocolClient.HasTarget);

        await ctx.StopWebServerAsync();

        Assert.False(ctx.PadProtocolClient.HasTarget);
    }
}
