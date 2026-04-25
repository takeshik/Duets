using System.IO.Compression;
using System.Net;
using System.Reflection;
using Duets.Tests.TestTypes.Declarations;

namespace Duets.Tests;

public sealed class XmlDocumentationProviderTests
{
    private sealed class FakeHttpHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body),
                }
            );
        }
    }

    private static byte[] BuildNupkg(params (string path, string xml)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (path, xml) in entries)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(xml);
            }
        }

        return ms.ToArray();
    }

    private static string MemberXml(string id, string summary)
    {
        return $"""<member name="{id}"><summary>{summary}</summary></member>""";
    }

    private static string WrapMembers(string members)
    {
        return $"""
            <?xml version="1.0"?>
            <doc><assembly><name>Test</name></assembly><members>{members}</members></doc>
            """;
    }

    private static XmlDocumentationProvider Build(string membersXml)
    {
        return new XmlDocumentationProvider(
            $"""
            <?xml version="1.0"?>
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
            {membersXml}
              </members>
            </doc>
            """
        );
    }

    [Fact]
    public void Ctor_tolerates_malformed_xml()
    {
        var provider = new XmlDocumentationProvider("<broken");

        Assert.Null(provider.Get(typeof(string)));
    }

    // ── FetchFromNuGetAsync: TFM selection ───────────────────────────────────

    [Fact]
    public async Task FetchFromNuGetAsync_prefers_runtime_tfm_over_netstandard()
    {
        var runtimeTfm = $"net{Environment.Version.Major}.{Environment.Version.Minor}";
        var nupkg = BuildNupkg(
            ("lib/netstandard2.1/TestLib.xml",
                WrapMembers(MemberXml("T:System.String", "netstandard docs"))),
            ($"lib/{runtimeTfm}/TestLib.xml",
                WrapMembers(MemberXml("T:System.String", "runtime docs")))
        );

        var cacheDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using var client = new HttpClient(new FakeHttpHandler(nupkg));
            var provider = await XmlDocumentationProvider.FetchFromNuGetAsync(
                "TestLib",
                "1.0.0",
                cacheDirectory: cacheDir,
                httpClient: client
            );

            Assert.Equal("runtime docs", provider!.Get(typeof(string)));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
        }
    }

    [Fact]
    public async Task FetchFromNuGetAsync_selects_xml_matching_assemblyName_in_multiassembly_package()
    {
        var nupkg = BuildNupkg(
            ("lib/net8.0/AssemblyA.xml",
                WrapMembers(MemberXml("T:System.String", "from AssemblyA"))),
            ("lib/net8.0/AssemblyB.xml",
                WrapMembers(MemberXml("T:System.String", "from AssemblyB")))
        );

        var cacheDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using var client = new HttpClient(new FakeHttpHandler(nupkg));
            var provider = await XmlDocumentationProvider.FetchFromNuGetAsync(
                "MultiPkg",
                "1.0.0",
                "net8.0",
                cacheDir,
                client,
                "AssemblyB"
            );

            Assert.Equal("from AssemblyB", provider!.Get(typeof(string)));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
        }
    }

    // ── @param / @returns ────────────────────────────────────────────────────

    [Fact]
    public void Get_includes_param_and_returns_annotations()
    {
        var provider = Build(
            """
                <member name="M:System.String.Contains(System.String)">
                  <summary>Returns a value indicating whether the string occurs.</summary>
                  <param name="value">The string to seek.</param>
                  <returns>true if found; otherwise, false.</returns>
                </member>
            """
        );

        var method = typeof(string).GetMethod("Contains", [typeof(string)])!;
        var result = provider.Get(method);

        Assert.NotNull(result);
        Assert.Contains("Returns a value indicating whether the string occurs.", result);
        Assert.Contains("@param value The string to seek.", result);
        Assert.Contains("@returns true if found; otherwise, false.", result);
    }

    [Fact]
    public void Get_includes_remarks_after_summary()
    {
        var provider = Build(
            """
                <member name="T:System.String">
                  <summary>Represents text.</summary>
                  <remarks>Immutable in .NET.</remarks>
                </member>
            """
        );

        var result = provider.Get(typeof(string));

        Assert.NotNull(result);
        Assert.Contains("Represents text.", result);
        Assert.Contains("Immutable in .NET.", result);
    }

    // ── whitespace normalisation ─────────────────────────────────────────────

    [Fact]
    public void Get_normalizes_indented_summary_to_single_line()
    {
        var provider = Build(
            """
                <member name="T:System.String">
                  <summary>
                    Represents text as a
                    sequence of Unicode characters.
                  </summary>
                </member>
            """
        );

        Assert.Equal(
            "Represents text as a sequence of Unicode characters.",
            provider.Get(typeof(string))
        );
    }

    [Fact]
    public void Get_renders_paramref_as_plain_name()
    {
        var provider = Build(
            """
                <member name="M:System.String.Contains(System.String)">
                  <summary>Returns true if <paramref name="value"/> is found.</summary>
                  <param name="value">The string to seek.</param>
                </member>
            """
        );

        var method = typeof(string).GetMethod("Contains", [typeof(string)])!;
        var result = provider.Get(method);

        Assert.NotNull(result);
        Assert.Contains("value", result);
    }

    // ── inline XML elements ───────────────────────────────────────────────────

    [Fact]
    public void Get_resolves_see_cref_to_simple_type_name()
    {
        var provider = Build(
            """
                <member name="T:System.String">
                  <summary>See also <see cref="T:System.Text.StringBuilder"/>.</summary>
                </member>
            """
        );

        var result = provider.Get(typeof(string));

        Assert.NotNull(result);
        Assert.Contains("StringBuilder", result);
        Assert.DoesNotContain("T:System.Text", result);
    }

    [Fact]
    public void Get_returns_null_for_member_with_no_recognized_elements()
    {
        var provider = Build(
            """    <member name="T:System.String"></member>"""
        );

        Assert.Null(provider.Get(typeof(string)));
    }

    // ── null / fallback ───────────────────────────────────────────────────────

    [Fact]
    public void Get_returns_null_for_unknown_member()
    {
        var provider = Build("");

        Assert.Null(provider.Get(typeof(string).GetProperty("Length")!));
    }

    [Fact]
    public void Get_returns_summary_for_constructor()
    {
        var provider = Build(
            """    <member name="M:Duets.Tests.TestTypes.Declarations.ConstructorSample.#ctor(System.String,System.Int32)"><summary>Creates instance.</summary></member>"""
        );

        var ctor = typeof(ConstructorSample).GetConstructor([typeof(string), typeof(int)])!;
        Assert.Equal("Creates instance.", provider.Get(ctor));
    }

    [Fact]
    public void Get_returns_summary_for_field()
    {
        var provider = Build(
            """    <member name="F:Duets.Tests.TestTypes.Declarations.DeclarationSample.GlobalCount"><summary>Global counter.</summary></member>"""
        );

        var field = typeof(DeclarationSample).GetField("GlobalCount", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal("Global counter.", provider.Get(field));
    }

    [Fact]
    public void Get_returns_summary_for_property()
    {
        var provider = Build(
            """    <member name="P:System.String.Length"><summary>Number of chars.</summary></member>"""
        );

        Assert.Equal("Number of chars.", provider.Get(typeof(string).GetProperty("Length")!));
    }

    // ── summary ──────────────────────────────────────────────────────────────

    [Fact]
    public void Get_returns_summary_for_type()
    {
        var provider = Build(
            """    <member name="T:System.String"><summary>Represents text.</summary></member>"""
        );

        Assert.Equal("Represents text.", provider.Get(typeof(string)));
    }
}
