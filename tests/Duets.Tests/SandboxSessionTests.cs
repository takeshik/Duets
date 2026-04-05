using Duets.Sandbox;
using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.NamespaceTargets;

namespace Duets.Tests;

public sealed class SandboxSessionTests
{
    private static SandboxSession CreateSession()
    {
        return new SandboxSession(
            declarations => FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations, true),
            FakeRuntimeAssets.CreateBabelTranspilerAsync,
            null
        );
    }

    [Fact]
    public async Task EnsureInitializedAsync_enables_typescript_completions_and_registers_typings_builtins()
    {
        await using var session = CreateSession();

        await session.EnsureInitializedAsync();

        var completions = session.GetCompletions("Math.", 5);
        var (result, _) = session.Evaluate("typeof typings");

        Assert.Contains(completions, entry => entry.Name == "abs");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task GetCompletions_requires_the_typescript_transpiler()
    {
        await using var session = CreateSession();
        await session.SetTranspilerAsync(TranspilerKind.Babel);

        var exception = Assert.Throws<InvalidOperationException>(() => session.GetCompletions("Math.", 5));

        Assert.Contains("requires the TypeScript transpiler", exception.Message);
    }

    [Fact]
    public async Task RegisterType_returns_the_full_name_and_records_declarations_before_initialization()
    {
        await using var session = CreateSession();

        var fullName = session.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        Assert.Equal(typeof(NamespaceAlpha).FullName, fullName);
        Assert.Contains(session.GetTypeDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
    }

    [Fact]
    public async Task ResetAsync_clears_previously_registered_type_declarations()
    {
        await using var session = CreateSession();
        session.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        await session.ResetAsync();

        Assert.DoesNotContain(session.GetTypeDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
    }

    [Fact]
    public async Task SetTranspilerAsync_switches_to_babel_and_still_allows_type_registration()
    {
        await using var session = CreateSession();

        await session.SetTranspilerAsync(TranspilerKind.Babel);
        var (result, _) = session.Evaluate("const answer: number = 40 + 2; answer");
        var fullName = session.RegisterType(typeof(NamespaceAlpha).AssemblyQualifiedName!);

        Assert.Equal("42", result);
        Assert.Equal(typeof(NamespaceAlpha).FullName, fullName);
        Assert.StartsWith("Babel", session.TranspilerDescription);
        Assert.Contains(session.GetTypeDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
    }
}
