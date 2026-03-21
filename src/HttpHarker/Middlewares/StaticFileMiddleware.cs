using System.Collections.Concurrent;
using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Serves files from an <see cref="IFileProvider"/> as static HTTP responses,
/// with ETag caching, Cache-Control, SPA fallback, and HEAD support.
/// </summary>
public sealed class StaticFileMiddleware(
    IFileProvider fileProvider,
    string root = "/",
    EmbeddedResourceOptions? options = null) : IMiddleware
{
    public StaticFileMiddleware(IFileProvider fileProvider, EmbeddedResourceOptions options)
        : this(fileProvider, "/", options)
    {
    }

    private readonly string _prefix = root.TrimEnd('/');
    private readonly EmbeddedResourceOptions _options = options ?? new EmbeddedResourceOptions();
    private readonly ConcurrentDictionary<string, CachedResource> _cache = new(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        if (string.IsNullOrWhiteSpace(this._options.DefaultDocument))
        {
            throw new InvalidOperationException("DefaultDocument must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(this._options.SpaFallbackDocument))
        {
            throw new InvalidOperationException("SpaFallbackDocument must not be empty.");
        }

        var rawPath = context.Request.Url?.AbsolutePath ?? "/";
        var relativePath = GetRelativePath(rawPath, this._prefix);
        if (relativePath is null)
        {
            await next();
            return;
        }

        var requestedSuffix = ToResourceSuffix(relativePath, this._options.DefaultDocument);
        var resource = this.GetOrCache(requestedSuffix);

        if (resource is null
            && this._options.EnableSpaFallback
            && this._options.SpaFallbackPredicate(context.Request))
        {
            var fallbackSuffix = NormalizeResourceSuffix(this._options.SpaFallbackDocument);
            resource = this.GetOrCache(fallbackSuffix);
        }

        if (resource is null)
        {
            await next();
            return;
        }

        var response = context.Response;
        response.ContentType = this._options.ContentTypeProvider.Resolve(
            Path.GetExtension(resource.ResourceSuffix),
            context.Request
        );

        if (this._options.CacheControlSelector is { } cacheControlSelector)
        {
            var cacheControl = cacheControlSelector(resource.ResourceSuffix);
            if (!string.IsNullOrWhiteSpace(cacheControl))
            {
                response.Headers["Cache-Control"] = cacheControl;
            }
        }

        if (resource.ETag is not null)
        {
            response.Headers["ETag"] = resource.ETag;
            var requestEtag = context.Request.Headers["If-None-Match"];
            if (requestEtag is { Length: > 0 } && IsEtagMatch(requestEtag, resource.ETag))
            {
                response.StatusCode = (int) HttpStatusCode.NotModified;
                response.Close();
                return;
            }
        }

        response.ContentLength64 = resource.Bytes.Length;
        if (string.Equals(context.Request.HttpMethod, HttpMethod.Head.Method, StringComparison.OrdinalIgnoreCase))
        {
            response.Close();
            return;
        }

        await response.OutputStream.WriteAsync(resource.Bytes);
        response.Close();
    }

    private static string? GetRelativePath(string path, string prefix)
    {
        if (prefix.Length == 0)
        {
            return path;
        }

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (path.Length == prefix.Length)
        {
            return "/";
        }

        if (path[prefix.Length] != '/')
        {
            return null;
        }

        return path[prefix.Length..];
    }

    private static bool IsEtagMatch(string ifNoneMatch, string etag)
    {
        foreach (var token in ifNoneMatch.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "*")
            {
                return true;
            }

            if (string.Equals(token, etag, StringComparison.Ordinal))
            {
                return true;
            }

            if (token.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
                && string.Equals(token[2..], etag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ToResourceSuffix(string relativePath, string defaultDocument)
    {
        var suffix = relativePath.TrimStart('/');
        return suffix.Length == 0
            ? NormalizeResourceSuffix(defaultDocument)
            : NormalizeResourceSuffix(suffix);
    }

    private static string NormalizeResourceSuffix(string path)
    {
        return path.TrimStart('/').Replace('\\', '/');
    }

    private CachedResource? GetOrCache(string resourceSuffix)
    {
        if (this._cache.TryGetValue(resourceSuffix, out var cached))
        {
            return cached;
        }

        var bytes = fileProvider.GetFileContent(resourceSuffix);
        if (bytes is null)
        {
            return null;
        }

        var etag = this._options.EnableETag ? this._options.ETagFactory(bytes) : null;
        cached = new CachedResource(resourceSuffix, bytes, etag);
        return this._cache.GetOrAdd(resourceSuffix, cached);
    }

    private sealed record CachedResource(string ResourceSuffix, byte[] Bytes, string? ETag);
}
