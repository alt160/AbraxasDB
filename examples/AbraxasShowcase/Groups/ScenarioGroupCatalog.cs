using AbraxasDB;
using System.Reflection;

namespace AbraxasShowcase;

/// <summary>One scenario-group setup contract that owns an entire replaceable RecordBase environment unit.<br/></summary>
internal sealed record ScenarioGroupDefinition(
    string Id,
    string Title,
    string Description,
    int SetupVersion,
    string PrimaryCountLabel,
    string RelatedCountLabel,
    int DefaultPrimaryCount,
    int DefaultRelatedCount,
    string SetupSource,
    string GenerationSource,
    bool ReplacesEnvironmentPerScenario,
    IReadOnlyList<ScenarioGroupConcept> Concepts,
    Action<string> CreateEnvironment,
    Action<string, GenerationRequest, Action<int, int, string>?, CancellationToken> GenerateData)
{
    /// <summary>Marks a group whose examples require the commercial Abraxas payload APIs.<br/></summary>
    internal bool CommercialOnly { get; init; }
}

/// <summary>One Abraxas term introduced on a scenario-group page before its examples are run.<br/></summary>
/// <param name="Term">Developer-facing term displayed as the concept-card title.<br/></param>
/// <param name="Meaning">Concise definition that remains readable without opening secondary detail.<br/></param>
/// <param name="DetailIntroduction">Optional framing paragraph for the reusable concept-detail lightbox.<br/></param>
/// <param name="Details">Ordered detail blocks; an empty collection intentionally suppresses the Learn More action.<br/></param>
internal sealed record ScenarioGroupConcept(
    string Term,
    string Meaning,
    string DetailIntroduction,
    IReadOnlyList<ScenarioConceptDetail> Details,
    string? SharedFileBaseName = null);

/// <summary>One reusable explanatory block rendered inside a concept-detail lightbox.<br/></summary>
internal sealed record ScenarioConceptDetail(string Title, string Description);

/// <summary>Ordered registry for the environment-owning scenario groups displayed in the rail and catalog.<br/></summary>
internal static class ScenarioGroupCatalog
{
    internal static IReadOnlyList<ScenarioGroupDefinition> All { get; } =
    [
        new(
            "recordbase-fundamentals",
            "Recordbase Fundamentals",
            "Start with the jobs you may know from SQL or document stores: open a place for records, add stores, and choose how to find them. Then see where Abraxas works differently.",
            1,
            "Environment examples",
            "RecordBases per run",
            1,
            1,
            "",
            "",
            true,
            [
                new(
                    "RecordBase",
                    "The named set of files Abraxas opens together for your stores and indexes.",
                    "A RecordBase is identified by a directory and a name. The name becomes the shared filename stem for the files below. For example, a RecordBase named showcase uses showcase.fractD, showcase.fractX, showcase.idgen, and showcase.lbdx in the selected directory.",
                    [
                        new(".fractD", "Stores the Inheto records and RecordBase metadata in FractalKVS."),
                        new(".fractX", "Maps RecordIDs to locations in .fractD. Abraxas can rebuild it from .fractD."),
                        new(".fractD.shm", "Stores the persisted counters and coordination values used with .fractD."),
                        new(".fractX.shm", "Stores the persisted counters, allocation values, and coordination values used with .fractX."),
                        new(".idgen", "Stores the monotonic RecordID counters for the RecordBase's stores."),
                        new(".lbdx", "Stores the LibraDex catalog and durable ordered indexes for every store in the RecordBase."),
                        new("Temporary files", "Rebuild, compaction, backup, or recovery work may create temporary sibling files. They still belong to this RecordBase and are not separate RecordBases.")
                    ],
                    "showcase"),
                new(
                    "RecordStore<T>",
                    "The named home for records you normally read and write as T.",
                    "Think of a collection of customer records, but without requiring a customer-shaped result every time. Inheto stores member paths and values, not CLR type information in each record. Normal reads use T; other reads can fill a different .NET type from matching paths or return just the paths and values you ask for.",
                    [
                        new("Normal .NET type", "Get reads the stored member paths and values into T."),
                        new("Different .NET type", "GetAs<TOut> fills the properties and fields on TOut whose paths match paths in the stored record."),
                        new("No declared type required", "Dynamic readers expose stored paths and values without asking the caller for a CLR type."),
                        new("Only the values you need", "Value readers return selected member paths without rebuilding the complete object.")
                    ]),
                new(
                    "RecordID",
                    "The UInt64 storage ID Abraxas gives each newly inserted record.",
                    "Like a generated primary key, a RecordID lets you get a record again. Unlike an ID property in many object models, it can stay outside your .NET object. Put allocates it from a persisted per-store counter and returns it; configure ObjectIDSetter only if you also want the value written into the object before storage.",
                    [
                        new("Generated at insert", "Put allocates the RecordID before it serializes and stores a new record."),
                        new("Monotonic, not gap-free", "Each store's counter moves forward. Reserved blocks can leave unused values, so gaps are normal."),
                        new("Store and record in one value", "The upper 8 bits identify the store. The lower 56 bits contain that store's monotonic counter."),
                        new("Object synchronization is optional", "RecordStore<T>.ObjectIDSetter writes the generated ID into your object before serialization when you configure it."),
                        new("Business IDs still work", "Index any unique business property when you want to find records by that value."),
                        new("RecordBase scope", "Use a RecordID as storage identity inside its RecordBase, not as a globally meaningful business ID.")
                    ]),
                new(
                    "Index",
                    "An optional shortcut for finding or walking records by a value other than RecordID.",
                    "If you know a RecordID, you can fetch its record without another index, much like a key-value lookup. Add an index when you often search by a member such as Code or need that member's key order. Each maintained index also adds write and storage work.",
                    [
                        new("Durable indexes", "These indexes remain available after the RecordBase closes and reopens. Their data lives in the .lbdx file."),
                        new("Memory-only indexes", "These indexes live only in memory and disappear when the store closes."),
                        new("Property paths", "A string path can be chosen at runtime and can identify admitted members or parameterless method continuations. Equivalent fixed chains may also be written as lambdas."),
                        new("Calculated and composite keys", "A calculated index stores a key derived from values. A composite key index keeps two or more parts separate and ordered instead of joining them into one value."),
                        new("Text subindexes", "Folded keys help no-case matching and reversed keys help suffix matching. Neither is the index's physical ascending or descending order."),
                        new("Sensitive data caveat", "A memory-only index disappears on close. The indexed value does not disappear from a durable record that already contains it."),
                        new("Value iteration", "Select several typed member values per row and combine them in a developer-defined struct. Avoiding a row object does not guarantee the current reader pipeline makes no allocations."),
                        new("No index required", "RecordID reads and supported record scans still work when you create no indexes."),
                        new("Business-key lookup", "Index a domain member such as CustomerNumber when you want to find records by it."),
                        new("Key order", "An ordered index can return records in its key order without a separate sort for that same order.")
                    ]),
                new(
                    "Composite key index",
                    "One named index combines two or more values into an ordered key without requiring a combined property on your object.",
                    "Think of a SQL multi-column index for a customer's FirstName and LastName, but do not picture two independent indexes or a concatenated string. LibraDex maintains one composite access path whose key retains each value as a separate, ordered part. Abraxas can use a prefix condition on FirstName together with an exact condition on LastName. Its typed composite reader then walks the persisted index directly, returning ordered key values and RecordIDs without loading customer objects. More advanced parts can also come from admitted paths or calculations.",
                    [
                        new("Part order matters", "FirstName then LastName and LastName then FirstName have different key order. Give each definition its own name."),
                        new("Conditions stay separate", "Write a condition on each property, including a prefix on one part and an exact value on another. Abraxas matches those predicates to the index parts; a combined property and delimiter rules are unnecessary."),
                        new("Covered value reads", "Use Readers.OpenCompositeValues<string, string>(\"display-name\") to walk the persisted key as typed Value1/Value2 plus RecordID, without opening the record payload. Scalar values decode without per-row boxing; requesting string values creates managed strings. GetUtf8Span(0/1) borrows UTF-8 when you only need bytes. The older OpenValuesReaderWithIds(index, paths) remains useful when you need runtime-selected paths or lazy fallback to non-indexed record values."),
                        new("One named definition", "CreateCompositeIndex gives the ordered parts a logical name. Reusing that name with different parts is a configuration conflict, not an automatic rebuild."),
                        new("Comparison rules matter", "An exact-case Region key cannot stand in for a case-insensitive Region request. The member names can match while the requested comparison does not."),
                        new("String paths are possible", "CreateCompositeIndex takes selector parts, not raw strings. For a path selected at runtime, build a part with Selector<T>.PropPath(path), choose its result type such as AsString, then call ForIndexing()."),
                        new("Maintained access path", "Like other indexes, the composite key is updated as records change. Add it for a recurring access pattern whose read benefit justifies write and storage cost.")
                    ]),
                new("RecordBase lifetime", "Keep a RecordBase and its stores open for the work they serve; dispose them together when that context ends.",
                    "If you come from ADO.NET, do not treat a RecordBase like a connection you must close after every command. You can open one for a short task, or keep it and its stores open while an application context uses them. The later groups use the second pattern.",
                    [new("Short task", "Open, do the bounded work, then close. The create-and-reopen example intentionally shows this."),
                     new("Application context", "Open once, perform many operations over time, then close when the owning service or application context ends."),
                     new("Store lifetime", "Do not use a store after its owning RecordBase has been disposed. Register instance-scoped hooks when a new store instance opens.")]),
                new("CreateOrOpen", "Creates the named RecordBase when its files are missing, or opens the existing files when they are present.", "", []),
                new("Showcase folder", "The folder reserved for these examples. The Showcase deletes only its own prior example files before the next fundamentals run.", "", [])
            ],
            static _ => { },
            static (_, _, _, _) => { }),
        new(
            "recordbase-foundations",
            "Record persistence basics",
            "Keep customers and orders in separate stores, then insert and retrieve ordinary .NET objects by their Abraxas RecordIDs.",
            1,
            "Customers",
            "Orders per customer",
            100,
            1,
            ExampleSource.Read("RecordPersistence/Environment.cs.txt"),
            ExampleSource.Read("RecordPersistence/Generation.cs.txt"),
            false,
            [],
            RecordBaseFoundationsGroupSetup.CreateEnvironment,
            static (directory, request, progress, token) => RecordBaseFoundationsGroupSetup.GenerateDeterministicData(
                directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token)),
        new(
            "object-shapes-identity",
            "Object shapes and identity",
            "Try the .NET shapes you might expect to need mapping for—nested objects, collections, and framework values—without adding persistence attributes or conversion code.",
            1,
            "Profiles",
            "Notes per profile",
            24,
            1,
            ExampleSource.Read("ObjectIdentity/Environment.cs.txt"),
            ExampleSource.Read("ObjectIdentity/Generation.cs.txt"),
            false,
            [
                new("Ordinary .NET shapes", "Start with your existing class. These examples save its members without persistence attributes or a separate mapping contract.", "", []),
                new("Framework values", "Guid, date and time values, Uri, and Version can live alongside your own members in the stored object.", "", []),
                new("Collections", "Dictionary keys and HashSet membership survive a write and read; do not assume a set has a meaningful order.", "", []),
                new("External identity", "Your profile or note does not need an ID property. Keep its Abraxas RecordID beside the object, or opt in to copying that ID into it.", "", [])
            ],
            ObjectShapesIdentityGroupSetup.CreateEnvironment,
            static (directory, request, progress, token) => ObjectShapesIdentityGroupSetup.GenerateDeterministicData(
                directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token)),
        new(
            "stores",
            "Stores",
            "See what a named store owns, inspect its saved definition, count its records without reading them all, and choose whether to observe store events.",
            1,
            "Customers",
            "Orders per customer",
            100,
            1,
            ExampleSource.Read("Stores/Environment.cs.txt"),
            ExampleSource.Read("Stores/Generation.cs.txt"),
            false,
            [
                new("Store definition", "Like inspecting a collection's settings, you can inspect a store's name, description, declared type, state, and indexes. This store-level information is separate from the values stored inside each record.", "", []),
                new("Store population", "Aggregates.Count returns the maintained record count without rebuilding every .NET object.", "", []),
                new("Store events", "Subscribe when your application needs to react as objects are read, written, or deleted; normal store use does not require event handlers.", "", [])
            ],
            StoresGroupSetup.CreateEnvironment,
            static (directory, request, progress, token) => StoresGroupSetup.GenerateDeterministicData(
                directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token)),
        new(
            "retrieval-and-readers",
            "Retrieval and readers",
            "When one-record lookup is not enough, walk results one at a time and stop as soon as your code has what it needs.",
            1,
            "Customers",
            "Orders per customer",
            100,
            1,
            ExampleSource.Read("Retrieval/Environment.cs.txt"),
            ExampleSource.Read("Retrieval/Generation.cs.txt"),
            false,
            [
                new("Reader", "A way to process results one at a time instead of loading them all first.", "Like a forward-only result reader, call Read to advance, then Get and ID for the current object and RecordID. Dispose it when finished. You can stop early.", []),
                new("Natural order", "The order a reader happens to return when you did not request a sort.", "Do not treat natural order as insertion order, RecordID order, or index order. If your application needs a stable order, request it explicitly.", [])
            ],
            RecordBaseFoundationsGroupSetup.CreateEnvironment,
            static (directory, request, progress, token) => RecordBaseFoundationsGroupSetup.GenerateDeterministicData(
                directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token)),
        CustomerOrderGroup("conditions-and-indexes", "Conditions and indexes", "Filter records by familiar business values, see when an index helps, and request a specific result order when it matters."),
        new(
            "memory-backed-indexes",
            "Memory-backed indexes",
            "Build useful lookup paths for the current store session without adding durable index definitions. Compare temporary paths, derived keys, composite values, and selective membership.",
            1, "Customers", "Orders per customer", 24, 1,
            ExampleSource.Read("MemoryIndexes/Environment.cs.txt"),
            ExampleSource.Read("MemoryIndexes/Generation.cs.txt"), false,
            [
                new("Memory-only index", "A lookup built from existing durable records that remains available only while its store is open.",
                    "Use one for a session-specific query or computation. Its keys and definition disappear when the store closes, so build it again after reopening if you need it then.",
                    [new("Build cost", "Creating it scans existing records and occupies memory; it is not a free alternative to a durable index."),
                     new("Write maintenance", "Later record changes maintain the live index until its store closes."),
                     new("Privacy boundary", "A memory-only index avoids a durable index copy of a key, but does not remove that value from the stored record, memory, swap, or backups.")]),
                new("Temporary composite key", "One memory-only index can retain several separately typed key parts for a session, not a concatenated string.", "Use a typed composite reader to get those parts and RecordIDs without hydrating full records.", []),
                new("Partial membership", "A condition chosen when creating an index limits which records enter it.", "This is useful when a session repeatedly searches only one relevant subset. Changing that condition requires a new index definition; it is not a dynamic per-query filter.", [])
            ],
            MemoryIndexGroupSetup.CreateEnvironment,
            static (directory, request, progress, token) => MemoryIndexGroupSetup.GenerateDeterministicData(directory, request.CustomerCount, request.OrdersPerCustomer, progress, token)),
        CustomerOrderGroup("search-and-lookup", "Search and lookup", "Compare three familiar searches: a date range, a text pattern, and a direct lookup by RecordID."),
        CustomerOrderGroup("projection-and-values", "Projection and value reads", "Read selected stored paths without building an object, continue those paths through properties and methods, or fill a different .NET shape."),
        new(
            "shape-and-materialization", "Shape and materialization",
            "Need to omit a value or control how it is saved and rebuilt? Try exclusions, custom converters, and object creation one step at a time.",
            1, "Documents", "Type-hook tokens per document", 24, 2,
            ExampleSource.Read("Hooks/Environment.cs.txt"), ExampleSource.Read("Hooks/Generation.cs.txt"), false,
            [
                new("Custom serialization", "Provide a writer and reader when an application value needs its own stored byte format.", "Start with a type-wide rule when the same value should be handled consistently wherever it appears. Use a path rule when one member needs a different representation. A write-time app setting may select a format, but stored bytes must identify that format so a later read can decode them under different settings.",
                    [new("Type-wide hook", "One serializer/deserializer pair handles that type throughout the store."),
                     new("Property-path hook", "One member path can override the broader type rule."),
                     new("Context and version", "Capture the current choice for a write and store a format marker; do not rely on the same ambient or UI state being present during a future read.")]),
                new("Member-path exclusion", "Have a member you do not want saved? Exclude just that path from the stored record.", "", []),
                new("Type exclusion", "Exclude every value of a chosen .NET type from writes to this store.", "", []),
                new("Type codec", "Supply a writer and reader for a type wherever it appears; a path-specific rule can take priority.", "", []),
                new("Property-path codec", "Supply a writer and reader for one member when its normal stored form is not what you need.", "", []),
                new("Custom activator", "Choose how Abraxas creates a nested .NET object before filling it from stored values.", "", [])
            ],
            MaterializationGroupSetup.CreateEnvironment,
            static (directory, request, progress, token) => MaterializationGroupSetup.GenerateData(directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token)),
        new(
            "object-graphs-across-stores", "Object graphs across stores",
            "Keep customers, orders, and lines in separate stores, then follow their RecordIDs to rebuild only the connected objects a read needs.",
            1, "Customers", "Orders per customer", 24, 2,
            ExampleSource.Read("Graphs/Environment.cs.txt"), ExampleSource.Read("Graphs/Generation.cs.txt"), false,
            [
                new("Packed child identities", "An order keeps its line RecordIDs in order; the line objects themselves stay in the line store.", "", []),
                new("Reverse relationship", "To find a customer's orders, search the indexed CustomerID member on the order store using that customer's RecordID.", "", []),
                new("Link suppression", "Need only an order's own values? Skip rebuilding its related line objects for that read.", "", [])
            ],
            ObjectGraphGroupSetup.CreateEnvironment,
            static (directory, request, progress, token) => ObjectGraphGroupSetup.GenerateData(directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token)),
        CustomerOrderGroup("relationships-and-aggregates", "Relationships and aggregates", "Use stored RecordIDs to find related orders, then count or total stored values without rebuilding every object."),
        CustomerOrderGroup("mutation-and-lifecycle", "Mutation and lifecycle", "Insert, replace, and delete records by RecordID, then check what changed."),
        CustomerOrderGroup("scale-and-performance", "Scale and performance", "Try batches and full-store reads with repeatable data sizes; compare measured work instead of assuming one route is always faster."),
        new ScenarioGroupDefinition(
            "protected-payloads",
            "Protected record payloads",
            "Compare adaptive compression, authenticated encryption, and their combination. These application-record payload features require the commercial Abraxas edition; their examples remain readable in FOSS.",
            1, "Records", "Stores", 1, 1, "", "", true,
            [
                new("Payload compression", "Brotli is attempted for sufficiently large application records and retained only when it saves enough bytes.", "Compression wraps the serialized application record, not its indexes. Small or poorly compressible records may stay raw.",
                    [new("Adaptive", "MinimumPayloadBytes and MinimumSavingsBytes decide whether Abraxas stores the compressed envelope."),
                     new("Telemetry", "Per-store counters show attempts, compressed writes, raw fallbacks, reads, and bytes saved without exposing record contents.")]),
                new("Payload encryption", "AES-256-GCM protects an application record payload with an application-supplied key.", "Key identifiers are stored with encrypted envelopes; the application must safely retain and resolve every key still needed for reads.",
                    [new("Key ownership", "Supply keys through a provider or resolver. A generated one-run demo key is not a production key-management strategy."),
                     new("Authenticated record binding", "The encrypted envelope is tied to the record and store identities; altered or transplanted ciphertext is rejected."),
                     new("Scope", "Payload encryption does not automatically encrypt durable index keys, metadata, filenames, or temporary files.")]),
                new("Combined policies", "Compression and encryption can be enabled together: Abraxas compresses first, then encrypts the stored payload.", "The two settings are independent. Whether compression actually saves space still depends on each record.", []),
                new("Sensitive indexed values", "A memory-only index avoids persisting its lookup keys in LibraDex.", "The value still exists in the durable record payload. Use record encryption as well if the value must not remain plaintext there; memory and swap exposure are separate operational concerns.", [])
            ],
            static _ => { }, static (_, _, _, _) => { }) { CommercialOnly = true }
    ];

    /// <summary>Creates an independently replaceable group over the tested customer/order setup without duplicating its topology or generator.<br/></summary>
    /// <param name="id">Stable group identifier used for saved setup identity.<br/></param>
    /// <param name="title">Group title shared with its runnable scenarios.<br/></param>
    /// <param name="description">Developer-facing purpose of this group.<br/></param>
    /// <returns>A group that owns fresh files and its own setup and browser page.<br/></returns>
    private static ScenarioGroupDefinition CustomerOrderGroup(string id, string title, string description) => new(
        id, title, description, id == "projection-and-values" ? 2 : 1, "Customers", "Orders per customer", 100, 1,
        ExampleSource.Read(id == "projection-and-values" ? "Projection/Environment.cs.txt" : "Retrieval/Environment.cs.txt"),
        ExampleSource.Read(id == "projection-and-values" ? "Projection/Generation.cs.txt" : "Retrieval/Generation.cs.txt"),
        false,
        id switch
        {
            "conditions-and-indexes" =>
            [
                new("Condition", "The Abraxas equivalent of asking for matching records: compare a stored member and combine comparisons when needed.", "", []),
                new("Indexed key", "Find records by a business value such as Code without making that value the RecordID.", "", [])
            ],
            "search-and-lookup" =>
            [
                new("Indexed range", "Find orders in a date window using the index already prepared on OrderedAt.", "", []),
                new("Text pattern", "Match stored text by a pattern. If no suitable index exists, Abraxas can still check the records.", "", []),
                new("Direct identity", "Already have a RecordID? Get that record without searching a business-key field.", "", [])
            ],
            "projection-and-values" =>
            [
                  new("Reprojection", "Like filling a small result DTO, read one stored record into another .NET type. Matching member paths fill its values; unmatched destination members keep their defaults.", "", []),
                  new("Value-only read", "Ask for specific member paths and get their values without rebuilding the full object.", "", []),
                  new("PropPath continuation", "Start at a stored member, then read a property or call a parameterless method on that value.", "A PropPath string such as .Code names a stored member. .Code.Length reads a property of that value; .Code.Trim().Length calls a parameterless, non-void method and then reads a property of its result. Those derived results are evaluated for the read, not stored as extra record fields or automatically indexed.",
                      [new("Choose paths at runtime", "Pass paths chosen by application configuration or UI state to GetValue or GetValues."),
                       new("Keep methods predictable", "A path continuation can execute application or framework code. Prefer pure, inexpensive methods; a method call is not a free stored-field lookup."),
                       new("Indexing is separate", "A direct RecordID value read does not require a secondary index. Index the derived path separately only when searches by that value justify it.")])
            ],
            "relationships-and-aggregates" =>
            [
                new("RecordID relationship", "Store a parent's RecordID on each child, then find the children by that value.", "", []),
                new("Grouped aggregate", "Like a count grouped by status, return one count for each stored order status.", "", [])
            ],
            "mutation-and-lifecycle" =>
            [
                new("Replace by RecordID", "Put an updated object at an existing RecordID to change the stored record.", "", []),
                new("Delete by RecordID", "Delete removes a record; TryGet can verify the same ID no longer resolves.", "", [])
            ],
            "scale-and-performance" =>
            [
                  new("PutManyFast", "Insert a batch of ordinary .NET objects and compare measured runs at different group sizes.", "", []),
                  new("Indexing completion", "The batch handle reports indexing completion; verify indexed selection after the write returns.", "", []),
                new("Unordered reader", "Traverse all objects without paying for an order you do not need.", "", [])
            ],
            _ => []
        },
        id == "projection-and-values" ? ProjectionGroupSetup.CreateEnvironment : RecordBaseFoundationsGroupSetup.CreateEnvironment,
        id == "projection-and-values" ? ProjectionGroupSetup.GenerateData : static (directory, request, progress, token) => RecordBaseFoundationsGroupSetup.GenerateDeterministicData(
            directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token));

    /// <summary>Returns the registered group whose title owns a scenario category.<br/></summary>
    /// <param name="category">Scenario category title emitted by the scenario registry.<br/></param>
    /// <returns>The matching group definition.<br/></returns>
    internal static ScenarioGroupDefinition ForCategory(string category) => All.Single(group => group.Title == category);

    /// <summary>Returns one registered group by stable identifier or fails visibly for an unknown request.<br/></summary>
    /// <param name="id">Stable group identifier supplied by the local UI.<br/></param>
    /// <returns>The matching group definition.<br/></returns>
    internal static ScenarioGroupDefinition Get(string id) => All.SingleOrDefault(group => group.Id == id)
        ?? throw new ArgumentException("Unknown scenario group.");
}

/// <summary>Extracts exact learner-facing group methods from source embedded in the same compilation.<br/></summary>
internal static class GroupSetupSource
{
    /// <summary>Reads one uniquely marked group source region without its marker lines.<br/></summary>
    /// <param name="fileName">Unique compiled group-source filename suffix.<br/></param>
    /// <param name="region">Stable marker name inside that source file.<br/></param>
    /// <returns>The exact compiled source between the requested markers.<br/></returns>
    internal static string Read(string fileName, string region)
    {
        Assembly assembly = typeof(GroupSetupSource).Assembly;
        string[] matches = assembly.GetManifestResourceNames().Where(name => name.EndsWith(fileName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one embedded group source ending in '{fileName}', found {matches.Length}.");
        using Stream stream = assembly.GetManifestResourceStream(matches[0]) ?? throw new InvalidOperationException("Group setup source is unavailable.");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();
        string start = $"// <{region}>";
        string end = $"// </{region}>";
        int startIndex = source.IndexOf(start, StringComparison.Ordinal);
        int endIndex = source.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex || source.IndexOf(start, startIndex + start.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Group source region '{region}' is missing, reversed, or duplicated.");
        return source[(startIndex + start.Length)..endIndex].Trim('\r', '\n');
    }
}
