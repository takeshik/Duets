using System.Text;
using Duets.Pad;
using Jint;

namespace Duets.Pad.Tests;

public sealed class TaggedTemplateCompletionTests
{
    private static Engine CreateEngine()
    {
        using var stream = typeof(DuetsPadService).Assembly.GetManifestResourceStream(
            "Duets.Pad.Resources.StaticFiles.tagged-template-completion.js"
        );
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return new Engine().Execute(reader.ReadToEnd());
    }

    [Fact]
    public void Provider_uses_the_fourth_argument_as_the_cancellation_token()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      let requestCalled = false;
                      const monaco = {
                        editor: {
                          tokenize: () => [[
                            { offset: 0, type: "identifier.ts" },
                            { offset: 4, type: "string.ts" },
                          ]],
                        },
                      };
                      const model = {
                        getValue: () => "path`abc`",
                        getOffsetAt: () => 8,
                      };
                      const provider = DuetsPadTaggedTemplateCompletion.createCompletionItemProvider({
                        monaco,
                        getTags: () => new Set(["path"]),
                        requestCompletions: () => {
                          requestCalled = true;
                          return { ok: true, items: [] };
                        },
                      });
                      provider.provideCompletionItems(
                        model,
                        { lineNumber: 1, column: 9 },
                        { triggerKind: 0 },
                        { isCancellationRequested: true },
                      );
                      return provider.provideCompletionItems.length === 4 && !requestCalled;
                    })()
                    """
                )
                .AsBoolean()
        );
    }
}
