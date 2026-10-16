function renderGroupBrowser(group, ready) {
  const stores = ready ? state.groupStores || [] : [],
    store =
      stores.find((item) => item.name === browserState?.storeName) || stores[0];
  if (store && !browserState)
    browserState = {
      storeName: store.name,
      propertyPaths: defaultBrowserPaths(store),
      pageIndex: 0,
      pageSize: 25,
    };
  const expanded = ready;
  return `<section id="group-browser-section" class="disclosure work-section ${expanded ? "expanded" : ""}"><div class="work-section-head"><button class="disclosure-toggle" type="button" aria-expanded="${expanded}"><span class="disclosure-chevron">›</span><span><small>CONNECTED DATA</small><strong>Browse store records</strong></span></button></div><div class="disclosure-body" ${expanded ? "" : "hidden"}>${ready ? `<div class="browser-controls"><div class="field"><label for="browser-store">Store</label><select id="browser-store">${stores.map((item) => `<option value="${escape(item.name)}" ${item.name === store?.name ? "selected" : ""}>${escape(item.name)} · ${number(item.totalRecords)} records</option>`).join("")}</select></div><div class="field"><label for="browser-property-one">Preview property 1</label><select id="browser-property-one">${browserPropertyOptions(store, browserState?.propertyPaths?.[0])}</select></div><div class="field"><label for="browser-property-two">Preview property 2</label><select id="browser-property-two"><option value="">None</option>${browserPropertyOptions(store, browserState?.propertyPaths?.[1])}</select></div><div class="field"><label for="browser-page-size">Rows per page</label><select id="browser-page-size">${[25, 50, 100].map((size) => `<option ${size === (browserState?.pageSize || 25) ? "selected" : ""}>${size}</option>`).join("")}</select></div></div><div class="browser-read-note">Preview values use <code>Readers.OpenValuesWithIds(...)</code> and runtime-shaped <code>GetObject(...)</code>; full objects are retrieved only when you expand a row.</div><div id="group-browser-result" class="browser-result"><p class="empty-result">Reading the first page…</p></div>` : '<p class="empty-result">Prepare or connect this group before browsing its stores.</p>'}</div></section>`;
}
function defaultBrowserPaths(store) {
  const scalar = store.properties.filter((property) => property.scalar);
  return (scalar.length ? scalar : store.properties)
    .slice(0, 2)
    .map((property) => property.path);
}
function browserPropertyOptions(store, selected) {
  return (store?.properties || [])
    .map(
      (property) =>
        `<option value="${escape(property.path)}" ${property.path === selected ? "selected" : ""}>${escape(property.path)} · ${escape(property.type)}</option>`,
    )
    .join("");
}
function wireGroupBrowser(group, ready) {
  if (!ready) return;
  const storeSelect = document.querySelector("#browser-store"),
    first = document.querySelector("#browser-property-one"),
    second = document.querySelector("#browser-property-two"),
    size = document.querySelector("#browser-page-size");
  storeSelect.onchange = () => {
    const store = state.groupStores.find(
      (item) => item.name === storeSelect.value,
    );
    browserState = {
      storeName: store.name,
      propertyPaths: defaultBrowserPaths(store),
      pageIndex: 0,
      pageSize: Number(size.value),
    };
    render(false);
  };
  const refresh = () => {
    browserState = {
      storeName: storeSelect.value,
      propertyPaths: [first.value, second.value].filter(Boolean),
      pageIndex: 0,
      pageSize: Number(size.value),
    };
    loadBrowserPage(group);
  };
  first.onchange = refresh;
  second.onchange = refresh;
  size.onchange = refresh;
  loadBrowserPage(group);
}

