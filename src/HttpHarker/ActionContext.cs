using System.Net;
using System.Text;

namespace HttpHarker;

/// <summary>
/// Request/response context passed to route handler delegates.
/// </summary>
public sealed record HttpActionContext(
    HttpListenerRequest Request,
    HttpListenerResponse Response,
    IReadOnlyDictionary<string, string> Args)
{
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
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            this.Response.Headers[name] = string.Join(", ", values);
        }

        await content.CopyToAsync(this.Response.OutputStream);
        this.Response.Close();
    }

    public Task CloseAsync(string contentType, string body)
    {
        // StringContent only accepts the media type without parameters; charset is provided via Encoding.
        var mediaType = contentType.Split(';')[0].Trim();
        return this.CloseAsync(new StringContent(body, Encoding.UTF8, mediaType));
    }

    public async Task CloseAsync(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        this.Response.ContentLength64 = bytes.Length;
        await this.Response.OutputStream.WriteAsync(bytes);
        this.Response.Close();
    }
}
