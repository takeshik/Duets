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

        /// <summary>Adds a <see cref="SimpleRoutingMiddleware"/> to the pipeline.</summary>
        /// <remarks>
        /// This middleware is terminal for matched routes; <c>next()</c> is not called after a
        /// route handler executes. Middleware registered after this call is unreachable for matched
        /// requests. Register error-page or status-code middleware <b>before</b> this call.
        /// </remarks>
        public HttpServer UseSimpleRouting(
            string root = "/",
            Action<SimpleRoutingMiddleware.Builder>? configure = null)
        {
            return server.Use(new SimpleRoutingMiddleware(root, configure));
        }

        /// <summary>Adds an <see cref="ErrorPagesMiddleware"/> to the pipeline.</summary>
        /// <remarks>
        /// Register this <b>before</b> any terminal middleware (e.g. <see cref="UseSimpleRouting"/>).
        /// It intercepts the response after the rest of the pipeline has run, so it must be
        /// outermost to be reachable for all requests.
        /// </remarks>
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
