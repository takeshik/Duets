((root, factory) => {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
  root.DuetsPadDumpCompletion = api;
})(globalThis, () => {
  function isNonCodeToken(type) {
    return (
      typeof type === "string" &&
      (type.includes("comment") ||
        type.includes("string") ||
        type.includes("regexp"))
    );
  }

  function isNumericToken(type) {
    return typeof type === "string" && type.includes("number");
  }

  function tokenAt(lines, position) {
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

  /**
   * Finds the Monaco replacement range for a member completion immediately following a dot.
   * Returns null in comments, strings, regular expressions, and non-member contexts.
   */
  function findCompletionRange(monaco, model, position) {
    const word = model.getWordUntilPosition(position);
    const line = model.getLineContent(position.lineNumber);
    if (word.startColumn < 2 || line[word.startColumn - 2] !== ".") {
      return null;
    }

    const memberDotOffset = word.startColumn - 2;
    if (
      line[memberDotOffset - 1] === "." &&
      line[memberDotOffset - 2] === "."
    ) {
      return null;
    }

    const lines = monaco.editor.tokenize(model.getValue(), "typescript");
    const token = tokenAt(lines, position);
    if (isNonCodeToken(token?.type)) {
      return null;
    }

    const memberDotToken = tokenAt(lines, {
      lineNumber: position.lineNumber,
      column: word.startColumn - 1,
    });
    if (isNumericToken(memberDotToken?.type)) {
      return null;
    }

    return {
      startLineNumber: position.lineNumber,
      startColumn: word.startColumn,
      endLineNumber: position.lineNumber,
      endColumn: word.endColumn,
    };
  }

  /** Creates the Monaco completion provider for DuetsPad's fluent dump method. */
  function createCompletionItemProvider({ monaco }) {
    return {
      triggerCharacters: ["."],
      provideCompletionItems(model, position, _context, cancellationToken) {
        if (cancellationToken?.isCancellationRequested) {
          return { suggestions: [] };
        }

        const range = findCompletionRange(monaco, model, position);
        if (!range) {
          return { suggestions: [] };
        }

        return {
          suggestions: [
            {
              label: "dump",
              kind: monaco.languages.CompletionItemKind.Method,
              insertText: "dump()",
              filterText: "dump",
              sortText: "00_dump",
              detail: "DuetsPad",
              documentation:
                "Render this value to the Timeline and return it unchanged.",
              range,
            },
          ],
        };
      },
    };
  }

  return {
    findCompletionRange,
    createCompletionItemProvider,
  };
});
