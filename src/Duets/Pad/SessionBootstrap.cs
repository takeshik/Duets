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

        // Bind canvases, canvas, ui, and pad globals.
        var canvasesGlobal = new CanvasesGlobal(padSession);
        duetsSession.SetValue("canvases", canvasesGlobal);
        duetsSession.SetValue("canvas", canvasesGlobal.Get("default"));
        duetsSession.SetValue("ui", new UIGlobal(renderer, padSession.DumpOptions, padSession));
        duetsSession.SetValue("pad", new PadGlobal(padSession));

        // Register per-session d.ts declarations for canvases, canvas, ui, dump, and pad.
        duetsSession.Declarations.RegisterDeclaration(
            """
            // DuetsPad per-session globals

            interface DuetsPadCanvas {
                /** Renders value and appends it as a new child of the canvas root. */
                add(value: any): void;
                /** Renders value and replaces all canvas children with it. */
                set(value: any): void;
                /** Clears all canvas children. */
                clear(): void;
            }

            /** The default canvas. Equivalent to `canvases.get("default")`. */
            declare const canvas: DuetsPadCanvas;

            /**
             * Named canvas collection. Use `canvases.get(name)` to obtain a canvas by name.
             * The first call for a given name creates the canvas; subsequent calls return the same instance.
             */
            declare const canvases: {
                /** Returns the canvas with the given name, creating it on first access. */
                get(name: string): DuetsPadCanvas;
            };

            /**
             * A mutable display handle. Place it once (e.g. `canvas.add(slot)`) and reassign
             * `slot.content` to update the rendered output in place, from anywhere a later run reaches.
             */
            interface DuetsPadSlot {
                /** The current content. Reassigning re-renders every placement of this slot in place. */
                content: any;
            }

            declare const ui: {
                /** Returns a raw-HTML escape-hatch node (use sparingly). */
                rawHtml(content: string): any;
                /** Returns a mutable slot whose `content` can be reassigned to update the display in place. */
                slot(initial?: any): DuetsPadSlot;
                /** Builds a structured element node. */
                element(tag: string, attributes?: any, children?: any[]): any;
                /** Returns a plain text node. */
                text(value: string): any;
                /** Returns a <span class="duetspad-label"> wrapping value. */
                label(value: string): any;
                /** Returns a Tabler <span class="badge"> wrapping text. */
                badge(text: string, options?: { color?: string; pill?: boolean; outline?: boolean }): any;
                /** Returns a Tabler alert with an optional title. */
                alert(message: string, options?: { variant?: "success" | "danger" | "warning" | "info"; title?: string }): any;
                /** Returns a Tabler spinner. */
                spinner(options?: { color?: string; small?: boolean }): any;
                /** Returns a Tabler status indicator wrapping text. */
                status(text: string, options?: { color?: string; animated?: boolean }): any;
                /** Returns a Tabler icon by icon name, for example "check" or "alert-triangle". */
                icon(name: string, options?: { size?: number; color?: string }): any;
                /** Returns a Tabler progress bar for a value between 0 and 100. */
                progress(value: number, options?: { color?: string; label?: string }): any;
                /** Returns a stack container. Direction defaults to "vertical". */
                stack(children?: any[], options?: { direction?: "vertical" | "horizontal" }): any;
                /** Returns a Tabler card with an optional title header and footer. */
                card(children?: any[], options?: { title?: string; footer?: string; color?: string }): any;
                /** Returns a Bootstrap/Tabler grid row container. */
                row(children?: any[], options?: { gutter?: "sm" | "md" | "lg" | number }): any;
                /** Returns a Bootstrap/Tabler grid column. Omit all spans for auto equal-width. */
                col(children?: any[], options?: { span?: number; sm?: number; md?: number; lg?: number; xl?: number }): any;
                /** Returns a horizontal divider. Pass text for a labeled divider. */
                divider(options?: { text?: string; color?: string }): any;
                /** Returns a link. Pass a URL string to navigate, or a handler function for an action link. */
                link(text: string, urlOrHandler: string | (() => void), options?: { title?: string }): any;
                /** Returns a button with a click handler. */
                button(label: string, handler: () => void, options?: { disabled?: boolean; title?: string; className?: string }): any;
                /** Builds a <table class="duetspad-table"> from rows. */
                table(rows: any[], options?: { columns?: string[] }): any;
            };

            declare const pad: {
                /** Resets the current session (engine + canvas + timeline). Eventually-consistent: takes effect after the current run completes. */
                resetSession(): void;
                /** Opens a new tab with the given text handed off as the initial content. */
                openText(text: string): void;
                /** Replaces the editor content with the given text. */
                setEditorText(text: string): void;
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
