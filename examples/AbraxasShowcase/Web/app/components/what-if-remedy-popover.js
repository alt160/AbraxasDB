const whatIfRemedyPopover = document.querySelector("#what-if-remedy-popover"),
  whatIfRemedyTitle = document.querySelector("#what-if-remedy-title"),
  whatIfRemedyExplanation = document.querySelector("#what-if-remedy-explanation"),
  whatIfRemedyTabs = document.querySelector("#what-if-remedy-tabs"),
  whatIfRemedyEditorHost = document.querySelector("#what-if-remedy-editor"),
  whatIfRemedyCopy = document.querySelector("#what-if-remedy-copy");
let activeWhatIfRemedy = null,
  activeWhatIfRemedyExample = 0,
  activeWhatIfRemedyAnchor = null,
  whatIfRemedyEditor = null,
  whatIfRemedyDrag = null;

function positionWhatIfRemedy(anchor) {
  const rect = whatIfRemedyPopover.getBoundingClientRect(),
    anchorRect = anchor.getBoundingClientRect(),
    margin = 12;
  const left = Math.max(margin, Math.min(anchorRect.left, innerWidth - rect.width - margin));
  const below = anchorRect.bottom + 8;
  const top = below + rect.height <= innerHeight - margin
    ? below
    : Math.max(margin, Math.min(anchorRect.top - rect.height - 8, innerHeight - rect.height - margin));
  whatIfRemedyPopover.style.left = `${left}px`;
  whatIfRemedyPopover.style.top = `${top}px`;
}

function renderWhatIfRemedyExample() {
  if (!activeWhatIfRemedy) return;
  if (whatIfRemedyEditor) {
    const index = codeEditors.indexOf(whatIfRemedyEditor);
    if (index >= 0) codeEditors.splice(index, 1);
    whatIfRemedyEditor.dispose();
    whatIfRemedyEditor = null;
  }
  const example = activeWhatIfRemedy.examples[activeWhatIfRemedyExample];
  whatIfRemedyTabs.hidden = activeWhatIfRemedy.examples.length < 2;
  whatIfRemedyTabs.innerHTML = activeWhatIfRemedy.examples.map((entry, index) =>
    `<button type="button" role="tab" aria-selected="${index === activeWhatIfRemedyExample}" tabindex="${index === activeWhatIfRemedyExample ? 0 : -1}" data-remedy-example="${index}">${escape(entry.label)}</button>`).join("");
  whatIfRemedyTabs.querySelectorAll("[data-remedy-example]").forEach((button) => {
    button.onclick = () => {
      activeWhatIfRemedyExample = Number(button.dataset.remedyExample);
      renderWhatIfRemedyExample();
      whatIfRemedyTabs.querySelector(`[data-remedy-example="${activeWhatIfRemedyExample}"]`)?.focus();
    };
  });
  whatIfRemedyEditorHost.innerHTML = `<pre class="code editor-fallback"><code>${escape(example.code)}</code></pre>`;
  delete whatIfRemedyEditorHost.dataset.mounted;
  whatIfRemedyEditorHost.classList.remove("monaco-ready");
  mountCodeEditor(whatIfRemedyEditorHost, example.code,
    (editor) => {
      whatIfRemedyEditor = editor;
      requestAnimationFrame(() => {
        if (activeWhatIfRemedyAnchor && !whatIfRemedyDrag)
          positionWhatIfRemedy(activeWhatIfRemedyAnchor);
      });
    }, { minHeight: 80, maxHeight: 360 });
  whatIfRemedyCopy.onclick = (event) =>
    copySource(example.code, `${example.label} solution`, event.currentTarget);
}

function openWhatIfRemedy(item, anchor) {
  const remedy = whatIfRemedies[item.id];
  if (!remedy) return;
  if (whatIfRemedyPopover.matches(":popover-open")) {
    whatIfRemedyPopover.hidePopover();
    requestAnimationFrame(() => openWhatIfRemedy(item, anchor));
    return;
  }
  activeWhatIfRemedy = remedy;
  activeWhatIfRemedyExample = 0;
  activeWhatIfRemedyAnchor = anchor;
  whatIfRemedyTitle.textContent = item.title;
  whatIfRemedyExplanation.textContent = remedy.explanation;
  whatIfRemedyPopover.showPopover();
  renderWhatIfRemedyExample();
  requestAnimationFrame(() => positionWhatIfRemedy(anchor));
}

function disposeWhatIfRemedy() {
  if (whatIfRemedyEditor) {
    const index = codeEditors.indexOf(whatIfRemedyEditor);
    if (index >= 0) codeEditors.splice(index, 1);
    whatIfRemedyEditor.dispose();
    whatIfRemedyEditor = null;
  }
  activeWhatIfRemedy = null;
  activeWhatIfRemedyAnchor = null;
  whatIfRemedyDrag = null;
}

whatIfRemedyPopover.addEventListener("toggle", (event) => {
  if (event.newState === "closed") disposeWhatIfRemedy();
});
document.querySelector("#what-if-remedy-close").onclick = () => whatIfRemedyPopover.hidePopover();
const whatIfRemedyHead = whatIfRemedyPopover.querySelector(".what-if-remedy-head");
whatIfRemedyHead.addEventListener("pointerdown", (event) => {
  if (event.button !== 0) return;
  const rect = whatIfRemedyPopover.getBoundingClientRect();
  whatIfRemedyDrag = { id: event.pointerId, x: event.clientX - rect.left, y: event.clientY - rect.top };
  whatIfRemedyHead.setPointerCapture(event.pointerId);
  whatIfRemedyHead.classList.add("dragging");
  event.preventDefault();
});
whatIfRemedyHead.addEventListener("pointermove", (event) => {
  if (!whatIfRemedyDrag || event.pointerId !== whatIfRemedyDrag.id) return;
  const rect = whatIfRemedyPopover.getBoundingClientRect();
  whatIfRemedyPopover.style.left = `${Math.max(8, Math.min(event.clientX - whatIfRemedyDrag.x, innerWidth - rect.width - 8))}px`;
  whatIfRemedyPopover.style.top = `${Math.max(8, Math.min(event.clientY - whatIfRemedyDrag.y, innerHeight - rect.height - 8))}px`;
});
function endWhatIfRemedyDrag(event) {
  if (!whatIfRemedyDrag || event.pointerId !== whatIfRemedyDrag.id) return;
  if (whatIfRemedyHead.hasPointerCapture(event.pointerId)) whatIfRemedyHead.releasePointerCapture(event.pointerId);
  whatIfRemedyHead.classList.remove("dragging");
  whatIfRemedyDrag = null;
}
whatIfRemedyHead.addEventListener("pointerup", endWhatIfRemedyDrag);
whatIfRemedyHead.addEventListener("pointercancel", endWhatIfRemedyDrag);
