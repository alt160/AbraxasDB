const pending = new Map();
let sequence = 0,
  state = null,
  currentView = "home",
  selectedScenario = null,
  selectedWhatIfCase = null,
  whatIfExpanded = false,
  selectedGroup = null,
  catalogCategoryFilter = null,
  catalogTargetScenarioId = null,
  selectedTypeSearchName = null,
  globalSearchQuery = "",
  creationBusy = false,
  pendingSetup = null,
  browserState = null,
  pageMotion = "fade",
  navigationIndex = -1,
  scenarioCodeEditor = null,
  typeDefinitionEditor = null,
  activeTypeDefinition = null,
  pendingTypeHighlight = null,
  typeDefinitionDrag = null,
  groupSetupEditing = false,
  pendingCleanup = null;
const codeEditors = [],
  expandedCategories = new Set(),
  expandedCatalogCategories = new Set();
const navigationHistory = [];

function call(command, payload = {}) {
  return new Promise((resolve, reject) => {
    const id = `r${++sequence}`;
    pending.set(id, { resolve, reject });
    chrome.webview.postMessage({ id, command, payload });
  });
}
chrome.webview.addEventListener("message", (event) => {
  const message = event.data;
  if (message.kind === "generation-progress") {
    showProgress(message);
    return;
  }
  if (!message.id) return;
  const request = pending.get(message.id);
  if (!request) return;
  pending.delete(message.id);
  message.ok
    ? request.resolve(message.result)
    : request.reject(new Error(message.error || "Request failed."));
});

const content = document.querySelector("#content"),
  status = document.querySelector("#status"),
  footer = document.querySelector("#footer-metrics"),
  footerContext = document.querySelector("#footer-context"),
  pill = document.querySelector("#dataset-pill"),
  pageTitle = document.querySelector("#page-title"),
  categoryNavigation = document.querySelector("#category-navigation"),
  explorerBackdrop = document.querySelector("#explorer-lightbox-backdrop"),
  breadcrumbs = document.querySelector("#breadcrumbs"),
  navigationBack = document.querySelector("#navigation-back"),
  navigationForward = document.querySelector("#navigation-forward");
const lightbox = document.querySelector("#creation-lightbox"),
  creationBody = document.querySelector("#creation-dialog-body"),
  creationTitle = document.querySelector("#creation-title"),
  creationEyebrow = document.querySelector("#creation-eyebrow"),
  acceptCreationButton = document.querySelector("#accept-creation"),
  cancelCreationButton = document.querySelector("#cancel-creation");
const conceptLightbox = document.querySelector("#concept-lightbox"),
  conceptBody = document.querySelector("#concept-dialog-body"),
  conceptTitle = document.querySelector("#concept-title"),
  conceptClose = document.querySelector("#concept-close"),
  conceptCloseIcon = document.querySelector("#concept-close-icon");
const typeDefinitionPopover = document.querySelector(
    "#type-definition-popover",
  ),
  typeDefinitionTitle = document.querySelector("#type-definition-title"),
  typeDefinitionDescription = document.querySelector(
    "#type-definition-description",
  ),
  typeDefinitionEditorHost = document.querySelector("#type-definition-editor"),
  typeDefinitionClose = document.querySelector("#type-definition-close"),
  copyTypeDefinition = document.querySelector("#copy-type-definition");
const escape = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (char) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        char
      ],
  );
const number = (value) => new Intl.NumberFormat().format(value || 0);
const bytes = (value) => {
  if (!value) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let index = 0,
    current = value;
  while (current >= 1024 && index < units.length - 1) {
    current /= 1024;
    index++;
  }
  return `${current.toFixed(index ? 1 : 0)} ${units[index]}`;
};
const reducedMotion = () =>
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;
const groupById = (id) => state.groups.find((group) => group.id === id);
const groupForCategory = (category) =>
  state.groups.find((group) => group.title === category);

function applyTheme(theme) {
  if (!theme) return;
  document.documentElement.style.setProperty("--accent", theme.accent);
  document.documentElement.style.setProperty(
    "--accent-contrast",
    theme.accentContrast,
  );
  document.documentElement.dataset.accentSource = theme.source;
}
function activeGroup() {
  return state.laboratory?.groupId ? groupById(state.laboratory.groupId) : null;
}
function updateContext() {
  const group = activeGroup();
  if (!group) {
    pill.textContent = state.startup.presence?.hasRecordBase
      ? "Saved RecordBase · closed"
      : "No RecordBase open";
    pill.classList.remove("ready");
    footerContext.innerHTML =
      '<span class="footer-empty-context">No group loaded</span>';
    return;
  }
  pill.textContent = `Ready · ${group.title}`;
  pill.classList.add("ready");
  const laboratory = state.laboratory,
    counts =
      group.id === "recordbase-foundations" || group.id === "stores"
        ? `${number(laboratory.customers)} customers · ${number(laboratory.orders)} orders`
        : group.id === "object-shapes-identity"
          ? `${number(laboratory.profiles)} profiles · ${number(laboratory.notes)} notes`
          : `${number(laboratory.customers + laboratory.orders + laboratory.profiles + laboratory.notes)} records`,
    modified = laboratory.lastModifiedUtc
      ? new Intl.DateTimeFormat(undefined, {
          dateStyle: "medium",
          timeStyle: "short",
        }).format(new Date(laboratory.lastModifiedUtc))
      : "Not yet written";
  footerContext.innerHTML = `<button class="footer-group-pill" type="button" title="Open ${escape(group.title)}">${escape(group.title)}</button><span>${bytes(laboratory.bytes)}</span><span>${escape(counts)}</span><span>Modified ${escape(modified)}</span>`;
  footerContext.querySelector(".footer-group-pill").onclick = () =>
    openGroup(group.id);
}
function setNavigation(view) {
  currentView = view;
  document
    .querySelectorAll(".nav")
    .forEach((button) =>
      button.classList.toggle("active", button.dataset.view === view),
    );
  renderCategoryNavigation();
}
async function copySource(source, label, button) {
  try {
    if (navigator.clipboard?.writeText)
      await navigator.clipboard.writeText(source);
    else {
      const textarea = document.createElement("textarea");
      textarea.value = source;
      textarea.style.position = "fixed";
      textarea.style.opacity = "0";
      document.body.append(textarea);
      textarea.select();
      if (!document.execCommand("copy"))
        throw new Error("Clipboard access was unavailable.");
      textarea.remove();
    }
    const previous = button?.textContent;
    if (button) {
      button.textContent = "Copied";
      button.classList.add("copied");
      setTimeout(() => {
        button.textContent = previous;
        button.classList.remove("copied");
      }, 1400);
    }
    footer.textContent = `Copied ${label} to the clipboard`;
  } catch (error) {
    showError(new Error(`Could not copy ${label}: ${error.message}`));
  }
}
