using Duets.Pad.Rendering;

namespace Duets.Pad;

/// <summary>
/// Performs one-time JavaScript-environment wiring for a new DuetsPad session.
/// </summary>
/// <remarks>
/// This class is a pure construction-time helper: it binds host objects, defines
/// JS globals, and registers the per-session <c>.d.ts</c> declarations into the
/// underlying <see cref="DuetsSession"/>. It does not retain session state, locks,
/// or subscribers after <see cref="Bootstrap"/> returns.
/// </remarks>
internal static class SessionBootstrap
{
    /// <summary>
    /// Wires the JS environment for <paramref name="padSession"/> into its
    /// underlying <see cref="DuetsSession"/>. Must be called once during construction,
    /// before the session is exposed to callers.
    /// </summary>
    /// <param name="padSession">
    /// The owning <see cref="DuetsPadSession"/>. Used to bind <c>canvas</c> and to wire the
    /// <c>dump</c> delegate.
    /// </param>
    /// <param name="renderer">
    /// The session's <see cref="DisplayRenderer"/>, captured at construction time and bound to the
    /// <c>ui</c> host object. Immutable for the session lifetime.
    /// </param>
    internal static void Bootstrap(DuetsPadSession padSession, DisplayRenderer renderer)
    {
        var duetsSession = padSession.DuetsSession;

        // Subscribe to console output — runs synchronously on the eval thread.
        duetsSession.ConsoleLogged += padSession.OnConsoleLogged;

        // Bind __padDump__ and define the dump global in JS.
        // Core (ScriptEngineInit.js) does not define dump; DuetsPad owns it.
        // The second argument is a JS options object; DumpOptionsResolver.Merge reads maxDepth/maxItems from it.
        duetsSession.SetValue(
            "__padDump__",
            new Action<object?, object?>(
                (v, opts) =>
                    padSession.Dump(v, DumpOptionsResolver.Merge(padSession.DumpOptions, opts))
            )
        );
        duetsSession.Execute("var dump = function (v, opts) { __padDump__(v, opts); return v; };");

        // Bind canvas and ui globals.
        duetsSession.SetValue("canvas", new CanvasApi(padSession));
        duetsSession.SetValue("ui", new UiApi(renderer, padSession.DumpOptions));

        // Register per-session d.ts declarations for canvas, ui, and dump.
        duetsSession.Declarations.RegisterDeclaration(
            """
            // DuetsPad per-session globals
            declare const canvas: {
                /** Renders value and appends it as a new child of the canvas root. */
                add(value: any): void;
                /** Renders value and replaces all canvas children with it. */
                set(value: any): void;
                /** Clears all canvas children. */
                clear(): void;
            };

            declare const ui: {
                /** Returns a raw-HTML escape-hatch node (use sparingly). */
                rawHtml(content: string): any;
                /** Builds a structured element node. */
                element(tag: string, attributes?: any, children?: any[]): any;
                /** Returns a plain text node. */
                text(value: string): any;
                /** Returns a <span class="duetspad-label"> wrapping value. */
                label(value: string): any;
                /** Returns a <div class="duetspad-stack"> containing rendered children. */
                stack(children?: any[]): any;
                /** Returns a button with a click handler. */
                button(label: string, handler: () => void, options?: { disabled?: boolean; title?: string; className?: string }): any;
                /** Builds a <table class="duetspad-table"> from rows. */
                table(rows: any[], options?: { columns?: string[] }): any;
            };

            /**
             * Renders value to the DuetsPad Timeline and returns it unchanged,
             * so it can be inserted anywhere in an expression chain without breaking it.
             *
             * ```ts
             * dump(someArray)          // renders the array, returns it
             * dump(obj).someProperty   // renders obj, then accesses .someProperty — type is preserved
             * ```
             */
            declare function dump<T>(value: T, opts?: { maxDepth?: number; maxItems?: number }): T;
            """
        );
    }
}
