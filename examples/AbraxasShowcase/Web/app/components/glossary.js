// One glossary is shared by group, scenario, and catalog prose. Code editors are never annotated.
const glossaryPopover = document.querySelector("#glossary-popover");
const glossaryAliases = [
  ["RecordStores", "RecordStore<T>"],
  ["RecordStore<T>", "RecordStore<T>"],
  ["RecordStore", "RecordStore<T>"],
  ["RecordBases", "RecordBase"],
  ["RecordBase", "RecordBase"],
  ["RecordIDs", "RecordID"],
  ["RecordID", "RecordID"],
  ["CreateOrOpen", "CreateOrOpen"],
  ["composite key indexes", "Composite key index"],
  ["composite key index", "Composite key index"],
  ["composite indexes", "Composite key index"],
  ["composite index", "Composite key index"],
  ["composite keys", "Composite key index"],
  ["composite key", "Composite key index"],
  ["indexes", "Index"],
  ["index", "Index"],
  ["reprojection", "Reprojection"],
  ["projection", "Reprojection"],
  ["value-only reads", "Value-only read"],
  ["value-only read", "Value-only read"],
  ["conditions", "Condition"],
  ["condition", "Condition"],
  ["natural order", "Natural order"],
  ["readers", "Reader"],
  ["reader", "Reader"],
  ["aggregates", "Grouped aggregate"],
  ["aggregate", "Grouped aggregate"],
  ["PutManyFast", "PutManyFast"],
  ["OpenUnordered", "Unordered reader"],
  ["Inheto", "Inheto"],
  ["LibraDex", "LibraDex"],
];
const glossaryFallback = new Map([
  ["Inheto", "The part of Abraxas that saves member paths and values. A later read can fill matching members on a .NET type or return selected values directly."],
  ["LibraDex", "The index engine Abraxas uses to keep ordered lookup keys for finding and walking records."],
]);
const glossaryAliasMap = new Map(glossaryAliases.map(([alias, term]) => [alias.toLowerCase(), term]));
const glossaryPattern = new RegExp(
  `(?<![\\w])(?:${glossaryAliases.map(([alias]) => alias.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|")})(?![\\w])`,
  "gi",
);
const glossaryTargets = [
  ".welcome-hero p:not(.eyebrow)",
  ".welcome-grid article p:not(.eyebrow)",
  ".catalog-intro p:not(.eyebrow)",
  ".catalog-group-action p",
  ".catalog-entry-heading em",
  ".catalog-entry-body > p",
  ".group-banner p:not(.eyebrow)",
  ".group-empty-card > div > span",
  ".group-ready-card > div > span",
  ".scenario-banner p:not(.eyebrow)",
  ".scenario-intent strong",
  ".scenario-group-state > div > span",
  ".fundamentals-intro > div > p:not(.eyebrow)",
  ".concept-grid article strong",
  ".concept-grid article p",
  ".group-scenarios button small",
  ".what-if-page p:not(.eyebrow)",
];
let glossaryAnchor = null;
let glossaryPinned = false;
let glossaryHideTimer = null;

function glossaryEntry(term) {
  for (const group of state?.groups || []) {
    const concept = group.concepts?.find((item) => item.term === term);
    if (concept)
      return { term, meaning: concept.meaning, detail: concept.detailIntroduction, concept, group };
  }
  return { term, meaning: glossaryFallback.get(term) || "", detail: "", concept: null, group: null };
}

function annotateGlossary(root) {
  if (!root || !state?.groups) return;
  for (const target of root.querySelectorAll(glossaryTargets.join(","))) {
    const walker = document.createTreeWalker(target, NodeFilter.SHOW_TEXT);
    const textNodes = [];
    while (walker.nextNode()) {
      const node = walker.currentNode;
      if (node.parentElement?.closest(".glossary-term") || !node.textContent.trim()) continue;
      textNodes.push(node);
    }
    for (const node of textNodes) {
      const source = node.textContent;
      glossaryPattern.lastIndex = 0;
      if (!glossaryPattern.test(source)) continue;
      glossaryPattern.lastIndex = 0;
      const fragment = document.createDocumentFragment();
      let offset = 0;
      for (const match of source.matchAll(glossaryPattern)) {
        if (match.index > offset) fragment.append(document.createTextNode(source.slice(offset, match.index)));
        const term = glossaryAliasMap.get(match[0].toLowerCase());
        const trigger = document.createElement("span");
        trigger.className = "glossary-term";
        trigger.tabIndex = 0;
        trigger.setAttribute("role", "button");
        trigger.setAttribute("aria-label", `Define ${match[0]}`);
        trigger.setAttribute("aria-expanded", "false");
        trigger.dataset.glossaryTerm = term;
        trigger.textContent = match[0];
        trigger.addEventListener("focus", () => showGlossary(trigger));
        trigger.addEventListener("blur", scheduleGlossaryDismiss);
        fragment.append(trigger);
        offset = match.index + match[0].length;
      }
      if (offset < source.length) fragment.append(document.createTextNode(source.slice(offset)));
      node.replaceWith(fragment);
    }
  }
}

function positionGlossary() {
  if (!glossaryAnchor?.isConnected || glossaryPopover.hidden) return;
  const anchor = glossaryAnchor.getBoundingClientRect();
  const popup = glossaryPopover.getBoundingClientRect();
  const left = Math.max(12, Math.min(anchor.left, window.innerWidth - popup.width - 12));
  const below = anchor.bottom + 10;
  const top = below + popup.height <= window.innerHeight - 12
    ? below
    : Math.max(12, anchor.top - popup.height - 10);
  glossaryPopover.style.left = `${left}px`;
  glossaryPopover.style.top = `${top}px`;
}

function showGlossary(anchor, pinned = false) {
  if (glossaryPinned && !pinned && anchor !== glossaryAnchor) return;
  clearTimeout(glossaryHideTimer);
  if (anchor === glossaryAnchor && !glossaryPopover.hidden) {
    if (pinned) {
      glossaryPinned = true;
      glossaryPopover.dataset.pinned = "true";
    }
    positionGlossary();
    return;
  }
  const term = anchor.dataset.glossaryTerm;
  const entry = glossaryEntry(term);
  if (!entry.meaning) return;
  if (glossaryAnchor && glossaryAnchor !== anchor)
    glossaryAnchor.setAttribute("aria-expanded", "false");
  glossaryAnchor = anchor;
  glossaryPinned = pinned;
  anchor.setAttribute("aria-expanded", "true");
  glossaryPopover.innerHTML = `<div class="glossary-popover-head"><small>ABRAXAS TERM</small><strong>${escape(entry.term)}</strong><button type="button" class="glossary-close" aria-label="Close definition">×</button></div><p>${escape(entry.meaning)}</p>${entry.detail ? `<p class="glossary-detail">${escape(entry.detail)}</p>` : ""}<div class="glossary-actions">${entry.concept?.details?.length ? '<button type="button" data-glossary-detail>Learn more</button>' : ""}${entry.group ? `<button type="button" data-glossary-group="${escape(entry.group.id)}">Open group</button>` : ""}</div>`;
  glossaryPopover.hidden = false;
  glossaryPopover.dataset.pinned = String(pinned);
  positionGlossary();
  if (!reducedMotion())
    glossaryPopover.animate([{ opacity: 0, transform: "translateY(5px)" }, { opacity: 1, transform: "none" }],
      { duration: 140, easing: "ease-out" });
  glossaryPopover.querySelector(".glossary-close").onclick = dismissGlossary;
  glossaryPopover.querySelector("[data-glossary-detail]")?.addEventListener("click", () => {
    dismissGlossary();
    openConceptDetails(entry.concept);
  });
  glossaryPopover.querySelector("[data-glossary-group]")?.addEventListener("click", () => {
    dismissGlossary();
    openGroup(entry.group.id);
  });
}

function dismissGlossary() {
  clearTimeout(glossaryHideTimer);
  glossaryAnchor?.setAttribute("aria-expanded", "false");
  glossaryAnchor = null;
  glossaryPinned = false;
  glossaryPopover.hidden = true;
  glossaryPopover.dataset.pinned = "false";
  glossaryPopover.innerHTML = "";
}

function scheduleGlossaryDismiss() {
  clearTimeout(glossaryHideTimer);
  if (!glossaryPinned) glossaryHideTimer = setTimeout(dismissGlossary, 180);
}

document.addEventListener("pointerover", (event) => {
  const trigger = event.target.closest?.(".glossary-term");
  if (trigger) showGlossary(trigger);
});
document.addEventListener("pointerout", (event) => {
  const trigger = event.target.closest?.(".glossary-term");
  const related = event.relatedTarget instanceof Node ? event.relatedTarget : null;
  if (trigger && !trigger.contains(related) && !glossaryPopover.contains(related))
    scheduleGlossaryDismiss();
});
document.addEventListener("focusin", (event) => {
  const trigger = event.target.closest?.(".glossary-term");
  if (trigger) showGlossary(trigger);
});
document.addEventListener("focusout", (event) => {
  const related = event.relatedTarget instanceof Node ? event.relatedTarget : null;
  if (event.target.closest?.(".glossary-term") && !glossaryPopover.contains(related))
    scheduleGlossaryDismiss();
});
document.addEventListener("click", (event) => {
  const trigger = event.target.closest?.(".glossary-term");
  if (!trigger) return;
  event.preventDefault();
  event.stopPropagation();
  if (glossaryPinned && glossaryAnchor === trigger) dismissGlossary();
  else showGlossary(trigger, true);
}, true);
document.addEventListener("keydown", (event) => {
  const trigger = event.target.closest?.(".glossary-term");
  if (trigger && (event.key === "Enter" || event.key === " ")) {
    event.preventDefault();
    event.stopPropagation();
    showGlossary(trigger, true);
  } else if (event.key === "Escape" && !glossaryPopover.hidden) {
    event.stopImmediatePropagation();
    dismissGlossary();
  }
});
document.addEventListener("pointerdown", (event) => {
  if (glossaryPinned && !glossaryPopover.contains(event.target) && !event.target.closest?.(".glossary-term"))
    dismissGlossary();
});
glossaryPopover.addEventListener("pointerenter", () => clearTimeout(glossaryHideTimer));
glossaryPopover.addEventListener("pointerleave", scheduleGlossaryDismiss);
window.addEventListener("resize", positionGlossary);
content.addEventListener("scroll", positionGlossary, { passive: true });
