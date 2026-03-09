const $$host = new (function () {
    this._files = {};
    this.log = function (_) {
    };
    this.trace = function (_) {
    };
    this.error = function (_) {
    };
    this.getCompilationSettings = ts.getDefaultCompilerOptions;
    this.getScriptIsOpen = function (_) {
        return true;
    };
    this.getCurrentDirectory = function () {
        return "";
    };
    this.getDefaultLibFileName = function (_) {
        return "lib";
    };
    this.getScriptVersion = function (fileName) {
        return this._files[fileName] ? this._files[fileName].ver.toString() : "0";
    };
    this.getScriptSnapshot = function (fileName) {
        return this._files[fileName] ? this._files[fileName].snap : undefined;
    };
    this.getScriptFileNames = function () {
        return Object.keys(this._files);
    };
    this.addFile = function (fileName, body) {
        var snap = ts.ScriptSnapshot.fromString(body);
        snap.getChangeRange = function (_) {
            return undefined;
        };
        if (this._files[fileName]) {
            this._files[fileName].ver++;
            this._files[fileName].snap = snap;
        } else {
            this._files[fileName] = {ver: 1, snap: snap};
        }
    };
})();
const $$service = ts.createLanguageService($$host);
