using System.Net;
using System.Security.Cryptography;

namespace HttpHarker;

public sealed class EmbeddedResourceOptions
{
    public ContentTypeProvider ContentTypeProvider { get; set; } = ContentTypeProvider.CreateDefault();

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
        return $"\"{Convert.ToHexString(SHA256.HashData(bytes))}\"";
    }
}
