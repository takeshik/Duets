var $$host;
var $$service;

(function () {
    // In-memory virtual file system: fileName -> { version, content }
    var _files = {};

    $$host = {
        // ── Logging (optional no-ops) ────────────────────────────────────────
        log: function (_) {},
        trace: function (_) {},
        error: function (_) {},

        // ── Compiler settings ────────────────────────────────────────────────
        getCompilationSettings: function () {
            return {
                allowJs: true,
                checkJs: false,
                skipLibCheck: true,
                target: ts.ScriptTarget.ESNext,
                module: ts.ModuleKind.None,
            };
        },

        // ── Source file registry ─────────────────────────────────────────────
        getScriptFileNames: function () {
            return Object.keys(_files);
        },

        // Version string must change when content changes so the language
        // service knows to rebuild its internal program.
        getScriptVersion: function (fileName) {
            return _files[fileName] ? String(_files[fileName].version) : "0";
        },

        getScriptSnapshot: function (fileName) {
            if (!Object.prototype.hasOwnProperty.call(_files, fileName)) {
                return undefined;
            }
            return ts.ScriptSnapshot.fromString(_files[fileName].content);
        },

        // ── Path helpers ─────────────────────────────────────────────────────
        getCurrentDirectory: function () { return ""; },

        // lib.es5.d.ts is injected by TypeScriptService after language service initialization.
        getDefaultLibFileName: function (_) { return "lib.es5.d.ts"; },

        useCaseSensitiveFileNames: function () { return false; },

        realpath: function (path) { return path; },

        // ── File system (required by ModuleResolutionHost) ───────────────────
        fileExists: function (fileName) {
            return Object.prototype.hasOwnProperty.call(_files, fileName);
        },

        readFile: function (fileName) {
            return _files[fileName] ? _files[fileName].content : undefined;
        },

        directoryExists: function (_) { return false; },

        getDirectories: function (_) { return []; },

        // ── Helper used by C# code to register virtual files ─────────────────
        addFile: function (fileName, content) {
            if (Object.prototype.hasOwnProperty.call(_files, fileName)) {
                _files[fileName].version++;
                _files[fileName].content = content;
            } else {
                _files[fileName] = { version: 1, content: content };
            }
        },
    };

    $$service = ts.createLanguageService($$host);
}());
