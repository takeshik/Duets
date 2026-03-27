// ScriptEngine built-in definitions
// Executed once on engine initialization before any user code runs.

// Formats a value for display, similar to Node.js util.inspect.
// opts.depth   — maximum nesting depth (default: 2)
// opts.compact — single-line output when true (default: false)
const inspect = function (value, opts) {
    var depth   = opts && opts.depth   !== undefined ? opts.depth   : 2;
    var compact = opts && opts.compact !== undefined ? opts.compact : false;

    var seen = [];

    function sanitize(val, d) {
        if (val === null || val === undefined) return val;
        switch (typeof val) {
            case 'boolean':
            case 'number':   return val;
            case 'string':   return val;
            case 'bigint':   return String(val) + 'n';
            case 'symbol':   return val.toString();
            case 'function': return '[Function: ' + (val.name || '(anonymous)') + ']';
        }
        // object
        if (seen.indexOf(val) !== -1) return '[Circular]';
        if (d <= 0) return Array.isArray(val) ? '[Array]' : '[Object]';
        seen.push(val);
        var d1 = d - 1;
        if (val instanceof Date)   return val; // JSON.stringify calls .toISOString()
        if (val instanceof RegExp) return String(val);
        if (val instanceof Error)  return val.name + ': ' + val.message;
        // Unwrap boxed primitives (e.g. new Number(42) → 42)
        var prim = val.valueOf();
        if (typeof prim !== 'object') return prim;
        if (typeof Map !== 'undefined' && val instanceof Map) {
            var pairs = {};
            val.forEach(function (v, k) { pairs[String(k)] = sanitize(v, d1); });
            return pairs;
        }
        if (typeof Set !== 'undefined' && val instanceof Set) {
            var items = [];
            val.forEach(function (v) { items.push(sanitize(v, d1)); });
            return items;
        }
        if (Array.isArray(val)) return val.map(function (v) { return sanitize(v, d1); });
        var out = {};
        Object.keys(val).forEach(function (k) { out[k] = sanitize(val[k], d1); });
        return out;
    }

    if (value === undefined) return 'undefined';
    if (typeof value === 'bigint')   return String(value) + 'n';
    if (typeof value === 'symbol')   return value.toString();
    if (typeof value === 'function') return '[Function: ' + (value.name || '(anonymous)') + ']';

    var result = JSON.stringify(sanitize(value, depth), null, compact ? undefined : 2);
    return result !== undefined ? result : String(value);
}

// util.inspect: returns a formatted string (use when you need the string itself).
var util = Object.freeze({
    inspect: function (value, opts) { return inspect(value, opts); },
});

var console = (() => {
    // Top-level strings are not quoted (matching console.log behavior);
    // non-strings are formatted with inspect.
    const fmt = args => Array.from(args).map(a =>
        typeof a === 'string' ? a : inspect(a)
    ).join(' ');
    return {
        log:   (...args) => __consoleImpl__('log',   fmt(args)),
        warn:  (...args) => __consoleImpl__('warn',  fmt(args)),
        error: (...args) => __consoleImpl__('error', fmt(args)),
        info:  (...args) => __consoleImpl__('info',  fmt(args)),
        debug: (...args) => __consoleImpl__('debug', fmt(args)),
    };
})();
