using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Meridian-inspired search routes that teach distinct public Abraxas read choices without adding a second application harness.<br/></summary>
internal static class MeridianSearchScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        new CatalogActionScenario("search-order-date-window", "Search and lookup", "Find orders in a date window",
            "Use an indexed date range to select orders within a fixed window.", "Indexed range read", false,
            new ScenarioDocumentation("Need orders from a date window, like a range filter? The group already indexed OrderedAt. A Between condition finds January 2026 orders, and the result shows the actual .NET objects read from the store.",
                ["date range", "indexed condition"], ["Where.Prop", "Between", "IterateRecordIDs"], ["date window", "orders", "indexed range"], ["Prepare Search and lookup"], 430),
            "Search/OrderDateWindow.cs.txt", OrderDateWindow),
        new CatalogActionScenario("search-customer-pattern", "Search and lookup", "Search customer names by pattern",
            "Use a text pattern when an exact business-key lookup cannot express the request.", "Pattern scan", false,
            new ScenarioDocumentation("Need names ending in 1 rather than an exact Code? A text pattern finds them. Code and Region are indexed here, but this Name pattern is not; Abraxas must check the relevant stored names instead of using those other indexes as if they matched.",
                ["text pattern", "scan selection"], ["MatchesPattern", "IterateRecordIDs"], ["regex", "text search", "scan"], ["Prepare Search and lookup"], 440),
            "Search/CustomerPattern.cs.txt", CustomerPattern),
        new CatalogActionScenario("lookup-recordid", "Search and lookup", "Get a record by its RecordID",
            "Retrieve one selected record directly by its external Abraxas identity.", "Direct identity read", false,
            new ScenarioDocumentation("When the caller already has a RecordID, Get goes straight to that record. The example first obtains an ID from the prepared store, then performs the direct lookup without building a business-key condition.",
                ["direct identity", "external RecordID"], ["Readers.Open", "Get"], ["identity lookup", "single record"], ["Prepare Search and lookup"], 450),
            "Search/DirectRecordID.cs.txt", DirectRecordID)
    ];

    /// <summary>Reads a bounded sample selected by the group's prepared order-date index.<br/></summary>
    /// <param name="workspace">Active group RecordBase and normalized order store.<br/></param>
    /// <param name="token">Cancellation observed between selected records.<br/></param>
    /// <returns>Actual orders whose stored OrderedAt value lies in January 2026.<br/></returns>
    private static ScenarioResult OrderDateWindow(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        DateTime first = new(2026, 1, 1), last = new(2026, 1, 31);
        var condition = workspace.Orders.Where.Prop(order => order.OrderedAt).Between(first, last).EndCondition;
        var items = new List<ScenarioResultItem>();
        foreach (ulong id in workspace.Orders.IterateRecordIDs(condition))
        {
            token.ThrowIfCancellationRequested();
            ShowcaseOrder order = workspace.Orders.Get(id)!;
            if (order.OrderedAt < first || order.OrderedAt > last) throw new InvalidDataException("The returned order is outside the requested date window.");
            if (items.Count < 8) items.Add(new("January order", id, order, typeof(ShowcaseOrder)));
        }
        watch.Stop();
        return new ScenarioResult(true, "The indexed range returned orders dated in January 2026.",
            [new("Sampled orders", items.Count.ToString())], items.Count > 0 ? items : [new("No January orders", null, false, typeof(bool))], watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Evaluates a text-pattern condition against generated customer names without claiming an indexed pattern plan.<br/></summary>
    /// <param name="workspace">Active group RecordBase and customer store.<br/></param>
    /// <param name="token">Cancellation observed between matching records.<br/></param>
    /// <returns>Bounded actual matches whose names satisfy the displayed pattern.<br/></returns>
    private static ScenarioResult CustomerPattern(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var condition = workspace.Customers.Where.Prop(customer => customer.Name).MatchesPattern("1$", false).EndCondition;
        var items = new List<ScenarioResultItem>();
        foreach (ulong id in workspace.Customers.IterateRecordIDs(condition))
        {
            token.ThrowIfCancellationRequested();
            if (items.Count == 8) break;
            ShowcaseCustomer customer = workspace.Customers.Get(id)!;
            if (!customer.Name.EndsWith('1')) throw new InvalidDataException("The returned name does not match the pattern.");
            items.Add(new("Matching customer", id, customer, typeof(ShowcaseCustomer)));
        }
        watch.Stop();
        return new ScenarioResult(items.Count > 0, "The pattern selected generated customer names ending in 1.",
            [new("Shown matches", items.Count.ToString())], items.Count > 0 ? items : [new("No matches", null, false, typeof(bool))], watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Obtains a real RecordID from the prepared store and performs a direct identity retrieval.<br/></summary>
    /// <param name="workspace">Active group RecordBase and customer store.<br/></param>
    /// <param name="token">Cancellation observed before the direct lookup.<br/></param>
    /// <returns>The actual customer and external RecordID selected for inspection.<br/></returns>
    private static ScenarioResult DirectRecordID(ShowcaseWorkspace workspace, CancellationToken token)
    {
        using var reader = workspace.Customers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no customers.");
        ulong id = reader.ID;
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ShowcaseCustomer customer = workspace.Customers.Get(id)!;
        watch.Stop();
        return new ScenarioResult(customer is not null, "The external RecordID retrieved the selected customer directly.",
            [new("RecordID", id.ToString())], customer, typeof(ShowcaseCustomer), watch.Elapsed.TotalMilliseconds, id);
    }
}
