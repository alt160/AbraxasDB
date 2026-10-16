# Queries and indexes

[Guide home](Home.md)

The examples below continue from [Getting started](Getting-started.md), using the same `customers` store.

## Find records, or just read their values

Build a condition using ordinary member selectors. The same completed condition can be reused for counting and reading:

```csharp
var adultsNamedA = customers.Where
    .Prop(customer => customer.Name).StartsWith("A")
    .AND.Prop(customer => customer.Age).GreaterOrEqual(18)
    .EndCondition;

Console.WriteLine($"Matches: {customers.CountWhere(adultsNamedA)}");
foreach (Customer match in customers.IterateRecords(adultsNamedA))
    Console.WriteLine(match.Name);
```

Property-path strings provide the same condition when paths come from application configuration or a user-selected field. Select the value type explicitly because a string does not carry the lambda's CLR type information:

```csharp
var adultsNamedAByPath = customers.Where
    .PropPath(".Name").AsString.StartsWith("A")
    .AND.PropPath(".Age").AsInt32.GreaterOrEqual(18)
    .EndCondition;

Console.WriteLine($"Matches: {customers.CountWhere(adultsNamedAByPath)}");
```

No secondary index is required for this condition to work. Without a usable index, Abraxas evaluates the condition against stored record data. Add indexes when the workload benefits from avoiding that scan:

```csharp
customers.CreateIndex(customer => customer.Name, IndexDirection.Ascending);
customers.CreateIndex(customer => customer.Age, IndexDirection.Ascending);
```
`CreateIndex` handles string keys as well as supported scalar keys. Strings use exact keys by default; the text-profile overload maintains additional folded, normalized, or culture sort-key representations when requested. For example, `customers.CreateIndex(customer => customer.Name, TextIndexProfile.ExactAndFolded, IndexDirection.Ascending)` maintains folded keys too. Choose the needed profile during setup: a later declaration with a different profile does not silently convert an existing index. The default string declaration and an explicit `TextIndexProfile.Exact` declaration are equivalent. `CreateMemoryIndex` offers the same overloads for volatile indexes. Either family also accepts a string path such as `".Name"` or a `PropPath` instead of a lambda.

Repeating either ordinary index declaration with the same definition reuses the existing index without duplicating it or repopulating its entries, including after reopening the store. The call still validates the definition and refreshes registration; it is not a cost-free per-record operation. A conflicting definition—for example, a different uniqueness contract, direction, or text profile—is rejected rather than silently replacing the index. Declare indexes during application setup, not inside the record-processing loop.

The condition does not need to be rebuilt after adding indexes. Abraxas plans each execution against the available indexes. Use `IterateRecordIDs(adultsNamedA)` when only the matching identities are needed.

If the application only needs a name and an age, do not reconstruct a `Customer` for each result:

```csharp
using (var values = customers.OpenValuesReader(
    adultsNamedA, ".Name", ".Age"))
{
    while (values.Read())
    {
        string? name = values.Get(".Name").AsString;
        int? age = values.Get(".Age").AsInt32;
        Console.WriteLine($"{name}: {age}");
    }
}
```

Property-path reads access Inheto's stored object shape. Where a selected value is covered by the current index key, supported reader routes can read that key without opening the record payload. Uncovered values retain a payload-reading path; creating an index does not make every projection an index-only operation.

## Reuse a condition with different values

Parameters keep a condition reusable without hiding its values in application delegates:

```csharp
ConditionParameter<int> minimumAge = customers.Parameter(18, "minimumAge");
var byMinimumAge = customers.Where
    .Prop(customer => customer.Age).GreaterOrEqual(minimumAge)
    .EndCondition;

minimumAge.Value = 21; // The next execution uses 21; no condition rebuild.
foreach (ulong matchID in customers.IterateRecordIDs(byMinimumAge))
    Console.WriteLine(customers.Get(matchID).Name);

byMinimumAge.Parameters.Reset(); // Restores the original value, 18.
```

`customers.Parameters.Create(...)` and a condition draft's `Parameters.Create(...)` create the same Abraxas-owned parameter type. A completed condition exposes the exact parameters it references, including named lookup; creating an unused parameter does not add it to the condition.

Repeated uses of a parameter share one execution snapshot. Membership parameters created with `customers.Parameter<int>(values)` freeze the enumerable's contents during execution preparation. Reset restores the original enumerable reference, not a saved copy of its contents. Coordinate parameter or collection mutations that could overlap preparation; reusable parameters do not add locking to each value access.

Descriptor rehydration with `NextDeferred(...)` or `SetDeferred(...)` also keeps scalar and membership bindings live for later executions. Building the condition does not invoke those factories or enumerate a deferred set. Use an explicit membership parameter when several leaves should share one frozen set; an ordinary factory runs during each leaf's preparation, never once per record. Direct collection bindings supplied with `Next(...)` or `Set(...)` are copied when the condition is built.
