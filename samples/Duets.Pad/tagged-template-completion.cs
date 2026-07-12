// Tagged-template completion in DuetsPad
//
// Serves DuetsPad and registers a `path` tagged-template function whose template
// body is completed from a host-owned list. Open http://127.0.0.1:17375/ and try:
//
//   path`/assets/sc`
//
#:project ../../src/Duets.Jint/Duets.Jint.csproj
#:project ../../src/Duets.Pad/Duets.Pad.csproj

using Duets;
using Duets.Completions;
using Duets.Jint;
using Duets.Pad;
using HttpHarker;
using Jint;

var paths = new[]
{
    "/assets/scene/main",
    "/assets/scene/menu",
    "/assets/scripts/player",
    "/assets/textures/sky",
};

using var server = new HttpServer("http://127.0.0.1:17375/");
using var pad = server
    .UseContentTypeDetection()
    .UseDuetsPad(configure: opts =>
        opts.SessionFactory = async () =>
        {
            var session = await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
            session.RegisterTaggedTemplate(
                "path",
                invocation => string.Concat(invocation.Raw),
                complete: (context, _) =>
                {
                    var prefix = context.TextBeforeCaret;
                    var items = paths
                        .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(path => new TemplateCompletionItem(
                            path,
                            ReplacementSpan: new TextSpan(0, context.CurrentSegmentRaw.Length),
                            Kind: TemplateCompletionKind.Value
                        ))
                        .ToArray();
                    return new ValueTask<IReadOnlyList<TemplateCompletionItem>>(items);
                }
            );
            return session;
        }
    );

Console.Error.WriteLine("DuetsPad started at http://127.0.0.1:17375/ - press Ctrl+C to stop.");
await server.RunAsync();
