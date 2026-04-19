using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.NamespaceTargets;

namespace Duets.Tests;

[Collection("TranspilerAssets")]
public sealed class TypeScriptServiceTests
{
    public TypeScriptServiceTests(TranspilerAssetsFixture assets, ITestOutputHelper output)
    {
        this._assets = assets;
        this._output = output;
    }

    private readonly TranspilerAssetsFixture _assets;
    private readonly ITestOutputHelper _output;

    [Fact]
    public async Task CreateAsync_can_inject_the_standard_library_during_initialization()
    {
        var declarations = new TypeDeclarations();
        using var service = await this._assets.CreateTypeScriptServiceAsync(declarations, true);
        this._output.WriteLine($"TypeScript {service.Version}");

        var files = TypeScriptServiceTestHelpers.GetLanguageServiceFiles(service);

        Assert.Contains("lib.es5.d.ts", files.Keys);
    }

    [Fact]
    public async Task Declaration_changes_after_initialization_are_mirrored_into_the_language_service()
    {
        var declarations = new TypeDeclarations();
        using var service = await this._assets.CreateTypeScriptServiceAsync(declarations);
        this._output.WriteLine($"TypeScript {service.Version}");

        declarations.RegisterDeclaration("declare const answer: number;");

        var files = TypeScriptServiceTestHelpers.GetLanguageServiceFiles(service);

        Assert.Contains(files.Values, content => content.Contains("declare const answer: number;"));
    }

    [Fact]
    public async Task GetCompletions_returns_entries_from_the_language_service()
    {
        var declarations = new TypeDeclarations();
        using var service = await this._assets.CreateTypeScriptServiceAsync(declarations, true);
        this._output.WriteLine($"TypeScript {service.Version}");

        var completions = service.GetCompletions("Math.", 5);

        Assert.NotEmpty(completions);
        Assert.Contains(completions, entry => entry.Name == "abs");
    }

    [Fact]
    public async Task InjectStdLibAsync_adds_the_standard_library_file()
    {
        var declarations = new TypeDeclarations();
        using var service = await this._assets.CreateTypeScriptServiceAsync(declarations);
        this._output.WriteLine($"TypeScript {service.Version}");

        await service.InjectStdLibAsync();

        var files = TypeScriptServiceTestHelpers.GetLanguageServiceFiles(service);
        Assert.Contains("lib.es5.d.ts", files.Keys);
        Assert.Contains("interface Math", files["lib.es5.d.ts"]);
    }

    [Fact]
    public async Task ResetAsync_initializes_the_runtime_and_replays_existing_declarations()
    {
        var declarations = new TypeDeclarations();
        declarations.RegisterType(typeof(NamespaceAlpha));
        declarations.RegisterDeclaration("declare const answer: number;");

        using var service = await this._assets.CreateTypeScriptServiceAsync(declarations);
        this._output.WriteLine($"TypeScript {service.Version}");

        var files = TypeScriptServiceTestHelpers.GetLanguageServiceFiles(service);

        Assert.NotNull(service.Version);
        Assert.Equal($"TypeScript {service.Version}", service.Description);
        Assert.Contains(files.Values, content => content.Contains("class NamespaceAlpha"));
        Assert.Contains(files.Values, content => content.Contains("declare const answer: number;"));
    }

    [Fact]
    public async Task Transpile_populates_diagnostics_with_location_and_error_code()
    {
        var declarations = new TypeDeclarations();
        using var service = await this._assets.CreateTypeScriptServiceAsync(declarations);
        this._output.WriteLine($"TypeScript {service.Version}");
        var diagnostics = new List<Diagnostic>();

        service.Transpile("const x = (", diagnostics: diagnostics);

        Assert.NotEmpty(diagnostics);
        var diagnostic = diagnostics[0];
        Assert.True(diagnostic.Start >= 0);
        Assert.True(diagnostic.Category > 0);
        Assert.True(diagnostic.Code > 0);
    }
}
