namespace AbraxasDB;

/// <summary>Chooses the cumulative work budget for explicit ordered-index maintenance, not ordinary queries or writes.<br/></summary>
public enum IndexMaintenanceMode
{
    /// <summary>Uses at most 16 physical traversal units unless a positive explicit limit overrides it.<br/></summary>
    Light = 0,
    /// <summary>Uses at most 256 physical traversal units unless a positive explicit limit overrides it; this is the default.<br/></summary>
    Bounded = 1,
    /// <summary>Walks all applicable work unless a positive explicit limit is supplied; may be expensive.<br/></summary>
    Full = 2
}

/// <summary>Configures one explicit Optimize/Repack call; immutable after initialization and unrelated to write-batch/index-build budgets.<br/></summary>
public sealed class IndexMaintenanceOptions
{
    /// <summary>Gets or initializes the mode-owned cumulative work limit; defaults to Bounded (256 traversal units).<br/></summary>
    public IndexMaintenanceMode Mode { get; init; } = IndexMaintenanceMode.Bounded;
    /// <summary>Gets or initializes a positive cumulative traversal-unit override; null uses the selected mode, while zero/negative values are rejected during maintenance.<br/></summary>
    public int? MaxWorkItems { get; init; }
}

/// <summary>Identifies the requested ordered-index maintenance operation; not all categories have a public RecordBase entry point.<br/></summary>
public enum IndexMaintenanceOperation
{
    /// <summary>Describes non-mutating structural validation; not an additional RecordBase operation.<br/></summary>
    Validate = 0,
    /// <summary>Rebuilds supported route/shelf topology; may increase backing-file size.<br/></summary>
    Optimize = 1,
    /// <summary>Densifies supported variable-payload shelves without promising backing-file truncation.<br/></summary>
    Repack = 2,
    /// <summary>Describes runtime cache work; not an additional RecordBase operation.<br/></summary>
    Cache = 3
}

/// <summary>Reports why an explicit index maintenance walk was incomplete; reasons can be combined.<br/></summary>
[Flags]
public enum IndexMaintenanceIncompleteReason
{
    /// <summary>No applicable requested work was left incomplete.<br/></summary>
    None = 0,
    /// <summary>The cumulative traversal budget was reached.<br/></summary>
    WorkLimit = 1,
    /// <summary>A valid index topology has no connected implementation for this operation.<br/></summary>
    UnsupportedTopology = 2,
    /// <summary>A physical topology could not be recognized or completely traversed as valid.<br/></summary>
    InvalidTopology = 4,
    /// <summary>The requested operation category has no executable implementation.<br/></summary>
    OperationUnavailable = 8
}

/// <summary>Owns the exact completion/count/message evidence of one ordered-index maintenance request; no engine handle or per-record adapter is retained.<br/></summary>
/// <param name="Operation">Requested operation.<br/></param><param name="Mode">Selected work mode.<br/></param>
/// <param name="Completed">Whether all applicable requested work completed.<br/></param>
/// <param name="ConsideredCount">Exact physical traversal units considered.<br/></param><param name="ChangedCount">Exact changes reported by the engine.<br/></param>
/// <param name="Message">Unmodified explanatory message; use IncompleteReasons for machine decisions.<br/></param>
public readonly record struct IndexMaintenanceResult(IndexMaintenanceOperation Operation, IndexMaintenanceMode Mode,
    bool Completed, int ConsideredCount, int ChangedCount, string Message)
{
    /// <summary>Gets or initializes combined reasons for incomplete work; completed work reports None.<br/></summary>
    public IndexMaintenanceIncompleteReason IncompleteReasons { get; init; }
    /// <summary>Gets or initializes distinct logical indexes unsupported by this operation; physical companions do not inflate the count.<br/></summary>
    public int UnsupportedCount { get; init; }
}

/// <summary>Identifies the physical projection represented by an index diagnostic, without exposing an engine enum.<br/></summary>
public enum RecordIndexProjectionKind
{
    /// <summary>Exact source representation.<br/></summary>
    Exact = 0,
    /// <summary>Folded-text comparison representation.<br/></summary>
    FoldedText = 1,
    /// <summary>Sort-key representation.<br/></summary>
    SortKey = 2,
    /// <summary>Structured date representation.<br/></summary>
    StructuredDate = 3,
    /// <summary>Segmented GUID representation.<br/></summary>
    GuidSegments = 4,
    /// <summary>Textual GUID representation.<br/></summary>
    GuidText = 5,
    /// <summary>Fixed-width large-integer representation.<br/></summary>
    BigIntFixed = 6,
    /// <summary>Variable-length large-integer representation.<br/></summary>
    BigIntVarLen = 7,
    /// <summary>Fixed large-integer keys with variable identity payloads.<br/></summary>
    BigIntFixedVarIdentity = 8,
    /// <summary>Exact variable-length binary representation.<br/></summary>
    VariableBlobExact = 9,
    /// <summary>Normalized-text representation.<br/></summary>
    NormalizedText = 10
}

/// <summary>Describes byte orientation in a physical projection, not the caller's ascending/descending query order.<br/></summary>
public enum RecordIndexByteDirection
{
    /// <summary>Normal byte orientation.<br/></summary>
    Forward = 0,
    /// <summary>Reversed-byte orientation.<br/></summary>
    Reversed = 1
}

/// <summary>Describes an actionable maintained-projection metadata issue discovered by assessment.<br/></summary>
public enum RecordIndexProjectionHealth
{
    /// <summary>Persisted projection intent has no active companion slot.<br/></summary>
    Missing = 0,
    /// <summary>A companion slot belongs to a different index group.<br/></summary>
    Inconsistent = 1
}

/// <summary>Disconnected attributable repack evidence for one logical ordered index; companion bytes are already included.<br/></summary>
/// <param name="Group">Physical index-group identifier; not necessarily the application store name.<br/></param><param name="IndexName">Logical source index name.<br/></param>
/// <param name="ReclaimableBytes">Exact attributable orphaned payload bytes, including maintained companions.<br/></param><param name="Reason">Recommendation evidence.<br/></param>
public readonly record struct RecordIndexRepackCandidate(string Group, string IndexName, long ReclaimableBytes, string Reason);

/// <summary>Attributes reclaimable bytes to one physical component; do not add these bytes to the logical candidate total again.<br/></summary>
/// <param name="Group">Physical group identifier.<br/></param><param name="IndexName">Logical source index.<br/></param>
/// <param name="PhysicalSlotIndex">Diagnostic physical directory slot.<br/></param><param name="Projection">Projection representation.<br/></param>
/// <param name="Direction">Projection byte orientation.<br/></param><param name="ReclaimableBytes">Exact orphaned payload bytes.<br/></param><param name="ShelfCount">Visited variable-record shelves.<br/></param>
public readonly record struct RecordIndexReclaimComponent(string Group, string IndexName, int PhysicalSlotIndex,
    RecordIndexProjectionKind Projection, RecordIndexByteDirection Direction, long ReclaimableBytes, int ShelfCount);

/// <summary>Disconnected evidence of missing/inconsistent maintained projection metadata.<br/></summary>
/// <param name="Group">Physical group identifier.<br/></param><param name="IndexName">Logical source index.<br/></param>
/// <param name="Projection">Projection representation.<br/></param><param name="Direction">Projection byte orientation.<br/></param>
/// <param name="Health">Detected issue.<br/></param><param name="RecommendedAction">Unmodified developer-facing action.<br/></param><param name="Reason">Persisted supporting evidence.<br/></param>
public readonly record struct RecordIndexProjectionIssue(string Group, string IndexName, RecordIndexProjectionKind Projection,
    RecordIndexByteDirection Direction, RecordIndexProjectionHealth Health, string RecommendedAction, string Reason);

/// <summary>Exact reachable accounting for one supported fixed-topology generation, not speculative ownership of obsolete extents.<br/></summary>
/// <param name="Group">Physical group identifier.<br/></param><param name="IndexName">Logical source index.<br/></param>
/// <param name="PhysicalSlotIndex">Assessed directory slot.<br/></param><param name="Generation">Active generation.<br/></param>
/// <param name="TupleCount">Exact live tuples.<br/></param><param name="ReachableBytes">Exact distinct reachable router/root/shelf bytes.<br/></param>
/// <param name="RouterCount">Distinct router pages.<br/></param><param name="OrdinaryShelfCount">Distinct ordinary shelves, including duplicate runs.<br/></param>
/// <param name="DuplicateRunShelfCount">Ordinary shelves participating in duplicate runs.<br/></param><param name="TerminalRootCount">Exact-key terminal roots.<br/></param>
/// <param name="TerminalShelfCount">Terminal identity shelves.<br/></param><param name="ShelfExtentSize">Selected fixed shelf extent bytes.<br/></param>
public readonly record struct RecordIndexFixedStorageComponent(string Group, string IndexName, int PhysicalSlotIndex,
    long Generation, long TupleCount, long ReachableBytes, int RouterCount, int OrdinaryShelfCount,
    int DuplicateRunShelfCount, int TerminalRootCount, int TerminalShelfCount, int ShelfExtentSize)
{
    /// <summary>Gets exact reachable bytes per live tuple; zero for an empty generation.<br/></summary>
    public double BytesPerLiveTuple => TupleCount == 0 ? 0 : (double)ReachableBytes / TupleCount;
    /// <summary>Gets per-index obsolete bytes only when allocation provenance exists; currently null, never an invented estimate.<br/></summary>
    public long? UnreachableBytes => null;
}

/// <summary>Exact reachable accounting for one supported variable-text/scalar-identity generation, including null/empty-key routes.<br/></summary>
/// <param name="Group">Physical group identifier.<br/></param><param name="IndexName">Logical source index.<br/></param>
/// <param name="PhysicalSlotIndex">Assessed directory slot.<br/></param><param name="Generation">Active generation.<br/></param>
/// <param name="TupleCount">Exact live tuples, including null/empty keys.<br/></param><param name="ReachableBytes">Exact distinct reachable bytes.<br/></param>
/// <param name="ReclaimablePayloadBytes">Exact orphaned payload within reachable ordinary shelves.<br/></param><param name="RouterCount">Distinct router pages.<br/></param>
/// <param name="OrdinaryShelfCount">Distinct ordinary shelves.<br/></param><param name="DuplicateRunShelfCount">Shelves in duplicate runs.<br/></param>
/// <param name="TerminalRootCount">Ordinary/promoted key-state terminal roots.<br/></param><param name="TerminalShelfCount">Terminal identity shelves.<br/></param>
/// <param name="KeyStateRootCount">Reachable null/empty-key roots.<br/></param>
public readonly record struct RecordIndexVariableTextStorageComponent(string Group, string IndexName, int PhysicalSlotIndex,
    long Generation, long TupleCount, long ReachableBytes, long ReclaimablePayloadBytes, int RouterCount,
    int OrdinaryShelfCount, int DuplicateRunShelfCount, int TerminalRootCount, int TerminalShelfCount, int KeyStateRootCount)
{
    /// <summary>Gets exact reachable bytes per live tuple; zero for an empty generation.<br/></summary>
    public double BytesPerLiveTuple => TupleCount == 0 ? 0 : (double)ReachableBytes / TupleCount;
    /// <summary>Gets per-index obsolete bytes only when allocation provenance exists; currently null rather than speculative attribution.<br/></summary>
    public long? UnreachableBytes => null;
}

/// <summary>Owns a disconnected ordered-index file/storage snapshot; incomplete accounting retains unknown values as null.<br/></summary>
public sealed class RecordIndexStorageAssessment
{
    /// <summary>Copies exact scalar evidence and already-disconnected diagnostic component lists once per assessment.<br/></summary>
    /// <param name="source">Published engine snapshot; no live catalog is retained.<br/></param>
    internal RecordIndexStorageAssessment(LibraDex.LibraDexCatalogStorageAssessment source)
    {
        PhysicalBytes = source.PhysicalBytes;
        KnownReachableBytes = source.KnownReachableBytes;
        CatalogOverheadReachableBytes = source.CatalogOverheadReachableBytes;
        AllocatorOverheadReachableBytes = source.AllocatorOverheadReachableBytes;
        ReusableBytes = source.ReusableBytes;
        UnreachableBytes = source.UnreachableBytes;
        IsComplete = source.IsComplete;
        UnsupportedIndexCount = source.UnsupportedIndexCount;
        FixedTopologyComponents = IndexMaintenanceBinding.CopyFixed(source.FixedTopologyComponents);
        VariableTextTopologyComponents = IndexMaintenanceBinding.CopyVariableText(source.VariableTextTopologyComponents);
        AmplificationRatio = source.AmplificationRatio;
    }
    /// <summary>Gets index-file bytes when available; not record-file or total database bytes.<br/></summary>
    public long? PhysicalBytes { get; }
    /// <summary>Gets exact reachable bytes understood by the connected walkers; partial when IsComplete is false.<br/></summary>
    public long KnownReachableBytes { get; }
    /// <summary>Gets exact reachable catalog/directory/projection metadata bytes.<br/></summary>
    public long CatalogOverheadReachableBytes { get; }
    /// <summary>Gets exact reachable allocator bookkeeping bytes.<br/></summary>
    public long AllocatorOverheadReachableBytes { get; }
    /// <summary>Gets engine-reported reusable physical bytes, separate from orphaned payload and unreachable bytes.<br/></summary>
    public long ReusableBytes { get; }
    /// <summary>Gets exact catalog-wide unreachable bytes only when accounting is complete; otherwise null.<br/></summary>
    public long? UnreachableBytes { get; }
    /// <summary>Gets whether all physical/index/key-state reachability is accounted for authoritatively.<br/></summary>
    public bool IsComplete { get; }
    /// <summary>Gets physical index slots whose topology is not represented in reachable accounting.<br/></summary>
    public int UnsupportedIndexCount { get; }
    /// <summary>Gets read-only owned fixed-topology diagnostic values.<br/></summary>
    public IReadOnlyList<RecordIndexFixedStorageComponent> FixedTopologyComponents { get; }
    /// <summary>Gets read-only owned variable-text diagnostic values.<br/></summary>
    public IReadOnlyList<RecordIndexVariableTextStorageComponent> VariableTextTopologyComponents { get; }
    /// <summary>Gets exact physical/reachable amplification when available and complete; otherwise null.<br/></summary>
    public double? AmplificationRatio { get; }
}

/// <summary>Owns disconnected maintenance recommendations and exact index-storage evidence; empty candidates alone do not prove that compaction cannot help.<br/></summary>
public sealed class IndexMaintenanceAssessment
{
    /// <summary>Copies explicit-assessment diagnostic values once without retaining a native report or catalog handle.<br/></summary>
    /// <param name="source">Published engine assessment with the caller's threshold.<br/></param>
    internal IndexMaintenanceAssessment(LibraDex.LibraDexMaintenanceAssessment source)
    {
        MinimumReclaimableBytes = source.MinimumReclaimableBytes;
        RepackCandidates = IndexMaintenanceBinding.CopyCandidates(source.RepackCandidates);
        ReclaimComponents = IndexMaintenanceBinding.CopyReclaim(source.ReclaimComponents);
        ProjectionIssues = IndexMaintenanceBinding.CopyIssues(source.ProjectionIssues);
        Storage = new RecordIndexStorageAssessment(source.Storage);
        CanAttributeRepackCandidates = source.CanAttributeRepackCandidates;
        UnattributedReclaimedCellCount = source.UnattributedReclaimedCellCount;
    }
    /// <summary>Gets the per-logical-index recommendation threshold supplied by the caller.<br/></summary>
    public long MinimumReclaimableBytes { get; }
    /// <summary>Gets read-only owned logical-index recommendations, already including maintained companion bytes.<br/></summary>
    public IReadOnlyList<RecordIndexRepackCandidate> RepackCandidates { get; }
    /// <summary>Gets read-only component attribution; do not add it to candidate totals a second time.<br/></summary>
    public IReadOnlyList<RecordIndexReclaimComponent> ReclaimComponents { get; }
    /// <summary>Gets read-only maintained projection issues requiring developer attention.<br/></summary>
    public IReadOnlyList<RecordIndexProjectionIssue> ProjectionIssues { get; }
    /// <summary>Gets the index-only physical/reachability snapshot from this assessment.<br/></summary>
    public RecordIndexStorageAssessment Storage { get; }
    /// <summary>Gets whether the engine can authoritatively attribute reclaimable payload evidence to logical indexes.<br/></summary>
    public bool CanAttributeRepackCandidates { get; }
    /// <summary>Gets known session-local reclaimed cells lacking logical attribution; a count, not a byte estimate.<br/></summary>
    public long UnattributedReclaimedCellCount { get; }
    /// <summary>Gets whether attributable repack candidates or projection issues were found.<br/></summary>
    public bool HasRecommendations => RepackCandidates.Count != 0 || ProjectionIssues.Count != 0;
}

/// <summary>Controls RecordBase compaction; independent of ordered-index Optimize/Repack work budgets.<br/></summary>
[Flags]
public enum RecordBaseCompactionOptions
{
    /// <summary>Runs existing bounded record-storage maintenance and rollback-protected ordered-index file compaction.<br/></summary>
    None = 0,
    /// <summary>Also reconstructs the healthy numeric record-identity lookup routing before ordered-index compaction.<br/>
    /// Preserves current A/B selections and record locations without rewriting payloads; requires the existing authoritative record index and is not missing-index recovery.<br/></summary>
    RebuildRecordIndex = 1
}

/// <summary>Reports exact completed physical ordered-index catalog compaction; never record-file or total database reclamation.<br/></summary>
/// <param name="Path">Replacement index catalog path.<br/></param><param name="BackupPath">Retained rollback path, or null after successful cleanup.<br/></param>
/// <param name="SourceBytes">Original index catalog bytes.<br/></param><param name="CompactedBytes">Validated replacement index catalog bytes.<br/></param>
/// <param name="ReclaimedBytes">Signed index byte reduction; negative means the replacement grew.<br/></param>
/// <param name="LogicalIndexCount">Logical source indexes rebuilt, excluding owned physical companions.<br/></param><param name="TupleCount">Authoritative source tuples copied.<br/></param>
public readonly record struct RecordIndexCompactionResult(string Path, string? BackupPath, long SourceBytes,
    long CompactedBytes, long ReclaimedBytes, int LogicalIndexCount, long TupleCount)
{
    /// <summary>Gets or initializes logical indexes whose legacy ordering required validated unordered-tuple recovery.<br/></summary>
    public int RecoveredUnorderedIndexCount { get; init; }
}

/// <summary>Reports completed RecordBase compaction and live-store rebinding.<br/>Record storage uses the existing bounded best-effort defrag pass; no exact record-file reclamation is reported.<br/></summary>
/// <param name="IndexStorage">Exact physical ordered-index catalog result, separate from record data and numeric record lookup storage.<br/></param>
/// <param name="RecordIndexRebuilt">Whether the requested numeric record lookup rebuild completed successfully.<br/></param>
public readonly record struct RecordBaseCompactionResult(RecordIndexCompactionResult IndexStorage, bool RecordIndexRebuilt);

/// <summary>Converts explicit maintenance authoring/results at the owner boundary; never participates in ordinary record/query execution.<br/></summary>
internal static class IndexMaintenanceBinding
{
    /// <summary>Maps one immutable option set by named modes; null retains the native default and no extra object.<br/></summary>
    /// <param name="options">Owner options at the existing admitted maintenance stage.<br/></param><returns>One native option set, or null.<br/></returns>
    internal static LibraDex.LibraDexMaintenanceOptions? ToNative(IndexMaintenanceOptions? options) => options is null ? null : new()
    {
        Mode = options.Mode switch
        {
            IndexMaintenanceMode.Light => LibraDex.LibraDexMaintenanceMode.Light,
            IndexMaintenanceMode.Bounded => LibraDex.LibraDexMaintenanceMode.Bounded,
            IndexMaintenanceMode.Full => LibraDex.LibraDexMaintenanceMode.Full,
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "The maintenance mode is not defined.")
        },
        MaxWorkItems = options.MaxWorkItems
    };

    /// <summary>Maps all structured completion evidence by value; messages are retained without copying and unknown native enum values fail explicitly.<br/></summary>
    /// <param name="source">Completed/incomplete native walk evidence.<br/></param><returns>Owner result with exact counters and combined reasons.<br/></returns>
    internal static IndexMaintenanceResult ToPublic(in LibraDex.LibraDexMaintenanceResult source) => new(
        source.Operation switch
        {
            LibraDex.LibraDexMaintenanceOperation.Validate => IndexMaintenanceOperation.Validate,
            LibraDex.LibraDexMaintenanceOperation.Optimize => IndexMaintenanceOperation.Optimize,
            LibraDex.LibraDexMaintenanceOperation.Repack => IndexMaintenanceOperation.Repack,
            LibraDex.LibraDexMaintenanceOperation.Cache => IndexMaintenanceOperation.Cache,
            _ => throw new InvalidDataException("Unknown index maintenance operation.")
        },
        source.Mode switch
        {
            LibraDex.LibraDexMaintenanceMode.Light => IndexMaintenanceMode.Light,
            LibraDex.LibraDexMaintenanceMode.Bounded => IndexMaintenanceMode.Bounded,
            LibraDex.LibraDexMaintenanceMode.Full => IndexMaintenanceMode.Full,
            _ => throw new InvalidDataException("Unknown index maintenance mode.")
        }, source.Completed, source.ConsideredCount, source.ChangedCount, source.Message)
    {
        IncompleteReasons = ToPublic(source.IncompleteReasons), UnsupportedCount = source.UnsupportedCount
    };

    /// <summary>Maps every recognized combined incomplete-reason flag without numeric enum coupling or boxing.<br/></summary>
    /// <param name="source">Native flag combination.<br/></param><returns>Exact owner flag combination.<br/></returns>
    private static IndexMaintenanceIncompleteReason ToPublic(LibraDex.LibraDexMaintenanceIncompleteReason source)
    {
        const LibraDex.LibraDexMaintenanceIncompleteReason known = LibraDex.LibraDexMaintenanceIncompleteReason.WorkLimit |
            LibraDex.LibraDexMaintenanceIncompleteReason.UnsupportedTopology | LibraDex.LibraDexMaintenanceIncompleteReason.InvalidTopology |
            LibraDex.LibraDexMaintenanceIncompleteReason.OperationUnavailable;
        if ((source & ~known) != 0) throw new InvalidDataException("Unknown index maintenance incomplete-reason flags.");
        var result = IndexMaintenanceIncompleteReason.None;
        if ((source & LibraDex.LibraDexMaintenanceIncompleteReason.WorkLimit) != 0) result |= IndexMaintenanceIncompleteReason.WorkLimit;
        if ((source & LibraDex.LibraDexMaintenanceIncompleteReason.UnsupportedTopology) != 0) result |= IndexMaintenanceIncompleteReason.UnsupportedTopology;
        if ((source & LibraDex.LibraDexMaintenanceIncompleteReason.InvalidTopology) != 0) result |= IndexMaintenanceIncompleteReason.InvalidTopology;
        if ((source & LibraDex.LibraDexMaintenanceIncompleteReason.OperationUnavailable) != 0) result |= IndexMaintenanceIncompleteReason.OperationUnavailable;
        return result;
    }

    /// <summary>Copies exact signed/null compaction evidence by value, with no additional filesystem operations or allocations.<br/></summary>
    /// <param name="source">Completed replacement/rebind evidence.<br/></param><param name="recordIndexRebuilt">Successful numeric record-index rebuild status.<br/></param>
    /// <returns>Owner result distinguishing index metrics from record-file maintenance.<br/></returns>
    internal static RecordBaseCompactionResult ToPublic(in LibraDex.LibraDexCompactionResult source, bool recordIndexRebuilt) => new(
        new RecordIndexCompactionResult(source.Path, source.BackupPath, source.SourceBytes, source.CompactedBytes,
            source.ReclaimedBytes, source.LogicalIndexCount, source.TupleCount) { RecoveredUnorderedIndexCount = source.RecoveredUnorderedIndexCount }, recordIndexRebuilt);

    /// <summary>Maps a diagnostic projection by explicit names; unknown engine representations are never mislabeled.<br/></summary>
    /// <param name="source">Physical representation.<br/></param><returns>Equivalent owned representation.<br/></returns>
    private static RecordIndexProjectionKind ToPublic(LibraDex.LibraDexIndexProjectionKind source) => source switch
    {
        LibraDex.LibraDexIndexProjectionKind.Exact => RecordIndexProjectionKind.Exact,
        LibraDex.LibraDexIndexProjectionKind.FoldedText => RecordIndexProjectionKind.FoldedText,
        LibraDex.LibraDexIndexProjectionKind.SortKey => RecordIndexProjectionKind.SortKey,
        LibraDex.LibraDexIndexProjectionKind.StructuredDate => RecordIndexProjectionKind.StructuredDate,
        LibraDex.LibraDexIndexProjectionKind.GuidSegments => RecordIndexProjectionKind.GuidSegments,
        LibraDex.LibraDexIndexProjectionKind.GuidText => RecordIndexProjectionKind.GuidText,
        LibraDex.LibraDexIndexProjectionKind.BigIntFixed => RecordIndexProjectionKind.BigIntFixed,
        LibraDex.LibraDexIndexProjectionKind.BigIntVarLen => RecordIndexProjectionKind.BigIntVarLen,
        LibraDex.LibraDexIndexProjectionKind.BigIntFixedVarIdentity => RecordIndexProjectionKind.BigIntFixedVarIdentity,
        LibraDex.LibraDexIndexProjectionKind.VariableBlobExact => RecordIndexProjectionKind.VariableBlobExact,
        LibraDex.LibraDexIndexProjectionKind.NormalizedText => RecordIndexProjectionKind.NormalizedText,
        _ => throw new InvalidDataException("Unknown index diagnostic projection.")
    };

    /// <summary>Maps physical byte orientation, not logical query sort order.<br/></summary>
    /// <param name="source">Native orientation.<br/></param><returns>Owned orientation.<br/></returns>
    private static RecordIndexByteDirection ToPublic(LibraDex.LibraDexIndexByteDirection source) => source switch
    {
        LibraDex.LibraDexIndexByteDirection.Forward => RecordIndexByteDirection.Forward,
        LibraDex.LibraDexIndexByteDirection.Reversed => RecordIndexByteDirection.Reversed,
        _ => throw new InvalidDataException("Unknown index diagnostic byte direction.")
    };

    /// <summary>Copies logical candidates once; the resulting list cannot mutate diagnostic arrays.<br/></summary>
    /// <param name="source">Disconnected native candidates.<br/></param><returns>Read-only owned candidate values.<br/></returns>
    internal static IReadOnlyList<RecordIndexRepackCandidate> CopyCandidates(IReadOnlyList<LibraDex.LibraDexRepackCandidate> source)
    {
        if (source.Count == 0) return Array.Empty<RecordIndexRepackCandidate>();
        var result = new RecordIndexRepackCandidate[source.Count];
        for (int i = 0; i < result.Length; i++) { var item = source[i]; result[i] = new(item.Group, item.IndexName, item.ReclaimableBytes, item.Reason); }
        return Array.AsReadOnly(result);
    }

    /// <summary>Copies component reclaim attribution once without double-counting logical candidate totals.<br/></summary>
    /// <param name="source">Disconnected native components.<br/></param><returns>Read-only owned attribution.<br/></returns>
    internal static IReadOnlyList<RecordIndexReclaimComponent> CopyReclaim(IReadOnlyList<LibraDex.LibraDexReclaimComponent> source)
    {
        if (source.Count == 0) return Array.Empty<RecordIndexReclaimComponent>();
        var result = new RecordIndexReclaimComponent[source.Count];
        for (int i = 0; i < result.Length; i++) { var item = source[i]; result[i] = new(item.Group, item.IndexName, item.PhysicalSlotIndex, ToPublic(item.Projection), ToPublic(item.Direction), item.ReclaimableBytes, item.ShelfCount); }
        return Array.AsReadOnly(result);
    }

    /// <summary>Copies exact projection issue/action strings once and validates health classification explicitly.<br/></summary>
    /// <param name="source">Disconnected native issues.<br/></param><returns>Read-only owned issues.<br/></returns>
    internal static IReadOnlyList<RecordIndexProjectionIssue> CopyIssues(IReadOnlyList<LibraDex.LibraDexProjectionIssue> source)
    {
        if (source.Count == 0) return Array.Empty<RecordIndexProjectionIssue>();
        var result = new RecordIndexProjectionIssue[source.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var item = source[i];
            var health = item.Health switch
            {
                LibraDex.LibraDexProjectionHealth.Missing => RecordIndexProjectionHealth.Missing,
                LibraDex.LibraDexProjectionHealth.Inconsistent => RecordIndexProjectionHealth.Inconsistent,
                _ => throw new InvalidDataException("Unknown index diagnostic projection health.")
            };
            result[i] = new(item.Group, item.IndexName, ToPublic(item.Projection), ToPublic(item.Direction), health, item.RecommendedAction, item.Reason);
        }
        return Array.AsReadOnly(result);
    }

    /// <summary>Copies exact fixed-generation storage values once; strings are reused and no engine handle survives.<br/></summary>
    /// <param name="source">Disconnected native storage components.<br/></param><returns>Read-only owned fixed storage values.<br/></returns>
    internal static IReadOnlyList<RecordIndexFixedStorageComponent> CopyFixed(IReadOnlyList<LibraDex.LibraDexFixedTopologyStorageComponent> source)
    {
        if (source.Count == 0) return Array.Empty<RecordIndexFixedStorageComponent>();
        var result = new RecordIndexFixedStorageComponent[source.Count];
        for (int i = 0; i < result.Length; i++) { var item = source[i]; result[i] = new(item.Group, item.IndexName, item.PhysicalSlotIndex, item.Generation, item.TupleCount, item.ReachableBytes, item.RouterCount, item.OrdinaryShelfCount, item.DuplicateRunShelfCount, item.TerminalRootCount, item.TerminalShelfCount, item.ShelfExtentSize); }
        return Array.AsReadOnly(result);
    }

    /// <summary>Copies exact variable-text/null-empty route storage values once without estimating obsolete per-index bytes.<br/></summary>
    /// <param name="source">Disconnected native storage components.<br/></param><returns>Read-only owned variable-text storage values.<br/></returns>
    internal static IReadOnlyList<RecordIndexVariableTextStorageComponent> CopyVariableText(IReadOnlyList<LibraDex.LibraDexVariableTextTopologyStorageComponent> source)
    {
        if (source.Count == 0) return Array.Empty<RecordIndexVariableTextStorageComponent>();
        var result = new RecordIndexVariableTextStorageComponent[source.Count];
        for (int i = 0; i < result.Length; i++) { var item = source[i]; result[i] = new(item.Group, item.IndexName, item.PhysicalSlotIndex, item.Generation, item.TupleCount, item.ReachableBytes, item.ReclaimablePayloadBytes, item.RouterCount, item.OrdinaryShelfCount, item.DuplicateRunShelfCount, item.TerminalRootCount, item.TerminalShelfCount, item.KeyStateRootCount); }
        return Array.AsReadOnly(result);
    }
}
