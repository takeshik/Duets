namespace Duets;

/// <summary>The severity level of a <see cref="ScriptConsoleEntry"/>.</summary>
public enum ConsoleLogLevel
{
    Log,
    Info,
    Warn,
    Error,
    Debug,
}

/// <summary>A single entry emitted by <c>console.log</c> (or <c>warn</c>, <c>error</c>, etc.) during script execution.</summary>
/// <param name="Level">The severity level corresponding to the console method called.</param>
/// <param name="Text">Space-joined string representation of all arguments passed to the console method.</param>
public record ScriptConsoleEntry(ConsoleLogLevel Level, string Text);
