using Duets.Tests.TestSupport;

namespace Duets.Tests;

/// <summary>
/// Shared contract tests run against every <see cref="ITranspiler"/> implementation.
/// Concrete subclasses supply the transpiler via <see cref="CreateTranspilerAsync"/>.
/// </summary>
public abstract class TranspilerCompatibilityTests : IAsyncLifetime
{
    private ITranspiler? _transpiler;

    protected ITranspiler Transpiler => this._transpiler!;

    protected abstract Task<ITranspiler> CreateTranspilerAsync();

    public async ValueTask InitializeAsync()
    {
        this._transpiler = await this.CreateTranspilerAsync();
    }

    public ValueTask DisposeAsync()
    {
        (this._transpiler as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void Transpile_enum_result_is_executable_by_jint()
    {
        using var engine = JintTestRuntime.CreateEngine(transpiler: this.Transpiler);

        engine.Execute("enum Status { Active = 1, Inactive = 2 }");
        var result = engine.Evaluate("Status.Active");

        Assert.Equal("1", result.ToString());
    }

    [Fact]
    public void Transpile_expands_enum_to_runtime_object()
    {
        var output = this.Transpiler.Transpile(
            "enum Direction { Up, Down, Left, Right }"
        );

        Assert.DoesNotContain("enum", output);
        Assert.Contains("Direction", output);
    }

    [Fact]
    public void Transpile_expands_string_enum()
    {
        var output = this.Transpiler.Transpile(
            """enum Color { Red = "red", Green = "green", Blue = "blue" }"""
        );

        Assert.DoesNotContain("enum", output);
        Assert.Contains("\"red\"", output);
    }

    [Fact]
    public void Transpile_handles_constructor_parameter_properties()
    {
        var output = this.Transpiler.Transpile(
            """
            class Point {
                constructor(public x: number, private y: number) {}
            }
            """
        );

        Assert.DoesNotContain("public", output);
        Assert.DoesNotContain("private", output);
        Assert.Contains("this.x", output);
        Assert.Contains("this.y", output);
    }

    [Fact]
    public void Transpile_handles_generics()
    {
        var output = this.Transpiler.Transpile(
            "function identity<T>(value: T): T { return value; }"
        );

        Assert.DoesNotContain("<T>", output);
        Assert.Contains("function identity", output);
    }

    [Fact]
    public void Transpile_handles_interface_declaration()
    {
        var output = this.Transpiler.Transpile(
            """
            interface Point { x: number; y: number; }
            const p: Point = { x: 1, y: 2 };
            """
        );

        Assert.DoesNotContain("interface", output);
        Assert.Contains("const p", output);
    }

    [Fact]
    public void Transpile_populates_diagnostics_on_syntax_error()
    {
        var diagnostics = new List<Diagnostic>();
        try
        {
            this.Transpiler.Transpile("const x = (", diagnostics: diagnostics);
        }
        catch
        {
            // Babel re-throws on parse failure; TS does not. Either way, diagnostics must be populated.
        }

        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Transpile_result_is_executable_by_jint()
    {
        using var engine = JintTestRuntime.CreateEngine(transpiler: this.Transpiler);

        engine.Execute("const answer: number = 40 + 2;");
        var result = engine.Evaluate("answer");

        Assert.Equal("42", result.ToString());
    }

    [Fact]
    public void Transpile_strips_type_annotations()
    {
        var output = this.Transpiler.Transpile("const x: number = 42;");

        Assert.DoesNotContain(": number", output);
        Assert.Contains("42", output);
    }
}

[Collection("TranspilerAssets")]
public sealed class BabelTranspilerCompatibilityTests : TranspilerCompatibilityTests
{
    public BabelTranspilerCompatibilityTests(TranspilerAssetsFixture assets, ITestOutputHelper output)
    {
        this._assets = assets;
        this._output = output;
    }

    private readonly TranspilerAssetsFixture _assets;
    private readonly ITestOutputHelper _output;

    protected override async Task<ITranspiler> CreateTranspilerAsync()
    {
        var transpiler = await this._assets.CreateBabelTranspilerAsync();
        this._output.WriteLine($"Babel {transpiler.Version}");
        return transpiler;
    }
}

[Collection("TranspilerAssets")]
public sealed class TypeScriptServiceCompatibilityTests : TranspilerCompatibilityTests
{
    public TypeScriptServiceCompatibilityTests(TranspilerAssetsFixture assets, ITestOutputHelper output)
    {
        this._assets = assets;
        this._output = output;
    }

    private readonly TranspilerAssetsFixture _assets;
    private readonly ITestOutputHelper _output;

    protected override async Task<ITranspiler> CreateTranspilerAsync()
    {
        var service = await this._assets.CreateTypeScriptServiceAsync(new TypeDeclarations());
        this._output.WriteLine($"TypeScript {service.Version}");
        return service;
    }
}
