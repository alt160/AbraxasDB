using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Shows four distinct uses for indexes whose keys and definitions belong only to the current store instance.<br/></summary>
internal static class MemoryIndexScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("memory-runtime-path", "Index a path chosen at runtime", "Build a temporary lookup for a property selected by application configuration.", 435,
            "MemoryIndexes/RuntimePath.cs.txt", "A runtime path can justify a one-session lookup without adding a permanent index to every installation. Build cost and RAM use still matter; direct RecordID reads need no such index.",
            ["ad-hoc lookup", "runtime property path"], ["CreateMemoryIndex", "PropPath", "GetIndex"], RuntimePath),
        New("memory-derived-value", "Index a derived value for this session", "Temporarily order records by the length of a stored name without persisting an extra member or index.", 436,
            "MemoryIndexes/DerivedValue.cs.txt", "The path continuation computes Name.Length while building the index from existing records. Its keys are then reusable during this store session and must be rebuilt after reopen.",
            ["derived key", "temporary ordering"], ["CreateMemoryExpressionIndex", "GetIndex"], DerivedValue),
        New("memory-composite-values", "Read a temporary composite key", "Keep first and last names as separate in-memory key parts and read them without hydrating customers.", 437,
            "MemoryIndexes/CompositeValues.cs.txt", "One volatile composite index covers both typed name parts and their RecordIDs for this session. It avoids a durable composite access path, not the durable customer records.",
            ["composite key", "index-only values"], ["CreateMemoryCompositeIndex", "OpenCompositeValues"], CompositeValues),
        New("memory-partial-lookup", "Index only the relevant subset", "Build a session-only Code lookup containing West customers rather than every customer.", 438,
            "MemoryIndexes/PartialLookup.cs.txt", "A creation-time condition fixes which records enter this temporary index. It reduces indexed membership when only a subset matters; it is not a security boundary or a dynamic query filter.",
            ["partial index", "session-specific subset", "durable payload caveat"], ["CreateMemoryIndex", "GetIndex"], PartialLookup)
    ];

    /// <summary>Registers a runnable memory-index lesson with its exact standalone source and searchable explanation.<br/></summary>
    /// <param name="id">Stable scenario identifier.<br/></param>
    /// <param name="title">Learner-facing scenario title.<br/></param>
    /// <param name="description">Concise demonstrated behavior.<br/></param>
    /// <param name="order">Position after ordinary conditions and indexes in the learning path.<br/></param>
    /// <param name="source">Embedded paste-ready scenario fragment.<br/></param>
    /// <param name="explanation">Reason to choose this temporary index and its principal limitation.<br/></param>
    /// <param name="concepts">Searchable concepts taught by the example.<br/></param>
    /// <param name="apiNames">Public Abraxas APIs exercised by the example.<br/></param>
    /// <param name="run">Compiled native scenario operation over the prepared group.<br/></param>
    /// <returns>One cataloged executable scenario.<br/></returns>
    private static IShowcaseScenario New(string id, string title, string description, int order, string source,
        string explanation, string[] concepts, string[] apiNames, Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> run)
        => new CatalogActionScenario(id, "Memory-backed indexes", title, description, "Temporary index build", true,
            new ScenarioDocumentation(explanation, concepts, apiNames, [title, description], ["Prepare Memory-backed indexes"], order), source, run);

    /// <summary>Backfills a property path chosen at runtime and walks the matching temporary index keys.<br/></summary>
    /// <param name="workspace">Prepared group with durable customers and no secondary indexes.<br/></param>
    /// <param name="token">Cancellation observed before indexing.<br/></param>
    /// <returns>Selected-record and volatile-index discovery proof.<br/></returns>
    private static ScenarioResult RuntimePath(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        RecordStore<ShowcaseCustomer> customers = workspace.Customers;
        string selectedPath = ".Region";
        if (!customers.TryGetIndex(selectedPath, out _, useMemoryIndex: true))
            customers.CreateMemoryIndex(new PropPath(selectedPath), IndexDirection.Ascending);
        ulong[] selected = customers.IterateRecordIDs(customers.Where.PropPath(selectedPath).AsString.EqualTo("West").EndCondition).ToArray();
        bool valid = selected.Length > 0 && selected.All(id => customers.Get(id)?.Region == "West");
        bool volatileOnly = customers.TryGetIndex(selectedPath, out _, useMemoryIndex: true)
            && NoDurableIndexes(customers);
        watch.Stop();
        bool passed = valid && volatileOnly;
        return new ScenarioResult(passed, passed ? "The runtime path found West customers through a memory-only index." : "The temporary path lookup did not match its stored records.",
            [new("Runtime path", selectedPath), new("Matching records", selected.Length.ToString()), new("Durable index", "None")], selected, typeof(ulong[]), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Backfills a transient index over a derived member path and reads its key order without hydrating each result.<br/></summary>
    /// <param name="workspace">Prepared group holding ordinary durable customer records.<br/></param>
    /// <param name="token">Cancellation observed before indexing.<br/></param>
    /// <returns>Derived-path index and ordered-ID proof.<br/></returns>
    private static ScenarioResult DerivedValue(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        RecordStore<ShowcaseCustomer> customers = workspace.Customers;
        const string path = ".Name.Length";
        if (!customers.TryGetIndex(path, out _, useMemoryIndex: true))
            customers.CreateMemoryExpressionIndex(path);
        ulong[] ids = customers.IterateRecordIDs(customers.GetIndex(path, useMemoryIndex: true)).ToArray();
        bool valid = ids.Length > 0 && ids.All(id => customers.Get(id)?.Name.Length > 0)
            && NoDurableIndexes(customers);
        watch.Stop();
        return new ScenarioResult(valid, valid ? "A derived name-length key was available only in the live memory catalog." : "The derived memory index did not return the prepared records.",
            [new("Derived path", path), new("Indexed records", ids.Length.ToString()), new("Durable index", "None")], ids, typeof(ulong[]), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Uses a volatile two-part key to read typed names and identities directly from the current index.<br/></summary>
    /// <param name="workspace">Prepared customer group whose name parts are durable record members.<br/></param>
    /// <param name="token">Cancellation observed before indexing and between returned rows.<br/></param>
    /// <returns>Typed covered-value count and sample name.<br/></returns>
    private static ScenarioResult CompositeValues(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        RecordStore<ShowcaseCustomer> customers = workspace.Customers;
        const string indexName = "session-display-name";
        if (!customers.TryGetIndex(indexName, out _, useMemoryIndex: true))
        {
            var selector = new Selector<ShowcaseCustomer>();
            customers.CreateMemoryCompositeIndex(indexName,
                selector.Prop(customer => customer.FirstName).ForIndexing(),
                selector.Prop(customer => customer.LastName).ForIndexing());
        }
        int count = 0;
        string sample = "";
        using (var names = customers.Readers.OpenCompositeValues<string, string>(indexName))
            while (names.Read())
            {
                token.ThrowIfCancellationRequested();
                if (count++ == 0) sample = $"{names.Value1} {names.Value2}";
            }
        bool passed = count > 0 && sample.Length > 1 && NoDurableIndexes(customers);
        watch.Stop();
        return new ScenarioResult(passed, passed ? "The temporary composite key supplied typed names and IDs without loading customer objects." : "The temporary composite value read was incomplete.",
            [new("Index", indexName), new("Covered rows", count.ToString()), new("First display name", sample)], count, typeof(int), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Creates a volatile text index whose fixed membership condition admits only the relevant region.<br/></summary>
    /// <param name="workspace">Prepared group containing both West and East customers.<br/></param>
    /// <param name="token">Cancellation observed before index backfill.<br/></param>
    /// <returns>Subset membership and durable-index absence proof.<br/></returns>
    private static ScenarioResult PartialLookup(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        RecordStore<ShowcaseCustomer> customers = workspace.Customers;
        const string indexName = "session-west-codes";
        if (!customers.TryGetIndex(indexName, out _, useMemoryIndex: true))
            customers.CreateMemoryIndex(".Code", TextIndexProfile.Exact, IndexDirection.Ascending,
                where: customers.Where.Prop(customer => customer.Region).EqualTo("West").EndCondition,
                indexName: indexName);
        ulong[] ids = customers.IterateRecordIDs(customers.GetIndex(indexName, useMemoryIndex: true)).ToArray();
        bool passed = ids.Length > 0 && ids.All(id => customers.Get(id)?.Region == "West")
            && ids.Length < customers.IterateRecordIDs().Count() && NoDurableIndexes(customers);
        watch.Stop();
        return new ScenarioResult(passed, passed ? "The temporary Code index contains only West customers." : "The memory-only partial index admitted unexpected records.",
            [new("Indexed West records", ids.Length.ToString()), new("Durable index", "None"), new("Subset fixed at creation", "Region = West")], ids, typeof(ulong[]), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Confirms the prepared group's visible secondary indexes are all volatile, without mistaking default mixed-catalog discovery for durable-only discovery.<br/></summary>
    /// <param name="customers">Prepared group store whose setup deliberately creates no durable secondary indexes.<br/></param>
    /// <returns><see langword="true"/> when every currently visible index is in the memory catalog.<br/></returns>
    private static bool NoDurableIndexes(RecordStore<ShowcaseCustomer> customers)
        => customers.GetIndexes().Count() == customers.GetIndexes(useMemoryIndex: true).Count();
}
