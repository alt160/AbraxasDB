const globalSearchInput = document.querySelector("#global-search-input");
const globalSearchDropdown = document.querySelector("#global-search-results");
const globalSearchKinds = ["Group", "Scenario", "Planned scenario", "Custom type", "What happens if"];
let globalSearchItems = [];
let globalSearchMatches = [];
let globalSearchVisible = [];
let globalSearchSelection = 0;

function buildGlobalSearchIndex() {
  const items = [];
  for (const group of state.groups) {
    items.push({
      key: `group:${group.id}`, kind: "Group", title: group.title,
      subtitle: group.description, route: { view: "group", groupId: group.id },
      fields: [
        ["Description", group.description],
        ["Concept", group.concepts?.map((concept) => `${concept.term} ${concept.meaning} ${concept.detailIntroduction || ""} ${(concept.details || []).map((detail) => `${detail.title} ${detail.description}`).join(" ")}`).join(" ")],
        ["Setup code", group.setupSource],
        ["Generation code", group.generationSource],
      ],
    });
  }
  for (const scenario of state.scenarios) {
    items.push({
      key: `scenario:${scenario.id}`,
      kind: scenario.runnable ? "Scenario" : "Planned scenario",
      title: scenario.title,
      subtitle: `${scenario.category} · ${scenario.description}`,
      route: scenario.runnable
        ? { view: "scenarios", scenarioId: scenario.id }
        : { view: "catalog", plannedId: scenario.id },
      fields: [
        ["Description", scenario.description],
        ["Explanation", scenario.explanation],
        ["Concepts", scenario.concepts?.join(" ")],
        ["API", scenario.apiNames?.join(" ")],
        ["Related terms", scenario.searchTerms?.join(" ")],
        ["Prerequisites", scenario.prerequisites?.join(" ")],
        ["Scenario code", scenario.source],
      ],
    });
  }
  for (const type of state.types) {
    items.push({
      key: `type:${type.name}`, kind: "Custom type", title: type.name,
      subtitle: type.description, route: { view: "types", typeName: type.name },
      fields: [
        ["Description", type.description],
        ["Definition", type.source],
        ["Used by", type.scenarioIds.map((id) => state.scenarios.find((scenario) => scenario.id === id)?.title || "").join(" ")],
      ],
    });
  }
  for (const item of whatIfCases) {
    items.push({
      key: `what-if:${item.id}`, kind: "What happens if", title: item.title,
      subtitle: item.summary, route: { view: "what-if", caseId: item.id },
      fields: [
        ["Explanation", `${item.summary} ${item.why} ${item.next}`],
        ["Expected outcome", item.expected],
        ["Planner evidence", item.diagnostic],
        ["Example code", item.code],
      ],
    });
  }
  globalSearchItems = items.map((item, order) => ({
    ...item,
    order,
    searchable: [item.title, item.subtitle, ...item.fields.map(([, value]) => value || "")].join(" ").toLocaleLowerCase(),
  }));
}

function findGlobalMatches(query) {
  const trimmed = query.trim().toLocaleLowerCase();
  if (!trimmed) return [];
  const words = trimmed.split(/\s+/);
  return globalSearchItems
    .filter((item) => words.every((word) => item.searchable.includes(word)))
    .map((item) => {
      const title = item.title.toLocaleLowerCase();
      const subtitle = item.subtitle.toLocaleLowerCase();
      const titleScore = title === trimmed ? 120 : title.startsWith(trimmed) ? 95 : words.every((word) => title.includes(word)) ? 75 : 0;
      const score = titleScore || (words.every((word) => subtitle.includes(word)) ? 45 : 15);
      return { ...item, score, match: describeSearchMatch(item, words) };
    })
    .sort((left, right) => right.score - left.score || left.order - right.order);
}

function describeSearchMatch(item, words) {
  const matched = item.fields.find(([, value]) => value && words.some((word) => value.toLocaleLowerCase().includes(word)));
  if (!matched) return "Title match";
  const [label, text] = matched;
  const compact = String(text).replace(/\s+/g, " ").trim();
  const lower = compact.toLocaleLowerCase();
  const position = Math.max(0, lower.indexOf(words.find((word) => lower.includes(word)) || words[0]));
  const start = Math.max(0, position - 30);
  const excerpt = compact.slice(start, start + 105);
  return `${label}: ${start ? "…" : ""}${excerpt}${start + 105 < compact.length ? "…" : ""}`;
}

function globalSearchResultMarkup(item, index, dropdown = true) {
  const stateLabel = item.kind === "Planned scenario" ? "Planned" : item.kind;
  return `<button class="global-search-result ${dropdown && index === globalSearchSelection ? "selected" : ""}" type="button" data-search-key="${escape(item.key)}" ${dropdown ? `id="global-search-option-${index}" role="option" aria-selected="${index === globalSearchSelection}"` : ""}><span class="global-search-result-title"><span>${escape(item.title)}</span><small>${escape(stateLabel)}</small></span><span class="global-search-result-subtitle">${escape(item.subtitle)}</span><span class="global-search-result-match">${escape(item.match)}</span></button>`;
}

function groupedSearchMarkup(items, dropdown = true) {
  return globalSearchKinds.map((kind) => {
    const matching = items.filter((item) => item.kind === kind);
    if (!matching.length) return "";
    return `<div class="search-group-label" role="presentation">${escape(kind.toUpperCase())}</div>${matching.map((item) => globalSearchResultMarkup(item, items.indexOf(item), dropdown)).join("")}`;
  }).join("");
}

function renderGlobalSearchDropdown() {
  globalSearchMatches = findGlobalMatches(globalSearchQuery);
  if (!globalSearchQuery.trim()) {
    closeGlobalSearchDropdown();
    return;
  }
  globalSearchVisible = globalSearchMatches.slice(0, 9).sort((left, right) => globalSearchKinds.indexOf(left.kind) - globalSearchKinds.indexOf(right.kind) || right.score - left.score);
  globalSearchSelection = Math.min(globalSearchSelection, Math.max(0, globalSearchVisible.length - 1));
  globalSearchDropdown.innerHTML = `${globalSearchVisible.length ? groupedSearchMarkup(globalSearchVisible) : '<div class="global-search-empty">No matching Showcase material. Try a shorter term or an API name.</div>'}<div class="global-search-footer"><span>${globalSearchMatches.length} ${globalSearchMatches.length === 1 ? "result" : "results"}</span><button type="button" id="global-search-all">View all results</button></div>`;
  globalSearchDropdown.hidden = false;
  globalSearchInput.setAttribute("aria-expanded", "true");
  syncGlobalSearchSelection();
  globalSearchDropdown.querySelectorAll("[data-search-key]").forEach((button) => {
    button.onclick = () => openGlobalSearchResult(button.dataset.searchKey);
  });
  globalSearchDropdown.querySelector("#global-search-all").onclick = openAllGlobalSearchResults;
}

function syncGlobalSearchSelection() {
  globalSearchDropdown.querySelectorAll("[role='option']").forEach((option) => {
    const selected = option.id === `global-search-option-${globalSearchSelection}`;
    option.classList.toggle("selected", selected);
    option.setAttribute("aria-selected", String(selected));
    if (selected) option.scrollIntoView({ block: "nearest" });
  });
  if (globalSearchVisible.length)
    globalSearchInput.setAttribute("aria-activedescendant", `global-search-option-${globalSearchSelection}`);
  else globalSearchInput.removeAttribute("aria-activedescendant");
}

function closeGlobalSearchDropdown() {
  globalSearchDropdown.hidden = true;
  globalSearchInput.setAttribute("aria-expanded", "false");
  globalSearchInput.removeAttribute("aria-activedescendant");
}

function openGlobalSearchResult(key) {
  const item = globalSearchItems.find((candidate) => candidate.key === key);
  if (!item) return;
  closeGlobalSearchDropdown();
  globalSearchInput.blur();
  if (item.kind === "Planned scenario") {
    catalogCategoryFilter = null;
    const scenario = state.scenarios.find((candidate) => candidate.id === item.route.plannedId);
    if (scenario) expandedCatalogCategories.add(scenario.category);
  }
  navigateTo(item.route, "left");
}

function openAllGlobalSearchResults() {
  closeGlobalSearchDropdown();
  globalSearchInput.blur();
  const route = { view: "search", query: globalSearchQuery.trim() };
  if (currentView === "search" && routeKey(currentRoute()) === routeKey(route)) renderGlobalSearchResults();
  else navigateTo(route, "left");
}

function renderGlobalSearchResults() {
  pageTitle.textContent = "Search results";
  const matches = findGlobalMatches(globalSearchQuery);
  content.innerHTML = `<div class="search-page"><section class="card search-page-intro"><p class="eyebrow">SHOWCASE SEARCH</p><h2>${matches.length} ${matches.length === 1 ? "result" : "results"}${globalSearchQuery.trim() ? ` for “${escape(globalSearchQuery.trim())}”` : ""}</h2><p>Search includes groups, scenarios, type definitions, concept explanations, example code, and What happens if cases. Planned scenarios are marked and open in the catalog.</p></section>${matches.length ? globalSearchKinds.map((kind) => {
    const entries = matches.filter((item) => item.kind === kind);
    return entries.length ? `<section class="search-page-group"><h3>${escape(kind)} · ${entries.length}</h3>${entries.map((item) => globalSearchResultMarkup(item, -1, false)).join("")}</section>` : "";
  }).join("") : `<div class="search-page-empty">${globalSearchQuery.trim() ? "No matches. Try a shorter term, type name, or API name." : "Enter a term in the toolbar to find Showcase material."}</div>`}</div>`;
  content.querySelectorAll("[data-search-key]").forEach((button) => {
    button.onclick = () => openGlobalSearchResult(button.dataset.searchKey);
  });
}

function wireGlobalSearch() {
  buildGlobalSearchIndex();
  globalSearchInput.oninput = () => {
    globalSearchQuery = globalSearchInput.value;
    globalSearchSelection = 0;
    renderGlobalSearchDropdown();
    if (currentView === "search") {
      navigationHistory[navigationIndex].route.query = globalSearchQuery;
      renderGlobalSearchResults();
      updateNavigationChrome();
    }
  };
  globalSearchInput.onfocus = () => {
    if (globalSearchQuery.trim()) renderGlobalSearchDropdown();
  };
  globalSearchInput.onkeydown = (event) => {
    if (event.key === "Escape" && !globalSearchDropdown.hidden) {
      event.preventDefault();
      event.stopPropagation();
      closeGlobalSearchDropdown();
      return;
    }
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      if (globalSearchDropdown.hidden && globalSearchQuery.trim()) renderGlobalSearchDropdown();
      if (globalSearchDropdown.hidden || !globalSearchVisible.length) return;
      event.preventDefault();
      const length = globalSearchVisible.length;
      globalSearchSelection = (globalSearchSelection + (event.key === "ArrowDown" ? 1 : -1) + length) % length;
      syncGlobalSearchSelection();
    } else if (event.key === "Enter" && globalSearchQuery.trim()) {
      event.preventDefault();
      if (!globalSearchDropdown.hidden && globalSearchVisible.length)
        openGlobalSearchResult(globalSearchVisible[globalSearchSelection].key);
      else openAllGlobalSearchResults();
    }
  };
  document.addEventListener("pointerdown", (event) => {
    if (!event.target.closest(".global-search")) closeGlobalSearchDropdown();
  });
  document.addEventListener("keydown", (event) => {
    if ((event.ctrlKey && event.key.toLowerCase() === "k") ||
        (event.key === "/" && !event.ctrlKey && !event.altKey && !event.metaKey && !["INPUT", "TEXTAREA"].includes(document.activeElement?.tagName))) {
      event.preventDefault();
      globalSearchInput.focus();
      globalSearchInput.select();
    }
  });
}
