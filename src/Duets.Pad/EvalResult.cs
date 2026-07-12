namespace Duets.Pad;

/// <summary>
/// The outcome of a single <see cref="DuetsPadSession.EvaluateAsync"/> call.
/// </summary>
internal sealed record EvalResult(bool Ok, string? Result, string? Error);
