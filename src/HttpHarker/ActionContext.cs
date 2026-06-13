using System.Net;
using System.Text;

namespace HttpHarker;

/// <summary>
/// Request/response context passed to route handler delegates.
/// </summary>
public sealed record HttpActionContext(
    HttpListenerRequest Request,
    HttpListenerResponse Response,
    IReadOnlyDictionary<string, string> Args
)
{
    /// <summary>
    /// Copies the headers and body of <paramref name="content"/> to the response, then closes it.
    /// </summary>
    /// <param name="content">The HTTP content whose headers and body are written to the response.</param>
    public async Task CloseAsync(HttpContent content)
    {
        if (content.Headers.ContentType is { } ct)
        {
            this.Response.ContentType = ct.ToString();
        }

        if (content.Headers.ContentLength is { } cl)
        {
            this.Response.ContentLength64 = cl;
        }

        foreach (var (name, values) in content.Headers)
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            this.Response.Headers[name] = string.Join(", ", values);
        }

        await content.CopyToAsync(this.Response.OutputStream);
        this.Response.Close();
    }

    /// <summary>
    /// Sends <paramref name="body"/> as a UTF-8 response with the given content type, then closes the response.
    /// </summary>
    /// <param name="contentType">The media type (e.g. <c>"text/html"</c>); parameters such as charset are stripped
    /// because they are supplied automatically via <see cref="System.Text.Encoding.UTF8"/>.</param>
    /// <param name="body">The response body text.</param>
    public Task CloseAsync(string contentType, string body)
    {
        // StringContent only accepts the media type without parameters; charset is provided via Encoding.
        var mediaType = contentType.Split(';')[0].Trim();
        return this.CloseAsync(new StringContent(body, Encoding.UTF8, mediaType));
    }

    /// <summary>
    /// Sends <paramref name="body"/> as raw UTF-8 bytes without setting a content type, then closes the response.
    /// </summary>
    /// <param name="body">The response body text.</param>
    public async Task CloseAsync(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        this.Response.ContentLength64 = bytes.Length;
        await this.Response.OutputStream.WriteAsync(bytes);
        this.Response.Close();
    }
}
