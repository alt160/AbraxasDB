async function renderExecutionProof(
  container,
  response,
  expandFirstLevel = false,
) {
  closeFloatingExplorers();
  const proof = response.proof,
    results = response.results || [];
  container.classList.toggle("failed", !proof.passed);
  container.innerHTML = `<section class="proof-summary"><div class="proof-status ${proof.passed ? "passed" : "failed"}"><span>${proof.passed ? "PASS" : "FAIL"}</span><div><strong>${escape(proof.summary)}</strong><small>${proof.elapsedMilliseconds ? `Scenario completed in ${proof.elapsedMilliseconds.toFixed(2)} ms · ` : ""}explorer reads are timed separately</small></div></div>${proof.facts.length ? `<div class="proof-facts">${proof.facts.map((fact) => `<div><span>${escape(fact.label)}</span><strong>${escape(fact.value)}</strong></div>`).join("")}</div>` : ""}</section><section class="result-collection"><div class="result-collection-heading"><div><p class="eyebrow">RETURNED RESULTS</p><h3>Results · ${results.length}</h3></div><span class="live-badge"><i></i>Held in C#</span></div><div class="result-items">${results.map((item) => resultItemMarkup(item, results.length === 1)).join("")}</div></section>`;
  wireObjectExplorer(container);
  wireResultItems(container, results);
  if (expandFirstLevel && results.length === 1) {
    const rootNode = container.querySelector(".result-item.expanded .root-node");
    if (rootNode?.querySelector(':scope > .node-row[data-expandable="true"]'))
      await expandObjectNode(rootNode);
  }
}
function resultItemMarkup(item, expanded) {
  const root = item.inspection,
    identity = item.recordID == null
      ? ""
      : `<span class="result-identity"><small>RecordID</small><strong>${escape(item.recordID)}</strong></span>`;
  if (!root.expandable) {
    return `<article class="result-item terminal-result"><div class="result-item-row"><span class="result-item-spacer" aria-hidden="true"></span><div class="result-item-title"><small>${escape(item.label || `Result ${item.index + 1}`)}</small><strong>${escape(root.runtimeType)}</strong></div>${identity}<span class="result-preview">${escape(root.summary)}</span></div></article>`;
  }
  return `<article class="result-item ${expanded ? "expanded" : ""}" data-result-index="${item.index}"><div class="result-item-row" role="button" tabindex="0" aria-expanded="${expanded}"><span class="result-item-chevron">›</span><div class="result-item-title"><small>${escape(item.label || `Result ${item.index + 1}`)}</small><strong>${escape(root.runtimeType)}</strong></div>${identity}<span class="result-preview">${escape(root.summary)}</span><button class="result-explore-pill secondary" type="button">Explore</button></div><div class="result-item-body" ${expanded ? "" : "hidden"}>${objectExplorerMarkup(root, false)}</div></article>`;
}
function objectExplorerMarkup(root, floating) {
  return `<section class="object-explorer ${floating ? "floating-object-explorer" : "inline-object-explorer"}"><div class="explorer-heading ${floating ? "floating-explorer-handle" : ""}"><div><p class="eyebrow">LIVE .NET OBJECT</p><h3>${escape(root.runtimeType)}</h3>${root.declaredType !== root.runtimeType ? `<small>Declared as ${escape(root.declaredType)}</small>` : ""}</div>${floating ? '<button class="floating-explorer-close secondary" type="button">Close</button>' : ""}</div><p class="explorer-note">Expand a row to read its real members or enumerate its real values from the retained C# object.</p><div class="tree-root">${objectNodeMarkup(root, true)}</div></section>`;
}
function wireResultItems(container, results) {
  container.querySelectorAll(".result-item:not(.terminal-result)").forEach((item) => {
    const row = item.querySelector(".result-item-row"),
      body = item.querySelector(".result-item-body"),
      toggle = () => {
        const opening = !item.classList.contains("expanded");
        item.classList.toggle("expanded", opening);
        row.setAttribute("aria-expanded", String(opening));
        animateRegion(body, opening);
      };
    row.onclick = (event) => {
      if (!event.target.closest(".result-explore-pill")) toggle();
    };
    row.onkeydown = (event) => {
      if ((event.key === "Enter" || event.key === " ") && !event.target.closest(".result-explore-pill")) {
        event.preventDefault();
        toggle();
      }
    };
    item.querySelector(".result-explore-pill").onclick = (event) => {
      event.stopPropagation();
      openFloatingExplorer(results[Number(item.dataset.resultIndex)]);
    };
  });
}
function objectNodeMarkup(node, isRoot = false) {
  const toggle = node.expandable
      ? '<span class="node-toggle">›</span>'
      : '<span class="node-leaf">·</span>',
    reference = node.referenceTo
      ? `<span class="node-reference">↩ ${escape(node.referenceTo)}</span>`
      : "",
    interaction = node.expandable
      ? 'data-expandable="true" role="button" tabindex="0" aria-expanded="false"'
      : "";
  const inspectionNotice = node.state === "error"
    ? `<div class="inspection-notice"><span>Value unavailable</span><small>Object explorer · ${escape(node.access)}</small><button class="inspection-details-toggle" type="button" aria-expanded="false">Details</button></div>`
    : `${escape(node.summary)}${reference}`;
  return `<div class="object-node ${isRoot ? "root-node" : ""}" data-session-id="${escape(node.sessionId)}" data-node-id="${escape(node.nodeId)}"><div class="node-row" ${interaction}>${toggle}<div class="node-name">${escape(node.name)}</div><div class="node-type" title="Declared: ${escape(node.declaredType)}">${escape(node.runtimeType)}</div><div class="node-value ${node.state === "error" ? "inspection-unavailable" : ""}">${inspectionNotice}</div><div class="node-access">${escape(node.access)}</div></div>${node.state === "error" ? `<div class="inspection-details" hidden><strong>Object explorer inspection detail</strong><p>This .NET access failed while reading the returned object. The scenario result is unchanged.</p><pre>${escape(node.inspectionDetails || "No exception detail was provided.")}</pre></div>` : ""}<div class="node-children" hidden></div></div>`;
}
function wireObjectExplorer(container) {
  container.querySelectorAll('.inspection-details-toggle:not([data-wired])').forEach((button) => {
    button.dataset.wired = "true";
    button.onclick = (event) => {
      event.stopPropagation();
      const details = button.closest(".object-node").querySelector(":scope > .inspection-details");
      const opening = details.hidden;
      animateRegion(details, opening);
      button.setAttribute("aria-expanded", String(opening));
      button.textContent = opening ? "Hide details" : "Details";
    };
  });
  container
    .querySelectorAll('.node-row[data-expandable="true"]:not([data-wired])')
    .forEach((row) => {
      row.dataset.wired = "true";
      row.onclick = () => expandObjectNode(row.closest(".object-node"));
      row.onkeydown = (event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          expandObjectNode(row.closest(".object-node"));
        }
      };
    });
}
async function expandObjectNode(node) {
  const row = node.querySelector(":scope > .node-row"),
    toggle = row.querySelector(".node-toggle"),
    children = node.querySelector(":scope > .node-children");
  if (row.dataset.busy === "true") return;
  if (node.dataset.loaded === "true") {
    const opening = children.hidden;
    animateRegion(children, opening);
    toggle.classList.toggle("open", opening);
    row.setAttribute("aria-expanded", String(opening));
    return;
  }
  row.dataset.busy = "true";
  toggle.classList.add("busy");
  try {
    const expansion = await call("expand-object", {
      sessionId: node.dataset.sessionId,
      nodeId: node.dataset.nodeId,
    });
    children.innerHTML = `${expansion.children.map((child) => objectNodeMarkup(child)).join("")}${expansion.note ? `<div class="expansion-note">${escape(expansion.note)} · ${expansion.elapsedMilliseconds.toFixed(2)} ms explorer read</div>` : ""}${expansion.truncated ? '<div class="expansion-warning">Additional children were intentionally bounded.</div>' : ""}`;
    await animateRegion(children, true);
    node.dataset.loaded = "true";
    toggle.classList.add("open");
    row.setAttribute("aria-expanded", "true");
    wireObjectExplorer(children);
    footer.textContent = `Object explorer · ${escape(expansion.viewer)} · ${expansion.elapsedMilliseconds.toFixed(2)} ms`;
  } catch (error) {
    children.innerHTML = `<div class="expansion-warning">${escape(error.message)}</div>`;
    animateRegion(children, true);
    showError(error);
  } finally {
    delete row.dataset.busy;
    toggle.classList.remove("busy");
  }
}
let floatingExplorerZ = 1200,
  floatingExplorerOffset = 0;
function openFloatingExplorer(item) {
  const windowElement = document.createElement("div"),
    offset = floatingExplorerOffset++ % 8;
  windowElement.className = "floating-explorer-window";
  windowElement.style.left = `${Math.min(window.innerWidth - 580, 110 + offset * 28)}px`;
  windowElement.style.top = `${Math.min(window.innerHeight - 420, 70 + offset * 24)}px`;
  windowElement.style.zIndex = String(++floatingExplorerZ);
  windowElement.innerHTML = objectExplorerMarkup(item.inspection, true);
  document.body.appendChild(windowElement);
  wireObjectExplorer(windowElement);
  windowElement.querySelector(".floating-explorer-close").onclick = () => windowElement.remove();
  windowElement.onpointerdown = () => (windowElement.style.zIndex = String(++floatingExplorerZ));
  wireFloatingExplorerDrag(windowElement);
  if (!reducedMotion())
    windowElement.animate(
      [
        { opacity: 0, transform: "translateY(12px) scale(.98)" },
        { opacity: 1, transform: "none" },
      ],
      { duration: 180, easing: "cubic-bezier(.2,.8,.2,1)" },
    );
}
function wireFloatingExplorerDrag(windowElement) {
  const handle = windowElement.querySelector(".floating-explorer-handle");
  let drag = null;
  handle.onpointerdown = (event) => {
    if (event.target.closest("button")) return;
    const bounds = windowElement.getBoundingClientRect();
    drag = { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
    handle.setPointerCapture(event.pointerId);
  };
  handle.onpointermove = (event) => {
    if (!drag) return;
    windowElement.style.left = `${Math.max(0, Math.min(window.innerWidth - 160, event.clientX - drag.x))}px`;
    windowElement.style.top = `${Math.max(0, Math.min(window.innerHeight - 80, event.clientY - drag.y))}px`;
  };
  handle.onpointerup = handle.onpointercancel = (event) => {
    drag = null;
    if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);
  };
}
function closeFloatingExplorers() {
  document.querySelectorAll(".floating-explorer-window").forEach((item) => item.remove());
}
function showError(error) {
  const toast = document.querySelector("#toast");
  toast.textContent = error.message || String(error);
  toast.hidden = false;
  if (!reducedMotion())
    toast.animate(
      [
        { opacity: 0, transform: "translateY(8px)" },
        { opacity: 1, transform: "none" },
      ],
      { duration: 180, easing: "ease-out" },
    );
  setTimeout(() => {
    if (reducedMotion()) {
      toast.hidden = true;
      return;
    }
    const animation = toast.animate([{ opacity: 1 }, { opacity: 0 }], {
      duration: 140,
      easing: "ease-in",
    });
    animation.onfinish = () => (toast.hidden = true);
  }, 6000);
}
