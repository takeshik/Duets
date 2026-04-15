using System.Reflection;
using Duets.Okojo;
using Duets.Tests.TestSupport;
using Okojo.Reflection;

namespace Duets.Tests;

/// <summary>
/// Compatibility tests for the Okojo JavaScript backend.
/// Tests that exercise the real JS bundles (BabelTranspiler, TypeScriptService) are expected
/// to fail against Okojo 0.1.1-preview.1 due to runtime bugs in the Okojo engine:
/// - BabelTranspiler: "for-in enumerator is invalid" during Babel bundle initialization
/// - TypeScriptService: "Index was outside the bounds of the array" in Map constructor
/// builtin during TypeScript bundle initialization
/// These failing tests are intentionally left in place to document the incompatibility.
/// </summary>
public sealed class OkojoCompatibilityTests
{
    // --- Tests expected to fail: Okojo runtime cannot load real JS bundles ---

    [Fact]
    public async Task BabelTranspiler_can_initialize_with_real_bundle()
    {
        // Fails: Okojo throws "for-in enumerator is invalid" during Babel bundle execution.
        using var transpiler = await BabelTranspiler.CreateAsync();

        Assert.NotNull(transpiler.Version);
    }

    [Fact]
    public async Task BabelTranspiler_output_is_executable_by_okojo()
    {
        // Fails: BabelTranspiler cannot initialize (see above).
        using var transpiler = await BabelTranspiler.CreateAsync();
        using var engine = OkojoTestRuntime.CreateEngine(transpiler: transpiler);

        engine.Execute("const answer: number = 40 + 2;");
        var result = engine.Evaluate("answer");

        Assert.Equal("42", result.ToString());
    }
    // --- Tests expected to pass: CLR interop and basic JS execution ---

    [Fact]
    public void Console_log_multiple_args_are_space_joined()
    {
        using var engine = OkojoTestRuntime.CreateEngine();
        ScriptConsoleEntry? entry = null;
        engine.ConsoleLogged += e => entry = e;

        engine.Execute("console.log('result:', 42)");

        Assert.NotNull(entry);
        Assert.Equal("result: 42", entry.Text);
    }

    [Fact]
    public void Dollar_exception_captures_the_last_failure()
    {
        using var engine = OkojoTestRuntime.CreateEngine();

        var thrown = Assert.ThrowsAny<Exception>(() => engine.Evaluate("null.prop"));
        var captured = engine.Evaluate("$exception");
        var capturedObject = Assert.IsAssignableFrom<Exception>(captured.ToObject());

        Assert.False(captured.IsUndefined());
        Assert.False(captured.IsNull());
        Assert.Equal(thrown.GetType(), capturedObject.GetType());
    }

    [Fact]
    public void Dollar_underscore_is_cleared_after_a_failed_evaluation()
    {
        using var engine = OkojoTestRuntime.CreateEngine();

        Assert.ThrowsAny<Exception>(() => engine.Evaluate("null.prop"));
        var afterFailure = engine.Evaluate("$_");

        Assert.True(afterFailure.IsUndefined());
    }

    [Fact]
    public void Get_global_variables_returns_user_defined_variables()
    {
        using var engine = OkojoTestRuntime.CreateEngine();

        engine.Execute("var x = 1; var y = 2;");
        var globals = engine.GetGlobalVariables();

        var keys = globals.Keys.Select(x => x.ToString()).ToHashSet();
        Assert.Contains("x", keys);
        Assert.Contains("y", keys);
        Assert.DoesNotContain("$_", keys);
        Assert.DoesNotContain("$exception", keys);
    }
    // --- Tests expected to pass: session integration with IdentityTranspiler ---

    [Fact]
    public async Task Session_RegisterTypeBuiltins_and_usingNamespace_work_without_transpiler()
    {
        using var session = await DuetsSession.CreateAsync(
            _ => Task.FromResult<ITranspiler>(new IdentityTranspiler()),
            configuration => configuration.UseOkojo(builder => builder.AllowClrAccess(
                    Assembly.GetExecutingAssembly(),
                    typeof(ScriptEngine).Assembly,
                    typeof(OkojoScriptEngine).Assembly,
                    typeof(Assembly).Assembly
                )
            )
        );

        session.Execute("typings.usingNamespace('System.IO')");
        var result = session.Evaluate("typeof File");

        Assert.Equal("function", result.ToString());
        Assert.Contains(
            session.Declarations.GetDeclarations().Select(x => x.Content),
            content => content.Contains("declare var File:")
        );
    }

    [Fact]
    public async Task Session_can_register_type_builtins_and_using_namespace()
    {
        // Fails: TypeScriptService cannot initialize (see above).
        using var session = await DuetsSession.CreateAsync(
            async declarations => await TypeScriptService.CreateAsync(declarations),
            configuration => configuration.UseOkojo(builder => builder.AllowClrAccess(
                    Assembly.GetExecutingAssembly(),
                    typeof(ScriptEngine).Assembly,
                    typeof(OkojoScriptEngine).Assembly,
                    typeof(Assembly).Assembly
                )
            )
        );

        session.Execute("typings.usingNamespace('System.IO')");
        var result = session.Evaluate("typeof File");

        Assert.Equal("function", result.ToString());
        Assert.Contains(
            session.Declarations.GetDeclarations().Select(x => x.Content),
            content => content.Contains("declare var File:")
        );
    }

    [Fact]
    public async Task TypeScriptService_can_initialize_with_real_bundle()
    {
        // Fails: Okojo throws "Index was outside the bounds of the array" in Map constructor
        // builtin during TypeScript bundle initialization.
        var declarations = new TypeDeclarations();
        using var service = await TypeScriptService.CreateAsync(declarations);

        Assert.NotNull(service.Version);
    }

    [Fact]
    public async Task TypeScriptService_can_transpile_typescript()
    {
        // Fails: TypeScriptService cannot initialize (see above).
        var declarations = new TypeDeclarations();
        using var service = await TypeScriptService.CreateAsync(declarations);

        var result = service.Transpile("const x: number = 42;");

        Assert.DoesNotContain(": number", result);
        Assert.Contains("42", result);
    }
}
