namespace Duets.Tests.TestSupport;

/// <summary>A no-op transpiler that returns the input unchanged. Used across multiple test classes.</summary>
internal sealed class IdentityTranspiler : ITranspiler
{
    public string Transpile(
        string input,
        string? fileName = null,
        IList<Diagnostic>? diagnostics = null,
        string? moduleName = null)
    {
        return input;
    }
}
