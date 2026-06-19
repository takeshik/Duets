namespace Duets.Completions;

/// <summary>Runtime invocation data passed to a registered tagged-template evaluator.</summary>
public sealed record TemplateInvocation(
    IReadOnlyList<string> Strings,
    IReadOnlyList<string> Raw,
    IReadOnlyList<object?> Values
);
