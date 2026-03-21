using System.Reflection;
using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.NamespaceTargets;
using Jint;
using Jint.Native;

namespace Duets.Tests;

public sealed class ScriptTypingsAndBuiltinsTests
{
    public static IEnumerable<object[]> UseTypeCases()
    {
        yield return [$"typings.useType('{typeof(NamespaceAlpha).AssemblyQualifiedName}')"];
        yield return ["typings.useType(NamespaceTargetsNs.NamespaceAlpha)"];
    }

    [Theory]
    [MemberData(nameof(UseTypeCases))]
    public void UseType_registers_type_definitions(string code)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute(code);

        var declaration = Assert.Single(harness.GetNonBuiltinDeclarations(), x => x.Content.Contains("class NamespaceAlpha"));
        Assert.Contains("declare namespace Duets.Tests.TestTypes.NamespaceTargets {", declaration.Content);
    }

    public static IEnumerable<object[]> ScanAssemblyCases()
    {
        yield return [$"typings.scanAssembly('{typeof(ScriptEngine).Assembly.FullName}')"];
    }

    [Theory]
    [MemberData(nameof(ScanAssemblyCases))]
    public void ScanAssembly_registers_namespace_skeletons(string code)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute(code);

        var declaration = Assert.Single(harness.GetNonBuiltinDeclarations());
        Assert.Equal("declare namespace Duets { const $name: 'Duets'; }\n", declaration.Content);
    }

    public static IEnumerable<object[]> UseAssemblyCases()
    {
        yield return [$"typings.useAssembly('{typeof(ScriptEngine).Assembly.FullName}')"];
    }

    [Theory]
    [MemberData(nameof(UseAssemblyCases))]
    public void UseAssembly_registers_public_types_from_an_assembly(string code)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute(code);

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();

        Assert.Contains(declarations, x => x.Contains("class ScriptEngine"));
        Assert.Contains(declarations, x => x.Contains("class TypeScriptService"));
        Assert.Contains(declarations, x => x.Contains("interface ITranspiler"));
    }

    [Theory]
    [InlineData("typings.useNamespace('Duets.Tests.TestTypes.NamespaceTargets')")]
    [InlineData("typings.useNamespace(NamespaceTargetsNs)")]
    public void UseNamespace_registers_all_public_types_in_the_namespace(string code)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute(code);

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();

        Assert.Equal(2, declarations.Count);
        Assert.Contains(declarations, x => x.Contains("class NamespaceAlpha"));
        Assert.Contains(declarations, x => x.Contains("class NamespaceBeta"));
    }

    private static TestHarness CreateHarness()
    {
        var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var engine = new ScriptEngine(
            options => options.AllowClr(Assembly.GetExecutingAssembly(), typeof(ScriptEngine).Assembly, typeof(Assembly).Assembly),
            new IdentityTranspiler()
        );
        engine.RegisterTypeBuiltins(service);
        engine.Execute(
            """
            var DuetsNs = importNamespace('Duets');
            var NamespaceTargetsNs = importNamespace('Duets.Tests.TestTypes.NamespaceTargets');
            """
        );
        return new TestHarness(service, engine);
    }

    private sealed class IdentityTranspiler : ITranspiler
    {
        public string Transpile(
            string input,
            string? fileName = null,
            IList<Diagnostic>? diagnostics = null,
            string? moduleName = null)
        {
            return input;
        }
    }

    private sealed class TestHarness(TypeScriptService service, ScriptEngine engine) : IDisposable
    {
        public TypeScriptService Service { get; } = service;
        public ScriptEngine Engine { get; } = engine;

        public IReadOnlyList<TypeScriptService.TypeDeclaration> GetNonBuiltinDeclarations()
        {
            return this.Service
                .GetTypeDeclarations()
                .Where(x => !x.Content.Contains("declare const typings:"))
                .ToList();
        }

        public void Dispose()
        {
            this.Engine.Dispose();
            this.Service.Dispose();
        }
    }

    [Fact]
    public void ClrTypeOf_throws_for_non_type_reference()
    {
        using var harness = CreateHarness();

        var exception = Assert.ThrowsAny<Exception>(() => harness.Engine.Evaluate("clrTypeOf(123)"));

        Assert.Contains("Expected a CLR type reference", exception.ToString());
    }

    [Fact]
    public void RegisterTypeBuiltins_registers_typings_declaration_and_exposes_clrTypeOf()
    {
        using var harness = CreateHarness();

        var result = harness.Engine.Evaluate("clrTypeOf(System.String).FullName");
        var declarations = harness.Service.GetTypeDeclarations().ToList();

        Assert.Equal("System.String", result.ToString());
        Assert.Contains(declarations, x => x.Content.Contains("declare const typings:"));
    }

    [Fact]
    public void ScanAssemblyOf_registers_namespace_skeletons_from_the_containing_assembly()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.scanAssemblyOf(DuetsNs.ScriptEngine)");

        var declaration = Assert.Single(harness.GetNonBuiltinDeclarations());
        Assert.Equal("declare namespace Duets { const $name: 'Duets'; }\n", declaration.Content);
    }

    [Fact]
    public void ScanAssemblyOf_throws_for_non_type_reference()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.ScanAssemblyOf(new JsString("Duets.ScriptEngine")));

        Assert.Contains("Expected a CLR type reference", exception.Message);
    }

    [Fact]
    public void ScanAssembly_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.ScanAssembly(JsBoolean.True));

        Assert.Contains("Expected an assembly name string or an Assembly object", exception.Message);
    }

    [Fact]
    public void UseAssemblyOf_registers_public_types_from_the_containing_assembly()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.useAssemblyOf(DuetsNs.ScriptEngine)");

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();

        Assert.Contains(declarations, x => x.Contains("class ScriptEngine"));
        Assert.Contains(declarations, x => x.Contains("class TypeScriptService"));
        Assert.Contains(declarations, x => x.Contains("interface ITranspiler"));
    }

    [Fact]
    public void UseAssemblyOf_throws_for_non_type_reference()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.UseAssemblyOf(new JsString("Duets.ScriptEngine")));

        Assert.Contains("Expected a CLR type reference", exception.Message);
    }

    [Fact]
    public void UseAssembly_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.UseAssembly(JsBoolean.False));

        Assert.Contains("Expected an assembly name string or an Assembly object", exception.Message);
    }

    [Fact]
    public void UseNamespace_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.UseNamespace(JsNumber.Create(123)));

        Assert.Contains("Expected a namespace reference or string", exception.Message);
    }

    [Fact]
    public void UseType_throws_for_unknown_type_name()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<InvalidOperationException>(() => typings.UseType(new JsString("Missing.Type, Missing.Assembly")));

        Assert.Contains("Type not found", exception.Message);
    }

    [Fact]
    public void UseType_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.UseType(JsNumber.Create(123)));

        Assert.Contains("Expected a CLR type reference", exception.Message);
    }
}
