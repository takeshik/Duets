namespace Duets.Completions;

/// <summary>Evaluates a runtime tagged-template invocation.</summary>
public delegate object? TemplateEvaluationCallback(TemplateInvocation invocation);

/// <summary>Produces completion candidates for a tagged-template body.</summary>
public delegate ValueTask<IReadOnlyList<TemplateCompletionItem>> TemplateCompletionCallback(
    TemplateCompletionContext context,
    CancellationToken cancellationToken
);
