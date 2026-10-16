function mountCodeEditor(host, source, onMounted, layout = {}) {
  if (!host) return;
  host.dataset.exportSource = source;
  if (window.showcaseBulkExporting) {
    if (!host.querySelector("pre")) {
      const fallback = document.createElement("pre");
      fallback.className = "code editor-fallback";
      fallback.textContent = source;
      host.append(fallback);
    }
    return;
  }
  const lineCount = source.split(/\r?\n/).length,
    minHeight = layout.minHeight ?? 210,
    maxHeight = layout.maxHeight ?? 570;
  host.style.height = `${Math.min(maxHeight, Math.max(minHeight, lineCount * 21 + 28))}px`;
  const mount = () => {
    if (
      !host.isConnected ||
      host.dataset.mounted === "true" ||
      !window.AbraxasCodeEditor
    )
      return;
    try {
      const accent =
        getComputedStyle(document.documentElement)
          .getPropertyValue("--accent")
          .trim() || "#0078d4";
      const editor = window.AbraxasCodeEditor.create(host, source, accent);
      codeEditors.push(editor);
      host.dataset.mounted = "true";
      host.classList.add("monaco-ready");
      onMounted?.(editor);
    } catch (error) {
      showError(new Error(`The code viewer could not start: ${error.message}`));
    }
  };
  if (window.AbraxasCodeEditor) mount();
  else window.addEventListener("abraxas-monaco-ready", mount, { once: true });
}
function disposeCodeEditors() {
  while (codeEditors.length) codeEditors.pop().dispose();
  scenarioCodeEditor = null;
  typeDefinitionEditor = null;
  pendingTypeHighlight = null;
}
function wireDisclosures(container) {
  container.querySelectorAll(".disclosure-toggle").forEach(
    (button) =>
      (button.onclick = (event) => {
        event.stopPropagation();
        setDisclosure(
          button.closest(".disclosure"),
          button.getAttribute("aria-expanded") !== "true",
        );
      }),
  );
  container.querySelectorAll(".work-section-head").forEach(
    (header) =>
      (header.onclick = (event) => {
        if (event.target.closest(".work-section-actions, .run-timing")) return;
        const disclosure = header.closest(".disclosure"),
          toggle = header.querySelector(".disclosure-toggle");
        setDisclosure(
          disclosure,
          toggle.getAttribute("aria-expanded") !== "true",
        );
      }),
  );
}
function setDisclosure(disclosure, expanded) {
  if (!disclosure) return;
  const toggle = disclosure.querySelector(
      ":scope > .work-section-head > .disclosure-toggle, :scope > .disclosure-toggle",
    ),
    body = disclosure.querySelector(":scope > .disclosure-body"),
    alreadyExpanded = toggle?.getAttribute("aria-expanded") === "true";
  if (alreadyExpanded === expanded) return;
  disclosure.classList.toggle("expanded", expanded);
  toggle?.setAttribute("aria-expanded", String(expanded));
  animateRegion(body, expanded);
}
function animateRegion(element, expanded) {
  if (!element) return Promise.resolve();
  element.motionAnimation?.cancel();
  if (reducedMotion()) {
    element.hidden = !expanded;
    element.style.removeProperty("overflow");
    return Promise.resolve();
  }
  if (expanded) element.hidden = false;
  const startHeight = expanded ? 0 : element.getBoundingClientRect().height,
    endHeight = expanded ? element.scrollHeight : 0;
  element.style.overflow = "hidden";
  const animation = element.animate(
    [
      { height: `${startHeight}px`, opacity: expanded ? 0 : 1 },
      { height: `${endHeight}px`, opacity: expanded ? 1 : 0 },
    ],
    { duration: 210, easing: "cubic-bezier(.2,.8,.2,1)" },
  );
  element.motionAnimation = animation;
  animation.onfinish = () => {
    if (element.motionAnimation !== animation) return;
    element.motionAnimation = null;
    element.style.removeProperty("overflow");
    if (!expanded) element.hidden = true;
  };
  return animation.finished.catch(() => undefined);
}
