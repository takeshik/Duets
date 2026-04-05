using Duets.Tests.TestSupport;

namespace Duets.Tests;

public sealed class BabelTranspilerTests : IAsyncLifetime
{
    private readonly BabelTranspiler _transpiler = FakeRuntimeAssets.CreateBabelTranspiler();

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
    public void InitializeAsync_sets_the_version_and_description()
    {
        Assert.Equal("test", this._transpiler.Version);
        Assert.Equal("Babel test", this._transpiler.Description);
    }

    [Fact]
    public void Transpile_populates_diagnostics_and_rethrows_when_babel_reports_an_error()
    {
        var diagnostics = new List<Diagnostic>();

        var exception = Assert.ThrowsAny<Exception>(() => this._transpiler.Transpile("syntaxError", diagnostics: diagnostics));

        Assert.Contains("Unexpected token", exception.Message);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(0, diagnostic.Start);
        Assert.Equal("Unexpected token", diagnostic.MessageText);
    }

    [Fact]
    public void Transpile_requires_initialization()
    {
        using var transpiler = FakeRuntimeAssets.CreateBabelTranspiler();

        var exception = Assert.Throws<InvalidOperationException>(() => transpiler.Transpile("const answer: number = 42;"));

        Assert.Contains("InitializeAsync", exception.Message);
    }

    [Fact]
    public void Transpile_returns_the_transformed_javascript_and_preserves_the_filename()
    {
        var output = this._transpiler.Transpile("const answer: number = 42;", "input.ts");

        Assert.Equal("/*input.ts*/\nconst answer = 42;", output);
    }
}
