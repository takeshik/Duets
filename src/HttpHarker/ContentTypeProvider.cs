using System.Net;

namespace HttpHarker;

/// <summary>
/// Maps HTTP requests to <c>Content-Type</c> values via a configurable key selector and extension-based lookup table.
/// </summary>
public sealed class ContentTypeProvider
{
    public ContentTypeProvider(
        Func<HttpListenerRequest, string?> keySelector,
        Func<HttpListenerRequest, string>? fallback = null,
        IEqualityComparer<string>? keyComparer = null)
    {
        this.KeySelector = keySelector;
        this.Fallback = fallback ?? (_ => "application/octet-stream");
        this._mappings = new Dictionary<string, string>(keyComparer ?? StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, string> _mappings;

    public Func<HttpListenerRequest, string?> KeySelector { get; }

    public Func<HttpListenerRequest, string> Fallback { get; }

    public static ContentTypeProvider CreateDefault()
    {
        return new ContentTypeProvider(static request => Path.GetExtension(request.Url?.AbsolutePath ?? "")
            )
            .Add(".html", "text/html; charset=utf-8")
            .Add(".css", "text/css; charset=utf-8")
            .Add(".js", "application/javascript; charset=utf-8")
            .Add(".mjs", "application/javascript; charset=utf-8")
            .Add(".json", "application/json; charset=utf-8")
            .Add(".txt", "text/plain; charset=utf-8")
            .Add(".svg", "image/svg+xml")
            .Add(".png", "image/png")
            .Add(".jpg", "image/jpeg")
            .Add(".jpeg", "image/jpeg")
            .Add(".gif", "image/gif")
            .Add(".ico", "image/x-icon")
            .Add(".woff", "font/woff")
            .Add(".woff2", "font/woff2")
            .Add(".ttf", "font/ttf")
            .Add(".map", "application/json; charset=utf-8");
    }

    public ContentTypeProvider Add(string key, string contentType)
    {
        this._mappings[key] = contentType;
        return this;
    }

    public ContentTypeProvider AddRange(IEnumerable<KeyValuePair<string, string>> mappings)
    {
        foreach (var (key, contentType) in mappings)
        {
            this._mappings[key] = contentType;
        }

        return this;
    }

    public string Resolve(HttpListenerRequest request)
    {
        return this.Resolve(this.KeySelector(request), request);
    }

    public string Resolve(string? key, HttpListenerRequest request)
    {
        if (key is { Length: > 0 } && this._mappings.TryGetValue(key, out var contentType))
        {
            return contentType;
        }

        return this.Fallback(request);
    }
}
