// DuetsPad browser client
// All URL construction is relative to document.baseURI so non-root mounts (e.g. /pad/) work.

(() => {
  // ── Protocol event-type constants ─────────────────────────────────────────────
  // Single source of truth for SSE event-type discriminators.
  // The string values here are the only place they should appear in this file.

  const PAD_EVENTS = {
    canvasSnapshot: "canvas.snapshot",
    canvasReplace: "canvas.replace",
    timelineReset: "timeline.reset",
    timelineAppend: "timeline.append",
    timelineUpdate: "timeline.update",
    timelineTrim: "timeline.trim",
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
    const stored = sessionStorage.getItem("duetspad.sessionId");
    const body = stored ? JSON.stringify({ sessionId: stored }) : "{}";

    const res = await fetch(padUrl("sessions"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body,
    });

    if (!res.ok) {
      throw new Error(`Session bootstrap failed: ${res.status}`);
    }

    const data = await res.json();
    sessionId = data.sessionId;
    sessionStorage.setItem("duetspad.sessionId", sessionId);
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
      if (!node || typeof node !== "object" || !node.kind) {
        return makeUnknownMarker("(null node)");
      }

      switch (node.kind) {
        case "text": {
          return document.createTextNode(String(node.value ?? ""));
        }

        case "element": {
          const el = document.createElement(node.tag || "span");
          if (node.attributes && typeof node.attributes === "object") {
            for (const [name, value] of Object.entries(node.attributes)) {
              // null attribute value → boolean attribute
              el.setAttribute(name, value !== null ? String(value) : "");
            }
          }
          if (Array.isArray(node.children)) {
            for (const child of node.children) {
              el.appendChild(projectNode(child));
            }
          }
          return el;
        }

        case "rawHtml": {
          // This is the ONLY place innerHTML is used.
          const wrapper = document.createElement("div");
          wrapper.innerHTML = node.content ?? "";
          return wrapper;
        }

        default: {
          return makeUnknownMarker(`unknown kind: ${node.kind}`);
        }
      }
    } catch (err) {
      return makeUnknownMarker(`render error: ${err}`);
    }
  }

  function makeUnknownMarker(msg) {
    const el = document.createElement("span");
    el.style.cssText = "color:#f48771;font-style:italic;font-size:11px";
    el.textContent = `[${msg}]`;
    return el;
  }

  // ── Eval helper ───────────────────────────────────────────────────────────────

  async function evalCode(code, immediate) {
    let url = padUrl(`sessions/${sessionId}/eval`);
    if (immediate) {
      url = `${url}?source=immediate`;
    }
    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "text/plain" },
      body: code,
    });
    return res.json();
  }

  // ── Status display helpers ────────────────────────────────────────────────────

  function setEditorStatus(text, isError) {
    const el = document.getElementById("editor-status");
    if (!el) return;
    el.textContent = text;
    el.className = text ? (isError ? "status-error" : "status-ok") : "";
  }

  function setSessionStatus(connected) {
    const el = document.getElementById("session-status");
    if (!el) return;
    const dot =
      el.querySelector(".status-dot") ?? document.createElement("span");
    dot.className = connected ? "status-dot status-dot-animated" : "status-dot";
    if (!el.contains(dot)) {
      el.prepend(dot);
    }
    // Replace text node (last child) with updated label.
    const label = connected ? "connected" : "disconnected";
    const lastChild = el.lastChild;
    if (lastChild && lastChild.nodeType === Node.TEXT_NODE) {
      lastChild.textContent = label;
    } else {
      el.appendChild(document.createTextNode(label));
    }
    el.className = `status ${connected ? "status-green" : "status-red"} session-status`;
    el.title = connected ? "Session connected" : "Session disconnected";
  }

  // ── Module-scoped editor reference ────────────────────────────────────────────
  // Assigned once Monaco has created the editor; null before that point.

  let activeEditor = null;

  // ── Run current editor content ────────────────────────────────────────────────

  async function runCurrent() {
    if (!activeEditor) return;
    const code = activeEditor.getValue();
    if (!code.trim()) return;
    setEditorStatus("Running…", false);
    try {
      const data = await evalCode(code);
      if (data.ok) {
        setEditorStatus("Run completed", false);
      } else {
        setEditorStatus(data.error ?? "Error", true);
      }
    } catch (err) {
      setEditorStatus(String(err), true);
    }
  }

  // ── Timeline state ────────────────────────────────────────────────────────────
  // Maps entry id (number) → <div class="timeline-entry"> node for O(1) replace.

  const timelineEntryMap = new Map();

  function resetTimeline() {
    const content = document.getElementById("timeline-content");
    if (content) content.textContent = "";
    timelineEntryMap.clear();
  }

  function renderTimelineEntry(entry) {
    const row = document.createElement("div");
    row.className = "timeline-entry";
    row.dataset.id = entry.id;

    const reasonEl = document.createElement("div");
    reasonEl.className = "timeline-reason";
    reasonEl.textContent = entry.reason ?? "";

    if (entry.timestamp) {
      const tsEl = document.createElement("span");
      tsEl.className = "timeline-timestamp";
      tsEl.textContent = new Date(entry.timestamp).toLocaleTimeString();
      tsEl.title = entry.timestamp;
      reasonEl.appendChild(tsEl);
    }

    const bodyEl = document.createElement("div");
    bodyEl.className = "timeline-body";
    bodyEl.appendChild(projectNode(entry.body));

    row.appendChild(reasonEl);
    row.appendChild(bodyEl);
    return row;
  }

  function handleTimelineEvent(msg) {
    const content = document.getElementById("timeline-content");
    if (!content) return;

    switch (msg.type) {
      case PAD_EVENTS.timelineReset: {
        resetTimeline();
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
        if (!entry) break;
        const row = renderTimelineEntry(entry);
        timelineEntryMap.set(entry.id, row);
        content.appendChild(row);
        // Scroll to bottom so new entries are visible.
        content.scrollTop = content.scrollHeight;
        break;
      }

      case PAD_EVENTS.timelineUpdate: {
        const entry = msg.entry;
        if (!entry) break;
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
    if (
      msg.type === PAD_EVENTS.canvasSnapshot ||
      msg.type === PAD_EVENTS.canvasReplace
    ) {
      const content = document.getElementById("canvas-content");
      if (!content) return;
      content.textContent = "";
      content.appendChild(projectNode(msg.state));
    }
  }

  // ── SSE helpers ───────────────────────────────────────────────────────────────

  function openSse(path, handler, { onOpen, onError } = {}) {
    const url = padUrl(path);
    const es = new EventSource(url);
    es.onmessage = (e) => {
      try {
        handler(JSON.parse(e.data));
      } catch (err) {
        console.error(`[DuetsPad] SSE parse error on ${path}`, err);
      }
    };
    es.onopen = () => {
      onOpen?.();
    };
    es.onerror = () => {
      // EventSource will attempt to reconnect automatically.
      onError?.();
    };
    return es;
  }

  // ── Monaco setup ──────────────────────────────────────────────────────────────

  function setupMonaco(id) {
    require.config({ paths: { vs: window.DUETSPAD_MONACO_VS } });

    require(["vs/editor/editor.main"], () => {
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
        padUrl(`type-declaration-events?sessionId=${encodeURIComponent(id)}`),
      );
      declEs.onmessage = (e) => {
        try {
          const decl = JSON.parse(e.data);
          monaco.languages.typescript.typescriptDefaults.addExtraLib(
            decl.content,
            decl.fileName,
          );
        } catch (err) {
          console.error("[DuetsPad] type-declaration-events parse error", err);
        }
      };

      function monacoThemeFromUi() {
        return document.documentElement.getAttribute("data-bs-theme") === "dark"
          ? "vs-dark"
          : "vs";
      }

      const editor = monaco.editor.create(
        document.getElementById("editor-host"),
        {
          value: "",
          language: "typescript",
          theme: monacoThemeFromUi(),
          automaticLayout: true,
          minimap: { enabled: false },
          scrollBeyondLastLine: false,
          fontSize: 13,
          fontFamily: "Consolas, 'Cascadia Code', monospace",
          fixedOverflowWidgets: true,
        },
      );

      activeEditor = editor;

      new MutationObserver(() => {
        monaco.editor.setTheme(monacoThemeFromUi());
      }).observe(document.documentElement, {
        attributes: true,
        attributeFilter: ["data-bs-theme"],
      });

      // Ctrl/Cmd+Enter — run without clearing status
      editor.addAction({
        id: "duetspad-run",
        label: "Run",
        keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter],
        run: () => runCurrent(),
      });

      // F5 — run (same as Ctrl+Enter; no output pane to clear in this layout)
      editor.addAction({
        id: "duetspad-run-f5",
        label: "Run (F5)",
        keybindings: [monaco.KeyCode.F5],
        run: () => runCurrent(),
      });

      // Open canvas and timeline SSE streams.
      // Drive the session-status indicator from the timeline stream's open/error events.
      openSse(`sessions/${id}/canvas-events`, handleCanvasEvent);
      openSse(`sessions/${id}/timeline-events`, handleTimelineEvent, {
        onOpen: () => setSessionStatus(true),
        onError: () => setSessionStatus(false),
      });

      // ── Immediate Monaco editor ───────────────────────────────────────────
      // Single-line REPL editor sharing the page-global TS completion env.

      const immediateEditor = monaco.editor.create(
        document.getElementById("immediate-input"),
        {
          value: "",
          language: "typescript",
          theme: monacoThemeFromUi(),
          automaticLayout: true,
          fontSize: 13,
          fontFamily: "Consolas, 'Cascadia Code', monospace",
          lineNumbers: "off",
          glyphMargin: false,
          folding: false,
          lineDecorationsWidth: 0,
          lineNumbersMinChars: 0,
          minimap: { enabled: false },
          overviewRulerLanes: 0,
          overviewRulerBorder: false,
          hideCursorInOverviewRuler: true,
          renderLineHighlight: "none",
          scrollBeyondLastLine: false,
          wordWrap: "off",
          scrollbar: {
            vertical: "hidden",
            horizontal: "hidden",
            handleMouseWheel: false,
            useShadows: false,
          },
          contextmenu: false,
          fixedOverflowWidgets: true,
        },
      );

      // Keep single-line: collapse any pasted newlines to spaces.
      immediateEditor.onDidChangeModelContent(() => {
        const value = immediateEditor.getValue();
        if (value.includes("\n")) {
          immediateEditor.setValue(value.replace(/\s*\n\s*/g, " "));
        }
      });

      // Placeholder content widget — shown when the immediate editor is empty.
      const immediatePlaceholder = {
        getId: () => "immediate-placeholder",
        getDomNode: () => {
          if (!immediatePlaceholder._node) {
            const node = document.createElement("div");
            node.className = "immediate-placeholder";
            node.textContent = "Type code and press Enter to dump…";
            immediatePlaceholder._node = node;
          }
          return immediatePlaceholder._node;
        },
        getPosition: () => ({
          position: { lineNumber: 1, column: 1 },
          preference: [monaco.editor.ContentWidgetPositionPreference.EXACT],
        }),
        _node: null,
      };

      function syncImmediatePlaceholder() {
        const isEmpty = immediateEditor.getValue() === "";
        if (isEmpty) {
          immediateEditor.addContentWidget(immediatePlaceholder);
        } else {
          immediateEditor.removeContentWidget(immediatePlaceholder);
        }
      }

      immediateEditor.onDidChangeModelContent(syncImmediatePlaceholder);
      // Show placeholder initially (editor starts empty).
      syncImmediatePlaceholder();

      async function submitImmediate() {
        const code = immediateEditor.getValue().trim();
        if (!code) return;
        setEditorStatus("Evaluating…", false);
        try {
          const data = await evalCode(code, /*immediate*/ true);
          if (data.ok) {
            // Result arrives in Timeline via SSE; clear input and status.
            immediateEditor.setValue("");
            setEditorStatus("", false);
          } else {
            setEditorStatus(data.error ?? "Error", true);
          }
        } catch (err) {
          setEditorStatus(String(err), true);
        }
      }

      // Scope the Enter keybinding to the immediate editor only. Monaco
      // registers standalone keybindings in a page-global service, so without
      // an editor-specific context key the Enter handler below would also fire
      // while the main Editor pane has focus and swallow its newline insertion.
      const immediateFocused = immediateEditor.createContextKey(
        "duetspadImmediateFocused",
        false,
      );
      immediateEditor.onDidFocusEditorText(() => immediateFocused.set(true));
      immediateEditor.onDidBlurEditorText(() => immediateFocused.set(false));

      // Submit on Enter only when the immediate editor is focused and the
      // suggest widget is NOT open, so Enter still accepts a completion when
      // the popup is visible.
      immediateEditor.addCommand(
        monaco.KeyCode.Enter,
        () => {
          submitImmediate();
        },
        "duetspadImmediateFocused && !suggestWidgetVisible",
      );
    });
  }

  // ── Entry point ───────────────────────────────────────────────────────────────

  async function main() {
    try {
      const id = await initSession();
      setupMonaco(id);
    } catch (err) {
      console.error("[DuetsPad] Startup error", err);
      setEditorStatus(`Startup error: ${err}`, true);
    }
  }

  // ── Public API ────────────────────────────────────────────────────────────────
  // Exposed before main() so duetspad-ui.js can reference it immediately after load.

  window.DuetsPad = {
    run: () => {
      runCurrent();
    },
    clearTimeline: () => {
      resetTimeline();
    },
  };

  main();
})();
