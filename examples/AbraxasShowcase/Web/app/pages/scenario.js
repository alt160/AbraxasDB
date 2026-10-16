function renderScenarios() {
  const selected =
    state.scenarios.find(
      (item) => item.id === selectedScenario && item.runnable,
    ) || state.scenarios.find((item) => item.runnable);
  if (!selected) {
    openCatalog();
    return;
  }
  selectedScenario = selected.id;
  const group = groupById(selected.groupId),
    exceptional = group.replacesEnvironmentPerScenario,
    available = !selected.commercialOnly || state.commercialFeaturesAvailable,
    ready = available && (
      exceptional ||
      (selected.groupReady && state.laboratory?.groupId === selected.groupId)),
    usedTypes = state.types.filter((type) =>
      type.scenarioIds.includes(selected.id),
    );
  pageTitle.textContent = selected.title;
  renderCategoryNavigation();
  const groupState = !available
    ? `<section class="scenario-group-state pending commercial-notice"><div><small>COMMERCIAL EDITION REQUIRED</small><strong>Readable here; execution requires commercial Abraxas assemblies.</strong><span>This FOSS build does not contain the payload APIs. The source and explanation remain available to inspect or copy.</span></div><button id="open-scenario-group" class="secondary" type="button">View group</button></section>`
    : exceptional
    ? `<section class="scenario-group-state ready"><div><small>SELF-CONTAINED FUNDAMENTALS RUN</small><strong>No separate group setup is required</strong><span>Run scenario deletes the files from the prior fundamentals run, then creates the files needed by this example.</span></div><button id="open-scenario-group" class="secondary" type="button">View fundamentals</button></section>`
    : `<section class="scenario-group-state ${ready ? "ready" : "pending"}"><div><small>GROUP RECORDBASE</small><strong>${ready ? "Ready for this scenario" : `${group.title} setup is required`}</strong><span>${ready ? "This scenario uses the records currently in the group and does not change its stores or indexes." : "Prepare the group RecordBase and sample data before running this example."}</span></div><button id="open-scenario-group" class="${ready ? "secondary" : "primary"}" type="button">${ready ? "View group data" : "Set up group now"}</button></section>`;
  content.innerHTML = `<div class="scenario-workspace"><section class="scenario-banner"><div><p class="eyebrow">${escape(selected.category)} · RUNNABLE SCENARIO</p><h2>${escape(selected.title)}</h2><p>${escape(selected.description)}</p></div><div class="scenario-badges"><span class="tag">${escape(selected.cost)}</span>${selected.mutates ? `<span class="tag mutate">${exceptional ? "Creates a fresh environment" : "Mutates group records"}</span>` : '<span class="tag">Read only</span>'}</div></section><section class="scenario-intent"><article><small>WHAT</small><strong>${escape(selected.description)}</strong></article><article><small>WHY</small><strong>${escape(selected.explanation)}</strong></article><article><small>HOW</small><div class="catalog-tags api">${selected.apiNames.map((item) => `<i>${escape(item)}</i>`).join("")}</div></article></section>${scenarioTypesPanel(usedTypes)}${groupState}<section id="code-run-section" class="disclosure work-section expanded"><div class="work-section-head"><button class="disclosure-toggle" type="button" aria-expanded="true"><span class="disclosure-chevron">›</span><span><small>PASTE-READY ABRAXAS SOURCE</small><strong>Code &amp; run</strong></span></button><div class="work-section-actions"><button id="copy-complete-example" class="secondary" type="button">Copy complete example</button><button id="run" class="primary" type="button" ${ready ? "" : "disabled"}>Run scenario</button></div></div><div class="disclosure-body"><section class="disclosure source-disclosure expanded"><button class="disclosure-toggle source-toggle" type="button" aria-expanded="true"><span class="disclosure-chevron">›</span><span><small>ABRAXAS EXAMPLE</small><strong>Scenario code</strong></span></button><div class="disclosure-body"><div class="source-copy-bar"><span>Exact paste-ready C# · Showcase proof plumbing omitted</span><button id="copy-scenario-code" class="secondary copy-source" type="button">Copy code</button></div><div id="scenario-code-editor" class="scenario-code-editor" aria-label="Read-only paste-ready Abraxas example"><pre class="code editor-fallback"><code>${escape(selected.source)}</code></pre></div></div></section></div></section><section id="proof-section" class="disclosure work-section"><div class="work-section-head"><button class="disclosure-toggle" type="button" aria-expanded="false"><span class="disclosure-chevron">›</span><span><small>LIVE EXECUTION</small><strong>Execution proof</strong></span></button><span id="run-timing" class="run-timing"></span></div><div class="disclosure-body" hidden><div id="result" class="result"><p class="empty-result">${exceptional ? "Choose Run scenario to replace the prior fundamentals files and execute the corresponding operation." : ready ? "Choose Run scenario to execute the corresponding operation against the active group RecordBase." : "Prepare the scenario group before executing this scenario."}</p></div></div></section></div>`;
  if (selected.commercialOnly) {
    content.querySelector(".scenario-workspace").classList.add("commercial-only");
    content.querySelector(".scenario-banner .eyebrow").textContent = `${selected.category} · COMMERCIAL-ONLY SCENARIO`;
    content.querySelector(".scenario-badges").insertAdjacentHTML("afterbegin", '<span class="tag commercial-tag">Commercial edition</span>');
    if (!available) {
      const runButton = content.querySelector("#run");
      runButton.disabled = false;
      runButton.textContent = "Commercial library required";
      runButton.title = "Explain why this example requires the commercial library";
      runButton.setAttribute("aria-haspopup", "dialog");
      runButton.setAttribute("aria-controls", "commercial-edition-dialog");
      content.querySelector("#result").innerHTML = '<p class="empty-result">Execution is unavailable in the FOSS build; the code remains readable above.</p>';
    }
  }
  wireDisclosures(content);
  mountCodeEditor(
    document.querySelector("#scenario-code-editor"),
    selected.source,
    (editor) => {
      scenarioCodeEditor = editor;
      if (pendingTypeHighlight) {
        const pending = pendingTypeHighlight;
        pendingTypeHighlight = null;
        highlightScenarioType(pending.typeName, pending.button);
      }
    },
  );
  wireScenarioTypeActions();
  document.querySelector("#copy-scenario-code").onclick = (event) => {
    event.stopPropagation();
    copySource(selected.source, "scenario code", event.currentTarget);
  };
  document.querySelector("#copy-complete-example").onclick = (event) => {
    event.stopPropagation();
    copySource(
      selected.completeSource,
      "complete Program.cs example",
      event.currentTarget,
    );
  };
  document.querySelector("#open-scenario-group").onclick = () =>
    openGroup(group.id);
  document.querySelector("#run").onclick = runScenario;
}
async function runScenario() {
  const button = document.querySelector("#run"),
    result = document.querySelector("#result"),
    codeSection = document.querySelector("#code-run-section"),
    proofSection = document.querySelector("#proof-section");
  if (!button || button.disabled) return;
  const selected = state.scenarios.find((item) => item.id === selectedScenario);
  if (selected?.commercialOnly && !state.commercialFeaturesAvailable) {
    openCommercialEditionNotice(selected.title);
    return;
  }
  button.disabled = true;
  const previousButtonText = button.textContent;
  button.textContent = "Running…";
  proofSection.classList.remove("execution-passed", "execution-failed");
  proofSection.classList.add("execution-running");
  setDisclosure(codeSection, false);
  setDisclosure(proofSection, true);
  status.textContent = "Running compiled scenario…";
  result.className = "result";
  result.innerHTML =
    '<p class="empty-result">Executing against the active group RecordBase…</p>';
  scrollExecutionProof(proofSection);
  try {
    const response = await call("run-scenario", {
      scenarioId: selectedScenario,
    });
    await renderExecutionProof(result, response, true);
    proofSection.classList.remove("execution-running");
    proofSection.classList.add(
      response.proof.passed ? "execution-passed" : "execution-failed",
    );
    document.querySelector("#run-timing").textContent =
      `${response.hostMilliseconds.toFixed(2)} ms host`;
    state.laboratory = response.laboratory;
    updateContext();
    footer.textContent = `${response.proof.passed ? "Passed" : "Failed"} · ${response.proof.elapsedMilliseconds.toFixed(2)} ms scenario`;
    scrollExecutionProof(proofSection);
  } catch (error) {
    proofSection.classList.remove("execution-running");
    proofSection.classList.add("execution-failed");
    result.classList.add("failed");
    result.innerHTML = `<p class="execution-error"><strong>Scenario failed</strong><span>${escape(error.message)}</span></p>`;
    scrollExecutionProof(proofSection);
    showError(error);
  } finally {
    button.disabled = false;
    button.textContent = previousButtonText;
    status.textContent = "Ready";
  }
}

function scrollExecutionProof(proofSection) {
  requestAnimationFrame(() =>
    proofSection.scrollIntoView({
      behavior: reducedMotion() ? "auto" : "smooth",
      block: "start",
    }),
  );
}
