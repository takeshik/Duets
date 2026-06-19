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
    typeDeclaration: "type.declaration",
    taggedTemplateSnapshot: "taggedTemplate.snapshot",
  };

  // URL helpers

  function padUrl(path) {
    return new URL(path, document.baseURI).href;
  }

  // Session bootstrap
  // Reads sessionId from sessionStorage; POSTs to /sessions to reuse a live session
  // or obtain a fresh one; stores the returned id back into sessionStorage.
  // A handoff tab (opened via pad.openText, carrying a "?handoff" param) ignores the
  // stored id so it always gets a fresh isolated session: window.open copies the
  // opener's sessionStorage, and reusing that id would attach the new tab to the
  // opener's session.

  let sessionId = null;

  async function initSession() {
    const hasHandoff = new URLSearchParams(window.location.search).has(
      "handoff",
    );
    const stored = hasHandoff
      ? null
      : sessionStorage.getItem("duetspad.sessionId");
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

  async function completeTaggedTemplate(request, cancellationToken) {
    const controller = new AbortController();
    const disposable = cancellationToken?.onCancellationRequested?.(() => {
      controller.abort();
    });

    try {
      const res = await fetch(padUrl(`sessions/${sessionId}/complete`), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
        signal: controller.signal,
      });
      return await res.json();
    } finally {
      disposable?.dispose?.();
    }
  }

  // Connection state — single source of truth for whether the SSE session is live.
  let isConnected = false;

  // Module-scoped EventSource reference.
  // Kept here so swapSession() can close the old stream before opening the new one.
  let activeEventSource = null;

  const taggedTemplateTags = new Set();

  // Set to true during swapSession() to suppress onerror-driven reconnect logic
  // for the outgoing stream.
  let sessionSwapInProgress = false;

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

  // Assigned inside setupMonaco() once the SSE dispatcher is ready.
  // swapSession() calls this to re-subscribe on the new session.
  let subscribeSession = null;
  // Holds the immediate REPL editor so syncConnectionUi() can toggle readOnly.
  let immediateEditorRef = null;

  // Editor content persistence (localStorage)

  const EDITOR_CONTENT_KEY = "duetspad.editor.content";

  // Handoff key utilities for one-shot localStorage text transfer across tabs.
  // A handoff key stores text that a newly opened tab reads exactly once, then
  // deletes. The key name embeds a UUID so concurrent openText calls do not
  // collide.

  const HANDOFF_KEY_PREFIX = "duetspad.handoff.";
  const HANDOFF_MAX_KEYS = 20;

  /**
   * Writes text under a fresh handoff key and returns the UUID portion so the
   * caller can embed it in the target URL.
   * Old surplus handoff keys (beyond HANDOFF_MAX_KEYS) are pruned to prevent
   * unbounded growth in case they are never consumed.
   * @param {string} text - The handoff content to store.
   * @returns {string} The UUID identifying this handoff key.
   */
  function writeHandoff(text) {
    const uuid = crypto.randomUUID();
    try {
      localStorage.setItem(HANDOFF_KEY_PREFIX + uuid, text);
      pruneHandoffKeys();
    } catch {
      // localStorage unavailable; best-effort only.
    }
    return uuid;
  }

  /**
   * Reads and immediately deletes the handoff for the given UUID.
   * Returns the stored text, or null if the key is absent or storage is
   * unavailable. The one-shot delete ensures a second caller gets nothing.
   * @param {string} uuid - The UUID from the URL ?handoff= parameter.
   * @returns {string|null} The stored text, or null.
   */
  function consumeHandoff(uuid) {
    try {
      const key = HANDOFF_KEY_PREFIX + uuid;
      const value = localStorage.getItem(key);
      if (value !== null) {
        localStorage.removeItem(key);
      }
      return value;
    } catch {
      return null;
    }
  }

  /**
   * Removes the oldest surplus handoff keys so at most HANDOFF_MAX_KEYS remain.
   * Keys are sorted lexicographically; since UUIDs are random (v4) this is not
   * truly FIFO, but it keeps storage bounded in pathological cases.
   */
  function pruneHandoffKeys() {
    try {
      const keys = [];
      for (let i = 0; i < localStorage.length; i++) {
        const k = localStorage.key(i);
        // biome-ignore lint/complexity/useOptionalChain: k?.startsWith() returns undefined (not false) when k is null, which is not safe in boolean context here
        if (k && k.startsWith(HANDOFF_KEY_PREFIX)) {
          keys.push(k);
        }
      }
      if (keys.length > HANDOFF_MAX_KEYS) {
        keys.sort();
        for (const k of keys.slice(0, keys.length - HANDOFF_MAX_KEYS)) {
          localStorage.removeItem(k);
        }
      }
    } catch {
      // Best-effort; ignore storage errors.
    }
  }

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

  // Maps canvas name → <div class="canvas-panel"> element.
  const canvasPanelMap = new Map();

  // Tracks which canvas is currently displayed (name string).
  let activeCanvasName = "default";

  // Callback registered by the UI layer; called when a new canvas name appears.
  let onCanvasCreatedCallback = null;

  // Cached reference to the #canvas-panels container. The UI layer detaches the
  // canvas pane (via .remove()) when it is not the active tab or is hidden;
  // a detached element is not reachable through document.getElementById, so
  // looking it up afresh on each event would return null and silently drop the
  // canvas update. The pane element is never recreated — only detached and
  // re-attached — so a once-captured reference stays valid across those moves,
  // letting canvas mutations keep updating the (possibly detached) subtree.
  let canvasPanelsRoot = null;

  /**
   * Returns the cached <div id="canvas-panels"> container, capturing it on first
   * use. Returns null only if the element has never existed in the document.
   * @returns {HTMLElement|null}
   */
  function getCanvasPanelsRoot() {
    if (!canvasPanelsRoot) {
      canvasPanelsRoot = document.getElementById("canvas-panels");
    }
    return canvasPanelsRoot;
  }

  /**
   * Ensures a canvas panel div exists for the given name. Creates it (and
   * appends it to #canvas-panels) if it did not exist before.
   * Returns the panel element.
   * @param {string} name - Canvas name.
   * @returns {HTMLElement|null}
   */
  function ensureCanvasPanel(name) {
    if (canvasPanelMap.has(name)) {
      return canvasPanelMap.get(name);
    }
    const root = getCanvasPanelsRoot();
    if (!root) return null;

    const panel = document.createElement("div");
    panel.className = "canvas-panel";
    panel.dataset.canvas = name;
    root.appendChild(panel);
    canvasPanelMap.set(name, panel);

    // Notify the UI layer that a new canvas name has appeared.
    if (onCanvasCreatedCallback) {
      try {
        onCanvasCreatedCallback(name);
      } catch (err) {
        console.error("[DuetsPad] onCanvasCreated callback threw", err);
      }
    }

    return panel;
  }

  /**
   * Makes the named canvas panel visible and hides all others. Also updates
   * the module-level activeCanvasName.
   * @param {string} name - Canvas name to activate.
   */
  function activateCanvasPanel(name) {
    activeCanvasName = name;
    for (const [panelName, panel] of canvasPanelMap) {
      panel.classList.toggle("active", panelName === name);
    }
  }

  function handleCanvasEvent(msg) {
    if (
      msg.type === PAD_EVENTS.canvasSnapshot ||
      msg.type === PAD_EVENTS.canvasReplace
    ) {
      const name = typeof msg.name === "string" ? msg.name : "default";
      const isNew = !canvasPanelMap.has(name);
      const panel = ensureCanvasPanel(name);
      if (!panel) return;

      // If this is the first event for this name (or the only canvas),
      // make it active so content is visible immediately.
      if (isNew || canvasPanelMap.size === 1) {
        activateCanvasPanel(name);
      }

      panel.textContent = "";
      const body = projectNode(msg.state);
      panel.appendChild(body);
      applyInteractions(body, msg.interactions);
    }
  }

  /**
   * Resets all canvas panels: clears and removes them from the DOM, clears the
   * map, resets activeCanvasName. Called by swapSession().
   */
  function resetCanvases() {
    const root = getCanvasPanelsRoot();
    if (root) {
      root.textContent = "";
    }
    canvasPanelMap.clear();
    activeCanvasName = "default";

    // Notify the UI layer so it can rebuild canvas tabs.
    if (onCanvasCreatedCallback) {
      try {
        onCanvasCreatedCallback(null);
      } catch {
        // Ignore.
      }
    }
  }

  // Session swap

  /**
   * Performs a no-reload session swap:
   * 1. Closes the current EventSource.
   * 2. Deletes the old session on the server (best-effort).
   * 3. Creates a new session via POST /sessions.
   * 4. Updates sessionStorage and the module-level sessionId.
   * 5. Clears the Canvas and Timeline panes (the initial SSE burst will re-populate them).
   * 6. Opens a new EventSource on the new session.
   * The editor content is intentionally left untouched.
   */
  async function swapSession() {
    if (sessionSwapInProgress) return;
    sessionSwapInProgress = true;
    setSessionStatus(false);

    const oldId = sessionId;

    // Step 1: close the outgoing EventSource.
    if (activeEventSource) {
      activeEventSource.close();
      activeEventSource = null;
    }

    // Step 2: delete the old session (best-effort).
    if (oldId) {
      try {
        await fetch(padUrl(`sessions/${oldId}`), { method: "DELETE" });
      } catch {
        // Ignore — the old session will eventually be evicted by the server.
      }
    }

    // Step 3: create a new session via POST /sessions (no prior id in the body).
    let newId;
    try {
      const res = await fetch(padUrl("sessions"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: "{}",
      });
      if (!res.ok) {
        throw new Error(`Session creation failed: ${res.status}`);
      }
      const data = await res.json();
      newId = data.sessionId;
    } catch (err) {
      console.error(
        "[DuetsPad] swapSession: failed to create new session",
        err,
      );
      sessionSwapInProgress = false;
      return;
    }

    // Step 4: persist the new id.
    sessionId = newId;
    sessionStorage.setItem("duetspad.sessionId", newId);

    // Step 5: clear Canvas and Timeline panes before the new stream arrives
    // so the old content does not persist during the brief gap.
    resetCanvases();
    resetTimeline();

    // Step 6: open the new EventSource.
    sessionSwapInProgress = false;
    if (subscribeSession) {
      subscribeSession(newId);
    }
  }

  // Control channel

  /**
   * Map of control op names to handler functions. Populate this map to handle
   * specific ops received from the server; unknown ops fall through to the default
   * console.warn branch.
   * @type {Map<string, function(object): void>}
   */
  const controlHandlers = new Map();

  /**
   * Replaces the editor content with the text supplied by the server.
   * @param {object} msg - Control message with a `text` field.
   */
  controlHandlers.set("setEditorText", (msg) => {
    if (activeEditor && typeof msg.text === "string") {
      activeEditor.setValue(msg.text);
    }
  });

  controlHandlers.set("reset", (_msg) => {
    void swapSession();
  });

  /**
   * Shows a non-blocking toast notification with an action link.
   * The toast is appended to #toast-container and auto-dismisses after 8 s.
   * This is intentionally implemented without Bootstrap JS; DuetsPad only
   * serves Tabler/Bootstrap CSS.
   * @param {string} message - Body text for the toast.
   * @param {string} linkLabel - Label for the action link inside the toast.
   * @param {string} href - URL the action link opens (in a new tab).
   */
  function showOpenTextToast(message, linkLabel, href) {
    const container = document.getElementById("toast-container");
    if (!container) return;

    const toastEl = document.createElement("div");
    toastEl.className = "toast align-items-center";
    toastEl.setAttribute("role", "alert");
    toastEl.setAttribute("aria-live", "assertive");
    toastEl.setAttribute("aria-atomic", "true");

    const body = document.createElement("div");
    body.className = "d-flex";

    const bodyInner = document.createElement("div");
    bodyInner.className = "toast-body";
    bodyInner.textContent = `${message} `;

    const link = document.createElement("a");
    link.href = href;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    // textContent is safe — no user-supplied HTML
    link.textContent = linkLabel;

    bodyInner.appendChild(link);
    body.appendChild(bodyInner);

    const closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.className = "btn-close me-2 m-auto";
    closeBtn.setAttribute("aria-label", "Close");
    body.appendChild(closeBtn);

    toastEl.appendChild(body);
    container.appendChild(toastEl);
    toastEl.classList.add("show");

    const closeToast = () => {
      toastEl.remove();
    };

    closeBtn.addEventListener("click", closeToast, { once: true });
    window.setTimeout(closeToast, 8000);
  }

  controlHandlers.set("openText", (msg) => {
    if (typeof msg.text !== "string") return;

    const uuid = writeHandoff(msg.text);
    const targetUrl = new URL(document.location.href);
    targetUrl.search = "";
    targetUrl.searchParams.set("handoff", uuid);
    const href = targetUrl.href;

    const newTab = window.open(href, "_blank");
    if (!newTab) {
      showOpenTextToast("New script ready.", "Open in new tab", href);
    }
  });

  /**
   * Dispatches a control event received from the server. Strips the "control."
   * prefix from msg.type to derive the op name, then delegates to the registered
   * handler in controlHandlers, or warns for unknown ops.
   * @param {object} msg - The parsed SSE message with a "control.<op>" type field.
   */
  function handleControlEvent(msg) {
    const op = msg.type.slice("control.".length);
    const handler = controlHandlers.get(op);
    if (handler) {
      try {
        handler(msg);
      } catch (err) {
        console.error(`[DuetsPad] control handler for "${op}" threw`, err);
      }
    } else {
      console.warn(`[DuetsPad] unknown control op: "${op}"`, msg);
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
      // During an intentional session swap the outgoing stream must be closed
      // immediately so it does not reconnect. Outside of a swap, let EventSource
      // attempt automatic reconnection (its default behaviour).
      if (sessionSwapInProgress) {
        es.close();
      }
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

      function addExtraLib(decl) {
        monaco.languages.typescript.typescriptDefaults.addExtraLib(
          decl.content,
          decl.fileName,
        );
      }

      const taggedTemplateCompletion = window.DuetsPadTaggedTemplateCompletion;
      if (taggedTemplateCompletion) {
        monaco.languages.registerCompletionItemProvider(
          "typescript",
          taggedTemplateCompletion.createCompletionItemProvider({
            monaco,
            getTags: () => taggedTemplateTags,
            requestCompletions: completeTaggedTemplate,
          }),
        );
      }

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

      // Handoff: if the URL carries ?handoff=<uuid>, consume it from localStorage
      // and use it as the initial content (one-shot; key is deleted on read).
      // Otherwise fall back to the last persisted editor content.
      const handoffParam = new URLSearchParams(window.location.search).get(
        "handoff",
      );
      const handoffContent = handoffParam ? consumeHandoff(handoffParam) : null;
      if (handoffContent !== null) {
        editor.setValue(handoffContent);
        // Remove the ?handoff= parameter from the URL without reloading so
        // the next time the user refreshes they get a fresh empty editor.
        const cleanUrl = new URL(window.location.href);
        cleanUrl.searchParams.delete("handoff");
        window.history.replaceState(null, "", cleanUrl.href);
      } else {
        // Restore previously saved content (overrides the empty initial value).
        const savedContent = loadEditorContent();
        if (savedContent) {
          editor.setValue(savedContent);
        }
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

      // Open the unified event stream. Route each message to the appropriate handler by type prefix.
      // subscribeSession is exposed at module scope so swapSession() can re-subscribe after a reset.
      subscribeSession = (targetId) => {
        activeEventSource = openSse(
          `sessions/${targetId}/events`,
          (msg) => {
            if (msg.type.startsWith("canvas.")) {
              handleCanvasEvent(msg);
            } else if (msg.type.startsWith("timeline.")) {
              handleTimelineEvent(msg);
            } else if (msg.type === PAD_EVENTS.typeDeclaration) {
              addExtraLib(msg);
            } else if (msg.type === PAD_EVENTS.taggedTemplateSnapshot) {
              taggedTemplateTags.clear();
              if (Array.isArray(msg.tags)) {
                for (const tag of msg.tags) {
                  if (typeof tag === "string") {
                    taggedTemplateTags.add(tag);
                  }
                }
              }
            } else if (msg.type.startsWith("control.")) {
              handleControlEvent(msg);
            }
          },
          {
            onOpen: () => setSessionStatus(true),
            onError: () => setSessionStatus(false),
          },
        );
      };
      subscribeSession(id);

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
     * Performs a no-reload session swap: closes the current SSE stream, deletes
     * the old session, creates a new one, and re-subscribes. The Canvas and
     * Timeline panes are cleared; the editor content is preserved.
     */
    resetSession: () => {
      void swapSession();
    },
    /**
     * Registers a callback that is invoked when a new canvas name appears for
     * the first time (first SSE event for that name). Pass null to
     * indicate a full canvas reset. Only one callback is supported; registering
     * again replaces the previous one.
     * @param {function(string|null): void} callback
     */
    onCanvasCreated: (callback) => {
      onCanvasCreatedCallback = callback;
    },
    /**
     * Returns the current list of canvas names in creation order.
     * @returns {string[]}
     */
    getCanvasNames: () => {
      return Array.from(canvasPanelMap.keys());
    },
    /**
     * Returns the name of the currently active (visible) canvas.
     * @returns {string}
     */
    getActiveCanvasName: () => {
      return activeCanvasName;
    },
    /**
     * Switches the visible canvas to the one with the given name.
     * No-ops if the name is not known.
     * @param {string} name
     */
    setActiveCanvasName: (name) => {
      if (canvasPanelMap.has(name)) {
        activateCanvasPanel(name);
      }
    },
  };

  main();
})();
