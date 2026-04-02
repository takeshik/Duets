using System.Net;
using System.Security.Cryptography;

namespace HttpHarker;

/// <summary>
/// Options for static file serving middleware, controlling content-type resolution,
/// SPA fallback, ETag generation, and cache headers.
/// </summary>
public sealed class StaticFileOptions
{
    public ContentTypeProvider ContentTypeProvider { get; } = ContentTypeProvider.CreateDefault();

    /// <summary>Directory index served for "/" requests.</summary>
    public string DefaultDocument { get; set; } = "index.html";

    /// <summary>Serve this file when SPA fallback is enabled and a route-like path is requested.</summary>
    public string SpaFallbackDocument { get; set; } = "index.html";

    public bool EnableSpaFallback { get; set; }

    public Func<HttpListenerRequest, bool> SpaFallbackPredicate { get; set; } = DefaultSpaFallbackPredicate;

    public bool EnableETag { get; set; }

    public Func<byte[], string> ETagFactory { get; set; } = DefaultEtagFactory;

    /// <summary>Input is the resolved resource suffix (e.g. "assets/app.js").</summary>
    public Func<string, string?>? CacheControlSelector { get; set; }

    private static bool DefaultSpaFallbackPredicate(HttpListenerRequest request)
    {
        if (!string.Equals(request.HttpMethod, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.HttpMethod, HttpMethod.Head.Method, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = request.Url?.AbsolutePath ?? "/";
        return Path.GetExtension(path).Length == 0;
    }

    private static string DefaultEtagFactory(byte[] bytes)
    {
#if NETSTANDARD2_1
        using var sha256 = SHA256.Create();
        return $"\"{BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty)}\"";
#else
        return $"\"{Convert.ToHexString(SHA256.HashData(bytes))}\"";
#endif
    }
}
