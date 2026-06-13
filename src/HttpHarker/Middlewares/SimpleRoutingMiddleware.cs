using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Pattern-based HTTP router middleware; matches method and path template, extracts route parameters,
/// and dispatches to registered handlers.
/// </summary>
/// <remarks>
/// This middleware is <b>terminal</b> for matched routes: once a handler is invoked,
/// <c>next()</c> is not called. Middleware registered after this in the pipeline is therefore
/// unreachable for any matched request. For requests that do not match any route, <c>next()</c>
/// is called normally.
/// </remarks>
public sealed class SimpleRoutingMiddleware : IMiddleware
{
    /// <summary>
    /// Initialises the middleware with the given URL root and optionally-configured routes.
    /// </summary>
    /// <param name="root">URL path prefix; requests outside this root are forwarded to <c>next()</c>.</param>
    /// <param name="configure">Callback that receives a <see cref="Builder"/> to register routes.</param>
    public SimpleRoutingMiddleware(string root, Action<Builder>? configure = null)
    {
        this._prefix = root.TrimEnd('/');
        var builder = new Builder();
        configure?.Invoke(builder);
        this._routes = [.. builder.Routes.Select(r => new Route(r.Method, r.Template, r.Handler))];
    }

    private readonly string _prefix;
    private readonly SortedSet<Route> _routes;

    /// <summary>
    /// Attempts to match the incoming request against registered routes and dispatches to the first match,
    /// or calls <paramref name="next"/> if no route matches.
    /// </summary>
    public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        var method = new HttpMethod(context.Request.HttpMethod);
        var rawPath = context.Request.Url?.AbsolutePath ?? "/";
        var path = GetRelativePath(rawPath, this._prefix);
        if (path is null)
        {
            await next();
            return;
        }

        foreach (var route in this._routes)
        {
            if (!route.TryMatch(method, path, out var handler))
            {
                continue;
            }

            await handler(context);
            return;
        }

        await next();
    }

    private static string? GetRelativePath(string path, string prefix)
    {
        if (prefix.Length == 0)
        {
            return path;
        }

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (path.Length == prefix.Length)
        {
            return "/";
        }

        if (path[prefix.Length] != '/')
        {
            return null;
        }

        return path[prefix.Length..];
    }

    /// <summary>Fluent builder for registering routes on <see cref="SimpleRoutingMiddleware"/>.</summary>
    public sealed class Builder
    {
        internal List<(
            HttpMethod Method,
            string Template,
            Func<HttpActionContext, Task> Handler
        )> Routes { get; } = [];

        /// <summary>Registers a route for <paramref name="method"/> and the given path template.</summary>
        /// <param name="method">The HTTP method this route matches.</param>
        /// <param name="template">
        /// The path template; segments may be literals, parameters (<c>{name}</c>), or a trailing
        /// catch-all (<c>{*name}</c>).
        /// </param>
        /// <param name="handler">The async delegate invoked when the route matches.</param>
        /// <returns>This builder, for fluent chaining.</returns>
        public Builder Map(
            HttpMethod method,
            string template,
            Func<HttpActionContext, Task> handler
        )
        {
            this.Routes.Add((method, template, handler));
            return this;
        }

        /// <summary>Registers a GET route. Equivalent to <c>Map(HttpMethod.Get, …)</c>.</summary>
        /// <param name="template">The path template.</param>
        /// <param name="handler">The async delegate invoked when the route matches.</param>
        /// <returns>This builder, for fluent chaining.</returns>
        public Builder MapGet(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Get, template, handler);
        }

        /// <summary>Registers a POST route. Equivalent to <c>Map(HttpMethod.Post, …)</c>.</summary>
        /// <param name="template">The path template.</param>
        /// <param name="handler">The async delegate invoked when the route matches.</param>
        /// <returns>This builder, for fluent chaining.</returns>
        public Builder MapPost(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Post, template, handler);
        }

        /// <summary>Registers a PUT route. Equivalent to <c>Map(HttpMethod.Put, …)</c>.</summary>
        /// <param name="template">The path template.</param>
        /// <param name="handler">The async delegate invoked when the route matches.</param>
        /// <returns>This builder, for fluent chaining.</returns>
        public Builder MapPut(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Put, template, handler);
        }

        /// <summary>Registers a DELETE route. Equivalent to <c>Map(HttpMethod.Delete, …)</c>.</summary>
        /// <param name="template">The path template.</param>
        /// <param name="handler">The async delegate invoked when the route matches.</param>
        /// <returns>This builder, for fluent chaining.</returns>
        public Builder MapDelete(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Delete, template, handler);
        }
    }

    /// <summary>
    /// An immutable, parsed representation of a single route entry, including its method, template,
    /// handler, and sort priority.
    /// </summary>
    public sealed class Route : IComparable<Route>
    {
        /// <summary>
        /// Parses <paramref name="template"/> and creates a route entry.
        /// </summary>
        /// <param name="method">The HTTP method this route matches.</param>
        /// <param name="template">The path template; validated eagerly — throws <see cref="ArgumentException"/> for invalid templates.</param>
        /// <param name="handler">The delegate invoked when this route matches.</param>
        /// <exception cref="ArgumentException">The template contains an empty parameter name or a non-terminal catch-all segment.</exception>
        public Route(HttpMethod method, string template, Func<HttpActionContext, Task> handler)
        {
            this.Method = method;
            this.Template = template;
            this.Handler = handler;
            this.SortKey = Array.ConvertAll(
                template.Split('/', StringSplitOptions.RemoveEmptyEntries),
                static part =>
                    part is ['{', .., '}']
                        ? part.Length > 2 && part[1] == '*'
                            ? 0
                            : 1
                        : 2
            );
            _ = this.Segments; // Validate template eagerly; throws ArgumentException for invalid templates.
        }

        private HttpMethod Method { get; }
        private string Template { get; }
        private Func<HttpActionContext, Task> Handler { get; }
        private int[] SortKey { get; }

        private RouteSegment[] Segments => field ??= ParseTemplate(this.Template);

        public override string ToString()
        {
            return $"{this.Method} {this.Template}";
        }

        internal bool TryMatch(
            HttpMethod method,
            string path,
            out Func<HttpListenerContext, Task> handler
        )
        {
            if (this.Method != method)
            {
                handler = null!;
                return false;
            }

            var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var args = new Dictionary<string, string>();

            for (var i = 0; i < this.Segments.Length; i++)
            {
                var (kind, value) = this.Segments[i];
                switch (kind)
                {
                    case SegmentKind.Literal:
                        if (
                            i >= pathSegments.Length
                            || !string.Equals(
                                pathSegments[i],
                                value,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            handler = null!;
                            return false;
                        }

                        break;

                    case SegmentKind.Parameter:
                        if (i >= pathSegments.Length)
                        {
                            handler = null!;
                            return false;
                        }

                        args[value] = pathSegments[i];
                        break;

                    case SegmentKind.CatchAll:
                        if (i >= pathSegments.Length)
                        {
                            handler = null!;
                            return false;
                        }

                        args[value] = string.Join('/', pathSegments[i..]);
                        handler = ctx =>
                            this.Handler(new HttpActionContext(ctx.Request, ctx.Response, args));
                        return true;
                }
            }

            if (pathSegments.Length != this.Segments.Length)
            {
                handler = null!;
                return false;
            }

            handler = ctx => this.Handler(new HttpActionContext(ctx.Request, ctx.Response, args));
            return true;
        }

        // SortedSet uses CompareTo for both ordering and identity.
        // Priority (descending by sort key) comes first; method + template act as a tiebreaker
        // to ensure a total order and to treat (method, template) as the unique key.
        /// <summary>
        /// Compares routes by descending priority (more-specific segments first), then by method and template as a
        /// tiebreaker, producing the total order required by <see cref="SortedSet{T}"/>.
        /// </summary>
        public int CompareTo(Route? other)
        {
            if (other is null)
            {
                return 1;
            }

            var len = Math.Max(this.SortKey.Length, other.SortKey.Length);
            for (var i = 0; i < len; i++)
            {
                var aVal = i < this.SortKey.Length ? this.SortKey[i] : -1;
                var bVal = i < other.SortKey.Length ? other.SortKey[i] : -1;
                var cmp = bVal.CompareTo(aVal); // descending: higher priority → smaller element → iterated first
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            var methodCmp = string.Compare(
                this.Method.Method,
                other.Method.Method,
                StringComparison.Ordinal
            );
            if (methodCmp != 0)
            {
                return methodCmp;
            }

            return string.Compare(this.Template, other.Template, StringComparison.Ordinal);
        }

        private static RouteSegment[] ParseTemplate(string template)
        {
            var parts = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var segments = new RouteSegment[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.StartsWith('{') && part.EndsWith('}'))
                {
                    var name = part[1..^1];
                    if (name.StartsWith('*'))
                    {
                        var paramName = name[1..];
                        if (paramName.Length == 0)
                        {
                            throw new ArgumentException(
                                $"Empty catch-all parameter name in template: {template}"
                            );
                        }

                        if (i != parts.Length - 1)
                        {
                            throw new ArgumentException(
                                $"Catch-all segment must be the last segment in template: {template}"
                            );
                        }

                        segments[i] = new RouteSegment(SegmentKind.CatchAll, paramName);
                    }
                    else
                    {
                        if (name.Length == 0)
                        {
                            throw new ArgumentException(
                                $"Empty parameter name in template: {template}"
                            );
                        }

                        segments[i] = new RouteSegment(SegmentKind.Parameter, name);
                    }
                }
                else
                {
                    segments[i] = new RouteSegment(SegmentKind.Literal, part);
                }
            }

            return segments;
        }
    }

    private enum SegmentKind
    {
        Literal,
        Parameter,
        CatchAll,
    }

    private readonly record struct RouteSegment(SegmentKind Kind, string Value);
}
