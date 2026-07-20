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
    dialogSnapshot: "dialog.snapshot",
    dialogOpen: "dialog.open",
    dialogPatch: "dialog.patch",
    dialogReplace: "dialog.replace",
    dialogClose: "dialog.close",
    typeDeclaration: "type.declaration",
    taggedTemplateSnapshot: "taggedTemplate.snapshot",
  };

  // URL helpers

  function padUrl(path) {
    return new URL(path, document.baseURI).href;
  }

  // Access token handling (ADR-49)
  // The token reaches the page via the URL fragment (#token=...): the fragment is
  // never sent to the server and never appears in access logs. It is moved into
  // sessionStorage on load and stripped from the address bar; every session-API
  // request then carries it explicitly in an Authorization: Bearer header. The
  // explicit (non-ambient) attachment is what makes the scheme CSRF-immune.

  const TOKEN_STORAGE_KEY = "duetspad.token";

  (function captureTokenFromFragment() {
    const hash = window.location.hash;
    if (!hash.startsWith("#token=")) return;
    const raw = hash.slice("#token=".length);
    let token;
    try {
      token = decodeURIComponent(raw);
    } catch {
      // Malformed percent-escapes: fall back to the raw text rather than letting the
      // URIError abort client startup, which would leave no UI to prompt for a token.
      token = raw;
    }
    if (token) {
      sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
    }
    history.replaceState(
      null,
      "",
      window.location.pathname + window.location.search,
    );
  })();

  /**
   * fetch wrapper for session-API requests: attaches the stored access token as an
   * Authorization: Bearer header when one is present, and funnels 401 responses
   * into the in-page token prompt (ADR-49). The 401 response is still returned so
   * callers keep their existing error paths.
   * @param {string} url - Absolute URL (already passed through padUrl).
   * @param {object} [init] - fetch init options; headers are merged, not replaced.
   */
  async function padFetch(url, init = {}) {
    const token = sessionStorage.getItem(TOKEN_STORAGE_KEY);
    const headers = token
      ? { ...(init.headers ?? {}), Authorization: `Bearer ${token}` }
      : (init.headers ?? {});
    const res = await fetch(url, { ...init, headers });
    if (res.status === 401) {
      showTokenPrompt();
    }
    return res;
  }

  let tokenPromptShown = false;

  /**
   * Shows a modal overlay asking for the access token. On submit the token is
   * stored in sessionStorage and the page reloads — the reload re-runs the session
   * bootstrap with the new credential, and editor content survives because it is
   * saved on pagehide. Built with DOM APIs only (no innerHTML; see the
   * render-node projection security note).
   */
  function showTokenPrompt() {
    if (tokenPromptShown) return;
    tokenPromptShown = true;

    const overlay = document.createElement("div");
    overlay.id = "token-prompt-overlay";
    overlay.style.cssText =
      "position:fixed;inset:0;z-index:2000;display:flex;align-items:center;" +
      "justify-content:center;background:rgba(0,0,0,0.5)";

    const card = document.createElement("div");
    card.className = "card";
    card.style.cssText = "max-width:22rem;width:90%";

    const cardBody = document.createElement("div");
    cardBody.className = "card-body";

    const title = document.createElement("h3");
    title.className = "card-title";
    title.textContent = "Access token required";

    const text = document.createElement("p");
    text.className = "text-secondary";
    text.textContent =
      "This pad requires an access token. Enter it below, or open the pad " +
      "through a #token=… link.";

    const input = document.createElement("input");
    input.type = "password";
    input.className = "form-control mb-2";
    input.placeholder = "Access token";
    input.autofocus = true;

    const button = document.createElement("button");
    button.type = "button";
    button.className = "btn btn-primary w-100";
    button.textContent = "Unlock";

    const submit = () => {
      const token = input.value.trim();
      if (!token) return;
      sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
      window.location.reload();
    };
    button.addEventListener("click", submit);
    input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") submit();
    });

    cardBody.append(title, text, input, button);
    card.appendChild(cardBody);
    overlay.appendChild(card);
    document.body.appendChild(overlay);
    input.focus();
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

    const res = await padFetch(padUrl("sessions"), {
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

  // Picker id to the currently unsettled selection. Entries survive failures so
  // interaction invocation remains blocked until the user makes a new selection.
  const attachmentUploadMap = new Map();
  const attachmentRevisionOverrideMap = new Map();
  const attachmentProjectionWaiters = new Set();
  const ATTACHMENT_CLIENT_ID_STORAGE_KEY = "duetspad.attachmentClientId";
  const ATTACHMENT_GENERATION_STORAGE_KEY = "duetspad.attachmentGeneration";
  const storedAttachmentClientId = sessionStorage.getItem(
    ATTACHMENT_CLIENT_ID_STORAGE_KEY,
  );
  let attachmentClientId =
    typeof storedAttachmentClientId === "string" &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
      storedAttachmentClientId,
    )
      ? storedAttachmentClientId
      : crypto.randomUUID();
  sessionStorage.setItem(ATTACHMENT_CLIENT_ID_STORAGE_KEY, attachmentClientId);
  const storedAttachmentGeneration = Number(
    sessionStorage.getItem(ATTACHMENT_GENERATION_STORAGE_KEY),
  );
  let attachmentSelectionGeneration =
    Number.isSafeInteger(storedAttachmentGeneration) &&
    storedAttachmentGeneration >= 0
      ? storedAttachmentGeneration
      : 0;

  function nextAttachmentSelectionGeneration() {
    if (attachmentSelectionGeneration >= Number.MAX_SAFE_INTEGER) {
      attachmentClientId = crypto.randomUUID();
      attachmentSelectionGeneration = 0;
      sessionStorage.setItem(
        ATTACHMENT_CLIENT_ID_STORAGE_KEY,
        attachmentClientId,
      );
    }
    attachmentSelectionGeneration++;
    sessionStorage.setItem(
      ATTACHMENT_GENERATION_STORAGE_KEY,
      String(attachmentSelectionGeneration),
    );
    return attachmentSelectionGeneration;
  }

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
    if (kind === "file") return;
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
    if (kind === "file") return null;
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
      const res = await padFetch(
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

  function attachmentSelectionUrl(pickerId, token = null) {
    const base = `sessions/${sessionId}/attachments/${encodeURIComponent(pickerId)}/selections`;
    return padUrl(token ? `${base}/${encodeURIComponent(token)}` : base);
  }

  async function readAttachmentResponse(res, fallback, observe = null) {
    let data;
    try {
      data = await res.json();
    } catch {
      throw new Error(`${fallback}: ${res.status}`);
    }
    observe?.(data);
    if (!res.ok || data.ok !== true) {
      throw new Error(data.error ?? `${fallback}: ${res.status}`);
    }
    return data;
  }

  function requestAttachmentSelectionCancellation(pickerId, entry) {
    if (!entry.token || entry.cancelRequested) return;
    entry.cancelRequested = true;
    void padFetch(attachmentSelectionUrl(pickerId, entry.token), {
      method: "DELETE",
    }).catch(() => {
      // A newer begin also retires this token server-side; cancellation is best effort.
    });
  }

  async function cancelFailedAttachmentSelection(wrapper, button) {
    const pickerId = wrapper.getAttribute("data-duetspad-field");
    const revision = Number(
      wrapper.getAttribute("data-duetspad-attachment-revision"),
    );
    if (!pickerId || !isRevision(revision) || revision === 0) return;

    button.disabled = true;
    try {
      const response = await padFetch(
        `${attachmentSelectionUrl(pickerId)}/failed?revision=${encodeURIComponent(revision)}`,
        { method: "DELETE" },
      );
      await readAttachmentResponse(
        response,
        "failed attachment selection cancellation failed",
      );
      const entry = attachmentUploadMap.get(pickerId);
      if (entry?.revision === revision) {
        entry.superseded = true;
        entry.controller.abort();
        attachmentUploadMap.delete(pickerId);
      }
    } catch (err) {
      button.disabled = false;
      setEditorStatus(String(err), true);
    }
  }

  function notifyAttachmentProjectionChanged() {
    for (const resolve of attachmentProjectionWaiters) resolve();
    attachmentProjectionWaiters.clear();
  }

  function waitForAttachmentProjectionChange(timeoutMs) {
    return new Promise((resolve) => {
      let timeout = null;
      const finish = () => {
        attachmentProjectionWaiters.delete(finish);
        if (timeout !== null) clearTimeout(timeout);
        resolve();
      };
      attachmentProjectionWaiters.add(finish);
      timeout = setTimeout(finish, timeoutMs);
    });
  }

  function cancelAttachmentSelection(pickerId, entry) {
    entry.superseded = true;
    entry.controller.abort();
    requestAttachmentSelectionCancellation(pickerId, entry);
  }

  async function uploadAttachmentSelection(pickerId, files, entry) {
    const beginResponse = await padFetch(attachmentSelectionUrl(pickerId), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        clientId: attachmentClientId,
        generation: entry.generation,
        files: files.map((file) => ({
          name: file.name,
          contentType: file.type,
          size: file.size,
        })),
      }),
    });
    const begin = await readAttachmentResponse(
      beginResponse,
      "attachment selection rejected",
      (data) => {
        if (typeof data.token !== "string" || !isRevision(data.revision)) {
          return;
        }
        entry.token = data.token;
        entry.revision = data.revision;
        attachmentRevisionOverrideMap.set(pickerId, data.revision);
        if (entry.superseded) {
          requestAttachmentSelectionCancellation(pickerId, entry);
        }
      },
    );
    if (entry.superseded) {
      requestAttachmentSelectionCancellation(pickerId, entry);
      return;
    }
    if (!Array.isArray(begin.files) || begin.files.length !== files.length) {
      throw new Error(
        "attachment selection response did not match the manifest",
      );
    }

    try {
      await Promise.all(
        files.map(async (file, index) => {
          const serverFile = begin.files[index];
          const uploadResponse = await padFetch(
            `${attachmentSelectionUrl(pickerId, entry.token)}/files/${encodeURIComponent(serverFile.id)}`,
            {
              method: "POST",
              headers: { "Content-Type": "application/octet-stream" },
              body: file,
              signal: entry.controller.signal,
            },
          );
          await readAttachmentResponse(
            uploadResponse,
            "attachment upload failed",
          );
        }),
      );
    } catch (err) {
      entry.controller.abort();
      throw err;
    }

    const commitResponse = await padFetch(
      `${attachmentSelectionUrl(pickerId, entry.token)}/commit`,
      { method: "POST", signal: entry.controller.signal },
    );
    const committed = await readAttachmentResponse(
      commitResponse,
      "attachment commit failed",
    );
    attachmentRevisionOverrideMap.set(pickerId, committed.revision);
  }

  function startAttachmentSelection(wrapper, selectedFiles) {
    const pickerId = wrapper.getAttribute("data-duetspad-field");
    if (!pickerId) return;

    const previous = attachmentUploadMap.get(pickerId);
    if (previous) {
      cancelAttachmentSelection(pickerId, previous);
    }

    const entry = {
      generation: nextAttachmentSelectionGeneration(),
      controller: new AbortController(),
      token: null,
      revision: null,
      cancelRequested: false,
      promise: null,
      failed: false,
      superseded: false,
      reconciledStable: false,
    };
    entry.promise = uploadAttachmentSelection(pickerId, selectedFiles, entry)
      .then(() => {
        if (attachmentUploadMap.get(pickerId) === entry) {
          attachmentUploadMap.delete(pickerId);
        }
      })
      .catch((err) => {
        if (entry.superseded || entry.reconciledStable) return;
        if (attachmentUploadMap.get(pickerId) === entry) {
          entry.failed = true;
          setEditorStatus(String(err), true);
        }
        throw err;
      });
    // The rejection is observed here as well as by awaitAttachmentUploads, avoiding
    // an unhandled-rejection report when no interaction is clicked after a failure.
    entry.promise.catch(() => {});
    attachmentUploadMap.set(pickerId, entry);
  }

  function bindFilePicker(wrapper, signal) {
    const input = wrapper.querySelector("[data-duetspad-file-input]");
    if (!(input instanceof HTMLInputElement)) return;
    input.addEventListener(
      "change",
      () => {
        const files = Array.from(input.files ?? []);
        input.value = "";
        startAttachmentSelection(wrapper, files);
      },
      signal ? { signal } : undefined,
    );
    const cancel = wrapper.querySelector("[data-duetspad-attachment-cancel]");
    if (cancel instanceof HTMLButtonElement) {
      cancel.addEventListener(
        "click",
        () => {
          void cancelFailedAttachmentSelection(wrapper, cancel);
        },
        signal ? { signal } : undefined,
      );
    }
  }

  function fieldElements(root) {
    if (!root || typeof root.querySelectorAll !== "function") return [];
    const fields = Array.from(root.querySelectorAll("[data-duetspad-field]"));
    if (root.matches?.("[data-duetspad-field]")) fields.unshift(root);
    return fields;
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
    for (const el of fieldElements(root)) {
      if (el.getAttribute("data-duetspad-field-kind") === "file") {
        bindFilePicker(el, signal);
      } else {
        bindFieldElement(el, signal);
      }
    }
    queueMicrotask(reconcileAttachmentUploads);
    notifyAttachmentProjectionChanged();
  }

  /**
   * Collects { fieldId: value } for every field-marked element within root,
   * for folding into an interaction-invoke request body (ADR-47).
   * @param {Element} root
   */
  function collectFieldSnapshot(root) {
    const fields = {};
    if (!root || typeof root.querySelectorAll !== "function") return fields;
    for (const el of fieldElements(root)) {
      if (el.getAttribute("data-duetspad-field-kind") === "file") continue;
      const fieldId = el.getAttribute("data-duetspad-field");
      if (!fieldId) continue;
      const value = fieldCurrentValue(el);
      if (value === null) continue;
      fields[fieldId] = value;
    }
    return fields;
  }

  function retainedAttachmentPickerStates() {
    const pickers = new Map();
    const roots = [
      ...canvasPanelMap.values(),
      ...timelineEntryMap.values(),
      ...Array.from(dialogMap.values(), (entry) => entry.root),
    ];
    for (const root of roots) {
      for (const el of fieldElements(root)) {
        if (el.getAttribute("data-duetspad-field-kind") !== "file") continue;
        const pickerId = el.getAttribute("data-duetspad-field");
        const revision = Number(
          el.getAttribute("data-duetspad-attachment-revision"),
        );
        if (pickerId && isRevision(revision)) {
          const current = pickers.get(pickerId);
          if (current === undefined || revision > current.revision) {
            pickers.set(pickerId, {
              revision,
              status: el.getAttribute("data-duetspad-attachment-status"),
            });
          }
        }
      }
    }
    return pickers;
  }

  function reconcileAttachmentUploads() {
    const retained = retainedAttachmentPickerStates();
    for (const [pickerId, entry] of attachmentUploadMap) {
      if (!retained.has(pickerId)) {
        cancelAttachmentSelection(pickerId, entry);
        attachmentUploadMap.delete(pickerId);
        attachmentRevisionOverrideMap.delete(pickerId);
        continue;
      }

      const state = retained.get(pickerId);
      if (
        entry.revision !== null &&
        state.status === "stable" &&
        state.revision >= entry.revision
      ) {
        entry.reconciledStable = true;
        attachmentUploadMap.delete(pickerId);
      }
    }
    for (const [pickerId, overrideRevision] of attachmentRevisionOverrideMap) {
      const domRevision = retained.get(pickerId)?.revision;
      if (
        (!retained.has(pickerId) && !attachmentUploadMap.has(pickerId)) ||
        (domRevision !== undefined && domRevision >= overrideRevision)
      ) {
        attachmentRevisionOverrideMap.delete(pickerId);
      }
    }
  }

  async function awaitAttachmentUploads() {
    while (true) {
      const unsettled = Array.from(attachmentUploadMap.values());
      if (unsettled.length === 0) return;
      await Promise.all(unsettled.map((entry) => entry.promise));
      if (attachmentUploadMap.size === 0) return;
    }
  }

  function collectAttachmentSnapshot() {
    const attachments = {};
    for (const [pickerId, state] of retainedAttachmentPickerStates()) {
      attachments[pickerId] =
        attachmentRevisionOverrideMap.get(pickerId) ?? state.revision;
    }
    return attachments;
  }

  async function invokeInteraction(handlerId, surfaceRoot) {
    if (!handlerId) return false;
    try {
      for (let attempt = 0; attempt < 3; attempt++) {
        await awaitAttachmentUploads();
        const res = await padFetch(
          padUrl(
            `sessions/${sessionId}/interactions/${encodeURIComponent(handlerId)}/invoke`,
          ),
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              fields: collectFieldSnapshot(surfaceRoot),
              attachments: collectAttachmentSnapshot(),
            }),
          },
        );
        const data = await res.json();
        if (res.ok && data.ok === true) return true;
        if (data.attachmentConflict === true && attempt < 2) {
          await waitForAttachmentProjectionChange(50 * 2 ** attempt);
          continue;
        }
        throw new Error(
          data.error ?? `interaction invoke failed: ${res.status}`,
        );
      }
    } catch (err) {
      console.error("[DuetsPad] interaction invoke failed", err);
      setEditorStatus(String(err), true);
      return false;
    }
    return false;
  }

  function applyInteractions(root, interactions, signal, onInvoke) {
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
            const pending = invokeInteraction(interaction.handlerId, root);
            onInvoke?.(pending, target);
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
    const res = await padFetch(url, {
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
      const res = await padFetch(padUrl(`sessions/${sessionId}/complete`), {
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

  // Module-scoped handle for the fetch-based SSE stream (see openSse).
  // Kept here so swapSession() can close the old stream before opening the new one.
  let activeEventStream = null;

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
    queueMicrotask(reconcileAttachmentUploads);
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
        queueMicrotask(reconcileAttachmentUploads);
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
      const res = await padFetch(url);
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
    queueMicrotask(reconcileAttachmentUploads);

    // Notify the UI layer so it can rebuild canvas tabs.
    if (onCanvasCreatedCallback) {
      try {
        onCanvasCreatedCallback(null);
      } catch {
        // Ignore.
      }
    }
  }

  // Dialog state

  const dialogMap = new Map();
  const dialogOrder = [];
  const dialogSizes = new Set(["sm", "md", "lg", "xl"]);
  let dialogRestoreFocus = null;
  let dialogResyncScheduled = false;

  function dialogActionButton(root, actionId) {
    for (const wrapper of root.querySelectorAll(
      "[data-duetspad-dialog-action]",
    )) {
      if (wrapper.getAttribute("data-duetspad-dialog-action") === actionId) {
        return wrapper.querySelector("button");
      }
    }
    return null;
  }

  function setDialogPending(entry, pending) {
    entry.pending = pending;
    entry.root.inert = pending;
    for (const button of entry.layer.querySelectorAll("button")) {
      button.disabled = pending;
    }
  }

  function bindDialogProjection(entry, interactions) {
    entry.controller?.abort();
    entry.controller = new AbortController();
    const signal = entry.controller.signal;
    applyInteractions(entry.root, interactions, signal, (pending, target) => {
      const closesDialog =
        target.matches?.("[data-duetspad-dialog-dismiss-handler]") ||
        target.closest?.("[data-duetspad-dialog-action]");
      if (!closesDialog) return;
      setDialogPending(entry, true);
      void pending.then((ok) => {
        if (!ok && dialogMap.get(entry.id) === entry) {
          setDialogPending(entry, false);
          if (target.isConnected && typeof target.focus === "function") {
            target.focus();
          }
        }
      });
    });
    bindFields(entry.root, signal);

    entry.layer.addEventListener(
      "click",
      (event) => {
        if (event.target === entry.layer) requestDialogDismiss(entry);
      },
      { signal },
    );
    entry.closeButton?.addEventListener(
      "click",
      () => requestDialogDismiss(entry),
      { signal },
    );
    entry.layer.addEventListener(
      "keydown",
      (event) => handleDialogKeydown(entry, event),
      { signal },
    );
  }

  function requestDialogDismiss(entry) {
    if (!entry.canDismiss || entry.pending || dialogOrder[0] !== entry.id)
      return;
    const dismiss = entry.root.querySelector(
      "[data-duetspad-dialog-dismiss-handler]",
    );
    dismiss?.click();
  }

  function handleDialogKeydown(entry, event) {
    if (dialogOrder[0] !== entry.id) return;
    if (event.key === "Escape" && entry.canDismiss) {
      event.preventDefault();
      requestDialogDismiss(entry);
      return;
    }
    if (
      event.key === "Enter" &&
      !event.isComposing &&
      entry.defaultButtonId &&
      !(event.target instanceof HTMLTextAreaElement) &&
      !(event.target instanceof HTMLSelectElement) &&
      !(event.target instanceof HTMLButtonElement) &&
      !(event.target instanceof HTMLAnchorElement) &&
      !event.target.isContentEditable
    ) {
      const button = dialogActionButton(entry.root, entry.defaultButtonId);
      if (button && !button.disabled) {
        event.preventDefault();
        button.click();
      }
      return;
    }
    if (event.key !== "Tab") return;

    const focusable = Array.from(
      entry.dialog.querySelectorAll(
        'button:not([disabled]):not([tabindex="-1"]), input:not([disabled]), textarea:not([disabled]), select:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])',
      ),
    ).filter((element) => !element.closest("[hidden]"));
    if (focusable.length === 0) {
      event.preventDefault();
      entry.dialog.focus();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  function createDialogEntry(projection) {
    if (
      typeof projection.dialogId !== "string" ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
        projection.dialogId,
      )
    ) {
      throw new Error("dialog projection id is invalid");
    }
    if (
      (projection.title !== null && typeof projection.title !== "string") ||
      !dialogSizes.has(projection.size) ||
      (projection.defaultButtonId !== null &&
        typeof projection.defaultButtonId !== "string") ||
      typeof projection.canDismiss !== "boolean" ||
      (projection.dismissButtonId !== null &&
        typeof projection.dismissButtonId !== "string") ||
      typeof projection.claimed !== "boolean"
    ) {
      throw new Error("dialog projection options are invalid");
    }
    assertCanvasRootNode(projection.state);
    const root = projectNode(projection.state);
    assertInteractionSet(root, projection.interactions);

    const layer = document.createElement("div");
    layer.className = "duetspad-dialog-layer";

    const dialog = document.createElement("section");
    dialog.className = `duetspad-dialog duetspad-dialog-${projection.size ?? "md"}`;
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-modal", "true");
    dialog.tabIndex = -1;

    let closeButton = null;
    if (projection.title || projection.canDismiss) {
      const header = document.createElement("header");
      header.className = "duetspad-dialog-header";
      const title = document.createElement("h2");
      title.className = "duetspad-dialog-title";
      title.textContent = projection.title ?? "Dialog";
      const titleId = `duetspad-dialog-title-${projection.dialogId}`;
      title.id = titleId;
      dialog.setAttribute("aria-labelledby", titleId);
      header.appendChild(title);
      if (projection.canDismiss) {
        closeButton = document.createElement("button");
        closeButton.type = "button";
        closeButton.className = "btn-close";
        closeButton.setAttribute("aria-label", "Close dialog");
        header.appendChild(closeButton);
      }
      dialog.appendChild(header);
    } else {
      dialog.setAttribute("aria-label", "Dialog");
    }

    dialog.appendChild(root);
    layer.appendChild(dialog);
    return {
      id: projection.dialogId,
      revision: projection.revision,
      root,
      layer,
      dialog,
      closeButton,
      canDismiss: projection.canDismiss === true,
      defaultButtonId: projection.defaultButtonId ?? null,
      controller: null,
      pending: projection.claimed === true,
    };
  }

  function captureDialogEdits(entry) {
    const occurrences = new Map();
    const edits = [];
    for (const field of fieldElements(entry.root)) {
      const fieldId = field.getAttribute("data-duetspad-field");
      const kind = field.getAttribute("data-duetspad-field-kind");
      if (!fieldId || !kind) continue;
      const option =
        kind === "radio" ? (field.getAttribute("value") ?? "") : "";
      const key = `${fieldId}\u0000${kind}\u0000${option}`;
      const occurrence = occurrences.get(key) ?? 0;
      occurrences.set(key, occurrence + 1);
      if (!isFieldGuarded(field)) continue;

      edits.push({
        key,
        occurrence,
        value: fieldCurrentValue(field),
        checked: "checked" in field ? field.checked : null,
        editGen: field.dataset.duetspadEditGen,
        pending: field.dataset.duetspadPending,
        focused: document.activeElement === field,
        selectionStart: "selectionStart" in field ? field.selectionStart : null,
        selectionEnd: "selectionEnd" in field ? field.selectionEnd : null,
        selectionDirection:
          "selectionDirection" in field ? field.selectionDirection : null,
      });
    }
    return edits;
  }

  function restoreDialogEdits(entry, edits) {
    if (edits.length === 0) return;
    const candidates = new Map();
    for (const field of fieldElements(entry.root)) {
      const fieldId = field.getAttribute("data-duetspad-field");
      const kind = field.getAttribute("data-duetspad-field-kind");
      if (!fieldId || !kind) continue;
      const option =
        kind === "radio" ? (field.getAttribute("value") ?? "") : "";
      const key = `${fieldId}\u0000${kind}\u0000${option}`;
      const fields = candidates.get(key) ?? [];
      fields.push(field);
      candidates.set(key, fields);
    }

    for (const edit of edits) {
      const field = candidates.get(edit.key)?.[edit.occurrence];
      if (!(field instanceof HTMLElement)) continue;
      if (edit.checked !== null && "checked" in field) {
        field.checked = edit.checked;
      } else if (edit.value !== null && "value" in field) {
        field.value = edit.value;
      }
      if (edit.editGen !== undefined)
        field.dataset.duetspadEditGen = edit.editGen;
      if (edit.pending !== undefined)
        field.dataset.duetspadPending = edit.pending;

      if (edit.focused) {
        field.focus();
        if (
          edit.selectionStart !== null &&
          edit.selectionEnd !== null &&
          typeof field.setSelectionRange === "function"
        ) {
          field.setSelectionRange(
            edit.selectionStart,
            edit.selectionEnd,
            edit.selectionDirection ?? undefined,
          );
        }
      }
    }
  }

  function addOrReplaceDialog(projection) {
    if (!projection || typeof projection.dialogId !== "string") {
      throw new Error("dialog projection id is invalid");
    }
    if (!isRevision(projection.revision)) {
      throw new Error("dialog projection revision is invalid");
    }
    const existing = dialogMap.get(projection.dialogId);
    if (existing && projection.revision <= existing.revision) return;

    const edits = existing ? captureDialogEdits(existing) : [];
    const entry = createDialogEntry(projection);
    const container = document.getElementById("dialog-container");
    if (!container) return;
    if (existing) {
      existing.controller?.abort();
      entry.layer.hidden = existing.layer.hidden;
      existing.layer.replaceWith(entry.layer);
    } else {
      dialogOrder.push(entry.id);
      container.appendChild(entry.layer);
    }
    restoreDialogEdits(entry, edits);
    dialogMap.set(entry.id, entry);
    bindDialogProjection(entry, projection.interactions);
    if (entry.pending) setDialogPending(entry, true);
    updateDialogPresentation();
  }

  function removeDialog(dialogId) {
    const entry = dialogMap.get(dialogId);
    if (!entry) return;
    entry.controller?.abort();
    entry.layer.remove();
    dialogMap.delete(dialogId);
    const index = dialogOrder.indexOf(dialogId);
    if (index >= 0) dialogOrder.splice(index, 1);
    updateDialogPresentation();
    queueMicrotask(reconcileAttachmentUploads);
  }

  function updateDialogPresentation() {
    const activeId = dialogOrder[0];
    const app = document.getElementById("app");
    const toasts = document.getElementById("toast-container");
    const hasDialog = activeId !== undefined;
    if (hasDialog && !dialogRestoreFocus) {
      dialogRestoreFocus = document.activeElement;
    }
    if (app) app.inert = hasDialog;
    if (toasts) toasts.inert = hasDialog;

    for (const [id, entry] of dialogMap) {
      entry.layer.hidden = id !== activeId;
    }

    if (hasDialog) {
      const entry = dialogMap.get(activeId);
      queueMicrotask(() => {
        if (dialogOrder[0] !== activeId) return;
        if (entry.dialog.contains(document.activeElement)) return;
        const preferred =
          !entry.pending && entry.defaultButtonId
            ? dialogActionButton(entry.root, entry.defaultButtonId)
            : null;
        const first = entry.pending
          ? null
          : entry.dialog.querySelector(
              'input:not([disabled]), textarea:not([disabled]), select:not([disabled]), button:not([disabled]):not([tabindex="-1"]), a[href]',
            );
        (preferred ?? first ?? entry.dialog).focus();
      });
    } else if (dialogRestoreFocus) {
      const restore = dialogRestoreFocus;
      dialogRestoreFocus = null;
      if (restore.isConnected && typeof restore.focus === "function")
        restore.focus();
    }
  }

  function resetDialogs() {
    for (const entry of dialogMap.values()) entry.controller?.abort();
    dialogMap.clear();
    dialogOrder.length = 0;
    const container = document.getElementById("dialog-container");
    if (container) container.textContent = "";
    updateDialogPresentation();
    queueMicrotask(reconcileAttachmentUploads);
  }

  function requestDialogResync() {
    if (dialogResyncScheduled) return;
    dialogResyncScheduled = true;
    queueMicrotask(() => {
      dialogResyncScheduled = false;
      activeEventStream?.close();
      activeEventStream = null;
      subscribeSession?.(sessionId);
    });
  }

  function handleDialogEvent(msg) {
    try {
      if (msg.type === PAD_EVENTS.dialogSnapshot) {
        if (!Array.isArray(msg.dialogs)) {
          throw new Error("dialog snapshot must contain an array");
        }
        const retainedIds = new Set();
        for (const projection of msg.dialogs) {
          if (!projection || typeof projection.dialogId !== "string") {
            throw new Error("dialog snapshot projection id is invalid");
          }
          if (retainedIds.has(projection.dialogId)) {
            throw new Error("dialog snapshot contains a duplicate id");
          }
          retainedIds.add(projection.dialogId);
        }
        for (const dialogId of [...dialogOrder]) {
          if (!retainedIds.has(dialogId)) removeDialog(dialogId);
        }
        for (const projection of msg.dialogs) addOrReplaceDialog(projection);
        dialogOrder.splice(0, dialogOrder.length, ...retainedIds);
        updateDialogPresentation();
      } else if (
        msg.type === PAD_EVENTS.dialogOpen ||
        msg.type === PAD_EVENTS.dialogReplace
      ) {
        addOrReplaceDialog(msg.dialog);
      } else if (msg.type === PAD_EVENTS.dialogPatch) {
        const entry = dialogMap.get(msg.dialogId);
        if (
          !entry ||
          !isRevision(msg.baseRevision) ||
          !isRevision(msg.revision) ||
          msg.revision !== msg.baseRevision + 1 ||
          entry.revision !== msg.baseRevision
        ) {
          requestDialogResync();
          return;
        }
        preflightCanvasPatch(entry.root, msg.operations, msg.interactions);
        applyCanvasPatch(entry.root, msg.operations);
        entry.revision = msg.revision;
        bindDialogProjection(entry, msg.interactions);
      } else if (msg.type === PAD_EVENTS.dialogClose) {
        removeDialog(msg.dialogId);
      }
    } catch (err) {
      console.error("[DuetsPad] dialog projection rejected", err);
      requestDialogResync();
    }
  }

  // Session swap

  /**
   * Performs a no-reload session swap:
   * 1. Closes the current event stream.
   * 2. Deletes the old session on the server (best-effort).
   * 3. Creates a new session via POST /sessions.
   * 4. Updates sessionStorage and the module-level sessionId.
   * 5. Clears Canvas, Timeline, and Dialog state (the initial SSE burst will re-populate them).
   * 6. Opens a new event stream on the new session.
   * The editor content is intentionally left untouched.
   */
  async function swapSession() {
    if (sessionSwapInProgress) return;
    sessionSwapInProgress = true;
    setSessionStatus(false);

    const oldId = sessionId;

    // Step 1: close the outgoing event stream.
    if (activeEventStream) {
      activeEventStream.close();
      activeEventStream = null;
    }

    // Step 2: delete the old session (best-effort).
    if (oldId) {
      try {
        await padFetch(padUrl(`sessions/${oldId}`), { method: "DELETE" });
      } catch {
        // Ignore — the old session will eventually be evicted by the server.
      }
    }

    // Step 3: create a new session via POST /sessions (no prior id in the body).
    let newId;
    try {
      const res = await padFetch(padUrl("sessions"), {
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

    // Step 5: clear projected surfaces before the new stream arrives
    // so the old content does not persist during the brief gap.
    resetCanvases();
    resetTimeline();
    resetDialogs();

    // Step 6: open the new event stream.
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

  const TOAST_VARIANTS = new Set(["info", "success", "warning", "danger"]);

  /**
   * Shows a non-blocking toast notification. This is intentionally implemented
   * without Bootstrap JS; DuetsPad only serves Tabler/Bootstrap CSS.
   * @param {string} message - Body text for the toast.
   * @param {{title?: string|null, variant?: string, durationMs?: number,
   *   action?: {label: string, href: string}|null}} [options]
   */
  function showToast(
    message,
    { title = null, variant = "info", durationMs = 5000, action = null } = {},
  ) {
    const container = document.getElementById("toast-container");
    if (!container) return;

    const resolvedVariant = TOAST_VARIANTS.has(variant) ? variant : "info";
    const isUrgent =
      resolvedVariant === "danger" || resolvedVariant === "warning";
    const toastEl = document.createElement("div");
    toastEl.className = `toast align-items-center duetspad-toast duetspad-toast-${resolvedVariant}`;
    toastEl.setAttribute("role", isUrgent ? "alert" : "status");
    toastEl.setAttribute("aria-live", isUrgent ? "assertive" : "polite");
    toastEl.setAttribute("aria-atomic", "true");

    const body = document.createElement("div");
    body.className = "d-flex";

    const bodyInner = document.createElement("div");
    bodyInner.className = "toast-body";
    if (typeof title === "string" && title.length > 0) {
      const titleEl = document.createElement("strong");
      titleEl.className = "duetspad-toast-title";
      titleEl.textContent = title;
      bodyInner.appendChild(titleEl);
    }

    const messageEl = document.createElement("span");
    messageEl.textContent = message;
    bodyInner.appendChild(messageEl);

    if (action) {
      bodyInner.appendChild(document.createTextNode(" "));
      const link = document.createElement("a");
      link.href = action.href;
      link.target = "_blank";
      link.rel = "noopener noreferrer";
      // textContent is safe — no user-supplied HTML
      link.textContent = action.label;
      bodyInner.appendChild(link);
    }

    body.appendChild(bodyInner);

    const closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.className = "btn-close me-2 m-auto";
    closeBtn.setAttribute("aria-label", "Close");
    body.appendChild(closeBtn);

    toastEl.appendChild(body);
    container.appendChild(toastEl);
    toastEl.classList.add("show");

    let dismissTimer = null;
    const closeToast = () => {
      if (dismissTimer !== null) {
        window.clearTimeout(dismissTimer);
        dismissTimer = null;
      }
      toastEl.remove();
    };

    closeBtn.addEventListener("click", closeToast, { once: true });
    if (durationMs > 0) {
      dismissTimer = window.setTimeout(closeToast, durationMs);
    }
  }

  controlHandlers.set("toast", (msg) => {
    if (typeof msg.message !== "string") return;

    showToast(msg.message, {
      title: typeof msg.title === "string" ? msg.title : null,
      variant: typeof msg.variant === "string" ? msg.variant : "info",
      durationMs:
        typeof msg.durationMs === "number" && Number.isFinite(msg.durationMs)
          ? msg.durationMs
          : 5000,
    });
  });

  controlHandlers.set("openText", (msg) => {
    if (typeof msg.text !== "string") return;

    const uuid = writeHandoff(msg.text);
    const targetUrl = new URL(document.location.href);
    targetUrl.search = "";
    targetUrl.searchParams.set("handoff", uuid);
    const href = targetUrl.href;

    const newTab = window.open(href, "_blank");
    if (!newTab) {
      showToast("New script ready.", {
        durationMs: 8000,
        action: { label: "Open in new tab", href },
      });
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

  // Delay before a dropped SSE connection is retried; mirrors EventSource's
  // default reconnection delay, which this fetch-based reader replaces.
  const SSE_RECONNECT_DELAY_MS = 3000;

  /**
   * Opens a server-sent-events stream over fetch instead of EventSource so the
   * Authorization header can be attached — EventSource cannot set request headers
   * (ADR-49). Returns a handle with a close() method, mirroring the EventSource
   * surface the callers use. Reconnects after a delay on error or unexpected
   * end-of-stream, replacing EventSource's automatic reconnection: during an
   * intentional session swap the outgoing stream must not reconnect (the swap
   * closes it and opens a stream on the new session), and a 401 must not retry
   * either — the token prompt raised by padFetch takes over instead.
   * @param {string} path - Pad-relative SSE endpoint path.
   * @param {function(object): void} handler - Receives each parsed JSON message.
   * @param {{onOpen?: function(): void, onError?: function(): void}} [callbacks]
   */
  function openSse(path, handler, { onOpen, onError } = {}) {
    const url = padUrl(path);
    let closed = false;
    let controller = null;

    function scheduleReconnect() {
      if (closed || sessionSwapInProgress) return;
      setTimeout(() => {
        if (!closed) void connect();
      }, SSE_RECONNECT_DELAY_MS);
    }

    /**
     * Dispatches one raw SSE event block. The server only emits single-line
     * `data:` payloads and `:` keepalive comments (SseTransport), but multi-line
     * data is joined per the SSE spec anyway; comment lines and non-data fields
     * are ignored.
     * @param {string} rawEvent - One event block, without its trailing blank line.
     */
    function dispatch(rawEvent) {
      if (closed) return;
      const dataLines = [];
      for (const line of rawEvent.split("\n")) {
        if (line.startsWith("data:")) {
          // Strip the field name, then the single optional leading space the
          // SSE spec allows after the colon.
          dataLines.push(line.slice("data:".length).replace(/^ /, ""));
        }
      }
      if (dataLines.length === 0) return;
      try {
        handler(JSON.parse(dataLines.join("\n")));
      } catch (err) {
        console.error(`[DuetsPad] SSE parse error on ${path}`, err);
      }
    }

    async function connect() {
      controller = new AbortController();
      try {
        const res = await padFetch(url, {
          headers: { Accept: "text/event-stream" },
          signal: controller.signal,
        });
        if (res.status === 401) {
          // padFetch already raised the token prompt; retrying would only
          // hammer the server with doomed requests.
          await res.body?.cancel();
          onError?.();
          return;
        }
        if (!res.ok || !res.body) {
          // Release the error body explicitly: on repeated failures an
          // un-consumed body would retain its connection until GC.
          await res.body?.cancel();
          onError?.();
          scheduleReconnect();
          return;
        }

        onOpen?.();

        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        // Accumulates decoded text across chunks; SSE event blocks are separated
        // by a blank line. The server writes LF only (SseTransport), so no CRLF
        // normalization is needed.
        let buffer = "";
        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true });
          let separator = buffer.indexOf("\n\n");
          while (separator >= 0) {
            dispatch(buffer.slice(0, separator));
            buffer = buffer.slice(separator + 2);
            separator = buffer.indexOf("\n\n");
          }
        }

        // The server ended the stream without close() being called locally:
        // treat it like a dropped connection.
        onError?.();
        scheduleReconnect();
      } catch (_err) {
        // Aborted by close(), or a network failure. close() means an intentional
        // shutdown (session swap or page teardown) — never reconnect after it.
        if (closed) return;
        onError?.();
        scheduleReconnect();
      }
    }

    void connect();

    return {
      close() {
        closed = true;
        controller?.abort();
      },
    };
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
        activeEventStream = openSse(
          `sessions/${targetId}/events`,
          (msg) => {
            if (msg.type.startsWith("canvas.")) {
              handleCanvasEvent(msg);
            } else if (msg.type.startsWith("timeline.")) {
              handleTimelineEvent(msg);
            } else if (msg.type.startsWith("dialog.")) {
              handleDialogEvent(msg);
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
