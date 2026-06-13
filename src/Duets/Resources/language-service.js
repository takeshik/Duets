var $$host;
// biome-ignore lint/correctness/noUnusedVariables: Retrieved by TypeScriptService after initialization.
var $$service;

(() => {
  // In-memory virtual file system: fileName -> { version, content }
  var _files = {};

  $$host = {
    // Logging (optional no-ops)
    log: (_) => {},
    trace: (_) => {},
    error: (_) => {},

    // Compiler settings
    getCompilationSettings: () => ({
      allowJs: true,
      checkJs: false,
      skipLibCheck: true,
      target: ts.ScriptTarget.ESNext,
      module: ts.ModuleKind.None,
    }),

    // Source file registry
    getScriptFileNames: () => Object.keys(_files),

    // Version string must change when content changes so the language
    // service knows to rebuild its internal program.
    getScriptVersion: (fileName) =>
      _files[fileName] ? String(_files[fileName].version) : "0",

    getScriptSnapshot: (fileName) => {
      if (!Object.hasOwn(_files, fileName)) {
        return undefined;
      }
      return ts.ScriptSnapshot.fromString(_files[fileName].content);
    },

    // Path helpers
    getCurrentDirectory: () => "",

    // lib.es5.d.ts is injected by TypeScriptService after language service initialization.
    getDefaultLibFileName: (_) => "lib.es5.d.ts",

    useCaseSensitiveFileNames: () => false,

    realpath: (path) => path,

    // File system (required by ModuleResolutionHost)
    fileExists: (fileName) => Object.hasOwn(_files, fileName),

    readFile: (fileName) =>
      _files[fileName] ? _files[fileName].content : undefined,

    directoryExists: (_) => false,

    getDirectories: (_) => [],

    // Helper used by C# code to register virtual files
    addFile: (fileName, content) => {
      if (Object.hasOwn(_files, fileName)) {
        _files[fileName].version++;
        _files[fileName].content = content;
      } else {
        _files[fileName] = { version: 1, content: content };
      }
    },
  };

  $$service = ts.createLanguageService($$host);
})();
