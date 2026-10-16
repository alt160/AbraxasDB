/* This function is serialized into a saved page; it has no Showcase host dependency. */
function initializeExportPage() {
  const data = JSON.parse(document.querySelector("#export-data").textContent);
  let activeData = data;
  function selectPageData(node) {
    activeData = data.pages?.[node?.closest?.(".export-page")?.dataset.exportPage] || data;
  }
  const documentBody = document.querySelector(".export-document");
  const dialog = document.createElement("dialog");
  dialog.className = "export-dialog";
  dialog.innerHTML = '<div class="export-dialog-head"><h2></h2><button type="button" class="export-close" aria-label="Close">×</button></div><div class="export-dialog-content"></div>';
  documentBody.appendChild(dialog);
  dialog.querySelector(".export-close").onclick = () => dialog.close();
  dialog.addEventListener("click", (event) => { if (event.target === dialog) dialog.close(); });
  const dialogTitle = dialog.querySelector("h2");
  const dialogContent = dialog.querySelector(".export-dialog-content");

  function openDialog(title, build) {
    dialogTitle.textContent = title;
    dialogContent.replaceChildren();
    build(dialogContent);
    dialog.showModal();
  }
  function textNode(tag, value, className = "") {
    const node = document.createElement(tag);
    node.textContent = value || "";
    if (className) node.className = className;
    return node;
  }
  async function copyText(value, button) {
    try {
      await navigator.clipboard.writeText(value);
      const before = button.textContent;
      button.textContent = "Copied";
      setTimeout(() => { button.textContent = before; }, 1400);
    } catch {
      button.textContent = "Select the text to copy";
    }
  }
  function openDefinition(name) {
    const type = activeData.types[name];
    if (!type) return;
    openDialog(name, (body) => {
      body.append(textNode("p", type.description));
      body.append(textNode("pre", type.source, "export-code"));
      const copy = textNode("button", "Copy definition", "export-action");
      copy.type = "button";
      copy.onclick = () => copyText(type.source, copy);
      body.append(copy);
    });
  }
  function openConcept(concept) {
    if (!concept?.details?.length) return;
    openDialog(concept.term, (body) => {
      body.append(textNode("p", concept.detailIntroduction));
      if (concept.sharedFileBaseName)
        body.append(textNode("p", `Files share the name ${concept.sharedFileBaseName}; their extensions distinguish their jobs.`, "export-concept-stem"));
      for (const detail of concept.details) {
        const card = document.createElement("article");
        card.className = "export-detail-card";
        card.append(textNode("h3", detail.title));
        if (concept.sharedFileBaseName && detail.title.startsWith("."))
          card.append(textNode("code", concept.sharedFileBaseName + detail.title));
        card.append(textNode("p", detail.description));
        body.append(card);
      }
    });
  }
  function openRemedy() {
    const remedy = activeData.remedy;
    if (!remedy) return;
    openDialog(remedy.title, (body) => {
      body.append(textNode("p", remedy.explanation));
      const tabs = document.createElement("div");
      tabs.className = "export-tabs";
      const code = textNode("pre", "", "export-code");
      const copy = textNode("button", "Copy code", "export-action");
      copy.type = "button";
      let active = 0;
      function select(index) {
        active = index;
        code.textContent = remedy.examples[index].code;
        tabs.querySelectorAll("button").forEach((tab, i) => tab.setAttribute("aria-selected", String(i === index)));
      }
      remedy.examples.forEach((example, index) => {
        const tab = textNode("button", example.label);
        tab.type = "button";
        tab.setAttribute("role", "tab");
        tab.onclick = () => select(index);
        tabs.append(tab);
      });
      if (remedy.examples.length > 1) body.append(tabs);
      body.append(code, copy);
      copy.onclick = () => copyText(remedy.examples[active].code, copy);
      select(0);
    });
  }

  const tooltip = document.createElement("aside");
  tooltip.className = "export-tooltip";
  tooltip.hidden = true;
  documentBody.append(tooltip);
  let tooltipAnchor = null;
  let pinned = false;
  let hideTimer = null;
  function hideTip() {
    clearTimeout(hideTimer);
    tooltip.hidden = true;
    tooltip.replaceChildren();
    tooltipAnchor?.setAttribute("aria-expanded", "false");
    tooltipAnchor = null;
    pinned = false;
  }
  function scheduleHide() {
    clearTimeout(hideTimer);
    if (!pinned) hideTimer = setTimeout(hideTip, 180);
  }
  function showTip(anchor, pin = false) {
    const term = anchor.dataset.glossaryTerm;
    selectPageData(anchor);
    const entry = activeData.terms[term];
    if (!entry?.meaning) return;
    clearTimeout(hideTimer);
    tooltipAnchor?.setAttribute("aria-expanded", "false");
    tooltipAnchor = anchor;
    pinned = pin;
    anchor.setAttribute("aria-expanded", "true");
    tooltip.replaceChildren(textNode("strong", term), textNode("p", entry.meaning));
    if (entry.detail) tooltip.append(textNode("p", entry.detail));
    if (entry.concept?.details?.length) {
      const more = textNode("button", "Learn more", "export-action");
      more.type = "button";
      more.onclick = () => { hideTip(); openConcept(entry.concept); };
      tooltip.append(more);
    }
    tooltip.hidden = false;
    const box = anchor.getBoundingClientRect();
    const left = Math.max(12, Math.min(box.left, innerWidth - tooltip.offsetWidth - 12));
    const below = box.bottom + 8;
    tooltip.style.left = `${left}px`;
    tooltip.style.top = `${below + tooltip.offsetHeight < innerHeight - 12 ? below : Math.max(12, box.top - tooltip.offsetHeight - 8)}px`;
  }
  tooltip.onpointerenter = () => clearTimeout(hideTimer);
  tooltip.onpointerleave = scheduleHide;
  document.addEventListener("pointerover", (event) => {
    const term = event.target.closest?.(".glossary-term");
    if (term && (!pinned || term === tooltipAnchor)) showTip(term, pinned);
  });
  document.addEventListener("pointerout", (event) => {
    if (event.target.closest?.(".glossary-term") && !tooltip.contains(event.relatedTarget)) scheduleHide();
  });
  document.addEventListener("focusin", (event) => {
    const term = event.target.closest?.(".glossary-term");
    if (term) showTip(term);
  });
  document.addEventListener("focusout", (event) => {
    if (event.target.closest?.(".glossary-term") && !tooltip.contains(event.relatedTarget)) scheduleHide();
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") hideTip();
    if ((event.key === "Enter" || event.key === " ") && event.target.matches?.(".glossary-term")) {
      event.preventDefault();
      showTip(event.target, true);
    }
  });

  documentBody.addEventListener("click", (event) => {
    selectPageData(event.target);
    const term = event.target.closest?.(".glossary-term");
    if (term) { showTip(term, true); return; }
    const button = event.target.closest?.("button.export-action");
    if (!button) { if (pinned && !tooltip.contains(event.target)) hideTip(); return; }
    if (button.matches(".disclosure-toggle")) {
      const panel = button.closest(".disclosure");
      const body = panel?.querySelector(":scope > .disclosure-body");
      if (body) {
        body.hidden = !body.hidden;
        button.setAttribute("aria-expanded", String(!body.hidden));
        panel.classList.toggle("expanded", !body.hidden);
      }
    } else if (button.matches(".catalog-category-toggle, .catalog-entry-toggle")) {
      const panel = button.closest(".catalog-category, .catalog-entry");
      const body = panel?.querySelector(":scope > .catalog-category-body, :scope > .catalog-entry-body");
      if (body) {
        body.hidden = !body.hidden;
        button.setAttribute("aria-expanded", String(!body.hidden));
      }
    } else if (button.matches(".what-if-show-how")) openRemedy();
    else if (button.matches(".copy-source")) {
      const source = button.closest(".source-disclosure")?.querySelector(".export-code")?.textContent;
      if (source) copyText(source, button);
    }
    else if (button.matches("#copy-complete-example, #copy-group-source, .export-complete-example, .export-group-source") && activeData.completeSource)
      copyText(activeData.completeSource, button);
    else if (button.dataset.viewType) openDefinition(button.dataset.viewType);
    else if (button.dataset.copyType && activeData.types[button.dataset.copyType])
      copyText(activeData.types[button.dataset.copyType].source, button);
    else if (button.dataset.conceptIndex)
      openConcept(activeData.concepts[Number(button.dataset.conceptIndex)]);
  });
}
