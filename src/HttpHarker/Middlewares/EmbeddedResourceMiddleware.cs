using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace HttpHarker.Middlewares;

public sealed class EmbeddedResourceMiddleware : IMiddleware
{
    public EmbeddedResourceMiddleware(
        Assembly assembly,
        string resourcePrefix,
        string root = "/",
        EmbeddedResourceOptions? options = null)
    {
        this._assembly = assembly;
        this._resourcePrefix = resourcePrefix.TrimEnd('.');
        this._prefix = root.TrimEnd('/');
        this._options = options ?? new EmbeddedResourceOptions();

        if (string.IsNullOrWhiteSpace(this._options.DefaultDocument))
        {
            throw new ArgumentException("DefaultDocument must not be empty.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(this._options.SpaFallbackDocument))
        {
            throw new ArgumentException("SpaFallbackDocument must not be empty.", nameof(options));
        }
    }

    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;
    private readonly string _prefix;
    private readonly EmbeddedResourceOptions _options;
    private readonly ConcurrentDictionary<string, CachedResource> _resourceCache = new(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        var rawPath = context.Request.Url?.AbsolutePath ?? "/";
        var relativePath = GetRelativePath(rawPath, this._prefix);
        if (relativePath is null)
        {
            await next();
            return;
        }

        var requestedSuffix = ToResourceSuffix(relativePath, this._options.DefaultDocument);
        var resource = this.GetCachedResourceOrNull(requestedSuffix);

        if (resource is null
            && this._options.EnableSpaFallback
            && this._options.SpaFallbackPredicate(context.Request))
        {
            var fallbackSuffix = NormalizeResourceSuffix(this._options.SpaFallbackDocument);
            resource = this.GetCachedResourceOrNull(fallbackSuffix);
        }

        if (resource is null)
        {
            await next();
            return;
        }

        var response = context.Response;
        response.ContentType = this._options.ContentTypeProvider.Resolve(Path.GetExtension(resource.ResourceSuffix), context.Request);

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
        if (prefix.Length == 0) return path;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Length == prefix.Length) return "/";
        if (path[prefix.Length] != '/') return null;
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
        if (suffix.Length == 0)
        {
            return NormalizeResourceSuffix(defaultDocument);
        }

        return NormalizeResourceSuffix(suffix);
    }

    private static string NormalizeResourceSuffix(string path)
    {
        return path.TrimStart('/').Replace('\\', '/');
    }

    private CachedResource? GetCachedResourceOrNull(string resourceSuffix)
    {
        if (this._resourceCache.TryGetValue(resourceSuffix, out var cached))
        {
            return cached;
        }

        var resourceName = $"{this._resourcePrefix}.{resourceSuffix.Replace('/', '.')}";
        using var stream = this._assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var etag = this._options.EnableETag ? this._options.ETagFactory(bytes) : null;
        cached = new CachedResource(resourceSuffix, bytes, etag);
        this._resourceCache[resourceSuffix] = cached;
        return cached;
    }

    private sealed record CachedResource(string ResourceSuffix, byte[] Bytes, string? ETag);
}
