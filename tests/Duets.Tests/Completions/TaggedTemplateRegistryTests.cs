using Duets.Completions;

namespace Duets.Tests.Completions;

public sealed class TaggedTemplateRegistryTests
{
    [Fact]
    public async Task Register_uses_last_registration_for_duplicate_tag()
    {
        var registry = new TaggedTemplateRegistry();

        registry.Register(
            "path",
            (_, _) => new ValueTask<IReadOnlyList<TemplateCompletionItem>>([])
        );
        registry.Register(
            "path",
            (_, _) =>
                new ValueTask<IReadOnlyList<TemplateCompletionItem>>([
                    new TemplateCompletionItem("second"),
                ])
        );

        Assert.True(registry.TryGet("path", out var registration));
        var items = await registration
            .Complete(
                new TemplateCompletionContext("path", "", "", "", 0, 0, [""]),
                CancellationToken.None
            )
            .AsTask();
        Assert.Equal("second", Assert.Single(items).Label);
    }

    [Theory]
    [InlineData("path", true)]
    [InlineData("_path1", true)]
    [InlineData("", false)]
    [InlineData("1path", false)]
    [InlineData("path-name", false)]
    [InlineData("$path", false)]
    public void IsValidTag_accepts_only_simple_ascii_identifiers(string tag, bool expected)
    {
        Assert.Equal(expected, TaggedTemplateRegistry.IsValidTag(tag));
    }

    [Fact]
    public void IsSpanWithinSegment_uses_overflow_safe_bounds_check()
    {
        Assert.False(
            TaggedTemplateRegistry.IsSpanWithinSegment(new TextSpan(int.MaxValue, int.MaxValue), 10)
        );
    }

    [Fact]
    public void TextSpan_End_uses_overflow_safe_sum()
    {
        Assert.Equal(
            (long)int.MaxValue + int.MaxValue,
            new TextSpan(int.MaxValue, int.MaxValue).End
        );
    }

    [Fact]
    public void Cap_limits_items_to_default_count()
    {
        var items = Enumerable
            .Range(0, TaggedTemplateRegistry.DefaultMaxItems + 1)
            .Select(i => new TemplateCompletionItem(i.ToString()))
            .ToArray();

        Assert.Equal(
            TaggedTemplateRegistry.DefaultMaxItems,
            TaggedTemplateRegistry.Cap(items).Count
        );
    }
}
