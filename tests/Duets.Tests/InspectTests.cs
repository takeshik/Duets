namespace Duets.Tests;

public sealed class InspectTests
{
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

    private static ScriptEngine CreateEngine()
    {
        return new ScriptEngine(null, new IdentityTranspiler());
    }

    private static string Inspect(ScriptEngine engine, string expr)
    {
        return engine.Evaluate($"util.inspect({expr})").ToString()!;
    }

    private static string Inspect(ScriptEngine engine, string expr, string opts)
    {
        return engine.Evaluate($"util.inspect({expr}, {opts})").ToString()!;
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("3.14", "3.14")]
    [InlineData("-1", "-1")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public void Primitive_values_format_without_quotes(string input, string expected)
    {
        using var engine = CreateEngine();
        Assert.Equal(expected, Inspect(engine, input));
    }

    [Fact]
    public void Anonymous_function_formats_as_anonymous()
    {
        using var engine = CreateEngine();
        Assert.Equal("[Function: (anonymous)]", Inspect(engine, "function() {}"));
    }

    [Fact]
    public void Array_compact_produces_single_line()
    {
        using var engine = CreateEngine();
        Assert.Equal("[1,2,3]", Inspect(engine, "[1, 2, 3]", "{compact: true}"));
    }

    // --- arrays ---

    [Fact]
    public void Array_defaults_to_multiline()
    {
        using var engine = CreateEngine();
        Assert.Equal(
            """
                [
                  1,
                  2,
                  3
                ]
                """.Trim(),
            Inspect(engine, "[1, 2, 3]")
        );
    }

    // --- circular reference ---

    [Fact]
    public void Circular_reference_is_replaced_with_marker()
    {
        using var engine = CreateEngine();
        engine.Execute("var a = {}; a.self = a;");
        Assert.Equal(
            """
                {
                  "self": "[Circular]"
                }
                """.Trim(),
            Inspect(engine, "a")
        );
    }

    [Fact]
    public void Empty_array_formats_as_empty_brackets()
    {
        using var engine = CreateEngine();
        Assert.Equal("[]", Inspect(engine, "[]"));
    }

    [Fact]
    public void Empty_object_formats_as_empty_braces()
    {
        using var engine = CreateEngine();
        Assert.Equal("{}", Inspect(engine, "{}"));
    }

    [Fact]
    public void Function_formats_with_name()
    {
        using var engine = CreateEngine();
        Assert.Equal("[Function: foo]", Inspect(engine, "function foo() {}"));
    }

    [Fact]
    public void Nested_array_beyond_depth_shows_placeholder()
    {
        using var engine = CreateEngine();
        var result = Inspect(engine, "[[1, 2]]", "{depth: 1}");
        Assert.Equal(
            """
                [
                  "[Array]"
                ]
                """.Trim(),
            result
        );
    }

    [Fact]
    public void Nested_object_respects_depth()
    {
        using var engine = CreateEngine();
        var result = Inspect(engine, "{a: {b: {c: 1}}}", "{depth: 1}");
        Assert.Equal(
            """
                {
                  "a": "[Object]"
                }
                """.Trim(),
            result
        );
    }

    // --- primitives ---

    [Fact]
    public void Null_formats_as_null()
    {
        using var engine = CreateEngine();
        Assert.Equal("null", Inspect(engine, "null"));
    }

    [Fact]
    public void Object_compact_produces_single_line()
    {
        using var engine = CreateEngine();
        Assert.Equal("""{"x":1,"y":2}""", Inspect(engine, "{x: 1, y: 2}", "{compact: true}"));
    }

    // --- objects ---

    [Fact]
    public void Object_defaults_to_multiline()
    {
        using var engine = CreateEngine();
        Assert.Equal(
            """
                {
                  "x": 1,
                  "y": 2
                }
                """.Trim(),
            Inspect(engine, "{x: 1, y: 2}")
        );
    }

    [Fact]
    public void String_values_are_json_quoted()
    {
        using var engine = CreateEngine();
        Assert.Equal(
            """
                "hello"
                """.Trim(),
            Inspect(engine, "'hello'")
        );
    }

    [Fact]
    public void Undefined_formats_as_undefined()
    {
        using var engine = CreateEngine();
        Assert.Equal("undefined", Inspect(engine, "undefined"));
    }
}
