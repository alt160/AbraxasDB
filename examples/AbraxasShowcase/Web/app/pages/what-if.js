// Each case has one rail entry and one focused explanation. Add new cases here as the curriculum grows.
const whatIfCases = [
  {
    id: "composite-combined-properties",
    kind: "intro",
    title: "I want to combine two or more properties as a single indexed value",
    summary: "A customer's FirstName and LastName can form one indexed display-name key without adding a DisplayName property to the object.",
    why: "This is one named composite index, not an ordinary index on FirstName plus another on LastName. LibraDex stores an ordered key with both values as distinct parts. That gives Abraxas a combined access path while preserving each part's own comparison: for example, a prefix on FirstName and an exact match on LastName. No concatenated string or delimiter is needed.",
    next: "Declare the two parts once, then write conditions against the original properties. A typed reader can walk that persisted index and return both key values with RecordIDs without loading customer objects. It can also lend UTF-8 spans when you do not need managed strings.",
    related: "index-composite-key",
    code: `// customers is the RecordStore<ShowcaseCustomer> prepared by this group.
var selector = new Selector<ShowcaseCustomer>();
customers.CreateCompositeIndex("display-name",
    selector.Prop(customer => customer.FirstName).ForIndexing(),
    selector.Prop(customer => customer.LastName).ForIndexing());

// One composite key can serve different comparisons on its two parts.
var match = customers.Where.Prop(customer => customer.FirstName)
    .StartsWith("Ad")
    .AND.Prop(customer => customer.LastName).EqualTo("Lovelace").EndCondition;
ulong[] ids = customers.IterateRecordIDs(match).ToArray();

// Read the typed key parts directly from the persisted index.
using var names = customers.Readers.OpenCompositeValues<string, string>("display-name");
while (names.Read())
    Console.WriteLine($"{names.ID}: {names.Value1} {names.Value2}");
// For byte-oriented work, GetUtf8Span(0/1) avoids decoding managed strings.`,
  },
  {
    id: "index-direction-conflict",
    title: "You change an index's sort direction",
    summary: "Changing Ascending to Descending does not silently rebuild an existing index.",
    expected: "IndexContractException · Configuration",
    why: "If you know schema migrations, think of sort direction as part of the saved index definition. Calling CreateIndex again with the same definition is safe; changing its direction is a different definition and raises a configuration error.",
    next: "Choose the direction as part of setup. If requirements change later, explicitly remove the old index and create the new definition, accounting for the rebuild cost.",
    related: "create-record-indexes",
    code: `// Assume customers is an open RecordStore<ShowcaseCustomer>.
customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);

try
{
    customers.CreateIndex(customer => customer.Code, IndexDirection.Descending);
}
catch (IndexContractException error) when (error.FailureKind == IndexContractFailureKind.Configuration)
{
    Console.WriteLine(error.Message); // Expected definition conflict.
}`,
  },
  {
    id: "composite-name-conflict",
    title: "You change the parts of a named composite index",
    summary: "A display-name index can combine a customer's FirstName and LastName properties. Changing their order changes the saved index definition.",
    expected: "IndexContractException · Configuration",
    why: "A composite index keeps two or more selected values as separate, ordered parts. Here the parts are FirstName and LastName from a customer object. The name 'display-name' identifies that complete definition; LastName then FirstName is a different order, even though it uses the same properties. Conditions still address the properties separately—there is no combined property to maintain on the object.",
    next: "Keep both orders under different index names if you need both. Otherwise, explicitly replace the saved definition and allow for its rebuild. Open the related scenario to see how the index can also supply both name values for a display name without reading each customer object.",
    related: "index-composite-key",
    code: `// ShowcaseCustomer has separate FirstName and LastName properties.
// Assume customers is an open RecordStore<ShowcaseCustomer>.
var selector = new Selector<ShowcaseCustomer>();
customers.CreateCompositeIndex("display-name",
    selector.Prop(customer => customer.FirstName).ForIndexing(),
    selector.Prop(customer => customer.LastName).ForIndexing());

try
{
    customers.CreateCompositeIndex("display-name",
        selector.Prop(customer => customer.LastName).ForIndexing(),
        selector.Prop(customer => customer.FirstName).ForIndexing());
}
catch (IndexContractException error) when (error.FailureKind == IndexContractFailureKind.Configuration)
{
    Console.WriteLine(error.Message); // The saved name identifies a different part order.
}`,
  },
  {
    id: "ordinary-physical-name",
    title: "You give an ordinary index a custom name",
    summary: "An ordinary property index is identified by its path, not by a custom alias.",
    expected: "NotSupportedException",
    why: "If you are used to naming every SQL index, this API works differently: an ordinary text index is selected by its property path. The indexName parameter is not a general-purpose alias. Composite and partial indexes offer developer-facing logical names.",
    next: "For an ordinary property index, use its property path with GetIndex. For a separately named logical access path, see the composite-index example.",
    related: "index-composite-key",
    code: `// Assume customers is an open RecordStore<ShowcaseCustomer>.
try
{
    customers.CreateIndex(customer => customer.Name,
        TextIndexProfile.Exact, IndexDirection.Ascending,
        indexName: "preferred-name");
}
catch (NotSupportedException error)
{
    Console.WriteLine(error.Message); // A physical name is not a logical alias.
}

// The ordinary path is selected by its member path instead.
customers.CreateIndex(customer => customer.Name,
    TextIndexProfile.Exact, IndexDirection.Ascending);
RecordIndex nameIndex = customers.GetIndex(".Name");`,
  },
  {
    id: "missing-index-name",
    title: "You request an index that does not exist",
    summary: "GetIndex reports that the requested name or path has no index.",
    expected: "KeyNotFoundException",
    why: "GetIndex looks up an index that setup already created. It can resolve a supported name or property path, but it does not create a missing index as a side effect.",
    next: "Declare the index during setup, then use its logical name or property path. Use TryGetIndex when absence is an expected branch.",
    related: "index-composite-key",
    code: `// Assume customers is an open RecordStore<ShowcaseCustomer>.
if (customers.TryGetIndex("region-code", out RecordIndex index))
{
    Console.WriteLine(index.Name);
}

try
{
    customers.GetIndex("not-created");
}
catch (KeyNotFoundException error)
{
    Console.WriteLine(error.Message); // No index has that identifier.
}`,
  },
  {
    id: "suffix-without-reversed-keys",
    kind: "performance",
    title: "You search a suffix without reversed keys",
    summary: "EndsWith still works, but without reversed keys it may have to check many indexed names.",
    expected: "IndexDirect · exact index-key scan",
    why: "Think of searching names that end in a word: an ordinary forward text key does not put the same endings together. Abraxas can check keys in the exact text index without rebuilding every record, but may check many of them. A reversed-key copy groups those endings so the lookup can be narrower.",
    next: "If suffix searches are frequent and selective, create a bidirectional text index during setup. Weigh its extra maintained projection against write and storage cost.",
    diagnostic: "ConditionExecutionPlanned reports IndexDirect for this indexed condition. That broad plan kind does not distinguish the exact-key scan from a reversed-key range; the index definition and text route explain the difference.",
    related: "index-text-projections",
    code: `// Assume customers is a fresh RecordStore<ShowcaseCustomer>.
// Exact + Ascending has no reversed-key projection.
customers.CreateIndex(customer => customer.Name,
    TextIndexProfile.Exact, IndexDirection.Ascending);

var suffix = customers.Where.Prop(customer => customer.Name)
    .EndsWith("EXAMPLE").EndCondition;
ConditionExecutionPlanEventArgs? plan = null;
EventHandler<ConditionExecutionPlanEventArgs> capture = (_, args) =>
{
    if (ReferenceEquals(args.Condition, suffix)) plan = args;
};
customers.ConditionExecutionPlanned += capture;
try
{
    ulong[] ids = customers.IterateRecordIDs(suffix).ToArray();
    Console.WriteLine($"{ids.Length} matches · {plan?.PlanKind}");
}
finally
{
    customers.ConditionExecutionPlanned -= capture;
}`,
  },
  {
    id: "condition-without-index",
    kind: "performance",
    title: "You filter on a path with no usable index",
    summary: "The filter still works, but Abraxas may need to check every stored record.",
    expected: "RecordScan",
    why: "As with a filter that cannot use an index in other systems, Abraxas must check stored values record by record. That can be reasonable for a small store or an occasional query; repeated selective queries may justify an index.",
    next: "If this becomes a frequent selective filter, add an appropriate index in the group's setup. Do not index every property by default: each maintained index also costs writes and storage.",
    diagnostic: "ConditionExecutionPlanned reports RecordScan and explains that no complete index tree or safe candidate was available.",
    related: "filter-with-conditions",
    code: `// Assume customers is an open store with no Region index.
var west = customers.Where.Prop(customer => customer.Region)
    .EqualTo("West").EndCondition;
ConditionExecutionPlanEventArgs? plan = null;
EventHandler<ConditionExecutionPlanEventArgs> capture = (_, args) =>
{
    if (ReferenceEquals(args.Condition, west)) plan = args;
};
customers.ConditionExecutionPlanned += capture;
try
{
    ulong[] ids = customers.IterateRecordIDs(west).ToArray();
    Console.WriteLine($"{ids.Length} matches · {plan?.PlanKind}");
    Console.WriteLine(plan?.Reason);
}
finally
{
    customers.ConditionExecutionPlanned -= capture;
}`,
  },
  {
    id: "indexed-and-residual",
    kind: "performance",
    title: "Only part of an AND condition is indexed",
    summary: "One indexed comparison narrows the IDs; Abraxas checks the other comparison afterward.",
    expected: "IndexCandidateWithResidual",
    why: "For Code = C-100 AND Region = West, the Code index finds possible RecordIDs first. Abraxas then reads those records to check Region. A selective Code value can avoid checking the entire store, but the extra test still costs work.",
    next: "Check candidate selectivity before adding more indexes. A composite index may be worthwhile for a repeated two-part query, while a rare query may be better served by the one existing index.",
    diagnostic: "ConditionExecutionPlanned reports IndexCandidateWithResidual. Its leaf details show that Code supplied indexed candidates and Region was checked afterward.",
    related: "filter-with-conditions",
    code: `// Assume customers is a fresh store: Code is indexed; Region is not.
customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);

var match = customers.Where.Prop(customer => customer.Code)
    .EqualTo("C-100")
    .AND.Prop(customer => customer.Region).EqualTo("West").EndCondition;
ConditionExecutionPlanEventArgs? plan = null;
EventHandler<ConditionExecutionPlanEventArgs> capture = (_, args) =>
{
    if (ReferenceEquals(args.Condition, match)) plan = args;
};
customers.ConditionExecutionPlanned += capture;
try
{
    ulong[] ids = customers.IterateRecordIDs(match).ToArray();
    Console.WriteLine($"{ids.Length} matches · {plan?.PlanKind}");
    if (plan is not null)
        foreach (var leaf in plan.Leaves)
            Console.WriteLine($"{leaf.IndexName ?? "(no index)"} · used: {leaf.UsedByIndex} · {leaf.Reason}");
}
finally
{
    customers.ConditionExecutionPlanned -= capture;
}`,
  },
];

function renderWhatIfCase() {
  const item = whatIfCases.find((entry) => entry.id === selectedWhatIfCase);
  if (!item) return;
  pageTitle.textContent = item.title;
  const related = state.scenarios.find((scenario) => scenario.id === item.related);
  const isIntro = item.kind === "intro";
  const category = item.kind === "performance" ? "PERFORMANCE" : "CONTRACT";
  const expected = isIntro ? "" : `<span class="what-if-expected ${item.kind === "performance" ? "performance" : ""}">${item.kind === "performance" ? "Expected route" : "Expected error"}: ${escape(item.expected)}</span>`;
  const sourceIntro = isIntro
    ? "One composite index combines the parts for selection and can return both indexed values. The related scenario runs this against a prepared RecordBase."
    : item.kind === "performance"
      ? "The optional listener prints the selected route; it is not required for the query and does not time it. Use the related scenario to explore the feature."
      : "This snippet deliberately exercises the boundary. It is separate from the successful, paste-ready scenario code.";
  content.innerHTML = `<div class="what-if-page"><section class="card what-if-intro"><p class="eyebrow">${isIntro ? "WHAT HAPPENS IF…" : `WHAT HAPPENS IF… · ${category}`}</p><h2>${escape(item.title)}?</h2><p>${escape(item.summary)}</p>${expected}</section><div class="what-if-explanation"><article class="card"><p class="eyebrow">${isIntro ? "WHAT THIS CREATES" : "WHY"}</p><p>${escape(item.why)}</p></article><article class="card"><p class="eyebrow">${isIntro ? "WHAT THIS ENABLES" : "WHAT TO DO"}</p><p>${escape(item.next)}</p><button class="secondary what-if-show-how" type="button">Show how</button></article></div>${item.diagnostic ? `<section class="card what-if-diagnostic"><p class="eyebrow">PLANNER EVIDENCE TO INSPECT</p><p>${escape(item.diagnostic)}</p></section>` : ""}${item.kind === "performance" ? `<section class="card what-if-diagnostic"><p class="eyebrow">OPTIONAL DIAGNOSTIC · USE SPARINGLY</p><p>Abraxas chooses a route without this event. A listener adds one synchronous callback and diagnostic details per enumerated query, not per returned record. Use it for tests, troubleshooting, or sampled production checks; keep handlers fast and nonthrowing. The event is store-wide, so distinguish your condition when other queries may run. PlanKind names the route, not its cost.</p></section>` : ""}<section class="card what-if-source"><div class="what-if-source-head"><div><p class="eyebrow">${isIntro ? "WORKING EXAMPLE" : "INTENTIONAL CONTRAST"}</p><h3>Code to inspect</h3></div></div><p>${sourceIntro}</p><div id="what-if-editor" class="code-editor"></div></section>${related ? `<button class="secondary" id="what-if-related" type="button">Open related scenario: ${escape(related.title)}</button>` : ""}</div>`;
  mountCodeEditor(content.querySelector("#what-if-editor"), item.code, null, {
    minHeight: 160,
    maxHeight: 520,
  });
  const relatedButton = content.querySelector("#what-if-related");
  if (relatedButton) relatedButton.onclick = () => selectScenario(item.related);
  content.querySelector(".what-if-show-how").onclick = (event) =>
    openWhatIfRemedy(item, event.currentTarget);
}

function renderWhatIfNavigation() {
  const section = document.querySelector("#what-if-navigation");
  const toggle = document.querySelector("#what-if-toggle");
  const children = document.querySelector("#what-if-children");
  const links = document.querySelector("#what-if-links");
  const active = currentView === "what-if";
  section.classList.toggle("active", active);
  section.classList.toggle("expanded", whatIfExpanded);
  toggle.setAttribute("aria-expanded", String(whatIfExpanded));
  children.classList.toggle("collapsed", !whatIfExpanded);
  children.inert = !whatIfExpanded;
  children.setAttribute("aria-hidden", String(!whatIfExpanded));
  links.innerHTML = whatIfCases.map((item, index) => `${item.kind !== "intro" && (index === 0 || whatIfCases[index - 1]?.kind !== item.kind) ? `<div class="workspace-case-heading">${item.kind === "performance" ? "PERFORMANCE" : "CONTRACTS"}</div>` : ""}<button class="workspace-case-link ${active && selectedWhatIfCase === item.id ? "active" : ""}" type="button" data-what-if="${escape(item.id)}">${escape(item.title)}</button>`).join("");
  links.querySelectorAll("[data-what-if]").forEach((button) => {
    button.onclick = () => navigateTo({ view: "what-if", caseId: button.dataset.whatIf }, "left");
  });
}

function toggleWhatIfNavigation() {
  whatIfExpanded = !whatIfExpanded;
  renderWhatIfNavigation();
}
