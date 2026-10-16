function currentRoute() {
  if (currentView === "search") return { view: "search", query: globalSearchQuery };
  if (currentView === "catalog") return { view: "catalog", plannedId: catalogTargetScenarioId };
  if (currentView === "types") return { view: "types", typeName: selectedTypeSearchName };
  if (currentView === "what-if") return { view: "what-if", caseId: selectedWhatIfCase };
  if (currentView === "group") return { view: "group", groupId: selectedGroup };
  if (currentView === "scenarios")
    return { view: "scenarios", scenarioId: selectedScenario };
  return { view: currentView };
}
function routeKey(route) {
  return `${route.view}:${route.groupId || route.scenarioId || route.caseId || route.typeName || route.plannedId || route.query || ""}`;
}
function inverseMotion(motion) {
  return (
    { up: "down", down: "up", left: "right", right: "left" }[motion] || "fade"
  );
}
function applyRoute(route, motion = "fade") {
  closeTypeDefinition();
  closeFloatingExplorers();
  pageMotion = motion;
  currentView = route.view;
  selectedTypeSearchName = route.view === "types" ? route.typeName || null : null;
  catalogTargetScenarioId = route.view === "catalog" ? route.plannedId || null : null;
  if (route.view === "search") {
    globalSearchQuery = route.query || "";
    globalSearchInput.value = globalSearchQuery;
  }
  if (route.view === "what-if") {
    selectedWhatIfCase = route.caseId;
    whatIfExpanded = true;
  }
  browserState = null;
  if (route.view === "group") selectedGroup = route.groupId;
  else if (route.view === "scenarios") {
    selectedScenario = route.scenarioId;
    const scenario = state.scenarios.find(
      (item) => item.id === route.scenarioId,
    );
    selectedGroup = scenario?.groupId || selectedGroup;
    if (scenario?.groupId) expandedCategories.add(scenario.groupId);
  }
  document
    .querySelectorAll(".nav")
    .forEach((button) =>
      button.classList.toggle("active", button.dataset.view === route.view),
    );
  render();
  if (route.view === "scenarios")
    requestAnimationFrame(() =>
      categoryNavigation
        .querySelector(`[data-scenario-nav="${CSS.escape(route.scenarioId)}"]`)
        ?.scrollIntoView({
          block: "nearest",
          behavior: reducedMotion() ? "auto" : "smooth",
        }),
    );
}
function navigateTo(route, motion = "fade") {
  if (
    navigationIndex >= 0 &&
    routeKey(navigationHistory[navigationIndex].route) === routeKey(route)
  )
    return;
  navigationHistory.splice(navigationIndex + 1);
  navigationHistory.push({ route, motion });
  navigationIndex = navigationHistory.length - 1;
  applyRoute(route, motion);
}
function traverseHistory(delta) {
  const nextIndex = navigationIndex + delta;
  if (nextIndex < 0 || nextIndex >= navigationHistory.length) return;
  const motion =
    delta < 0
      ? inverseMotion(navigationHistory[navigationIndex].motion)
      : navigationHistory[nextIndex].motion;
  navigationIndex = nextIndex;
  applyRoute(navigationHistory[navigationIndex].route, motion);
}
function updateNavigationChrome() {
  navigationBack.disabled = navigationIndex <= 0;
  navigationForward.disabled =
    navigationIndex < 0 || navigationIndex >= navigationHistory.length - 1;
  const crumbs = [{ label: "Home", route: { view: "home" } }];
  if (currentView === "search")
    crumbs.push({ label: "Search results", route: { view: "search", query: globalSearchQuery } });
  else if (currentView === "types") {
    crumbs.push({ label: "Scenario types", route: { view: "types" } });
    if (selectedTypeSearchName)
      crumbs.push({ label: selectedTypeSearchName, route: { view: "types", typeName: selectedTypeSearchName } });
  }
  else if (currentView === "what-if") {
    const item = whatIfCases.find((entry) => entry.id === selectedWhatIfCase);
    if (item) crumbs.push({ label: "What happens if…", route: null }, { label: item.title, route: { view: "what-if", caseId: item.id } });
  }
  else if (currentView !== "home")
    crumbs.push({ label: "Examples", route: { view: "catalog" } });
  if (currentView === "catalog" && catalogTargetScenarioId) {
    const planned = state.scenarios.find((item) => item.id === catalogTargetScenarioId);
    if (planned) crumbs.push({ label: planned.title, route: { view: "catalog", plannedId: planned.id } });
  }
  const group =
    currentView === "group"
      ? groupById(selectedGroup)
      : currentView === "scenarios"
        ? groupById(
            state.scenarios.find((item) => item.id === selectedScenario)
              ?.groupId,
          )
        : null;
  if (group)
    crumbs.push({
      label: group.title,
      route: { view: "group", groupId: group.id },
    });
  if (currentView === "scenarios") {
    const scenario = state.scenarios.find(
      (item) => item.id === selectedScenario,
    );
    if (scenario)
      crumbs.push({
        label: scenario.title,
        route: { view: "scenarios", scenarioId: scenario.id },
      });
  }
  breadcrumbs.innerHTML = crumbs
    .map((crumb, index) => {
      const current = index === crumbs.length - 1;
      return `${index ? '<span class="breadcrumb-separator" aria-hidden="true">/</span>' : ""}${crumb.route ? `<button type="button" data-breadcrumb-index="${index}" ${current ? 'aria-current="page" disabled' : ""}>${escape(crumb.label)}</button>` : `<span>${escape(crumb.label)}</span>`}`;
    })
    .join("");
  breadcrumbs
    .querySelectorAll("[data-breadcrumb-index]:not(:disabled)")
    .forEach(
      (button) =>
        (button.onclick = () => {
          const crumb = crumbs[Number(button.dataset.breadcrumbIndex)];
          navigateTo(crumb.route, "right");
        }),
    );
}
function render(transition = true) {
  dismissGlossary();
  disposeCodeEditors();
  applyTheme(state.theme);
  updateContext();
  renderCategoryNavigation();
  renderWhatIfNavigation();
  if (currentView === "home") {
    pageTitle.textContent = "Welcome to Abraxas Showcase";
    renderHome();
  } else if (currentView === "types") {
    pageTitle.textContent = "Scenario types";
    renderTypes();
  } else if (currentView === "what-if") {
    renderWhatIfCase();
  } else if (currentView === "search") {
    renderGlobalSearchResults();
  } else if (currentView === "group") renderGroup();
  else if (currentView === "scenarios") renderScenarios();
  else {
    pageTitle.textContent = "Examples catalog";
    renderCatalog();
  }
  annotateGlossary(content);
  updateNavigationChrome();
  if (!transition) return;
  content.scrollTo({ top: 0, left: 0, behavior: "auto" });
  animatePageEntry();
}
function animatePageEntry() {
  const page = content.firstElementChild,
    motion = pageMotion;
  pageMotion = "fade";
  if (!page || reducedMotion()) return;
  const offsets = {
    up: "translateY(22px)",
    down: "translateY(-22px)",
    left: "translateX(28px)",
    right: "translateX(-28px)",
    fade: "none",
  };
  page.animate(
    [
      { opacity: 0, transform: offsets[motion] || "none" },
      { opacity: 1, transform: "none" },
    ],
    { duration: 240, easing: "cubic-bezier(.2,.8,.2,1)" },
  );
}

function scenariosForGroup(group) {
  return state.scenarios.filter(
    (scenario) => scenario.runnable && scenario.groupId === group.id,
  );
}
function renderCategoryNavigation() {
  if (!state?.groups) return;
  categoryNavigation.innerHTML = state.groups
    .map((group) => {
      const scenarios = scenariosForGroup(group),
        expanded = expandedCategories.has(group.id),
        active =
          (currentView === "group" && selectedGroup === group.id) ||
          (currentView === "scenarios" &&
            state.scenarios.find((item) => item.id === selectedScenario)
              ?.groupId === group.id);
      return `<section class="category-group ${expanded ? "expanded" : ""} ${active ? "active-group" : ""}"><div class="category-header"><button class="category-arrow" type="button" data-group-toggle="${escape(group.id)}" aria-label="${expanded ? "Collapse" : "Expand"} ${escape(group.title)}" aria-expanded="${expanded}"><span class="category-chevron" aria-hidden="true"></span></button><button class="category-link" type="button" data-group-nav="${escape(group.id)}" ${active ? 'aria-current="true"' : ""}><span>${escape(group.title)}</span><small>${scenarios.length}</small></button></div><div class="category-scenarios ${expanded ? "" : "collapsed"}" aria-hidden="${!expanded}" ${expanded ? "" : "inert"}><div class="category-scenarios-inner">${scenarios.map((scenario) => `<button class="scenario-nav ${scenario.id === selectedScenario && currentView === "scenarios" ? "active" : ""}" type="button" data-scenario-nav="${escape(scenario.id)}" ${scenario.id === selectedScenario && currentView === "scenarios" ? 'aria-current="page"' : ""}><span></span><strong>${escape(scenario.title)}</strong></button>`).join("")}</div></div></section>`;
    })
    .join("");
  state.groups.filter((group) => group.commercialOnly).forEach((group) => {
    const groupNode = categoryNavigation.querySelector(`[data-group-nav="${CSS.escape(group.id)}"]`)?.closest(".category-group");
    if (!groupNode) return;
    groupNode.classList.add("commercial-only");
    groupNode.querySelector(".category-link small").textContent = `◆ ${scenariosForGroup(group).length}`;
    groupNode.querySelectorAll(".scenario-nav").forEach((button) => button.classList.add("commercial-only"));
  });
  categoryNavigation
    .querySelectorAll("[data-group-toggle]")
    .forEach((button) => {
      button.onclick = (event) => {
        event.stopPropagation();
        toggleCategory(button);
      };
    });
  categoryNavigation.querySelectorAll("[data-group-nav]").forEach((button) => {
    button.onclick = () => {
      if (currentView === "group" && selectedGroup === button.dataset.groupNav)
        toggleCategory(button.closest(".category-header").querySelector("[data-group-toggle]"));
      else openGroup(button.dataset.groupNav);
    };
  });
  categoryNavigation
    .querySelectorAll("[data-scenario-nav]")
    .forEach(
      (button) =>
        (button.onclick = () => selectScenario(button.dataset.scenarioNav)),
    );
}
function toggleCategory(button) {
  const groupId = button.dataset.groupToggle,
    group = button.closest(".category-group"),
    children = group.querySelector(".category-scenarios"),
    opening = !group.classList.contains("expanded");
  if (opening) expandedCategories.add(groupId);
  else expandedCategories.delete(groupId);
  group.classList.toggle("expanded", opening);
  children.classList.toggle("collapsed", !opening);
  children.inert = !opening;
  button.setAttribute("aria-expanded", String(opening));
  button.setAttribute(
    "aria-label",
    `${opening ? "Collapse" : "Expand"} ${groupById(groupId)?.title || "group"}`,
  );
  children.setAttribute("aria-hidden", String(!opening));
}
function openGroup(groupId) {
  const oldGroup =
      selectedGroup ||
      (selectedScenario
        ? state.scenarios.find((item) => item.id === selectedScenario)?.groupId
        : null),
    oldIndex = state.groups.findIndex((item) => item.id === oldGroup),
    newIndex = state.groups.findIndex((item) => item.id === groupId),
    motion =
      currentView === "scenarios" && oldGroup === groupId
        ? "right"
        : oldIndex >= 0 && newIndex >= 0
          ? newIndex > oldIndex
            ? "up"
            : "down"
          : "left";
  groupSetupEditing = false;
  navigateTo({ view: "group", groupId }, motion);
}
function selectScenario(scenarioId) {
  const next = state.scenarios.find((item) => item.id === scenarioId),
    current = state.scenarios.find((item) => item.id === selectedScenario);
  let motion;
  if (currentView === "group" && selectedGroup === next?.groupId)
    motion = "left";
  else if (current?.groupId === next?.groupId)
    motion = next.learningOrder >= current.learningOrder ? "left" : "right";
  else {
    const oldIndex = state.groups.findIndex(
        (item) => item.id === current?.groupId || item.id === selectedGroup,
      ),
      newIndex = state.groups.findIndex((item) => item.id === next?.groupId);
    motion = newIndex > oldIndex ? "up" : "down";
  }
  navigateTo({ view: "scenarios", scenarioId }, motion);
}
function openCatalog(category = null, motion = "right") {
  catalogCategoryFilter = category;
  catalogTargetScenarioId = null;
  expandedCatalogCategories.clear();
  navigateTo({ view: "catalog" }, motion);
}

