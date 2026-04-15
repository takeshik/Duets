using System.Reflection;
using Duets.Jint;
using Duets.Tests.TestSupport;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Duets.Tests;

public sealed class ScriptTypingsTests
{
    private static Harness CreateHarness(bool allowClr = true)
    {
        var declarations = new TypeDeclarations();
        var engine = JintTestRuntime.CreateEngine(
            allowClr
                ? options => options.AllowClr(
                    Assembly.GetExecutingAssembly(),
                    typeof(ScriptEngine).Assembly,
                    typeof(JintScriptEngine).Assembly,
                    typeof(Assembly).Assembly
                )
                : null
        );
        engine.RegisterTypeBuiltins(declarations);
        if (allowClr)
        {
            engine.Execute(
                """
                var DuetsNs = importNamespace('Duets');
                var JintNs = importNamespace('Duets.Jint');
                var NamespaceTargetsNs = importNamespace('Duets.Tests.TestTypes.NamespaceTargets');
                """
            );
        }

        return new Harness(declarations, engine);
    }

    private sealed class Harness(TypeDeclarations declarations, ScriptEngine engine) : IDisposable
    {
        public TypeDeclarations Declarations { get; } = declarations;
        public ScriptEngine Engine { get; } = engine;

        public IReadOnlyList<TypeDeclaration> GetNonBuiltinDeclarations()
        {
            return this.Declarations
                .GetDeclarations()
                .Where(declaration => !declaration.Content.Contains("declare const typings:"))
                .ToList();
        }

        public void Dispose()
        {
            this.Engine.Dispose();
        }
    }

    [Theory]
    [InlineData("typings.importType('Duets.Tests.TestTypes.NamespaceTargets.NamespaceAlpha, Duets.Tests')")]
    [InlineData("typings.importType(NamespaceTargetsNs.NamespaceAlpha)")]
    public void ImportType_registers_the_requested_type_declaration(string code)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute(code);

        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
    }

    [Theory]
    // "System" exercises generic type definitions heavily: Nullable<T>, Action<T>, Func<T,TResult>, etc.
    // This was the namespace that exposed the IndexOf('`') == -1 crash in BuildTypeHeader.
    [InlineData("System", "Exception")]
    [InlineData("System.IO", "File")]
    // System.Collections.Generic is dense with IsGenericTypeDefinition types: List<T>, Dictionary<TKey,TValue>, etc.
    [InlineData("System.Collections.Generic", "List")]
    [InlineData("System.Text", "StringBuilder")]
    public void ImportNamespace_registers_types_in_bcl_namespaces(string ns, string expectedTypeName)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.importNamespace('{ns}')");

        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains(expectedTypeName));
    }

    [Theory]
    // List<T> (System.Collections.Generic.List`1) is in System.Collections.dll in .NET 5+
    [InlineData("System.Collections", "List")]
    public void ImportAssembly_registers_types_from_bcl_assembly(string assemblyName, string expectedTypeName)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.importAssembly('{assemblyName}')");

        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains(expectedTypeName));
    }

    [Theory]
    [InlineData("System.IO", "File")]
    [InlineData("System.IO", "Directory")]
    // System.Collections.Generic contains generic types (List`1, Dictionary`2, etc.) —
    // exercises the GetScriptName path that strips backtick arity suffixes in the global name
    // and declare var declaration.
    [InlineData("System.Collections.Generic", "List")]
    [InlineData("System.Collections.Generic", "Dictionary")]
    public void UsingNamespace_exposes_bcl_type_as_global(string ns, string typeName)
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.usingNamespace('{ns}')");

        // Type is accessible at runtime without namespace prefix
        Assert.False(
            harness.Engine.Evaluate(typeName).IsUndefined(),
            $"{typeName} should be a global after usingNamespace('{ns}')"
        );

        // declare var is registered so completions work without namespace prefix
        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains($"declare var {typeName}:"));
    }

    [Fact]
    public void ClrTypeOf_throws_for_non_type_references()
    {
        using var harness = CreateHarness();

        var exception = Assert.ThrowsAny<Exception>(() => harness.Engine.Evaluate("clrTypeOf(123)"));

        Assert.Contains("Expected a CLR type reference", exception.ToString());
    }

    [Fact]
    public void Global_importNamespace_does_not_register_type_declarations()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("importNamespace('Duets.Tests.TestTypes.NamespaceTargets')");

        Assert.DoesNotContain(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
        Assert.DoesNotContain(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains("class NamespaceBeta"));
    }

    [Fact]
    public void ImportAssemblyOf_registers_backend_specific_types_from_the_backend_assembly()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.importAssemblyOf(JintNs.TypeScriptService)");

        var declarations = harness.GetNonBuiltinDeclarations().Select(declaration => declaration.Content).ToList();
        Assert.Contains(declarations, content => content.Contains("class TypeScriptService"));
        Assert.Contains(declarations, content => content.Contains("class BabelTranspiler"));
    }

    [Fact]
    public void ImportAssemblyOf_registers_representative_public_types_from_the_containing_assembly()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.importAssemblyOf(DuetsNs.DuetsSession)");

        var declarations = harness.GetNonBuiltinDeclarations().Select(declaration => declaration.Content).ToList();
        Assert.Contains(declarations, content => content.Contains("class DuetsSession"));
        Assert.Contains(declarations, content => content.Contains("interface ITranspiler"));
    }

    [Fact]
    public void ImportAssemblyOf_rejects_values_that_are_not_type_references()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<ArgumentException>(() =>
            {
                typings.ImportAssemblyOf(new JsString("Duets.ScriptEngine"));
            }
        );

        Assert.Contains("Expected a CLR type reference", exception.Message);
    }

    [Fact]
    public void ImportAssembly_rejects_values_that_are_not_strings_or_assemblies()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<ArgumentException>(() =>
            {
                typings.ImportAssembly(JsBoolean.False);
            }
        );

        Assert.Contains("Expected an assembly name string or an Assembly object", exception.Message);
    }

    [Fact]
    public void ImportAssembly_with_a_wrapped_assembly_object_registers_representative_public_types()
    {
        var declarations = new TypeDeclarations();
        var typings = new ScriptTypings(declarations);
        var engine = new Engine(options => options.AllowClr());
        var assemblyRef = ObjectWrapper.Create(engine, typeof(TypeScriptService).Assembly, typeof(Assembly));

        typings.ImportAssembly(assemblyRef);

        var contents = declarations.GetDeclarations().Select(declaration => declaration.Content).ToList();
        Assert.Contains(contents, content => content.Contains("class TypeScriptService"));
        Assert.Contains(contents, content => content.Contains("class BabelTranspiler"));
    }

    [Fact]
    public void ImportAssembly_with_an_assembly_name_registers_representative_public_types()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.importAssembly('{typeof(TypeScriptService).Assembly.FullName}')");

        var declarations = harness.GetNonBuiltinDeclarations().Select(declaration => declaration.Content).ToList();
        Assert.Contains(declarations, content => content.Contains("class TypeScriptService"));
        Assert.Contains(declarations, content => content.Contains("class BabelTranspiler"));
    }

    [Fact]
    public void ImportNamespace_rejects_values_that_are_not_strings_or_namespace_references()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<ArgumentException>(() =>
            {
                typings.ImportNamespace(JsNumber.Create(123));
            }
        );

        Assert.Contains("Expected a namespace name string", exception.Message);
    }

    [Fact]
    public void ImportNamespace_with_a_namespace_reference_registers_types_in_that_namespace()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.importNamespace(NamespaceTargetsNs)");

        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains("class NamespaceBeta"));
    }

    [Fact]
    public void ImportNamespace_with_a_string_requires_AllowClr()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<InvalidOperationException>(() => typings.ImportNamespace(new JsString("System.IO")));

        Assert.Contains("AllowClr", exception.Message);
    }

    [Fact]
    public void ImportNamespace_with_a_string_returns_a_usable_namespace_reference_and_registers_its_types()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("var ns = typings.importNamespace('Duets.Tests.TestTypes.NamespaceTargets')");
        var result = harness.Engine.Evaluate("new ns.NamespaceAlpha()");

        Assert.True(result.IsObject());
        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains("class NamespaceAlpha"));
        Assert.Contains(harness.GetNonBuiltinDeclarations(), declaration => declaration.Content.Contains("class NamespaceBeta"));
    }

    [Fact]
    public void ImportType_rejects_unknown_type_names()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                typings.ImportType(new JsString("Missing.Type, Missing.Assembly"));
            }
        );

        Assert.Contains("Type not found", exception.Message);
    }

    [Fact]
    public void ImportType_rejects_values_that_are_not_type_references_or_strings()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<ArgumentException>(() =>
            {
                typings.ImportType(JsNumber.Create(123));
            }
        );

        Assert.Contains("Expected a CLR type reference", exception.Message);
    }

    [Fact]
    public void RegisterTypeBuiltins_registers_the_typings_declaration_and_exposes_clrTypeOf()
    {
        using var harness = CreateHarness();

        var result = harness.Engine.Evaluate("clrTypeOf(System.String).FullName");

        Assert.Equal("System.String", result.ToString());
        Assert.Contains(harness.Declarations.GetDeclarations(), declaration => declaration.Content.Contains("declare const typings:"));
    }

    [Fact]
    public void ScanAssemblyOf_registers_a_placeholder_for_the_containing_namespace()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.scanAssemblyOf(DuetsNs.DuetsSession)");

        var declaration = Assert.Single(harness.GetNonBuiltinDeclarations());
        Assert.Equal("declare namespace Duets { const $name: 'Duets'; }\n", declaration.Content);
    }

    [Fact]
    public void ScanAssemblyOf_rejects_values_that_are_not_type_references()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<ArgumentException>(() =>
            {
                typings.ScanAssemblyOf(new JsString("Duets.ScriptEngine"));
            }
        );

        Assert.Contains("Expected a CLR type reference", exception.Message);
    }

    [Fact]
    public void ScanAssembly_rejects_values_that_are_not_strings_or_assemblies()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<ArgumentException>(() =>
            {
                typings.ScanAssembly(JsBoolean.True);
            }
        );

        Assert.Contains("Expected an assembly name string or an Assembly object", exception.Message);
    }

    [Fact]
    public void ScanAssembly_with_a_wrapped_assembly_object_registers_a_placeholder_for_the_duets_namespace()
    {
        var declarations = new TypeDeclarations();
        var typings = new ScriptTypings(declarations);
        var engine = new Engine(options => options.AllowClr());
        var assemblyRef = ObjectWrapper.Create(engine, typeof(ScriptEngine).Assembly, typeof(Assembly));

        typings.ScanAssembly(assemblyRef);

        var declaration = Assert.Single(declarations.GetDeclarations());
        Assert.Equal("declare namespace Duets { const $name: 'Duets'; }\n", declaration.Content);
    }

    [Fact]
    public void ScanAssembly_with_an_assembly_name_registers_a_placeholder_for_the_duets_namespace()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute($"typings.scanAssembly('{typeof(ScriptEngine).Assembly.FullName}')");

        var declaration = Assert.Single(harness.GetNonBuiltinDeclarations());
        Assert.Equal("declare namespace Duets { const $name: 'Duets'; }\n", declaration.Content);
    }

    [Fact]
    public void UsingNamespace_does_not_expose_nested_types_as_globals()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.usingNamespace('System')");
        var declarations = harness.GetNonBuiltinDeclarations().Select(declaration => declaration.Content).ToList();

        Assert.Equal("undefined", harness.Engine.Evaluate("typeof SpecialFolder").ToString());
        Assert.NotEqual("undefined", harness.Engine.Evaluate("typeof Exception").ToString());
        Assert.DoesNotContain(declarations, content => content.Contains("declare var SpecialFolder:"));
        Assert.Contains(declarations, content => content.Contains("declare var Exception:"));
    }

    [Fact]
    public void UsingNamespace_exposes_runtime_globals_and_registers_declare_var_declarations()
    {
        using var harness = CreateHarness();

        harness.Engine.Execute("typings.usingNamespace('Duets.Tests.TestTypes.NamespaceTargets')");
        var result = harness.Engine.Evaluate("new NamespaceAlpha()");
        var declarations = harness.GetNonBuiltinDeclarations().Select(declaration => declaration.Content).ToList();

        Assert.True(result.IsObject());
        Assert.Contains(declarations, content => content.Contains("declare var NamespaceAlpha:"));
        Assert.Contains(declarations, content => content.Contains("declare var NamespaceBeta:"));
        Assert.Contains(declarations, content => content.Contains("class NamespaceAlpha"));
        Assert.Contains(declarations, content => content.Contains("class NamespaceBeta"));
    }

    [Fact]
    public void UsingNamespace_rejects_values_that_are_not_strings_or_namespace_references()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<ArgumentException>(() =>
            {
                typings.UsingNamespace(JsNumber.Create(123));
            }
        );

        Assert.Contains("Expected a namespace name string", exception.Message);
    }

    [Fact]
    public void UsingNamespace_with_a_string_requires_AllowClr()
    {
        var typings = new ScriptTypings(new TypeDeclarations());

        var exception = Assert.Throws<InvalidOperationException>(() => typings.UsingNamespace(new JsString("System.IO")));

        Assert.Contains("AllowClr", exception.Message);
    }
}
