// DuetsPad browser client
// All URL construction is relative to document.baseURI so non-root mounts (e.g. /pad/) work.

(() => {
  // Protocol event-type constants
  // Single source of truth for SSE event-type discriminators.
  // The string values here are the only place they should appear in this file.

  const PAD_EVENTS = {
    canvasSnapshot: "canvas.snapshot",
    canvasReplace: "canvas.replace",
    canvasPatch: "canvas.patch",
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
          // A freshly built element has never been focused or edited, so the
          // live property is always applied unguarded (ADR-47).
          applyFieldLiveValue(el);
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

  function assertRenderNode(node, path = "node") {
    if (!node || typeof node !== "object") {
      throw new Error(`${path} must be an object`);
    }

    switch (node.kind) {
      case "text":
        if (typeof node.value !== "string") {
          throw new Error(`${path}.value must be a string`);
        }
        return;

      case "element":
        assertElementNode(node, path);
        return;

      case "rawHtml":
        if (typeof node.content !== "string") {
          throw new Error(`${path}.content must be a string`);
        }
        return;

      default:
        throw new Error(`${path}.kind is not supported`);
    }
  }

  const URL_ATTRIBUTES = new Set([
    "href",
    "src",
    "action",
    "formaction",
    "poster",
    "srcset",
  ]);

  function assertElementNode(node, path) {
    if (!isSafeTagName(node.tag)) {
      throw new Error(`${path}.tag is not allowed`);
    }

    if (
      !node.attributes ||
      typeof node.attributes !== "object" ||
      Array.isArray(node.attributes)
    ) {
      throw new Error(`${path}.attributes must be an object`);
    }

    for (const [name, value] of Object.entries(node.attributes)) {
      assertSafeAttribute(name, value, `${path}.attributes.${name}`);
      if (value !== null && typeof value !== "string") {
        throw new Error(`${path}.attributes.${name} is invalid`);
      }
    }

    if (!Array.isArray(node.children)) {
      throw new Error(`${path}.children must be an array`);
    }

    for (let i = 0; i < node.children.length; i++) {
      assertRenderNode(node.children[i], `${path}.children[${i}]`);
    }
  }

  function assertCanvasRootNode(node) {
    assertRenderNode(node, "canvas state");
    if (
      node.kind !== "element" ||
      node.tag !== "div" ||
      !Object.hasOwn(node.attributes, "data-duetspad-root")
    ) {
      throw new Error("canvas state root invariant is invalid");
    }
  }

  function isSafeTagName(tag) {
    if (typeof tag !== "string" || tag.length === 0) return false;
    if (!/^[a-z][a-z0-9-]*$/i.test(tag)) return false;
    if (
      ["script", "iframe", "object", "embed", "template"].includes(
        tag.toLowerCase(),
      )
    ) {
      return false;
    }

    for (const ch of tag) {
      if (/[\s"'<>/=]/.test(ch) || ch < " ") return false;
    }

    return true;
  }

  function assertSafeAttribute(name, value, path = "attribute") {
    if (!isSafeAttributeName(name)) {
      throw new Error(`${path}.name is not allowed`);
    }

    const lower = name.toLowerCase();
    if (lower === "srcdoc") {
      throw new Error(`${path}.name is not allowed`);
    }

    if (
      typeof value === "string" &&
      URL_ATTRIBUTES.has(lower) &&
      value.trimStart().toLowerCase().startsWith("javascript:")
    ) {
      throw new Error(`${path}.value is not allowed`);
    }
  }

  function isSafeAttributeName(name) {
    if (typeof name !== "string" || name.length === 0) return false;
    if (!/^[a-z_:][a-z0-9_.:-]*$/i.test(name)) return false;
    if (/^on/i.test(name)) return false;

    for (const ch of name) {
      if (/[\s"'<>/=]/.test(ch) || ch < " ") return false;
    }

    return true;
  }

  function resolveNode(root, path) {
    let node = root;
    if (!Array.isArray(path)) return null;
    for (const segment of path) {
      if (!node || !Number.isInteger(segment) || segment < 0) return null;
      node = node.childNodes[segment] ?? null;
    }
    return node;
  }

  function resolveTarget(root, path) {
    const node = resolveNode(root, path);
    return node instanceof HTMLElement ? node : null;
  }

  // Form-input fields (ADR-47)
  // Every field-marked element carries data-duetspad-field (the field id) and
  // data-duetspad-field-kind. Text-like kinds encode their value in the "value"
  // attribute/property; "checkbox" and "radio" encode it as the "checked"
  // boolean attribute/property. The server is the canonical holder; the
  // browser is a second writer that commits on blur and folds a snapshot into
  // the invoke body so a click handler sees the latest edit regardless of
  // blur timing.

  function isFieldGuarded(el) {
    // A focused or mid-edit (pending, not yet committed) field must not be
    // clobbered by an incoming projection — the ordinary controlled-input
    // concern, not a change in who owns the value.
    return document.activeElement === el || el.dataset.duetspadPending === "1";
  }

  /**
   * Applies the live DOM property (value/checked) a field-marked element's
   * encoded attribute represents. Called after projecting a fresh element and
   * after every canvas-patch attribute mutation on an existing one.
   * @param {Element} el
   * @param {{ checkGuard?: boolean }} [options]
   */
  function applyFieldLiveValue(el, options = {}) {
    if (!(el instanceof HTMLElement)) return;
    const kind = el.getAttribute("data-duetspad-field-kind");
    if (!kind) return;
    if (options.checkGuard && isFieldGuarded(el)) return;

    if (kind === "checkbox" || kind === "radio") {
      el.checked = el.hasAttribute("checked");
    } else {
      el.value = el.getAttribute("value") ?? "";
    }
  }

  /**
   * Returns the current value a field-marked element should commit, or null
   * when it should not contribute one (an unchecked radio option).
   * @param {Element} el
   */
  function fieldCurrentValue(el) {
    const kind = el.getAttribute("data-duetspad-field-kind");
    if (kind === "checkbox") return el.checked ? "True" : "False";
    if (kind === "radio") return el.checked ? el.value : null;
    return el.value;
  }

  async function commitFieldValue(el) {
    const fieldId = el.getAttribute("data-duetspad-field");
    if (!fieldId) return;
    const value = fieldCurrentValue(el);
    if (value === null) return;
    // Capture the edit generation this commit is chasing: if a newer edit
    // lands while the request is in flight, the generation captured here
    // will be stale by the time the response arrives, and the pending flag
    // must survive to keep guarding that newer, still-uncommitted edit
    // (concurrent commits can otherwise race and clear pending too early).
    const editGen = el.dataset.duetspadEditGen;
    try {
      const res = await fetch(
        padUrl(
          `sessions/${sessionId}/fields/${encodeURIComponent(fieldId)}/commit`,
        ),
        {
          method: "POST",
          headers: { "Content-Type": "text/plain" },
          body: value,
        },
      );
      if (!res.ok) {
        throw new Error(`field commit failed: ${res.status}`);
      }
      const data = await res.json();
      if (data.ok !== true) {
        throw new Error(data.error ?? "field commit rejected");
      }
      // Only a confirmed commit clears the pending flag, and only when no
      // newer edit has started since this commit began: an unconfirmed or
      // superseded edit must keep guarding the field against being
      // clobbered by the next incoming projection.
      if (el.dataset.duetspadEditGen === editGen) {
        delete el.dataset.duetspadPending;
      }
    } catch (err) {
      console.error("[DuetsPad] field commit failed", err);
    }
  }

  function bindFieldElement(el, signal) {
    const listenerOptions = signal ? { signal } : undefined;
    el.addEventListener(
      "input",
      () => {
        el.dataset.duetspadPending = "1";
        el.dataset.duetspadEditGen = String(
          (Number(el.dataset.duetspadEditGen) || 0) + 1,
        );
      },
      listenerOptions,
    );
    el.addEventListener(
      "focusout",
      () => {
        void commitFieldValue(el);
      },
      listenerOptions,
    );
  }

  /**
   * Binds blur-commit (and pending-flag) listeners on every field-marked
   * element within root, including root itself when root is field-marked
   * (a timeline entry's body can project a field element as its own root).
   * Rebound on every full render and canvas patch, sharing the same abort
   * signal as the canvas's interaction bindings.
   * @param {Element} root
   * @param {AbortSignal} [signal]
   */
  function bindFields(root, signal) {
    if (!root || typeof root.querySelectorAll !== "function") return;
    if (root.matches?.("[data-duetspad-field]")) {
      bindFieldElement(root, signal);
    }
    for (const el of root.querySelectorAll("[data-duetspad-field]")) {
      bindFieldElement(el, signal);
    }
  }

  /**
   * Collects { fieldId: value } for every field-marked element within root,
   * for folding into an interaction-invoke request body (ADR-47).
   * @param {Element} root
   */
  function collectFieldSnapshot(root) {
    const fields = {};
    if (!root || typeof root.querySelectorAll !== "function") return fields;
    for (const el of root.querySelectorAll("[data-duetspad-field]")) {
      const fieldId = el.getAttribute("data-duetspad-field");
      if (!fieldId) continue;
      const value = fieldCurrentValue(el);
      if (value === null) continue;
      fields[fieldId] = value;
    }
    return fields;
  }

  async function invokeInteraction(handlerId, canvasRoot) {
    if (!handlerId) return;
    try {
      await fetch(
        padUrl(
          `sessions/${sessionId}/interactions/${encodeURIComponent(handlerId)}/invoke`,
        ),
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fields: collectFieldSnapshot(canvasRoot) }),
        },
      );
    } catch (err) {
      console.error("[DuetsPad] interaction invoke failed", err);
    }
  }

  function applyInteractions(root, interactions, signal) {
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
        target.addEventListener(
          "click",
          () => {
            void invokeInteraction(interaction.handlerId, root);
          },
          signal ? { signal } : undefined,
        );
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
    bindFields(body);

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

  // Maps canvas name to the latest validated revision rendered in the DOM.
  const canvasRevisionMap = new Map();

  // Maps canvas name to in-flight resync state.
  const canvasResyncMap = new Map();

  // Maps canvas name to the AbortController used by the current canvas bindings.
  const canvasInteractionControllerMap = new Map();

  const CANVAS_RESYNC_MAX_BUFFERED_PATCHES = 256;
  const CANVAS_RESYNC_MAX_BUFFERED_BYTES = 1024 * 1024;
  const CANVAS_RESYNC_MAX_REPEATED_FAILURES = 3;
  const textEncoder = new TextEncoder();

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

  function getCanvasName(msg) {
    return typeof msg.name === "string" && msg.name.length > 0
      ? msg.name
      : "default";
  }

  function isRevision(value) {
    return Number.isSafeInteger(value) && value >= 0;
  }

  function resetCanvasInteractionBindings(name) {
    canvasInteractionControllerMap.get(name)?.abort();
    const controller = new AbortController();
    canvasInteractionControllerMap.set(name, controller);
    return controller.signal;
  }

  function pathKey(path) {
    return path.join("/");
  }

  function assertDisplayPath(path, label) {
    if (!Array.isArray(path)) {
      throw new Error(`${label} must be an array`);
    }

    for (const segment of path) {
      if (!Number.isInteger(segment) || segment < 0) {
        throw new Error(`${label} contains an invalid segment`);
      }
    }
  }

  function isSameOrDescendantPath(path, ancestor) {
    if (path.length < ancestor.length) return false;
    for (let i = 0; i < ancestor.length; i++) {
      if (path[i] !== ancestor[i]) return false;
    }
    return true;
  }

  function comparePath(left, right) {
    const length = Math.min(left.length, right.length);
    for (let i = 0; i < length; i++) {
      if (left[i] !== right[i]) return left[i] - right[i];
    }

    return left.length - right.length;
  }

  function hasSameOrAncestorPath(path, paths) {
    return paths.some((candidate) => isSameOrDescendantPath(path, candidate));
  }

  function assertNotUnderReplacedPath(path, replacedPaths, label) {
    if (hasSameOrAncestorPath(path, replacedPaths)) {
      throw new Error(`${label} conflicts with replace-node`);
    }
  }

  function operationPhase(operation) {
    switch (operation.op) {
      case "replace-node":
        return 0;
      case "set-attr":
      case "remove-attr":
      case "replace-text":
        return 1;
      case "remove-child":
        return 2;
      case "insert-child":
        return 3;
      default:
        return 4;
    }
  }

  function scalarOperationKindOrder(operation) {
    switch (operation.op) {
      case "set-attr":
        return 0;
      case "remove-attr":
        return 1;
      case "replace-text":
        return 2;
      default:
        return 3;
    }
  }

  function operationSortPath(operation) {
    switch (operation.op) {
      case "set-attr":
      case "remove-attr":
      case "replace-text":
      case "replace-node":
        return operation.path;
      case "remove-child":
      case "insert-child":
        return operation.parentPath;
      default:
        return [];
    }
  }

  function compareCanonicalOperations(left, right) {
    const phase = operationPhase(left) - operationPhase(right);
    if (phase !== 0) return phase;

    switch (left.op) {
      case "replace-node": {
        const depth = right.path.length - left.path.length;
        return depth !== 0 ? depth : comparePath(left.path, right.path);
      }

      case "set-attr":
      case "remove-attr":
      case "replace-text": {
        const path = comparePath(
          operationSortPath(left),
          operationSortPath(right),
        );
        return path !== 0
          ? path
          : scalarOperationKindOrder(left) - scalarOperationKindOrder(right);
      }

      case "remove-child": {
        const parent = comparePath(left.parentPath, right.parentPath);
        return parent !== 0 ? parent : right.index - left.index;
      }

      case "insert-child": {
        const parent = comparePath(left.parentPath, right.parentPath);
        return parent !== 0 ? parent : left.index - right.index;
      }

      default:
        return 0;
    }
  }

  function assertCanonicalOperationOrder(operations) {
    for (let i = 1; i < operations.length; i++) {
      if (compareCanonicalOperations(operations[i - 1], operations[i]) > 0) {
        throw new Error("canvas patch operations are not canonical");
      }
    }
  }

  function collectReplaceNodePaths(operations) {
    const paths = [];
    for (const operation of operations) {
      if (operation.op !== "replace-node") continue;

      assertDisplayPath(operation.path, "replace-node.path");
      if (operation.path.length === 0) {
        throw new Error("replace-node cannot replace the canvas root");
      }

      if (
        paths.some(
          (path) =>
            isSameOrDescendantPath(path, operation.path) ||
            isSameOrDescendantPath(operation.path, path),
        )
      ) {
        throw new Error("replace-node paths conflict");
      }

      paths.push(operation.path);
    }

    return paths;
  }

  function assertInteractionSet(root, interactions) {
    if (!Array.isArray(interactions)) {
      throw new Error("canvas interactions must be an array");
    }

    const seen = new Set();
    for (const interaction of interactions) {
      if (!interaction || typeof interaction !== "object") {
        throw new Error("canvas interaction must be an object");
      }

      assertDisplayPath(interaction.target, "interaction.target");
      if (interaction.event !== "click") {
        throw new Error("interaction event is not supported");
      }

      if (
        typeof interaction.handlerId !== "string" ||
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
          interaction.handlerId,
        )
      ) {
        throw new Error("interaction handler id is invalid");
      }

      if (interaction.state !== "live") {
        throw new Error("canvas interaction state must be live");
      }

      if (!resolveTarget(root, interaction.target)) {
        throw new Error("interaction target is missing");
      }

      const key = `${pathKey(interaction.target)}|${interaction.event}`;
      if (seen.has(key)) {
        throw new Error("duplicate canvas interaction target/event");
      }
      seen.add(key);
    }
  }

  function renderCanvasFullState(msg) {
    const name = getCanvasName(msg);
    const revision = msg.revision;
    if (!isRevision(revision)) {
      throw new Error(`invalid canvas revision: ${revision}`);
    }

    const currentRevision = canvasRevisionMap.get(name);
    if (currentRevision !== undefined && revision <= currentRevision) {
      return false;
    }

    assertCanvasRootNode(msg.state);
    const body = projectNode(msg.state);
    assertInteractionSet(body, msg.interactions);

    const isNew = !canvasPanelMap.has(name);
    const panel = ensureCanvasPanel(name);
    if (!panel) return false;

    if (isNew || canvasPanelMap.size === 1) {
      activateCanvasPanel(name);
    }

    panel.textContent = "";
    panel.appendChild(body);
    const signal = resetCanvasInteractionBindings(name);
    applyInteractions(body, msg.interactions, signal);
    bindFields(body, signal);
    canvasRevisionMap.set(name, revision);
    return true;
  }

  function applyCanvasPatch(root, operations) {
    if (!root) {
      throw new Error("canvas root is missing");
    }

    // This mutation pass is intentionally guard-light. It must only run
    // synchronously after preflightCanvasPatch has validated the same DOM
    // revision; async or interleaved application must re-preflight or add guards.
    for (const operation of operations) {
      switch (operation.op) {
        case "set-attr": {
          const target = resolveNode(root, operation.path);
          target.setAttribute(operation.name, operation.value ?? "");
          // The differ emits value/checked changes as plain attribute ops (ADR-47); mirror
          // them onto the live DOM property, guarding a focused/mid-edit field from being
          // clobbered by an incoming projection.
          applyFieldLiveValue(target, { checkGuard: true });
          break;
        }

        case "remove-attr": {
          const target = resolveNode(root, operation.path);
          target.removeAttribute(operation.name);
          applyFieldLiveValue(target, { checkGuard: true });
          break;
        }

        case "replace-text": {
          const target = resolveNode(root, operation.path);
          target.textContent = operation.value;
          break;
        }

        case "replace-node": {
          const target = resolveNode(root, operation.path);
          target.replaceWith(projectNode(operation.node));
          break;
        }

        case "remove-child": {
          const parent = resolveNode(root, operation.parentPath);
          const child = parent.childNodes[operation.index];
          parent.removeChild(child);
          break;
        }

        case "insert-child": {
          const parent = resolveNode(root, operation.parentPath);
          parent.insertBefore(
            projectNode(operation.node),
            parent.childNodes[operation.index] ?? null,
          );
          break;
        }

        default:
          throw new Error(`unknown canvas patch op: ${operation.op}`);
      }
    }
  }

  function preflightCanvasPatch(root, operations, interactions) {
    if (!root) {
      throw new Error("canvas root is missing");
    }

    if (!Array.isArray(operations)) {
      throw new Error("canvas patch operations must be an array");
    }

    for (const operation of operations) {
      if (!operation || typeof operation !== "object") {
        throw new Error("canvas patch operation must be an object");
      }

      switch (operation.op) {
        case "set-attr":
        case "remove-attr":
        case "replace-text":
        case "replace-node":
          assertDisplayPath(operation.path, `${operation.op}.path`);
          break;
        case "remove-child":
        case "insert-child":
          assertDisplayPath(operation.parentPath, `${operation.op}.parentPath`);
          break;
        default:
          throw new Error(`unknown canvas patch op: ${operation.op}`);
      }
    }

    assertCanonicalOperationOrder(operations);

    // The clone preserves the hard atomicity contract: invalid patches must not
    // touch the live DOM. This costs O(canvas size) per preflight and is an
    // accepted DuetsPad trade-off documented in ADR-45.
    const candidateRoot = root.cloneNode(true);
    const replacedPaths = collectReplaceNodePaths(operations);
    const attrOps = new Set();
    const textOps = new Set();
    const removeChildOps = new Set();
    const insertChildOps = new Set();
    const virtualChildCounts = new Map();

    for (const operation of operations) {
      if (!operation || typeof operation !== "object") {
        throw new Error("canvas patch operation must be an object");
      }

      switch (operation.op) {
        case "set-attr": {
          assertDisplayPath(operation.path, "set-attr.path");
          assertNotUnderReplacedPath(
            operation.path,
            replacedPaths,
            "set-attr.path",
          );
          assertSafeAttribute(operation.name, operation.value, "set-attr.name");
          if (operation.value !== null && typeof operation.value !== "string") {
            throw new Error("set-attr.value must be a string or null");
          }
          const target = resolveNode(candidateRoot, operation.path);
          if (!(target instanceof HTMLElement)) {
            throw new Error("set-attr target is not an element");
          }
          const key = `${pathKey(operation.path)}|${operation.name}`;
          if (attrOps.has(key)) {
            throw new Error("duplicate attribute operation");
          }
          attrOps.add(key);
          break;
        }

        case "remove-attr": {
          assertDisplayPath(operation.path, "remove-attr.path");
          assertNotUnderReplacedPath(
            operation.path,
            replacedPaths,
            "remove-attr.path",
          );
          assertSafeAttribute(operation.name, null, "remove-attr.name");
          const target = resolveNode(candidateRoot, operation.path);
          if (!(target instanceof HTMLElement)) {
            throw new Error("remove-attr target is not an element");
          }
          const key = `${pathKey(operation.path)}|${operation.name}`;
          if (attrOps.has(key)) {
            throw new Error("duplicate attribute operation");
          }
          attrOps.add(key);
          break;
        }

        case "replace-text": {
          assertDisplayPath(operation.path, "replace-text.path");
          assertNotUnderReplacedPath(
            operation.path,
            replacedPaths,
            "replace-text.path",
          );
          if (typeof operation.value !== "string") {
            throw new Error("replace-text.value must be a string");
          }
          const target = resolveNode(candidateRoot, operation.path);
          if (!target || target.nodeType !== Node.TEXT_NODE) {
            throw new Error("replace-text target is not a text node");
          }
          const key = pathKey(operation.path);
          if (textOps.has(key)) {
            throw new Error("duplicate replace-text operation");
          }
          textOps.add(key);
          break;
        }

        case "replace-node": {
          assertDisplayPath(operation.path, "replace-node.path");
          if (operation.path.length === 0) {
            throw new Error("replace-node cannot replace the canvas root");
          }
          assertRenderNode(operation.node, "replace-node.node");
          const target = resolveNode(candidateRoot, operation.path);
          if (!target?.parentNode) {
            throw new Error("replace-node target is missing");
          }
          break;
        }

        case "remove-child": {
          assertDisplayPath(operation.parentPath, "remove-child.parentPath");
          assertNotUnderReplacedPath(
            operation.parentPath,
            replacedPaths,
            "remove-child.parentPath",
          );
          if (!Number.isInteger(operation.index) || operation.index < 0) {
            throw new Error("remove-child.index is invalid");
          }
          const targetPath = [...operation.parentPath, operation.index];
          if (
            replacedPaths.some(
              (path) =>
                pathKey(path) === pathKey(targetPath) ||
                isSameOrDescendantPath(path, targetPath),
            )
          ) {
            throw new Error("remove-child conflicts with replace-node");
          }
          const parent = resolveNode(candidateRoot, operation.parentPath);
          if (!(parent instanceof HTMLElement)) {
            throw new Error("remove-child parent is not an element");
          }
          const parentKey = pathKey(operation.parentPath);
          const count =
            virtualChildCounts.get(parentKey) ?? parent.childNodes.length;
          if (operation.index >= count) {
            throw new Error("remove-child index is out of range");
          }
          const key = `${parentKey}|${operation.index}`;
          if (removeChildOps.has(key)) {
            throw new Error("duplicate remove-child operation");
          }
          removeChildOps.add(key);
          virtualChildCounts.set(parentKey, count - 1);
          break;
        }

        case "insert-child": {
          assertDisplayPath(operation.parentPath, "insert-child.parentPath");
          assertNotUnderReplacedPath(
            operation.parentPath,
            replacedPaths,
            "insert-child.parentPath",
          );
          if (!Number.isInteger(operation.index) || operation.index < 0) {
            throw new Error("insert-child.index is invalid");
          }
          assertRenderNode(operation.node, "insert-child.node");
          const parent = resolveNode(candidateRoot, operation.parentPath);
          if (!(parent instanceof HTMLElement)) {
            throw new Error("insert-child parent is not an element");
          }
          const parentKey = pathKey(operation.parentPath);
          const count =
            virtualChildCounts.get(parentKey) ?? parent.childNodes.length;
          if (operation.index > count) {
            throw new Error("insert-child index is out of range");
          }
          const key = `${parentKey}|${operation.index}`;
          if (insertChildOps.has(key)) {
            throw new Error("duplicate insert-child operation");
          }
          insertChildOps.add(key);
          virtualChildCounts.set(parentKey, count + 1);
          break;
        }

        default:
          throw new Error(`unknown canvas patch op: ${operation.op}`);
      }

      applyCanvasPatch(candidateRoot, [operation]);
    }

    assertInteractionSet(candidateRoot, interactions);
  }

  function getCanvasResyncState(name) {
    let state = canvasResyncMap.get(name);
    if (!state) {
      state = {
        inFlight: false,
        requestId: 0,
        buffer: [],
        bufferedBytes: 0,
        refreshAfterCurrent: false,
        overflowCount: 0,
        failureCount: 0,
      };
      canvasResyncMap.set(name, state);
    }
    return state;
  }

  function serializedByteLength(value) {
    return textEncoder.encode(JSON.stringify(value)).length;
  }

  function surfaceCanvasResyncError(name, reason) {
    setEditorStatus(`Canvas "${name}" resync failed: ${reason}`, true);
  }

  function noteCanvasResyncFailure(name, err) {
    const state = getCanvasResyncState(name);
    state.failureCount++;
    if (state.failureCount >= CANVAS_RESYNC_MAX_REPEATED_FAILURES) {
      surfaceCanvasResyncError(name, String(err));
      return false;
    }
    return true;
  }

  function clearCanvasPatchBuffer(state) {
    state.buffer = [];
    state.bufferedBytes = 0;
  }

  function bufferCanvasPatch(name, msg) {
    const state = getCanvasResyncState(name);
    state.buffer.push(msg);
    state.bufferedBytes += serializedByteLength(msg);
    if (
      state.buffer.length > CANVAS_RESYNC_MAX_BUFFERED_PATCHES ||
      state.bufferedBytes > CANVAS_RESYNC_MAX_BUFFERED_BYTES
    ) {
      clearCanvasPatchBuffer(state);
      state.overflowCount++;
      if (state.overflowCount >= CANVAS_RESYNC_MAX_REPEATED_FAILURES) {
        surfaceCanvasResyncError(name, "patch buffer overflow");
      }
      if (state.inFlight) {
        state.refreshAfterCurrent = true;
      }
    }
  }

  async function requestCanvasResync(name) {
    const state = getCanvasResyncState(name);
    if (state.inFlight) return;

    const requestId = ++state.requestId;
    state.inFlight = true;
    try {
      const url = padUrl(
        `sessions/${sessionId}/canvas?name=${encodeURIComponent(name)}`,
      );
      const res = await fetch(url);
      if (!res.ok) {
        throw new Error(`resync failed: ${res.status}`);
      }

      const msg = await res.json();
      if (state.requestId !== requestId) {
        return;
      }

      state.inFlight = false;
      if (state.refreshAfterCurrent) {
        state.refreshAfterCurrent = false;
        clearCanvasPatchBuffer(state);
        void requestCanvasResync(name);
        return;
      }
      if (
        msg.type !== PAD_EVENTS.canvasSnapshot &&
        msg.type !== PAD_EVENTS.canvasReplace
      ) {
        throw new Error("resync response did not contain a canvas snapshot");
      }

      handleCanvasFullEvent(msg, { requestedName: name });
    } catch (err) {
      if (state.requestId !== requestId) {
        return;
      }

      state.inFlight = false;
      console.error("[DuetsPad] canvas resync failed", err);
      if (noteCanvasResyncFailure(name, err)) {
        void requestCanvasResync(name);
      }
    }
  }

  function drainCanvasPatchBuffer(name) {
    const state = canvasResyncMap.get(name);
    if (!state || state.inFlight) return;

    const buffered = state.buffer;
    clearCanvasPatchBuffer(state);
    for (let i = 0; i < buffered.length; i++) {
      const msg = buffered[i];
      handleCanvasPatchEvent(msg);
      if (state.inFlight) {
        const remaining = buffered.slice(i + 1);
        if (remaining.length > 0) {
          state.buffer.push(...remaining);
          state.bufferedBytes += remaining.reduce(
            (total, patch) => total + serializedByteLength(patch),
            0,
          );
        }
        break;
      }
    }

    if (!state.inFlight && state.buffer.length === 0) {
      canvasResyncMap.delete(name);
    }
  }

  function handleCanvasFullEvent(msg, options = {}) {
    const responseName = getCanvasName(msg);
    const name = options.requestedName ?? responseName;
    try {
      if (
        options.requestedName !== undefined &&
        responseName !== options.requestedName
      ) {
        throw new Error(
          "resync response canvas name did not match the request",
        );
      }

      const applied = renderCanvasFullState(msg);
      const state = canvasResyncMap.get(name);
      if (state) {
        state.failureCount = 0;
        if (applied && options.requestedName === undefined && state.inFlight) {
          state.requestId++;
          state.inFlight = false;
          state.refreshAfterCurrent = false;
        }
      }
      drainCanvasPatchBuffer(name);
    } catch (err) {
      console.error("[DuetsPad] canvas full-state event rejected", err);
      const state = canvasResyncMap.get(name);
      let shouldRetry = true;
      if (state) {
        shouldRetry = noteCanvasResyncFailure(name, err);
        if (state.inFlight) {
          state.requestId++;
          state.inFlight = false;
          state.refreshAfterCurrent = false;
        }
      }
      if (shouldRetry) {
        void requestCanvasResync(name);
      }
    }
  }

  function handleCanvasPatchEvent(msg) {
    const name = getCanvasName(msg);
    const baseRevision = msg.baseRevision;
    const revision = msg.revision;

    if (
      !isRevision(baseRevision) ||
      !isRevision(revision) ||
      revision !== baseRevision + 1
    ) {
      console.error("[DuetsPad] malformed canvas patch revision", msg);
      const state = canvasResyncMap.get(name);
      if (state?.inFlight) {
        state.refreshAfterCurrent = true;
      }
      void requestCanvasResync(name);
      return;
    }

    const resyncState = canvasResyncMap.get(name);
    if (resyncState?.inFlight) {
      bufferCanvasPatch(name, msg);
      return;
    }

    const currentRevision = canvasRevisionMap.get(name);
    if (currentRevision === undefined || baseRevision > currentRevision) {
      bufferCanvasPatch(name, msg);
      void requestCanvasResync(name);
      return;
    }

    if (baseRevision < currentRevision) {
      return;
    }

    const panel = ensureCanvasPanel(name);
    if (!panel) {
      bufferCanvasPatch(name, msg);
      void requestCanvasResync(name);
      return;
    }

    try {
      const body = panel.firstChild;
      preflightCanvasPatch(body, msg.operations, msg.interactions);
      applyCanvasPatch(body, msg.operations);
      const signal = resetCanvasInteractionBindings(name);
      applyInteractions(body, msg.interactions, signal);
      bindFields(body, signal);
      canvasRevisionMap.set(name, revision);
      drainCanvasPatchBuffer(name);
    } catch (err) {
      console.error("[DuetsPad] canvas patch rejected", err);
      void requestCanvasResync(name);
    }
  }

  function handleCanvasEvent(msg) {
    if (
      msg.type === PAD_EVENTS.canvasSnapshot ||
      msg.type === PAD_EVENTS.canvasReplace
    ) {
      handleCanvasFullEvent(msg);
    } else if (msg.type === PAD_EVENTS.canvasPatch) {
      handleCanvasPatchEvent(msg);
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
    canvasRevisionMap.clear();
    canvasResyncMap.clear();
    for (const controller of canvasInteractionControllerMap.values()) {
      controller.abort();
    }
    canvasInteractionControllerMap.clear();
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
