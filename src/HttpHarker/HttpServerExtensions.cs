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
            Action<EmbeddedResourceOptions>? configure = null)
        {
            var options = new EmbeddedResourceOptions();
            configure?.Invoke(options);
            server.Use(new EmbeddedResourceMiddleware(assembly, resourcePrefix, root, options));
            return server;
        }
    }
}
