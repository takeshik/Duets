using System.Reflection;

namespace Duets;

/// <summary>
/// Provides JSDoc body text for .NET members, used to populate TypeScript declaration comments.
/// </summary>
public interface IJsDocProvider
{
    /// <summary>
    /// Returns the JSDoc body text for the specified member, or <c>null</c> if not available.
    /// The returned text must not include <c>/**</c> or <c>*/</c> delimiters — only the inner content.
    /// Multiple lines are separated by <c>\n</c>.
    /// </summary>
    string? Get(MemberInfo member);
}
