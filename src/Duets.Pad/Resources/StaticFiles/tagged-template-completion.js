// DuetsPad tagged-template completion helper.

((root, factory) => {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
  root.DuetsPadTaggedTemplateCompletion = api;
})(globalThis, () => {
  const TAG_PATTERN = /([A-Za-z_][A-Za-z0-9_]*)\s*$/;

  function isTemplateToken(type) {
    return typeof type === "string" && type.includes("string");
  }

  function tokenAt(monaco, source, position) {
    const lines = monaco.editor.tokenize(source, "typescript");
    const lineTokens = lines[position.lineNumber - 1] ?? [];
    const columnOffset = position.column - 1;
    let current = null;
    for (const token of lineTokens) {
      if (token.offset <= columnOffset) {
        current = token;
      } else {
        break;
      }
    }
    return current;
  }

  function findContext(monaco, model, position, registeredTags) {
    const source = model.getValue();
    const caretOffset = model.getOffsetAt(position);
    const token = tokenAt(monaco, source, position);
    if (!isTemplateToken(token?.type)) {
      return null;
    }

    const templateStart = source.lastIndexOf("`", Math.max(0, caretOffset - 1));
    const templateEnd = source.indexOf("`", caretOffset);
    if (templateStart < 0 || templateEnd < caretOffset) {
      return null;
    }

    const beforeTemplate = source.slice(0, templateStart);
    const tagMatch = TAG_PATTERN.exec(beforeTemplate);
    if (!tagMatch) {
      return null;
    }

    const tag = tagMatch[1];
    if (!registeredTags.has(tag)) {
      return null;
    }

    const tagStart = tagMatch.index;
    const directPrevious = beforeTemplate[tagStart - 1];
    if (/[A-Za-z0-9_]/.test(directPrevious ?? "")) {
      return null;
    }

    const prefixBeforeTag = beforeTemplate.slice(0, tagStart).trimEnd();
    const previous = prefixBeforeTag[prefixBeforeTag.length - 1];
    if (previous === "." || previous === "#") {
      return null;
    }

    const segmentStart = templateStart + 1;
    const currentSegmentRaw = source.slice(segmentStart, templateEnd);
    if (currentSegmentRaw.includes("${")) {
      return null;
    }

    const caretOffsetInSegment = caretOffset - segmentStart;
    return {
      request: {
        tag,
        textBeforeCaret: currentSegmentRaw.slice(0, caretOffsetInSegment),
        textAfterCaret: currentSegmentRaw.slice(caretOffsetInSegment),
        currentSegmentRaw,
        caretOffsetInSegment,
        segmentIndex: 0,
        rawSegments: [currentSegmentRaw],
      },
      segmentStartOffset: segmentStart,
    };
  }

  function completionKind(monaco, kind) {
    switch (kind) {
      case "File":
        return monaco.languages.CompletionItemKind.File;
      case "Folder":
        return monaco.languages.CompletionItemKind.Folder;
      case "Member":
        return monaco.languages.CompletionItemKind.Property;
      default:
        return monaco.languages.CompletionItemKind.Value;
    }
  }

  function itemRange(model, context, span) {
    if (!span) {
      const offset =
        context.segmentStartOffset + context.request.caretOffsetInSegment;
      const position = model.getPositionAt(offset);
      return {
        startLineNumber: position.lineNumber,
        startColumn: position.column,
        endLineNumber: position.lineNumber,
        endColumn: position.column,
      };
    }

    const start = model.getPositionAt(context.segmentStartOffset + span.start);
    const end = model.getPositionAt(
      context.segmentStartOffset + span.start + span.length,
    );
    return {
      startLineNumber: start.lineNumber,
      startColumn: start.column,
      endLineNumber: end.lineNumber,
      endColumn: end.column,
    };
  }

  function createCompletionItemProvider({
    monaco,
    getTags,
    requestCompletions,
  }) {
    return {
      triggerCharacters: ["/", ".", "-", "_"],
      async provideCompletionItems(
        model,
        position,
        _context,
        cancellationToken,
      ) {
        const context = findContext(monaco, model, position, getTags());
        if (!context || cancellationToken.isCancellationRequested) {
          return { suggestions: [] };
        }

        const response = await requestCompletions(
          context.request,
          cancellationToken,
        );
        if (!response?.ok || cancellationToken.isCancellationRequested) {
          return { suggestions: [] };
        }

        return {
          suggestions: (response.items ?? []).map((item) => ({
            label: item.label,
            kind: completionKind(monaco, item.kind),
            insertText: item.insertText ?? item.label,
            range: itemRange(model, context, item.replacementSpan),
            filterText: item.filterText ?? undefined,
            sortText: item.sortText ?? undefined,
            detail: item.detail ?? undefined,
            documentation: item.documentation ?? undefined,
          })),
        };
      },
    };
  }

  return {
    findContext,
    createCompletionItemProvider,
  };
});
