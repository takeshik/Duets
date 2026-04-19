using Duets.Jint;
using Duets.Tests.TestSupport;

namespace Duets.Tests;

/// <summary>
/// Integration tests for <see cref="BabelTranspiler"/> using the real Babel bundle.
/// The bundle is fetched from unpkg on first run and cached in the system temp directory.
/// Shared contract tests live in <see cref="BabelTranspilerCompatibilityTests"/>.
/// </summary>
[Collection("TranspilerAssets")]
public sealed class BabelTranspilerIntegrationTests : IAsyncLifetime
{
    public BabelTranspilerIntegrationTests(TranspilerAssetsFixture assets, ITestOutputHelper output)
    {
        this._assets = assets;
        this._output = output;
    }

    private readonly TranspilerAssetsFixture _assets;
    private readonly ITestOutputHelper _output;
    private BabelTranspiler _transpiler = null!;

    public async ValueTask InitializeAsync()
    {
        this._transpiler = await this._assets.CreateBabelTranspilerAsync();
        this._output.WriteLine($"Babel {this._transpiler.Version}");
    }

    public ValueTask DisposeAsync()
    {
        this._transpiler.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void Transpile_rethrows_exception_on_syntax_error()
    {
        Assert.ThrowsAny<Exception>(() => this._transpiler.Transpile("const x = ("));
    }
}
