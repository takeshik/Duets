using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.Declarations;
using Duets.Tests.TestTypes.NamespaceTargets;

namespace Duets.Tests;

public sealed class TypeScriptServiceRegistrationTests
{
    [Fact]
    public void GetTypeDeclarations_returns_a_snapshot()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();

        service.RegisterDeclaration("declare const first: number;");
        var snapshot = service.GetTypeDeclarations();
        service.RegisterDeclaration("declare const second: number;");

        Assert.Single(snapshot);
        Assert.Equal(2, service.GetTypeDeclarations().Count);
    }

    [Fact]
    public void RegisterDeclaration_ignores_duplicate_content()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var added = new List<TypeScriptService.TypeDeclaration>();
        service.TypeDeclarationAdded += added.Add;

        service.RegisterDeclaration("declare const answer: number;");
        service.RegisterDeclaration("declare const answer: number;");

        var declaration = Assert.Single(service.GetTypeDeclarations());
        Assert.Single(added);
        Assert.Equal(declaration, added[0]);
    }

    [Fact]
    public void RegisterNamespaceSkeleton_is_ignored_for_duplicate_calls()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var added = new List<TypeScriptService.TypeDeclaration>();
        service.TypeDeclarationAdded += added.Add;

        service.RegisterNamespaceSkeleton("Duets.Tests.TestTypes.NamespaceTargets");
        service.RegisterNamespaceSkeleton("Duets.Tests.TestTypes.NamespaceTargets");

        Assert.Single(added);
        Assert.Single(service.GetTypeDeclarations());
    }

    [Fact]
    public void RegisterNamespaceSkeleton_is_ignored_when_namespace_already_has_a_registered_type()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var added = new List<TypeScriptService.TypeDeclaration>();
        service.TypeDeclarationAdded += added.Add;

        service.RegisterType(typeof(NamespaceAlpha));
        service.RegisterNamespaceSkeleton("Duets.Tests.TestTypes.NamespaceTargets");

        Assert.Single(added);
        Assert.DoesNotContain(service.GetTypeDeclarations(), x => x.Content.Contains("$name"));
    }

    [Fact]
    public void RegisterNamespaceSkeleton_notifies_when_dummy_member_is_replaced_after_registering_a_real_type()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var added = new List<TypeScriptService.TypeDeclaration>();
        service.TypeDeclarationAdded += added.Add;

        service.RegisterNamespaceSkeleton("Duets.Tests.TestTypes.NamespaceTargets");
        service.RegisterType(typeof(NamespaceAlpha));

        var declarations = service.GetTypeDeclarations().ToList();

        Assert.Equal(3, added.Count);
        Assert.Contains(added, x => x.Content.Contains("const $name"));
        Assert.Contains(added, x => x.Content.Contains("class NamespaceAlpha"));
        Assert.Contains(added, x => x.Content == "declare namespace Duets.Tests.TestTypes.NamespaceTargets { }\n");
        Assert.Contains(declarations, x => x.Content == "declare namespace Duets.Tests.TestTypes.NamespaceTargets { }\n");
        Assert.Contains(declarations, x => x.Content.Contains("class NamespaceAlpha"));
    }

    [Fact]
    public void RegisterType_registers_base_type_before_the_requested_type_and_ignores_duplicates()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var added = new List<TypeScriptService.TypeDeclaration>();
        service.TypeDeclarationAdded += added.Add;

        service.RegisterType(typeof(DeclarationSample));
        service.RegisterType(typeof(DeclarationSample));

        Assert.Equal(2, added.Count);
        Assert.Contains("class DeclarationBase {", added[0].Content);
        Assert.Contains(
            "class DeclarationSample extends Duets.Tests.TestTypes.Declarations.DeclarationBase {",
            added[1].Content
        );
        Assert.Equal(2, service.GetTypeDeclarations().Count);
    }

    [Fact]
    public void RegisterType_throws_when_called_before_ResetAsync()
    {
        using var service = new TypeScriptService();

        Assert.Throws<InvalidOperationException>(() => service.RegisterType(typeof(string)));
    }
}
