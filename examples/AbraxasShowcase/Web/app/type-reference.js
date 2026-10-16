function renderTypes() {
  const types = state.types;
  content.innerHTML = `<div class="type-reference-page"><section class="type-reference-intro"><div><p class="eyebrow">PASTE-READY DOMAIN MODELS</p><h2>Persisted types used by the examples</h2><p>These are the application types stored by Abraxas setup and scenario code. Showcase runner, proof, browser, and transport types stay out of this reference. Use the top search box to find a type or member from any page.</p></div><div class="type-reference-count"><strong>${types.length}</strong><span>${types.length === 1 ? "type" : "types"}</span></div></section><div class="type-reference-list">${types.map(typeReferenceCard).join("")}</div></div>`;
  wireTypeReferenceActions(content);
  if (selectedTypeSearchName)
    requestAnimationFrame(() => content.querySelector(`[data-type-card="${CSS.escape(selectedTypeSearchName)}"]`)?.scrollIntoView({ block: "start", behavior: reducedMotion() ? "auto" : "smooth" }));
}
function typeReferenceCard(type) {
  const scenarios = type.scenarioIds
      .map((id) => state.scenarios.find((item) => item.id === id))
      .filter(Boolean),
    groups = type.groupIds.map(groupById).filter(Boolean),
    scenarioPills = scenarios
      .map(
        (scenario) =>
          `<button class="type-scenario-pill" type="button" data-type-scenario="${escape(scenario.id)}">${escape(scenario.title)}</button>`,
      )
      .join(""),
    setupNote = groups.length
      ? `<span class="type-setup-note">Also used by ${groups.map((group) => escape(group.title)).join(" · ")} setup.</span>`
      : "";
  return `<article class="type-reference-card ${type.name === selectedTypeSearchName ? "search-target" : ""}" data-type-card="${escape(type.name)}"><h3>${escape(type.name)}</h3><p>${escape(type.description)}</p><div class="type-reference-uses"><small>USED BY</small>${scenarioPills ? `<div class="type-scenario-pills">${scenarioPills}</div>` : '<span class="type-setup-note">Setup code only in the current scenario set.</span>'}${setupNote}</div><div class="type-reference-actions"><button class="secondary" type="button" data-view-type="${escape(type.name)}">View definition</button><button class="secondary" type="button" data-copy-type="${escape(type.name)}">Copy definition</button></div></article>`;
}
function wireTypeReferenceActions(container) {
  container
    .querySelectorAll("[data-type-scenario]")
    .forEach(
      (button) =>
        (button.onclick = () => selectScenario(button.dataset.typeScenario)),
    );
  container.querySelectorAll("[data-view-type]").forEach(
    (button) =>
      (button.onclick = (event) =>
        openTypeDefinition(
          state.types.find((type) => type.name === button.dataset.viewType),
          event.currentTarget,
        )),
  );
  container.querySelectorAll("[data-copy-type]").forEach(
    (button) =>
      (button.onclick = (event) => {
        const type = state.types.find(
          (item) => item.name === button.dataset.copyType,
        );
        if (type)
          copySource(
            type.source,
            `${type.name} definition`,
            event.currentTarget,
          );
      }),
  );
}
function positionTypeDefinition(anchor) {
  const margin = 12,
    gap = 8,
    popoverRect = typeDefinitionPopover.getBoundingClientRect(),
    anchorRect = anchor.getBoundingClientRect();
  let left = Math.min(
      Math.max(margin, anchorRect.left),
      window.innerWidth - popoverRect.width - margin,
    ),
    top = anchorRect.bottom + gap;
  if (top + popoverRect.height > window.innerHeight - margin)
    top = Math.max(margin, anchorRect.top - popoverRect.height - gap);
  typeDefinitionPopover.style.left = `${left}px`;
  typeDefinitionPopover.style.top = `${top}px`;
}
function openTypeDefinition(type, anchor) {
  if (!type) return;
  if (typeDefinitionPopover.matches(":popover-open")) {
    if (activeTypeDefinition?.name === type.name) {
      typeDefinitionPopover.hidePopover();
      return;
    }
    typeDefinitionPopover.hidePopover();
    requestAnimationFrame(() => openTypeDefinition(type, anchor));
    return;
  }
  activeTypeDefinition = type;
  typeDefinitionTitle.textContent = type.name;
  typeDefinitionDescription.textContent = type.description;
  typeDefinitionEditorHost.innerHTML = `<pre class="code editor-fallback"><code>${escape(type.source)}</code></pre>`;
  copyTypeDefinition.onclick = (event) =>
    copySource(type.source, `${type.name} definition`, event.currentTarget);
  typeDefinitionPopover.showPopover();
  requestAnimationFrame(() => {
    positionTypeDefinition(anchor);
    mountCodeEditor(
      typeDefinitionEditorHost,
      type.source,
      (editor) => (typeDefinitionEditor = editor),
      { minHeight: 84, maxHeight: 300 },
    );
  });
}
function closeTypeDefinition() {
  if (typeDefinitionPopover?.matches(":popover-open"))
    typeDefinitionPopover.hidePopover();
}
function disposeTypeDefinitionEditor() {
  if (typeDefinitionEditor) {
    const index = codeEditors.indexOf(typeDefinitionEditor);
    if (index >= 0) codeEditors.splice(index, 1);
    typeDefinitionEditor.dispose();
    typeDefinitionEditor = null;
  }
  typeDefinitionEditorHost.innerHTML = "";
  delete typeDefinitionEditorHost.dataset.mounted;
  typeDefinitionEditorHost.classList.remove("monaco-ready");
  activeTypeDefinition = null;
  endTypeDefinitionDrag();
}
function beginTypeDefinitionDrag(event) {
  if (event.button !== 0 || event.target.closest("button")) return;
  const rect = typeDefinitionPopover.getBoundingClientRect();
  typeDefinitionDrag = {
    pointerId: event.pointerId,
    offsetX: event.clientX - rect.left,
    offsetY: event.clientY - rect.top,
  };
  event.currentTarget.setPointerCapture(event.pointerId);
  event.currentTarget.classList.add("dragging");
  event.preventDefault();
}
function moveTypeDefinitionDrag(event) {
  if (!typeDefinitionDrag || event.pointerId !== typeDefinitionDrag.pointerId)
    return;
  const margin = 8,
    rect = typeDefinitionPopover.getBoundingClientRect(),
    left = Math.min(
      Math.max(margin, event.clientX - typeDefinitionDrag.offsetX),
      window.innerWidth - rect.width - margin,
    ),
    top = Math.min(
      Math.max(margin, event.clientY - typeDefinitionDrag.offsetY),
      window.innerHeight - rect.height - margin,
    );
  typeDefinitionPopover.style.left = `${left}px`;
  typeDefinitionPopover.style.top = `${top}px`;
}
function endTypeDefinitionDrag(event) {
  if (
    !typeDefinitionDrag ||
    (event && event.pointerId !== typeDefinitionDrag.pointerId)
  )
    return;
  const head = typeDefinitionPopover.querySelector(".type-definition-head");
  if (event && head.hasPointerCapture?.(event.pointerId))
    head.releasePointerCapture(event.pointerId);
  head.classList.remove("dragging");
  typeDefinitionDrag = null;
}

