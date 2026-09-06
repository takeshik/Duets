namespace Duets;

/// <summary>
/// Minimal transpiler abstraction: converts TypeScript source to JavaScript.
/// Implementations include <c>BabelTranspiler</c> and <c>TypeScriptService</c> from runtime
/// integration packages.
/// </summary>
public interface ITranspiler
{
    /// <summary>
    /// Human-readable description of this transpiler, including name and version where available.
    /// Defaults to the implementation type name.
    /// </summary>
    public string Description => this.GetType().Name;

    /// <summary>Transpiles TypeScript source to JavaScript.</summary>
    /// <param name="input">The TypeScript source.</param>
    /// <param name="fileName">An optional source file name used in diagnostics.</param>
    /// <param name="diagnostics">An optional collection that receives compiler diagnostics.</param>
    /// <param name="moduleName">An optional module name supplied to the transpiler.</param>
    /// <returns>The generated JavaScript source.</returns>
    public string Transpile(
        string input,
        string? fileName = null,
        IList<Diagnostic>? diagnostics = null,
        string? moduleName = null
    );
}

/// <summary>A diagnostic emitted by the TypeScript compiler during transpilation.</summary>
public record Diagnostic(int Start, int Length, string MessageText, int Category, int Code)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"({this.Start},{this.Length}) TS{this.Code}: {this.MessageText}";
    }
}
