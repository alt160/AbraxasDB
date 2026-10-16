using System.Reflection;

namespace AbraxasShowcase;

/// <summary>Loads the paste-ready C# fragments displayed by the Showcase and assembles complete console-application examples.<br/></summary>
internal static class ExampleSource
{
    private const string Header = "using AbraxasDB;\r\nusing Inheto;\r\nusing System;\r\nusing System.Buffers.Binary;\r\nusing System.Collections.Generic;\r\nusing System.IO;\r\nusing System.Linq;\r\nusing System.Security.Cryptography;\r\n\r\n";

    /// <summary>Reads one exact learner-facing source fragment embedded with the Showcase.<br/></summary>
    /// <param name="relativePath">Path beneath the <c>Examples</c> folder using forward slashes.<br/></param>
    /// <returns>The exact source text with only outer line breaks removed.<br/></returns>
    internal static string Read(string relativePath)
    {
        string suffix = "Examples." + relativePath.Replace('/', '.').Replace('\\', '.');
        Assembly assembly = typeof(ExampleSource).Assembly;
        string[] matches = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one embedded example ending in '{suffix}', found {matches.Length}.");
        using Stream stream = assembly.GetManifestResourceStream(matches[0]) ?? throw new InvalidOperationException("Example source is unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim('\r', '\n');
    }

    /// <summary>Builds one complete top-level <c>Program.cs</c> for the requested runnable scenario.<br/></summary>
    /// <param name="scenarioId">Stable runnable scenario identifier.<br/></param>
    /// <returns>Paste-ready source containing imports, required setup, the scenario, and its domain types.<br/></returns>
    internal static string CompleteScenario(string scenarioId) => scenarioId switch
    {
        "create-recordbase" => Join(Read("Fundamentals/CreateRecordBase.cs.txt")),
        "create-typed-recordstore" => Join(
            Read("Fundamentals/CreateTypedRecordStore.cs.txt"),
            Read("Models/CustomerOrder.cs.txt")),
        "create-record-indexes" => Join(
            Read("Fundamentals/CreateRecordIndexes.cs.txt"),
            Read("Models/CustomerOrder.cs.txt")),
        "index-method-path" => Join(Read("Fundamentals/IndexMethodPath.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "index-calculated-key" => Join(Read("Fundamentals/IndexCalculatedKey.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "index-composite-key" => Join(Read("Fundamentals/IndexCompositeKey.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "index-text-projections" => Join(Read("Fundamentals/IndexTextProjections.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "index-memory-only" => Join(Read("Fundamentals/IndexMemoryOnly.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "analyze-foundation-values" => Join(Read("Fundamentals/AnalyzeValues.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "rebuild-index-without-records" => Join(Read("Fundamentals/RebuildIndex.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "unique-scalar-index" => Join(Read("Fundamentals/UniqueScalarIndex.cs.txt"), Read("Models/UniqueAccount.cs.txt")),
        "unique-string-index" => Join(Read("Fundamentals/UniqueStringIndex.cs.txt"), Read("Models/UniqueAccount.cs.txt")),
        "unique-composite-index" => Join(Read("Fundamentals/UniqueCompositeIndex.cs.txt"), Read("Models/UniqueAccount.cs.txt")),
        "adaptive-payload-compression" => Join(Read("Commercial/AdaptiveCompression.cs.txt"), Read("Models/VaultRecord.cs.txt")),
        "encrypted-payload-roundtrip" => Join(Read("Commercial/EncryptedRoundTrip.cs.txt"), Read("Models/VaultRecord.cs.txt")),
        "compressed-and-encrypted-payload" => Join(Read("Commercial/CompressedAndEncrypted.cs.txt"), Read("Models/VaultRecord.cs.txt")),
        "encrypted-payload-memory-index" => Join(Read("Commercial/EncryptedMemoryIndex.cs.txt"), Read("Models/VaultRecord.cs.txt")),
        "memory-runtime-path" => MemoryIndexExample("MemoryIndexes/RuntimePath.cs.txt"),
        "memory-derived-value" => MemoryIndexExample("MemoryIndexes/DerivedValue.cs.txt"),
        "memory-composite-values" => MemoryIndexExample("MemoryIndexes/CompositeValues.cs.txt"),
        "memory-partial-lookup" => MemoryIndexExample("MemoryIndexes/PartialLookup.cs.txt"),
        "inspect-active-recordbase" => Join(
            Read("Fundamentals/InspectActiveRecordBase.cs.txt"),
            Read("Models/CustomerOrder.cs.txt")),
        "put-get-customer" => Join(
            Read("RecordPersistence/Generation.cs.txt"),
            Read("RecordPersistence/PutAndGetCustomer.cs.txt"),
            Read("RecordPersistence/Environment.cs.txt"),
            Read("Models/CustomerOrder.cs.txt")),
        "persist-object-graph" => Join(
            Read("ObjectIdentity/Generation.cs.txt"),
            Read("ObjectIdentity/PersistObjectGraph.cs.txt"),
            Read("ObjectIdentity/Environment.cs.txt"),
            Read("Models/ObjectIdentity.cs.txt")),
        "retain-external-identity" => Join(
            Read("ObjectIdentity/Generation.cs.txt"),
            Read("ObjectIdentity/RetainExternalIdentity.cs.txt"),
            Read("ObjectIdentity/Environment.cs.txt"),
            Read("Models/ObjectIdentity.cs.txt")),
        "ordinary-temporal-values" => IdentityExample("ObjectIdentity/TemporalValues.cs.txt"),
        "ordinary-framework-values" => IdentityExample("ObjectIdentity/FrameworkValues.cs.txt"),
        "ordinary-dictionary" => IdentityExample("ObjectIdentity/DictionaryValues.cs.txt"),
        "ordinary-hashset" => IdentityExample("ObjectIdentity/HashSetValues.cs.txt"),
        "inspect-store-definitions" => Join(Read("Stores/Generation.cs.txt"), Read("Stores/InspectStoreDefinitions.cs.txt"), Read("Stores/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "count-store-records" => Join(Read("Stores/Generation.cs.txt"), Read("Stores/CountStoreRecords.cs.txt"), Read("Stores/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "observe-store-persistence" => Join(Read("Stores/Generation.cs.txt"), Read("Stores/ObserveStorePersistence.cs.txt"), Read("Stores/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "read-natural-order" => Join(Read("Retrieval/Generation.cs.txt"), Read("Retrieval/ReadNaturalOrder.cs.txt"), Read("Retrieval/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "filter-with-conditions" => CustomerOrderExample("Retrieval/FilterWithConditions.cs.txt"),
        "create-string-index" => CustomerOrderExample("Conditions/UseStringIndex.cs.txt"),
        "explicit-sorted-read" => CustomerOrderExample("Conditions/ExplicitSortedRead.cs.txt"),
        "reproject-object-shape" => ProjectionExample("Projection/ReprojectObject.cs.txt", Read("Models/CustomerSummary.cs.txt")),
        "alias-renamed-member" => ProjectionExample("Projection/PathAlias.cs.txt", Read("Models/CustomerRenamed.cs.txt")),
        "read-values-only" => ProjectionExample("Projection/ReadValuesOnly.cs.txt"),
        "read-path-continuations" => ProjectionExample("Projection/PathContinuations.cs.txt"),
        "path-collection-position" => ProjectionExample("Projection/CollectionPosition.cs.txt"),
        "path-derived-collection-member" => ProjectionExample("Projection/DerivedCollectionMember.cs.txt"),
        "path-literal-dictionary-keys" => ProjectionExample("Projection/LiteralDictionaryKeys.cs.txt"),
        "path-multidimensional-coordinate" => ProjectionExample("Projection/MultidimensionalCoordinate.cs.txt"),
        "navigate-related-records" => CustomerOrderExample("Relationships/NavigateRelatedRecords.cs.txt"),
        "group-with-aggregate" => CustomerOrderExample("Relationships/GroupAndCount.cs.txt"),
        "update-existing-record" => CustomerOrderExample("Mutation/UpdateRecord.cs.txt"),
        "delete-and-verify" => CustomerOrderExample("Mutation/DeleteRecord.cs.txt"),
        "bulk-ingest" => CustomerOrderExample("Scale/BulkIngest.cs.txt"),
        "observe-indexing-completion" => CustomerOrderExample("Scale/IndexingCompletion.cs.txt"),
        "unordered-throughput-read" => CustomerOrderExample("Scale/UnorderedRead.cs.txt"),
        "iterate-selected-values" => CustomerOrderExample("Scale/SelectedValueAnalysis.cs.txt"),
        "analyze-custom-struct" => CustomerOrderExample("Scale/CustomStructAnalysis.cs.txt"),
        "search-order-date-window" => CustomerOrderExample("Search/OrderDateWindow.cs.txt"),
        "search-customer-pattern" => CustomerOrderExample("Search/CustomerPattern.cs.txt"),
        "lookup-recordid" => CustomerOrderExample("Search/DirectRecordID.cs.txt"),
        "exclude-member-path" => HookExample("Hooks/ExcludeMember.cs.txt"),
        "contextual-type-codec" => HookExample("Hooks/ContextualCodec.cs.txt"),
        "exclude-member-type" => HookExample("Hooks/ExcludeType.cs.txt"),
        "custom-property-codec" => HookExample("Hooks/PropertyCodec.cs.txt"),
        "custom-type-codec" => HookExample("Hooks/TypeCodec.cs.txt"),
        "custom-object-activator" => HookExample("Hooks/Activator.cs.txt"),
        "hydrate-packed-lines" => GraphExample("Graphs/HydrateLines.cs.txt"),
        "hydrate-reverse-orders" => GraphExample("Graphs/HydrateOrders.cs.txt"),
        "read-order-without-links" => GraphExample("Graphs/ReadNoLinks.cs.txt"),
        "put-multiple-customers" => PersistenceExample("RecordPersistence/PutMultiple.cs.txt"),
        "try-get-absent" => PersistenceExample("RecordPersistence/TryGetAbsent.cs.txt"),
        "read-array-member" => PersistenceExample("RecordPersistence/ReadArray.cs.txt"),
        "enumerate-store-catalog" => StoreExample("Stores/EnumerateCatalog.cs.txt"),
        "read-ids-without-objects" => CustomerOrderExample("Retrieval/ReadIDs.cs.txt"),
        "read-orders-forward" => CustomerOrderExample("Retrieval/ReadOrders.cs.txt"),
        "select-by-region" => CustomerOrderExample("Conditions/SelectRegion.cs.txt"),
        "select-by-total-range" => CustomerOrderExample("Conditions/TotalRange.cs.txt"),
        "find-first-in-region" => CustomerOrderExample("Search/FirstInRegion.cs.txt"),
        "project-customer-summary" => ProjectionExample("Projection/CustomerSummary.cs.txt", Read("Models/CustomerSummary.cs.txt")),
        "read-order-total-path" => ProjectionExample("Projection/OrderTotal.cs.txt"),
        "read-customer-without-links" => GraphExample("Graphs/ReadCustomerNoLinks.cs.txt"),
        "count-related-orders" => CustomerOrderExample("Relationships/CountRelatedOrders.cs.txt"),
        "sum-order-totals" => CustomerOrderExample("Relationships/SumOrderTotals.cs.txt"),
        "update-indexed-code" => CustomerOrderExample("Mutation/UpdateIndexedCode.cs.txt"),
        "delete-missing-id" => CustomerOrderExample("Mutation/DeleteMissing.cs.txt"),
        "count-ids-without-hydration" => CustomerOrderExample("Scale/CountIDs.cs.txt"),
        _ => throw new ArgumentException("Unknown runnable scenario.", nameof(scenarioId))
    };

    /// <summary>Builds the complete paste-ready setup program for one group that owns reusable data.<br/></summary>
    /// <param name="groupId">Stable scenario-group identifier.<br/></param>
    /// <returns>Complete console source for preparing the group's RecordBase and deterministic data.<br/></returns>
    internal static string CompleteGroup(string groupId) => groupId switch
    {
        "recordbase-foundations" => Join(
            Read("RecordPersistence/Generation.cs.txt"),
            Read("RecordPersistence/Environment.cs.txt"),
            Read("Models/CustomerOrder.cs.txt")),
        "object-shapes-identity" => Join(
            Read("ObjectIdentity/Generation.cs.txt"),
            Read("ObjectIdentity/Environment.cs.txt"),
            Read("Models/ObjectIdentity.cs.txt")),
        "stores" => Join(Read("Stores/Generation.cs.txt"), Read("Stores/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "retrieval-and-readers" => Join(Read("Retrieval/Generation.cs.txt"), Read("Retrieval/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "shape-and-materialization" => Join(Read("Hooks/Generation.cs.txt"), Read("Hooks/Environment.cs.txt"), Read("Models/ShapeHooks.cs.txt")),
        "object-graphs-across-stores" => Join(Read("Graphs/Generation.cs.txt"), Read("Graphs/Environment.cs.txt"), Read("Models/ObjectGraphs.cs.txt")),
        "projection-and-values" => Join(Read("Projection/Generation.cs.txt"), Read("Projection/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"), Read("Models/PathGraph.cs.txt")),
        "memory-backed-indexes" => Join(Read("MemoryIndexes/Generation.cs.txt"), Read("MemoryIndexes/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        "conditions-and-indexes" or "search-and-lookup" or "relationships-and-aggregates" or "mutation-and-lifecycle" or "scale-and-performance" =>
            Join(Read("Retrieval/Generation.cs.txt"), Read("Retrieval/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt")),
        _ => ""
    };

    /// <summary>Combines the shared customer/order setup with one independent scenario and any additional projection type.<br/></summary>
    /// <param name="scenarioPath">Embedded learner-facing scenario source path.<br/></param>
    /// <param name="extraType">Optional additional public .NET type required by the scenario.<br/></param>
    /// <returns>One complete top-level C# console program.<br/></returns>
    private static string CustomerOrderExample(string scenarioPath, string? extraType = null) => extraType is null
        ? Join(Read("Retrieval/Generation.cs.txt"), Read(scenarioPath), Read("Retrieval/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"))
        : Join(Read("Retrieval/Generation.cs.txt"), Read(scenarioPath), Read("Retrieval/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"), extraType);

    /// <summary>Combines durable sample-record setup with one temporary-index lesson and its ordinary customer type.<br/></summary>
    /// <param name="scenarioPath">Embedded memory-index scenario source to execute after the records are prepared.<br/></param>
    /// <returns>A complete paste-ready console program with a context that owns its RecordBase and store.<br/></returns>
    private static string MemoryIndexExample(string scenarioPath) => Join(Read("MemoryIndexes/Generation.cs.txt"), Read(scenarioPath), Read("MemoryIndexes/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"));

    /// <summary>Combines the projection group's three-store setup, one runnable path lesson, and its exact domain declarations.<br/></summary>
    /// <param name="scenarioPath">Embedded scenario fragment under Projection.<br/></param>
    /// <param name="extraType">Optional result-shape declaration required by the scenario.<br/></param>
    /// <returns>One complete paste-ready C# program.<br/></returns>
    private static string ProjectionExample(string scenarioPath, string? extraType = null) => extraType is null
        ? Join(Read("Projection/Generation.cs.txt"), Read(scenarioPath), Read("Projection/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"), Read("Models/PathGraph.cs.txt"))
        : Join(Read("Projection/Generation.cs.txt"), Read(scenarioPath), Read("Projection/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"), Read("Models/PathGraph.cs.txt"), extraType);

    /// <summary>Combines hook-group setup, one scenario, and the exact domain declarations into a console example.<br/></summary>
    /// <param name="scenarioPath">Scenario fragment under Examples.<br/></param>
    /// <returns>Paste-ready top-level C# source.<br/></returns>
    private static string HookExample(string scenarioPath) => Join(Read("Hooks/Generation.cs.txt"), Read(scenarioPath), Read("Hooks/Environment.cs.txt"), Read("Models/ShapeHooks.cs.txt"));

    /// <summary>Combines the reusable identity-group setup with one hook-free ordinary-shape scenario.<br/></summary>
    /// <param name="scenarioPath">Embedded scenario source to run after deterministic profile generation.<br/></param>
    /// <returns>One paste-ready top-level program with its owner and model declarations.<br/></returns>
    private static string IdentityExample(string scenarioPath) => Join(Read("ObjectIdentity/Generation.cs.txt"), Read(scenarioPath), Read("ObjectIdentity/Environment.cs.txt"), Read("Models/ObjectIdentity.cs.txt"));

    /// <summary>Combines persistence-group setup and one independent record operation into a paste-ready program.<br/></summary>
    /// <param name="scenarioPath">Embedded scenario source to execute after setup.<br/></param>
    /// <returns>Complete source with the group owner and ordinary domain models.<br/></returns>
    private static string PersistenceExample(string scenarioPath) => Join(Read("RecordPersistence/Generation.cs.txt"), Read(scenarioPath), Read("RecordPersistence/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"));

    /// <summary>Combines store-group setup and one metadata example into a paste-ready program.<br/></summary>
    /// <param name="scenarioPath">Embedded scenario source to execute after setup.<br/></param>
    /// <returns>Complete source with the group owner and typed store models.<br/></returns>
    private static string StoreExample(string scenarioPath) => Join(Read("Stores/Generation.cs.txt"), Read(scenarioPath), Read("Stores/Environment.cs.txt"), Read("Models/CustomerOrder.cs.txt"));

    /// <summary>Combines normalized graph setup, one scenario, and all persisted model declarations into a console example.<br/></summary>
    /// <param name="scenarioPath">Scenario fragment under Examples.<br/></param>
    /// <returns>Paste-ready top-level C# source.<br/></returns>
    private static string GraphExample(string scenarioPath) => Join(Read("Graphs/Generation.cs.txt"), Read(scenarioPath), Read("Graphs/Environment.cs.txt"), Read("Models/ObjectGraphs.cs.txt"));

    /// <summary>Concatenates top-level source fragments beneath the shared Abraxas import with stable spacing.<br/></summary>
    /// <param name="parts">Ordered source fragments followed by any required type declarations.<br/></param>
    /// <returns>One complete source document.<br/></returns>
    private static string Join(params string[] parts) => Header + string.Join("\r\n\r\n", parts) + "\r\n";
}
