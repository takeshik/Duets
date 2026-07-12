// DuetsPad workspace UI controller

(() => {
  const PANE_NAMES = Object.freeze(["editor", "canvas", "timeline"]);
  const MIN_PREVIOUS_SIZE = 120;
  const MIN_NEXT_SIZE = 160;
  const TAB_LABELS = Object.freeze({
    editor: { icon: "ti-code", text: "Editor" },
    canvas: { icon: "ti-layout-dashboard", text: "Canvas" },
    timeline: { icon: "ti-timeline-event", text: "Timeline" },
  });

  const state = {
    layout: "vertical",
    view: "split",
    // In tabs view, activeTab may be "canvas:default", "canvas:myName", etc.
    activeTab: "editor",
    visible: { editor: true, canvas: true, timeline: true },
  };

  let app;
  let workspace;
  let tabbar;
  const panes = {};

  function query(selector, root = document) {
    return root.querySelector(selector);
  }

  function queryRequired(selector, root = document) {
    const element = query(selector, root);
    if (!element) {
      throw new Error(`Missing required DuetsPad element: ${selector}`);
    }
    return element;
  }

  function queryAll(selector, root = document) {
    return Array.from(root.querySelectorAll(selector));
  }

  function createDiv(className) {
    const element = document.createElement("div");
    element.className = className;
    return element;
  }

  function createIcon(iconClass, extraClass = "") {
    const icon = document.createElement("i");
    icon.className = `ti ${iconClass}${extraClass ? ` ${extraClass}` : ""}`;
    return icon;
  }

  function setGrow(pane, enabled) {
    pane.style.flex = enabled ? "1 1 0" : "0 0 auto";
  }

  function isVisible(name) {
    // In tabs view all panes are always shown; user visibility state is preserved
    // so switching back to a split layout restores the previous visibility.
    if (state.view === "tabs") {
      return Boolean(panes[name]);
    }
    return state.visible[name] && Boolean(panes[name]);
  }

  /**
   * Returns the canvas name portion of a "canvas:name" tab key, or null if the
   * key is not a canvas tab key.
   * @param {string} tabKey
   * @returns {string|null}
   */
  function canvasNameFromTabKey(tabKey) {
    if (typeof tabKey === "string" && tabKey.startsWith("canvas:")) {
      return tabKey.slice("canvas:".length);
    }
    return null;
  }

  /**
   * Returns the tab key for a given canvas name ("canvas:name").
   * @param {string} name
   * @returns {string}
   */
  function canvasTabKey(name) {
    return `canvas:${name}`;
  }

  /**
   * Returns the display label for a canvas sub-tab: the canvas name as-is.
   * @param {string} name
   * @returns {string}
   */
  function canvasTabLabel(name) {
    return name;
  }

  /**
   * Returns the display label for a top-level canvas tab (tabs view). When only
   * the default canvas exists the bare word "Canvas" is shown; once there are
   * multiple canvases each is disambiguated as "Canvas(name)".
   * @param {string} name
   * @returns {string}
   */
  function canvasTopTabLabel(name) {
    const names = window.DuetsPad ? window.DuetsPad.getCanvasNames() : [];
    return names.length > 1 ? `Canvas(${name})` : "Canvas";
  }

  /**
   * Updates the canvas sub-tabbar (#canvas-tabbar) inside the canvas pane
   * header to reflect the current list of canvas names. Only shown when there
   * are two or more canvases.
   */
  function syncCanvasTabbar() {
    const tabbar = document.getElementById("canvas-tabbar");
    if (!tabbar) return;

    const names = window.DuetsPad ? window.DuetsPad.getCanvasNames() : [];
    const activeName = window.DuetsPad
      ? window.DuetsPad.getActiveCanvasName()
      : "default";

    tabbar.replaceChildren();
    tabbar.classList.toggle("multi", names.length > 1);

    for (const name of names) {
      const item = document.createElement("li");
      item.className = "canvas-tab";
      const link = document.createElement("a");
      link.className = `canvas-tab-link${name === activeName ? " active" : ""}`;
      link.href = "#";
      link.textContent = canvasTabLabel(name);
      link.addEventListener("click", (event) => {
        event.preventDefault();
        window.DuetsPad?.setActiveCanvasName(name);
        syncCanvasTabbar();
      });
      item.appendChild(link);
      tabbar.appendChild(item);
    }
  }

  function createContainer(className) {
    return createDiv(className);
  }

  function createSplitter(orientation) {
    const splitter = createDiv(`splitter ${orientation}`);
    const isVertical = orientation === "v";

    splitter.setAttribute("role", "separator");
    splitter.setAttribute(
      "aria-orientation",
      isVertical ? "vertical" : "horizontal",
    );
    splitter.tabIndex = 0;

    splitter.addEventListener("pointerdown", (event) => {
      if (event.button !== 0) {
        return;
      }

      const previous = splitter.previousElementSibling;
      const next = splitter.nextElementSibling;
      const container = splitter.parentElement;
      if (!previous || !next || !container) {
        return;
      }

      event.preventDefault();

      const startPosition = isVertical ? event.clientX : event.clientY;
      const initialPreviousSize = isVertical
        ? previous.offsetWidth
        : previous.offsetHeight;
      const totalSize = isVertical
        ? container.offsetWidth
        : container.offsetHeight;
      const maxPreviousSize = totalSize - MIN_NEXT_SIZE;

      if (maxPreviousSize < MIN_PREVIOUS_SIZE) {
        return;
      }

      const applyDrag = (pointerEvent) => {
        if (pointerEvent.pointerId !== event.pointerId) {
          return;
        }

        const currentPosition = isVertical
          ? pointerEvent.clientX
          : pointerEvent.clientY;
        const delta = currentPosition - startPosition;
        const nextSize = Math.max(
          MIN_PREVIOUS_SIZE,
          Math.min(maxPreviousSize, initialPreviousSize + delta),
        );

        previous.style.flex = `0 0 ${nextSize}px`;
        next.style.flex = "1 1 0";
      };

      const finishDrag = (pointerEvent) => {
        if (pointerEvent.pointerId !== event.pointerId) {
          return;
        }

        splitter.classList.remove("is-dragging");
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
        if (splitter.hasPointerCapture(event.pointerId)) {
          splitter.releasePointerCapture(event.pointerId);
        }
        splitter.removeEventListener("pointermove", applyDrag);
        splitter.removeEventListener("pointerup", finishDrag);
        splitter.removeEventListener("pointercancel", finishDrag);
      };

      splitter.classList.add("is-dragging");
      document.body.style.cursor = isVertical ? "col-resize" : "row-resize";
      document.body.style.userSelect = "none";
      splitter.setPointerCapture(event.pointerId);
      splitter.addEventListener("pointermove", applyDrag);
      splitter.addEventListener("pointerup", finishDrag);
      splitter.addEventListener("pointercancel", finishDrag);
    });

    return splitter;
  }

  function fillWithSplitters(container, items, orientation) {
    items.forEach((item, index) => {
      if (index > 0) {
        container.appendChild(createSplitter(orientation));
      }
      container.appendChild(item);
    });
  }

  function buildVerticalLayout() {
    const rightItems = [];
    if (isVisible("canvas")) {
      setGrow(panes.canvas, true);
      rightItems.push(panes.canvas);
    }
    if (isVisible("timeline")) {
      setGrow(panes.timeline, true);
      rightItems.push(panes.timeline);
    }

    const columns = [];
    if (isVisible("editor")) {
      setGrow(panes.editor, true);
      columns.push(panes.editor);
    }
    if (rightItems.length > 0) {
      const rightColumn = createContainer("workspace-col");
      fillWithSplitters(rightColumn, rightItems, "h");
      rightColumn.style.flex = columns.length > 0 ? "0 0 46%" : "1 1 0";
      columns.push(rightColumn);
    }

    fillWithSplitters(workspace, columns, "v");
    workspace.style.flexDirection = "row";
  }

  function buildHorizontalLayout() {
    const resultItems = [];
    if (isVisible("canvas")) {
      setGrow(panes.canvas, true);
      resultItems.push(panes.canvas);
    }
    if (isVisible("timeline")) {
      setGrow(panes.timeline, true);
      resultItems.push(panes.timeline);
    }

    const blocks = [];
    if (isVisible("editor")) {
      setGrow(panes.editor, true);
      blocks.push(panes.editor);
    }
    if (resultItems.length > 0) {
      const resultsRow = createContainer("workspace-row");
      fillWithSplitters(resultsRow, resultItems, "v");
      resultsRow.style.flex = "1 1 0";
      blocks.push(resultsRow);
    }

    fillWithSplitters(workspace, blocks, "h");
    workspace.style.flexDirection = "column";
  }

  /**
   * Creates a workspace-level tab item for the given tab key. The key is either
   * a plain pane name ("editor", "timeline") or a canvas composite key
   * ("canvas:default", "canvas:myCanvas").
   * @param {string} key - Tab key.
   * @returns {HTMLLIElement}
   */
  function createTab(key) {
    const canvasName = canvasNameFromTabKey(key);
    const label =
      canvasName !== null
        ? { icon: TAB_LABELS.canvas.icon, text: canvasTopTabLabel(canvasName) }
        : TAB_LABELS[key];

    const item = document.createElement("li");
    const link = document.createElement("a");

    item.className = "nav-item";
    link.className = `nav-link${state.activeTab === key ? " active" : ""}`;
    link.href = "#";
    link.append(
      createIcon(label.icon, "me-1"),
      document.createTextNode(label.text),
    );
    link.addEventListener("click", (event) => {
      event.preventDefault();
      state.activeTab = key;
      render();
    });

    item.appendChild(link);
    return item;
  }

  /**
   * Determines whether the given tab key is "visible" (has a backing pane and,
   * for canvas tabs, an existing canvas name).
   * @param {string} key
   * @returns {boolean}
   */
  function isTabVisible(key) {
    const canvasName = canvasNameFromTabKey(key);
    if (canvasName !== null) {
      if (!panes.canvas) return false;
      const names = window.DuetsPad ? window.DuetsPad.getCanvasNames() : [];
      return names.includes(canvasName);
    }
    return isVisible(key);
  }

  /**
   * Returns the ordered list of tab keys for tabs view, expanding "canvas" into
   * one key per canvas name.
   * @returns {string[]}
   */
  function getTabKeys() {
    const keys = [];
    for (const name of PANE_NAMES) {
      if (name === "canvas") {
        const canvasNames = window.DuetsPad
          ? window.DuetsPad.getCanvasNames()
          : [];
        if (canvasNames.length === 0) {
          // No canvas yet — include a placeholder "canvas:default" so the tab
          // exists before the first SSE event arrives.
          keys.push(canvasTabKey("default"));
        } else {
          for (const cn of canvasNames) {
            keys.push(canvasTabKey(cn));
          }
        }
      } else {
        keys.push(name);
      }
    }
    return keys;
  }

  function buildTabsLayout() {
    const tabKeys = getTabKeys();

    // Normalise activeTab: if it no longer exists (e.g., after a session reset),
    // or was a plain "canvas" key from an older state, fall back to the first
    // visible key.
    if (!tabKeys.includes(state.activeTab) || !isTabVisible(state.activeTab)) {
      state.activeTab = tabKeys.find(isTabVisible) ?? tabKeys[0] ?? "editor";
    }

    tabbar.replaceChildren();
    for (const key of tabKeys) {
      if (isTabVisible(key)) {
        tabbar.appendChild(createTab(key));
      }
    }

    // Determine which pane to show and which canvas name to activate.
    const activeCanvasName = canvasNameFromTabKey(state.activeTab);
    let activePane;
    if (activeCanvasName !== null) {
      activePane = panes.canvas;
      // Tell duetspad.js which canvas panel should be visible.
      window.DuetsPad?.setActiveCanvasName(activeCanvasName);
    } else {
      activePane = panes[state.activeTab];
    }

    if (activePane) {
      setGrow(activePane, true);
      workspace.appendChild(activePane);
    }
    workspace.style.flexDirection = "column";
  }

  function detachPanes() {
    for (const pane of Object.values(panes)) {
      pane.remove();
      pane.style.flex = "";
    }
  }

  function render() {
    detachPanes();
    workspace.replaceChildren();
    tabbar.replaceChildren();
    app.dataset.view = state.view;

    if (state.view === "tabs") {
      buildTabsLayout();
    } else if (state.layout === "horizontal") {
      buildHorizontalLayout();
      syncCanvasTabbar();
    } else {
      buildVerticalLayout();
      syncCanvasTabbar();
    }

    syncToolbar();
  }

  function syncToolbar() {
    // Hide the pane-visibility toggle group in tabs view (all panes are always shown).
    const segPanes = query("#seg-panes");
    if (segPanes) {
      segPanes.hidden = state.view === "tabs";
    }

    for (const button of queryAll("#seg-panes button")) {
      const active = Boolean(state.visible[button.dataset.pane]);
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
    }

    for (const button of queryAll("#seg-view button")) {
      const mode = button.dataset.arrange;
      const active =
        mode === "tabs"
          ? state.view === "tabs"
          : state.view !== "tabs" && state.layout === mode;

      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
    }
  }

  function setTheme(theme) {
    document.documentElement.setAttribute("data-bs-theme", theme);

    const themeButton = query("#btn-theme");
    if (!themeButton) {
      return;
    }

    themeButton.replaceChildren(
      createIcon(theme === "dark" ? "ti-moon" : "ti-sun"),
    );
  }

  function setArrange(mode) {
    if (mode === "tabs") {
      state.view = "tabs";
    } else {
      state.view = "split";
      state.layout = mode === "horizontal" ? "horizontal" : "vertical";
    }
    render();
  }

  function setVisible(name, visible) {
    if (!Object.hasOwn(state.visible, name)) {
      return;
    }

    state.visible[name] = visible;
    render();
  }

  /**
   * Wires the custom dropdown for #btn-more.
   * Bootstrap JS is not loaded, so this implements a minimal open/close
   * cycle: clicking the button toggles `.show` on the menu; clicking outside
   * or pressing Escape closes it.
   */
  function wireMoreDropdown() {
    const btnMore = query("#btn-more");
    const menuMore = query("#menu-more");
    if (!btnMore || !menuMore) return;

    function closeMenu() {
      menuMore.classList.remove("show");
      btnMore.setAttribute("aria-expanded", "false");
    }

    function openMenu() {
      menuMore.classList.add("show");
      btnMore.setAttribute("aria-expanded", "true");
    }

    btnMore.addEventListener("click", (event) => {
      event.stopPropagation();
      const isOpen = menuMore.classList.contains("show");
      if (isOpen) {
        closeMenu();
      } else {
        openMenu();
      }
    });

    // Close on any click outside the dropdown.
    document.addEventListener("click", (event) => {
      if (!menuMore.contains(event.target) && event.target !== btnMore) {
        closeMenu();
      }
    });

    // Close on Escape.
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape") {
        closeMenu();
      }
    });

    query("#menu-reset-session")?.addEventListener("click", () => {
      closeMenu();
      window.DuetsPad?.resetSession();
    });
  }

  function wireToolbar() {
    for (const button of queryAll("#seg-panes button")) {
      button.addEventListener("click", () => {
        setVisible(button.dataset.pane, !state.visible[button.dataset.pane]);
      });
    }

    for (const button of queryAll("#seg-view button")) {
      button.addEventListener("click", () => {
        setArrange(button.dataset.arrange);
      });
    }

    query("#btn-theme")?.addEventListener("click", () => {
      const currentTheme =
        document.documentElement.getAttribute("data-bs-theme");
      setTheme(currentTheme === "dark" ? "light" : "dark");
    });

    query("#btn-run")?.addEventListener("click", () => {
      window.DuetsPad?.run();
    });

    for (const button of queryAll(
      "#btn-clear, #pane-timeline .pane-tools button",
    )) {
      button.addEventListener("click", () => {
        window.DuetsPad?.clearTimeline();
      });
    }

    wireMoreDropdown();
  }

  function init() {
    app = queryRequired("#app");
    workspace = queryRequired("#workspace");
    tabbar = queryRequired("#workspace-tabbar");

    for (const name of PANE_NAMES) {
      panes[name] = queryRequired(`#pane-${name}`);
    }

    // Register with the canvas state module so we hear about new canvas names.
    // The callback receives the new name (string) on creation, or null on a full
    // canvas reset (session swap). Either way, re-render to update tabs.
    if (window.DuetsPad) {
      window.DuetsPad.onCanvasCreated((_nameOrNull) => {
        if (state.view === "tabs") {
          render();
        } else {
          syncCanvasTabbar();
        }
      });
    }

    wireToolbar();
    render();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init, { once: true });
  } else {
    init();
  }
})();
