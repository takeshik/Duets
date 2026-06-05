using System.Reflection;
using System.Text;

namespace Duets;

/// <summary>
/// Provides binary content for a runtime asset (JavaScript file, TypeScript declaration, font, etc.).
/// </summary>
public interface IAssetSource
{
    /// <param name="force">When <see langword="true"/>, bypasses any caching layer and fetches fresh content.</param>
    public Task<byte[]> GetBytesAsync(bool force = false);
}

/// <summary>
/// Extension methods for <see cref="IAssetSource"/>.
/// </summary>
public static class AssetSourceExtensions
{
    /// <summary>
    /// Returns the asset content decoded as a string.
    /// </summary>
    /// <param name="source">The asset source to read from.</param>
    /// <param name="force">When <see langword="true"/>, bypasses any caching layer and fetches fresh content.</param>
    /// <param name="encoding">The encoding used to decode the bytes; defaults to UTF-8 when <see langword="null"/>.</param>
    public static async Task<string> GetStringAsync(
        this IAssetSource source,
        bool force = false,
        Encoding? encoding = null
    )
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return (encoding ?? Encoding.UTF8).GetString(
            await source.GetBytesAsync(force).ConfigureAwait(false)
        );
    }
}

/// <summary>
/// Factory methods for creating <see cref="IAssetSource"/> instances.
/// </summary>
public static class AssetSources
{
    private static readonly HttpClient _defaultHttpClient = new();

    /// <summary>
    /// Creates an asset source that fetches content from the given HTTP URL.
    /// Security is the caller's responsibility — only use trusted URLs.
    /// </summary>
    public static IAssetSource Http(string url, HttpClient? httpClient = null)
    {
        return new HttpAssetSource(url, httpClient ?? _defaultHttpClient);
    }

    /// <summary>
    /// Creates an asset source that fetches content from unpkg CDN.
    /// </summary>
    public static IAssetSource Unpkg(
        string package,
        string version,
        string path,
        HttpClient? httpClient = null
    )
    {
        return Http($"https://unpkg.com/{package}@{version}/{path}", httpClient);
    }

    /// <summary>
    /// Creates an asset source that reads content from an assembly manifest embedded resource.
    /// </summary>
    public static IAssetSource EmbeddedResource(Assembly assembly, string resourceName)
    {
        return new EmbeddedResourceAssetSource(assembly, resourceName);
    }

    /// <summary>
    /// Creates an asset source from an arbitrary text delegate. The produced string is encoded as UTF-8.
    /// Useful for testing or custom scenarios.
    /// </summary>
    public static IAssetSource FromString(Func<bool, Task<string>> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        return new AdHocAssetSource(async force =>
            Encoding.UTF8.GetBytes(await factory(force).ConfigureAwait(false))
        );
    }

    /// <summary>
    /// Creates an asset source from an arbitrary text delegate. The produced string is encoded as UTF-8.
    /// Useful for testing or custom scenarios.
    /// </summary>
    public static IAssetSource From(Func<bool, Task<string>> factory) => FromString(factory);

    /// <summary>
    /// Creates an asset source from an arbitrary binary delegate.
    /// Useful for testing or custom scenarios that require non-text assets.
    /// </summary>
    public static IAssetSource FromBytes(Func<bool, Task<byte[]>> factory)
    {
        return new AdHocAssetSource(factory);
    }

    /// <summary>
    /// Wraps an asset source with a disk-based cache at the given file path using a 7-day TTL.
    /// </summary>
    public static IAssetSource WithDiskCache(this IAssetSource inner, string cacheFilePath)
    {
        return new CachedAssetSource(inner, cacheFilePath, TimeSpan.FromDays(7));
    }

    /// <summary>
    /// Wraps an asset source with a disk-based cache at the given file path using the specified TTL.
    /// </summary>
    public static IAssetSource WithDiskCache(
        this IAssetSource inner,
        string cacheFilePath,
        TimeSpan ttl
    )
    {
        return new CachedAssetSource(inner, cacheFilePath, ttl);
    }

    private sealed class HttpAssetSource(string url, HttpClient client) : IAssetSource
    {
        public Task<byte[]> GetBytesAsync(bool force = false)
        {
            return client.GetByteArrayAsync(url);
        }
    }

    private sealed class EmbeddedResourceAssetSource(Assembly assembly, string resourceName)
        : IAssetSource
    {
        public async Task<byte[]> GetBytesAsync(bool force = false)
        {
            await using var stream =
                assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' not found."
                );
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
    }

    private sealed class CachedAssetSource(IAssetSource inner, string cacheFile, TimeSpan ttl)
        : IAssetSource
    {
        public async Task<byte[]> GetBytesAsync(bool force = false)
        {
            if (
                !force
                && File.Exists(cacheFile)
                && DateTime.UtcNow - File.GetCreationTimeUtc(cacheFile) < ttl
            )
            {
                return await File.ReadAllBytesAsync(cacheFile).ConfigureAwait(false);
            }

            var content = await inner.GetBytesAsync(force).ConfigureAwait(false);
            var dir = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllBytesAsync(cacheFile, content).ConfigureAwait(false);
            return content;
        }
    }

    private sealed class AdHocAssetSource(Func<bool, Task<byte[]>> factory) : IAssetSource
    {
        public Task<byte[]> GetBytesAsync(bool force = false)
        {
            return factory(force);
        }
    }
}
