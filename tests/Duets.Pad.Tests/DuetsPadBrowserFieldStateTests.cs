using System.Text;
using Acornima;
using Acornima.Ast;
using Jint;

namespace Duets.Pad.Tests;

public sealed class DuetsPadBrowserFieldStateTests
{
    private static string LoadScript()
    {
        using var stream = typeof(DuetsPadService).Assembly.GetManifestResourceStream(
            "Duets.Pad.Resources.StaticFiles.duetspad.js"
        );
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Engine CreateEngine()
    {
        var source = LoadScript();
        var functions = string.Join(
            '\n',
            ExtractFunction(source, "isFieldGuarded"),
            ExtractFunction(source, "applyFieldLiveValue"),
            ExtractFunction(source, "fieldCurrentValue"),
            ExtractFunction(source, "fieldElements"),
            ExtractFunction(source, "captureFieldEdits"),
            ExtractFunction(source, "restoreFieldEdits")
        );

        return new Engine().Execute(
            $$"""
            class HTMLElement {
              constructor() {
                this.attributes = {};
                this.dataset = {};
                this.value = "";
                this.checked = false;
                this.selectionStart = 0;
                this.selectionEnd = 0;
                this.selectionDirection = "none";
              }
              getAttribute(name) { return this.attributes[name] ?? null; }
              hasAttribute(name) { return Object.hasOwn(this.attributes, name); }
              focus() { document.activeElement = this; }
              setSelectionRange(start, end, direction) {
                this.selectionStart = start;
                this.selectionEnd = end;
                this.selectionDirection = direction ?? "none";
              }
            }
            const document = { activeElement: null };
            {{functions}}
            """
        );
    }

    private static string ExtractFunction(string source, string name)
    {
        var script = new Parser().ParseScript(source);
        FunctionDeclaration? match = null;
        foreach (var node in DescendantsAndSelf(script))
        {
            if (node is not FunctionDeclaration function || function.Id?.Name != name)
            {
                continue;
            }

            Assert.Null(match);
            match = function;
        }

        Assert.NotNull(match);
        return source[match.Range.Start..match.Range.End];
    }

    private static IEnumerable<Node> DescendantsAndSelf(Node node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void Function_extraction_uses_javascript_syntax_boundaries()
    {
        const string source = """
            (() => {
            function target() {
              const braces = "} {";
              const matcher = /[{}]/;
              return braces.replace(matcher, "");
            }
            function next() { return "next"; }
            })();
            """;

        var function = ExtractFunction(source, "target");
        using var engine = new Engine().Execute(function);

        Assert.Equal(" {", engine.Evaluate("target()").AsString());
    }

    [Fact]
    public void Authoritative_value_discards_pending_input_and_advances_generation()
    {
        using var engine = CreateEngine();

        var result = engine.Evaluate(
            """
            (() => {
              const field = new HTMLElement();
              field.attributes["data-duetspad-field-kind"] = "text";
              field.attributes.value = "canonical";
              field.value = "uncommitted";
              field.dataset.duetspadPending = "1";
              field.dataset.duetspadEditGen = "4";

              applyFieldLiveValue(field, { authoritative: true });

              return field.value === "canonical"
                && field.dataset.duetspadPending === undefined
                && field.dataset.duetspadEditGen === "5";
            })()
            """
        );

        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void Older_commit_response_cannot_clear_edit_started_after_authoritative_value()
    {
        using var engine = CreateEngine();

        var result = engine.Evaluate(
            """
            (() => {
              const field = new HTMLElement();
              field.attributes["data-duetspad-field-kind"] = "text";
              field.attributes.value = "canonical";
              field.dataset.duetspadPending = "1";
              field.dataset.duetspadEditGen = "4";
              const oldCommitGeneration = field.dataset.duetspadEditGen;

              applyFieldLiveValue(field, { authoritative: true });
              field.dataset.duetspadPending = "1";
              field.dataset.duetspadEditGen = String(
                (Number(field.dataset.duetspadEditGen) || 0) + 1,
              );

              if (field.dataset.duetspadEditGen === oldCommitGeneration) {
                delete field.dataset.duetspadPending;
              }

              return field.dataset.duetspadPending === "1"
                && field.dataset.duetspadEditGen === "6";
            })()
            """
        );

        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void Timeline_update_discards_only_the_authoritative_field_and_restores_other_edits()
    {
        using var engine = CreateEngine();

        var result = engine.Evaluate(
            """
            (() => {
              const targetId = "11111111-1111-1111-1111-111111111111";
              const otherId = "22222222-2222-2222-2222-222222222222";
              const createField = (id, value, pending) => {
                const field = new HTMLElement();
                field.attributes["data-duetspad-field"] = id;
                field.attributes["data-duetspad-field-kind"] = "text";
                field.attributes.value = value;
                field.value = value;
                field.dataset.duetspadEditGen = "3";
                if (pending) field.dataset.duetspadPending = "1";
                return field;
              };
              const root = (fields) => ({
                querySelectorAll: () => fields,
                matches: () => false,
              });

              const oldTarget = createField(targetId, "local target", true);
              const oldOther = createField(otherId, "local other", true);
              oldOther.selectionStart = 2;
              oldOther.selectionEnd = 5;
              document.activeElement = oldOther;
              const edits = captureFieldEdits(root([oldTarget, oldOther]), targetId);

              const newTarget = createField(targetId, "server target", false);
              const newOther = createField(otherId, "server other", false);
              restoreFieldEdits(root([newTarget, newOther]), edits);

              return [
                newTarget.value === "server target",
                newTarget.dataset.duetspadPending === undefined,
                newOther.value === "local other",
                newOther.dataset.duetspadPending === "1",
                document.activeElement === newOther,
                newOther.selectionStart === 2,
                newOther.selectionEnd === 5,
              ].join(",");
            })()
            """
        );

        Assert.Equal("true,true,true,true,true,true,true", result.AsString());
    }
}
