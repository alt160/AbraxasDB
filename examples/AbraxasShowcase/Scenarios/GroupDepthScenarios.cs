using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Adds focused runnable operations so each established learning group has enough depth to stand alone.<br/></summary>
internal static class GroupDepthScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("put-multiple-customers", "Record persistence basics", "Put two independent objects", "Insert two customers and keep the two returned RecordIDs; neither customer class needs an ID member.", 112, "RecordPersistence/PutMultiple.cs.txt", PutMultiple, true),
        New("try-get-absent", "Record persistence basics", "Try to read a missing RecordID", "Use TryGet after deleting a scenario-owned record, when absence is an ordinary outcome.", 114, "RecordPersistence/TryGetAbsent.cs.txt", TryGetAbsent, true),
        New("read-array-member", "Record persistence basics", "Read an ordinary array member", "A string[] member round-trips with the rest of an unannotated customer.", 116, "RecordPersistence/ReadArray.cs.txt", ReadArray),
        New("enumerate-store-catalog", "Stores", "List every store definition", "GetStoreInfos discovers the named stores without reading their records.", 230, "Stores/EnumerateCatalog.cs.txt", EnumerateStoreCatalog),
        New("read-ids-without-objects", "Retrieval and readers", "Iterate RecordIDs without objects", "Walk customer IDs without building a customer object for every row.", 330, "Retrieval/ReadIDs.cs.txt", ReadIDs),
        New("read-orders-forward", "Retrieval and readers", "Stream typed orders", "The same forward reader pattern works for a second declared store type.", 340, "Retrieval/ReadOrders.cs.txt", ReadOrders),
        New("select-by-region", "Conditions and indexes", "Select by indexed region", "Use the prepared Region index to select matching customer IDs and objects.", 425, "Conditions/SelectRegion.cs.txt", SelectRegion),
        New("select-by-total-range", "Conditions and indexes", "Filter an unindexed value range", "A Total range condition can select orders even without a durable Total index.", 426, "Conditions/TotalRange.cs.txt", SelectTotalRange),
        New("find-first-in-region", "Search and lookup", "Find one customer in a region", "Stop indexed region selection as soon as one matching customer is found.", 460, "Search/FirstInRegion.cs.txt", FirstInRegion),
        New("project-customer-summary", "Projection and value reads", "Fill a small customer shape", "Read a customer into a smaller .NET type using the stored members whose paths match.", 525, "Projection/CustomerSummary.cs.txt", ProjectSummary),
        New("read-order-total-path", "Projection and value reads", "Read just an order total", "GetValues returns one requested path without creating an order object.", 526, "Projection/OrderTotal.cs.txt", ReadOrderTotal),
        New("read-customer-without-links", "Object graphs across stores", "Read a customer without child links", "Need only the customer? GetNoLinks leaves its related Orders unloaded for this read.", 605, "Graphs/ReadCustomerNoLinks.cs.txt", ReadCustomerNoLinks),
        New("count-related-orders", "Relationships and aggregates", "Count one customer's orders", "Use the customer's RecordID to count its orders without building each order object.", 625, "Relationships/CountRelatedOrders.cs.txt", CountRelatedOrders),
        New("sum-order-totals", "Relationships and aggregates", "Sum order totals", "Add stored order totals without building each order object.", 630, "Relationships/SumOrderTotals.cs.txt", SumOrderTotals),
        New("update-indexed-code", "Mutation and lifecycle", "Change an indexed business key", "Replacing a record updates its Code lookup while keeping the same RecordID.", 725, "Mutation/UpdateIndexedCode.cs.txt", UpdateIndexedCode, true),
        New("delete-missing-id", "Mutation and lifecycle", "Delete an already absent record", "Delete returns false when the requested RecordID no longer exists.", 730, "Mutation/DeleteMissing.cs.txt", DeleteMissing, true),
        New("count-ids-without-hydration", "Scale and performance", "Count a full ID traversal", "Compare an identity-only traversal count with the maintained store population.", 830, "Scale/CountIDs.cs.txt", CountIDs)
    ];

    /// <summary>Creates one scenario with a separate paste-ready source fragment and its real host action.<br/></summary>
    /// <param name="id">Stable navigation and export identity.<br/></param>
    /// <param name="category">Existing group that owns this scenario.<br/></param>
    /// <param name="title">Concise learner-facing title.<br/></param>
    /// <param name="description">One-sentence behavior and learning objective.<br/></param>
    /// <param name="order">Order within the curriculum.<br/></param>
    /// <param name="source">Embedded scenario source fragment.<br/></param>
    /// <param name="action">Real operation over the prepared group RecordBase.<br/></param>
    /// <param name="mutates">Whether the operation writes a scenario-owned record.<br/></param>
    /// <returns>A searchable executable scenario using the shared presentation template.<br/></returns>
    private static IShowcaseScenario New(string id, string category, string title, string description, int order,
        string source, Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> action, bool mutates = false) =>
        new CatalogActionScenario(id, category, title, description, mutates ? "Scenario-owned write" : "Focused read", mutates,
            new ScenarioDocumentation(description, [title], ["RecordStore<T>", "RecordBase"],
                [title, description], ["Prepare " + category], order), source, action);

    /// <summary>Persists two distinct ordinary customers and verifies their independently returned RecordIDs.<br/></summary>
    private static ScenarioResult PutMultiple(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        string suffix = Guid.NewGuid().ToString("N");
        ulong first = workspace.Customers.Put(new ShowcaseCustomer { Code = "PAIR-A-" + suffix, Name = "First", Region = "West" });
        ulong second = workspace.Customers.Put(new ShowcaseCustomer { Code = "PAIR-B-" + suffix, Name = "Second", Region = "West" });
        ShowcaseCustomer a = workspace.Customers.Get(first)!;
        ShowcaseCustomer b = workspace.Customers.Get(second)!;
        watch.Stop();
        return new ScenarioResult(first != second && a.Name == "First" && b.Name == "Second", "Two writes produced two independent external identities.", [],
            [new("First customer", first, a, typeof(ShowcaseCustomer)), new("Second customer", second, b, typeof(ShowcaseCustomer))], watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Demonstrates absence-aware lookup with a valid same-store RecordID whose record was removed.<br/></summary>
    private static ScenarioResult TryGetAbsent(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong id = workspace.Customers.Put(new ShowcaseCustomer { Code = "TRYGET-" + Guid.NewGuid().ToString("N"), Name = "Temporary customer" });
        workspace.Customers.Delete(id);
        bool found = workspace.Customers.TryGet(id, out ShowcaseCustomer? customer);
        watch.Stop();
        return new ScenarioResult(!found && customer is null, "TryGet reported an absent RecordID without requiring an exception.",
            [new("RecordID requested", id.ToString())], found, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Reads an existing customer and verifies its ordinary string array member survives hydration.<br/></summary>
    private static ScenarioResult ReadArray(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var reader = workspace.Customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong id = reader.ID;
        ShowcaseCustomer customer = reader.Get();
        watch.Stop();
        return new ScenarioResult(customer.Tags.Length > 0, "The string[] Tags member was read back without persistence attributes.", [], customer.Tags, typeof(string[]), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Enumerates detached metadata for all stores owned by the active RecordBase.<br/></summary>
    private static ScenarioResult EnumerateStoreCatalog(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        RecordBase.StoresInfo stores = workspace.RecordBase.GetStoreInfos() ?? new RecordBase.StoresInfo();
        var items = stores.Select(store => new ScenarioResultItem(store.Key, null, store.Value, typeof(RecordBase.StoreInfo))).ToList();
        watch.Stop();
        return new ScenarioResult(items.Any(item => item.Label == "customers") && items.Any(item => item.Label == "orders"),
            "The RecordBase lists both saved stores without reading their record objects.", [], items, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Walks a bounded set of RecordIDs without requesting corresponding domain objects.<br/></summary>
    private static ScenarioResult ReadIDs(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var items = new List<ScenarioResultItem>();
        foreach (ulong id in workspace.Customers.IterateRecordIDs())
        {
            token.ThrowIfCancellationRequested();
            items.Add(new("Customer RecordID", null, id, typeof(ulong)));
            if (items.Count == 8) break;
        }
        watch.Stop();
        return new ScenarioResult(items.Count > 0, "Read customer RecordIDs without building customer objects.", [], items, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Uses a typed forward reader over the order store and retains a bounded object sample.<br/></summary>
    private static ScenarioResult ReadOrders(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var items = new List<ScenarioResultItem>();
        using var reader = workspace.Orders.Readers.Open();
        while (items.Count < 8 && reader.Read())
        {
            token.ThrowIfCancellationRequested();
            items.Add(new("Order", reader.ID, reader.Get(), typeof(ShowcaseOrder)));
        }
        watch.Stop();
        return new ScenarioResult(items.Count > 0, "The same reader pattern streamed typed orders.", [], items, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Selects customers whose Region equals the group's prepared indexed value.<br/></summary>
    private static ScenarioResult SelectRegion(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var condition = workspace.Customers.Where.Prop(customer => customer.Region).EqualTo("West").EndCondition;
        var items = new List<ScenarioResultItem>();
        foreach (ulong id in workspace.Customers.IterateRecordIDs(condition))
        {
            token.ThrowIfCancellationRequested();
            ShowcaseCustomer customer = workspace.Customers.Get(id)!;
            if (customer.Region != "West") throw new InvalidDataException("Region selection returned a different value.");
            if (items.Count < 8) items.Add(new("West customer", id, customer, typeof(ShowcaseCustomer)));
        }
        watch.Stop();
        return new ScenarioResult(items.Count > 0, "The indexed Region condition selected West customers.", [], items, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Applies a decimal range condition to an unindexed order member and checks each result.<br/></summary>
    private static ScenarioResult SelectTotalRange(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var condition = workspace.Orders.Where.Prop(order => order.Total).Between(25m, 5000m).EndCondition;
        var items = new List<ScenarioResultItem>();
        foreach (ulong id in workspace.Orders.IterateRecordIDs(condition))
        {
            token.ThrowIfCancellationRequested();
            ShowcaseOrder order = workspace.Orders.Get(id)!;
            if (order.Total < 25m || order.Total > 5000m) throw new InvalidDataException("Total is outside the range.");
            if (items.Count < 8) items.Add(new("Order in range", id, order, typeof(ShowcaseOrder)));
        }
        watch.Stop();
        return new ScenarioResult(items.Count > 0, "A value-range condition worked without a Total index.", [], items, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Stops indexed Region selection after its first match rather than scanning every matching object.<br/></summary>
    private static ScenarioResult FirstInRegion(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var condition = workspace.Customers.Where.Prop(customer => customer.Region).EqualTo("West").EndCondition;
        ulong id = workspace.Customers.IterateRecordIDs(condition).FirstOrDefault();
        ShowcaseCustomer? customer = id == 0 ? null : workspace.Customers.Get(id);
        watch.Stop();
        return new ScenarioResult(customer?.Region == "West", "Stopped after the first West customer.", [], customer, typeof(ShowcaseCustomer), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Projects a stored customer into a smaller .NET shape by matching member paths.<br/></summary>
    private static ScenarioResult ProjectSummary(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var reader = workspace.Customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong id = reader.ID;
        ShowcaseCustomerSummary summary = workspace.Customers.GetAs<ShowcaseCustomerSummary>(id);
        watch.Stop();
        return new ScenarioResult(summary.Code.Length > 0 && summary.Region.Length > 0, "Matching paths populated the smaller customer shape.", [],
            summary, typeof(ShowcaseCustomerSummary), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Retrieves one decimal path from an order without hydrating a ShowcaseOrder instance.<br/></summary>
    private static ScenarioResult ReadOrderTotal(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var reader = workspace.Orders.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no orders.");
        ulong id = reader.ID;
        object? total = workspace.Orders.GetValues(id, ".Total")?[0];
        watch.Stop();
        return new ScenarioResult(total is decimal value && value >= 25m, "Read only .Total without filling an order object.", [],
            total, total?.GetType() ?? typeof(object), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Reads a graph customer with links suppressed so its reverse Orders member stays unloaded.<br/></summary>
    private static ScenarioResult ReadCustomerNoLinks(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var reader = workspace.GraphCustomers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The graph group has no customers.");
        ShowcaseGraphCustomer customer = reader.GetNoLinks();
        watch.Stop();
        return new ScenarioResult(customer.Orders is null, "The customer was read without loading its reverse Orders link.", [],
            customer, typeof(ShowcaseGraphCustomer), watch.Elapsed.TotalMilliseconds, reader.ID);
    }

    /// <summary>Counts indexed child order identities for one real customer without hydrating the orders.<br/></summary>
    private static ScenarioResult CountRelatedOrders(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var reader = workspace.Customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong customerID = reader.ID;
        var condition = workspace.Orders.Where.Prop(order => order.CustomerID).EqualTo(customerID).EndCondition;
        ulong count = (ulong)workspace.Orders.IterateRecordIDs(condition).Count();
        watch.Stop();
        return new ScenarioResult(count > 0, "Counted related orders by their stored parent RecordID.",
            [new("Customer RecordID", customerID.ToString())], count, typeof(ulong), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Computes a decimal aggregate from stored order totals without materializing order objects.<br/></summary>
    private static ScenarioResult SumOrderTotals(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        decimal total = workspace.Orders.Aggregates.Sum(order => order.Total);
        watch.Stop();
        return new ScenarioResult(total > 0m, "Aggregates.Sum added stored order totals without building order objects.", [],
            total, typeof(decimal), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Changes the Code of a scenario-owned record and verifies both index lookups under the same RecordID.<br/></summary>
    private static ScenarioResult UpdateIndexedCode(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        string oldCode = "KEY-OLD-" + Guid.NewGuid().ToString("N");
        string newCode = "KEY-NEW-" + Guid.NewGuid().ToString("N");
        ulong id = workspace.Customers.Put(new ShowcaseCustomer { Code = oldCode, Name = "Indexed key change", Region = "West" });
        ShowcaseCustomer customer = workspace.Customers.Get(id)!;
        customer.Code = newCode;
        workspace.Customers.Put(id, customer);
        bool oldFound = workspace.Customers.GetOrDefault(".Code", oldCode) is not null;
        ShowcaseCustomer? byNewCode = workspace.Customers.GetOrDefault(".Code", newCode);
        watch.Stop();
        return new ScenarioResult(!oldFound && byNewCode?.Code == newCode, "The old key stopped matching and the new key selected the same record.",
            [new("RecordID", id.ToString())], byNewCode, typeof(ShowcaseCustomer), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Deletes a newly inserted record twice to expose the missing-record return value on the second call.<br/></summary>
    private static ScenarioResult DeleteMissing(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong id = workspace.Customers.Put(new ShowcaseCustomer { Code = "DELETE-TWICE-" + Guid.NewGuid().ToString("N"), Name = "Delete twice" });
        bool first = workspace.Customers.Delete(id);
        bool second = workspace.Customers.Delete(id);
        watch.Stop();
        return new ScenarioResult(first && !second, "The first delete removed the record; the second reported it absent.",
            [new("RecordID", id.ToString())], second, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Traverses every customer RecordID and compares the count with the maintained population.<br/></summary>
    private static ScenarioResult CountIDs(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        ulong visited = 0;
        foreach (ulong _ in workspace.Customers.IterateRecordIDs())
        {
            token.ThrowIfCancellationRequested();
            visited++;
        }
        ulong maintained = workspace.Customers.Aggregates.Count();
        watch.Stop();
        return new ScenarioResult(visited == maintained, "The RecordID traversal and maintained count agreed; no customer objects were built.",
            [new("Maintained count", maintained.ToString())], visited, typeof(ulong), watch.Elapsed.TotalMilliseconds);
    }
}
