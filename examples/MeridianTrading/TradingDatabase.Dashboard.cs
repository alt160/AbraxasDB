using AbraxasDB;
using System.Diagnostics;

namespace MeridianTrading;

/// <summary>Small live aggregate bucket; never contains or retains source order graphs.<br/></summary>
internal sealed record DashboardGroup(string Key, ulong Count, decimal Total);

internal sealed partial class TradingDatabase
{
    /// <summary>Counts required indexed values on a ready corpus; incomplete corpora use authoritative identity counts.<br/>
    /// Required Number and ProductID values contribute exactly once per record; no manifest counters substitute for live data.<br/></summary>
    internal object DatasetInfo()
    {
        ActionTiming.Phase("Abraxas — reading live workspace counts");
        using var work = ActionTiming.Db();
        var manifest = Manifest();
        bool ready = manifest.Phase == "Ready";
        return new
        {
            manifest,
            directory = DirectoryPath,
            maximumGeneratedOrders = GenerationProfile.MaximumOrders,
            coveringAnalysis = HasCoveringAnalysis(),
            orders = ready ? Orders.Aggregates.Count(o => o.Number) : Orders.Count,
            lines = ready ? Lines.Aggregates.Count(l => l.ProductID) : Lines.Count,
            indexCatalogFileFormatVersion = IndexCatalogFileFormatVersion,
            recoveryMigrationRequired = RecoveryMigrationRequired,
            storage = StorageInfo()
        };
    }

    /// <summary>Builds all order KPIs and charts from one live two-key grouped count/sum result.<br/>
    /// Only the small group result is folded in application code; there is no whole-corpus cache or generated-total shortcut.<br/>
    /// All component timings describe actual public calls and result shaping within this request.<br/></summary>
    internal object Overview() => BuildOverview(projected: false);

    /// <summary>Reports only publicly attached, completed application analysis indexes.<br/></summary>
    internal bool HasCoveringAnalysis() => Orders.TryGetIndex("meridian-region-total", out _) && Orders.TryGetIndex("meridian-status-total", out _);

    /// <summary>Retains the equivalent public two-key aggregate route for measured comparison and parity checks.<br/></summary>
    internal object OverviewGrouped() => BuildOverview(projected: false);

    /// <summary>Diagnostic alternative using one public projected-value scan and application-side folding.<br/>
    /// It does not retain source rows or substitute generated facts; production selection requires measured parity.<br/></summary>
    internal object OverviewProjected() => BuildOverview(projected: true);

    /// <summary>Diagnostic full-scalar alternative; links remain suppressed and no source corpus is retained.<br/></summary>
    internal object OverviewScalar() => BuildOverview(projected: false, scalar: true);

    /// <summary>Builds live dashboard facts from either projected rows or public grouped results without retaining source rows.<br/></summary>
    private object BuildOverview(bool projected, bool scalar = false)
    {
        var timings = new Dictionary<string, double>();
        var timer = Stopwatch.StartNew();
        var regions = new Dictionary<string, (ulong Count, decimal Total)>(StringComparer.Ordinal);
        var statuses = new Dictionary<string, (ulong Count, decimal Total)>(StringComparer.Ordinal);
        ulong orders = 0, processing = 0;
        decimal revenue = 0;
        if (scalar)
        {
            ActionTiming.Phase("Abraxas — scanning scalar orders without links");
            using var work = ActionTiming.Db();
            using var reader = Orders.Readers.Open();
            while (reader.ReadNoLinks())
            {
                var row = reader.GetNoLinks();
                using var calculation = ActionTiming.Calculate();
                AddBucket(regions, row.Region, 1, row.Total);
                AddBucket(statuses, row.Status, 1, row.Total);
                orders++;
                if (row.Status != "Cancelled") revenue += row.Total;
                if (row.Status == "Processing") processing++;
            }
        }
        else if (projected)
        {
            ActionTiming.Phase("Abraxas — projecting order summary values");
            using var work = ActionTiming.Db();
            using var reader = Orders.Readers.OpenValues<string, string, decimal>(".Region", ".Status", ".Total");
            while (reader.Read())
            {
                var row = reader.Get();
                using var calculation = ActionTiming.Calculate();
                AddBucket(regions, row.Value1!, 1, row.Value3);
                AddBucket(statuses, row.Value2!, 1, row.Value3);
                orders++;
                if (row.Value2 != "Cancelled") revenue += row.Value3;
                if (row.Value2 == "Processing") processing++;
            }
        }
        else
        {
            ActionTiming.Phase("Abraxas — grouped order count and sum");
            using var work = ActionTiming.Db();
            var definition = Orders.Aggregates.GroupBy<string, string>(o => o.Region, o => o.Status).AddKeys().AddCount().AddSum(o => o.Total);
            using var reader = Orders.Aggregates.Open(definition);
            while (reader.Read())
            {
                var row = reader.Get();
                using var calculation = ActionTiming.Calculate();
                ulong count = row.Value3;
                decimal total = row.Value4;
                AddBucket(regions, row.Value1, count, total);
                AddBucket(statuses, row.Value2, count, total);
                orders += count;
                if (row.Value2 != "Cancelled") revenue += total;
                if (row.Value2 == "Processing") processing += count;
            }
        }
        timings[scalar ? "scalarOrderSummary" : projected ? "projectedOrderSummary" : "orderGroupedCountAndSum"] = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        // These model fields are required and indexed; each record contributes one non-null value.
        ulong customers, products, lines;
        ActionTiming.Phase("Abraxas — counting indexed customers, products and lines");
        using (ActionTiming.Db())
        {
            customers = Customers.Aggregates.Count(c => c.Name); products = Products.Aggregates.Count(p => p.Name); lines = Lines.Aggregates.Count(l => l.ProductID);
        }
        timings["storeCounts"] = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        ulong lowStock;
        ActionTiming.Phase("Abraxas — low-stock count");
        using (ActionTiming.Db()) lowStock = Inventory.Aggregates.Count(Inventory.Where.Prop(item => item.OnHand).LessThan(30).EndCondition);
        timings["lowStock"] = timer.Elapsed.TotalMilliseconds;
        return new { manifest = Manifest(), directory = DirectoryPath, orders, customers, products, lines, revenue, processing, lowStock,
            regions = regions.Select(p => new DashboardGroup(p.Key, p.Value.Count, p.Value.Total)).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray(),
            statuses = statuses.Select(p => new DashboardGroup(p.Key, p.Value.Count, p.Value.Total)).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray(), timings };
    }

    /// <summary>Folds one backend aggregate bucket into another small dashboard dimension with ordinal key semantics.<br/></summary>
    private static void AddBucket(Dictionary<string, (ulong Count, decimal Total)> target, string key, ulong count, decimal total)
    {
        target.TryGetValue(key, out var prior);
        target[key] = (checked(prior.Count + count), prior.Total + total);
    }

    /// <summary>Prepares the ordinary catalog sort requested by the UI through the supported public index API.<br/>
    /// This is explicit application schema setup, not a change to an Abraxas implementation or a private index bypass.<br/></summary>
    internal void PrepareCatalogSort() => Products.CreateIndex(p => p.Price, IndexDirection.Ascending);

    /// <summary>Sets up the actual filter used by the dashboard, avoiding an unindexed stock scan.<br/></summary>
    internal void PrepareDashboardIndexes() => Inventory.CreateIndex(i => i.OnHand, IndexDirection.Ascending);

    /// <summary>Creates two narrow covering indexes for the application's region/status monetary analyses.<br/>
    /// Both grouping key and amount are carried by supported Abraxas composite indexes, maintained by normal record writes.<br/></summary>
    internal void PrepareCoveringAnalysis()
    {
        PrepareRegionAnalysis();
        PrepareStatusAnalysis();
    }

    /// <summary>Prepares the region/value covering index independently so generation can report and cancel between builds.<br/></summary>
    internal void PrepareRegionAnalysis()
    {
        if (Orders.TryGetIndex("meridian-region-total", out _)) return;
        var selector = new Selector<SalesOrder>();
        Orders.CreateCompositeIndex("meridian-region-total", selector.Prop(o => o.Region).ForIndexing(), selector.Prop(o => o.Total).ForIndexing());
    }

    /// <summary>Prepares the status/value covering index independently through the ordinary public schema API.<br/></summary>
    internal void PrepareStatusAnalysis()
    {
        if (Orders.TryGetIndex("meridian-status-total", out _)) return;
        var selector = new Selector<SalesOrder>();
        Orders.CreateCompositeIndex("meridian-status-total", selector.Prop(o => o.Status).ForIndexing(), selector.Prop(o => o.Total).ForIndexing());
    }

    /// <summary>Upgrades a ready application workspace once, retaining partial successful index work for safe retry.<br/>
    /// The application schema marker advances only after every required public index setup call returns successfully.<br/></summary>
    internal void PrepareApplicationIndexes(Action<string>? progress = null)
    {
        var manifest = Manifest();
        if (manifest.Phase != "Ready" || manifest.ApplicationIndexVersion >= 3) return;
        if (manifest.ApplicationIndexVersion < 2)
        {
            progress?.Invoke("Preparing catalog sort index…"); PrepareCatalogSort();
            progress?.Invoke("Preparing inventory filter index…"); PrepareDashboardIndexes();
            progress?.Invoke("Preparing order total sort…"); PrepareSort("orders", "Total");
            progress?.Invoke("Preparing customer credit sort…"); PrepareSort("customers", "CreditLimit");
            progress?.Invoke("Preparing inventory SKU sort…"); PrepareSort("inventory", "SKU");
            progress?.Invoke("Preparing line SKU sort…"); PrepareSort("lines", "SKU");
            progress?.Invoke("Preparing line total sort…"); PrepareSort("lines", "Total");
        }
        progress?.Invoke("Preparing customer email suffix index…"); PrepareCustomerEmailSearch();
        manifest.ApplicationIndexVersion = 3;
        manifests.Put(manifestID, manifest);
    }

    /// <summary>Builds the same dashboard from two narrow covering aggregate traversals, with all counts kept live.<br/></summary>
    internal object OverviewCovered()
    {
        var timings = new Dictionary<string, double>();
        var watch = Stopwatch.StartNew();
        var regions = ReadDashboardGroups("Region");
        var statuses = ReadDashboardGroups("Status");
        timings["coveringGroupSummaries"] = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        ulong customers = Customers.Aggregates.Count(c => c.Name), products = Products.Aggregates.Count(p => p.Name), lines = Lines.Aggregates.Count(l => l.ProductID);
        timings["storeCounts"] = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        ulong lowStock = Inventory.Aggregates.Count(Inventory.Where.Prop(i => i.OnHand).LessThan(30).EndCondition);
        timings["lowStock"] = watch.Elapsed.TotalMilliseconds;
        ulong orders = 0, processing = 0; decimal revenue = 0;
        foreach (var row in statuses) { orders += row.Count; if (row.Key != "Cancelled") revenue += row.Total; if (row.Key == "Processing") processing += row.Count; }
        return new { manifest = Manifest(), directory = DirectoryPath, orders, customers, products, lines, revenue, processing, lowStock, regions, statuses, timings };
    }

    /// <summary>Executes an allowed live count/sum definition; Abraxas chooses its supported covering traversal.<br/></summary>
    private DashboardGroup[] ReadDashboardGroups(string property)
    {
        var definition = Orders.Aggregates.GroupBy<string>("." + property).AddKey().AddCount().AddSum(o => o.Total);
        using var reader = Orders.Aggregates.Open(definition);
        var groups = new List<DashboardGroup>();
        while (reader.Read()) { var row = reader.Get(); groups.Add(new(row.Value1, row.Value2, row.Value3)); }
        return groups.OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Prepares one allowlisted grid sort through public schema APIs, reusing an existing index.<br/>
    /// Application schema version two prepares these sorts before normal navigation, after million-line indexed acceptance.<br/>
    /// Index construction is a one-time setup cost; filters combined with sorts can still require separate query preparation.<br/></summary>
    internal void PrepareSort(string entity, string field)
    {
        switch ((entity, field))
        {
            case ("orders", "Number"): if (!Orders.TryGetIndex(".Number", out _)) Orders.CreateIndex(o => o.Number, IndexDirection.Ascending); break;
            case ("orders", "OrderedAt"): if (!Orders.TryGetIndex(".OrderedAt", out _)) Orders.CreateIndex(o => o.OrderedAt, IndexDirection.Ascending); break;
            case ("orders", "Total"): if (!Orders.TryGetIndex(".Total", out _)) Orders.CreateIndex(o => o.Total, IndexDirection.Ascending); break;
            case ("customers", "Name"): if (!Customers.TryGetIndex(".Name", out _)) Customers.CreateIndex(c => c.Name, IndexDirection.Ascending); break;
            case ("customers", "CreditLimit"): if (!Customers.TryGetIndex(".CreditLimit", out _)) Customers.CreateIndex(c => c.CreditLimit, IndexDirection.Ascending); break;
            case ("products", "Name"): if (!Products.TryGetIndex(".Name", out _)) Products.CreateIndex(p => p.Name, IndexDirection.Ascending); break;
            case ("products", "Price"): if (!Products.TryGetIndex(".Price", out _)) PrepareCatalogSort(); break;
            case ("inventory", "SKU"): if (!Inventory.TryGetIndex(".SKU", out _)) Inventory.CreateIndex(s => s.SKU, IndexDirection.Ascending); break;
            case ("inventory", "OnHand"): if (!Inventory.TryGetIndex(".OnHand", out _)) PrepareDashboardIndexes(); break;
            case ("lines", "SKU"): if (!Lines.TryGetIndex(".SKU", out _)) Lines.CreateIndex(l => l.SKU, IndexDirection.Ascending); break;
            case ("lines", "Total"): if (!Lines.TryGetIndex(".Total", out _)) Lines.CreateIndex(l => l.Total, IndexDirection.Ascending); break;
            default: throw new ArgumentException("Unsupported sort index.");
        }
    }

    /// <summary>
    /// Prepares the customer email index used by Meridian's suffix-search example.<br/>
    /// Exact and folded representations preserve case-sensitive and no-case equality/prefix semantics, while bidirectional routing gives LibraDex maintained reversed projections for literal suffix discovery.<br/>
    /// The ordinary public schema call is idempotent for a workspace that already owns the logical <c>.Email</c> index.<br/>
    /// </summary>
    internal void PrepareCustomerEmailSearch()
    {
        if (!Customers.TryGetIndex(".Email", out _))
            Customers.CreateIndex(customer => customer.Email, TextIndexProfile.ExactAndFolded, IndexDirection.Bidirectional);
    }
}
