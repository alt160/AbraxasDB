using AbraxasDB;
using System.Diagnostics;

namespace MeridianTrading;

/// <summary>Named reproducible corpus dimensions; physical record identities remain engine-assigned.<br/></summary>
internal sealed record GenerationProfile(string Name, int Customers, int Products, int Orders)
{
    public int OrdersPerCheckpoint { get; init; } = 256;
    /// <summary>Resolves a public profile name and validates custom order counts before any storage mutation.<br/></summary>
    internal static GenerationProfile Resolve(string name, int orders = 0)
    {
        GenerationProfile profile = name switch
        {
        "Smoke" => new(name, 100, 100, 1_000),
        "Standard" => new(name, 10_000, 5_000, 200_000),
        "Large" => new(name, 100_000, 25_000, 2_000_000),
        "Custom" when orders is >= 1 and <= 2_000_000 => new(name, Math.Clamp(orders / 20, 100, 100_000), Math.Clamp(orders / 40, 100, 25_000), orders),
        _ => throw new ArgumentException("Choose Smoke, Standard, Large, or Custom (1–2,000,000 orders).")
        };
        profile.ValidateCapacity();
        return profile;
    }

    /// <summary>Gets the generator's order limit for the loaded edition, accounting for five normalized lines per order on average.<br/>
    /// This bounds a fresh generated fixture; it is not a promise about remaining identities in an existing store.<br/></summary>
    internal static int MaximumOrders => (int)Math.Min(2_000_000UL, RecordBase.MaximumRecordCounter / 5);

    /// <summary>Rejects a corpus that cannot fit the loaded edition before writing a generation manifest or any application record.<br/>
    /// The deterministic 3,4,5,6,7 line cycle is counted without allocating or enumerating the requested corpus.<br/></summary>
    /// <exception cref="ArgumentOutOfRangeException">A requested store population exceeds the compiled identity capacity or supported generator dimensions.<br/></exception>
    internal void ValidateCapacity()
    {
        long remainder = Orders % 5;
        long lines = (long)(Orders / 5) * 25 + remainder * (remainder + 5) / 2;
        ulong limit = RecordBase.MaximumRecordCounter;
        if (Orders < 1 || Orders > MaximumOrders || Customers < 1 || Products < 1
            || (ulong)Customers > limit || (ulong)Products > limit || lines < 0 || (ulong)lines > limit)
            throw new ArgumentOutOfRangeException(nameof(Orders),
                $"This loaded Abraxas edition supports at most {MaximumOrders:N0} orders in a fresh Meridian corpus. Standard/Large require the commercial capacity; use Smoke or a smaller Custom profile.");
    }
}

/// <summary>Small progress packet; counters describe completed application checkpoints, not crash-proof transactions.<br/></summary>
internal sealed record GenerationProgress(string Phase, int CompletedOrders, int RequestedOrders, long CompletedLines, double ElapsedMs);

internal sealed partial class TradingDatabase
{
    /// <summary>Generates normalized data using bounded public progressive batches and post-ingestion indexes.<br/>
    /// Retains only master identity arrays and a bounded order chunk (maximum 1,024 headers / 7,168 lines); no all-orders identity list exists.<br/>
    /// Cancellation occurs before chunks and index builds. A failed or cancelled corpus is retained and never marked Ready.<br/></summary>
    internal void GenerateProfile(GenerationProfile profile, Action<GenerationProgress>? progress = null, CancellationToken cancel = default)
    {
        profile.ValidateCapacity();
        if (Manifest().Phase != "Empty") throw new InvalidOperationException("Generate into a new workspace; existing data is never replaced.");
        if (profile.OrdersPerCheckpoint is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(profile));
        var manifest = new DatasetManifest { Version = "meridian-3", Profile = profile.Name, Phase = "Generating masters", RequestedOrders = profile.Orders, RequestedCustomers = profile.Customers, RequestedProducts = profile.Products };
        using (ActionTiming.Db()) manifests.Put(manifestID, manifest);
        manifest.OrdersPerCheckpoint = profile.OrdersPerCheckpoint;
        var timer = Stopwatch.StartNew();
        try
        {
            var rng = new Random(manifest.Seed);
            var customers = new ulong[profile.Customers];
            var products = new ulong[profile.Products];
            var prices = new decimal[profile.Products];
            string[] names = ["Aster", "Cedar", "Harbor", "Meridian", "Northstar", "Orchard", "Summit", "Lakeside"];
            for (int start = 0; start < customers.Length; start += 512)
            {
                cancel.ThrowIfCancellationRequested();
                var chunk = new List<Customer>(512);
                for (int i = start; i < Math.Min(start + 512, customers.Length); i++) chunk.Add(new Customer { Code = $"CUS-{i + 1:000000}", Name = $"{names[i % names.Length]} Trading {i + 1:000000}", Region = Regions[i % Regions.Length], Email = $"purchasing{i + 1}@example.test", CreditLimit = (i % 8 + 1) * 10_000m });
                var ids = WriteChunk(Customers, chunk);
                for (int i = 0; i < ids.Count; i++) customers[start + i] = ids[i];
                ReportGeneration(manifest, timer, progress);
            }
            string[] categories = ["Pantry", "Beverages", "Specialty", "Kitchen", "Packaging"];
            for (int start = 0; start < products.Length; start += 512)
            {
                cancel.ThrowIfCancellationRequested();
                var chunk = new List<Product>(512);
                for (int i = start; i < Math.Min(start + 512, products.Length); i++)
                {
                    prices[i] = 5.25m + (i % 500) * 1.35m;
                    chunk.Add(new Product { SKU = $"MRD-{i + 1:000000}", Name = $"{categories[i % 5]} collection {i + 1:000000}", Category = categories[i % 5], Price = prices[i], ReorderPoint = 30 });
                }
                var ids = WriteChunk(Products, chunk);
                var stock = new List<InventoryPosition>(chunk.Count);
                for (int i = 0; i < ids.Count; i++)
                {
                    products[start + i] = ids[i];
                    int onHand = rng.Next(500);
                    stock.Add(new InventoryPosition { ProductID = ids[i], SKU = chunk[i].SKU, Warehouse = new[] { "Phoenix", "Portland", "Newark" }[(start + i) % 3], OnHand = onHand, Reserved = Math.Min(onHand, rng.Next(20)) });
                }
                _ = WriteChunk(Inventory, stock);
                ReportGeneration(manifest, timer, progress);
            }
            manifest.Phase = "Generating orders";
            string[] statuses = ["Delivered", "Processing", "Shipped", "Pending", "Cancelled"];
            for (int start = 0; start < profile.Orders; start += profile.OrdersPerCheckpoint)
            {
                cancel.ThrowIfCancellationRequested();
                var orders = new List<SalesOrder>(profile.OrdersPerCheckpoint);
                var lines = new List<OrderLine>(profile.OrdersPerCheckpoint * 7);
                for (int i = start; i < Math.Min(start + profile.OrdersPerCheckpoint, profile.Orders); i++)
                {
                    int owner = i % 11 == 0 ? 0 : rng.Next(Math.Max(1, customers.Length * 95 / 100));
                    var order = new SalesOrder { Number = $"SO-{100001 + i:0000000}", CustomerID = customers[owner], Region = Regions[owner % 5], Status = statuses[rng.Next(5)], OrderedAt = new DateTime(2026, 9, 1).AddDays(-rng.Next(365)), Lines = [] };
                    for (int j = 0; j < 3 + i % 5; j++)
                    {
                        int product = rng.Next(products.Length);
                        var line = new OrderLine { ProductID = products[product], SKU = $"MRD-{product + 1:000000}", Quantity = rng.Next(1, 25), UnitPrice = prices[product] };
                        line.Total = line.Quantity * line.UnitPrice;
                        lines.Add(line); order.Lines.Add(line); order.Total += line.Total;
                    }
                    orders.Add(order);
                }
                _ = WriteChunk(Lines, lines);
                _ = WriteChunk(Orders, orders);
                manifest.CompletedOrders += orders.Count;
                manifest.CompletedLines += lines.Count;
                ReportGeneration(manifest, timer, progress);
            }
            manifest.IngestionMilliseconds = timer.Elapsed.TotalMilliseconds;
            (string Name, Action Build)[] indexes = [
                ("Customer ownership", () => Orders.CreateIndex(o => o.CustomerID, IndexDirection.Ascending)),
                ("Order number", () => Orders.CreateIndex(o => o.Number, IndexDirection.Ascending)),
                ("Order date", () => Orders.CreateIndex(o => o.OrderedAt, IndexDirection.Ascending)),
                ("Order status", () => Orders.CreateIndex(o => o.Status, IndexDirection.Ascending)),
                ("Order region", () => Orders.CreateIndex(o => o.Region, IndexDirection.Ascending)),
                ("Customer name", () => Customers.CreateIndex(c => c.Name, IndexDirection.Ascending)),
                ("Customer email suffix", PrepareCustomerEmailSearch),
                ("Product name", () => Products.CreateIndex(p => p.Name, IndexDirection.Ascending)),
                ("Product price", PrepareCatalogSort),
                ("Stock on hand", PrepareDashboardIndexes),
                ("Line product", () => Lines.CreateIndex(l => l.ProductID, IndexDirection.Ascending)),
                ("Order total", () => PrepareSort("orders", "Total")),
                ("Customer credit", () => PrepareSort("customers", "CreditLimit")),
                ("Inventory SKU", () => PrepareSort("inventory", "SKU")),
                ("Line SKU", () => PrepareSort("lines", "SKU")),
                ("Line total", () => PrepareSort("lines", "Total"))];
            foreach (var index in indexes)
            {
                cancel.ThrowIfCancellationRequested();
                manifest.Phase = "Indexing: " + index.Name;
                ReportGeneration(manifest, timer, progress);
                double indexStart = timer.Elapsed.TotalMilliseconds;
                ActionTiming.Phase(manifest.Phase);
                using (ActionTiming.Db()) index.Build();
                manifest.IndexTimings.Add(new IndexBuildTiming { Name = index.Name, Milliseconds = timer.Elapsed.TotalMilliseconds - indexStart });
            }
            cancel.ThrowIfCancellationRequested();
            manifest.IndexingMilliseconds = timer.Elapsed.TotalMilliseconds - manifest.IngestionMilliseconds;
            manifest.Phase = "Ready";
            manifest.ApplicationIndexVersion = 3;
            ReportGeneration(manifest, timer, progress);
        }
        catch (Exception error)
        {
            if (manifest.IngestionMilliseconds == 0) manifest.IngestionMilliseconds = timer.Elapsed.TotalMilliseconds;
            else manifest.IndexingMilliseconds = timer.Elapsed.TotalMilliseconds - manifest.IngestionMilliseconds;
            manifest.Phase = error is OperationCanceledException ? "Cancelled" : "Incomplete";
            manifest.LastError = error.Message;
            ReportGeneration(manifest, timer, progress);
            throw;
        }
    }

    /// <summary>Commits one sub-threshold chunk and returns identities in source order without cumulative ID retention.<br/>
    /// Keeping the chunk below its configured threshold prevents automatic publication from consuming the explicit checkpoint's identities.<br/></summary>
    private static IReadOnlyList<ulong> WriteChunk<T>(RecordStore<T> store, List<T> records)
    {
        using var work = ActionTiming.Db();
        if (records.Count >= 8192) throw new ArgumentException("Application chunk must be smaller than its batch threshold.");
        using var batch = store.Writers.BeginBatch(new RecordWriteBatchOptions { CheckpointRecordCount = Math.Max(2048, records.Count + 1), RetainRecordIds = false });
        batch.PutMany(records);
        var checkpoint = batch.Commit();
        batch.Complete();
        return checkpoint.RecordIds;
    }

    /// <summary>Persists bounded checkpoint progress and sends a tiny callback packet after completed operations.<br/></summary>
    private void ReportGeneration(DatasetManifest manifest, Stopwatch timer, Action<GenerationProgress>? progress)
    {
        manifest.GenerationMilliseconds = timer.Elapsed.TotalMilliseconds;
        using (ActionTiming.Db()) manifests.Put(manifestID, manifest);
        progress?.Invoke(new(manifest.Phase, manifest.CompletedOrders, manifest.RequestedOrders, manifest.CompletedLines, manifest.GenerationMilliseconds));
    }
}
