using System.Net;
using System.Security.Cryptography;

namespace HttpHarker;

/// <summary>
/// Options for static file serving middleware, controlling content-type resolution,
/// SPA fallback, ETag generation, and cache headers.
/// </summary>
public sealed class StaticFileOptions
{
    /// <summary>Resolves <c>Content-Type</c> values from file extensions. Defaults to <see cref="ContentTypeProvider.CreateDefault"/>.</summary>
    public ContentTypeProvider ContentTypeProvider { get; } = ContentTypeProvider.CreateDefault();

    /// <summary>Directory index served for "/" requests.</summary>
    public string DefaultDocument { get; set; } = "index.html";

    /// <summary>Serve this file when SPA fallback is enabled and a route-like path is requested.</summary>
    public string SpaFallbackDocument { get; set; } = "index.html";

    /// <summary>When <c>true</c>, requests that match <see cref="SpaFallbackPredicate"/> and resolve to no file
    /// are served <see cref="SpaFallbackDocument"/> instead, enabling client-side routing.</summary>
    public bool EnableSpaFallback { get; set; }

    /// <summary>
    /// Determines which requests are eligible for the SPA fallback.
    /// Defaults to GET/HEAD requests whose URL path has no file extension.
    /// </summary>
    public Func<HttpListenerRequest, bool> SpaFallbackPredicate { get; set; } =
        DefaultSpaFallbackPredicate;

    /// <summary>When <c>true</c>, an <c>ETag</c> header is computed and sent with each file response,
    /// and <c>If-None-Match</c> conditional requests are honoured with 304 responses.</summary>
    public bool EnableETag { get; set; }

    /// <summary>
    /// Computes an <c>ETag</c> value from raw file bytes. Defaults to a quoted hex-encoded SHA-256 digest.
    /// Only invoked when <see cref="EnableETag"/> is <c>true</c>.
    /// </summary>
    public Func<byte[], string> ETagFactory { get; set; } = DefaultEtagFactory;

    /// <summary>Input is the resolved resource suffix (e.g. "assets/app.js").</summary>
    public Func<string, string?>? CacheControlSelector { get; set; }

    private static bool DefaultSpaFallbackPredicate(HttpListenerRequest request)
    {
        if (
            !string.Equals(
                request.HttpMethod,
                HttpMethod.Get.Method,
                StringComparison.OrdinalIgnoreCase
            )
            && !string.Equals(
                request.HttpMethod,
                HttpMethod.Head.Method,
                StringComparison.OrdinalIgnoreCase
            )
        )
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
