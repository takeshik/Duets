using System.Net;

namespace HttpHarker.Middlewares;

/// <summary>
/// Pattern-based HTTP router middleware; matches method and path template, extracts route parameters,
/// and dispatches to registered handlers.
/// </summary>
public sealed class SimpleRoutingMiddleware : IMiddleware
{
    public SimpleRoutingMiddleware(string root, Action<Builder>? configure = null)
    {
        this._prefix = root.TrimEnd('/');
        var builder = new Builder();
        configure?.Invoke(builder);
        this._routes = new SortedSet<Route>(
            builder.Routes.Select(r => new Route(r.Method, r.Template, r.Handler))
        );
    }

    private readonly string _prefix;
    private readonly SortedSet<Route> _routes;

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
            if (!route.TryMatch(method, path, out var handler)) continue;
            await handler(context);
            return;
        }

        await next();
    }

    private static string? GetRelativePath(string path, string prefix)
    {
        if (prefix.Length == 0) return path;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Length == prefix.Length) return "/";
        if (path[prefix.Length] != '/') return null;
        return path[prefix.Length..];
    }

    public sealed class Builder
    {
        internal List<(HttpMethod Method, string Template, Func<HttpActionContext, Task> Handler)> Routes { get; } = [];

        public Builder Map(HttpMethod method, string template, Func<HttpActionContext, Task> handler)
        {
            this.Routes.Add((method, template, handler));
            return this;
        }

        public Builder MapGet(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Get, template, handler);
        }

        public Builder MapPost(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Post, template, handler);
        }

        public Builder MapPut(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Put, template, handler);
        }

        public Builder MapDelete(string template, Func<HttpActionContext, Task> handler)
        {
            return this.Map(HttpMethod.Delete, template, handler);
        }
    }

    public sealed class Route : IComparable<Route>
    {
        public Route(HttpMethod method, string template, Func<HttpActionContext, Task> handler)
        {
            this.Method = method;
            this.Template = template;
            this.Handler = handler;
            this.SortKey = Array.ConvertAll(
                template.Split('/', StringSplitOptions.RemoveEmptyEntries),
                static part => part is ['{', .., '}']
                    ? part.Length > 2 && part[1] == '*' ? 0 : 1
                    : 2
            );
        }

        private HttpMethod Method { get; }
        private string Template { get; }
        private Func<HttpActionContext, Task> Handler { get; }
        private int[] SortKey { get; }

        private RouteSegment[] Segments
            => field ??= ParseTemplate(this.Template);

        public override string ToString()
        {
            return $"{this.Method} {this.Template}";
        }

        internal bool TryMatch(HttpMethod method, string path, out Func<HttpListenerContext, Task> handler)
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
                        if (i >= pathSegments.Length || !string.Equals(pathSegments[i], value, StringComparison.OrdinalIgnoreCase))
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
                        handler = ctx => this.Handler(new HttpActionContext(ctx.Request, ctx.Response, args));
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
        public int CompareTo(Route? other)
        {
            if (other is null) return 1;
            var len = Math.Max(this.SortKey.Length, other.SortKey.Length);
            for (var i = 0; i < len; i++)
            {
                var aVal = i < this.SortKey.Length ? this.SortKey[i] : -1;
                var bVal = i < other.SortKey.Length ? other.SortKey[i] : -1;
                var cmp = bVal.CompareTo(aVal); // descending: higher priority → smaller element → iterated first
                if (cmp != 0) return cmp;
            }

            var methodCmp = string.Compare(this.Method.Method, other.Method.Method, StringComparison.Ordinal);
            if (methodCmp != 0) return methodCmp;
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
                            throw new ArgumentException($"Empty catch-all parameter name in template: {template}");
                        }

                        if (i != parts.Length - 1)
                        {
                            throw new ArgumentException($"Catch-all segment must be the last segment in template: {template}");
                        }

                        segments[i] = new RouteSegment(SegmentKind.CatchAll, paramName);
                    }
                    else
                    {
                        if (name.Length == 0)
                        {
                            throw new ArgumentException($"Empty parameter name in template: {template}");
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
