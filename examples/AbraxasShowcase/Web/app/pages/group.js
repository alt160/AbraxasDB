function groupCounts(group, lab) {
  if (!lab || lab.groupId !== group.id)
    return `${group.primaryCountLabel}: ${number(group.defaultPrimaryCount)} · ${group.relatedCountLabel}: ${number(group.defaultRelatedCount)}`;
  const counts = groupRecordCounts(group, lab);
  return `${group.primaryCountLabel}: ${number(counts.primary)} · ${counts.relatedLabel}: ${number(counts.related)}`;
}
function groupRecordCounts(group, lab) {
  if (group.id === "object-shapes-identity") return { primary: lab.profiles, related: lab.notes, relatedLabel: "generated related records" };
  if (group.id === "shape-and-materialization") return { primary: lab.hookDocuments, related: Math.max(1, Math.round(lab.targetOrders / lab.targetCustomers)), relatedLabel: group.relatedCountLabel };
  if (group.id === "object-graphs-across-stores") return { primary: lab.graphCustomers, related: lab.graphOrders, relatedLabel: "orders across stores" };
  return { primary: lab.customers, related: lab.orders, relatedLabel: "generated related records" };
}
function laboratoryFacts(group, lab) {
  const counts = groupRecordCounts(group, lab);
  return `<div class="facts"><div class="fact"><b>${number(counts.primary)}</b><span>${escape(group.primaryCountLabel.toLocaleLowerCase())}</span></div><div class="fact"><b>${number(counts.related)}</b><span>${escape(counts.relatedLabel)}</span></div><div class="fact"><b>${bytes(lab.bytes)}</b><span>RecordBase files</span></div><div class="fact"><b>${number(Math.round(lab.generationMilliseconds || 0))} ms</b><span>setup time</span></div><div class="fact"><b>${escape(lab.complexity)}</b><span>data complexity</span></div><div class="fact"><b>${escape(lab.seed)}</b><span>random seed</span></div></div>`;
}
function setupEditor(id, copyId, label, title, source) {
  return `<section class="disclosure source-disclosure expanded"><button class="disclosure-toggle source-toggle" type="button" aria-expanded="true"><span class="disclosure-chevron">›</span><span><small>${escape(label)}</small><strong>${escape(title)}</strong></span></button><div class="disclosure-body"><div class="source-copy-bar"><span>Exact paste-ready C# · no Showcase runner code</span><button id="${copyId}" class="secondary copy-source" type="button">Copy code</button></div><div id="${id}" class="scenario-code-editor" aria-label="Read-only ${escape(title)} source"><pre class="code editor-fallback"><code>${escape(source)}</code></pre></div></div></section>`;
}
function groupSetupConfiguration(group, lab) {
  const primary = lab?.targetCustomers || group.defaultPrimaryCount,
    related = lab?.targetCustomers
      ? Math.max(1, Math.round(lab.targetOrders / lab.targetCustomers))
      : group.defaultRelatedCount;
  return {
    size: lab?.size || "Small",
    primary,
    related,
    complexity: lab?.complexity || "Simple",
    seed: lab?.seed ?? 1729,
  };
}
function groupSetupFormMarkup(group, configuration) {
  return `<div class="preset-row">${[
    ["Small", group.defaultPrimaryCount, group.defaultRelatedCount],
    ["Large", Math.max(group.defaultPrimaryCount * 100, 10000), Math.max(group.defaultRelatedCount, 3)],
    ["Very large", Math.max(group.defaultPrimaryCount * 1000, 100000), Math.max(group.defaultRelatedCount, 6)],
    ["Custom", configuration.primary, configuration.related],
  ].map(([name, count, density]) => `<button class="preset ${name === configuration.size ? "active" : ""}" type="button" data-preset="${name}" data-primary="${count}" data-related="${density}">${name}</button>`).join("")}</div><div class="form-grid"><div class="field"><label for="dialog-primary-count">${escape(group.primaryCountLabel)}</label><input id="dialog-primary-count" type="number" min="1" max="1000000" value="${configuration.primary}"></div><div class="field"><label for="dialog-related-count">${escape(group.relatedCountLabel)}</label><input id="dialog-related-count" type="number" min="1" max="50" value="${configuration.related}"></div><div class="field"><label for="dialog-complexity">Data complexity</label><select id="dialog-complexity"><option ${configuration.complexity === "Simple" ? "selected" : ""}>Simple</option><option ${configuration.complexity === "Relational" ? "selected" : ""}>Relational</option><option ${configuration.complexity === "Dense" ? "selected" : ""}>Dense</option></select></div><div class="field"><label for="dialog-seed">Random seed</label><input id="dialog-seed" type="number" value="${configuration.seed}"></div></div>`;
}
function renderFundamentalsGroup(group) {
  const active = state.laboratory?.groupId === group.id,
    lab = active ? state.laboratory : null;
  content.innerHTML = `<div class="group-workspace fundamentals-workspace"><section class="scenario-banner group-banner"><div><p class="eyebrow">START HERE · ABRAXAS BASICS</p><h2>${escape(group.title)}</h2><p>${escape(group.description)}</p></div><div class="scenario-badges"><span class="tag">No shared setup step</span><span class="tag mutate">Replaces files per run</span></div></section>${groupTypesPanel(group)}<section class="fundamentals-intro"><div><p class="eyebrow">ABRAXAS TERMS</p><h3>What the main parts are and what they do</h3><p>Some names may look familiar if you have used SQL, document, or key-value stores. These short definitions describe what the terms mean in Abraxas.</p></div><div class="concept-grid">${group.concepts.map((concept, index) => `<article><strong>${escape(concept.term)}</strong><p>${escape(concept.meaning)}</p>${concept.details?.length ? `<button class="secondary concept-learn-more" type="button" data-concept-index="${index}">Learn More</button>` : ""}</article>`).join("")}</div></section><section class="group-ready-card fundamentals-lifecycle"><div><small>${active ? "CURRENT FUNDAMENTALS RESULT" : "HOW THESE EXAMPLES RUN"}</small><strong>${active ? `${escape(lab.lastScenarioTitle)} created the current files.` : "Each scenario starts with no files from the prior fundamentals run."}</strong><span>${active ? `${escape(lab.directory)} · running another fundamentals scenario replaces these files.` : "Every scenario uses the same Showcase folder. Before it runs, the Showcase deletes only the files left by the prior fundamentals scenario."}</span></div><span class="status-badge ${active ? "ready" : ""}">${active ? "Current" : "Self-contained"}</span></section><section class="group-scenarios card"><p class="eyebrow">RUN IN ANY ORDER</p><h3>RecordBase setup scenarios</h3><p>Open any scenario to see paste-ready Abraxas code. Its files remain available until you run another fundamentals scenario or prepare a different group.</p><div class="group-scenario-list">${scenariosForGroup(
    group,
  )
    .map(
      (scenario) =>
        `<button type="button" data-group-scenario="${escape(scenario.id)}"><span><strong>${escape(scenario.title)}</strong><small>${escape(scenario.description)}</small></span><i>Open scenario ›</i></button>`,
    )
    .join("")}</div></section></div>`;
  wireDisclosures(content);
  wireTypeReferenceActions(content);
  content
    .querySelectorAll("[data-concept-index]")
    .forEach(
      (button) =>
        (button.onclick = () =>
          openConceptDetails(
            group.concepts[Number(button.dataset.conceptIndex)],
          )),
    );
  content
    .querySelectorAll("[data-group-scenario]")
    .forEach(
      (button) =>
        (button.onclick = () => selectScenario(button.dataset.groupScenario)),
    );
}
function renderGroup() {
  const group = groupById(selectedGroup) || state.groups[0];
  if (!group) {
    openCatalog();
    return;
  }
  selectedGroup = group.id;
  pageTitle.textContent = group.title;
  renderCategoryNavigation();
  if (group.commercialOnly) {
    renderCommercialGroup(group);
    return;
  }
  if (group.replacesEnvironmentPerScenario) {
    renderFundamentalsGroup(group);
    return;
  }
  const ready = group.ready && state.laboratory?.groupId === group.id,
    lab = ready ? state.laboratory : null,
    configuration = groupSetupConfiguration(group, lab),
    presence = state.startup.presence,
    savedClosed =
      !state.laboratory &&
      presence?.hasRecordBase &&
      presence.groupId === group.id &&
      presence.setupVersion === group.setupVersion,
    savedOther = !state.laboratory && presence?.hasRecordBase && !savedClosed,
    connectedOther = !ready && activeGroup();
  const statusPanel = ready
    ? `<section class="group-ready-card"><div><small>OPEN GROUP RECORDBASE</small><strong>${escape(groupCounts(group, lab))}</strong><span>${escape(lab.directory)}</span></div><div class="group-state-actions"><span class="status-badge ready">Ready</span><button id="reset-group" class="secondary" type="button">Reset / recreate…</button></div></section>${laboratoryFacts(group, lab)}`
    : `<section class="group-empty-card ${connectedOther ? "group-handoff-card" : ""}"><div><small>${savedClosed ? "MATCHING SAVED RECORDBASE" : connectedOther ? "ANOTHER GROUP IS OPEN" : savedOther ? "OTHER SAVED FILES FOUND" : "GROUP RECORDBASE NOT READY"}</small><strong>${savedClosed ? "Open this group’s saved RecordBase." : connectedOther ? `<button id="open-active-group" class="group-name-pill" type="button">${escape(connectedOther.title)}</button><span> is currently open.</span>` : savedOther ? "The saved files do not belong to this group." : "Prepare this group before running its scenarios or browsing its records."}</strong><span>${savedClosed ? "Its group identity and setup version match this page." : connectedOther ? `Unload and delete ${escape(connectedOther.title)} before preparing this group.` : savedOther ? "Run setup to replace the existing Showcase files with this group." : "Setup creates this group’s RecordBase files and sample records."}</span></div>${savedClosed ? '<div class="group-state-actions"><button id="replace-saved" class="secondary" type="button">Replace instead…</button><button id="connect-saved" class="primary" type="button">Open existing RecordBase</button></div>' : connectedOther ? `<div class="group-state-actions"><button id="unload-active-group" class="secondary danger-pill" type="button">Unload and delete…</button></div>` : ""}</section>`;
  const setupExpanded = groupSetupEditing || (!ready && !savedClosed),
    setupVisible = setupExpanded;
  content.innerHTML = `<div class="group-workspace"><section class="scenario-banner group-banner"><div><p class="eyebrow">SCENARIO GROUP</p><h2>${escape(group.title)}</h2><p>${escape(group.description)}</p></div><div class="scenario-badges"><span class="tag">Setup version ${group.setupVersion}</span><span class="tag ${ready ? "ready-tag" : "mutate"}">${ready ? "Open and ready" : "Setup required"}</span></div></section>${groupTypesPanel(group)}${statusPanel}<section id="group-setup-section" class="disclosure work-section ${setupExpanded ? "expanded" : ""}" ${setupVisible ? "" : "hidden"}><div class="work-section-head"><button class="disclosure-toggle" type="button" aria-expanded="${setupExpanded}"><span class="disclosure-chevron">›</span><span><small>RECORDBASE SETUP + SAMPLE DATA</small><strong>${ready ? "Recreate group RecordBase" : "Prepare group RecordBase"}</strong></span></button><div class="work-section-actions"><button id="copy-group-source" class="secondary" type="button">Copy complete setup</button><button id="run-group-setup" class="primary" type="button" ${connectedOther ? `disabled title="Unload and delete ${escape(connectedOther.title)} first"` : ""}>${ready ? "Recreate…" : "Run Setup…"}</button></div></div><div class="disclosure-body" ${setupExpanded ? "" : "hidden"}><section class="group-parameters"><div class="setup-configuration-summary"><span>CURRENT CONFIGURATION</span><strong>${escape(configuration.size)} · ${number(configuration.primary)} ${escape(group.primaryCountLabel.toLocaleLowerCase())} · ${number(configuration.related)} ${escape(group.relatedCountLabel.toLocaleLowerCase())} · seed ${escape(configuration.seed)}</strong><small>Choose Run Setup to configure these values before anything changes.</small></div><div class="setup-code-stack">${setupEditor("environment-code-editor", "copy-environment-code", "STEP 1 · RECORDBASE", "Create stores and indexes", group.setupSource)}${setupEditor("generation-code-editor", "copy-generation-code", "STEP 2 · SAMPLE DATA", "Generate repeatable records", group.generationSource)}</div></section></div></section>${renderGroupBrowser(group, ready)}<section class="group-scenarios card"><p class="eyebrow">SCENARIOS USING THIS RECORDBASE</p><div class="group-scenario-list">${scenariosForGroup(
    group,
  )
    .map(
      (scenario) =>
        `<button type="button" data-group-scenario="${escape(scenario.id)}"><span><strong>${escape(scenario.title)}</strong><small>${escape(scenario.description)}</small></span><i>Open scenario ›</i></button>`,
    )
    .join("")}</div></section></div>`;
  wireDisclosures(content);
  wireTypeReferenceActions(content);
  mountCodeEditor(
    document.querySelector("#environment-code-editor"),
    group.setupSource,
  );
  mountCodeEditor(
    document.querySelector("#generation-code-editor"),
    group.generationSource,
  );
  document.querySelector("#copy-environment-code").onclick = (event) => {
    event.stopPropagation();
    copySource(group.setupSource, "environment code", event.currentTarget);
  };
  document.querySelector("#copy-generation-code").onclick = (event) => {
    event.stopPropagation();
    copySource(group.generationSource, "generation code", event.currentTarget);
  };
  document.querySelector("#copy-group-source").onclick = (event) => {
    event.stopPropagation();
    copySource(
      group.completeSource,
      "complete group setup",
      event.currentTarget,
    );
  };
  document.querySelector("#run-group-setup").onclick = (event) => {
    event.stopPropagation();
    openGroupSetupDialog(group);
  };
  document
    .querySelector("#connect-saved")
    ?.addEventListener("click", () => openExistingWorkspace(group.id));
  document
    .querySelector("#open-active-group")
    ?.addEventListener("click", () => openGroup(connectedOther.id));
  document
    .querySelector("#unload-active-group")
    ?.addEventListener("click", () =>
      openGroupCleanupDialog(connectedOther, group),
    );
  document.querySelector("#reset-group")?.addEventListener("click", () => {
    openGroupSetupDialog(group);
  });
  document.querySelector("#replace-saved")?.addEventListener("click", () => {
    openGroupSetupDialog(group);
  });
  content
    .querySelectorAll("[data-group-scenario]")
    .forEach(
      (button) =>
        (button.onclick = () => selectScenario(button.dataset.groupScenario)),
    );
  wireGroupBrowser(group, ready);
}

function renderCommercialGroup(group) {
  renderFundamentalsGroup(group);
  const root = content.querySelector(".group-workspace");
  root.classList.add("commercial-only");
  root.querySelector(".group-banner .eyebrow").textContent = "COMMERCIAL EDITION · PROTECTED RECORDS";
  root.querySelector(".scenario-badges").insertAdjacentHTML("afterbegin", '<span class="tag commercial-tag">Commercial edition</span>');
  root.querySelector(".fundamentals-intro .eyebrow").textContent = "PAYLOAD CONCEPTS";
  root.querySelector(".fundamentals-intro h3").textContent = "How compression and encryption fit together";
  root.querySelector(".fundamentals-intro p:not(.eyebrow)").textContent = "These policies affect application-record payloads. Read each example before running it, especially the key-ownership and index-visibility notes.";
  root.querySelector(".fundamentals-lifecycle small").textContent = "SELF-CONTAINED EXAMPLES";
  root.querySelector(".fundamentals-lifecycle strong").textContent = "Each example uses a fresh Showcase-owned RecordBase.";
  root.querySelector(".fundamentals-lifecycle span:not(.status-badge)").textContent = "Running one replaces the prior self-contained example files; copied encryption examples use a temporary demo key, not a production key-management policy.";
  root.querySelector(".group-scenarios h3").textContent = "Compression and encryption scenarios";
  root.querySelector(".group-scenarios p:not(.eyebrow)").textContent = "The code and explanations are always readable. Running requires a commercial Abraxas build.";
  if (!state.commercialFeaturesAvailable) {
    root.querySelector(".group-banner").insertAdjacentHTML("afterend", '<section class="scenario-group-state pending commercial-notice"><div><small>COMMERCIAL EDITION REQUIRED</small><strong>Examples are readable in this OSS build.</strong><span>Choose a commercial example’s run button for an explanation. These payload APIs are not included in the OSS library.</span></div></section>');
  }
}
