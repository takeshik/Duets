// DuetsPad browser client
// All URL construction is relative to document.baseURI so non-root mounts (e.g. /pad/) work.

(() => {
  // Protocol event-type constants
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

  // URL helpers

  function padUrl(path) {
    return new URL(path, document.baseURI).href;
  }

  // Session bootstrap
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

  // Render-node projection
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

  function resolveTarget(root, path) {
    let node = root;
    if (!Array.isArray(path)) return null;
    for (const segment of path) {
      if (!node || !Number.isInteger(segment) || segment < 0) return null;
      node = node.childNodes[segment] ?? null;
    }
    return node instanceof HTMLElement ? node : null;
  }

  async function invokeInteraction(handlerId) {
    if (!handlerId) return;
    try {
      await fetch(
        padUrl(
          `sessions/${sessionId}/interactions/${encodeURIComponent(handlerId)}/invoke`,
        ),
        { method: "POST" },
      );
    } catch (err) {
      console.error("[DuetsPad] interaction invoke failed", err);
    }
  }

  function applyInteractions(root, interactions) {
    if (!Array.isArray(interactions)) return;

    for (const interaction of interactions) {
      const target = resolveTarget(root, interaction.target);
      if (!target) continue;

      if (interaction.state !== "live") {
        if ("disabled" in target) target.disabled = true;
        target.classList.add("duetspad-interaction-stale");
        continue;
      }

      if (interaction.event === "click") {
        target.addEventListener("click", () => {
          void invokeInteraction(interaction.handlerId);
        });
      }
    }
  }

  // Eval helper

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

  // Connection state — single source of truth for whether the SSE session is live.
  let isConnected = false;

  /**
   * Synchronises all UI elements whose enabled/disabled state depends on the
   * SSE connection: the Run button, the immediate input, and the canvas pane.
   */
  function syncConnectionUi() {
    const btnRun = document.getElementById("btn-run");
    if (btnRun) {
      btnRun.disabled = !isConnected;
    }

    if (immediateEditorRef) {
      immediateEditorRef.updateOptions({ readOnly: !isConnected });
    }

    activeEditor?.updateOptions({ readOnly: !isConnected });

    const paneCanvas = document.getElementById("pane-canvas");
    if (paneCanvas) {
      paneCanvas.classList.toggle("pane-disabled", !isConnected);
    }
  }

  // Status display helpers

  function setEditorStatus(text, isError) {
    const el = document.getElementById("editor-status");
    if (!el) return;
    el.textContent = text;
    el.className = text ? (isError ? "status-error" : "status-ok") : "";
  }

  function setSessionStatus(connected) {
    isConnected = connected;

    const el = document.getElementById("session-status");
    if (el) {
      const dot =
        el.querySelector(".status-dot") ?? document.createElement("span");
      dot.className = connected
        ? "status-dot status-dot-animated"
        : "status-dot";
      if (!el.contains(dot)) {
        el.prepend(dot);
      }

      const labelEl = el.querySelector(".session-label");
      if (labelEl) {
        labelEl.textContent = connected ? "connected" : "disconnected";
      }

      el.className = `status ${connected ? "status-green" : "status-red"} session-status`;
      el.title = connected ? "Session connected" : "Session disconnected";

      // Update the hover popup with current connection details.
      const popup = el.querySelector(".session-popup");
      if (popup) {
        const sid =
          sessionId ?? sessionStorage.getItem("duetspad.sessionId") ?? "—";
        popup.textContent = `Session: ${sid}\nStatus: ${connected ? "connected" : "disconnected"}`;
      }
    }

    syncConnectionUi();
  }

  // Module-scoped editor references
  // Assigned once Monaco has created the editors; null before that point.

  let activeEditor = null;
  // Holds the immediate REPL editor so syncConnectionUi() can toggle readOnly.
  let immediateEditorRef = null;

  // Editor content persistence (localStorage)

  const EDITOR_CONTENT_KEY = "duetspad.editor.content";

  /** Persists the editor content; silently ignores storage errors. */
  function saveEditorContent(text) {
    try {
      localStorage.setItem(EDITOR_CONTENT_KEY, text);
    } catch {
      // localStorage may be unavailable (private browsing quota etc.)
    }
  }

  // Run current editor content

  async function runCurrent() {
    if (!activeEditor) return;
    if (!isConnected) {
      setEditorStatus("Disconnected", true);
      return;
    }
    const code = activeEditor.getValue();
    if (!code.trim()) return;
    saveEditorContent(code);
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

  // Timeline state
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
    const body = projectNode(entry.body);
    bodyEl.appendChild(body);
    applyInteractions(body, entry.interactions);

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

  // Canvas state

  function handleCanvasEvent(msg) {
    if (
      msg.type === PAD_EVENTS.canvasSnapshot ||
      msg.type === PAD_EVENTS.canvasReplace
    ) {
      const content = document.getElementById("canvas-content");
      if (!content) return;
      content.textContent = "";
      const body = projectNode(msg.state);
      content.appendChild(body);
      applyInteractions(body, msg.interactions);
    }
  }

  // SSE helpers

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

  // Monaco setup

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

      /** Loads the last-saved editor content, returning "" on any error. */
      function loadEditorContent() {
        try {
          return localStorage.getItem(EDITOR_CONTENT_KEY) ?? "";
        } catch {
          return "";
        }
      }

      // Restore previously saved content (overrides the empty initial value).
      const savedContent = loadEditorContent();
      if (savedContent) {
        editor.setValue(savedContent);
      }

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

      // Placeholder content widget for the main editor — shown when empty.
      const editorPlaceholder = {
        getId: () => "editor-placeholder",
        getDomNode: () => {
          if (!editorPlaceholder._node) {
            const node = document.createElement("div");
            node.className = "editor-placeholder";
            node.textContent =
              "Type code and press Ctrl/Cmd+Enter or F5 to run…";
            editorPlaceholder._node = node;
          }
          return editorPlaceholder._node;
        },
        getPosition: () => ({
          position: { lineNumber: 1, column: 1 },
          preference: [monaco.editor.ContentWidgetPositionPreference.EXACT],
        }),
        _node: null,
      };

      function syncEditorPlaceholder() {
        const isEmpty = editor.getValue() === "";
        if (isEmpty) {
          editor.addContentWidget(editorPlaceholder);
        } else {
          editor.removeContentWidget(editorPlaceholder);
        }
      }

      editor.onDidChangeModelContent(() => {
        syncEditorPlaceholder();
      });
      syncEditorPlaceholder();

      // Save on blur so content survives tab switches and navigation.
      editor.onDidBlurEditorText(() => {
        saveEditorContent(editor.getValue());
      });

      // Save on page hide and visibility-hidden so content survives navigation
      // and backgrounding (the latter matters for mobile where pagehide may not
      // fire reliably).
      window.addEventListener("pagehide", () => {
        if (activeEditor) saveEditorContent(activeEditor.getValue());
      });
      document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "hidden" && activeEditor) {
          saveEditorContent(activeEditor.getValue());
        }
      });

      // Open canvas and timeline SSE streams.
      // Drive the session-status indicator from the timeline stream's open/error events.
      openSse(`sessions/${id}/canvas-events`, handleCanvasEvent);
      openSse(`sessions/${id}/timeline-events`, handleTimelineEvent, {
        onOpen: () => setSessionStatus(true),
        onError: () => setSessionStatus(false),
      });

      // Immediate Monaco editor
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

      immediateEditorRef = immediateEditor;
      // Apply current connection state to the immediate editor now that it exists.
      syncConnectionUi();

      // Immediate history (localStorage)
      const HISTORY_KEY = "duetspad.immediate.history";
      const HISTORY_MAX = 100;

      function loadHistory() {
        try {
          return JSON.parse(localStorage.getItem(HISTORY_KEY) ?? "[]");
        } catch {
          return [];
        }
      }

      function saveHistory(hist) {
        try {
          localStorage.setItem(HISTORY_KEY, JSON.stringify(hist));
        } catch {
          // localStorage may be unavailable (private browsing quota etc.)
        }
      }

      function pushHistory(code) {
        const hist = loadHistory();
        if (hist.length > 0 && hist[hist.length - 1] === code) return;
        hist.push(code);
        if (hist.length > HISTORY_MAX)
          hist.splice(0, hist.length - HISTORY_MAX);
        saveHistory(hist);
      }

      // Navigation state: index into history (-1 = not navigating / at draft),
      // and the draft text saved before the user started navigating.
      let histNavIndex = -1;
      let histNavDraft = null;
      // Flag to suppress nav-state reset when we do setValue during navigation.
      let histNavSetting = false;

      function resetHistNav() {
        histNavIndex = -1;
        histNavDraft = null;
      }

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

      immediateEditor.onDidChangeModelContent(() => {
        syncImmediatePlaceholder();
        // Reset history navigation on user edits (not on programmatic setValue).
        if (!histNavSetting) {
          resetHistNav();
        }
      });

      // Show placeholder initially (editor starts empty).
      syncImmediatePlaceholder();

      // Dynamic height for multi-line Immediate
      const IMMEDIATE_MAX_HEIGHT = 200; // ~8 lines
      const immediateInputEl = document.getElementById("immediate-input");

      function updateImmediateHeight() {
        const contentHeight = Math.min(
          immediateEditor.getContentHeight(),
          IMMEDIATE_MAX_HEIGHT,
        );
        immediateInputEl.style.height = `${contentHeight}px`;
        immediateEditor.layout();
      }

      immediateEditor.onDidContentSizeChange(updateImmediateHeight);
      updateImmediateHeight();

      // Scrollbar becomes visible only when content exceeds max height
      // scrollbar config is already set to hidden; override when needed.
      immediateEditor.onDidContentSizeChange(() => {
        const overflow =
          immediateEditor.getContentHeight() > IMMEDIATE_MAX_HEIGHT;
        immediateEditor.updateOptions({
          scrollbar: {
            vertical: overflow ? "auto" : "hidden",
            horizontal: "hidden",
            handleMouseWheel: overflow,
            useShadows: false,
          },
        });
      });

      async function submitImmediate() {
        const code = immediateEditor.getValue().trim();
        if (!code) return;
        if (!isConnected) {
          setEditorStatus("Disconnected", true);
          return;
        }
        setEditorStatus("Evaluating…", false);
        try {
          const data = await evalCode(code, /*immediate*/ true);
          if (data.ok) {
            // Save to history before clearing.
            pushHistory(code);
            resetHistNav();
            // Result arrives in Timeline via SSE; clear input and status.
            histNavSetting = true;
            immediateEditor.setValue("");
            histNavSetting = false;
            setEditorStatus("", false);
          } else {
            setEditorStatus(data.error ?? "Error", true);
          }
        } catch (err) {
          setEditorStatus(String(err), true);
        }
      }

      // Scope the Enter / Up / Down keybindings to the immediate editor only.
      // Monaco registers standalone keybindings in a page-global service, so
      // without an editor-specific context key the Enter handler below would also
      // fire while the main Editor pane has focus and swallow its newline
      // insertion.
      const immediateFocused = immediateEditor.createContextKey(
        "duetspadImmediateFocused",
        false,
      );
      immediateEditor.onDidFocusEditorText(() => immediateFocused.set(true));
      immediateEditor.onDidBlurEditorText(() => immediateFocused.set(false));

      // Submit on plain Enter only when the immediate editor is focused and the
      // suggest widget is NOT open, so Enter still accepts a completion when the
      // popup is visible.  Shift+Enter falls through to Monaco's default
      // (insert newline).
      immediateEditor.addCommand(
        monaco.KeyCode.Enter,
        () => {
          submitImmediate();
        },
        "duetspadImmediateFocused && !suggestWidgetVisible",
      );

      // ↑ — navigate to previous history entry when cursor is on the first line.
      immediateEditor.addCommand(
        monaco.KeyCode.UpArrow,
        () => {
          const pos = immediateEditor.getPosition();
          if (pos?.lineNumber !== 1) {
            // Not on the first line — fall through to normal cursor movement.
            immediateEditor.trigger("keyboard", "cursorUp", null);
            return;
          }
          const hist = loadHistory();
          if (hist.length === 0) return;
          if (histNavIndex === -1) {
            // Start navigation: save current draft.
            histNavDraft = immediateEditor.getValue();
            histNavIndex = hist.length - 1;
          } else if (histNavIndex > 0) {
            histNavIndex--;
          }
          histNavSetting = true;
          immediateEditor.setValue(hist[histNavIndex]);
          histNavSetting = false;
          // Move cursor to end of content.
          const model = immediateEditor.getModel();
          if (model) {
            const lastLine = model.getLineCount();
            const lastCol = model.getLineLength(lastLine) + 1;
            immediateEditor.setPosition({
              lineNumber: lastLine,
              column: lastCol,
            });
          }
        },
        "duetspadImmediateFocused && !suggestWidgetVisible",
      );

      // ↓ — navigate to next history entry when cursor is on the last line;
      //       restores draft when going past the newest entry.
      immediateEditor.addCommand(
        monaco.KeyCode.DownArrow,
        () => {
          if (histNavIndex === -1) {
            // Not navigating — fall through to normal cursor movement.
            immediateEditor.trigger("keyboard", "cursorDown", null);
            return;
          }
          const model = immediateEditor.getModel();
          const lastLine = model ? model.getLineCount() : 1;
          const pos = immediateEditor.getPosition();
          if (!pos || pos.lineNumber !== lastLine) {
            // Not on the last line — fall through.
            immediateEditor.trigger("keyboard", "cursorDown", null);
            return;
          }
          const hist = loadHistory();
          histNavSetting = true;
          if (histNavIndex < hist.length - 1) {
            histNavIndex++;
            immediateEditor.setValue(hist[histNavIndex]);
          } else {
            // Past the newest entry — restore draft.
            immediateEditor.setValue(histNavDraft ?? "");
            histNavSetting = false;
            resetHistNav();
            return;
          }
          histNavSetting = false;
          // Move cursor to end of content.
          if (model) {
            const newLastLine = model.getLineCount();
            const lastCol = model.getLineLength(newLastLine) + 1;
            immediateEditor.setPosition({
              lineNumber: newLastLine,
              column: lastCol,
            });
          }
        },
        "duetspadImmediateFocused && !suggestWidgetVisible",
      );
    });
  }

  // Entry point

  async function main() {
    // Apply initial disconnected state immediately (before SSE connects).
    syncConnectionUi();
    try {
      const id = await initSession();
      setupMonaco(id);
    } catch (err) {
      console.error("[DuetsPad] Startup error", err);
      setEditorStatus(`Startup error: ${err}`, true);
    }
  }

  // Public API
  // Exposed before main() so duetspad-ui.js can reference it immediately after load.

  window.DuetsPad = {
    run: () => {
      runCurrent();
    },
    clearTimeline: () => {
      resetTimeline();
    },
    /**
     * Terminates the current session on the server (best-effort DELETE), clears
     * the stored session id, and reloads the page to start a fresh session.
     * Editor content in localStorage is intentionally preserved across resets.
     */
    resetSession: async () => {
      try {
        if (sessionId) {
          await fetch(padUrl(`sessions/${sessionId}`), { method: "DELETE" });
        }
      } catch {
        // Ignore — the page reload will clean up regardless.
      }
      sessionStorage.removeItem("duetspad.sessionId");
      location.reload();
    },
  };

  main();
})();
