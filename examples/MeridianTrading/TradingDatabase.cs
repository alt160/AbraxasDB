using AbraxasDB;
using Inheto;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;

namespace MeridianTrading;

/// <summary>
/// Small application service demonstrating public Abraxas calls without a repository abstraction.<br/>
/// The host serializes calls. Scalar grids suppress Links; explicit details hydrate complete bounded order lines.<br/>
/// </summary>
internal sealed partial class TradingDatabase : IDisposable
{
    private readonly RecordBase database;
    internal readonly RecordStore<Customer> Customers;
    internal readonly RecordStore<SalesOrder> Orders;
    internal readonly RecordStore<OrderLine> Lines;
    internal readonly RecordStore<Product> Products;
    internal readonly RecordStore<InventoryPosition> Inventory;
    private readonly RecordStore<DatasetManifest> manifests;
    private readonly ulong manifestID;
    internal readonly string DirectoryPath;
    private const ushort RecoverableLibraDexFileFormatVersion = 2;
    private static readonly string[] Regions = ["West", "Northeast", "South", "Midwest", "International"];

    /// <summary>Opens application stores and rebinds application-owned link codecs on every launch.<br/></summary>
    /// <param name="path">Dedicated Meridian dataset directory.<br/></param>
    internal TradingDatabase(string path)
    {
        DirectoryPath = Path.GetFullPath(path);
        Directory.CreateDirectory(DirectoryPath);
        var configuration = RecordBaseConfiguration.Default;
        configuration.UseRecoverableIndexFileFormat = true;
        database = RecordBase.CreateOrOpen(DirectoryPath, "meridian", configuration);
        Lines = database.CreateOrOpenStore<OrderLine>("order-lines");
        // The line model deliberately demonstrates Abraxas's optional object-identity hook. Most Meridian
        // models carry no storage identity at all; OrderLine opts in because its generated identity is also
        // the packed relationship value retained by SalesOrder.Lines.
        Lines.ObjectIDSetter = static (line, generatedID) => line.ID = generatedID;
        Products = database.CreateOrOpenStore<Product>("products");
        Inventory = database.CreateOrOpenStore<InventoryPosition>("inventory");
        Orders = database.CreateOrOpenStore<SalesOrder>("orders");
        Customers = database.CreateOrOpenStore<Customer>("customers");
        manifests = database.CreateOrOpenStore<DatasetManifest>("dataset");
        // Abraxas/Inheto invokes this property-path codec while persisting and materializing SalesOrder.
        // An ordinary Orders.Get(...) therefore returns a natural SalesOrder whose Lines collection is
        // already composed from the separate order-lines store; Meridian has no repository/ORM join step.
        Orders.Links.Add(order => order.Lines, SerializeLineRelationship, HydrateLinesFromStoredIdentities);

        // This second shape derives Customer.Orders from the parent record identity. Abraxas/Inheto passes
        // that identity to the registered resolver and assigns the returned collection during materialization.
        Customers.Links.AddReadOnly<List<SalesOrder>>(".Orders", LinkSource.RecordID,
            bytes => HydrateOrdersFromCustomerIndex(InhetoBinaryCodec.Decode<ulong>(bytes)));
        using var reader = manifests.Readers.Open();
        manifestID = reader.Read() ? reader.ID : manifests.Put(new DatasetManifest());
    }

    /// <summary>
    /// Gets the actual LibraDex file-format version read from this workspace's open RecordBase.<br/>
    /// This is intentionally distinct from Meridian's requested creation policy so a legacy workspace remains visibly legacy until the user explicitly migrates it.<br/>
    /// </summary>
    internal ushort IndexCatalogFileFormatVersion => database.IndexCatalogFileFormatVersion;

    /// <summary>
    /// Gets whether this workspace still requires the explicit version-one-to-version-two LibraDex migration.<br/>
    /// New Meridian workspaces begin in version two; existing version-one workspaces remain readable and are never rewritten merely by opening them.<br/>
    /// </summary>
    internal bool RecoveryMigrationRequired => IndexCatalogFileFormatVersion < RecoverableLibraDexFileFormatVersion;

    /// <summary>
    /// Explicitly migrates a legacy workspace through RecordBase's coordinated compaction path and then completes any pending application-index setup.<br/>
    /// The callback reports only real phase boundaries; compaction has no determinate item count, so the UI presents an indeterminate wait indicator instead of inventing a percentage.<br/>
    /// The operation is idempotent for an already-migrated workspace and verifies the actual reopened catalog version before returning.<br/>
    /// </summary>
    /// <param name="progress">Optional callback receiving completed or active phase descriptions.<br/></param>
    /// <param name="cancellationToken">Cancellation observed at RecordBase maintenance and application-index boundaries.<br/></param>
    /// <returns>Fresh Dataset-tab data measured after migration and application-index preparation.<br/></returns>
    internal object MigrateRecoveryFormat(Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (RecoveryMigrationRequired)
        {
            const string phase = "Abraxas — migrating LibraDex to embedded recovery format v2";
            progress?.Invoke(phase);
            ActionTiming.Phase(phase);
            using (ActionTiming.Db())
                database.CompactAsync(cancellationToken).GetAwaiter().GetResult();
        }

        if (IndexCatalogFileFormatVersion != RecoverableLibraDexFileFormatVersion)
            throw new InvalidDataException($"Expected LibraDex file format v{RecoverableLibraDexFileFormatVersion}, but the reopened RecordBase reports v{IndexCatalogFileFormatVersion}.");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke("Abraxas — preparing Meridian application indexes");
        PrepareApplicationIndexes(message =>
        {
            progress?.Invoke(message);
            ActionTiming.Phase("Abraxas — " + message);
        });
        progress?.Invoke("Recovery migration complete");
        return DatasetInfo();
    }

    /// <summary>Returns the persisted generation readiness contract.<br/></summary>
    internal DatasetManifest Manifest() { using var work = ActionTiming.Db(); return manifests.Get(manifestID); }

    /// <summary>
    /// Serializes the normalized <see cref="SalesOrder.Lines"/> relationship as an ordered identity list.<br/>
    /// Abraxas/Inheto invokes this writer for the registered <c>.Lines</c> property path while persisting a <see cref="SalesOrder"/>.<br/>
    /// The child objects remain authoritative records in the separate <c>order-lines</c> store; the order payload retains only their identities.<br/>
    /// </summary>
    /// <param name="lines">Runtime relationship value supplied by the Abraxas property-path writer.<br/></param>
    /// <returns>A checked count prefix followed by the line identities in application order.<br/></returns>
    private static byte[] SerializeLineRelationship(List<OrderLine>? lines)
    {
        using var work = ActionTiming.Calculate();
        int count = lines?.Count ?? 0;
        var bytes = new byte[checked(4 + count * 8)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        for (int i = 0; i < count; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4 + i * 8), lines![i].ID);
        return bytes;
    }

    /// <summary>
    /// Hydrates the normalized <see cref="SalesOrder.Lines"/> relationship from its ordered identity payload.<br/>
    /// Abraxas/Inheto invokes this reader during ordinary <c>Orders.Get(...)</c> and reader materialization, then assigns the returned list to <see cref="SalesOrder.Lines"/> before returning the order to application code.<br/>
    /// Direct identity retrieval bypasses query planning while retaining the exact stored order; missing children fail visibly instead of silently changing the logical collection.<br/>
    /// No repository, ORM, change tracker, or post-hydration join is involved.<br/>
    /// </summary>
    /// <param name="bytes">Exact relationship payload retained in the parent order record.<br/></param>
    /// <returns>The ordered line objects loaded from the separate <c>order-lines</c> store.<br/></returns>
    private List<OrderLine> HydrateLinesFromStoredIdentities(ReadOnlySpan<byte> bytes)
    {
        using var work = ActionTiming.Calculate();
        if (bytes.Length < 4) throw new InvalidDataException("Invalid order-line manifest.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count < 0 || bytes.Length != 4L + count * 8L) throw new InvalidDataException("Invalid order-line count.");
        var ids = new ulong[count];
        for (int i = 0; i < count; i++) ids[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(4 + i * 8, 8));
        IReadOnlyList<OrderLine> values;
        using (ActionTiming.Db()) values = Lines.GetMany(ids);
        var result = new List<OrderLine>(count);
        for (int i = 0; i < count; i++) { values[i].ID = ids[i]; result.Add(values[i]); }
        return result;
    }

    /// <summary>
    /// Hydrates <see cref="Customer.Orders"/> through the indexed child <see cref="SalesOrder.CustomerID"/> relationship under the current immutable <see cref="CustomerOrderHydrationScope"/> policy.<br/>
    /// Abraxas/Inheto supplies the parent record identity to this registered property-path resolver; the resolver optionally adds an indexed date/value rule or a descending date limit, and Abraxas assigns the policy-complete collection before returning the customer object.<br/>
    /// Without an ambient filter this explicit object path returns the complete historical collection; grids deliberately use a separate paged, links-suppressed path.<br/>
    /// </summary>
    /// <param name="id">Customer record identity supplied by <see cref="LinkSource.RecordID"/>.<br/></param>
    /// <returns>Normalized orders selected through the customer-ID index and the active ambient policy, with every nested line relationship hydrated.<br/></returns>
    private List<SalesOrder> HydrateOrdersFromCustomerIndex(ulong id)
    {
        CustomerOrderHydrationPolicy policy = CustomerOrderHydrationScope.Current;
        var result = new List<SalesOrder>();
        var condition = policy.Mode switch
        {
            CustomerOrderHydrationMode.DateWindow => Orders.Where.Prop(order => order.CustomerID).EqualTo(id)
                .AND.Prop(order => order.OrderedAt).Between(policy.FromDate, policy.ThroughDate).EndCondition,
            CustomerOrderHydrationMode.OrderValue => Orders.Where.Prop(order => order.CustomerID).EqualTo(id)
                .AND.Prop(order => order.Total).Between(policy.MinimumTotal, policy.MaximumTotal).EndCondition,
            _ => Orders.Where.Prop(order => order.CustomerID).EqualTo(id).EndCondition
        };

        if (policy.Mode == CustomerOrderHydrationMode.RecentCount)
        {
            if (!Orders.TryGetIndex(".OrderedAt", out RecordIndex orderedAtIndex)) throw new InvalidOperationException("Recent-order hydration requires the prepared OrderedAt index.");
            using var recent = Orders.Readers.OpenSorted(condition, new IndexSort(orderedAtIndex, SortDirection.Descending));
            while (result.Count < policy.RecentCount && recent.Read()) result.Add(recent.Get());
            return result;
        }

        using var reader = Orders.Readers.Open(condition);
        while (reader.Read()) result.Add(reader.Get());
        return result;
    }

    /// <summary>Generates repeatable smoke data in an empty dataset, retaining an incomplete marker on failure.<br/>
    /// One order and its bounded line list are retained at a time. Large-scale batching is a later milestone.<br/></summary>
    /// <param name="orderCount">Number of order headers, bounded for the first executable slice.<br/></param>
    /// <param name="progress">Optional phase/progress callback invoked between completed orders.<br/></param>
    /// <param name="cancel">Cancellation observed before starting the next order.<br/></param>
    internal void Generate(int orderCount, Action<int>? progress = null, CancellationToken cancel = default)
    {
        if (orderCount < 1 || orderCount > 10_000) throw new ArgumentOutOfRangeException(nameof(orderCount), "Initial slice supports 1–10,000 orders. Large profiles are in the blueprint.");
        if (Manifest().Phase != "Empty") throw new InvalidOperationException("Use a new dataset directory; this dataset has already started generation.");
        var manifest = new DatasetManifest { Phase = "Generating", RequestedOrders = orderCount };
        manifests.Put(manifestID, manifest);
        var timer = Stopwatch.StartNew();
        try
        {
            var rng = new Random(manifest.Seed);
            var customerIDs = new ulong[100];
            string[] first = ["Aster", "Northstar", "Cedar", "Atlas", "Sierra", "Harbor", "Summit", "Juniper", "Orchard", "Lakeside"];
            string[] last = ["Supply Co.", "Market", "Provisioners", "Wholesale", "Trading", "Foods", "Collective", "Stores", "Hospitality", "Goods"];
            for (int i = 0; i < customerIDs.Length; i++)
                customerIDs[i] = Customers.Put(new Customer { Code = $"CUS-{i + 1:0000}", Name = $"{first[i % 10]} {last[i / 10]}", Region = Regions[i % 5], Email = $"purchasing{i + 1}@example.com", CreditLimit = (i % 8 + 1) * 10_000m });
            var productIDs = new ulong[100];
            var productPrices = new decimal[100];
            string[] categories = ["Pantry", "Beverages", "Specialty", "Kitchen", "Packaging"];
            string[] names = ["Organic olive oil", "Single-origin coffee", "Wildflower honey", "Ceramic serving set", "Recycled gift cartons", "Arborio rice", "Jasmine green tea", "Smoked sea salt", "Linen table runner", "Kraft carry bags"];
            for (int i = 0; i < productIDs.Length; i++)
            {
                productPrices[i] = 5.25m + i * 1.35m;
                productIDs[i] = Products.Put(new Product { SKU = $"MRD-{i + 1:0000}", Name = names[i % 10] + $" / pack {i / 10 + 1}", Category = categories[i % 5], Price = productPrices[i], ReorderPoint = 30 });
                int onHand = rng.Next(0, 500);
                Inventory.Put(new InventoryPosition { ProductID = productIDs[i], SKU = $"MRD-{i + 1:0000}", Warehouse = new[] { "Phoenix", "Portland", "Newark" }[i % 3], OnHand = onHand, Reserved = Math.Min(onHand, rng.Next(0, 20)) });
            }
            for (int i = 0; i < orderCount; i++)
            {
                cancel.ThrowIfCancellationRequested();
                int owner = i % 11 == 0 ? 0 : rng.Next(0, 95);
                var order = new SalesOrder { Number = $"SO-{100001 + i}", CustomerID = customerIDs[owner], Region = Regions[owner % 5], Status = new[] { "Delivered", "Processing", "Shipped", "Pending", "Cancelled" }[rng.Next(5)], OrderedAt = new DateTime(2026, 9, 1).AddDays(-rng.Next(365)), Lines = [] };
                for (int j = 0; j < 3 + i % 5; j++)
                {
                    int product = rng.Next(100);
                    var line = new OrderLine { ProductID = productIDs[product], SKU = $"MRD-{product + 1:0000}", Quantity = rng.Next(1, 25), UnitPrice = productPrices[product] };
                    line.Total = line.Quantity * line.UnitPrice;
                    line.ID = Lines.Put(line);
                    order.Lines.Add(line);
                    order.Total += line.Total;
                }
                Orders.Put(order);
                if (i % 50 == 0) progress?.Invoke(i + 1);
            }
            manifest.Phase = "Indexing";
            manifests.Put(manifestID, manifest);
            Orders.CreateIndex(order => order.CustomerID, IndexDirection.Ascending);
            Orders.CreateIndex(order => order.Number, IndexDirection.Ascending);
            Orders.CreateIndex(order => order.OrderedAt, IndexDirection.Ascending);
            Orders.CreateIndex(order => order.Status, IndexDirection.Ascending);
            Orders.CreateIndex(order => order.Region, IndexDirection.Ascending);
            Customers.CreateIndex(customer => customer.Name, IndexDirection.Ascending);
            Customers.CreateIndex(customer => customer.Email, TextIndexProfile.ExactAndFolded, IndexDirection.Bidirectional);
            Products.CreateIndex(product => product.Name, IndexDirection.Ascending);
            Lines.CreateIndex(line => line.ProductID, IndexDirection.Ascending);
            manifest.GenerationMilliseconds = timer.Elapsed.TotalMilliseconds;
            manifest.Phase = "Ready";
            manifests.Put(manifestID, manifest);
            progress?.Invoke(orderCount);
        }
        catch
        {
            manifest.Phase = "Incomplete";
            manifests.Put(manifestID, manifest);
            throw;
        }
    }

    /// <summary>Computes live dashboard facts through Abraxas aggregate calls.<br/></summary>
    internal object OverviewBaseline() => new
    {
        manifest = Manifest(), directory = DirectoryPath,
        orders = Orders.Aggregates.Count(), customers = Customers.Aggregates.Count(), products = Products.Aggregates.Count(), lines = Lines.Aggregates.Count(),
        revenue = Orders.Aggregates.Sum(order => order.Total, Orders.Where.Prop(order => order.Status).NotEqualTo("Cancelled").EndCondition),
        processing = Orders.Aggregates.Count(Orders.Where.Prop(order => order.Status).EqualTo("Processing").EndCondition),
        lowStock = Inventory.Aggregates.Count(Inventory.Where.Prop(item => item.OnHand).LessThan(30).EndCondition),
        regions = Analyze("Region"), statuses = Analyze("Status")
    };

    /// <summary>Groups order values using public deferred aggregate definitions and returns scalar output only.<br/></summary>
    /// <param name="group">Allowed order grouping property; arbitrary code and paths are rejected.<br/></param>
    internal object Analyze(string group)
    {
        ActionTiming.Phase("Abraxas — grouping order values by " + group);
        using var work = ActionTiming.Db();
        if (group is not ("Region" or "Status")) throw new ArgumentException("Choose Region or Status.");
        var definition = Orders.Aggregates.GroupBy<string>("." + group).AddKey().AddCount().AddSum(order => order.Total).AddAverage(order => order.Total);
        using var reader = Orders.Aggregates.Open(definition);
        var results = new List<object>();
        while (reader.Read())
        {
            var row = reader.Get();
            using var calculation = ActionTiming.Calculate();
            results.Add(new { key = row.Value1, count = row.Value2, total = row.Value3, average = row.Value4 });
        }
        return results;
    }

    /// <summary>Routes a bounded grid request to its concrete typed store, suppressing graph hydration.<br/></summary>
    internal object Page(string entity, int offset, string query, string sort = "", bool descending = false, string searchField = "", string searchMode = "contains", bool ignoreCase = true)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Page offsets cannot be negative.");
        query = query.Trim();
        SearchDatasetDefinition dataset = GetSearchDataset(entity);
        searchField = string.IsNullOrWhiteSpace(searchField) ? DefaultSearchField(entity) : searchField;
        string[] allowed = entity switch { "orders" => ["Number", "OrderedAt", "Total"], "customers" => ["Name", "CreditLimit"], "products" => ["Name", "Price"], "inventory" => ["SKU", "OnHand"], "lines" => ["SKU", "Total"], _ => [] };
        if (sort.Length > 0 && !allowed.Contains(sort)) throw new ArgumentException("Unsupported sort field.");
        return entity switch
        {
            "orders" => ReadGridPage(Orders, dataset, offset, query, searchField, searchMode, ignoreCase, sort, descending, allowed),
            "customers" => ReadGridPage(Customers, dataset, offset, query, searchField, searchMode, ignoreCase, sort, descending, allowed),
            "products" => ReadGridPage(Products, dataset, offset, query, searchField, searchMode, ignoreCase, sort, descending, allowed),
            "inventory" => ReadGridPage(Inventory, dataset, offset, query, searchField, searchMode, ignoreCase, sort, descending, allowed),
            "lines" => ReadGridPage(Lines, dataset, offset, query, searchField, searchMode, ignoreCase, sort, descending, allowed),
            _ => throw new ArgumentException("Unknown dataset.")
        };
    }

    /// <summary>
    /// Builds one selected text condition and delegates to the common bounded grid reader.<br/>
    /// Empty text deliberately removes the condition while retaining the selected policy for the next user search.<br/>
    /// </summary>
    private static object ReadGridPage<T>(RecordStore<T> store, SearchDatasetDefinition dataset, int offset, string query, string searchField, string searchMode, bool ignoreCase, string sort, bool descending, string[] allowed)
    {
        if (query.Length == 0) return ReadPage(store, offset, null, sort, descending, allowed);
        var search = BuildGridSearch(store, dataset, searchField, searchMode, query, ignoreCase);
        return ReadPage(store, offset, search.Condition, sort, descending, allowed, search.Evidence);
    }

    /// <summary>
    /// Reads at most 51 rows to supply a 50-row page and a continuation indication.<br/>
    /// Filtered ordering adopts the application's exact attached index when available so Abraxas intersects identities without privately sorting every matching payload.<br/>
    /// When grid-search evidence is supplied, an exact native count runs first and remains separately timed from first-result and bounded-page materialization.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete domain type owned by the selected store.<br/></typeparam>
    /// <param name="store">Open store that supplies aggregate, reader, and optional exact sort-index routes.<br/></param>
    /// <param name="offset">Zero-based matching-record offset requested by the UI.<br/></param>
    /// <param name="condition">Optional completed filter condition.<br/></param>
    /// <param name="sort">Optional allow-listed sort property name.<br/></param>
    /// <param name="descending">Whether the selected sort runs in descending order.<br/></param>
    /// <param name="allowedSorts">Allow-listed sort fields exposed by the current grid.<br/></param>
    /// <param name="searchEvidence">Optional single-clause grid-search evidence populated with exact count and reader metrics.<br/></param>
    /// <returns>A bounded scalar page, continuation state, route evidence, and optional completed search evidence.<br/></returns>
    private static object ReadPage<T>(RecordStore<T> store, int offset, Condition<T>.EndCondition? condition, string sort = "", bool descending = false, string[]? allowedSorts = null, GridSearchEvidence? searchEvidence = null)
    {
        ActionTiming.Phase("Abraxas — " + (sort.Length > 0 ? "ordered " : "") + "page at offset " + offset);
        using var work = ActionTiming.Db();
        var sortOptions = (allowedSorts ?? []).Select(field => new { field, indexed = store.TryGetIndex("." + field, out _) }).ToArray();
        if (searchEvidence is not null)
        {
            ActionTiming.Phase("Abraxas — completing exact matching-record count");
            var countTimer = Stopwatch.StartNew();
            searchEvidence.MatchCount = store.Aggregates.Count(condition);
            countTimer.Stop();
            searchEvidence.CountMs = countTimer.Elapsed.TotalMilliseconds;
            ActionTiming.Phase("Abraxas — materializing bounded matching page");
        }
        var readerTimer = searchEvidence is null ? null : Stopwatch.StartNew();
        if (sort.Length > 0)
        {
            RecordStore<T>.SortedReader sorted;
            string route;
            if (store.TryGetIndex("." + sort, out RecordIndex exactIndex))
            {
                // This index is selected because the user explicitly requested its UI order. Do not generalize this branch into
                // the default route for an order-insensitive complete-object scan: an eligible complete index is excellent for
                // identity discovery and selective populations, but dense per-identity payload hydration can cost more than a
                // locality-preserving store traversal. See COVERAGE_INDEX_TRAVERSAL_PROTOTYPE_20260918.md.
                var order = new IndexSort(exactIndex, descending ? SortDirection.Descending : SortDirection.Ascending);
                sorted = condition is null ? store.Readers.OpenSorted(order) : store.Readers.OpenSorted(condition, order);
                route = "Abraxas exact-index sorted reader · links suppressed";
            }
            else
            {
                var order = new RecordStore<T>.SortBy("." + sort, descending: descending);
                sorted = condition is null ? store.Readers.OpenSorted(order) : store.Readers.OpenSorted(condition, order);
                route = "Abraxas prepared sorted reader · links suppressed";
            }
            using (sorted)
            {
                sorted.Skip(offset);
                var rows = new List<object>(50);
                while (rows.Count < 50 && sorted.ReadNoLinks())
                {
                    var value = sorted.GetNoLinks(); var id = sorted.ID;
                    if (searchEvidence is not null && searchEvidence.FirstResultMs is null)
                        searchEvidence.FirstResultMs = readerTimer!.Elapsed.TotalMilliseconds;
                    using var calculation = ActionTiming.Calculate();
                    rows.Add(new { id = id.ToString(CultureInfo.InvariantCulture), value });
                }
                bool sortedMore = sorted.ReadNoLinks();
                readerTimer?.Stop();
                if (searchEvidence is not null) searchEvidence.ReaderMs = readerTimer!.Elapsed.TotalMilliseconds;
                return new { rows, offset, more = sortedMore, sortOptions, route, search = searchEvidence };
            }
        }
        using var reader = condition is null ? store.Readers.Open() : store.Readers.Open(condition);
        reader.Skip(offset);
        var result = new List<object>(50);
        while (result.Count < 50 && reader.ReadNoLinks())
        {
            var value = reader.GetNoLinks(); var id = reader.ID;
            if (searchEvidence is not null && searchEvidence.FirstResultMs is null)
                searchEvidence.FirstResultMs = readerTimer!.Elapsed.TotalMilliseconds;
            using var calculation = ActionTiming.Calculate();
            result.Add(new { id = id.ToString(CultureInfo.InvariantCulture), value });
        }
        bool more = reader.ReadNoLinks();
        readerTimer?.Stop();
        if (searchEvidence is not null) searchEvidence.ReaderMs = readerTimer!.Elapsed.TotalMilliseconds;
        return new { rows = result, offset, more, sortOptions, route = "Abraxas reader · scalar page · links suppressed", search = searchEvidence };
    }

    /// <summary>
    /// Reads one natural order object whose line collection is composed by Abraxas/Inheto from the separate normalized line store.<br/>
    /// The ordinary <c>Orders.Get(...)</c> call returns only after its registered <c>.Lines</c> property-path reader has completed and the collection has been assigned.<br/>
    /// The customer projection and scalar save revision remain explicit because this response deliberately returns them beside, rather than inside, <see cref="SalesOrder"/>.<br/>
    /// The host holds its single-writer gate for the whole operation, so displayed values and revision belong to the same application snapshot.<br/>
    /// </summary>
    /// <param name="id">Order identity to materialize.<br/></param>
    /// <returns>A UI projection containing the Abraxas-composed order plus its separately requested customer and revision.<br/></returns>
    internal object OrderDetail(ulong id)
    {
        ActionTiming.Phase("Abraxas — hydrating order, customer and packed lines");
        using var work = ActionTiming.Db();
        // At this line Abraxas/Inheto has already populated order.Lines from the separate
        // order-lines store through the registered .Lines property-path deserializer.
        SalesOrder order = Orders.Get(id);
        using var customer = Customers.Readers.Open(new[] { order.CustomerID });
        if (!customer.ReadNoLinks()) throw new InvalidDataException("Order customer is missing.");
        return new { id = id.ToString(CultureInfo.InvariantCulture), order, revision = Revision(ReadScalar(Orders, id)), customer = customer.GetNoLinks(), route = "Orders.Get → Abraxas/Inheto composes .Lines from the separate line store · no separate ORM" };
    }

    /// <summary>Updates an allowed business field while preserving existing packed relationship payloads.<br/></summary>
    internal void UpdateOrderStatus(ulong id, string status)
    {
        if (status is not ("Pending" or "Processing" or "Shipped" or "Delivered" or "Cancelled")) throw new ArgumentException("Invalid order status.");
        SalesOrder order;
        using var work = ActionTiming.Db();
        using (var reader = Orders.Readers.Open(new[] { id }))
        {
            if (!reader.ReadNoLinks()) throw new KeyNotFoundException("Order is missing.");
            order = reader.GetNoLinks();
        }
        order.Status = status;
        Orders.PutNoLinks(id, order);
    }

    /// <summary>Closes the application's owned database and its public store facades.<br/></summary>
    public void Dispose() => database.Dispose();
}
