namespace Duets.Tests;

public sealed class ScriptEngineTests
{
    private sealed class IdentityTranspiler : ITranspiler
    {
        public string Transpile(
            string input,
            CompilerOptions? compilerOptions = null,
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
            CompilerOptions? compilerOptions = null,
            string? fileName = null,
            IList<Diagnostic>? diagnostics = null,
            string? moduleName = null)
        {
            this.Inputs.Add(input);
            return mappings[input];
        }
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
    public void Execute_state_persists_across_multiple_calls()
    {
        using var engine = new ScriptEngine(null, new IdentityTranspiler());

        engine.Execute("var counter = 10;");
        engine.Execute("counter += 5;");
        var result = engine.Evaluate("counter");

        Assert.Equal("15", result.ToString());
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
