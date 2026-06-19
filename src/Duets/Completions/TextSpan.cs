namespace Duets.Completions;

/// <summary>A UTF-16 text span relative to a template segment.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>The exclusive end offset.</summary>
    public long End => (long)this.Start + this.Length;
}
