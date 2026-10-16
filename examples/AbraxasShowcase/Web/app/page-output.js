/* Current-page documents for print and for explanatory, offline interaction. */
function buildPageHtml(interactive = false) {
  const title = pageTitle.textContent.trim() || "Abraxas Showcase";
  const snapshot = content.cloneNode(true);
  const originalEditors = content.querySelectorAll("[data-export-source]");
  const clonedEditors = snapshot.querySelectorAll("[data-export-source]");
  clonedEditors.forEach((editor, index) => {
    const pre = document.createElement("pre");
    pre.className = "export-code";
    pre.textContent = originalEditors[index]?.dataset.exportSource || "";
    editor.replaceWith(pre);
  });
  if (!interactive)
    snapshot.querySelectorAll("[hidden], [inert]").forEach((node) => {
      node.removeAttribute("hidden");
      node.removeAttribute("inert");
    });
  else snapshot.querySelectorAll("[inert]").forEach((node) => node.removeAttribute("inert"));
  snapshot.querySelectorAll("[style]").forEach((node) => node.removeAttribute("style"));
  snapshot.querySelectorAll("button").forEach((button) => {
    if (interactive && button.matches(".disclosure-toggle, .catalog-entry-toggle, .catalog-category-toggle, .what-if-show-how, .copy-source, #copy-complete-example, #copy-group-source, [data-view-type], [data-copy-type], [data-concept-index]")) {
      button.classList.add("export-action");
      button.disabled = false;
      return;
    }
    const label = document.createElement("span");
    const heading = button.matches(".disclosure-toggle, .catalog-entry-toggle, .catalog-category-toggle");
    label.className = heading ? "export-heading-control" : "export-button";
    if (heading) label.innerHTML = button.innerHTML;
    else label.textContent = button.textContent;
    button.replaceWith(label);
  });
  snapshot.querySelectorAll("input, select, textarea").forEach((field) => {
    const label = document.createElement("span");
    label.textContent = field.value || "";
    field.replaceWith(label);
  });
  const liveImages = content.querySelectorAll("img");
  snapshot.querySelectorAll("img").forEach((image, index) => {
    try {
      const original = liveImages[index];
      const canvas = document.createElement("canvas");
      canvas.width = original.naturalWidth;
      canvas.height = original.naturalHeight;
      if (!canvas.width || !canvas.height) throw new Error("Image has not loaded.");
      canvas.getContext("2d").drawImage(original, 0, 0);
      image.src = canvas.toDataURL("image/png");
      image.removeAttribute("srcset");
    } catch { image.replaceWith(document.createTextNode(image.alt || "")); }
  });
  snapshot.querySelectorAll("script, iframe, canvas").forEach((node) => node.remove());

  const sheet = [...document.styleSheets].find((item) => item.href && new URL(item.href).pathname.endsWith("/page-output.css"));
  if (!sheet) throw new Error("The standalone page stylesheet is unavailable.");
  const styles = [...sheet.cssRules].map((rule) => rule.cssText).join("\n");
  const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim();
  const path = [...breadcrumbs.querySelectorAll("button, span")]
    .map((item) => item.textContent.trim()).filter((part) => part && part !== "/").join(" / ");
  const offline = interactive ? buildOfflinePagePayload(snapshot) : null;
  const offlineJson = offline ? JSON.stringify(offline).replace(/</g, "\\u003c") : "";
  const runtime = interactive ? `<script type="application/json" id="export-data">${offlineJson}</script><script>(${initializeExportPage.toString()})();</script>` : "";
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'none'; form-action 'none'"><title>${escape(title)} — Abraxas Showcase</title><style>${styles}</style></head><body class="export-document" style="--export-accent:${escape(accent || "#336ead")}"><div class="export-heading"><small>ABRAXAS SHOWCASE · ${escape(path)}</small><h1>${escape(title)}</h1></div><main>${snapshot.innerHTML}</main>${runtime}</body></html>`;
}

function buildOfflinePagePayload(snapshot) {
  const terms = Object.fromEntries([...new Set([...snapshot.querySelectorAll("[data-glossary-term]")]
    .map((node) => node.dataset.glossaryTerm))].map((term) => {
    const entry = glossaryEntry(term);
    return [term, { meaning: entry.meaning, detail: entry.detail,
      concept: entry.concept ? { term: entry.concept.term, detailIntroduction: entry.concept.detailIntroduction,
        sharedFileBaseName: entry.concept.sharedFileBaseName, details: entry.concept.details } : null }];
  }));
  const group = currentView === "group" ? groupById(selectedGroup) : null;
  const concepts = group?.concepts?.map((concept) => ({ term: concept.term,
    detailIntroduction: concept.detailIntroduction, sharedFileBaseName: concept.sharedFileBaseName,
    details: concept.details })) || [];
  const typeNames = new Set([...snapshot.querySelectorAll("[data-view-type], [data-copy-type]")]
    .map((node) => node.dataset.viewType || node.dataset.copyType));
  const types = Object.fromEntries(state.types.filter((type) => typeNames.has(type.name))
    .map((type) => [type.name, { description: type.description, source: type.source }]));
  const remedy = currentView === "what-if" ? whatIfRemedies[selectedWhatIfCase] : null;
  const completeSource = currentView === "scenarios"
    ? state.scenarios.find((scenario) => scenario.id === selectedScenario)?.completeSource
    : currentView === "group" ? group?.completeSource : null;
  return { terms, concepts, types, completeSource,
    remedy: remedy ? { title: pageTitle.textContent, ...remedy } : null };
}

async function saveCurrentPageHtml() {
  const button = document.querySelector("#save-page-html");
  button.disabled = true;
  try {
    const result = await call("save-page-html", { title: pageTitle.textContent, html: buildPageHtml(true) });
    if (result.saved) button.textContent = "Saved";
  } catch (error) { showError(error); }
  finally { setTimeout(() => { button.disabled = false; button.textContent = "Save HTML"; }, 1600); }
}

function printCurrentPage() {
  const frame = document.createElement("iframe");
  frame.setAttribute("aria-hidden", "true");
  frame.style.cssText = "position:fixed;left:-12000px;top:0;width:1000px;height:800px;border:0";
  frame.onload = () => {
    frame.contentWindow.addEventListener("afterprint", () => frame.remove(), { once: true });
    frame.contentWindow.focus();
    frame.contentWindow.print();
    setTimeout(() => frame.remove(), 300000);
  };
  frame.srcdoc = buildPageHtml();
  document.body.appendChild(frame);
}

document.querySelector("#save-page-html").onclick = saveCurrentPageHtml;
document.querySelector("#print-page").onclick = printCurrentPage;

/* Package the curriculum without running examples or changing the active RecordBase. */
function buildAllPagesHtml() {
  const saved = {
    state, currentView, selectedScenario, selectedGroup, selectedWhatIfCase,
    selectedTypeSearchName, catalogCategoryFilter, catalogTargetScenarioId,
    browserState, groupSetupEditing, title: pageTitle.textContent,
    scrollTop: content.scrollTop,
  };
  const livePage = document.createDocumentFragment();
  livePage.replaceChildren(...content.childNodes);
  const pages = [];
  const payloads = {};
  let styles = "";
  const routes = [
    { view: "home", title: "Welcome to Abraxas Showcase" },
    { view: "catalog", title: "Examples catalog" },
    { view: "types", title: "Scenario types" },
    ...whatIfCases.map((item) => ({ view: "what-if", caseId: item.id, title: item.title })),
    ...state.groups.flatMap((group) => [
      { view: "group", groupId: group.id, title: group.title },
      ...scenariosForGroup(group).map((scenario) => ({ view: "scenarios", scenarioId: scenario.id, title: scenario.title })),
    ]),
  ];
  try {
    window.showcaseBulkExporting = true;
    state = { ...state, laboratory: null, groupStores: [],
      startup: { ...state.startup, presence: null } };
    selectedTypeSearchName = null;
    catalogCategoryFilter = null;
    catalogTargetScenarioId = null;
    browserState = null;
    groupSetupEditing = false;
    routes.forEach((route, index) => {
      currentView = route.view;
      selectedGroup = route.groupId || null;
      selectedScenario = route.scenarioId || null;
      selectedWhatIfCase = route.caseId || null;
      if (route.view === "home") { pageTitle.textContent = route.title; renderHome(); }
      else if (route.view === "catalog") { pageTitle.textContent = route.title; renderCatalog(); }
      else if (route.view === "types") { pageTitle.textContent = route.title; renderTypes(); }
      else if (route.view === "what-if") renderWhatIfCase();
      else if (route.view === "group") renderGroup();
      else renderScenarios();
      annotateGlossary(content);
      const documentPage = new DOMParser().parseFromString(buildPageHtml(true), "text/html");
      const main = documentPage.querySelector("main");
      main.querySelectorAll("#proof-section, #group-browser-section, .group-ready-card:not(.fundamentals-lifecycle), .group-empty-card, .home-state, .scenario-group-state")
        .forEach((node) => node.remove());
      const pageId = `page-${index + 1}`;
      main.querySelectorAll("#copy-complete-example").forEach((node) => node.classList.add("export-complete-example"));
      main.querySelectorAll("#copy-group-source").forEach((node) => node.classList.add("export-group-source"));
      main.querySelectorAll("[id]").forEach((node) => { node.id = `${pageId}-${node.id}`; });
      main.querySelectorAll("[for], [aria-controls]").forEach((node) => {
        for (const name of ["for", "aria-controls"])
          if (node.hasAttribute(name)) node.setAttribute(name, `${pageId}-${node.getAttribute(name)}`);
      });
      payloads[pageId] = JSON.parse(documentPage.querySelector("#export-data").textContent);
      pages.push({ id: pageId, title: route.title,
        html: main.innerHTML, kind: route.view, group: route.groupId || null });
      if (!styles) styles = documentPage.querySelector("style").textContent;
    });
  } finally {
    window.showcaseBulkExporting = false;
    content.replaceChildren(...livePage.childNodes);
    state = saved.state;
    currentView = saved.currentView;
    selectedScenario = saved.selectedScenario;
    selectedGroup = saved.selectedGroup;
    selectedWhatIfCase = saved.selectedWhatIfCase;
    selectedTypeSearchName = saved.selectedTypeSearchName;
    catalogCategoryFilter = saved.catalogCategoryFilter;
    catalogTargetScenarioId = saved.catalogTargetScenarioId;
    browserState = saved.browserState;
    groupSetupEditing = saved.groupSetupEditing;
    pageTitle.textContent = saved.title;
    content.scrollTop = saved.scrollTop;
    renderCategoryNavigation();
  }
  const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#336ead";
  const toc = pages.map((page) => `<li class="export-toc-${page.kind}"><a href="#${page.id}">${escape(page.title)}</a></li>`).join("");
  const sections = pages.map((page) => `<section class="export-page" id="${page.id}" data-export-page="${page.id}"><div class="export-heading"><small>ABRAXAS SHOWCASE · ${escape(page.kind === "scenarios" ? "Scenario" : page.kind === "what-if" ? "What happens if…" : page.kind === "group" ? "Scenario group" : "Workspace")}</small><h1>${escape(page.title)}</h1></div><main>${page.html}</main></section>`).join("");
  const offlineJson = JSON.stringify({ pages: payloads }).replace(/</g, "\\u003c");
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'none'; form-action 'none'"><title>Abraxas Showcase — Complete Guide</title><style>${styles}</style></head><body class="export-document export-collection" style="--export-accent:${escape(accent)}"><section class="export-cover"><small>ABRAXAS SHOWCASE</small><h1>Complete Guide</h1><p>Instructional pages and paste-ready examples. Live execution results are intentionally omitted.</p><h2>Contents</h2><ol class="export-toc">${toc}</ol></section>${sections}<script type="application/json" id="export-data">${offlineJson}</script><script>(${initializeExportPage.toString()})();</script></body></html>`;
}

async function saveAllPagesHtml() {
  const button = document.querySelector("#save-all-html");
  button.disabled = true;
  button.textContent = "Preparing…";
  try {
    const html = buildAllPagesHtml();
    const result = await call("save-page-html", { title: "Abraxas Showcase - Complete Guide", html });
    if (result.saved) button.textContent = "Saved";
  } catch (error) { showError(error); }
  finally { setTimeout(() => { button.disabled = false; button.textContent = "Save all HTML"; }, 1600); }
}

document.querySelector("#save-all-html").onclick = saveAllPagesHtml;
