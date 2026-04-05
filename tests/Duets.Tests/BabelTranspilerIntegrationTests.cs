namespace Duets.Tests;

/// <summary>
/// Integration tests for <see cref="BabelTranspiler"/> using the real Babel bundle.
/// The bundle is fetched from unpkg on first run and cached in the system temp directory.
/// </summary>
public sealed class BabelTranspilerIntegrationTests : IAsyncLifetime
{
    private readonly BabelTranspiler _transpiler = new();

    public async ValueTask InitializeAsync()
    {
        await this._transpiler.InitializeAsync();
    }

    public ValueTask DisposeAsync()
    {
        this._transpiler.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void Transpile_enum_result_is_executable_by_jint()
    {
        using var engine = new ScriptEngine(null, this._transpiler);

        engine.Execute("enum Status { Active = 1, Inactive = 2 }");
        var result = engine.Evaluate("Status.Active");

        Assert.Equal("1", result.ToString());
    }

    [Fact]
    public void Transpile_expands_enum_to_runtime_object()
    {
        var output = this._transpiler.Transpile(
            """
            enum Direction { Up, Down, Left, Right }
            """
        );

        Assert.DoesNotContain("enum", output);
        Assert.Contains("Direction", output);
    }

    [Fact]
    public void Transpile_expands_string_enum()
    {
        var output = this._transpiler.Transpile(
            """
            enum Color { Red = "red", Green = "green", Blue = "blue" }
            """
        );

        Assert.DoesNotContain("enum", output);
        Assert.Contains("\"red\"", output);
    }

    [Fact]
    public void Transpile_handles_constructor_parameter_properties()
    {
        var output = this._transpiler.Transpile(
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
        var output = this._transpiler.Transpile(
            """
            function identity<T>(value: T): T { return value; }
            """
        );

        Assert.DoesNotContain("<T>", output);
        Assert.Contains("function identity", output);
    }

    [Fact]
    public void Transpile_handles_interface_declaration()
    {
        var output = this._transpiler.Transpile(
            """
            interface Point { x: number; y: number; }
            const p: Point = { x: 1, y: 2 };
            """
        );

        Assert.DoesNotContain("interface", output);
        Assert.Contains("const p", output);
    }

    [Fact]
    public void Transpile_populates_diagnostics_and_rethrows_on_syntax_error()
    {
        var diagnostics = new List<Diagnostic>();

        Assert.ThrowsAny<Exception>(() => this._transpiler.Transpile("const x = (", diagnostics: diagnostics));

        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Transpile_result_is_executable_by_jint()
    {
        using var engine = new ScriptEngine(null, this._transpiler);

        engine.Execute("const answer: number = 40 + 2;");
        var result = engine.Evaluate("answer");

        Assert.Equal("42", result.ToString());
    }

    [Fact]
    public void Transpile_strips_type_annotations()
    {
        var output = this._transpiler.Transpile("const x: number = 42;");

        Assert.DoesNotContain(": number", output);
        Assert.Contains("42", output);
    }
}
