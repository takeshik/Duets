using Duets.Tests.TestTypes.Declarations;

namespace Duets.Tests;

public sealed class ClrDeclarationGeneratorTests
{
    [Fact]
    public void GenerateTypeDefTs_collapses_overloads_with_same_typescript_signature_into_grouped_jsdoc()
    {
        var generator = new ClrDeclarationGenerator();

        var actual = generator.GenerateTypeDefTs(typeof(OverloadSample));

        // int and long both map to number, so both overloads collapse to one TS signature
        Assert.Equal(1, actual.Split("Format(value: number): string;").Length - 1);
        // Both CLR signatures are listed in the grouped JSDoc comment
        Assert.Contains("* - String Format(Int32 value)", actual);
        Assert.Contains("* - String Format(Int64 value)", actual);
    }

    [Fact]
    public void GenerateTypeDefTs_emits_implemented_interface_bridge_for_classes()
    {
        var generator = new ClrDeclarationGenerator();

        var actual = generator.GenerateTypeDefTs(typeof(DeclarationExtensionTarget));

        Assert.Contains(
            "class DeclarationExtensionTarget implements Duets.Tests.TestTypes.Declarations.IDeclarationExtensionTarget {",
            actual
        );
        Assert.Contains(
            "interface DeclarationExtensionTarget extends Duets.Tests.TestTypes.Declarations.IDeclarationExtensionTarget {",
            actual
        );
    }

    [Fact]
    public void GenerateTypeDefTs_generates_class_members_with_supported_type_mappings()
    {
        var generator = new ClrDeclarationGenerator();

        var actual = generator.GenerateTypeDefTs(typeof(DeclarationSample));

        Assert.Contains("declare namespace Duets.Tests.TestTypes.Declarations {", actual);
        Assert.Contains("class DeclarationSample extends Duets.Tests.TestTypes.Declarations.DeclarationBase {", actual);
        Assert.Contains("static GlobalCount: number;", actual);
        Assert.Contains("readonly OptionalCount: number | null;", actual);
        Assert.Contains("Names: string[];", actual);
        Assert.Contains("Scores: { [key: string]: number };", actual);
        Assert.Contains("LoadAsync(value: string, optional: number | null): Promise<string>;", actual);
        Assert.Contains("Convert(_default: string, values: number[]): string;", actual);
        Assert.Contains("// [skipped] Void Mutate(ref Int32 value)", actual);
        Assert.DoesNotContain("Item:", actual);
    }

    [Fact]
    public void GenerateTypeDefTs_generates_constructor_signatures()
    {
        var generator = new ClrDeclarationGenerator();

        var actual = generator.GenerateTypeDefTs(typeof(ConstructorSample));

        Assert.Contains("constructor(name: string, count: number);", actual);
    }

    [Fact]
    public void GenerateTypeDefTs_generates_generic_method_signatures_without_backtick_arity()
    {
        var generator = new ClrDeclarationGenerator();

        // System.Array has many generic static methods: Sort<T>, Find<T>, Reverse<T>, etc.
        var actual = generator.GenerateTypeDefTs(typeof(Array));

        // MethodInfo.Name has no backtick arity suffix — method names must use clean identifiers
        Assert.Contains("Sort<T>(", actual);
        Assert.Contains("Find<T>(", actual);
        Assert.Contains("Reverse<T>(", actual);
        // The TypeScript declarations themselves must not contain backtick arity suffixes.
        // JSDoc comment lines (/** ... */ or * ...) may contain raw CLR names, so exclude them.
        var declarationLines = actual.Split('\n')
            .Where(l =>
                {
                    var t = l.TrimStart();
                    return !t.StartsWith("//") && !t.StartsWith("/*") && !t.StartsWith("*");
                }
            );
        Assert.DoesNotContain(declarationLines, l => l.Contains('`'));
    }

    [Fact]
    public void GenerateTypeDefTs_generates_interface_enum_and_generic_headers()
    {
        var generator = new ClrDeclarationGenerator();

        var interfaceDef = generator.GenerateTypeDefTs(typeof(IDeclarationContract));
        var enumDef = generator.GenerateTypeDefTs(typeof(DeclarationMode));
        var genericDef = generator.GenerateTypeDefTs(typeof(GenericBox<>));

        Assert.Contains("interface IDeclarationContract {", interfaceDef);
        Assert.Contains("readonly Name: string;", interfaceDef);
        Assert.Contains("Count(seed: number): number;", interfaceDef);

        Assert.Contains("enum DeclarationMode {", enumDef);
        Assert.Contains("Alpha = 1,", enumDef);
        Assert.Contains("Beta = 3,", enumDef);

        Assert.Contains("class GenericBox<T> {", genericDef);
        Assert.Contains("Value: T;", genericDef);
    }

    [Fact]
    public void GenerateTypeDefTs_maps_bool_task_static_reference_and_unsupported_dict_key_types()
    {
        var generator = new ClrDeclarationGenerator();

        var actual = generator.GenerateTypeDefTs(typeof(TypeMappingSample));

        Assert.Contains("Flag: boolean;", actual);
        Assert.Contains("Run(): Promise<void>;", actual);
        Assert.Contains("static StaticOp(enabled: boolean): void;", actual);
        Assert.Contains("GetBase(): Duets.Tests.TestTypes.Declarations.DeclarationBase;", actual);
        Assert.Contains("WeirdMap: any;", actual);
    }

    [Fact]
    public void GenerateTypeDefTs_uses_toplevel_declare_keyword_for_types_without_namespace()
    {
        var generator = new ClrDeclarationGenerator();

        var classDef = generator.GenerateTypeDefTs(typeof(NoNamespaceClass));
        var interfaceDef = generator.GenerateTypeDefTs(typeof(INoNamespaceInterface));
        var enumDef = generator.GenerateTypeDefTs(typeof(NoNamespaceEnum));

        Assert.DoesNotContain("declare namespace", classDef);
        Assert.Contains("declare class NoNamespaceClass {", classDef);

        Assert.DoesNotContain("declare namespace", interfaceDef);
        Assert.Contains("declare interface INoNamespaceInterface {", interfaceDef);

        Assert.DoesNotContain("declare namespace", enumDef);
        Assert.Contains("declare enum NoNamespaceEnum {", enumDef);
    }
}
