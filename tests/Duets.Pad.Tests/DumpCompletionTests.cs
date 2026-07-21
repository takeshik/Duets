using System.Text;
using Duets.Pad;
using Jint;

namespace Duets.Pad.Tests;

public sealed class DumpCompletionTests
{
    private static Engine CreateEngine()
    {
        using var stream = typeof(DuetsPadService).Assembly.GetManifestResourceStream(
            "Duets.Pad.Resources.StaticFiles.dump-completion.js"
        );
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return new Engine().Execute(reader.ReadToEnd());
    }

    [Fact]
    public void FindCompletionRange_accepts_empty_and_partial_member_names()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: { tokenize: () => [[{ offset: 0, type: "identifier.ts" }, { offset: 3, type: "delimiter.ts" }]] },
                      };
                      const emptyModel = {
                        getValue: () => "obj.",
                        getLineContent: () => "obj.",
                        getWordUntilPosition: () => ({ startColumn: 5, endColumn: 5 }),
                      };
                      const partialModel = {
                        getValue: () => "obj.du",
                        getLineContent: () => "obj.du",
                        getWordUntilPosition: () => ({ startColumn: 5, endColumn: 7 }),
                      };
                      const empty = DuetsPadDumpCompletion.findCompletionRange(
                        monaco,
                        emptyModel,
                        { lineNumber: 1, column: 5 },
                      );
                      const partial = DuetsPadDumpCompletion.findCompletionRange(
                        monaco,
                        partialModel,
                        { lineNumber: 1, column: 7 },
                      );
                      return empty.startColumn === 5 && empty.endColumn === 5
                        && partial.startColumn === 5 && partial.endColumn === 7;
                    })()
                    """
                )
                .AsBoolean()
        );
    }

    [Theory]
    [InlineData("string.ts")]
    [InlineData("comment.ts")]
    [InlineData("regexp.ts")]
    public void FindCompletionRange_rejects_non_code_tokens(string tokenType)
    {
        using var engine = CreateEngine();
        engine.SetValue("tokenType", tokenType);

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: { tokenize: () => [[{ offset: 0, type: tokenType }]] },
                      };
                      const model = {
                        getValue: () => "value.",
                        getLineContent: () => "value.",
                        getWordUntilPosition: () => ({ startColumn: 7, endColumn: 7 }),
                      };
                      return DuetsPadDumpCompletion.findCompletionRange(
                        monaco,
                        model,
                        { lineNumber: 1, column: 7 },
                      ) === null;
                    })()
                    """
                )
                .AsBoolean()
        );
    }

    [Fact]
    public void FindCompletionRange_rejects_dot_absorbed_by_numeric_literal()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: { tokenize: () => [[{ offset: 0, type: "number.float.ts" }]] },
                      };
                      const model = {
                        getValue: () => "3.",
                        getLineContent: () => "3.",
                        getWordUntilPosition: () => ({ startColumn: 3, endColumn: 3 }),
                      };
                      return DuetsPadDumpCompletion.findCompletionRange(
                        monaco,
                        model,
                        { lineNumber: 1, column: 3 },
                      ) === null;
                    })()
                    """
                )
                .AsBoolean()
        );
    }

    [Fact]
    public void FindCompletionRange_accepts_member_dot_after_numeric_literal()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: {
                          tokenize: () => [[
                            { offset: 0, type: "number.float.ts" },
                            { offset: 2, type: "delimiter.ts" },
                          ]],
                        },
                      };
                      const model = {
                        getValue: () => "3..",
                        getLineContent: () => "3..",
                        getWordUntilPosition: () => ({ startColumn: 4, endColumn: 4 }),
                      };
                      const range = DuetsPadDumpCompletion.findCompletionRange(
                        monaco,
                        model,
                        { lineNumber: 1, column: 4 },
                      );
                      return range.startColumn === 4 && range.endColumn === 4;
                    })()
                    """
                )
                .AsBoolean()
        );
    }

    [Fact]
    public void FindCompletionRange_rejects_spread_context()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: { tokenize: () => { throw new Error("must not tokenize"); } },
                      };
                      const model = {
                        getValue: () => "foo(...du",
                        getLineContent: () => "foo(...du",
                        getWordUntilPosition: () => ({ startColumn: 8, endColumn: 10 }),
                      };
                      return DuetsPadDumpCompletion.findCompletionRange(
                        monaco,
                        model,
                        { lineNumber: 1, column: 10 },
                      ) === null;
                    })()
                    """
                )
                .AsBoolean()
        );
    }

    [Fact]
    public void FindCompletionRange_rejects_non_member_context()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: { tokenize: () => [[{ offset: 0, type: "identifier.ts" }]] },
                      };
                      const model = {
                        getValue: () => "dump",
                        getLineContent: () => "dump",
                        getWordUntilPosition: () => ({ startColumn: 1, endColumn: 5 }),
                      };
                      return DuetsPadDumpCompletion.findCompletionRange(
                        monaco,
                        model,
                        { lineNumber: 1, column: 5 },
                      ) === null;
                    })()
                    """
                )
                .AsBoolean()
        );
    }

    [Fact]
    public void Provider_returns_typed_dump_suggestion_with_the_member_range()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: { tokenize: () => [[{ offset: 0, type: "identifier.ts" }, { offset: 3, type: "delimiter.ts" }]] },
                        languages: { CompletionItemKind: { Method: 7 } },
                      };
                      const model = {
                        getValue: () => "obj.du",
                        getLineContent: () => "obj.du",
                        getWordUntilPosition: () => ({ startColumn: 5, endColumn: 7 }),
                      };
                      const provider = DuetsPadDumpCompletion.createCompletionItemProvider({ monaco });
                      const result = provider.provideCompletionItems(
                        model,
                        { lineNumber: 1, column: 7 },
                        { triggerKind: 0 },
                        { isCancellationRequested: false },
                      );
                      const item = result.suggestions[0];
                      return provider.triggerCharacters[0] === "."
                        && provider.provideCompletionItems.length === 4
                        && result.suggestions.length === 1
                        && item.label === "dump"
                        && item.kind === 7
                        && item.insertText === "dump()"
                        && item.range.startColumn === 5
                        && item.range.endColumn === 7;
                    })()
                    """
                )
                .AsBoolean()
        );
    }

    [Fact]
    public void Provider_honors_cancellation()
    {
        using var engine = CreateEngine();

        Assert.True(
            engine
                .Evaluate(
                    """
                    (() => {
                      const monaco = {
                        editor: { tokenize: () => { throw new Error("must not tokenize"); } },
                        languages: { CompletionItemKind: { Method: 7 } },
                      };
                      const provider = DuetsPadDumpCompletion.createCompletionItemProvider({ monaco });
                      const result = provider.provideCompletionItems(
                        {},
                        { lineNumber: 1, column: 1 },
                        { triggerKind: 0 },
                        { isCancellationRequested: true },
                      );
                      return result.suggestions.length === 0;
                    })()
                    """
                )
                .AsBoolean()
        );
    }
}
