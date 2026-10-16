function catalogEntryMarkup(scenario) {
  const target = scenario.id === catalogTargetScenarioId;
  return `<article class="catalog-entry ${scenario.runnable ? "runnable" : "planned"} ${target ? "expanded search-target" : ""}" data-catalog-scenario="${escape(scenario.id)}"><button class="catalog-entry-toggle" type="button" aria-expanded="${target}"><span class="catalog-entry-chevron">›</span><span class="catalog-entry-heading"><small>${scenario.runnable ? "RUNNABLE EXAMPLE" : "PLANNED EXAMPLE"}</small><strong>${escape(scenario.title)}</strong><em>${escape(scenario.description)}</em></span><span class="catalog-entry-state ${scenario.runnable ? "ready" : "planned"}">${scenario.runnable ? "Runnable" : "Planned"}</span></button><div class="catalog-entry-body" ${target ? "" : "hidden"}><p>${escape(scenario.explanation)}</p><div class="catalog-metadata"><div><span>Concepts</span><div class="catalog-tags">${scenario.concepts.map((item) => `<i>${escape(item)}</i>`).join("")}</div></div><div><span>API and types</span><div class="catalog-tags api">${scenario.apiNames.map((item) => `<i>${escape(item)}</i>`).join("")}</div></div>${scenario.prerequisites.length ? `<div><span>Start with</span><strong>${scenario.prerequisites.map(escape).join(" · ")}</strong></div>` : ""}<div><span>Cost</span><strong>${escape(scenario.cost)}</strong></div></div><div class="catalog-entry-actions">${scenario.runnable ? `<button class="primary open-example" type="button" data-open-scenario="${escape(scenario.id)}">Open runnable example</button>` : '<span class="planned-note">Documentation placeholder · code and execution proof will arrive with implementation.</span>'}</div></div></article>`;
}
function renderCatalog() {
  const matching = state.scenarios.filter(
      (scenario) => !catalogCategoryFilter || scenario.category === catalogCategoryFilter,
    ),
    groups = new Map();
  matching.forEach((scenario) => {
    if (!groups.has(scenario.category)) groups.set(scenario.category, []);
    groups.get(scenario.category).push(scenario);
  });
  const runnableCount = matching.filter((item) => item.runnable).length;
  content.innerHTML = `<div class="catalog-workspace"><section class="catalog-intro"><div><p class="eyebrow">PRIMER + DOCUMENTATION BY EXAMPLE</p><h2>Browse the Abraxas examples</h2><p>Each group explains what its RecordBase contains and how to prepare it. Use the search box in the top toolbar to find terms, API names, and example code from any page.</p></div><div class="catalog-count"><strong>${matching.length}</strong><span>scenarios</span><small>${runnableCount} runnable now</small></div></section><section class="start-here"><div><p class="eyebrow">START HERE</p><h3>Learn the parts, prepare data, then run examples</h3></div><ol><li class="${activeGroup() ? "complete" : ""}"><span>1</span><div><strong>Learn the terms</strong><small>Start with Recordbase Fundamentals.</small></div></li><li><span>2</span><div><strong>Prepare a group</strong><small>Choose its record counts and random seed.</small></div></li><li><span>3</span><div><strong>Run and inspect</strong><small>Changes remain visible to later scenarios in the group.</small></div></li></ol></section>${catalogCategoryFilter ? `<div class="catalog-filter"><span>Category: ${escape(catalogCategoryFilter)}</span><button id="show-all-categories" type="button">Show all categories</button></div>` : ""}<div class="catalog-results">${
    groups.size
      ? [...groups]
          .map(([category, scenarios]) => {
            const group = groupForCategory(category),
              expanded = expandedCatalogCategories.has(category) || scenarios.some((item) => item.id === catalogTargetScenarioId);
            return `<section class="catalog-category ${expanded ? "expanded" : ""}"><button class="catalog-category-toggle" type="button" data-catalog-category="${escape(category)}" aria-expanded="${expanded}"><span class="catalog-category-chevron">›</span><span class="catalog-category-heading"><strong>${escape(category)}</strong><small>${scenarios.length} ${scenarios.length === 1 ? "scenario" : "scenarios"}</small></span><span class="catalog-category-state">${scenarios.filter((item) => item.runnable).length} runnable</span></button><div class="catalog-category-body" ${expanded ? "" : "hidden"}><div class="catalog-group-action"><p>${escape(group?.description || "")}</p>${group ? `<button class="secondary" type="button" data-open-group="${escape(group.id)}">${group.replacesEnvironmentPerScenario ? "Open group introduction" : "Prepare or browse this group"}</button>` : ""}</div><div class="catalog-entries">${scenarios.map(catalogEntryMarkup).join("")}</div></div></section>`;
          })
          .join("")
      : `<section class="catalog-empty"><h3>No scenarios in this category</h3><p>Choose another category or browse all groups.</p></section>`
  }</div></div>`;
  state.groups.filter((group) => group.commercialOnly).forEach((group) => {
    const category = [...content.querySelectorAll(".catalog-category")]
      .find((item) => item.querySelector("[data-catalog-category]")?.dataset.catalogCategory === group.title);
    if (!category) return;
    category.classList.add("commercial-only");
    category.querySelector(".catalog-category-state").textContent = state.commercialFeaturesAvailable ? "Commercial runnable" : "Commercial code · FOSS read-only";
  });
  annotateGlossary(content);
  wireCatalog();
  if (catalogTargetScenarioId)
    requestAnimationFrame(() => content.querySelector(`[data-catalog-scenario="${CSS.escape(catalogTargetScenarioId)}"]`)?.scrollIntoView({ block: "start", behavior: reducedMotion() ? "auto" : "smooth" }));
}
function wireCatalog() {
  document
    .querySelector("#show-all-categories")
    ?.addEventListener("click", () => {
      catalogCategoryFilter = null;
      renderCatalog();
    });
  content.querySelectorAll(".catalog-category-toggle").forEach(
    (button) =>
      (button.onclick = () => {
        const category = button.dataset.catalogCategory,
          group = button.closest(".catalog-category"),
          body = group.querySelector(".catalog-category-body"),
          expanded = button.getAttribute("aria-expanded") !== "true";
        if (expanded) expandedCatalogCategories.add(category);
        else expandedCatalogCategories.delete(category);
        button.setAttribute("aria-expanded", String(expanded));
        group.classList.toggle("expanded", expanded);
        animateRegion(body, expanded);
      }),
  );
  content.querySelectorAll(".catalog-entry-toggle").forEach(
    (button) =>
      (button.onclick = () => {
        const entry = button.closest(".catalog-entry"),
          body = entry.querySelector(".catalog-entry-body"),
          expanded = button.getAttribute("aria-expanded") !== "true";
        button.setAttribute("aria-expanded", String(expanded));
        entry.classList.toggle("expanded", expanded);
        animateRegion(body, expanded);
      }),
  );
  content.querySelectorAll("[data-open-scenario]").forEach(
    (button) =>
      (button.onclick = (event) => {
        event.stopPropagation();
        selectScenario(button.dataset.openScenario);
      }),
  );
  content
    .querySelectorAll("[data-open-group]")
    .forEach(
      (button) => (button.onclick = () => openGroup(button.dataset.openGroup)),
    );
}

