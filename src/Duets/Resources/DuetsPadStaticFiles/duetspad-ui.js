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

  function createTab(name) {
    const label = TAB_LABELS[name];
    const item = document.createElement("li");
    const link = document.createElement("a");

    item.className = "nav-item";
    link.className = `nav-link${state.activeTab === name ? " active" : ""}`;
    link.href = "#";
    link.append(
      createIcon(label.icon, "me-1"),
      document.createTextNode(label.text),
    );
    link.addEventListener("click", (event) => {
      event.preventDefault();
      state.activeTab = name;
      render();
    });

    item.appendChild(link);
    return item;
  }

  function buildTabsLayout() {
    if (!isVisible(state.activeTab)) {
      state.activeTab = PANE_NAMES.find(isVisible) ?? "editor";
    }

    tabbar.replaceChildren();
    for (const name of PANE_NAMES) {
      if (isVisible(name)) {
        tabbar.appendChild(createTab(name));
      }
    }

    const activePane = panes[state.activeTab];
    if (activePane && isVisible(state.activeTab)) {
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
    } else {
      buildVerticalLayout();
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

    wireToolbar();
    render();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init, { once: true });
  } else {
    init();
  }
})();
