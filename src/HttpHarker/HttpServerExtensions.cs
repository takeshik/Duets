using System.Reflection;
using HttpHarker.Middlewares;

namespace HttpHarker;

/// <summary>
/// Extension methods for configuring common middleware on <see cref="HttpServer"/>.
/// </summary>
public static class HttpServerExtensions
{
    extension(HttpServer server)
    {
        public HttpServer UseStaticFiles(
            IFileProvider fileProvider,
            string root = "/",
            Action<StaticFileOptions>? configure = null)
        {
            var options = new StaticFileOptions();
            configure?.Invoke(options);
            return server.Use(new StaticFileMiddleware(fileProvider, root, options));
        }

        public HttpServer UseZipArchive(
            Stream zipStream,
            string root = "/",
            Action<StaticFileOptions>? configure = null)
        {
            var options = new StaticFileOptions();
            configure?.Invoke(options);
            return server.Use(new ZipArchiveMiddleware(zipStream, root, options));
        }

        public HttpServer UseZipArchive(
            Assembly assembly,
            string resourceName,
            string root = "/",
            Action<StaticFileOptions>? configure = null)
        {
            var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new ArgumentException(
                    $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'.",
                    nameof(resourceName)
                );
            return server.UseZipArchive(stream, root, configure);
        }

        public HttpServer UseSimpleRouting(
            string root = "/",
            Action<SimpleRoutingMiddleware.Builder>? configure = null)
        {
            return server.Use(new SimpleRoutingMiddleware(root, configure));
        }

        public HttpServer UseErrorPages(Action<ErrorPagesMiddleware.Builder>? configure = null)
        {
            return server.Use(new ErrorPagesMiddleware(configure));
        }

        public HttpServer UseContentTypeDetection(ContentTypeProvider? contentTypeProvider = null)
        {
            var provider = contentTypeProvider ?? ContentTypeProvider.CreateDefault();
            server.Use(async (ctx, next) =>
                {
                    ctx.Response.ContentType = provider.Resolve(ctx.Request);
                    await next();
                }
            );
            return server;
        }

        public HttpServer UseEmbeddedResources(
            Assembly assembly,
            string resourcePrefix,
            string root = "/",
            Action<StaticFileOptions>? configure = null)
        {
            var options = new StaticFileOptions();
            configure?.Invoke(options);
            server.Use(new EmbeddedResourceMiddleware(assembly, resourcePrefix, root, options));
            return server;
        }
    }
}
