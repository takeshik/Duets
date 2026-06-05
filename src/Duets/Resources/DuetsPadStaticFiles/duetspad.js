// DuetsPad browser client
// All URL construction is relative to document.baseURI so non-root mounts (e.g. /pad/) work.

(function () {
    'use strict';

    // ── Protocol event-type constants ─────────────────────────────────────────────
    // Single source of truth for SSE event-type discriminators.
    // The string values here are the only place they should appear in this file.

    const PAD_EVENTS = {
        canvasSnapshot: 'canvas.snapshot',
        canvasReplace:  'canvas.replace',
        timelineReset:  'timeline.reset',
        timelineAppend: 'timeline.append',
        timelineUpdate: 'timeline.update',
        timelineTrim:   'timeline.trim',
    };

    // ── URL helpers ──────────────────────────────────────────────────────────────

    function padUrl(path) {
        return new URL(path, document.baseURI).href;
    }

    // ── Session bootstrap ─────────────────────────────────────────────────────────
    // Reads sessionId from sessionStorage; POSTs to /sessions to reuse a live session
    // or obtain a fresh one; stores the returned id back into sessionStorage.

    let sessionId = null;

    async function initSession() {
        const stored = sessionStorage.getItem('duetspad.sessionId');
        const body = stored ? JSON.stringify({ sessionId: stored }) : '{}';

        const res = await fetch(padUrl('sessions'), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body,
        });

        if (!res.ok) {
            throw new Error('Session bootstrap failed: ' + res.status);
        }

        const data = await res.json();
        sessionId = data.sessionId;
        sessionStorage.setItem('duetspad.sessionId', sessionId);
        return sessionId;
    }

    // ── Render-node projection ────────────────────────────────────────────────────
    // Security-critical: only rawHtml nodes use innerHTML; all other node kinds use
    // textContent or DOM APIs exclusively.
    //   text     → textContent only (never innerHTML)
    //   element  → createElement + setAttribute + recursive children
    //   rawHtml  → innerHTML (ONLY allowed location)
    //   unknown  → visible error marker, never throws

    function projectNode(node) {
        try {
            if (!node || typeof node !== 'object' || !node.kind) {
                return makeUnknownMarker('(null node)');
            }

            switch (node.kind) {
                case 'text': {
                    return document.createTextNode(String(node.value ?? ''));
                }

                case 'element': {
                    const el = document.createElement(node.tag || 'span');
                    if (node.attributes && typeof node.attributes === 'object') {
                        for (const [name, value] of Object.entries(node.attributes)) {
                            // null attribute value → boolean attribute
                            el.setAttribute(name, value !== null ? String(value) : '');
                        }
                    }
                    if (Array.isArray(node.children)) {
                        for (const child of node.children) {
                            el.appendChild(projectNode(child));
                        }
                    }
                    return el;
                }

                case 'rawHtml': {
                    // This is the ONLY place innerHTML is used.
                    const wrapper = document.createElement('div');
                    wrapper.innerHTML = node.content ?? '';
                    return wrapper;
                }

                default: {
                    return makeUnknownMarker('unknown kind: ' + node.kind);
                }
            }
        } catch (err) {
            return makeUnknownMarker('render error: ' + err);
        }
    }

    function makeUnknownMarker(msg) {
        const el = document.createElement('span');
        el.style.cssText = 'color:#f48771;font-style:italic;font-size:11px';
        el.textContent = '[' + msg + ']';
        return el;
    }

    // ── Eval helper ───────────────────────────────────────────────────────────────

    async function evalCode(code, immediate) {
        var url = padUrl('sessions/' + sessionId + '/eval');
        if (immediate) {
            url = url + '?source=immediate';
        }
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'text/plain' },
            body: code,
        });
        return res.json();
    }

    // ── Status display helpers ────────────────────────────────────────────────────

    function setEditorStatus(text, isError) {
        const el = document.getElementById('editor-status');
        el.textContent = text;
        el.className = text ? (isError ? 'status-error' : 'status-ok') : '';
    }

    function setImmediateStatus(text, isError) {
        const el = document.getElementById('immediate-status');
        if (!el) return;
        el.textContent = text;
        el.className = text ? (isError ? 'result-error' : 'result-ok') : '';
    }

    // ── Timeline state ────────────────────────────────────────────────────────────
    // Maps entry id (number) → <div class="tl-entry"> node for O(1) replace.

    const timelineEntryMap = new Map();

    function renderTimelineEntry(entry) {
        const row = document.createElement('div');
        row.className = 'tl-entry';
        row.dataset.id = entry.id;

        const reasonEl = document.createElement('div');
        reasonEl.className = 'tl-reason';
        reasonEl.textContent = entry.reason ?? '';

        const bodyEl = document.createElement('div');
        bodyEl.className = 'tl-body';
        bodyEl.appendChild(projectNode(entry.body));

        row.appendChild(reasonEl);
        row.appendChild(bodyEl);
        return row;
    }

    function handleTimelineEvent(msg) {
        const content = document.getElementById('timeline-content');

        switch (msg.type) {
            case PAD_EVENTS.timelineReset: {
                content.textContent = '';
                timelineEntryMap.clear();
                if (Array.isArray(msg.entries)) {
                    for (const entry of msg.entries) {
                        const row = renderTimelineEntry(entry);
                        timelineEntryMap.set(entry.id, row);
                        content.appendChild(row);
                    }
                }
                break;
            }

            case PAD_EVENTS.timelineAppend: {
                const entry = msg.entry;
                const row = renderTimelineEntry(entry);
                timelineEntryMap.set(entry.id, row);
                content.appendChild(row);
                // Scroll to bottom so new entries are visible.
                content.scrollTop = content.scrollHeight;
                break;
            }

            case PAD_EVENTS.timelineUpdate: {
                const entry = msg.entry;
                const existing = timelineEntryMap.get(entry.id);
                const row = renderTimelineEntry(entry);
                timelineEntryMap.set(entry.id, row);
                if (existing && existing.parentNode === content) {
                    content.replaceChild(row, existing);
                } else {
                    content.appendChild(row);
                }
                break;
            }

            case PAD_EVENTS.timelineTrim: {
                const removeBeforeId = msg.removeBeforeId;
                for (const [id, row] of timelineEntryMap) {
                    if (id < removeBeforeId) {
                        if (row.parentNode === content) {
                            content.removeChild(row);
                        }
                        timelineEntryMap.delete(id);
                    }
                }
                if (msg.marker != null) {
                    const existingMarker = timelineEntryMap.get(msg.marker.id);
                    if (existingMarker && existingMarker.parentNode === content) {
                        content.removeChild(existingMarker);
                    }
                    const markerRow = renderTimelineEntry(msg.marker);
                    timelineEntryMap.set(msg.marker.id, markerRow);
                    if (content.firstChild) {
                        content.insertBefore(markerRow, content.firstChild);
                    } else {
                        content.appendChild(markerRow);
                    }
                }
                break;
            }

            default:
                // Unknown event type — ignore silently.
                break;
        }
    }

    // ── Canvas state ──────────────────────────────────────────────────────────────

    function handleCanvasEvent(msg) {
        if (msg.type === PAD_EVENTS.canvasSnapshot || msg.type === PAD_EVENTS.canvasReplace) {
            const content = document.getElementById('canvas-content');
            content.textContent = '';
            content.appendChild(projectNode(msg.state));
        }
    }

    // ── SSE helpers ───────────────────────────────────────────────────────────────

    function openSse(path, handler) {
        const url = padUrl(path);
        const es = new EventSource(url);
        es.onmessage = function (e) {
            try {
                handler(JSON.parse(e.data));
            } catch (err) {
                console.error('[DuetsPad] SSE parse error on ' + path, err);
            }
        };
        es.onerror = function () {
            // EventSource will attempt to reconnect automatically.
        };
        return es;
    }

    // ── Monaco setup ──────────────────────────────────────────────────────────────

    function setupMonaco(id) {
        require.config({ paths: { vs: window.DUETSPAD_MONACO_VS } });

        require(['vs/editor/editor.main'], function () {
            // Compiler options
            monaco.languages.typescript.typescriptDefaults.setCompilerOptions({
                target: monaco.languages.typescript.ScriptTarget.ESNext,
                allowNonTsExtensions: true,
                skipLibCheck: true,
                noImplicitAny: false,
                strictNullChecks: false,
            });

            // Receive type declarations via SSE and register with Monaco.
            // addExtraLib is incremental; the worker is not reset on each call.
            const declEs = new EventSource(
                padUrl('type-declaration-events?sessionId=' + encodeURIComponent(id))
            );
            declEs.onmessage = function (e) {
                try {
                    const decl = JSON.parse(e.data);
                    monaco.languages.typescript.typescriptDefaults.addExtraLib(
                        decl.content,
                        decl.fileName
                    );
                } catch (err) {
                    console.error('[DuetsPad] type-declaration-events parse error', err);
                }
            };

            const editor = monaco.editor.create(document.getElementById('editor-host'), {
                value: '',
                language: 'typescript',
                theme: 'vs-dark',
                automaticLayout: true,
                minimap: { enabled: false },
                scrollBeyondLastLine: false,
                fontSize: 13,
                fontFamily: "Consolas, 'Cascadia Code', monospace",
            });

            // Ctrl/Cmd+Enter — run without clearing status
            editor.addAction({
                id: 'duetspad-run',
                label: 'Run',
                keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter],
                run: async function () {
                    const code = editor.getValue();
                    if (!code.trim()) return;
                    setEditorStatus('Running…', false);
                    try {
                        const data = await evalCode(code);
                        if (data.ok) {
                            setEditorStatus('Run completed', false);
                        } else {
                            setEditorStatus(data.error ?? 'Error', true);
                        }
                    } catch (err) {
                        setEditorStatus(String(err), true);
                    }
                },
            });

            // F5 — run (same as Ctrl+Enter; no output pane to clear in this layout)
            editor.addAction({
                id: 'duetspad-run-f5',
                label: 'Run (F5)',
                keybindings: [monaco.KeyCode.F5],
                run: async function () {
                    const code = editor.getValue();
                    if (!code.trim()) return;
                    setEditorStatus('Running…', false);
                    try {
                        const data = await evalCode(code);
                        if (data.ok) {
                            setEditorStatus('Run completed', false);
                        } else {
                            setEditorStatus(data.error ?? 'Error', true);
                        }
                    } catch (err) {
                        setEditorStatus(String(err), true);
                    }
                },
            });

            // Open canvas and timeline SSE streams.
            openSse('sessions/' + id + '/canvas-events', handleCanvasEvent);
            openSse('sessions/' + id + '/timeline-events', handleTimelineEvent);
        });
    }

    // ── Immediate input ───────────────────────────────────────────────────────────

    function setupImmediate() {
        const input = document.getElementById('immediate-input');
        input.addEventListener('keydown', async function (e) {
            if (e.key !== 'Enter') return;
            const code = input.value.trim();
            if (!code) return;
            setImmediateStatus('Evaluating…', false);
            try {
                const data = await evalCode(code, /*immediate*/ true);
                if (data.ok) {
                    // Result arrives in Timeline via SSE; clear the input and status.
                    input.value = '';
                    setImmediateStatus('', false);
                } else {
                    setImmediateStatus(data.error ?? 'Error', true);
                }
            } catch (err) {
                setImmediateStatus(String(err), true);
            }
        });
    }

    // ── Entry point ───────────────────────────────────────────────────────────────

    async function main() {
        setupImmediate();

        try {
            const id = await initSession();
            setupMonaco(id);
        } catch (err) {
            console.error('[DuetsPad] Startup error', err);
            setEditorStatus('Startup error: ' + err, true);
        }
    }

    main();
})();
