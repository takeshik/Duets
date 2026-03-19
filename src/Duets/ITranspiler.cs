namespace Duets;

/// <summary>
/// Minimal transpiler abstraction: converts TypeScript source to JavaScript.
/// Implemented by <see cref="TypeScriptService"/>; also implementable by stubs for testing.
/// </summary>
public interface ITranspiler
{
    /// <summary>
    /// Human-readable description of this transpiler, including name and version where available.
    /// Defaults to the implementation type name.
    /// </summary>
    string Description => GetType().Name;

    string Transpile(
        string input,
        CompilerOptions? compilerOptions = null,
        string? fileName = null,
        IList<Diagnostic>? diagnostics = null,
        string? moduleName = null);
}

/// <summary>TypeScript compiler options passed to ts.transpile.</summary>
public record CompilerOptions(
    // ReSharper disable InconsistentNaming
    string? module = null,
    string? target = null,
    string? jsx = null,
    bool? allowJs = null,
    int? maxNodeModulesJsDepth = null,
    bool? strictNullChecks = null,
    bool? sourceMap = null,
    bool? allowSyntheticDefaultImports = null,
    bool? allowNonTsExtensions = null,
    bool? resolveJsonModule = null
    // ReSharper restore InconsistentNaming
);

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
