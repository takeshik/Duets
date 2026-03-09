using System.Net;
using System.Reflection;

namespace HttpHarker.Middlewares;

public sealed class EmbeddedResourceMiddleware : IMiddleware
{
    public EmbeddedResourceMiddleware(Assembly assembly, string resourcePrefix, string root = "/")
    {
        this._assembly = assembly;
        this._resourcePrefix = resourcePrefix.TrimEnd('.');
        this._prefix = root.TrimEnd('/');
    }

    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;
    private readonly string _prefix;

    public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        var rawPath = context.Request.Url?.AbsolutePath ?? "/";
        var relativePath = GetRelativePath(rawPath, this._prefix);
        if (relativePath is null)
        {
            await next();
            return;
        }

        // "/" → "index.html" fallback
        var resourceSuffix = relativePath.TrimStart('/');
        if (resourceSuffix.Length == 0)
        {
            resourceSuffix = "index.html";
        }

        // /foo/bar.html → foo.bar.html (path separators become dots)
        var resourceName = $"{this._resourcePrefix}.{resourceSuffix.Replace('/', '.')}";
        var stream = this._assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            await next();
            return;
        }

        var ext = Path.GetExtension(resourceSuffix);
        context.Response.ContentType = ContentTypeProvider.GetContentType(ext);
        await using (stream)
        {
            await stream.CopyToAsync(context.Response.OutputStream);
        }

        context.Response.Close();
    }

    private static string? GetRelativePath(string path, string prefix)
    {
        if (prefix.Length == 0) return path;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Length == prefix.Length) return "/";
        if (path[prefix.Length] != '/') return null;
        return path[prefix.Length..];
    }
}
