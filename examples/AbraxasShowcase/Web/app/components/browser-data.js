function showProgress(message) {
  const bar = document.querySelector("#progress-bar"),
    label = document.querySelector("#progress-label");
  if (bar)
    bar.style.width = `${message.total ? Math.min(100, (message.completed / message.total) * 100) : 0}%`;
  if (label)
    label.textContent = `${message.phase} · ${number(message.completed)} / ${number(message.total)}`;
}
async function loadBrowserPage(group) {
  const result = document.querySelector("#group-browser-result");
  if (!result || !browserState) return;
  result.innerHTML =
    '<p class="empty-result">Reading identities and selected values without hydrating full objects…</p>';
  status.textContent = "Reading store values…";
  try {
    const page = await call("browse-group-data", {
      groupId: group.id,
      ...browserState,
    });
    browserState = {
      storeName: page.storeName,
      propertyPaths: page.propertyPaths,
      pageIndex: page.pageIndex,
      pageSize: page.pageSize,
    };
    renderBrowserPage(result, group, page);
    footer.textContent = `${number(page.rows.length)} dynamic rows · page ${page.pageIndex + 1}`;
  } catch (error) {
    result.classList.add("failed");
    result.textContent = error.message;
    showError(error);
  } finally {
    status.textContent = "Ready";
  }
}
function renderBrowserPage(container, group, page) {
  const first = page.pageIndex * page.pageSize + 1,
    last = page.pageIndex * page.pageSize + page.rows.length;
  container.className = "browser-result";
  container.innerHTML = `<div class="browser-page-head"><div><p class="eyebrow">${escape(page.storeName)} · ${escape(page.recordType)}</p><strong>${page.rows.length ? `${number(first)}–${number(last)} of ${number(page.totalRecords)}` : "No records"}</strong></div><div class="browser-pager"><button id="browser-previous" class="secondary" type="button" ${page.hasPrevious ? "" : "disabled"}>Previous</button><span>Page ${page.pageIndex + 1}</span><button id="browser-next" class="secondary" type="button" ${page.hasNext ? "" : "disabled"}>Next</button></div></div><div class="browser-tree">${page.rows.map((row) => browserRowMarkup(page, row)).join("")}</div>`;
  document.querySelector("#browser-previous").onclick = () =>
    changeBrowserPage(group, -1);
  document.querySelector("#browser-next").onclick = () =>
    changeBrowserPage(group, 1);
  container.querySelectorAll(".browser-row-head").forEach((row) => {
    row.onclick = () =>
      inspectBrowserRow(group, page, row.closest(".browser-row"));
    row.onkeydown = (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        inspectBrowserRow(group, page, row.closest(".browser-row"));
      }
    };
  });
}
function browserRowMarkup(page, row) {
  return `<div class="browser-row" data-record-id="${row.recordID}"><div class="browser-row-head" role="button" tabindex="0" aria-expanded="false"><span class="node-toggle">›</span><div class="browser-identity"><small>ABRAXAS RECORDID</small><strong>${row.recordID}</strong></div>${row.values.map((value) => `<div class="browser-value" title="${escape(value.runtimeType)}"><small>${escape(value.path)}</small><strong>${escape(value.text)}</strong></div>`).join("")}<div class="node-access">Dynamic value read</div></div><div class="browser-object" hidden></div></div>`;
}
function changeBrowserPage(group, delta) {
  browserState.pageIndex = Math.max(0, browserState.pageIndex + delta);
  loadBrowserPage(group);
}
async function inspectBrowserRow(group, page, row) {
  const head = row.querySelector(".browser-row-head"),
    target = row.querySelector(".browser-object"),
    toggle = head.querySelector(".node-toggle");
  if (head.dataset.busy === "true") return;
  if (row.dataset.loaded === "true") {
    const opening = target.hidden;
    animateRegion(target, opening);
    toggle.classList.toggle("open", opening);
    head.setAttribute("aria-expanded", String(opening));
    return;
  }
  head.dataset.busy = "true";
  toggle.classList.add("busy");
  try {
    const root = await call("inspect-group-record", {
      groupId: group.id,
      storeName: page.storeName,
      recordId: row.dataset.recordId,
    });
    target.innerHTML = `<section class="browser-object-header"><span>LIVE .NET OBJECT</span><strong>${escape(root.runtimeType)}</strong></section>${objectNodeMarkup(root, true)}`;
    row.dataset.loaded = "true";
    animateRegion(target, true);
    toggle.classList.add("open");
    head.setAttribute("aria-expanded", "true");
    wireObjectExplorer(target);
    const rootNode = target.querySelector(".object-node");
    if (root.expandable) expandObjectNode(rootNode);
    footer.textContent = `Record ${row.dataset.recordId} retrieved when expanded`;
  } catch (error) {
    showError(error);
  } finally {
    delete head.dataset.busy;
    toggle.classList.remove("busy");
  }
}

