using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Tests;

public sealed class DuetsSessionTests
{
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
    public async Task CreateAsync_disposes_transpilers_when_engine_construction_fails()
    {
        var transpiler = new DisposableTranspiler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DuetsSession.CreateAsync(
                _ => Task.FromResult<ITranspiler>(transpiler),
                _ => throw new InvalidOperationException("boom")
            )
        );

        Assert.True(transpiler.IsDisposed);
    }

    [Fact]
    public async Task CreateAsync_passes_the_session_owned_declarations_to_the_async_factory()
    {
        TypeDeclarations? capturedDeclarations = null;

        using var session = await DuetsSession.CreateAsync(async declarations =>
            {
                capturedDeclarations = declarations;
                return await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations);
            }
        );

        session.RegisterTypeBuiltins();

        Assert.Same(capturedDeclarations, session.Declarations);

        var files = FakeRuntimeAssets.GetLanguageServiceFiles((TypeScriptService) session.Transpiler);
        Assert.Contains(files.Values, content => content.Contains("declare const typings:"));
    }

    [Fact]
    public void Create_disposes_transpilers_when_engine_construction_fails()
    {
        var transpiler = new DisposableTranspiler();

        Assert.Throws<InvalidOperationException>(() =>
            DuetsSession.Create(
                _ => transpiler,
                _ => throw new InvalidOperationException("boom")
            )
        );

        Assert.True(transpiler.IsDisposed);
    }

    [Fact]
    public void Create_passes_the_session_owned_declarations_to_the_sync_factory()
    {
        TypeDeclarations? capturedDeclarations = null;

        using var session = DuetsSession.Create(declarations =>
            {
                capturedDeclarations = declarations;
                return new IdentityTranspiler();
            }
        );

        Assert.Same(capturedDeclarations, session.Declarations);
        Assert.Equal("42", session.Evaluate("42").ToString());
    }

    [Fact]
    public void Dispose_disposes_transpilers_created_by_the_sync_factory()
    {
        var transpiler = new DisposableTranspiler();
        var session = DuetsSession.Create(_ => transpiler);

        session.Dispose();

        Assert.True(transpiler.IsDisposed);
    }

    [Fact]
    public async Task Extension_method_array_augmentations_do_not_break_array_completions()
    {
        using var session = await DuetsSession.CreateAsync(
            async declarations => await TypeScriptService.CreateAsync(declarations, null, true),
            opts => opts.AllowClr()
        );
        session.RegisterTypeBuiltins();
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
