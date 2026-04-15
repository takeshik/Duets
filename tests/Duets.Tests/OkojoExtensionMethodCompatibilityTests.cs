using Duets.Okojo;
using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.Declarations;
using Duets.Tests.TestTypes.Extensions;

namespace Duets.Tests;

public sealed class OkojoExtensionMethodCompatibilityTests
{
    private static (TypeDeclarations Declarations, OkojoScriptEngine Engine) CreateEngine()
    {
        var declarations = new TypeDeclarations();
        var engine = OkojoTestRuntime.CreateEngine();
        engine.RegisterTypeBuiltins(declarations);
        engine.Execute("var ExtNs = importNamespace('Duets.Tests.TestTypes.Extensions');");
        return (declarations, engine);
    }

    [Fact]
    public void AddExtensionMethods_is_idempotent()
    {
        var (declarations, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");
        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");

        var count = declarations.GetDeclarations()
            .Count(declaration => declaration.Content.Contains("interface Item") && declaration.Content.Contains("Describe"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddExtensionMethods_supports_host_values_and_return_values()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute(
            """
            typings.addExtensionMethods(ExtNs.ItemExtensions);
            typings.addExtensionMethods(ExtNs.ArrayExtensions);
            """
        );

        engine.SetValue("item", new Item { Label = "x", Value = 7 });
        engine.SetValue("factory", new ArrayFactory());

        Assert.Equal("x=7", engine.Evaluate("item.Describe()").ToString());
        Assert.Equal("14", engine.Evaluate("item.Map(i => i.Value * 2)").ToString());
        Assert.Equal("4", engine.Evaluate("factory.MakeNumbers().HeadOr(99)").ToString());
    }

    [Fact]
    public void AddExtensionMethods_supports_interface_targets()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.DeclarationExtensions)");
        engine.SetValue("target", new DeclarationExtensionTarget { Value = 21 });

        Assert.Equal("42", engine.Evaluate("target.DoubleValue()").ToString());
    }

    [Fact]
    public void AddExtensionMethods_supports_linq_extensions_for_host_arrays()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(\"System.Linq.Enumerable, System.Linq\")");
        engine.SetValue("items", new[] { 1, 2, 3 });

        Assert.True((bool) engine.Evaluate("Array.isArray(util.toJsArray(items.Select(x => x * 2).ToArray()))").ToObject()!);
        Assert.Equal("[2,4,6]", engine.Evaluate("util.inspect(util.toJsArray(items.Select(x => x * 2).ToArray()), { compact: true })").ToString());
    }

    [Fact]
    public void RegisterTypeBuiltins_makes_util_to_js_array_available_for_host_values()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.SetValue("items", new[] { 1, 2, 3 });
        engine.SetValue("matrix", new[,] { { 1, 2 }, { 3, 4 } });

        Assert.True((bool) engine.Evaluate("Array.isArray(util.toJsArray(items))").ToObject()!);
        Assert.Equal("2", engine.Evaluate("util.toJsArray(items)[1]").ToString());
        Assert.Equal(
            "[[1,2],[3,4]]",
            engine.Evaluate("util.inspect(util.toJsArray(matrix), { compact: true })").ToString()
        );
    }
}
