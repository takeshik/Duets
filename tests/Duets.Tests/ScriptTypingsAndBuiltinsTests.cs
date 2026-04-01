using System.Reflection;
using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.NamespaceTargets;
using Jint;
using Jint.Native;

namespace Duets.Tests;

public sealed class ScriptTypingsAndBuiltinsTests
{
    public static IEnumerable<object[]> ImportTypeCases()
    {
        yield return [$"typings.importType('{typeof(NamespaceAlpha).AssemblyQualifiedName}')"];
        yield return ["typings.importType(NamespaceTargetsNs.NamespaceAlpha)"];
    }

    [Theory]
    [MemberData(nameof(ImportTypeCases))]
    public void ImportType_registers_type_definitions(string code)
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

    public static IEnumerable<object[]> ImportAssemblyCases()
    {
        yield return [$"typings.importAssembly('{typeof(ScriptEngine).Assembly.FullName}')"];
    }

    [Theory]
    [MemberData(nameof(ImportAssemblyCases))]
    public void ImportAssembly_registers_public_types_from_an_assembly(string code)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute(code);

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();

        Assert.Contains(declarations, x => x.Contains("class ScriptEngine"));
        Assert.Contains(declarations, x => x.Contains("class TypeScriptService"));
        Assert.Contains(declarations, x => x.Contains("interface ITranspiler"));
    }

    [Theory]
    [InlineData("typings.importNamespace('Duets.Tests.TestTypes.NamespaceTargets')")]
    [InlineData("typings.importNamespace(NamespaceTargetsNs)")]
    public void ImportNamespace_registers_types_and_returns_usable_namespace_reference(string code)
    {
        using var harness = CreateHarness();

        // The call must return a usable namespace reference.
        harness.Engine.Execute($"var ns = {code}");
        harness.Engine.Execute("typings.importType(ns.NamespaceAlpha)");

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();
        Assert.Contains(declarations, x => x.Contains("class NamespaceAlpha"));
        Assert.Contains(declarations, x => x.Contains("class NamespaceBeta"));
    }

    [Theory]
    [InlineData("typings.usingNamespace('Duets.Tests.TestTypes.NamespaceTargets')")]
    [InlineData("typings.usingNamespace(NamespaceTargetsNs)")]
    public void UsingNamespace_exposes_types_as_globals_and_registers_declare_var(string code)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute(code);

        // Types are registered for completions
        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();
        Assert.Contains(declarations, x => x.Contains("class NamespaceAlpha"));
        Assert.Contains(declarations, x => x.Contains("class NamespaceBeta"));

        // declare var entries are registered so completions work without namespace prefix
        Assert.Contains(declarations, x => x.Contains("declare var NamespaceAlpha:"));
        Assert.Contains(declarations, x => x.Contains("declare var NamespaceBeta:"));

        // Types are accessible as globals at runtime
        var result = harness.Engine.Evaluate("new NamespaceAlpha()");
        Assert.True(result.IsObject());
    }

    public static IEnumerable<object[]> BclImportNamespaceCases()
    {
        // "System" exercises generic type definitions heavily: Nullable<T>, Action<T>, Func<T,TResult>, etc.
        // This was the namespace that exposed the IndexOf('`') == -1 crash in BuildTypeHeader.
        yield return ["System", "Exception"];
        yield return ["System.IO", "File"];
        // System.Collections.Generic is dense with IsGenericTypeDefinition types: List<T>, Dictionary<TKey,TValue>, etc.
        yield return ["System.Collections.Generic", "List"];
        yield return ["System.Text", "StringBuilder"];
    }

    [Theory]
    [MemberData(nameof(BclImportNamespaceCases))]
    public void ImportNamespace_registers_bcl_namespace_types(string ns, string expectedTypeName)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.importNamespace('{ns}')");

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();
        Assert.Contains(declarations, x => x.Contains(expectedTypeName));
    }

    public static IEnumerable<object[]> BclImportAssemblyCases()
    {
        // List<T> (System.Collections.Generic.List`1) is in System.Collections.dll in .NET 5+
        yield return ["System.Collections", "List"];
    }

    [Theory]
    [MemberData(nameof(BclImportAssemblyCases))]
    public void ImportAssembly_registers_bcl_assembly_types(string assemblyName, string expectedTypeName)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.importAssembly('{assemblyName}')");

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();
        Assert.Contains(declarations, x => x.Contains(expectedTypeName));
    }

    [Theory]
    [InlineData("System.IO", "File")]
    [InlineData("System.IO", "Directory")]
    public void UsingNamespace_exposes_bcl_type_as_global(string ns, string typeName)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.usingNamespace('{ns}')");

        // Type is accessible at runtime without namespace prefix
        var result = harness.Engine.Evaluate(typeName);
        Assert.False(result.IsUndefined(), $"{typeName} should be a global after usingNamespace('{ns}')");

        // declare var is registered so completions work without namespace prefix
        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();
        Assert.Contains(declarations, x => x.Contains($"declare var {typeName}:"));
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

    /// <summary>
    /// Verifies that the global <c>importNamespace</c> does NOT register type declarations for completions.
    /// Only <c>typings.importNamespace</c> does. This test guards against a silent override
    /// that would change this intentional design.
    /// </summary>
    [Fact]
    public void GlobalImportNamespace_does_not_register_types_for_completions()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var engine = new ScriptEngine(
            options => options.AllowClr(Assembly.GetExecutingAssembly()),
            new IdentityTranspiler()
        );
        engine.RegisterTypeBuiltins(service);

        engine.Execute("importNamespace('Duets.Tests.TestTypes.NamespaceTargets')");

        var declarations = service.GetTypeDeclarations()
            .Where(x => !x.Content.Contains("declare const typings:"))
            .Select(x => x.Content)
            .ToList();
        Assert.DoesNotContain(declarations, x => x.Contains("class NamespaceAlpha"));
        Assert.DoesNotContain(declarations, x => x.Contains("class NamespaceBeta"));
    }

    [Fact]
    public void ImportAssemblyOf_registers_public_types_from_the_containing_assembly()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.importAssemblyOf(DuetsNs.ScriptEngine)");

        var declarations = harness.GetNonBuiltinDeclarations().Select(x => x.Content).ToList();

        Assert.Contains(declarations, x => x.Contains("class ScriptEngine"));
        Assert.Contains(declarations, x => x.Contains("class TypeScriptService"));
        Assert.Contains(declarations, x => x.Contains("interface ITranspiler"));
    }

    [Fact]
    public void ImportAssemblyOf_throws_for_non_type_reference()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.ImportAssemblyOf(new JsString("Duets.ScriptEngine")));

        Assert.Contains("Expected a CLR type reference", exception.Message);
    }

    [Fact]
    public void ImportAssembly_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.ImportAssembly(JsBoolean.False));

        Assert.Contains("Expected an assembly name string or an Assembly object", exception.Message);
    }

    [Fact]
    public void ImportNamespace_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.ImportNamespace(JsNumber.Create(123)));

        Assert.Contains("Expected a namespace name string", exception.Message);
    }

    [Fact]
    public void ImportNamespace_throws_when_AllowClr_not_configured()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<InvalidOperationException>(() => typings.ImportNamespace(new JsString("System.IO")));

        Assert.Contains("AllowClr", exception.Message);
    }

    [Fact]
    public void ImportType_throws_for_unknown_type_name()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<InvalidOperationException>(() => typings.ImportType(new JsString("Missing.Type, Missing.Assembly")));

        Assert.Contains("Type not found", exception.Message);
    }

    [Fact]
    public void ImportType_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.ImportType(JsNumber.Create(123)));

        Assert.Contains("Expected a CLR type reference", exception.Message);
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
    public void UsingNamespace_throws_for_unsupported_value()
    {
        using var service = TypeScriptServiceTestFactory.CreateInitializedService();
        var typings = new ScriptTypings(service);

        var exception = Assert.Throws<ArgumentException>(() => typings.UsingNamespace(JsNumber.Create(123)));

        Assert.Contains("Expected a namespace name string", exception.Message);
    }
}
