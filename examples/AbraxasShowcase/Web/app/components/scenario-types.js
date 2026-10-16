function customTypesPanel(
  types,
  {
    id = "scenario-types-section",
    title = "Custom types used",
    highlight = false,
    empty = "This page works with RecordBase infrastructure and does not use a persisted application type.",
  } = {},
) {
  return `<section id="${escape(id)}" class="disclosure work-section scenario-types-section"><div class="work-section-head"><button class="disclosure-toggle" type="button" aria-expanded="false"><span class="disclosure-chevron">›</span><span><small>PERSISTED DOMAIN MODELS</small><strong>${escape(title)} · ${types.length}</strong></span></button></div><div class="disclosure-body" hidden>${types.length ? `<div class="scenario-type-cards">${types.map((type) => `<article class="scenario-type-card"><strong>${escape(type.name)}</strong><p>${escape(type.description)}</p><div class="scenario-type-actions">${highlight ? `<button class="secondary" type="button" data-highlight-type="${escape(type.name)}">Highlight usages</button>` : ""}<button class="secondary" type="button" data-view-type="${escape(type.name)}">View definition</button></div></article>`).join("")}</div>` : `<p class="type-setup-note">${escape(empty)}</p>`}</div></section>`;
}
function scenarioTypesPanel(types) {
  return customTypesPanel(types, { highlight: true });
}
function typesForGroup(groupId) {
  const scenarioIds = new Set(
    state.scenarios
      .filter((scenario) => scenario.groupId === groupId)
      .map((scenario) => scenario.id),
  );
  return state.types.filter(
    (type) =>
      type.groupIds.includes(groupId) ||
      type.scenarioIds.some((id) => scenarioIds.has(id)),
  );
}
function groupTypesPanel(group) {
  return customTypesPanel(typesForGroup(group.id), {
    id: "group-types-section",
    title: "Custom types used by this group",
    empty:
      "This group demonstrates RecordBase infrastructure without a persisted application type.",
  });
}
function wireScenarioTypeActions() {
  content.querySelectorAll("[data-highlight-type]").forEach(
    (button) =>
      (button.onclick = (event) => {
        event.stopPropagation();
        highlightScenarioType(
          button.dataset.highlightType,
          event.currentTarget,
        );
      }),
  );
  content.querySelectorAll(".scenario-types-section [data-view-type]").forEach(
    (button) =>
      (button.onclick = (event) => {
        event.stopPropagation();
        openTypeDefinition(
          state.types.find((type) => type.name === button.dataset.viewType),
          event.currentTarget,
        );
      }),
  );
}
function highlightScenarioType(typeName, button) {
  const codeSection = document.querySelector("#code-run-section"),
    sourceSection = document
      .querySelector("#scenario-code-editor")
      ?.closest(".source-disclosure");
  setDisclosure(codeSection, true);
  setDisclosure(sourceSection, true);
  content
    .querySelectorAll("[data-highlight-type]")
    .forEach((item) => item.classList.toggle("active", item === button));
  const show = () => {
    const result = scenarioCodeEditor?.highlightIdentifier(typeName);
    if (!result?.count) {
      footer.textContent = `No ${typeName} usages were found in the displayed scenario source`;
      return;
    }
    button.textContent = `Next usage · ${result.index + 1}/${result.count}`;
    footer.textContent = `${typeName} · usage ${result.index + 1} of ${result.count}`;
  };
  if (scenarioCodeEditor) show();
  else {
    pendingTypeHighlight = { typeName, button };
    footer.textContent = `Preparing ${typeName} usage highlights…`;
  }
}

