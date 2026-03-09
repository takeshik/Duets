using System.Reflection;
using HttpHarker.Middlewares;

namespace HttpHarker;

public static class HttpServerExtensions
{
    extension(HttpServer server)
    {
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

        public HttpServer UseContentTypeDetection()
        {
            server.Use(async (ctx, next) =>
                {
                    var ext = Path.GetExtension(ctx.Request.Url?.AbsolutePath ?? "");
                    if (ext.Length > 0)
                    {
                        ctx.Response.ContentType = ContentTypeProvider.GetContentType(ext);
                    }

                    await next();
                }
            );
            return server;
        }

        public HttpServer UseEmbeddedResources(
            Assembly assembly,
            string resourcePrefix,
            string root = "/")
        {
            server.Use(new EmbeddedResourceMiddleware(assembly, resourcePrefix, root));
            return server;
        }
    }
}
