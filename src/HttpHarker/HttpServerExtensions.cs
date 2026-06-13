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
        /// <summary>Adds a <see cref="StaticFileMiddleware"/> backed by <paramref name="fileProvider"/> to the pipeline.</summary>
        /// <param name="fileProvider">Source of file bytes for static requests.</param>
        /// <param name="root">URL path prefix under which static files are served.</param>
        /// <param name="configure">Optional callback to configure <see cref="StaticFileOptions"/>.</param>
        /// <returns>This server instance, for fluent chaining.</returns>
        public HttpServer UseStaticFiles(
            IFileProvider fileProvider,
            string root = "/",
            Action<StaticFileOptions>? configure = null
        )
        {
            var options = new StaticFileOptions();
            configure?.Invoke(options);
            return server.Use(new StaticFileMiddleware(fileProvider, root, options));
        }

        /// <summary>Adds a <see cref="ZipArchiveMiddleware"/> that serves files from a zip stream to the pipeline.</summary>
        /// <param name="zipStream">A readable stream containing a zip archive; read once at registration time.</param>
        /// <param name="root">URL path prefix under which the archive entries are served.</param>
        /// <param name="configure">Optional callback to configure <see cref="StaticFileOptions"/>.</param>
        /// <returns>This server instance, for fluent chaining.</returns>
        public HttpServer UseZipArchive(
            Stream zipStream,
            string root = "/",
            Action<StaticFileOptions>? configure = null
        )
        {
            var options = new StaticFileOptions();
            configure?.Invoke(options);
            return server.Use(new ZipArchiveMiddleware(zipStream, root, options));
        }

        /// <summary>
        /// Loads a zip archive from an embedded assembly resource and adds a <see cref="ZipArchiveMiddleware"/> to the pipeline.
        /// </summary>
        /// <param name="assembly">The assembly that contains the embedded zip resource.</param>
        /// <param name="resourceName">The fully-qualified manifest resource name of the zip file.</param>
        /// <param name="root">URL path prefix under which the archive entries are served.</param>
        /// <param name="configure">Optional callback to configure <see cref="StaticFileOptions"/>.</param>
        /// <returns>This server instance, for fluent chaining.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="resourceName"/> does not match any manifest resource in <paramref name="assembly"/>.
        /// </exception>
        public HttpServer UseZipArchive(
            Assembly assembly,
            string resourceName,
            string root = "/",
            Action<StaticFileOptions>? configure = null
        )
        {
            var stream =
                assembly.GetManifestResourceStream(resourceName)
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
            Action<SimpleRoutingMiddleware.Builder>? configure = null
        )
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

        /// <summary>
        /// Adds middleware that sets the response <c>Content-Type</c> header from the request URL
        /// before passing control to the next component.
        /// </summary>
        /// <param name="contentTypeProvider">
        /// Provider used to resolve the content type; defaults to <see cref="ContentTypeProvider.CreateDefault"/> when <c>null</c>.
        /// </param>
        /// <returns>This server instance, for fluent chaining.</returns>
        public HttpServer UseContentTypeDetection(ContentTypeProvider? contentTypeProvider = null)
        {
            var provider = contentTypeProvider ?? ContentTypeProvider.CreateDefault();
            server.Use(
                async (ctx, next) =>
                {
                    ctx.Response.ContentType = provider.Resolve(ctx.Request);
                    await next();
                }
            );
            return server;
        }

        /// <summary>Adds an <see cref="EmbeddedResourceMiddleware"/> that serves assembly manifest resources as static files.</summary>
        /// <param name="assembly">The assembly whose embedded resources are served.</param>
        /// <param name="resourcePrefix">The dot-delimited namespace prefix that all served resources share.</param>
        /// <param name="root">URL path prefix under which the resources are served.</param>
        /// <param name="configure">Optional callback to configure <see cref="StaticFileOptions"/>.</param>
        /// <returns>This server instance, for fluent chaining.</returns>
        public HttpServer UseEmbeddedResources(
            Assembly assembly,
            string resourcePrefix,
            string root = "/",
            Action<StaticFileOptions>? configure = null
        )
        {
            var options = new StaticFileOptions();
            configure?.Invoke(options);
            server.Use(new EmbeddedResourceMiddleware(assembly, resourcePrefix, root, options));
            return server;
        }
    }
}
