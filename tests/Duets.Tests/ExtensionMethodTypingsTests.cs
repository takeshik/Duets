using System.Reflection;
using Duets.Jint;
using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.Declarations;
using Duets.Tests.TestTypes.Extensions;
using Jint;
using Jint.Native;

namespace Duets.Tests;

public sealed class ExtensionMethodTypingsTests
{
    private sealed class CountingRegistrar : ITypeDeclarationRegistrar
    {
        private static readonly ClrDeclarationGenerator _generator = new();

        public List<string> RawDeclarations { get; } = [];

        public void RegisterType(Type type)
        {
        }

        public void RegisterDeclaration(string content)
        {
            this.RawDeclarations.Add(content);
        }

        public void RegisterNamespace(string namespaceName)
        {
        }

        public void RegisterExtensionMethodContainer(Type containerType)
        {
            this.RawDeclarations.Add(_generator.GenerateExtensionMethodsTs(containerType));
        }
    }

    // -------------------------------------------------------------------------
    // Runtime: MemberAccessor dispatch via ExtensionMethodRegistry
    // -------------------------------------------------------------------------

    private static (TypeDeclarations declarations, IScriptEngine engine) CreateEngine()
    {
        var declarations = new TypeDeclarations();
        var engine = JintTestRuntime.CreateEngine(opts => opts.AllowClr(
                Assembly.GetExecutingAssembly(),
                typeof(IScriptEngine).Assembly,
                typeof(JintScriptEngine).Assembly
            )
        );
        engine.RegisterTypeBuiltins(declarations);
        engine.Execute(
            """
            var ExtNs = importNamespace('Duets.Tests.TestTypes.Extensions');
            """
        );
        return (declarations, engine);
    }

    [Fact]
    public void AddExtensionMethods_does_not_re_register_declarations_for_duplicate_container_registration()
    {
        var declarations = new CountingRegistrar();
        using var engine = JintTestRuntime.CreateEngine(opts => opts.AllowClr(
                Assembly.GetExecutingAssembly(),
                typeof(IScriptEngine).Assembly,
                typeof(JintScriptEngine).Assembly
            )
        );
        engine.RegisterTypeBuiltins(declarations);
        engine.Execute(
            """
            var ExtNs = importNamespace('Duets.Tests.TestTypes.Extensions');
            typings.addExtensionMethods(ExtNs.ItemExtensions);
            typings.addExtensionMethods(ExtNs.ItemExtensions);
            """
        );

        Assert.Equal(
            2,
            declarations.RawDeclarations.Count
        );
        Assert.Single(
            declarations.RawDeclarations,
            content => content.Contains("interface Item") && content.Contains("Describe")
        );
    }

    [Fact]
    public void AddExtensionMethods_is_idempotent()
    {
        var (declarations, engine) = CreateEngine();
        using var _ = engine;

        // Registering twice should not duplicate declarations or cause errors
        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");
        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");

        var count = declarations.GetDeclarations()
            .Count(d => d.Content.Contains("interface Item") && d.Content.Contains("Describe"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddExtensionMethods_makes_concrete_array_extension_callable_at_runtime_for_host_values()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ByteArrayExtensions)");

        engine.SetValue("items", new byte[] { 10, 20 });
        var result = (int) (double) engine.Evaluate("items.FirstPlus(5)").ToObject()!;

        Assert.Equal(15, result);
    }

    [Fact]
    public void AddExtensionMethods_makes_generic_array_extension_callable_at_runtime_for_clr_method_return_values()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ArrayExtensions)");

        engine.SetValue("factory", new ArrayFactory());
        var result = (int) (double) engine.Evaluate("factory.MakeNumbers().HeadOr(99)").ToObject()!;

        Assert.Equal(4, result);
    }

    [Fact]
    public void AddExtensionMethods_makes_generic_array_extension_callable_at_runtime_for_host_values()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ArrayExtensions)");

        engine.SetValue("items", new[] { 10, 20 });
        var result = (int) (double) engine.Evaluate("items.HeadOr(99)").ToObject()!;

        Assert.Equal(10, result);
    }

    [Fact]
    public void AddExtensionMethods_makes_generic_extension_with_delegate_callable_at_runtime()
    {
        var (declarations, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");

        var item = new Item { Label = "test", Value = 7 };
        engine.SetValue("item", item);
        var result = (int) (double) engine.Evaluate("item.Map(i => i.Value * 2)").ToObject()!;

        Assert.Equal(14, result);
    }

    [Fact]
    public void AddExtensionMethods_makes_interface_target_extension_callable_at_runtime()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.DeclarationExtensions)");

        engine.SetValue("target", new DeclarationExtensionTarget { Value = 21 });
        var result = (int) (double) engine.Evaluate("target.DoubleValue()").ToObject()!;

        Assert.Equal(42, result);
    }

    [Fact]
    public void AddExtensionMethods_makes_linq_select_callable_at_runtime_for_host_arrays()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(\"System.Linq.Enumerable, System.Linq\")");

        engine.SetValue("items", new[] { 1, 2, 3 });
        var result = engine.Evaluate("util.toJsArray(items.Select(x => x * 2).ToArray())");

        Assert.True((bool) engine.Evaluate("Array.isArray(util.toJsArray(items.Select(x => x * 2).ToArray()))").ToObject()!);
        Assert.Equal("[2,4,6]", engine.Evaluate("util.inspect(util.toJsArray(items.Select(x => x * 2).ToArray()), { compact: true })").ToString());
    }

    [Fact]
    public void AddExtensionMethods_makes_no_arg_extension_callable_at_runtime()
    {
        var (declarations, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");

        var item = new Item { Label = "hello", Value = 42 };
        engine.SetValue("item", item);
        var result = engine.Evaluate("item.Describe()").ToString();

        Assert.Equal("hello=42", result);
    }

    [Fact]
    public void AddExtensionMethods_makes_value_arg_extension_callable_at_runtime()
    {
        var (declarations, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");

        var item = new Item { Label = "x", Value = 1 };
        engine.SetValue("item", item);
        var result = engine.Evaluate("item.WithValue(99)");
        var resultItem = result.ToObject() as Item;

        Assert.NotNull(resultItem);
        Assert.Equal("x", resultItem.Label);
        Assert.Equal(99, resultItem.Value);
    }

    [Fact]
    public void AddExtensionMethods_registers_declaration_in_TypeDeclarations()
    {
        var (declarations, engine) = CreateEngine();
        using var _ = engine;

        engine.Execute("typings.addExtensionMethods(ExtNs.ItemExtensions)");

        var decls = declarations.GetDeclarations();
        Assert.Contains(decls, d => d.Content.Contains("interface Item") && d.Content.Contains("Describe"));
    }

    [Fact]
    public void ExtensionMethodRegistry_makes_generic_array_extension_callable()
    {
        var registryType = typeof(JintScriptEngine).Assembly.GetType("Duets.Jint.ExtensionMethodRegistry", true)!;
        var registry = Activator.CreateInstance(registryType)!;
        registryType.GetMethod("Register")!.Invoke(registry, [typeof(ArrayExtensions)]);

        using var engine = new Engine();
        var memberValue = (JsValue?) registryType.GetMethod("CreateMemberValue")!.Invoke(
            registry,
            [engine, new[] { 10, 20 }, "HeadOr"]
        );

        Assert.NotNull(memberValue);

        engine.SetValue("headOr", memberValue);
        var result = (int) (double) engine.Evaluate("headOr(99)").ToObject()!;
        Assert.Equal(10, result);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_does_not_emit_unsound_global_array_augmentation_for_concrete_array_receivers()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(ByteArrayExtensions));

        Assert.DoesNotContain("interface Array<T> {", decl);
        Assert.DoesNotContain("FirstPlus(extra: number): number;", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_emits_array_augmentation_for_generic_array_targets()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(ArrayExtensions));

        Assert.Contains("interface Array<T> {", decl);
        Assert.Contains("HeadOr(fallback: T): T;", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_emits_extension_method_with_value_param()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(ItemExtensions));

        Assert.Contains("WithValue(value: number):", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_emits_general_interface_augmentation_for_non_projection_receivers()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(DeclarationExtensions));

        Assert.Contains("declare namespace Duets.Tests.TestTypes.Declarations {", decl);
        Assert.Contains("interface IDeclarationExtensionTarget {", decl);
        Assert.Contains("DoubleValue(): number;", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_emits_generic_extension_method_with_delegate_param()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(ItemExtensions));

        // Map<TResult>(Func<Item, TResult>) → Map<TResult>(selector: (arg0: ...) => TResult): TResult
        Assert.Contains("Map<TResult>(", decl);
        Assert.Contains("): TResult;", decl);
    }
    // -------------------------------------------------------------------------
    // TypeScript declaration generation
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateExtensionMethodsTs_emits_interface_augmentation_for_target_type()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(ItemExtensions));

        Assert.Contains("interface Item {", decl);
        Assert.Contains("declare namespace Duets.Tests.TestTypes.Extensions {", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_emits_no_arg_extension_method()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(ItemExtensions));

        // Describe() takes no args beyond 'this'
        Assert.Contains("Describe(): string;", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_keeps_clr_collection_augmentations_for_array_mapped_targets()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(Enumerable));

        Assert.Contains("interface Array<T> {", decl);
        Assert.Contains("declare namespace System.Collections.Generic {", decl);
        Assert.Contains("interface List<T> {", decl);
        Assert.Contains("Select<TResult>(", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_keeps_named_aliases_for_other_lossy_projection_families()
    {
        var generator = new ClrDeclarationGenerator();

        var decl = generator.GenerateExtensionMethodsTs(typeof(DictionaryExtensions));

        Assert.Contains("declare namespace System.Collections.Generic {", decl);
        Assert.Contains("interface IDictionary<TKey, TValue> {", decl);
        Assert.Contains("interface Dictionary<TKey, TValue> {", decl);
        Assert.Contains("CountPlus(extra: number): number;", decl);
    }

    [Fact]
    public void GenerateExtensionMethodsTs_substitutes_iface_type_params_for_generic_targets()
    {
        var generator = new ClrDeclarationGenerator();

        // Enumerable methods targeting IEnumerable<T>: TSource should become T
        var decl = generator.GenerateExtensionMethodsTs(typeof(Enumerable));

        // IEnumerable<T> maps to T[] in TypeScript, so extension methods augment Array<T>
        // rather than a CLR-namespace interface, so that completions apply to T[] typed values.
        Assert.Contains("interface Array<T> {", decl);
        // Select<TResult> should appear — TSource becomes T in parameter, TResult stays
        Assert.Contains("Select<TResult>(", decl);
        // Where should appear with T (not TSource) in predicate
        Assert.Contains("Where(", decl);
    }

    [Fact]
    public void MapType_maps_Func_delegate_to_arrow_function_syntax()
    {
        var generator = new ClrDeclarationGenerator();

        // TypeMappingSample has no Func param, so use a type that does; generate Enumerable
        var decl = generator.GenerateExtensionMethodsTs(typeof(Enumerable));

        // Select's selector is Func<TSource, TResult> → (arg0: T) => TResult in TS
        Assert.Contains("(arg0: T) => TResult", decl);
    }

    [Fact]
    public void RegisterTypeBuiltins_makes_util_to_js_array_available_for_clr_method_return_values()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.SetValue("factory", new ArrayFactory());

        Assert.True((bool) engine.Evaluate("Array.isArray(util.toJsArray(factory.MakeNumbers()))").ToObject()!);
        Assert.Equal("6", engine.Evaluate("util.toJsArray(factory.MakeNumbers())[2]").ToString());
    }

    [Fact]
    public void RegisterTypeBuiltins_makes_util_to_js_array_available_for_host_clr_arrays()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.SetValue("items", new[] { 1, 2, 3 });

        Assert.True((bool) engine.Evaluate("Array.isArray(util.toJsArray(items))").ToObject()!);
        Assert.Equal("2", engine.Evaluate("util.toJsArray(items)[1]").ToString());
    }

    [Fact]
    public void RegisterTypeBuiltins_util_to_js_array_recursively_converts_multidimensional_clr_arrays()
    {
        var (_, engine) = CreateEngine();
        using var _ = engine;

        engine.SetValue("matrix", new[,] { { 1, 2 }, { 3, 4 } });

        Assert.True((bool) engine.Evaluate("Array.isArray(util.toJsArray(matrix))").ToObject()!);
        Assert.True((bool) engine.Evaluate("Array.isArray(util.toJsArray(matrix)[0])").ToObject()!);
        Assert.Equal(
            "[[1,2],[3,4]]",
            engine.Evaluate("util.inspect(util.toJsArray(matrix), { compact: true })").ToString()
        );
    }
}
