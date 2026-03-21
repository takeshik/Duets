using System.Reflection;

namespace HttpHarker;

/// <summary>
/// Provides file bytes from assembly manifest embedded resources.
/// </summary>
public sealed class EmbeddedResourceFileProvider(Assembly assembly, string resourcePrefix) : IFileProvider
{
    private readonly string _resourcePrefix = resourcePrefix.TrimEnd('.');

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
