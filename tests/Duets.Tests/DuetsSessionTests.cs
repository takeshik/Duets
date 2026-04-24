using Duets.Jint;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Tests;

[Collection("TranspilerAssets")]
public sealed class DuetsSessionTests
{
    public DuetsSessionTests(TranspilerAssetsFixture assets, ITestOutputHelper output)
    {
        this._assets = assets;
        this._output = output;
        this._output.WriteLine($"TypeScript {assets.TypeScriptVersion}, Babel {assets.BabelVersion}");
    }

    private readonly TranspilerAssetsFixture _assets;
    private readonly ITestOutputHelper _output;

    private sealed class DisposableTranspiler : ITranspiler,
        IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            this.IsDisposed = true;
        }

        public string Transpile(
            string input,
            string? fileName = null,
            IList<Diagnostic>? diagnostics = null,
            string? moduleName = null)
        {
            return input;
        }
    }

    [Fact]
    public async Task CreateAsync_disposes_transpiler_when_engine_construction_fails()
    {
        var transpiler = new DisposableTranspiler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DuetsSession.CreateAsync(config => config
                .UseTranspiler(_ => Task.FromResult<ITranspiler>(transpiler))
                .UseEngine(_ => throw new InvalidOperationException("boom"))
            )
        );

        Assert.True(transpiler.IsDisposed);
    }

    [Fact]
    public async Task CreateAsync_does_not_register_type_builtins_without_clr_interop()
    {
        using var session = await DuetsSession.CreateAsync(config => config
            .UseTranspiler(async declarations => await this._assets.CreateTypeScriptServiceAsync(declarations))
            .UseEngine(transpiler => JintTestRuntime.CreateEngine(transpiler: transpiler))
        );

        Assert.Equal("undefined", session.Evaluate("typeof typings").ToString());
    }

    [Fact]
    public async Task CreateAsync_passes_the_session_owned_declarations_to_the_async_factory()
    {
        TypeDeclarations? capturedDeclarations = null;

        using var session = await DuetsSession.CreateAsync(config => config
            .UseTranspiler(async declarations =>
                {
                    capturedDeclarations = declarations;
                    return await this._assets.CreateTypeScriptServiceAsync(declarations);
                }
            )
            .UseEngine(transpiler => JintTestRuntime.CreateEngine(transpiler: transpiler))
        );

        Assert.Same(capturedDeclarations, session.Declarations);
    }

    [Fact]
    public async Task CreateAsync_passes_the_session_owned_declarations_to_the_transpiler_factory()
    {
        TypeDeclarations? capturedDeclarations = null;

        using var session = await DuetsSession.CreateAsync(config => config
            .UseTranspiler(declarations =>
                {
                    capturedDeclarations = declarations;
                    return Task.FromResult<ITranspiler>(new IdentityTranspiler());
                }
            )
            .UseEngine(transpiler => JintTestRuntime.CreateEngine(transpiler: transpiler))
        );

        Assert.Same(capturedDeclarations, session.Declarations);
        Assert.Equal("42", session.Evaluate("42").ToString());
    }

    [Fact]
    public async Task CreateAsync_registers_type_builtins_when_clr_interop_is_enabled()
    {
        using var session = await DuetsSession.CreateAsync(config => config
            .UseTranspiler(async declarations => await this._assets.CreateTypeScriptServiceAsync(declarations))
            .UseJint(opts => opts.AllowClr())
        );

        var files = TypeScriptServiceTestHelpers.GetLanguageServiceFiles((TypeScriptService) session.Transpiler);
        Assert.Contains(files.Values, content => content.Contains("declare const typings:"));
        Assert.Equal("object", session.Evaluate("typeof typings").ToString());
    }

    [Fact]
    public async Task Dispose_disposes_transpiler()
    {
        var transpiler = new DisposableTranspiler();
        var session = await DuetsSession.CreateAsync(config => config
            .UseTranspiler(_ => Task.FromResult<ITranspiler>(transpiler))
            .UseEngine(engineTranspiler => JintTestRuntime.CreateEngine(transpiler: engineTranspiler))
        );

        session.Dispose();

        Assert.True(transpiler.IsDisposed);
    }

    [Fact]
    public async Task Extension_method_array_augmentations_do_not_break_array_completions()
    {
        using var session = await DuetsSession.CreateAsync(config => config
            .UseTranspiler(async declarations => await this._assets.CreateTypeScriptServiceAsync(declarations, true))
            .UseJint(opts => opts.AllowClr())
        );
        session.Execute(
            """
            typings.usingNamespace('System.Linq');
            typings.addExtensionMethods(Enumerable);
            """
        );

        var transpiler = (TypeScriptService) session.Transpiler;

        var arrayLiteralCompletions = transpiler.GetCompletions("const xs = [1,2,3]; xs.", "const xs = [1,2,3]; xs.".Length);
        Assert.Contains(arrayLiteralCompletions, entry => entry.Name == "map");
        Assert.Contains(arrayLiteralCompletions, entry => entry.Name == "Select");

        var rangeCompletions = transpiler.GetCompletions("Enumerable.Range(1, 10).", "Enumerable.Range(1, 10).".Length);
        Assert.Contains(rangeCompletions, entry => entry.Name == "map");
        Assert.Contains(rangeCompletions, entry => entry.Name == "Select");
    }
}
