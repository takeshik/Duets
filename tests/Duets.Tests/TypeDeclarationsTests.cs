using System.Collections.Concurrent;
using Duets.Tests.TestTypes.Declarations;
using Duets.Tests.TestTypes.NamespaceTargets;

namespace Duets.Tests;

public sealed class TypeDeclarationsTests
{
    [Fact]
    public void GetDeclarations_returns_a_snapshot_that_is_not_affected_by_later_registrations()
    {
        var declarations = new TypeDeclarations();

        declarations.RegisterDeclaration("declare const first: number;");
        var snapshot = declarations.GetDeclarations();
        declarations.RegisterDeclaration("declare const second: number;");

        Assert.Single(snapshot);
        Assert.Equal(2, declarations.GetDeclarations().Count);
    }

    [Fact]
    public void RegisterDeclaration_ignores_duplicate_content()
    {
        var declarations = new TypeDeclarations();
        var changes = new List<TypeDeclaration>();
        declarations.DeclarationChanged += changes.Add;

        declarations.RegisterDeclaration("declare const answer: number;");
        declarations.RegisterDeclaration("declare const answer: number;");

        Assert.Single(declarations.GetDeclarations());
        Assert.Single(changes);
    }

    [Fact]
    public async Task RegisterDeclaration_is_idempotent_under_concurrent_access()
    {
        var declarations = new TypeDeclarations();
        var changes = new ConcurrentBag<TypeDeclaration>();
        declarations.DeclarationChanged += changes.Add;

        await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => declarations.RegisterDeclaration("declare const answer: number;")))
        );

        Assert.Single(declarations.GetDeclarations());
        Assert.Single(changes);
    }

    [Fact]
    public void RegisterDeclaration_raises_a_change_event_for_new_content()
    {
        var declarations = new TypeDeclarations();
        var changes = new List<TypeDeclaration>();
        declarations.DeclarationChanged += changes.Add;

        declarations.RegisterDeclaration("declare const answer: number;");

        var declaration = Assert.Single(declarations.GetDeclarations());
        Assert.Equal([declaration], changes);
    }

    [Fact]
    public void RegisterNamespace_creates_a_placeholder_declaration_for_an_uncovered_namespace()
    {
        var declarations = new TypeDeclarations();

        declarations.RegisterNamespace("Duets.Tests.TestTypes.NamespaceTargets");

        var placeholder = Assert.Single(declarations.GetDeclarations());
        Assert.Equal("declare namespace Duets.Tests.TestTypes.NamespaceTargets { const $name: 'Duets.Tests.TestTypes.NamespaceTargets'; }\n", placeholder.Content);
    }

    [Fact]
    public void RegisterNamespace_ignores_duplicate_calls_and_namespaces_covered_by_real_types()
    {
        var declarations = new TypeDeclarations();

        declarations.RegisterNamespace("Duets.Tests.TestTypes.NamespaceTargets");
        declarations.RegisterNamespace("Duets.Tests.TestTypes.NamespaceTargets");
        declarations.RegisterType(typeof(NamespaceAlpha));
        declarations.RegisterNamespace("Duets.Tests.TestTypes.NamespaceTargets");

        Assert.Equal(2, declarations.GetDeclarations().Count);
        Assert.DoesNotContain(declarations.GetDeclarations(), declaration => declaration.Content.Contains("$name"));
    }

    [Fact]
    public async Task RegisterNamespace_is_idempotent_under_concurrent_access()
    {
        var declarations = new TypeDeclarations();
        var changes = new ConcurrentBag<TypeDeclaration>();
        declarations.DeclarationChanged += changes.Add;

        await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => declarations.RegisterNamespace("Duets.Tests.TestTypes.NamespaceTargets")))
        );

        var placeholder = Assert.Single(declarations.GetDeclarations());
        Assert.Contains("$name", placeholder.Content);
        Assert.Single(changes);
    }

    [Fact]
    public async Task RegisterType_is_idempotent_under_concurrent_access()
    {
        var declarations = new TypeDeclarations();
        var changes = new ConcurrentBag<TypeDeclaration>();
        declarations.DeclarationChanged += changes.Add;

        await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => declarations.RegisterType(typeof(DeclarationSample))))
        );

        Assert.Equal(2, declarations.GetDeclarations().Count);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void RegisterType_registers_base_types_before_the_requested_type()
    {
        var declarations = new TypeDeclarations();
        var changes = new List<TypeDeclaration>();
        declarations.DeclarationChanged += changes.Add;

        declarations.RegisterType(typeof(DeclarationSample));

        Assert.Equal(2, changes.Count);
        Assert.Contains("class DeclarationBase {", changes[0].Content);
        Assert.Contains(
            "class DeclarationSample extends Duets.Tests.TestTypes.Declarations.DeclarationBase {",
            changes[1].Content
        );
    }

    [Fact]
    public void RegisterType_replaces_a_namespace_placeholder_with_an_empty_namespace_declaration()
    {
        var declarations = new TypeDeclarations();
        var changes = new List<TypeDeclaration>();
        declarations.DeclarationChanged += changes.Add;

        declarations.RegisterNamespace("Duets.Tests.TestTypes.NamespaceTargets");
        declarations.RegisterType(typeof(NamespaceAlpha));

        Assert.Equal(3, changes.Count);
        Assert.Contains(changes, declaration => declaration.Content.Contains("const $name"));
        Assert.Contains(changes, declaration => declaration.Content.Contains("class NamespaceAlpha"));
        Assert.Contains(changes, declaration => declaration.Content == "declare namespace Duets.Tests.TestTypes.NamespaceTargets { }\n");
    }
}
