using System.Reflection;
using System.Text;
using Mio;
using Mio.Destructive;

namespace Duets;

/// <summary>
/// Provides text content for a runtime asset (JavaScript file, TypeScript declaration, etc.).
/// </summary>
public interface IAssetSource
{
    /// <param name="force">When <see langword="true"/>, bypasses any caching layer and fetches fresh content.</param>
    Task<string> GetAsync(bool force = false);
}

/// <summary>
/// Factory methods for creating <see cref="IAssetSource"/> instances.
/// </summary>
public static class AssetSources
{
    private static readonly HttpClient DefaultHttpClient = new();

    /// <summary>
    /// Creates an asset source that fetches content from the given HTTP URL.
    /// Security is the caller's responsibility — only use trusted URLs.
    /// </summary>
    public static IAssetSource Http(string url, HttpClient? httpClient = null)
    {
        return new HttpAssetSource(url, httpClient ?? DefaultHttpClient);
    }

    /// <summary>
    /// Creates an asset source that fetches content from unpkg CDN.
    /// </summary>
    public static IAssetSource Unpkg(
        string package, string version, string path,
        HttpClient? httpClient = null)
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
    /// Creates an asset source from an arbitrary delegate. Useful for testing or custom scenarios.
    /// </summary>
    public static IAssetSource From(Func<bool, Task<string>> factory)
    {
        return new AdHocAssetSource(factory);
    }

    /// <summary>
    /// Wraps an asset source with a disk-based cache at the given file path using a 7-day TTL.
    /// </summary>
    public static IAssetSource WithDiskCache(this IAssetSource inner, FilePath cacheFilePath)
    {
        return new CachedAssetSource(inner, cacheFilePath, TimeSpan.FromDays(7));
    }

    /// <summary>
    /// Wraps an asset source with a disk-based cache at the given file path using the specified TTL.
    /// </summary>
    public static IAssetSource WithDiskCache(
        this IAssetSource inner, FilePath cacheFilePath,
        TimeSpan ttl)
    {
        return new CachedAssetSource(inner, cacheFilePath, ttl);
    }

    private sealed class HttpAssetSource(string url, HttpClient client) : IAssetSource
    {
        public Task<string> GetAsync(bool force = false)
        {
            return client.GetStringAsync(url);
        }
    }

    private sealed class EmbeddedResourceAssetSource(Assembly assembly, string resourceName) : IAssetSource
    {
        public async Task<string> GetAsync(bool force = false)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }

    private sealed class CachedAssetSource(IAssetSource inner, FilePath cacheFile, TimeSpan ttl) : IAssetSource
    {
        public async Task<string> GetAsync(bool force = false)
        {
            if (!force
                && cacheFile.Exists()
                && DateTimeOffset.Now - cacheFile.GetCreationTime() < ttl)
            {
                return await cacheFile.ReadAllTextAsync();
            }

            var content = await inner.GetAsync(force);
            await cacheFile.AsDestructive().WriteAsync(content);
            return content;
        }
    }

    private sealed class AdHocAssetSource(Func<bool, Task<string>> factory) : IAssetSource
    {
        public Task<string> GetAsync(bool force = false)
        {
            return factory(force);
        }
    }
}
