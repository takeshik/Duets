using Duets.Tests.TestSupport;
using Duets.Tests.TestTypes.NamespaceTargets;

namespace Duets.Tests;

public sealed class TypeScriptServiceTests
{
    [Theory]
    [InlineData("nullResult.", 11)]
    [InlineData("noEntries.", 10)]
    public async Task GetCompletions_returns_an_empty_list_when_the_language_service_has_no_entries(string source, int position)
    {
        var declarations = new TypeDeclarations();
        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations, true);

        var completions = service.GetCompletions(source, position);

        Assert.Empty(completions);
    }

    [Fact]
    public async Task CreateAsync_can_inject_the_standard_library_during_initialization()
    {
        var declarations = new TypeDeclarations();
        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations, true);

        var files = FakeRuntimeAssets.GetLanguageServiceFiles(service);

        Assert.Contains("lib.es5.d.ts", files.Keys);
    }

    [Fact]
    public async Task Declaration_changes_after_initialization_are_mirrored_into_the_language_service()
    {
        var declarations = new TypeDeclarations();
        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations);

        declarations.RegisterDeclaration("declare const answer: number;");

        var files = FakeRuntimeAssets.GetLanguageServiceFiles(service);

        Assert.Contains(files.Values, content => content.Contains("declare const answer: number;"));
    }

    [Fact]
    public async Task GetCompletions_returns_entries_from_the_language_service()
    {
        var declarations = new TypeDeclarations();
        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations, true);

        var completions = service.GetCompletions("Math.", 5);

        var entry = Assert.Single(completions);
        Assert.Equal("abs", entry.Name);
        Assert.Equal("method", entry.Kind);
        Assert.Equal("0", entry.SortText);
    }

    [Fact]
    public async Task InjectStdLibAsync_adds_the_standard_library_file()
    {
        var declarations = new TypeDeclarations();
        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations);

        await service.InjectStdLibAsync();

        var files = FakeRuntimeAssets.GetLanguageServiceFiles(service);
        Assert.Contains("lib.es5.d.ts", files.Keys);
        Assert.Contains("interface Math", files["lib.es5.d.ts"]);
    }

    [Fact]
    public async Task ResetAsync_initializes_the_runtime_and_replays_existing_declarations()
    {
        var declarations = new TypeDeclarations();
        declarations.RegisterType(typeof(NamespaceAlpha));
        declarations.RegisterDeclaration("declare const answer: number;");

        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations);

        var files = FakeRuntimeAssets.GetLanguageServiceFiles(service);

        Assert.Equal("0.test", service.Version);
        Assert.Equal("TypeScript 0.test", service.Description);
        Assert.Contains(files.Values, content => content.Contains("class NamespaceAlpha"));
        Assert.Contains(files.Values, content => content.Contains("declare const answer: number;"));
    }

    [Fact]
    public async Task Transpile_populates_diagnostics_reported_by_the_runtime()
    {
        var declarations = new TypeDeclarations();
        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations);
        var diagnostics = new List<Diagnostic>();

        var output = service.Transpile("syntaxError", diagnostics: diagnostics);

        Assert.Equal("/*|*/\nsyntaxError", output);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(0, diagnostic.Start);
        Assert.Equal(11, diagnostic.Length);
        Assert.Equal("Unexpected token", diagnostic.MessageText);
        Assert.Equal(1001, diagnostic.Code);
    }

    [Fact]
    public async Task Transpile_returns_the_transformed_source_and_preserves_metadata_arguments()
    {
        var declarations = new TypeDeclarations();
        using var service = await FakeRuntimeAssets.CreateInitializedTypeScriptServiceAsync(declarations);

        var output = service.Transpile("const answer: number = 42;", "input.ts", moduleName: "main");

        Assert.Equal("/*input.ts|main*/\nconst answer = 42;", output);
    }
}
