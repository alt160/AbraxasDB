using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Registers the remaining group-owned examples without coupling their execution to navigation order.<br/></summary>
internal static class AdditionalScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("filter-with-conditions", "Retrieval and readers", "Filter with composed conditions", "Select West-region customers by two matching scalar paths.", "Depends on selection", false, 320, "Retrieval/FilterWithConditions.cs.txt", FilterWithConditions),
        New("create-string-index", "Conditions and indexes", "Use a string index", "Look up a customer by its indexed code.", "Indexed lookup", false, 410, "Conditions/UseStringIndex.cs.txt", UseStringIndex),
        New("explicit-sorted-read", "Conditions and indexes", "Request an explicit result order", "Read customers by name in descending order.", "Includes ordering cost", false, 420, "Conditions/ExplicitSortedRead.cs.txt", ExplicitSortedRead),
        New("reproject-object-shape", "Projection and value reads", "Compare two reprojections", "Read one stored customer into two different .NET shapes and inspect what each receives.", "Reads selected shapes", false, 510, "Projection/ReprojectObject.cs.txt", ReprojectObject),
        New("alias-renamed-member", "Projection and value reads", "Bridge a renamed property path", "Temporarily map a new .NET member name to an older stored path without rewriting the record.", "Narrow projection", false, 515, "Projection/PathAlias.cs.txt", AliasRenamedMember),
        New("read-values-only", "Projection and value reads", "Read stored paths without hydrating", "Choose PropPath strings at runtime and read string, collection, decimal, and date values without filling an object.", "Narrow value read", false, 520, "Projection/ReadValuesOnly.cs.txt", ReadValuesOnly),
        New("read-path-continuations", "Projection and value reads", "Continue a PropPath through methods and properties", "Read a stored Code, its Length property, and its parameterless Trim() result without hydrating the customer.", "Narrow value read", false, 521, "Projection/PathContinuations.cs.txt", ReadPathContinuations),
        New("navigate-related-records", "Relationships and aggregates", "Navigate parent and child records", "Select orders by their customer's RecordID.", "Scales with related records", false, 610, "Relationships/NavigateRelatedRecords.cs.txt", NavigateRelatedRecords),
        New("group-with-aggregate", "Relationships and aggregates", "Group and count orders", "Group orders by status and read the count for each group.", "Aggregate query", false, 620, "Relationships/GroupAndCount.cs.txt", GroupAndCount),
        New("update-existing-record", "Mutation and lifecycle", "Update an existing record", "Change a customer while retaining its RecordID.", "Updates indexes", true, 710, "Mutation/UpdateRecord.cs.txt", UpdateRecord),
        New("delete-and-verify", "Mutation and lifecycle", "Delete a record and verify absence", "Delete a customer and check that its RecordID no longer resolves.", "Updates indexes", true, 720, "Mutation/DeleteRecord.cs.txt", DeleteRecord),
        New("bulk-ingest", "Scale and performance", "Bulk-ingest deterministic records", "Insert a deterministic batch with PutManyFast and report this run's elapsed time.", "Size-dependent", true, 810, "Scale/BulkIngest.cs.txt", BulkIngest),
        New("observe-indexing-completion", "Scale and performance", "Observe batch indexing completion", "Insert a batch, inspect its indexing handle, and verify indexed selection after completion.", "Size-dependent", true, 815, "Scale/IndexingCompletion.cs.txt", ObserveIndexingCompletion),
        New("unordered-throughput-read", "Scale and performance", "Traverse complete objects for throughput", "Read all customers without asking for an order.", "Full traversal", false, 820, "Scale/UnorderedRead.cs.txt", UnorderedRead)
    ];

    /// <summary>Constructs one independently runnable catalog scenario from its category, source fragment, and native action.<br/></summary>
    /// <param name="id">Stable scenario identity used by catalog navigation and export.<br/></param>
    /// <param name="category">Owning group title.<br/></param>
    /// <param name="title">Developer-facing scenario title.<br/></param>
    /// <param name="description">Short explanation shown in the catalog and banner.<br/></param>
    /// <param name="cost">Qualitative execution cost.<br/></param>
    /// <param name="mutates">Whether this action writes records.<br/></param>
    /// <param name="order">Learning-path order.<br/></param>
    /// <param name="source">Embedded learner-facing C# fragment.<br/></param>
    /// <param name="run">Native execution using the prepared group RecordBase.<br/></param>
    /// <returns>A complete scenario contract.<br/></returns>
    private static IShowcaseScenario New(string id, string category, string title, string description, string cost, bool mutates, int order, string source, Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> run)
    {
        (string explanation, string[] concepts, string[] apis) detail = id switch
        {
            "filter-with-conditions" => ("Need the same kind of answer as a WHERE clause with two tests? Compare stored members, combine the tests with And, and read matching RecordIDs and objects. The group's setup created the indexes; this read does not.", ["condition composition", "indexed selection"], ["Where.Prop", "And", "IterateRecordIDs"]),
            "create-string-index" => ("Want to find a customer by Code instead of storage ID? The group already indexed Code. This read uses that business value, while the returned RecordID stays separate.", ["durable string index", "business-key lookup"], ["CreateIndex", "GetOrDefault"]),
            "explicit-sorted-read" => ("Need names in descending order? OpenSorted requests that order and reads the first eight objects. Without an explicit order, do not assume the reader returns names—or RecordIDs—in that order.", ["explicit order", "bounded reader"], ["Readers.OpenSorted", "Read", "Get"]),
            "reproject-object-shape" => ("Need a smaller result type, like a DTO? GetAs fills the destination members whose paths match the stored record. Members you leave out are not returned; destination members with no matching stored path keep their defaults.", ["matching member paths", "alternate .NET shapes", "unmatched defaults"], ["GetAs<TOut>"]),
            "alias-renamed-member" => ("Renamed Name to DisplayName in application code? Register a read-side PropPath alias and project old records into the new type. The stored path is not rewritten. Remove the alias when the compatibility window ends; until then, exact requested paths win when both names exist.", ["schema evolution", "temporary read alias", "stored path unchanged"], ["Aliases.Add", "GetAs<TOut>", "Aliases.Remove"]),
            "read-values-only" => ("Need only a few values for a calculation or display? Pass PropPath strings such as .Code and .Tags to GetValues. They can come from application configuration or UI choices; Abraxas reads those stored values by RecordID without constructing the containing customer or order object.", ["PropPath notation", "runtime-selected member paths", "no full object"], ["GetValues", "RecordID"]),
            "read-path-continuations" => ("A PropPath can continue beyond the stored member: .Code.Length reads a property, and .Code.Trim().Length calls a parameterless, non-void method before reading the resulting property. Abraxas gets the stored Code by RecordID, evaluates the continuation for this read, and does not hydrate a ShowcaseCustomer or persist those derived values.", ["property continuation", "parameterless method continuation", "derived value at read time"], ["GetValue<T>", "PropPath", "Trim"]),
            "navigate-related-records" => ("Need a customer's orders? Each order stores the customer's RecordID. Search the separate order store by that value, then inspect the matching order objects; this is not an automatic SQL join.", ["separate stores", "external RecordID relationship"], ["Where.Prop", "IterateRecordIDs", "Get"]),
            "group-with-aggregate" => ("Need counts by order status? Group stored Status values and return one count per status without building each order object.", ["grouped count", "aggregate result"], ["Aggregates.GroupBy", "AddCount", "Aggregates.Open"]),
            "update-existing-record" => ("Put a scenario-owned customer, change its Name, then Put the changed object at the same RecordID and read it back. Group indexes remain in place.", ["same RecordID", "replacement write"], ["Put", "Get"]),
            "delete-and-verify" => ("Put a scenario-owned customer, Delete it by RecordID, then use TryGet to prove it is absent. Baseline sample records remain untouched.", ["delete by identity", "absence check"], ["Delete", "TryGet"]),
            "bulk-ingest" => ("Need to insert many objects? PutManyFast writes a repeatable batch. Try different group sizes and compare this run's measured time instead of assuming a fixed throughput.", ["batch ingestion", "measured host time"], ["PutManyFast"]),
            "observe-indexing-completion" => ("After a batch write, when can Code lookup find the new records? PutManyFast returns an indexing handle; this build finishes indexing before the call returns. The example checks that state and then finds an inserted Code.", ["batch indexing", "completion signal", "indexed selection"], ["PutManyFast", "IndexingBatchHandle.GetSnapshot", "GetOrDefault"]),
            "unordered-throughput-read" => ("Need to visit every customer, with no particular order? OpenUnordered reads complete objects without promising key order or the quickest first result. Compare it with other readers for your workload.", ["complete traversal", "unordered snapshot"], ["Readers.OpenUnordered", "Read", "Get"]),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        return new CatalogActionScenario(id, category, title, description, cost, mutates,
            new ScenarioDocumentation(detail.explanation, detail.concepts, detail.apis, [title, description], ["Prepare " + category], order), source, run);
    }

    /// <summary>Selects only customers satisfying both a region and a tag condition, retaining actual hydrated matches.<br/></summary>
    private static ScenarioResult FilterWithConditions(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var customers = workspace.Customers;
        var named = customers.Where.Prop(customer => customer.Code).EqualTo("CUS-0000001").EndCondition;
        var condition = customers.Where.Prop(customer => customer.Region).EqualTo("West").And(named).EndCondition;
        var items = new List<ScenarioResultItem>();
        foreach (ulong id in customers.IterateRecordIDs(condition))
        {
            token.ThrowIfCancellationRequested();
            if (items.Count == 8) break;
            items.Add(new ScenarioResultItem("Matching customer", id, customers.Get(id), typeof(ShowcaseCustomer)));
        }
        watch.Stop();
        return Result(items, "Read matching customers through a composed condition.", watch);
    }

    /// <summary>Uses the group's already-created durable Code index for one business-key lookup.<br/></summary>
    private static ScenarioResult UseStringIndex(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ShowcaseCustomer? customer = workspace.Customers.GetOrDefault(".Code", "CUS-0000001");
        watch.Stop();
        return new ScenarioResult(customer is not null, "The indexed business key selected the first customer.", [new("Index path", ".Code")], customer, typeof(ShowcaseCustomer), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Requests descending Name order explicitly and retains a bounded sample of actual result objects.<br/></summary>
    private static ScenarioResult ExplicitSortedRead(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var items = new List<ScenarioResultItem>();
        using var reader = workspace.Customers.Readers.OpenSorted(".Name", descending: true);
        while (items.Count < 8 && reader.Read())
        {
            token.ThrowIfCancellationRequested();
            items.Add(new ScenarioResultItem("Sorted customer", reader.ID, reader.Get(), typeof(ShowcaseCustomer)));
        }
        watch.Stop();
        return Result(items, "Read customers in explicitly requested descending Name order.", watch);
    }

    /// <summary>Reads the same stored customer into two destination types and retains all three real objects for comparison.<br/></summary>
    private static ScenarioResult ReprojectObject(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var reader = workspace.Customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong id = reader.ID;
        ShowcaseCustomer original = workspace.Customers.Get(id)!;
        ShowcaseCustomerSummary projected = workspace.Customers.GetAs<ShowcaseCustomerSummary>(id);
        ShowcaseCustomerAlternative alternative = workspace.Customers.GetAs<ShowcaseCustomerAlternative>(id);
        watch.Stop();
        bool passed = projected.Code == original.Code && projected.Region == original.Region
            && alternative.Name == original.Name && alternative.Region == original.Region
            && alternative.Unmatched == "not stored";
        return new ScenarioResult(passed, "Matching paths filled each destination; the unmatched member kept its default.",
            [new("RecordID", id.ToString())],
            [new("Stored customer", id, original, typeof(ShowcaseCustomer)),
             new("Code + Region projection", id, projected, typeof(ShowcaseCustomerSummary)),
             new("Name + Region projection", id, alternative, typeof(ShowcaseCustomerAlternative))],
            watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Reads mixed runtime values from two stores by path without creating destination domain objects.<br/></summary>
    private static ScenarioResult ReadValuesOnly(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var reader = workspace.Customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong id = reader.ID;
        string[] customerPaths = [".Code", ".Tags"];
        List<object?> values = workspace.Customers.GetValues(id, customerPaths) ?? throw new InvalidOperationException("The selected customer is missing.");
        using var orderReader = workspace.Orders.Readers.Open();
        if (!orderReader.Read()) throw new InvalidOperationException("The prepared group has no orders.");
        ulong orderId = orderReader.ID;
        List<object?> orderValues = workspace.Orders.GetValues(orderId, ".Total", ".OrderedAt") ?? throw new InvalidOperationException("The selected order is missing.");
        watch.Stop();
        return new ScenarioResult(true, "Read four member paths without building customer or order objects.", [new("Customer RecordID", id.ToString()), new("Order RecordID", orderId.ToString())],
            [new("Customer .Code", id, values[0], values[0]?.GetType() ?? typeof(object)),
             new("Customer .Tags", id, values[1], values[1]?.GetType() ?? typeof(object)),
             new("Order .Total", orderId, orderValues[0], orderValues[0]?.GetType() ?? typeof(object)),
             new("Order .OrderedAt", orderId, orderValues[1], orderValues[1]?.GetType() ?? typeof(object))], watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Reads a stored scalar and evaluates property and parameterless-method continuations without building its customer object.<br/></summary>
    /// <param name="workspace">Prepared projection-group store containing customer Code values.<br/></param>
    /// <param name="token">Cancellation signal observed before the direct value reads.<br/></param>
    /// <returns>The actual stored and derived values with a consistency proof.<br/></returns>
    private static ScenarioResult ReadPathContinuations(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var customers = workspace.Customers;
        using var reader = customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong id = reader.ID;
        string code = customers.GetValue<string>(id, ".Code");
        int length = customers.GetValue<int>(id, ".Code.Length");
        int trimmedLength = customers.GetValue<int>(id, ".Code.Trim().Length");
        watch.Stop();
        bool passed = length == code.Length && trimmedLength == code.Trim().Length;
        return new ScenarioResult(passed, passed ? "PropPath continuations evaluated from the stored Code without hydrating its customer." : "The direct and continued PropPath values disagreed.",
            [new("RecordID", id.ToString()), new("Stored path", ".Code"), new("Property path", ".Code.Length"), new("Method and property path", ".Code.Trim().Length")],
            [new ScenarioResultItem("Stored Code", id, code, typeof(string)), new ScenarioResultItem("Code length", id, length, typeof(int)), new ScenarioResultItem("Trimmed length", id, trimmedLength, typeof(int))],
            watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Uses a customer's external RecordID to select normalized child orders.<br/></summary>
    private static ScenarioResult NavigateRelatedRecords(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        using var customerReader = workspace.Customers.Readers.Open();
        if (!customerReader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong customerID = customerReader.ID;
        var condition = workspace.Orders.Where.Prop(order => order.CustomerID).EqualTo(customerID).EndCondition;
        var items = new List<ScenarioResultItem>();
        foreach (ulong id in workspace.Orders.IterateRecordIDs(condition))
        {
            token.ThrowIfCancellationRequested();
            if (items.Count == 8) break;
            items.Add(new("Related order", id, workspace.Orders.Get(id), typeof(ShowcaseOrder)));
        }
        watch.Stop();
        return Result(items, $"Read orders whose CustomerID matches {customerID}.", watch);
    }

    /// <summary>Groups normalized orders by status and reads the actual count for each returned group.<br/></summary>
    private static ScenarioResult GroupAndCount(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var definition = workspace.Orders.Aggregates.GroupBy(order => order.Status).AddKey().AddCount();
        var items = new List<ScenarioResultItem>();
        using var reader = workspace.Orders.Aggregates.Open(definition);
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            var row = reader.Get();
            items.Add(new(row.Value1, null, row.Value2, typeof(ulong)));
        }
        watch.Stop();
        return Result(items, "Grouped orders by status and counted each group.", watch);
    }

    /// <summary>Inserts one scenario-owned customer, then replaces it under the same RecordID and reads it back.<br/></summary>
    private static ScenarioResult UpdateRecord(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong id = workspace.Customers.Put(new ShowcaseCustomer { Code = "UPDATE-" + Guid.NewGuid().ToString("N"), Name = "Before update", Region = "West" });
        ShowcaseCustomer updated = workspace.Customers.Get(id)!;
        updated.Name = "After update";
        workspace.Customers.Put(id, updated);
        ShowcaseCustomer readBack = workspace.Customers.Get(id)!;
        watch.Stop();
        return new ScenarioResult(readBack.Name == "After update", "The same RecordID now returns the updated object.", [new("RecordID", id.ToString())], readBack, typeof(ShowcaseCustomer), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Inserts a scenario-owned record, deletes it, and proves the same ID is absent without touching baseline data.<br/></summary>
    private static ScenarioResult DeleteRecord(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong id = workspace.Customers.Put(new ShowcaseCustomer { Code = "DELETE-" + Guid.NewGuid().ToString("N"), Name = "Delete example", Region = "West" });
        bool deleted = workspace.Customers.Delete(id);
        bool found = workspace.Customers.TryGet(id, out ShowcaseCustomer? _);
        watch.Stop();
        return new ScenarioResult(deleted && !found, "The deleted RecordID no longer resolves.", [new("RecordID", id.ToString())], !found, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Inserts a deterministic sized batch through the public fast-ingestion API and reports measured host time.<br/></summary>
    private static ScenarioResult BulkIngest(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count = Math.Clamp((int)(workspace.Customers.Aggregates.Count() / 10), 10, 1000);
        string prefix = "BULK-" + workspace.Customers.Aggregates.Count().ToString("D10") + "-";
        ShowcaseCustomer[] batch = Enumerable.Range(0, count).Select(index => new ShowcaseCustomer
        {
            Code = prefix + index.ToString("D5"), Name = "Batch customer " + index.ToString("D5"), Region = "West"
        }).ToArray();
        var watch = Stopwatch.StartNew();
        var outcome = workspace.Customers.PutManyFast(batch);
        using (outcome.IndexingBatch) { }
        watch.Stop();
        return new ScenarioResult(outcome.RecordIds.Count == count, $"PutManyFast inserted {outcome.RecordIds.Count} customers.", [new("Batch size", count.ToString())], (ulong)outcome.RecordIds.Count, typeof(ulong), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Writes a scenario-owned batch and proves its indexed key is selectable after the returned indexing handle reports completion.<br/></summary>
    private static ScenarioResult ObserveIndexingCompletion(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string code = "INDEX-BATCH-" + Guid.NewGuid().ToString("N");
        ShowcaseCustomer[] batch = [new() { Code = code, Name = "Index completion sample", Region = "West" }];
        var watch = Stopwatch.StartNew();
        var outcome = workspace.Customers.PutManyFast(batch);
        using var handle = outcome.IndexingBatch;
        IndexingBatchSnapshot snapshot = handle.GetSnapshot();
        ShowcaseCustomer? byId = workspace.Customers.Get(outcome.RecordIds[0]);
        ShowcaseCustomer? byIndex = workspace.Customers.GetOrDefault(".Code", code);
        watch.Stop();
        bool passed = snapshot.PendingCount == 0 && snapshot.DeadLetterCount == 0
            && handle.IsCompleted && byId?.Code == code && byIndex?.Code == code;
        return new ScenarioResult(passed, "The batch returned with indexing complete; both RecordID and indexed Code selected its record.",
            [new("Scheduled", snapshot.ScheduledCount.ToString()), new("Processed", snapshot.ProcessedCount.ToString()),
             new("Pending", snapshot.PendingCount.ToString()), new("Indexing completed on return", handle.IsCompleted.ToString())],
            byIndex, typeof(ShowcaseCustomer), watch.Elapsed.TotalMilliseconds, outcome.RecordIds[0]);
    }

    /// <summary>Traverses every customer through an unordered reader while retaining only count and a small sample.<br/></summary>
    private static ScenarioResult UnorderedRead(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        ulong count = 0;
        var items = new List<ScenarioResultItem>();
        using var reader = workspace.Customers.Readers.OpenUnordered();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            count++;
            if (items.Count < 4) items.Add(new("Sample customer", reader.ID, reader.Get(), typeof(ShowcaseCustomer)));
        }
        watch.Stop();
        return Result(items, $"Traversed {count} customers without requesting an order.", watch);
    }

    /// <summary>Converts an actual result sample into the standard multi-result proof without fabricating records.<br/></summary>
    private static ScenarioResult Result(IReadOnlyList<ScenarioResultItem> items, string summary, Stopwatch watch)
        => new(items.Count > 0, summary, [new("Returned results", items.Count.ToString())], items, watch.Elapsed.TotalMilliseconds);

    /// <summary>Demonstrates a temporary read-side PropPath alias for an evolved .NET shape without modifying stored data.<br/></summary>
    /// <param name="workspace">Prepared projection-group store containing the original Name member.<br/></param>
    /// <param name="token">Cancellation signal checked before the read sequence.<br/></param>
    /// <returns>Proof that the renamed member can be filled while the persisted original path remains readable.<br/></returns>
    private static ScenarioResult AliasRenamedMember(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var customers = workspace.Customers;
        using var reader = customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong id = reader.ID;
        ShowcaseCustomer original = customers.Get(id)!;
        bool added = customers.Aliases.Add(".Name", ".DisplayName");
        ShowcaseCustomerRenamed renamed;
        try
        {
            renamed = customers.GetAs<ShowcaseCustomerRenamed>(id);
        }
        finally
        {
            if (added) customers.Aliases.Remove(".Name");
        }
        ShowcaseCustomer unchanged = customers.Get(id)!;
        watch.Stop();
        bool passed = renamed.DisplayName == original.Name && unchanged.Name == original.Name;
        return new ScenarioResult(passed, passed ? "A temporary read alias filled DisplayName; the stored Name remained unchanged." : "The alias projection or original member did not match.",
            [new("Stored path", ".Name"), new("Projected path", ".DisplayName"), new("Alias removed after read", added.ToString())],
            [new ScenarioResultItem("Original shape", id, original, typeof(ShowcaseCustomer)), new ScenarioResultItem("Renamed projection", id, renamed, typeof(ShowcaseCustomerRenamed))],
            watch.Elapsed.TotalMilliseconds);
    }
}

/// <summary>One configurable compiled scenario with embedded learner source and a native public-API action.<br/></summary>
internal sealed class CatalogActionScenario(
    string id, string category, string title, string description, string cost, bool mutates,
    ScenarioDocumentation documentation, string sourcePath, Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> action) : IShowcaseScenario
{
    public string Id => id;
    public string Category => category;
    public string Title => title;
    public string Description => description;
    public string Cost => cost;
    public bool Mutates => mutates;
    public ScenarioDocumentation Documentation => documentation;
    public string SourceCode => ExampleSource.Read(sourcePath);
    public string CompleteSource => ExampleSource.CompleteScenario(id);

    /// <summary>Runs this example against its already-prepared group environment and returns retained native results.<br/></summary>
    /// <param name="workspace">Active group-scoped RecordBase and typed stores.<br/></param>
    /// <param name="cancellationToken">Cancellation to observe between reader or batch operations.<br/></param>
    /// <returns>Proof facts and actual .NET result values for on-demand exploration.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken) => action(workspace, cancellationToken);
}

/// <summary>Smaller destination shape filled from matching stored customer member paths.<br/></summary>
public sealed class ShowcaseCustomerSummary
{
    public string Code { get; set; } = "";
    public string Region { get; set; } = "";
}

/// <summary>Alternate destination shape with two matching paths and one member absent from the stored customer.<br/></summary>
public sealed class ShowcaseCustomerAlternative
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Unmatched { get; set; } = "not stored";
}

/// <summary>Application's newer customer view whose DisplayName can temporarily read an older stored Name path.<br/></summary>
public sealed class ShowcaseCustomerRenamed
{
    public string DisplayName { get; set; } = "";
}
