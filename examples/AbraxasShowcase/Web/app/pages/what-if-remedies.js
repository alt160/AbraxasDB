// Successful follow-ups for the intentional contrasts in what-if.js.
// Each code sample assumes the named RecordStore remains open for the group.
const whatIfRemedies = {
  "index-direction-conflict": {
    explanation: "Treat sort direction like a schema choice. Set it when you create the index; to change an existing one, remove it and build the new order when the extra work is acceptable.",
    examples: [
      { label: "Fresh setup", code: `// customers is the open group RecordStore<ShowcaseCustomer>.
customers.CreateIndex(customer => customer.Code, IndexDirection.Descending);` },
      { label: "Change existing", code: `// Remove the old physical ordering before declaring the new one.
// Schedule this rebuild when the store can tolerate index maintenance.
customers.RemoveIndex(".Code");
customers.CreateIndex(customer => customer.Code, IndexDirection.Descending);` },
    ],
  },
  "composite-name-conflict": {
    explanation: "FirstName then LastName and LastName then FirstName are two distinct index orders. Give each order its own name if both are useful; otherwise remove the old definition before building its replacement. Neither requires a combined property on the customer object.",
    examples: [
      { label: "Keep both", code: `var selector = new Selector<ShowcaseCustomer>();
customers.CreateCompositeIndex("first-last",
    selector.Prop(customer => customer.FirstName).ForIndexing(),
    selector.Prop(customer => customer.LastName).ForIndexing());
customers.CreateCompositeIndex("last-first",
    selector.Prop(customer => customer.LastName).ForIndexing(),
    selector.Prop(customer => customer.FirstName).ForIndexing());` },
      { label: "Replace existing", code: `// Replacing a durable access path incurs index maintenance.
customers.RemoveIndex("display-name");
var selector = new Selector<ShowcaseCustomer>();
customers.CreateCompositeIndex("display-name",
    selector.Prop(customer => customer.LastName).ForIndexing(),
    selector.Prop(customer => customer.FirstName).ForIndexing());` },
    ],
  },
  "ordinary-physical-name": {
    explanation: "Find an ordinary property index by its member path, such as .Name. If your access path combines two values and needs its own logical name, create a named composite index instead.",
    examples: [
      { label: "Property path", code: `customers.CreateIndex(customer => customer.Name,
    TextIndexProfile.Exact, IndexDirection.Ascending);
RecordIndex nameIndex = customers.GetIndex(".Name");` },
      { label: "Two-part logical name", code: `var selector = new Selector<ShowcaseCustomer>();
customers.CreateCompositeIndex("region-name",
    selector.Prop(customer => customer.Region).ForIndexing(),
    selector.Prop(customer => customer.Name).ForIndexing());
RecordIndex regionNameIndex = customers.GetIndex("region-name");` },
    ],
  },
  "missing-index-name": {
    explanation: "If your code requires this index, create it during setup before calling GetIndex. If either setup is valid, use TryGetIndex and handle the missing case.",
    examples: [
      { label: "Declare in setup", code: `customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);
RecordIndex codeIndex = customers.GetIndex(".Code");` },
      { label: "Handle optional", code: `if (customers.TryGetIndex(".Code", out RecordIndex codeIndex))
{
    Console.WriteLine(codeIndex.Name);
}
else
{
    Console.WriteLine("Code index is not present in this setup.");
}` },
    ],
  },
  "suffix-without-reversed-keys": {
    explanation: "Search endings often? Bidirectional text indexing stores a reversed version of the key so matching endings can be found more narrowly. That extra key also costs storage and write work.",
    examples: [
      { label: "Fresh setup", code: `customers.CreateIndex(customer => customer.Name,
    TextIndexProfile.Exact, IndexDirection.Bidirectional);
var suffix = customers.Where.Prop(customer => customer.Name)
    .EndsWith("EXAMPLE").EndCondition;
ulong[] ids = customers.IterateRecordIDs(suffix).ToArray();` },
      { label: "Change existing", code: `// Explicitly replace the Exact + Ascending definition.
customers.RemoveIndex(".Name");
customers.CreateIndex(customer => customer.Name,
    TextIndexProfile.Exact, IndexDirection.Bidirectional);` },
    ],
  },
  "condition-without-index": {
    explanation: "A scan is reasonable for a small store or a rare query. For a frequent, selective Region filter, add the index in group setup and then run the same condition.",
    examples: [
      { label: "Add index", code: `customers.CreateIndex(customer => customer.Region, IndexDirection.Ascending);
var west = customers.Where.Prop(customer => customer.Region)
    .EqualTo("West").EndCondition;
ulong[] ids = customers.IterateRecordIDs(west).ToArray();` },
    ],
  },
  "indexed-and-residual": {
    explanation: "If Code usually finds only a few records, checking Region on those records may be enough. For a frequent exact two-part lookup, consider a composite key index—but verify that the actual query uses it.",
    examples: [
      { label: "Keep selective Code", code: `// Code remains the candidate index; Region is checked on candidates.
var match = customers.Where.Prop(customer => customer.Code)
    .EqualTo("C-100")
    .AND.Prop(customer => customer.Region).EqualTo("West").EndCondition;
ulong[] ids = customers.IterateRecordIDs(match).ToArray();` },
      { label: "Repeated pair", code: `// Add this in setup only when repeated queries justify its upkeep.
var selector = new Selector<ShowcaseCustomer>();
customers.CreateCompositeIndex("code-region",
    selector.Prop(customer => customer.Code).ForIndexing(),
    selector.Prop(customer => customer.Region).ForIndexing());` },
    ],
  },
  "composite-combined-properties": {
    explanation: "This creates one named composite index, not two independent property indexes. Its ordered key preserves FirstName and LastName as individually addressable parts: mix a prefix comparison on one with an exact comparison on the other, then walk the persisted key values through the typed index-only reader.",
    examples: [
      { label: "Create and find", code: `var selector = new Selector<ShowcaseCustomer>();
customers.CreateCompositeIndex("display-name",
    selector.Prop(customer => customer.FirstName).ForIndexing(),
    selector.Prop(customer => customer.LastName).ForIndexing());
var match = customers.Where.Prop(customer => customer.FirstName)
    .StartsWith("Ad")
    .AND.Prop(customer => customer.LastName).EqualTo("Lovelace").EndCondition;
ulong[] ids = customers.IterateRecordIDs(match).ToArray();` },
      { label: "Read display names", code: `// The display-name composite index was created during setup.
using var names = customers.Readers.OpenCompositeValues<string, string>("display-name");
while (names.Read())
    Console.WriteLine($"{names.ID}: {names.Value1} {names.Value2}");
// GetUtf8Span(0/1) borrows text bytes when no managed strings are needed.` },
    ],
  },
};
