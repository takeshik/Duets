using Jint.Native;

namespace Duets.Tests;

public sealed class ScriptEngineTests
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

    private sealed class RecordingTranspiler(Dictionary<string, string> mappings) : ITranspiler
    {
        public List<string> Inputs { get; } = [];

        public string Transpile(
            string input,
            string? fileName = null,
            IList<Diagnostic>? diagnostics = null,
            string? moduleName = null)
        {
            this.Inputs.Add(input);
            return mappings[input];
        }
    }

    [Fact]
    public void Console_log_multiple_args_are_space_joined()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());
        ScriptConsoleEntry? entry = null;
        engine.ConsoleLogged += e => entry = e;

        engine.Execute("console.log('result:', 42)");

        Assert.NotNull(entry);
        Assert.Equal("result: 42", entry.Text);
    }

    [Fact]
    public void Console_log_object_is_formatted()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());
        ScriptConsoleEntry? entry = null;
        engine.ConsoleLogged += e => entry = e;

        engine.Execute("console.log({x: 1})");

        Assert.NotNull(entry);
        Assert.Equal(
            """
                {
                  "x": 1
                }
                """.Trim(),
            entry.Text
        );
    }

    [Fact]
    public void Console_log_string_is_not_quoted()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());
        ScriptConsoleEntry? entry = null;
        engine.ConsoleLogged += e => entry = e;

        engine.Execute("console.log('hello')");

        Assert.NotNull(entry);
        Assert.Equal("hello", entry.Text);
    }

    [Fact]
    public void Dollar_exception_captures_exception_from_evaluate()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        Assert.ThrowsAny<Exception>(() => engine.Evaluate("null.prop"));
        var ex = engine.Evaluate("$exception");

        Assert.NotEqual(JsValue.Undefined, ex);
        Assert.NotEqual(JsValue.Null, ex);
    }

    [Fact]
    public void Dollar_exception_captures_exception_from_execute()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        Assert.ThrowsAny<Exception>(() => engine.Execute("null.prop"));
        var ex = engine.Evaluate("$exception");

        Assert.NotEqual(JsValue.Undefined, ex);
        Assert.NotEqual(JsValue.Null, ex);
    }

    [Fact]
    public void Dollar_exception_is_cleared_after_successful_evaluate()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        Assert.ThrowsAny<Exception>(() => engine.Evaluate("null.prop"));
        engine.Evaluate("1 + 1");
        var ex = engine.Evaluate("$exception");

        Assert.Equal(JsValue.Undefined, ex);
    }

    [Fact]
    public void Dollar_exception_is_cleared_after_successful_execute()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        Assert.ThrowsAny<Exception>(() => engine.Evaluate("null.prop"));
        engine.Execute("var x = 1;");
        var ex = engine.Evaluate("$exception");

        Assert.Equal(JsValue.Undefined, ex);
    }

    [Fact]
    public void Dollar_underscore_is_cleared_after_execute()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.Evaluate("42");
        engine.Execute("var x = 1;");
        var result = engine.Evaluate("$_");

        Assert.Equal(JsValue.Undefined, result);
    }

    [Fact]
    public void Dollar_underscore_is_cleared_when_evaluate_throws()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.Evaluate("42");
        Assert.ThrowsAny<Exception>(() => engine.Evaluate("null.prop"));
        var result = engine.Evaluate("$_");

        Assert.Equal(JsValue.Undefined, result);
    }

    [Fact]
    public void Dollar_underscore_tracks_last_evaluated_value()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.Evaluate("42");
        var result = engine.Evaluate("$_");

        Assert.Equal("42", result.ToString());
    }

    [Fact]
    public void Dump_emits_console_log_entry_and_returns_value()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());
        ScriptConsoleEntry? entry = null;
        engine.ConsoleLogged += e => entry = e;

        var result = engine.Evaluate("dump(42)");

        Assert.NotNull(entry);
        Assert.Equal(ConsoleLogLevel.Log, entry.Level);
        Assert.Equal("42", entry.Text);
        Assert.Equal("42", result.ToString());
    }

    [Fact]
    public void Dump_returns_value_unchanged_enabling_expression_chaining()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());
        var entries = new List<ScriptConsoleEntry>();
        engine.ConsoleLogged += e => entries.Add(e);

        var result = engine.Evaluate("dump({x: 1}).x");

        Assert.Single(entries);
        Assert.Equal("1", result.ToString());
    }

    [Fact]
    public void Execute_and_evaluate_transpile_source_before_running_it()
    {
        var transpiler = new RecordingTranspiler(
            new Dictionary<string, string>
            {
                ["const answer: number = 40 + 2;"] = "var answer = 40 + 2;",
                ["answer"] = "answer",
            }
        );

        using var engine = new ScriptEngine(null, transpiler);

        engine.Execute("const answer: number = 40 + 2;");
        var result = engine.Evaluate("answer");

        Assert.Equal(
            ["const answer: number = 40 + 2;", "answer"],
            transpiler.Inputs
        );
        Assert.Equal("42", result.ToString());
    }

    [Fact]
    public async Task Execute_is_safe_to_call_from_multiple_threads()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());
        engine.Execute("var counter = 0;");

        await Task.WhenAll(
            Enumerable.Range(0, 200)
                .Select(_ => Task.Run(() => engine.Execute("counter += 1;")))
        );

        var result = engine.Evaluate("counter");
        Assert.Equal("200", result.ToString());
    }

    [Fact]
    public void Execute_state_persists_across_multiple_calls()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.Execute("var counter = 10;");
        engine.Execute("counter += 5;");
        var result = engine.Evaluate("counter");

        Assert.Equal("15", result.ToString());
    }

    [Fact]
    public void GetGlobalVariables_excludes_builtins_and_engine_special_variables()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.Evaluate("42"); // sets $_
        var globals = engine.GetGlobalVariables();

        var keys = globals.Keys.Select(k => k.ToString()).ToHashSet();
        Assert.DoesNotContain("$_", keys);
        Assert.DoesNotContain("$exception", keys);
        Assert.DoesNotContain("Math", keys);
        Assert.DoesNotContain("undefined", keys);
    }

    [Fact]
    public void GetGlobalVariables_is_empty_when_no_user_variables_defined()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        var globals = engine.GetGlobalVariables();

        Assert.Empty(globals);
    }

    [Fact]
    public void GetGlobalVariables_returns_user_defined_variables()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.Execute("var x = 1; var y = 2;");
        var globals = engine.GetGlobalVariables();

        var keys = globals.Keys.Select(k => k.ToString()).ToHashSet();
        Assert.Contains("x", keys);
        Assert.Contains("y", keys);
    }

    [Fact]
    public void SetValue_exposes_host_values_to_script_execution()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.SetValue("offset", 5);

        var result = engine.Evaluate("offset + 7");

        Assert.Equal("12", result.ToString());
    }
}
