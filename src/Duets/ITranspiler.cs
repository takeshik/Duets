namespace Duets;

/// <summary>
/// Minimal transpiler abstraction: converts TypeScript source to JavaScript.
/// Implemented by <see cref="BabelTranspiler"/> and <see cref="TypeScriptService"/>.
/// </summary>
public interface ITranspiler
{
    /// <summary>
    /// Human-readable description of this transpiler, including name and version where available.
    /// Defaults to the implementation type name.
    /// </summary>
    string Description => this.GetType().Name;

    string Transpile(
        string input,
        string? fileName = null,
        IList<Diagnostic>? diagnostics = null,
        string? moduleName = null);
}

/// <summary>A diagnostic emitted by the TypeScript compiler during transpilation.</summary>
public record Diagnostic(
    int Start,
    int Length,
    string MessageText,
    int Category,
    int Code)
{
    public override string ToString()
    {
        return $"({this.Start},{this.Length}) TS{this.Code}: {this.MessageText}";
    }
}
