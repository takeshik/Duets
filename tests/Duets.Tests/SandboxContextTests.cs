using Duets.Sandbox;
using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.NamespaceTargets;

namespace Duets.Tests;

public sealed class SandboxContextTests
{
    private static Task<SandboxContext> CreateContextAsync()
    {
        return SandboxContext.CreateAsync(
            declarations => FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations, true),
            FakeRuntimeAssets.CreateBabelTranspilerAsync
        );
    }

    [Fact]
    public async Task CreateAsync_enables_typescript_completions_and_registers_typings_builtins()
    {
        await using var ctx = await CreateContextAsync();

        var completions = ctx.GetCompletions("Math.", 5);
        var (result, _) = ctx.Evaluate("typeof typings");

        Assert.Contains(completions, entry => entry.Name == "abs");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task GetCompletions_requires_the_typescript_transpiler()
    {
        await using var ctx = await CreateContextAsync();
        await ctx.SetTranspilerAsync(TranspilerKind.Babel);

        var exception = Assert.Throws<InvalidOperationException>(() => ctx.GetCompletions("Math.", 5));

        Assert.Contains("require the TypeScript transpiler", exception.Message);
    }

    [Fact]
    public async Task RegisterType_returns_the_full_name_and_records_declarations()
    {
        await using var ctx = await CreateContextAsync();

        var fullName = ctx.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        Assert.Equal(typeof(NamespaceAlpha).FullName, fullName);
        Assert.Contains(ctx.GetTypeDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
    }

    [Fact]
    public async Task ResetAsync_clears_previously_registered_type_declarations()
    {
        await using var ctx = await CreateContextAsync();
        ctx.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        await ctx.ResetAsync();

        Assert.DoesNotContain(ctx.GetTypeDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
    }

    [Fact]
    public async Task SetTranspilerAsync_switches_to_babel_and_still_allows_type_registration()
    {
        await using var ctx = await CreateContextAsync();

        await ctx.SetTranspilerAsync(TranspilerKind.Babel);
        var (result, _) = ctx.Evaluate("const answer: number = 40 + 2; answer");
        var fullName = ctx.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        Assert.Equal("42", result);
        Assert.Equal(typeof(NamespaceAlpha).FullName, fullName);
        Assert.StartsWith("Babel", ctx.TranspilerDescription);
        Assert.Contains(ctx.GetTypeDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
    }
}
