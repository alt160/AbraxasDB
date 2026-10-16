using AbraxasDB;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace AbraxasShowcase;

/// <summary>Provides type-agnostic store discovery, paging, preview, and hydration for the group data browser.<br/></summary>
internal sealed partial class ShowcaseWorkspace
{
    internal GroupDataBrowser BrowseActiveGroup(int sampleLimit)
    {
        LaboratoryManifest manifest = manifests.Get(manifestID);
        ScenarioGroupDefinition group = ScenarioGroupCatalog.Get(manifest.GroupID);
        GroupStoreData[] stores = group.Id switch
        {
            "recordbase-foundations" =>
            [
                BrowseStore("customers", Customers, sampleLimit),
                BrowseStore("orders", Orders, sampleLimit)
            ],
            "projection-and-values" =>
            [
                BrowseStore("customers", Customers, sampleLimit),
                BrowseStore("orders", Orders, sampleLimit),
                BrowseStore("path-graphs", PathGraphs, sampleLimit)
            ],
            "stores" or "retrieval-and-readers" or "conditions-and-indexes" or "memory-backed-indexes" or "search-and-lookup" or "relationships-and-aggregates" or "mutation-and-lifecycle" or "scale-and-performance" =>
            [
                BrowseStore("customers", Customers, sampleLimit),
                BrowseStore("orders", Orders, sampleLimit)
            ],
            "object-shapes-identity" =>
            [
                BrowseStore("customer-profiles", Profiles, sampleLimit),
                BrowseStore("identity-free-notes", Notes, sampleLimit)
            ],
            "shape-and-materialization" => [BrowseStore("hook-documents", HookDocuments, sampleLimit)],
            "object-graphs-across-stores" =>
            [
                BrowseStore("graph-customers", GraphCustomers, sampleLimit),
                BrowseStore("graph-orders", GraphOrders, sampleLimit),
                BrowseStore("graph-lines", GraphLines, sampleLimit)
            ],
            _ => []
        };
        return new GroupDataBrowser { Group = group.Title, RecordBaseState = manifest.State, Stores = stores };
    }

    /// <summary>Returns the active group's store and first-level member choices without reading any stored record.<br/></summary>
    /// <returns>Ordered store descriptors for the connected group page.<br/></returns>
    internal GroupStoreDescriptor[] DescribeActiveGroupStores()
    {
        LaboratoryManifest manifest = manifests.Get(manifestID);
        return manifest.GroupID switch
        {
            "recordbase-foundations" =>
            [
                DescribeStore("customers", Customers),
                DescribeStore("orders", Orders)
            ],
            "projection-and-values" =>
            [
                DescribeStore("customers", Customers),
                DescribeStore("orders", Orders),
                DescribeStore("path-graphs", PathGraphs)
            ],
            "stores" or "retrieval-and-readers" or "conditions-and-indexes" or "memory-backed-indexes" or "search-and-lookup" or "relationships-and-aggregates" or "mutation-and-lifecycle" or "scale-and-performance" =>
            [
                DescribeStore("customers", Customers),
                DescribeStore("orders", Orders)
            ],
            "object-shapes-identity" =>
            [
                DescribeStore("customer-profiles", Profiles),
                DescribeStore("identity-free-notes", Notes)
            ],
            "shape-and-materialization" => [DescribeStore("hook-documents", HookDocuments)],
            "object-graphs-across-stores" =>
            [
                DescribeStore("graph-customers", GraphCustomers),
                DescribeStore("graph-orders", GraphOrders),
                DescribeStore("graph-lines", GraphLines)
            ],
            _ => []
        };
    }

    /// <summary>
    /// Reads one explicit page of identities and selected runtime values without hydrating the stored domain objects.<br/>
    /// The implementation deliberately uses Abraxas dynamic value access so callers do not supply a property value type.<br/>
    /// </summary>
    /// <param name="storeName">Registered store belonging to the active scenario group.<br/></param>
    /// <param name="propertyPaths">One or two validated first-level paths selected in the UI.<br/></param>
    /// <param name="pageIndex">Zero-based page requested by the user.<br/></param>
    /// <param name="pageSize">Bounded number of rows returned.<br/></param>
    /// <returns>Identity-aware dynamic values plus explicit paging state.<br/></returns>
    internal GroupBrowserPage BrowseActiveStore(string storeName, string[] propertyPaths, int pageIndex, int pageSize)
    {
        LaboratoryManifest manifest = manifests.Get(manifestID);
        return (manifest.GroupID, storeName) switch
        {
            ("recordbase-foundations", "customers") => BrowseStorePage(manifest.GroupID, storeName, Customers, propertyPaths, pageIndex, pageSize),
            ("recordbase-foundations", "orders") => BrowseStorePage(manifest.GroupID, storeName, Orders, propertyPaths, pageIndex, pageSize),
            ("stores", "customers") => BrowseStorePage(manifest.GroupID, storeName, Customers, propertyPaths, pageIndex, pageSize),
            ("stores", "orders") => BrowseStorePage(manifest.GroupID, storeName, Orders, propertyPaths, pageIndex, pageSize),
            ("retrieval-and-readers", "customers") => BrowseStorePage(manifest.GroupID, storeName, Customers, propertyPaths, pageIndex, pageSize),
            ("retrieval-and-readers", "orders") => BrowseStorePage(manifest.GroupID, storeName, Orders, propertyPaths, pageIndex, pageSize),
            ("conditions-and-indexes" or "memory-backed-indexes" or "search-and-lookup" or "projection-and-values" or "relationships-and-aggregates" or "mutation-and-lifecycle" or "scale-and-performance", "customers") => BrowseStorePage(manifest.GroupID, storeName, Customers, propertyPaths, pageIndex, pageSize),
            ("conditions-and-indexes" or "memory-backed-indexes" or "search-and-lookup" or "projection-and-values" or "relationships-and-aggregates" or "mutation-and-lifecycle" or "scale-and-performance", "orders") => BrowseStorePage(manifest.GroupID, storeName, Orders, propertyPaths, pageIndex, pageSize),
            ("projection-and-values", "path-graphs") => BrowseStorePage(manifest.GroupID, storeName, PathGraphs, propertyPaths, pageIndex, pageSize),
            ("object-shapes-identity", "customer-profiles") => BrowseStorePage(manifest.GroupID, storeName, Profiles, propertyPaths, pageIndex, pageSize),
            ("object-shapes-identity", "identity-free-notes") => BrowseStorePage(manifest.GroupID, storeName, Notes, propertyPaths, pageIndex, pageSize),
            ("shape-and-materialization", "hook-documents") => BrowseStorePage(manifest.GroupID, storeName, HookDocuments, propertyPaths, pageIndex, pageSize),
            ("object-graphs-across-stores", "graph-customers") => BrowseStorePage(manifest.GroupID, storeName, GraphCustomers, propertyPaths, pageIndex, pageSize),
            ("object-graphs-across-stores", "graph-orders") => BrowseStorePage(manifest.GroupID, storeName, GraphOrders, propertyPaths, pageIndex, pageSize),
            ("object-graphs-across-stores", "graph-lines") => BrowseStorePage(manifest.GroupID, storeName, GraphLines, propertyPaths, pageIndex, pageSize),
            _ => throw new ArgumentException("Choose a store belonging to the active scenario group.", nameof(storeName))
        };
    }

    /// <summary>Hydrates exactly one requested record for deferred object-explorer inspection.<br/></summary>
    /// <param name="storeName">Registered store belonging to the active scenario group.<br/></param>
    /// <param name="recordID">External Abraxas identity selected by the user.<br/></param>
    /// <returns>The actual hydrated object and its declared store type.<br/></returns>
    internal (object Value, Type DeclaredType) GetActiveGroupRecord(string storeName, ulong recordID)
    {
        LaboratoryManifest manifest = manifests.Get(manifestID);
        return (manifest.GroupID, storeName) switch
        {
            ("recordbase-foundations", "customers") => (Customers.Get(recordID)!, typeof(ShowcaseCustomer)),
            ("recordbase-foundations", "orders") => (Orders.Get(recordID)!, typeof(ShowcaseOrder)),
            ("stores", "customers") => (Customers.Get(recordID)!, typeof(ShowcaseCustomer)),
            ("stores", "orders") => (Orders.Get(recordID)!, typeof(ShowcaseOrder)),
            ("retrieval-and-readers", "customers") => (Customers.Get(recordID)!, typeof(ShowcaseCustomer)),
            ("retrieval-and-readers", "orders") => (Orders.Get(recordID)!, typeof(ShowcaseOrder)),
            ("conditions-and-indexes" or "memory-backed-indexes" or "search-and-lookup" or "projection-and-values" or "relationships-and-aggregates" or "mutation-and-lifecycle" or "scale-and-performance", "customers") => (Customers.Get(recordID)!, typeof(ShowcaseCustomer)),
            ("conditions-and-indexes" or "memory-backed-indexes" or "search-and-lookup" or "projection-and-values" or "relationships-and-aggregates" or "mutation-and-lifecycle" or "scale-and-performance", "orders") => (Orders.Get(recordID)!, typeof(ShowcaseOrder)),
            ("projection-and-values", "path-graphs") => (PathGraphs.Get(recordID)!, typeof(ShowcasePathGraph)),
            ("object-shapes-identity", "customer-profiles") => (Profiles.Get(recordID)!, typeof(ShowcaseCustomerProfile)),
            ("object-shapes-identity", "identity-free-notes") => (Notes.Get(recordID)!, typeof(ShowcaseIdentityFreeNote)),
            ("shape-and-materialization", "hook-documents") => (HookDocuments.Get(recordID)!, typeof(ShowcaseHookDocument)),
            ("object-graphs-across-stores", "graph-customers") => (GraphCustomers.Get(recordID)!, typeof(ShowcaseGraphCustomer)),
            ("object-graphs-across-stores", "graph-orders") => (GraphOrders.Get(recordID)!, typeof(ShowcaseGraphOrder)),
            ("object-graphs-across-stores", "graph-lines") => (GraphLines.Get(recordID)!, typeof(ShowcaseGraphLine)),
            _ => throw new ArgumentException("Choose a store belonging to the active scenario group.", nameof(storeName))
        };
    }

    /// <summary>Closes the active RecordBase and releases its files.<br/></summary>
    private static GroupStoreData BrowseStore<T>(string name, RecordStore<T> store, int sampleLimit)
    {
        var samples = new List<object>(sampleLimit);
        using var reader = store.Readers.Open();
        while (samples.Count < sampleLimit && reader.Read()) samples.Add(reader.Get()!);
        return new GroupStoreData
        {
            Name = name,
            RecordType = typeof(T).FullName ?? typeof(T).Name,
            TotalRecords = store.Aggregates.Count(),
            SampleRecords = samples.ToArray()
        };
    }

    /// <summary>Builds browser member choices from the store's ordinary public first-level object shape.<br/></summary>
    private static GroupStoreDescriptor DescribeStore<T>(string name, RecordStore<T> store)
    {
        GroupStoreProperty[] properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .Select(property => new GroupStoreProperty(property.Name, TypeNameFormatter.Format(property.PropertyType), IsScalar(property.PropertyType)))
            .Concat(typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => new GroupStoreProperty(field.Name, TypeNameFormatter.Format(field.FieldType), IsScalar(field.FieldType))))
            .ToArray();
        return new GroupStoreDescriptor(name, TypeNameFormatter.Format(typeof(T)), store.Aggregates.Count(), properties);
    }

    /// <summary>Reads one page through the identity-aware runtime-shaped Abraxas value reader.<br/></summary>
    private static GroupBrowserPage BrowseStorePage<T>(
        string groupID,
        string storeName,
        RecordStore<T> store,
        string[] propertyPaths,
        int pageIndex,
        int pageSize)
    {
        if (propertyPaths is null || propertyPaths.Length is < 1 or > 2)
            throw new ArgumentException("Choose one or two preview properties.", nameof(propertyPaths));
        if (propertyPaths.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Preview property paths cannot be empty.", nameof(propertyPaths));
        pageIndex = Math.Max(0, pageIndex);
        pageSize = Math.Clamp(pageSize, 10, 100);
        int skip = checked(pageIndex * pageSize);
        ulong total = store.Aggregates.Count();
        var rows = new List<GroupBrowserRow>(pageSize);
        using RecordStore<T>.ValueDataReaderWithIds reader = store.Readers.OpenValuesWithIds(propertyPaths);
        reader.Skip(skip);
        while (rows.Count < pageSize && reader.Read())
        {
            var values = new GroupBrowserValue[propertyPaths.Length];
            for (int ordinal = 0; ordinal < values.Length; ordinal++)
            {
                object? value = UnwrapDynamicValue(reader.GetObject(ordinal));
                values[ordinal] = new GroupBrowserValue(reader.GetPath(ordinal), TypeNameFormatter.Format(value?.GetType() ?? typeof(object)), FormatPreview(value));
            }
            rows.Add(new GroupBrowserRow(reader.ID.ToString(CultureInfo.InvariantCulture), values));
        }
        ulong consumed = (ulong)skip + (ulong)rows.Count;
        return new GroupBrowserPage(groupID, storeName, TypeNameFormatter.Format(typeof(T)), total, pageIndex, pageSize, pageIndex > 0, consumed < total, propertyPaths, rows.ToArray());
    }

    /// <summary>Classifies member types that make useful concise defaults in the row preview.<br/></summary>
    private static bool IsScalar(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual.IsPrimitive || actual.IsEnum || actual == typeof(string) || actual == typeof(decimal)
            || actual == typeof(DateTime) || actual == typeof(DateTimeOffset) || actual == typeof(DateOnly)
            || actual == typeof(TimeOnly) || actual == typeof(TimeSpan) || actual == typeof(Guid) || actual == typeof(Uri);
    }

    /// <summary>Produces an unquoted display value while preserving the runtime type in a separate field.<br/></summary>
    private static string FormatPreview(object? value) => value switch
    {
        null => "null",
        string text => text,
        char character => character.ToString(),
        bool boolean => boolean ? "true" : "false",
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        ICollection collection => $"{collection.Count:N0} items",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    /// <summary>
    /// Extracts the runtime value carried by an Inheto agnostic scalar without declaring its generic value type.<br/>
    /// Complex agnostic nodes remain intact so their shape is not falsely presented as an ordinary hydrated domain object.<br/>
    /// </summary>
    /// <param name="value">Runtime-shaped value returned by <c>ValueDataReaderWithIds.GetObject</c>.<br/></param>
    /// <returns>The scalar payload when the runtime envelope exposes one; otherwise the original runtime-shaped value.<br/></returns>
    private static object? UnwrapDynamicValue(object? value)
    {
        if (value is null) return null;
        Type runtimeType = value.GetType();
        if (runtimeType.Namespace != "Inheto" || !runtimeType.Name.StartsWith("InhetoObject`", StringComparison.Ordinal)) return value;
        return runtimeType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
    }

    /// <summary>
    /// Writes the minimum non-RecordBase metadata needed to identify a closed Showcase environment without opening it.<br/>
    /// The sidecar contains no records or user data and is replaced only after the durable laboratory manifest is updated.<br/>
    /// </summary>
    /// <param name="manifest">Persisted laboratory manifest whose group identity and setup version were just committed.<br/></param>
}
