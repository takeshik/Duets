using System.Reflection;

namespace HttpHarker;

/// <summary>
/// Provides file bytes from assembly manifest embedded resources.
/// </summary>
public sealed class EmbeddedResourceFileProvider(Assembly assembly, string resourcePrefix)
    : IFileProvider
{
    private readonly string _resourcePrefix = resourcePrefix.TrimEnd('.');

    /// <summary>
    /// Returns the bytes of the embedded resource corresponding to <paramref name="relativePath"/>,
    /// or <c>null</c> if no matching manifest resource exists.
    /// </summary>
    /// <param name="relativePath">
    /// A forward-slash-delimited path relative to the resource prefix (e.g. <c>"assets/app.js"</c>);
    /// path separators are converted to dots for the manifest resource name lookup.
    /// </param>
    /// <returns>The resource bytes, or <c>null</c> if not found.</returns>
    public byte[]? GetFileContent(string relativePath)
    {
        var resourceName = $"{this._resourcePrefix}.{relativePath.Replace('/', '.')}";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
