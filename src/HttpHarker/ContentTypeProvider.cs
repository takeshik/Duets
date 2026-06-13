using System.Net;

namespace HttpHarker;

/// <summary>
/// Maps HTTP requests to <c>Content-Type</c> values via a configurable key selector and extension-based lookup table.
/// </summary>
public sealed class ContentTypeProvider(
    Func<HttpListenerRequest, string?> keySelector,
    Func<HttpListenerRequest, string>? fallback = null,
    IEqualityComparer<string>? keyComparer = null
)
{
    private readonly Dictionary<string, string> _mappings = new(
        keyComparer ?? StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Derives the lookup key from an incoming request (typically the URL file extension).</summary>
    public Func<HttpListenerRequest, string?> KeySelector { get; } = keySelector;

    /// <summary>Returns the content type when the key produced by <see cref="KeySelector"/> has no mapping.
    /// Defaults to <c>application/octet-stream</c>.</summary>
    public Func<HttpListenerRequest, string> Fallback { get; } =
        fallback ?? (_ => "application/octet-stream");

    /// <summary>
    /// Creates a provider keyed on the URL path file extension and pre-populated with mappings for
    /// common web asset types (HTML, CSS, JS, JSON, images, fonts, and source maps).
    /// </summary>
    /// <returns>A new <see cref="ContentTypeProvider"/> ready for use.</returns>
    public static ContentTypeProvider CreateDefault()
    {
        return new ContentTypeProvider(static request =>
            Path.GetExtension(request.Url?.AbsolutePath ?? "")
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

    /// <summary>Adds or replaces a mapping from <paramref name="key"/> to <paramref name="contentType"/>.</summary>
    /// <param name="key">The lookup key (e.g. a file extension such as <c>".html"</c>).</param>
    /// <param name="contentType">The <c>Content-Type</c> value to return for this key.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public ContentTypeProvider Add(string key, string contentType)
    {
        this._mappings[key] = contentType;
        return this;
    }

    /// <summary>Adds or replaces multiple key-to-content-type mappings in bulk.</summary>
    /// <param name="mappings">Pairs of lookup key and content type value.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public ContentTypeProvider AddRange(IEnumerable<KeyValuePair<string, string>> mappings)
    {
        foreach (var (key, contentType) in mappings)
        {
            this._mappings[key] = contentType;
        }

        return this;
    }

    /// <summary>
    /// Derives the key from <paramref name="request"/> via <see cref="KeySelector"/> and returns the mapped content type,
    /// or the <see cref="Fallback"/> value when no mapping exists.
    /// </summary>
    /// <param name="request">The incoming HTTP request.</param>
    /// <returns>The resolved <c>Content-Type</c> string.</returns>
    public string Resolve(HttpListenerRequest request)
    {
        return this.Resolve(this.KeySelector(request), request);
    }

    /// <summary>
    /// Returns the content type mapped to <paramref name="key"/>, or the <see cref="Fallback"/> value
    /// when <paramref name="key"/> is <c>null</c>, empty, or has no mapping.
    /// </summary>
    /// <param name="key">The lookup key; <c>null</c> or empty always falls back.</param>
    /// <param name="request">The incoming HTTP request, forwarded to <see cref="Fallback"/> when needed.</param>
    /// <returns>The resolved <c>Content-Type</c> string.</returns>
    public string Resolve(string? key, HttpListenerRequest request)
    {
        if (key is { Length: > 0 } && this._mappings.TryGetValue(key, out var contentType))
        {
            return contentType;
        }

        return this.Fallback(request);
    }
}
