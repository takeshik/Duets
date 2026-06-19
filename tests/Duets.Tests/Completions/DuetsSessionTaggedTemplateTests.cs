using Duets.Completions;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Tests.Completions;

public sealed class DuetsSessionTaggedTemplateTests
{
    [Fact]
    public async Task RegisterTaggedTemplate_with_evaluate_installs_runtime_tag_and_declaration()
    {
        using var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());

        session.RegisterTaggedTemplate("path", invocation => string.Join("|", invocation.Raw));

        Assert.Equal("a/b", session.Evaluate("path`a/b`").ToString());
        Assert.Contains(
            session.Declarations.GetDeclarations(),
            declaration => declaration.Content.Contains("declare function path")
        );
    }

    [Fact]
    public async Task RegisterTaggedTemplate_with_complete_only_installs_no_runtime_tag_or_declaration()
    {
        using var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());

        session.RegisterTaggedTemplate(
            "asset",
            complete: (_, _) =>
                new ValueTask<IReadOnlyList<TemplateCompletionItem>>([
                    new TemplateCompletionItem("asset"),
                ])
        );

        Assert.Equal("undefined", session.Evaluate("typeof asset").ToString());
        Assert.DoesNotContain(
            session.Declarations.GetDeclarations(),
            declaration => declaration.Content.Contains("declare function asset")
        );
        Assert.True(session.TaggedTemplates.TryGet("asset", out _));
    }

    [Fact]
    public async Task RegisterTaggedTemplate_omitted_paths_remove_previous_registration()
    {
        using var session = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());

        session.RegisterTaggedTemplate(
            "path",
            invocation => string.Join("", invocation.Raw),
            (_, _) =>
                new ValueTask<IReadOnlyList<TemplateCompletionItem>>([
                    new TemplateCompletionItem("path"),
                ])
        );
        session.RegisterTaggedTemplate("path");

        Assert.Equal("undefined", session.Evaluate("typeof path").ToString());
        Assert.False(session.TaggedTemplates.TryGet("path", out _));
    }
}
