using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using Standart.Hash.xxHash;
using FractalKVS;








namespace AbraxasDB
{
    //====== TYPES ======
    public partial class RecordStore<T> : IRecordIndexOwner
    {
        private static SortBy[] BuildSortDescriptors(bool ignoreCase, params string[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(propPaths);
            if (propPaths.Length == 0)
                return Array.Empty<SortBy>();

            var sorts = new SortBy[propPaths.Length];
            for (var i = 0; i < propPaths.Length; i++)
            {
                sorts[i] = new SortBy(new PropPath(propPaths[i]), ignoreCase: ignoreCase);
            }

            return sorts;
        }
        //Shared/Static Members
        private static void EnsurePropPathCount(string[] propPaths, int expectedCount)
        {
            ArgumentNullException.ThrowIfNull(propPaths);
            if (propPaths.Length != expectedCount)
                throw new ArgumentException($"The number of propPaths ({propPaths.Length}) doesn't equal the number of ValueTuple elements ({expectedCount}).", nameof(propPaths));
        }
        private static void EnsureRecordNotNull(T record, string parameterName)
        {
            if (record is null)
                throw new ArgumentNullException(parameterName, "Records collection contains a null entry.");
        }

        //======  FIELDS  ======
        private const string RecordsParameterName = "records";
        private Timer? autoIndexTrimTimer;
        private Inheto.DeserializationOptions deserializationOptions = null!;
        private Inheto.SerializationOptions serializationOptions = null!;
        private RecordDeserializationOptions recordDeserializationOptions = null!;
        private RecordSerializationOptions recordSerializationOptions = null!;
        internal Inheto.DeserializationOptions NativeDeserializationOptions => deserializationOptions;
        internal Inheto.SerializationOptions NativeSerializationOptions => serializationOptions;








        //======  FIELDS  ======
        private string indexGroupName = null!;








        internal AbraxasDB.RowIDGenerator idGen;
        internal string name;
        internal RecordBase rbase;
        internal byte storeID;

        /// <summary>
        /// Gets the store-level policy that controls reusable auto-created volatile helper indexes.<br/>
        /// Auto indexes are memory-backed, are visible through index metadata, and are removed when their owning store/base closes.<br/>
        /// </summary>
        public AutoIndexingOptions AutoIndexing { get; } = new();

        /// <summary>
        /// Gets the store-level policy controlling behavior when requested ordering cannot use a complete compatible LibraDex index.<br/>
        /// The default preserves low-friction semantics by sorting only the surviving candidate set; performance-sensitive stores may require an index explicitly.<br/>
        /// </summary>
        public OrderingOptions Ordering { get; } = new();

        /// <summary>
        /// Gets the store-owned registry of persistent property links used consistently by ordinary object hydration paths.<br/>
        /// Links are matched by canonical property path or explicit alias rather than by the originally stored CLR root type.<br/>
        /// </summary>
        public LinkCollection Links { get; }








        //======  CONSTRUCTORS  ======
        /// <summary>
        /// Creates one store facade and allows application conversion handlers to be installed before persisted LibraDex index intent is attached.<br/>
        /// The setup callback runs after the store's Inheto option objects and core store state exist, but before any persistent ordinary or partial index is reopened.<br/>
        /// This ordering lets developer-owned Inheto handlers remain the conversion authority for custom indexed values without requiring executable delegates in Abraxas persistence.<br/>
        /// </summary>
        /// <param name="recordBase">The owning record base.<br/></param>
        /// <param name="name">The stable store name.<br/></param>
        /// <param name="storeID">The persisted Fractal store identifier.<br/></param>
        /// <param name="storeRuntime">The shared active runtime generation used to invalidate every facade coherently.<br/></param>
        /// <param name="configureBeforeIndexAttachment">Optional one-time setup callback for Inheto handlers and related custom index conversion contracts.<br/></param>
        internal RecordStore(
            RecordBase recordBase,
            string name,
            byte storeID,
            StoreRuntimeState storeRuntime,
            Action<RecordStore<T>>? configureBeforeIndexAttachment = null
            )
        {
            rbase = recordBase;
            this.name = name;
            this.storeID = storeID;
            this.storeRuntime = storeRuntime;
            idGen = rbase.idGen;
            recordDeserializationOptions = new RecordDeserializationOptions();
            deserializationOptions = recordDeserializationOptions.NativeOptions;
            SerializationOptions = new RecordSerializationOptions();
            Links = new LinkCollection(this);
            InitStore();
            configureBeforeIndexAttachment?.Invoke(this);
            BindLibraDexIdentityIndexes(rbase.GetLibraDexIndexSet(name), LibraDexIndexBackendMode.Preferred);
            rbase.RegisterVolatileIndexCatalog(libraDexVolatileCatalog);
        }








        //====== EVENTS & DELEGATES ======
        /// <summary>
        /// Raised synchronously before condition materialization and execution-plan construction for an operation.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<ConditionEventArgs>? ConditionBuilding;
        /// <summary>
        /// Raised synchronously after the condition has been materialized for the operation and before result traversal.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<ConditionEventArgs>? ConditionBuilt;
        /// <summary>
        /// Raised during execution-time operand materialization; a handler may replace the operand value with a compatible value through the event arguments.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<ConditionParameterEventArgs>? ConditionParameterMaterialized;
        /// <summary>
        /// Raised once after automatic condition planning and before result traversal when a subscriber is present.<br/>
        /// No diagnostic payload is allocated and no diagnostic callback occurs when the event has no subscribers.<br/>
        /// </summary>
        public event EventHandler<ConditionExecutionPlanEventArgs>? ConditionExecutionPlanned;

        /// <summary>
        /// Raised after one FastFind iterator is fully consumed, abandoned, or fails.<br/>
        /// Counters and timing state are allocated only while this event has a subscriber, leaving ordinary FastFind calls free of diagnostic bookkeeping.<br/>
        /// </summary>
        public event EventHandler<FastFindExecutionEventArgs>? FastFindExecutionCompleted;

        internal FastFindFractalParallelStrategy FastFindFractalStrategy { get; set; } = FastFindFractalParallelStrategy.PhysicalPartitions;

        /// <summary>
        /// Raised after Abraxas selects the execution route for one property-ordered identity request.<br/>
        /// The event is diagnostic only and is raised once before ordered identities are traversed.<br/>
        /// </summary>
        public event EventHandler<OrderingExecutionPlanEventArgs>? OrderingExecutionPlanned;
        /// <summary>
        /// Raised after deletion and maintained-index convergence; handler failure does not roll back the completed deletion.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<RecordDeletedEventArgs>? RecordDeleted;
        /// <summary>
        /// Raised before a record is deleted; an exception from a handler can stop the deletion before the physical mutation.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<RecordDeletingEventArgs>? RecordDeleting;
        /// <summary>
        /// Raised after a record has been hydrated into the requested CLR object shape.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<RecordMaterializedEventArgs>? RecordMaterialized;
        /// <summary>
        /// Raised before hydrating a record into the requested CLR object shape.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<RecordMaterializingEventArgs>? RecordMaterializing;
        /// <summary>
        /// Raised after authoritative persistence and maintained-index convergence; handler failure does not roll back the completed write.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<RecordPersistedEventArgs>? RecordPersisted;
        /// <summary>
        /// Raised before serialization and persistence; the event carries the original source object, not a detached copy.<br/>
        /// Handlers run on the executing operation or worker thread; no UI-thread dispatch is provided.<br/>
        /// </summary>
        public event EventHandler<RecordPersistingEventArgs>? RecordPersisting;








        /// <summary>
        /// Returns the total number of records stored for this record store instance.
        /// </summary>
        //------ Public Methods -----
        //======  PROPERTIES  ======
        public ulong Count
        {
            get
            {
                using var operation = EnterStoreOperationScope();
                const ulong StoreIdMask = 0xFF00_0000_0000_0000ul;
                if (TryCountAllRecordsByIndex(out ulong indexedCount)) return indexedCount;
                ulong expectedValue = ((ulong)storeID) << 56;
                return rbase.data.RecordCount(StoreIdMask, expectedValue);
            }
        }

        /// <summary>
        /// Gets the durable Fractal source generation currently represented by this record store's authoritative group.<br/>
        /// The value advances after successful record puts, updates, deletes, grouped resets, and other query-visible source mutations; it does not imply that an external consumer has published related application configuration.<br/>
        /// </summary>
        public ulong SourceUSN
        {
            get
            {
                using var operation = EnterStoreOperationScope();
                return rbase.data.GetUSN(storeID);
            }
        }
        //======  PROPERTIES  ======
        /// <summary>
        /// Gets/sets live record hydration settings owned by Abraxas.<br/>
        /// Replacement rebinds this store's registered links and discards their prepared option forks.<br/>
        /// Finish configuration before concurrent use; neither mutation nor replacement during active reads is supported.<br/>
        /// </summary>
        public RecordDeserializationOptions DeserializationOptions
        {
            get => recordDeserializationOptions;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                Inheto.DeserializationOptions previous = deserializationOptions;
                recordDeserializationOptions = value;
                deserializationOptions = value.NativeOptions;
                Links?.Rebind(previous, deserializationOptions);
            }
        }
        /// <summary>
        /// Gets a fresh condition-group entry point for authoring a grouped condition independently of earlier builder instances.<br/>
        /// </summary>
        public Condition<T>.GroupClause GroupCond => new Condition<T>.GroupClause();
        /// <summary>
        /// Aggregate operators supported by grouped aggregate materialization helpers.<br/>
        /// </summary>
        public enum AggregateFunction
        {
            /// <summary>Returns per-group row counts via <c>COUNT(*)</c>.<br/></summary>
            Count,            /// <summary>Returns per-group sums for the supplied selector expression.<br/></summary>
            Sum,            /// <summary>Returns per-group averages for the supplied selector expression.<br/></summary>
            Average,            /// <summary>Returns per-group minimum values for the supplied selector expression.<br/></summary>
            Min,            /// <summary>Returns per-group maximum values for the supplied selector expression.<br/></summary>
            Max,            /// <summary>Returns per-group distinct counts via <c>COUNT(DISTINCT ...)</c>.<br/></summary>
            DistinctCount
        }
        /// <summary>
        /// Gets or sets the optional delegate used to assign an object identifier to an instance of type <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>The delegate should accept the target object and the identifier value to set. This<br/>
        /// property allows customization of how object identifiers are assigned, which can be useful when integrating<br/>
        /// with different object models or persistence strategies.<br/>
        /// This delegate is called during <see cref="Put(T, long?)"/> execution just before T is serialized so that the ID value is persisted with the object.<br/>
        /// Leave this property <see langword="null"/> when the developer-owned object should not carry the Abraxas identity.
        /// </remarks>
        public Action<T, ulong>? ObjectIDSetter { get; set; }
        /// <summary>
        /// Begins a reusable semantic calculation for this record type.<br/>
        /// The calculation is evaluated only when consumed by a reader, condition, aggregate, or index operation.<br/>
        /// </summary>
        public Selector<T> Calc => new();
        /// <summary>
        /// Gets/sets live record serialization settings owned by Abraxas.<br/>
        /// Register handlers before index attachment when an index depends on their binary representation.<br/>
        /// Finish configuration before concurrent use; replacing these options does not rewrite existing records or indexes.<br/>
        /// </summary>
        public RecordSerializationOptions SerializationOptions
        {
            get => recordSerializationOptions;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                recordSerializationOptions = value;
                serializationOptions = value.NativeOptions;
            }
        }
        /// <summary>
        /// Gets a fresh condition entry point for this record type; property access alone does not execute a query.<br/>
        /// </summary>
        public Condition<T>.WhereClause Where => new Condition<T>.WhereClause();








        /// <summary>
        /// Appends one backend-neutral equality predicate for a materialized grouping key.<br/>
        /// </summary>
        /// <param name="selector">Original direct or durably computed grouping selector used as the left operand.<br/></param>
        /// <param name="value">Materialized grouping value, or <c>null</c> for an explicit null predicate.<br/></param>
        /// <param name="current">Existing grouped-key predicate to extend with <c>AND</c>, or <c>null</c> to begin a new condition.<br/></param>
        /// <returns>The completed predicate fragment including the supplied grouping key.<br/></returns>
        /// <exception cref="NotSupportedException">Thrown when the materialized key type has no backend-neutral equality operator.<br/></exception>
        private Condition<T>.ContinueOrEndCondition? AppendGroupingEquality(ISelector selector, object? value, Condition<T>.ContinueOrEndCondition? current)
        {
            // Start a new WHERE clause or append via AND when chaining.
            Condition<T>.Clause clause = current is null ? Where : current.AND;

            var vts = clause.Calc(selector);

            if (value is null)
                return vts.IsNull;

            var pType = value.GetType();
            switch (System.Type.GetTypeCode(pType))
            {
                case TypeCode.String:
                    return vts.AsString.EqualTo((string)value);
                case TypeCode.Byte:
                    return vts.AsByte.EqualTo((byte)value);
                case TypeCode.SByte:
                    return vts.AsSByte.EqualTo((sbyte)value);
                case TypeCode.Int16:
                    return vts.AsInt16.EqualTo((short)value);
                case TypeCode.UInt16:
                    return vts.AsUInt16.EqualTo((ushort)value);
                case TypeCode.Int32:
                    return vts.AsInt32.EqualTo((int)value);
                case TypeCode.UInt32:
                    return vts.AsUInt32.EqualTo((uint)value);
                case TypeCode.Int64:
                    return vts.AsInt64.EqualTo((long)value);
                case TypeCode.UInt64:
                    return vts.AsUInt64.EqualTo((ulong)value);
                case TypeCode.Single:
                    return vts.AsSingle.EqualTo((float)value);
                case TypeCode.Double:
                    return vts.AsDouble.EqualTo((double)value);
                case TypeCode.Decimal:
                    return vts.AsDecimal.EqualTo((decimal)value);
                case TypeCode.Boolean:
                    return vts.AsBoolean.EqualTo((bool)value);
                case TypeCode.DateTime:
                    return vts.AsDateTime.EqualTo((DateTime)value);
                case TypeCode.Char:
                    return vts.AsString.EqualTo(((char)value).ToString());
                default:
                    if (pType == typeof(Int128))
                        return vts.AsInt128.EqualTo((Int128)value);
                    if (pType == typeof(UInt128))
                        return vts.AsUInt128.EqualTo((UInt128)value);
                    if (pType == typeof(Half))
                        return vts.AsHalf.EqualTo((Half)value);
                    if (pType == typeof(Rune))
                        return vts.AsString.EqualTo(value.ToString()!);
                    if (pType == typeof(Guid))
                        return vts.AsGuid.EqualTo((Guid)value);
                    if (pType == typeof(DateOnly))
                        return vts.AsDateOnly.EqualTo((DateOnly)value);
                    if (pType == typeof(TimeOnly))
                        return vts.AsTimeOnly.EqualTo((TimeOnly)value);
                    if (pType == typeof(TimeSpan))
                        return vts.AsTimeSpan.EqualTo((TimeSpan)value);
                    if (pType == typeof(byte[]))
                        return vts.AsBinary.EqualTo((byte[])value);
                    throw new NotSupportedException(
                        $"Grouping drill-down equality does not support materialized key type '{pType.FullName}'.");
            }
        }

        /// <summary>
        /// Creates an empty result for <see cref="PutManyFast(IEnumerable{T}, long?)"/> indicating no records were added.
        /// </summary>
        //======  METHODS  ======
        private PutManyFastResult CreateEmptyPutManyFastResult()
        {
            var handle = rbase.CreateBatchHandle(0, autoComplete: true);
            return new PutManyFastResult(Array.Empty<ulong>(), handle);
        }

        //======  METHODS  ======
        private void EnsureRecordBelongsToCurrentStore(ulong recordID, string? parameterName = null)
        {
            if (!RowIDGenerator.IsValidRecordID(recordID, storeID))
            {
                throw new ArgumentException(
                    "The recordID provided does not belong to the current store or exceeds this compiled edition's identity capacity.",
                    parameterName ?? nameof(recordID));
            }
        }

        /// <summary>
        /// Single-thread calls remain lazy at the match-stream boundary, while parallel calls use the stream's bounded worker channel and publish worker completion order.<br/>
        /// FastFind is unordered for every route; an observed source-cursor sequence is incidental and must not be treated as a stable contract.<br/>
        /// </summary>
        private IEnumerable<TResult> FindFastCore<TResult>(Condition<T>.EndCondition where, Func<Inheto.InhetoBinary, ulong, TResult> projector, string operationName, int? maxDegreeOfParallelism, int? channelCapacity, bool? useParallel, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(projector);

            cancellationToken.ThrowIfCancellationRequested();
            var (degree, capacity, useParallelWorkers) = ResolveFindFastParallel(maxDegreeOfParallelism, channelCapacity, useParallel);
            int requestedWorkers = useParallelWorkers ? degree : 1;
            FastFindExecutionObserver? observer = FastFindExecutionCompleted is null
                ? null
                : new FastFindExecutionObserver(operationName, requestedWorkers);
            bool completed = false;
            try
            {
                foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                    where,
                    operationName,
                    includePayload: true,
                    maxDegreeOfParallelism: requestedWorkers,
                    channelCapacity: capacity,
                    cancellationToken: cancellationToken,
                    fastFindObserver: observer))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Inheto.InhetoBinary payload = match.Payload
                        ?? throw new InvalidDataException($"{operationName} requested a projected match without a retained Inheto payload.");
                    payload.SetMaterializationContext(match.RecordId);
                    observer?.RecordMatch();
                    yield return projector(payload, match.RecordId);
                }
                completed = true;
            }
            finally
            {
                if (observer is not null)
                    OnFastFindExecutionCompleted(observer, completed);
            }
        }

        /// <summary>
        /// Executes one completed condition for an identity-only FastFind consumer without retaining or materializing result payloads.<br/>
        /// A complete direct LibraDex plan therefore remains entirely identity based; candidate and Fractal-scan plans still read each required payload exactly once to evaluate residual semantics.<br/>
        /// A requested multi-worker direct plan must use independent LibraDex physical partitions; it never routes one shared cursor through cosmetic consumers or silently collapses to one.<br/>
        /// Results remain unordered by contract even when a single selected cursor happens to produce a repeatable physical sequence.<br/>
        /// </summary>
        /// <param name="where">Completed condition to plan and execute.<br/></param>
        /// <param name="operationName">Public operation name reported through condition lifecycle diagnostics.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; the compatibility parameter name is retained for existing named callers.<br/></param>
        /// <param name="channelCapacity">Optional maximum completed-match buffer size.<br/></param>
        /// <param name="useParallel">Optional compatibility switch selecting one worker when false.<br/></param>
        /// <param name="cancellationToken">Token observed by planning, traversal, evaluation, and result consumption.<br/></param>
        /// <returns>Matching authoritative identities; result payloads are never retained for projection.<br/></returns>
        private IEnumerable<ulong> FindFastIdentityCore(
            Condition<T>.EndCondition where,
            string operationName,
            int? maxDegreeOfParallelism,
            int? channelCapacity,
            bool? useParallel,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(where);

            cancellationToken.ThrowIfCancellationRequested();
            var (degree, capacity, useParallelWorkers) = ResolveFindFastParallel(maxDegreeOfParallelism, channelCapacity, useParallel);
            int requestedWorkers = useParallelWorkers ? degree : 1;
            FastFindExecutionObserver? observer = FastFindExecutionCompleted is null
                ? null
                : new FastFindExecutionObserver(operationName, requestedWorkers);
            bool completed = false;
            try
            {
                foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                    where,
                    operationName,
                    includePayload: false,
                    maxDegreeOfParallelism: requestedWorkers,
                    channelCapacity: capacity,
                    cancellationToken: cancellationToken,
                    fastFindObserver: observer))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    observer?.RecordMatch();
                    yield return match.RecordId;
                }
                completed = true;
            }
            finally
            {
                if (observer is not null)
                    OnFastFindExecutionCompleted(observer, completed);
            }
        }

        // Grouping selector abstraction built on Selector<T> for grouping/drill-down semantics.
        //======  METHODS  ======
        private void InitStore()
        {
            indexGroupName = $"{typeof(T).Name}-{name}";
        }

        private IEnumerable<Record> IterateFractalRecordsByGroupKey(byte groupKey)
        {
            using var operation = EnterStoreOperationScope();
            foreach (var record in rbase.data.IterateRecordsByGroupKey(groupKey))
            {
                yield return record;
            }
        }

        private void OnConditionBuilding(string operationName, object condition)
        {
            var handler = ConditionBuilding;
            if (handler is null)
                return;

            handler(this, new ConditionEventArgs(operationName, condition));
        }

        private void OnConditionBuilt(string operationName, object condition)
        {
            var handler = ConditionBuilt;
            if (handler is null)
                return;

            handler(this, new ConditionEventArgs(operationName, condition));
        }

        private object? OnConditionParameterMaterialized(
            string operationName,
            object condition,
            Type conditionType,
            string? propertyPath,
            int? propertyPathOccurrence,
            string? parameterAlias,
            object? value,
            RecordBase? recordBase)
        {
            var handler = ConditionParameterMaterialized;
            if (handler is null)
                return value;

            var args = new ConditionParameterEventArgs(
                operationName,
                condition,
                conditionType,
                propertyPath,
                propertyPathOccurrence,
                parameterAlias,
                value,
                recordBase);

            handler(this, args);
            return args.Value;
        }

        /// <summary>
        /// Raises one opt-in immutable diagnostic describing the selected condition execution path and every leaf's maintained-index disposition.<br/>
        /// The subscriber check occurs before dynamic reason text, index-name arrays, or leaf arrays are created, keeping the default execution path allocation-free with respect to these diagnostics.<br/>
        /// </summary>
        /// <param name="operationName">The public RecordStore operation being executed.<br/></param>
        /// <param name="condition">The completed Abraxas condition.<br/></param>
        /// <param name="planKind">The selected storage execution path.<br/></param>
        /// <param name="libraDexCondition">The direct or candidate LibraDex condition used by the plan, or null for a complete scan.<br/></param>
        /// <param name="reasonPrefix">The complete static reason, or the text preceding a dynamic index name.<br/></param>
        /// <param name="reasonIndexName">An existing index name to include only when a listener is present; otherwise null.<br/></param>
        /// <param name="reasonSuffix">The text following <paramref name="reasonIndexName"/>, when supplied.<br/></param>
        private void OnConditionExecutionPlanned(
            string operationName,
            Condition<T>.EndCondition condition,
            ConditionExecutionPlanKind planKind,
            LibraDex.LibraDexConditionEndCondition? libraDexCondition,
            string reasonPrefix,
            string? reasonIndexName = null,
            string? reasonSuffix = null)
        {
            var handler = ConditionExecutionPlanned;
            if (handler is null)
                return;

            string reason = reasonIndexName is null
                ? reasonPrefix
                : string.Concat(reasonPrefix, reasonIndexName, reasonSuffix);

            var physicalIndexNames = libraDexCondition?.Leaves
                .Select(leaf => leaf.IndexName)
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            var usedIndexes = physicalIndexNames.ToHashSet(StringComparer.Ordinal);
            var indexNames = physicalIndexNames
                .Select(GetLibraDexDiagnosticIndexName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var leaves = new List<ConditionExecutionLeafDiagnostic>();
            condition.builder.VisitLibraDexLeafDiagnostics(
                HasAnyLibraDexIndex,
                TryResolveAnyLibraDexDerivedSelector,
                (leafType, indexName, indexAvailable) =>
                {
                    var used = indexName is not null && usedIndexes.Contains(indexName);
                    string? diagnosticIndexName = indexName is null
                        ? null
                        : GetLibraDexDiagnosticIndexName(indexName);
                    var leafReason = diagnosticIndexName is null
                        ? "The leaf has no direct property index identity or matching maintained derived projection."
                        : !indexAvailable
                            ? $"The resolved index '{diagnosticIndexName}' is not available in the bound LibraDex identity group."
                            : used
                                ? planKind == ConditionExecutionPlanKind.IndexDirect
                                    ? $"The maintained index '{diagnosticIndexName}' executes this leaf in the complete direct tree."
                                    : $"The maintained index '{diagnosticIndexName}' supplies candidates; the complete condition is evaluated as a residual."
                                : $"The maintained index '{diagnosticIndexName}' is available, but this leaf/operator is not executable by the selected LibraDex plan or cannot participate safely in the complete logical tree.";
                    leaves.Add(new ConditionExecutionLeafDiagnostic(
                        leafType,
                        diagnosticIndexName,
                        indexAvailable,
                        used,
                        leafReason));
                });

            handler(this, new ConditionExecutionPlanEventArgs(
                operationName,
                condition,
                planKind,
                indexNames,
                leaves.ToArray(),
                reason));
        }

        /// <summary>
        /// Publishes one completed opt-in FastFind execution measurement.<br/>
        /// The caller supplies an operation-scoped observer only when a subscriber existed at iterator start; a subscriber removed before completion is handled as a no-op.<br/>
        /// </summary>
        /// <param name="observer">Operation-scoped counters and timing state.<br/></param>
        /// <param name="completed">Whether the consumer drained the iterator successfully.<br/></param>
        private void OnFastFindExecutionCompleted(FastFindExecutionObserver observer, bool completed)
        {
            ArgumentNullException.ThrowIfNull(observer);
            FastFindExecutionCompleted?.Invoke(this, observer.Complete(completed));
        }

        /// <summary>
        /// Raises one opt-in immutable diagnostic describing the selected complete-store ordering route.<br/>
        /// The subscriber check occurs before property-path arrays are allocated so ordinary ordering pays no diagnostic allocation cost.<br/>
        /// </summary>
        /// <param name="operationName">Public RecordStore operation selecting the order.<br/></param>
        /// <param name="planKind">Abraxas-level ordering execution route.<br/></param>
        /// <param name="sort">Requested property-order descriptors.<br/></param>
        /// <param name="completeStorePopulation">Whether the requested source is the complete logical store population.<br/></param>
        /// <param name="reason">Concise explanation of the selected route.<br/></param>
        private void OnOrderingExecutionPlanned(
            string operationName,
            OrderingExecutionPlanKind planKind,
            SortBy[] sort,
            bool completeStorePopulation,
            string reason)
        {
            var handler = OrderingExecutionPlanned;
            if (handler is null)
                return;

            string[] propertyPaths = new string[sort.Length];
            for (int i = 0; i < sort.Length; i++)
                propertyPaths[i] = sort[i].Key.Path;

            handler(this, new OrderingExecutionPlanEventArgs(
                operationName,
                planKind,
                propertyPaths,
                completeStorePopulation,
                reason));
        }

        /// <summary>
        /// Raises one opt-in immutable diagnostic for a direct or calculated sorted-reader request.<br/>
        /// Runtime-only selectors use their non-durable diagnostic rendering and are never promoted to persistent index identities.<br/>
        /// The subscriber check occurs before ordering-key arrays are allocated so ordinary reader construction remains allocation-free when diagnostics are unused.<br/>
        /// </summary>
        /// <param name="operationName">Public RecordStore operation selecting the order.<br/></param>
        /// <param name="planKind">Abraxas-level ordering execution route.<br/></param>
        /// <param name="order">Requested calculated ordering descriptors.<br/></param>
        /// <param name="completeStorePopulation">Whether the requested source is the complete logical store population.<br/></param>
        /// <param name="reason">Concise explanation of the selected route.<br/></param>
        private void OnCalculatedOrderingExecutionPlanned(
            string operationName,
            OrderingExecutionPlanKind planKind,
            IndexSelectorBuilder[] order,
            bool completeStorePopulation,
            string reason)
        {
            var handler = OrderingExecutionPlanned;
            if (handler is null)
                return;

            string[] orderingKeys = new string[order.Length];
            for (int i = 0; i < order.Length; i++)
                orderingKeys[i] = order[i].CalculatedSelector.ToString() ?? "<calculated selector>";

            handler(this, new OrderingExecutionPlanEventArgs(
                operationName,
                planKind,
                orderingKeys,
                completeStorePopulation,
                reason));
        }

        private IEnumerable<(ulong ID, TResult Record)> IterateWithIdsCore<TResult>(IEnumerable<ulong> ids, Func<ulong, TResult> materializer)
        {
            foreach (var id in ids)
            {
                yield return (id, materializer(id));
            }
        }

        private void OnRecordDeleted(ulong recordID, bool zeroWrite, string operationName)
        {
            var handler = RecordDeleted;
            if (handler is null)
                return;

            handler(this, new RecordDeletedEventArgs(operationName, recordID, zeroWrite));
        }

        private void OnRecordDeleting(ulong recordID, bool zeroWrite, string operationName)
        {
            var handler = RecordDeleting;
            if (handler is null)
                return;

            handler(this, new RecordDeletingEventArgs(operationName, recordID, zeroWrite));
        }

        private void OnRecordMaterialized(ulong recordID, Type requestedType, object? record, string operationName)
        {
            var handler = RecordMaterialized;
            if (handler is null)
                return;

            handler(this, new RecordMaterializedEventArgs(operationName, recordID, requestedType, record));
        }

        private void OnRecordMaterializing(ulong recordID, Type requestedType, string operationName)
        {
            var handler = RecordMaterializing;
            if (handler is null)
                return;

            handler(this, new RecordMaterializingEventArgs(operationName, recordID, requestedType));
        }

        private void OnRecordPersisted(ulong recordID, object record, long? state, bool isNewRecord, string operationName, bool usesFastPath)
        {
            var handler = RecordPersisted;
            if (handler is null)
                return;

            handler(this, new RecordPersistedEventArgs(operationName, recordID, record, state, isNewRecord, usesFastPath));
        }

        private void OnRecordPersisting(ulong recordID, object record, long? state, bool isNewRecord, string operationName, bool usesFastPath)
        {
            var handler = RecordPersisting;
            if (handler is null)
                return;

            handler(this, new RecordPersistingEventArgs(operationName, recordID, record, state, isNewRecord, usesFastPath));
        }

        private ulong PersistRecord(
            ulong recordID,
            T record,
            long? state,
            string operationName,
            bool isNewRecord,
            bool usesFastPath = false,
            LinkWritePlan? linkWrite = null,
            bool replaceStateWhenMissing = false)
        {
            ArgumentNullException.ThrowIfNull(record);
            rbase.EnsureWriteAllowed(operationName);
            EnsureFunctionIndexProjectionsAvailable(operationName);
            using var partialIndexMutation = EnterPartialIndexMutationScope();
            RecordStoreMutationDiagnosticsAccumulator? diagnostics = storeRuntime.ActiveMutationDiagnostics;
            long recordStarted = diagnostics is null ? 0 : Stopwatch.GetTimestamp();
            long allocatedBefore = diagnostics is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
            bool completed = false;
            try
            {
                EnsureLibraDexConvergence(operationName);
                Links.ValidateWrite(linkWrite);

                ObjectIDSetter?.Invoke(record, recordID);
                OnRecordPersisting(recordID, record, state, isNewRecord, operationName, usesFastPath);

                Inheto.InhetoBinary? priorBinary = !isNewRecord && linkWrite?.NeedsExisting == true
                    ? GetInhetoBinary(recordID, operationName)
                    : null;
                long serializationStarted = diagnostics is null ? 0 : Stopwatch.GetTimestamp();
                Inheto.SerializationOptions serializationOptions = Links.PrepareWrite(
                    NativeSerializationOptions,
                    record,
                    priorBinary,
                    isNewRecord,
                    linkWrite);
                var binary = Inheto.InhetoBinary.FromObject(record, serializationOptions);
                Links.ValidateWrite(binary, linkWrite);
                if (diagnostics is not null)
                    diagnostics.RecordSerialization(Stopwatch.GetTimestamp() - serializationStarted);

                long functionKeysStarted = diagnostics is null ? 0 : Stopwatch.GetTimestamp();
                PreparedFunctionIndexKeys functionKeys = PrepareFunctionIndexKeys(record, binary);
                if (diagnostics is not null)
                    diagnostics.RecordFunctionKeyPreparation(Stopwatch.GetTimestamp() - functionKeysStarted);
                try
                {
                    PersistSerializedRecord(
                        recordID,
                        binary,
                        state,
                        operationName,
                        isNewRecord,
                        functionKeys,
                        priorBinary,
                        replaceStateWhenMissing);
                }
                finally
                {
                    functionKeys.Dispose();
                }
                OnRecordPersisted(recordID, record, state, isNewRecord, operationName, usesFastPath);
                completed = true;
                return recordID;
            }
            finally
            {
                if (diagnostics is not null)
                {
                    diagnostics.RecordRecordWork(
                        Stopwatch.GetTimestamp() - recordStarted,
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                        completed);
                }
            }
        }

        /// <summary>
        /// Replaces an existing record with an authenticated portable stored payload while retaining its exact ciphertext bytes.<br/>
        /// The caller has already decoded <paramref name="binary"/> from <paramref name="storedPayload"/>, proving the package is authentic and supplying the exact INHETO view used for every index preflight and update.<br/>
        /// This deliberately bypasses logical link-write transformation because replication preserves the source's already serialized link payloads; ordinary imports continue through <see cref="PersistRecord"/>.<br/>
        /// </summary>
        /// <param name="recordID">Existing destination identity, identical to the authenticated source identity.<br/></param>
        /// <param name="record">Materialized source record used for lifecycle notifications and function-index projections.<br/></param>
        /// <param name="binary">Authenticated, decoded source INHETO binary matching the retained stored payload.<br/></param>
        /// <param name="storedPayload">Exact portable ciphertext to retain in Fractal without destination re-encryption.<br/></param>
        /// <param name="state">Optional imported persistent State value.<br/></param>
        /// <param name="operationName">Public operation name used by diagnostics and lifecycle events.<br/></param>
        /// <returns>The existing destination identity after Fractal, State, and LibraDex convergence.<br/></returns>
        private ulong PersistPortableStoredPayload(
            ulong recordID,
            T record,
            Inheto.InhetoBinary binary,
            ReadOnlyMemory<byte> storedPayload,
            long? state,
            string operationName)
        {
            ArgumentNullException.ThrowIfNull(record);
            rbase.EnsureWriteAllowed(operationName);
            EnsureFunctionIndexProjectionsAvailable(operationName);
            using var partialIndexMutation = EnterPartialIndexMutationScope();
            RecordStoreMutationDiagnosticsAccumulator? diagnostics = storeRuntime.ActiveMutationDiagnostics;
            long recordStarted = diagnostics is null ? 0 : Stopwatch.GetTimestamp();
            long allocatedBefore = diagnostics is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
            bool completed = false;
            try
            {
                EnsureLibraDexConvergence(operationName);
                ObjectIDSetter?.Invoke(record, recordID);
                OnRecordPersisting(recordID, record, state, isNewRecord: false, operationName: operationName, usesFastPath: false);

                long functionKeysStarted = diagnostics is null ? 0 : Stopwatch.GetTimestamp();
                PreparedFunctionIndexKeys functionKeys = PrepareFunctionIndexKeys(record, binary);
                if (diagnostics is not null)
                    diagnostics.RecordFunctionKeyPreparation(Stopwatch.GetTimestamp() - functionKeysStarted);
                try
                {
                    PersistSerializedRecord(
                        recordID,
                        binary,
                        state,
                        operationName,
                        isNewRecord: false,
                        functionKeys: functionKeys,
                        replaceStateWhenMissing: true,
                        storedPayloadOverride: storedPayload);
                }
                finally
                {
                    functionKeys.Dispose();
                }

                OnRecordPersisted(recordID, record, state, isNewRecord: false, operationName: operationName, usesFastPath: false);
                completed = true;
                return recordID;
            }
            finally
            {
                if (diagnostics is not null)
                {
                    diagnostics.RecordRecordWork(
                        Stopwatch.GetTimestamp() - recordStarted,
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                        completed);
                }
            }
        }

        /// <summary>
        /// Allocates and persists one new record while holding the same store-local mutation scope used by partial-index uniqueness checks and physical maintenance.<br/>
        /// Keeping allocation inside that scope preserves allocation-to-persistence order for contending writers, preventing a higher freshly allocated identity from reaching Fractal before a lower identity that later fails uniqueness preflight.<br/>
        /// The nested <see cref="PersistRecord"/> scope is intentionally reentrant so every existing persistence invariant remains centralized in one implementation.<br/>
        /// </summary>
        /// <param name="record">Non-null record to serialize and persist.<br/></param>
        /// <param name="state">Optional developer State value.<br/></param>
        /// <param name="operationName">Public operation name used by diagnostics and lifecycle events.<br/></param>
        /// <param name="usesFastPath">Whether lifecycle diagnostics should identify this as a fast-path write.<br/></param>
        /// <param name="linkWrite">Optional prepared link-write plan used when serializing this record and its linked data domains.<br/></param>
        /// <returns>The newly allocated and successfully persisted record identity.<br/></returns>
        private ulong PersistNewRecord(
            T record,
            long? state,
            string operationName,
            bool usesFastPath = false,
            LinkWritePlan? linkWrite = null)
        {
            using var partialIndexMutation = EnterPartialIndexMutationScope();
            var recordID = idGen.GetRowID(storeID);
            return PersistRecord(
                recordID,
                record,
                state,
                operationName,
                isNewRecord: true,
                usesFastPath,
                linkWrite);
        }

        /// <summary>
        /// Applies one already-serialized record to authoritative Fractal storage and then converges every maintained LibraDex companion.<br/>
        /// The caller owns record validation, lifecycle notification, serialization, and any explicitly requested compatibility work.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record being persisted.<br/></param>
        /// <param name="binary">Single Inheto serialization reused for Fractal persistence and LibraDex tuple extraction.<br/></param>
        /// <param name="state">Optional developer State value maintained by the reserved LibraDex State index.<br/></param>
        /// <param name="operationName">Operation name used by convergence diagnostics.<br/></param>
        /// <param name="isNewRecord">Whether the identity has no prior authoritative Fractal payload.<br/></param>
        /// <param name="functionKeys">Pooled function projection keys evaluated once from the CLR object already supplied to Abraxas.<br/></param>
        /// <param name="replaceStateWhenMissing">Whether a missing <paramref name="state"/> clears rather than preserves the existing persistent State value.<br/></param>
        /// <param name="storedPayloadOverride">Exact authenticated stored payload to retain instead of applying the destination encoder; used only for validated same-identity portable replay.<br/></param>
        /// <param name="knownPriorBinary">Optional already-read prior record payload reused for old-key removal; null reads it from Fractal for an existing record.<br/></param>
        private void PersistSerializedRecord(
            ulong recordID,
            Inheto.InhetoBinary binary,
            long? state,
            string operationName,
            bool isNewRecord,
            PreparedFunctionIndexKeys functionKeys,
            Inheto.InhetoBinary? knownPriorBinary = null,
            bool replaceStateWhenMissing = false,
            ReadOnlyMemory<byte>? storedPayloadOverride = null)
        {
            RecordStoreMutationDiagnosticsAccumulator? diagnostics = storeRuntime.ActiveMutationDiagnostics;
            var payload = binary.RawBytes;
            ValidateLibraDexPartialUniqueUpsert(recordID, binary, isNewRecord);
            ValidateLibraDexIndexUpsert(recordID, binary, functionKeys);
            ValidateLibraDexCompositeIndexUpsert(recordID, binary);
            Inheto.InhetoBinary? priorBinary = isNewRecord
                ? null
                : knownPriorBinary ?? GetInhetoBinary(recordID, operationName);

            storeRuntime.ClearAppliedIndexSourceUsn();
            PendingStateIntent? retainedStateIntent = null;
            if (state.HasValue)
                retainedStateIntent = RetainSetStateIntent(recordID, state.Value, payload.AsReadOnlyMemory);
            else if (replaceStateWhenMissing)
                retainedStateIntent = RetainClearStateIntent(recordID);

            long fractalWriteStarted = diagnostics is null ? 0 : Stopwatch.GetTimestamp();
            try
            {
                ReadOnlyMemory<byte> storedPayload = storedPayloadOverride ?? EncodeRecordPayload(recordID, payload.AsReadOnlyMemory);
                rbase.data.PutOne(recordID, storedPayload.Span);
            }
            catch (Exception fractalFailure)
            {
                if (retainedStateIntent.HasValue)
                {
                    try
                    {
                        ClearPendingStateIntent(retainedStateIntent.Value);
                    }
                    catch (Exception clearFailure)
                    {
                        throw new AggregateException(fractalFailure, clearFailure);
                    }
                }
                throw;
            }
            finally
            {
                if (diagnostics is not null)
                    diagnostics.RecordFractalWrite(Stopwatch.GetTimestamp() - fractalWriteStarted);
            }

            int maintainedTargets = GetMaintainedLibraDexMutationTargetCount();
            if (priorBinary is not null)
                maintainedTargets += GetMaintainedLibraDexMutationTargetCount();
            if (state.HasValue || replaceStateWhenMissing)
                maintainedTargets++;
            int checkpointPublications = 0;
            long libraDexStarted = diagnostics is null ? 0 : Stopwatch.GetTimestamp();
            try
            {
                if (priorBinary is not null)
                    TryApplyLibraDexDelete(recordID, priorBinary);
                TryApplyLibraDexUpsert(recordID, binary, functionKeys);
                if (state.HasValue)
                    TryApplyLibraDexStateUpsert(recordID, state);
                else if (replaceStateWhenMissing)
                    TryApplyLibraDexStateDelete(recordID);
                if (retainedStateIntent.HasValue)
                    ClearPendingStateIntent(retainedStateIntent.Value);
                checkpointPublications = 1;
                SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
            }
            catch (Exception indexFailure)
            {
                TryRepairLibraDexAfterFractalMutation(
                    operationName,
                    recordID,
                    indexFailure,
                    retainedStateIntent.HasValue);
            }
            finally
            {
                if (diagnostics is not null)
                {
                    diagnostics.RecordLibraDexMaintenance(
                        Stopwatch.GetTimestamp() - libraDexStarted,
                        maintainedTargets,
                        checkpointPublications);
                }
            }
        }

        private PutManyFastResult PutManyFastFromList(
            IReadOnlyList<T> source,
            int recordCount,
            long? state,
            PutManyFastOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (recordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count must be non-negative.");
            if (recordCount == 0)
                return CreateEmptyPutManyFastResult();
            if (recordCount > source.Count)
                throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count exceeds the number of available records.");

            if (TryPutManyFastConcurrentFractal(source, recordCount, state, options, out PutManyFastResult concurrentResult))
                return concurrentResult;

            var idsBuffer = new ulong[recordCount];
            var batchHandle = rbase.CreateBatchHandle(recordCount, autoComplete: true);
            var config = rbase.config?.FractalStoreConfig;
            var threshold = config?.PutManyParallelMinRecordCount ?? int.MaxValue;
            int workerCount = ResolvePutManyFastWorkerCount(options);
            var useParallel = workerCount > 1 &&
                (recordCount >= threshold || options?.MaximumDegreeOfParallelism.HasValue == true);
            PutManyFastProgressReporter? progress = PutManyFastProgressReporter.Create(options, recordCount, workerCount);
            progress?.BeginPhase(PutManyFastRoute.PerRecordFallback, PutManyFastPhase.WritingFallbackRecords);

            if (useParallel)
            {
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = workerCount };
                int completedRecords = 0;

                Parallel.For(0, recordCount, parallelOptions, i =>
                {
                    var record = source[i];
                    EnsureRecordNotNull(record, RecordsParameterName);

                    idsBuffer[i] = PersistNewRecord(
                        record,
                        state,
                        nameof(PutManyFast),
                        usesFastPath: true);
                    progress?.Report(Interlocked.Increment(ref completedRecords));
                });
            }
            else
            {
                for (var i = 0; i < recordCount; i++)
                {
                    var record = source[i];
                    EnsureRecordNotNull(record, RecordsParameterName);

                    idsBuffer[i] = PersistNewRecord(
                        record,
                        state,
                        nameof(PutManyFast),
                        usesFastPath: true);
                    progress?.Report(i + 1);
                }
            }

            progress?.CompletePhase(recordCount);
            progress?.CompleteOperation();

            return new PutManyFastResult(Array.AsReadOnly(idsBuffer), batchHandle);
        }

        /// <summary>
        /// Persists records from an <see cref="IList{T}"/> source without first copying the list into an array.<br/>
        /// This exists for list implementations that expose indexed access but do not implement <see cref="IReadOnlyList{T}"/>.<br/>
        /// The implementation intentionally mirrors <see cref="PutManyFastFromList"/> so the hot loop keeps direct indexer access and avoids delegate or adapter dispatch per record.<br/>
        /// </summary>
        /// <param name="source">Indexed mutable-list source containing records to persist.<br/></param>
        /// <param name="recordCount">Number of records to persist from the start of <paramref name="source"/>.<br/></param>
        /// <param name="state">Optional state value currently reserved for legacy metadata compatibility.<br/></param>
        /// <param name="options">Optional bulk-write controls applied by the centralized batch execution path.<br/></param>
        /// <returns>Generated identifiers and the completed indexing batch handle.<br/></returns>
        private PutManyFastResult PutManyFastFromMutableList(
            IList<T> source,
            int recordCount,
            long? state,
            PutManyFastOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (recordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count must be non-negative.");
            if (recordCount == 0)
                return CreateEmptyPutManyFastResult();
            if (recordCount > source.Count)
                throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count exceeds the number of available records.");

            var idsBuffer = new ulong[recordCount];
            var batchHandle = rbase.CreateBatchHandle(recordCount, autoComplete: true);
            var config = rbase.config?.FractalStoreConfig;
            var threshold = config?.PutManyParallelMinRecordCount ?? int.MaxValue;
            int workerCount = ResolvePutManyFastWorkerCount(options);
            var useParallel = workerCount > 1 &&
                (recordCount >= threshold || options?.MaximumDegreeOfParallelism.HasValue == true);
            PutManyFastProgressReporter? progress = PutManyFastProgressReporter.Create(options, recordCount, workerCount);
            progress?.BeginPhase(PutManyFastRoute.PerRecordFallback, PutManyFastPhase.WritingFallbackRecords);

            if (useParallel)
            {
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = workerCount };
                int completedRecords = 0;

                Parallel.For(0, recordCount, parallelOptions, i =>
                {
                    var record = source[i];
                    EnsureRecordNotNull(record, RecordsParameterName);

                    idsBuffer[i] = PersistNewRecord(
                        record,
                        state,
                        nameof(PutManyFast),
                        usesFastPath: true);
                    progress?.Report(Interlocked.Increment(ref completedRecords));
                });
            }
            else
            {
                for (var i = 0; i < recordCount; i++)
                {
                    var record = source[i];
                    EnsureRecordNotNull(record, RecordsParameterName);

                    idsBuffer[i] = PersistNewRecord(
                        record,
                        state,
                        nameof(PutManyFast),
                        usesFastPath: true);
                    progress?.Report(i + 1);
                }
            }

            progress?.CompletePhase(recordCount);
            progress?.CompleteOperation();

            return new PutManyFastResult(Array.AsReadOnly(idsBuffer), batchHandle);
        }

        private PutManyFastResult PutManyFastStreaming(
            IEnumerable<T> records,
            int recordCount,
            long? state,
            PutManyFastOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(records);
            if (recordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count must be non-negative.");
            if (recordCount == 0)
                return CreateEmptyPutManyFastResult();

            var idsBuffer = new ulong[recordCount];
            var batchHandle = rbase.CreateBatchHandle(recordCount, autoComplete: true);
            int workerCount = ResolvePutManyFastWorkerCount(options);
            PutManyFastProgressReporter? progress = PutManyFastProgressReporter.Create(options, recordCount, workerCount);
            progress?.BeginPhase(PutManyFastRoute.PerRecordFallback, PutManyFastPhase.WritingFallbackRecords);
            using var enumerator = records.GetEnumerator();

            var index = 0;
            while (index < recordCount)
            {
                if (!enumerator.MoveNext())
                    throw new ArgumentException("Record count exceeds the number of available records.", nameof(recordCount));

                var record = enumerator.Current;
                EnsureRecordNotNull(record, RecordsParameterName);

                idsBuffer[index] = PersistNewRecord(
                    record,
                    state,
                    nameof(PutManyFast),
                    usesFastPath: true);

                index++;
                progress?.Report(index);
            }

            progress?.CompletePhase(recordCount);
            progress?.CompleteOperation();

            return new PutManyFastResult(Array.AsReadOnly(idsBuffer), batchHandle);
        }

        private (int Degree, int Capacity, bool UseParallel) ResolveFindFastParallel(int? maxDegreeOfParallelism, int? channelCapacity, bool? useParallel)
        {
            var config = rbase.config;
            bool explicitWorkerCount = maxDegreeOfParallelism.HasValue;
            int degree = maxDegreeOfParallelism
                ?? (useParallel == true
                    ? config?.FractalStoreConfig.GetManyParallelThreadCount ?? Environment.ProcessorCount
                    : 1);
            if (degree < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Degree must be at least 1.");

            if (config?.MaxFindFastParallelThreadCount is int cap && cap > 0 && degree > cap)
            {
                if (explicitWorkerCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maxDegreeOfParallelism),
                        degree,
                        $"The requested exact FastFind worker count exceeds the configured limit of {cap}.");
                }

                degree = cap;
            }

            if (useParallel == false)
            {
                if (explicitWorkerCount && degree != 1)
                {
                    throw new ArgumentException(
                        "FastFind useParallel=false conflicts with an exact worker count greater than one.",
                        nameof(useParallel));
                }

                degree = 1;
            }
            var capacity = channelCapacity ?? Math.Max(degree * 4, 32);
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(channelCapacity), "Channel capacity must be at least 1.");

            return (degree, capacity, degree > 1);
        }

        private void RunOnlineDefragUntilIdle(TimeSpan timeout)
        {
            rbase.data.StartOnlineDefrag();

            try
            {
                var sw = Stopwatch.StartNew();

                while (sw.Elapsed < timeout)
                {
                    var stats = rbase.data.GetOnlineDefragStats();
                    if (!stats.IsRunning || stats.State == FractalStore.OnlineDefragState.Stopped)
                        break;

                    if (stats.Iterations > 0 && stats.State == FractalStore.OnlineDefragState.IdleBackoff)
                        break;

                    Thread.Sleep(100);
                }
            }
            finally
            {
                rbase.data.StopOnlineDefrag();
            }
        }

        private IDisposable SubscribeToConditionParameters<TCondition>(Condition<TCondition>.EndCondition condition, string operationName)
        {
            if (ConditionParameterMaterialized is null)
                return ConditionParameterSubscriptionSentinel.Instance;

            EventHandler<Condition<TCondition>.ParameterMaterializedEventArgs>? handler = null;
            handler = (_, args) =>
            {
                args.Value = OnConditionParameterMaterialized(
                    operationName,
                    condition,
                    typeof(TCondition),
                    args.PropertyPath,
                    args.PropertyPathOccurrence,
                    args.ParameterAlias,
                    args.Value,
                    args.RecordBase);
            };

            condition.ParameterMaterialized += handler;
            return new ScopedDelegateDisposable(() => condition.ParameterMaterialized -= handler);
        }

        /// <summary>
        /// Returns the number of records that satisfy the specified condition using automatic condition execution planning.<br/>
        /// A completely indexable condition uses LibraDex; otherwise Abraxas walks this store's Fractal records and evaluates the same typed condition against their Inheto payloads.<br/>
        /// </summary>
        /// <param name="Where">Condition built by the Abraxas condition builder.<br/></param>
        /// <returns>The number of matching records reported by LibraDex or the Fractal/Inheto scan fallback.<br/></returns>
        public ulong CountWhere(Condition<T>.EndCondition Where)
        {
            ArgumentNullException.ThrowIfNull(Where);

            ulong count = 0;
            foreach (PlannedConditionMatch _ in IteratePlannedConditionMatches(
                Where,
                nameof(CountWhere),
                includePayload: false,
                countResult: nativeCount => count = nativeCount))
            {
                count = checked(count + 1);
            }

            return count;
        }

        /// <summary>
        /// Determines whether at least one authoritative record satisfies the specified condition.<br/>
        /// Execution uses the same direct LibraDex, candidate-plus-residual, or Fractal/Inheto plan as other condition consumers and stops after the first matching identity.<br/>
        /// No matching record is materialized into <typeparamref name="T"/>.<br/>
        /// </summary>
        /// <param name="where">Condition built by the Abraxas condition builder.<br/></param>
        /// <returns><see langword="true"/> when the condition produces at least one authoritative match; otherwise <see langword="false"/>.<br/></returns>
        public bool Exists(Condition<T>.EndCondition where)
        {
            ArgumentNullException.ThrowIfNull(where);

            foreach (PlannedConditionMatch _ in IteratePlannedConditionMatches(
                where,
                nameof(Exists),
                includePayload: false))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Counts distinct key values present in the specified Abraxas index.<br/>
        /// This is intended for non-unique indexes where many records can share one key; unique indexes usually return the indexed record count except for missing-key policy.<br/>
        /// </summary>
        /// <param name="index">Index handle returned by index creation or lookup.<br/></param>
        /// <returns>Number of distinct key values present in the index.<br/></returns>
        public ulong CountDistinct(RecordIndex index)
        {
            if (TryCountDistinctByLibraDex(index, out var count))
                return count;

            throw new NotSupportedException("CountDistinct requires a registered ordered index for the selected property path.");
        }

        /// <summary>
        /// Counts distinct key values present in the named Abraxas index.<br/>
        /// The identifier may be an index name, physical index name, or property path that resolves to an ordered index.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Index name or property path.<br/></param>
        /// <returns>Number of distinct key values present in the index.<br/></returns>
        public ulong CountDistinct(string indexIdentifier)
        {
            if (!TryGetOrCreateAutoLibraDexIndex(indexIdentifier, AutoIndexUse.Distinct, out var index))
                throw new NotSupportedException($"CountDistinct requires an ordered index or auto-index support for simple property path '{indexIdentifier}'.");

            return CountDistinct(index);
        }

        /// <summary>
        /// Counts distinct key values present in the Abraxas index for the specified property path.<br/>
        /// The property path must resolve to an ordered index; this method does not create a hidden index or temporary sort structure.<br/>
        /// </summary>
        /// <param name="key">Property path identifying the indexed key.<br/></param>
        /// <returns>Number of distinct key values present in the index.<br/></returns>
        public ulong CountDistinct(PropPath key)
        {
            if (!TryGetOrCreateAutoLibraDexIndex(key.Path, AutoIndexUse.Distinct, out var index))
                throw new NotSupportedException($"CountDistinct requires an ordered index or auto-index support for simple property path '{key.Path}'.");

            return CountDistinct(index);
        }

        /// <summary>
        /// Counts distinct key values in the specified Abraxas index among records matching a LibraDex-translatable condition.<br/>
        /// Distinctness is always based on <paramref name="index"/>; it is not inferred from the condition.<br/>
        /// </summary>
        /// <param name="index">Index handle that provides the distinct key.<br/></param>
        /// <param name="where">Condition limiting which records participate in the count.<br/></param>
        /// <returns>Number of distinct indexed key values among matching records.<br/></returns>
        public ulong CountDistinctWhere(RecordIndex index, Condition<T>.EndCondition where)
        {
            ArgumentNullException.ThrowIfNull(where);

            if (TryCountDistinctWhereByLibraDex(index, where, out var count))
                return count;

            throw new NotSupportedException("CountDistinctWhere requires a LibraDex ordered index and a condition shape that can be translated to LibraDex.");
        }

        /// <summary>
        /// Counts distinct key values in the named Abraxas index among records matching a LibraDex-translatable condition.<br/>
        /// The identifier may be an index name, physical index name, or property path that resolves to an ordered index.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Index name or property path that provides the distinct key.<br/></param>
        /// <param name="where">Condition limiting which records participate in the count.<br/></param>
        /// <returns>Number of distinct indexed key values among matching records.<br/></returns>
        public ulong CountDistinctWhere(string indexIdentifier, Condition<T>.EndCondition where)
        {
            if (!TryGetOrCreateAutoLibraDexIndex(indexIdentifier, AutoIndexUse.Distinct, out var index))
                throw new NotSupportedException($"CountDistinctWhere requires an ordered index or auto-index support for simple property path '{indexIdentifier}'.");

            return CountDistinctWhere(index, where);
        }

        /// <summary>
        /// Counts distinct key values in the Abraxas index for the specified property path among records matching a LibraDex-translatable condition.<br/>
        /// The property path must resolve to an ordered index; this method does not create a hidden index or temporary sort structure.<br/>
        /// </summary>
        /// <param name="key">Property path identifying the indexed key.<br/></param>
        /// <param name="where">Condition limiting which records participate in the count.<br/></param>
        /// <returns>Number of distinct indexed key values among matching records.<br/></returns>
        public ulong CountDistinctWhere(PropPath key, Condition<T>.EndCondition where)
        {
            if (!TryGetOrCreateAutoLibraDexIndex(key.Path, AutoIndexUse.Distinct, out var index))
                throw new NotSupportedException($"CountDistinctWhere requires an ordered index or auto-index support for simple property path '{key.Path}'.");

            return CountDistinctWhere(index, where);
        }

        /// <summary>
        /// Determines whether authoritative Fractal storage contains the supplied record identity for this store.<br/>
        /// </summary>
        /// <param name="recordID">Identifier whose encoded store key must match this store.<br/></param>
        /// <returns><see langword="true"/> when Fractal contains the record payload; otherwise <see langword="false"/>.<br/></returns>
        public bool Exists(ulong recordID)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            return rbase.data.RecordExists(recordID);
        }

        /// <summary>
        /// Assigns developer-owned State metadata to an existing record without rewriting or deserializing its Inheto payload.<br/>
        /// State is stored in the reserved persistent LibraDex State index; assigning the current value is an allocation-light no-op.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store and exist in Fractal.<br/></param>
        /// <param name="state">The signed 64-bit developer value to associate with the record.<br/></param>
        /// <returns><see langword="true"/> when State changed or was newly assigned; otherwise <see langword="false"/> when the same value was already assigned.<br/></returns>
        public bool SetState(ulong recordID, long state)
        {
            rbase.EnsureWriteAllowed(nameof(SetState));
            using var mutation = EnterPartialIndexMutationScope();
            EnsureLibraDexConvergence(nameof(SetState));
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.RecordExists(recordID))
                throw new KeyNotFoundException($"Record '{recordID}' does not exist in this store.");
            return SetStateCore(recordID, state, nameof(SetState));
        }

        /// <summary>
        /// Removes developer-owned State metadata from an existing record without deleting or rewriting its Fractal payload.<br/>
        /// This is the explicit clear operation; passing <c>null</c> to a Put overwrite continues to preserve existing State.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store and exist in Fractal.<br/></param>
        /// <returns><see langword="true"/> when a State tuple was removed; otherwise <see langword="false"/> when the record already had no State.<br/></returns>
        public bool ClearState(ulong recordID)
        {
            rbase.EnsureWriteAllowed(nameof(ClearState));
            using var mutation = EnterPartialIndexMutationScope();
            EnsureLibraDexConvergence(nameof(ClearState));
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.RecordExists(recordID))
                throw new KeyNotFoundException($"Record '{recordID}' does not exist in this store.");
            return ClearStateCore(recordID, nameof(ClearState));
        }

        /// <summary>
        /// Attempts to read developer-owned State metadata for an existing record without hydrating its Inheto payload.<br/>
        /// A <see langword="false"/> result means no State tuple exists; an explicit State value of zero returns <see langword="true"/> with zero.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">The assigned State value when this method returns <see langword="true"/>.<br/></param>
        /// <returns><see langword="true"/> when State is assigned; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetState(ulong recordID, out long state)
        {
            using var lifecycle = EnterStoreOperationScope();
            EnsureLibraDexConvergence(nameof(TryGetState));
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.RecordExists(recordID))
            {
                state = default;
                return false;
            }

            var index = RequireLibraDexStateIndex(nameof(TryGetState));

            lock (libraDexIndexLock)
                return index.TryGetSingleKey(recordID, out state);
        }

        /// <summary>
        /// Determines whether authoritative Fractal storage contains the supplied identity and its reserved LibraDex State value matches the requested rule.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare against Abraxas' ordered State index.<br/></param>
        /// <param name="mask">Optional bitmask ensuring <c>(State &amp; mask) == state</c>.<br/></param>
        /// <returns><see langword="true"/> when Fractal contains the payload and the assigned State matches; otherwise <see langword="false"/>.<br/></returns>
        public bool Exists(ulong recordID, long state, long? mask = null)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));

            if (!rbase.data.RecordExists(recordID))
                return false;

            if (TryExistsByLibraDexState(recordID, state, mask, out var libraDexExists))
                return libraDexExists;

            throw new InvalidOperationException(
                $"Operation '{nameof(Exists)}' requires this store's reserved persistent LibraDex State index.");
        }

        /// <summary>
        /// Retains the historical State-existence signature while directing callers to the persistent-only State contract.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns><see langword="true"/> when the record exists and its assigned State matches.<br/></returns>
        [Obsolete("State is always persistent. Use Exists(recordID, state, mask) instead.")]
        public bool Exists(ulong recordID, long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Exists(recordID, state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> calls that omitted a State mask.<br/></summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns><see langword="true"/> when the record exists and its assigned State matches.<br/></returns>
        [Obsolete("State is always persistent. Use Exists(recordID, state) instead.")]
        public bool Exists(ulong recordID, long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Exists(recordID, state);
        }

        /// <summary>
        /// Lazily emits identities satisfying one completed condition through the fastest applicable Abraxas execution plan without preserving result order.<br/>
        /// The worker count is exact: one preserves the proven single-thread route, while a value greater than one requires that many independent workers or reports that the selected physical route is not yet partitionable.<br/>
        /// Enumeration observes cancellation and consumer abandonment so an early-stopping caller does not require complete result materialization.<br/>
        /// </summary>
        /// <param name="where">Completed Abraxas condition whose semantics remain identical across physical FastFind routes.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact number of physical workers; the compatibility parameter name is retained so existing named callers continue to compile.<br/></param>
        /// <param name="channelCapacity">Optional maximum number of completed parallel matches buffered before consumer backpressure applies.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; <see langword="false"/> selects exactly one worker and conflicts with an explicit count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, source traversal, evaluation, and consumer enumeration.<br/></param>
        /// <returns>A lazy unordered sequence of matching authoritative record identities.<br/></returns>
        public IEnumerable<ulong> FindFastIDs(Condition<T>.EndCondition where, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(where);
            return FindFastIdentityCore(where, nameof(FindFastIDs), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching records hydrated into this store's CLR record type.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of hydrated matching records or projections.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<T> FindFastRecords(Condition<T>.EndCondition where, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            return FindFastCore(where, (ib, recordId) =>
            {
                OnRecordMaterializing(recordId, typeof(T), nameof(FindFastRecords));
                var entity = ib.ToObject<T>(NativeDeserializationOptions);
                OnRecordMaterialized(recordId, typeof(T), entity, nameof(FindFastRecords));
                return entity!;
            }, nameof(FindFastRecords), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching records hydrated into a caller-selected CLR shape.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of hydrated matching records or projections.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<TOut> FindFastRecordsAs<TOut>(Condition<T>.EndCondition where, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            return FindFastCore(where, (ib, recordId) =>
            {
                OnRecordMaterializing(recordId, typeof(TOut), nameof(FindFastRecordsAs));
                var entity = ib.ToObject<TOut>(NativeDeserializationOptions);
                OnRecordMaterialized(recordId, typeof(TOut), entity, nameof(FindFastRecordsAs));
                return entity!;
            }, nameof(FindFastRecordsAs), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching selected values or value tuples without hydrating each complete root object.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="propPath">Member expression identifying the requested selected value.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of selected values or typed tuples in requested path order.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<T1?> FindFastValues<T1>(Condition<T>.EndCondition where, Expression<Func<T, object>> propPath, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            var paths = new[] { propPath.GetPropertyPath() };
            return FindFastCore(where, (ib, _) => ib.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), nameof(FindFastValues), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching selected values or value tuples without hydrating each complete root object.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="propPath1">Member expression identifying the first selected value.<br/></param>
        /// <param name="propPath2">Member expression identifying the second selected value.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of selected values or typed tuples in requested path order.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<(T1?, T2?)> FindFastValues<T1, T2>(Condition<T>.EndCondition where, Expression<Func<T, object>> propPath1, Expression<Func<T, object>> propPath2, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            var paths = new[] { propPath1.GetPropertyPath(), propPath2.GetPropertyPath() };
            return FindFastCore(where, (ib, _) => (ib.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), ib.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), nameof(FindFastValues), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching selected values or value tuples without hydrating each complete root object.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="propPath1">Member expression identifying the first selected value.<br/></param>
        /// <param name="propPath2">Member expression identifying the second selected value.<br/></param>
        /// <param name="propPath3">Member expression identifying the third selected value.<br/></param>
        /// <param name="propPath4">Member expression identifying the fourth selected value.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of selected values or typed tuples in requested path order.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<(T1?, T2, T3, T4?)> FindFastValues<T1, T2, T3, T4>(Condition<T>.EndCondition where, Expression<Func<T, object>> propPath1, Expression<Func<T, object>> propPath2, Expression<Func<T, object>> propPath3, Expression<Func<T, object>> propPath4, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            var paths = new[] { propPath1.GetPropertyPath(), propPath2.GetPropertyPath(), propPath3.GetPropertyPath(), propPath4.GetPropertyPath() };
            return FindFastCore(where, (ib, _) => (
                ib.ReadPropAs<T1>(paths[0], NativeDeserializationOptions),
                ib.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)!,
                ib.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)!,
                ib.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), nameof(FindFastValues), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching selected values or value tuples without hydrating each complete root object.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <typeparam name="T5">Requested CLR type of the fifth selected value.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="propPath1">Member expression identifying the first selected value.<br/></param>
        /// <param name="propPath2">Member expression identifying the second selected value.<br/></param>
        /// <param name="propPath3">Member expression identifying the third selected value.<br/></param>
        /// <param name="propPath4">Member expression identifying the fourth selected value.<br/></param>
        /// <param name="propPath5">Member expression identifying the fifth selected value.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of selected values or typed tuples in requested path order.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<(T1?, T2, T3, T4, T5?)> FindFastValues<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, Expression<Func<T, object>> propPath1, Expression<Func<T, object>> propPath2, Expression<Func<T, object>> propPath3, Expression<Func<T, object>> propPath4, Expression<Func<T, object>> propPath5, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            var paths = new[] { propPath1.GetPropertyPath(), propPath2.GetPropertyPath(), propPath3.GetPropertyPath(), propPath4.GetPropertyPath(), propPath5.GetPropertyPath() };
            return FindFastCore(where, (ib, _) => (
                ib.ReadPropAs<T1>(paths[0], NativeDeserializationOptions),
                ib.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)!,
                ib.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)!,
                ib.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)!,
                ib.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), nameof(FindFastValues), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching selected values or value tuples without hydrating each complete root object.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <typeparam name="T5">Requested CLR type of the fifth selected value.<br/></typeparam>
        /// <typeparam name="T6">Requested CLR type of the sixth selected value.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="propPath1">Member expression identifying the first selected value.<br/></param>
        /// <param name="propPath2">Member expression identifying the second selected value.<br/></param>
        /// <param name="propPath3">Member expression identifying the third selected value.<br/></param>
        /// <param name="propPath4">Member expression identifying the fourth selected value.<br/></param>
        /// <param name="propPath5">Member expression identifying the fifth selected value.<br/></param>
        /// <param name="propPath6">Member expression identifying the sixth selected value.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of selected values or typed tuples in requested path order.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<(T1?, T2, T3, T4, T5, T6?)> FindFastValues<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, Expression<Func<T, object>> propPath1, Expression<Func<T, object>> propPath2, Expression<Func<T, object>> propPath3, Expression<Func<T, object>> propPath4, Expression<Func<T, object>> propPath5, Expression<Func<T, object>> propPath6, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            var paths = new[] { propPath1.GetPropertyPath(), propPath2.GetPropertyPath(), propPath3.GetPropertyPath(), propPath4.GetPropertyPath(), propPath5.GetPropertyPath(), propPath6.GetPropertyPath() };
            return FindFastCore(where, (ib, _) => (
                ib.ReadPropAs<T1>(paths[0], NativeDeserializationOptions),
                ib.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)!,
                ib.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)!,
                ib.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)!,
                ib.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)!,
                ib.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), nameof(FindFastValues), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Enumerates matching selected values or value tuples without hydrating each complete root object.<br/>
        /// Results are unordered for every FastFind route, including a single-worker route.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <typeparam name="T5">Requested CLR type of the fifth selected value.<br/></typeparam>
        /// <typeparam name="T6">Requested CLR type of the sixth selected value.<br/></typeparam>
        /// <typeparam name="T7">Requested CLR type of the seventh selected value.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="propPath1">Member expression identifying the first selected value.<br/></param>
        /// <param name="propPath2">Member expression identifying the second selected value.<br/></param>
        /// <param name="propPath3">Member expression identifying the third selected value.<br/></param>
        /// <param name="propPath4">Member expression identifying the fourth selected value.<br/></param>
        /// <param name="propPath5">Member expression identifying the fifth selected value.<br/></param>
        /// <param name="propPath6">Member expression identifying the sixth selected value.<br/></param>
        /// <param name="propPath7">Member expression identifying the seventh selected value.<br/></param>
        /// <param name="maxDegreeOfParallelism">Optional exact physical worker count; null uses the FastFind configuration contract.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="useParallel">Optional compatibility switch; false selects one worker and conflicts with an explicit worker count greater than one.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>A lazy unordered sequence of selected values or typed tuples in requested path order.<br/></returns>
        /// <remarks>
        /// Condition planning chooses a complete-index, candidate-plus-residual, or authoritative scan route. Parallel matches use bounded buffering; requesting multiple workers requires a route that supports that exact worker count.<br/>
        /// </remarks>
        public IEnumerable<(T1?, T2?, T3?, T4?, T5?, T6, T7?)> FindFastValues<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, Expression<Func<T, object>> propPath1, Expression<Func<T, object>> propPath2, Expression<Func<T, object>> propPath3, Expression<Func<T, object>> propPath4, Expression<Func<T, object>> propPath5, Expression<Func<T, object>> propPath6, Expression<Func<T, object>> propPath7, int? maxDegreeOfParallelism = null, int? channelCapacity = null, bool? useParallel = null, CancellationToken cancellationToken = default)
        {
            var paths = new[] { propPath1.GetPropertyPath(), propPath2.GetPropertyPath(), propPath3.GetPropertyPath(), propPath4.GetPropertyPath(), propPath5.GetPropertyPath(), propPath6.GetPropertyPath(), propPath7.GetPropertyPath() };
            return FindFastCore(where, (ib, _) => (
                ib.ReadPropAs<T1>(paths[0], NativeDeserializationOptions),
                ib.ReadPropAs<T2>(paths[1], NativeDeserializationOptions),
                ib.ReadPropAs<T3>(paths[2], NativeDeserializationOptions),
                ib.ReadPropAs<T4>(paths[3], NativeDeserializationOptions),
                ib.ReadPropAs<T5>(paths[4], NativeDeserializationOptions),
                ib.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)!,
                ib.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), nameof(FindFastValues), maxDegreeOfParallelism, channelCapacity, useParallel, cancellationToken);
        }

        /// <summary>
        /// Hydrates one store-owned record into the requested CLR shape.<br/>
        /// </summary>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <returns>The hydrated record or requested projection.<br/></returns>
        /// <exception cref="KeyNotFoundException">No matching live record exists, including any required State filter.<br/></exception>
        public T Get(ulong recordID)
        {
            if (TryMaterializeRecordCore(recordID, nameof(Get), out T? record))
                return record!;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Locates the first matching record by a supported scalar property value and hydrates it into the requested CLR shape.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Property path or member expression whose supported scalar value is expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null supported scalar value matched by the property lookup.<br/></param>
        /// <param name="state">Optional persistent State value that must also match the property lookup.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <returns>The hydrated record or requested projection.<br/></returns>
        /// <remarks>
        /// The property is expected to identify one record; use a unique index to enforce that expectation. The fallback lookup returns the first matching identity rather than checking for a second match.<br/>
        /// </remarks>
        /// <exception cref="KeyNotFoundException">No matching live record exists, including any required State filter.<br/></exception>
        public T Get(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, long? state = null, bool useMemoryIndex = false)
            => Get(uniquePropPath.GetPropertyPath(), uniquePropValue, state, useMemoryIndex);

        /// <summary>
        /// Retrieves the record identified by <paramref name="recordID"/> only when the associated state matches the supplied criteria.
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.</param>
        /// <param name="state">State value persisted in the metadata table.</param>
        /// <param name="mask">Optional bitmask ensuring <c>(State &amp; mask) == state</c>.</param>
        /// <returns>The materialized record when the identifier and state match.</returns>
        public T Get(ulong recordID, long state, long? mask = null)
        {
            if (TryGet(recordID, state, out T? record, mask))
                return record!;
            var maskSuffix = mask.HasValue ? $" and mask '{mask.Value}'" : string.Empty;
            throw new KeyNotFoundException($"No record found with recordID '{recordID}' matching state '{state}'{maskSuffix}.");
        }

        /// <summary>
        /// Retains the historical State-qualified retrieval signature while directing callers to persistent-only State selection.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The matching materialized record.<br/></returns>
        [Obsolete("State is always persistent. Use Get(recordID, state, mask) instead.")]
        public T Get(ulong recordID, long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Get(recordID, state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State-qualified retrieval calls that omitted a mask.<br/></summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The matching materialized record.<br/></returns>
        [Obsolete("State is always persistent. Use Get(recordID, state) instead.")]
        public T Get(ulong recordID, long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Get(recordID, state);
        }

        /// <summary>
        /// Locates the first matching record by a supported scalar property value and hydrates it into the requested CLR shape.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Property path or member expression whose supported scalar value is expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null supported scalar value matched by the property lookup.<br/></param>
        /// <param name="state">Optional persistent State value that must also match the property lookup.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <returns>The hydrated record or requested projection.<br/></returns>
        /// <remarks>
        /// The property is expected to identify one record; use a unique index to enforce that expectation. The fallback lookup returns the first matching identity rather than checking for a second match.<br/>
        /// </remarks>
        /// <exception cref="KeyNotFoundException">No matching live record exists, including any required State filter.<br/></exception>
        public T Get(string uniquePropPath, object uniquePropValue, long? state = null, bool useMemoryIndex = false)
        {
            _ = useMemoryIndex;
            if (TryGet(uniquePropPath, uniquePropValue, out T? record, state))
                return record!;
            throw new KeyNotFoundException($"The prop path '{uniquePropPath}' and value '{uniquePropValue}' returned no records.");
        }

        /// <summary>
        /// Hydrates one store-owned record into the requested CLR shape.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <returns>The hydrated record or requested projection.<br/></returns>
        /// <remarks>
        /// The target shape may be unrelated to the original serialized type; matching stored member paths are hydrated through the store's deserialization options.<br/>
        /// </remarks>
        /// <exception cref="KeyNotFoundException">No matching live record exists, including any required State filter.<br/></exception>
        public TOut GetAs<TOut>(ulong recordID)
        {
            if (TryMaterializeRecordCore(recordID, nameof(GetAs), out TOut? record))
                return record!;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Locates the first matching record by a supported scalar property value and hydrates it into the requested CLR shape.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="uniquePropPath">Property path or member expression whose supported scalar value is expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null supported scalar value matched by the property lookup.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <returns>The hydrated record or requested projection.<br/></returns>
        /// <remarks>
        /// The property is expected to identify one record; use a unique index to enforce that expectation. The fallback lookup returns the first matching identity rather than checking for a second match.<br/>
        /// The target shape may be unrelated to the original serialized type; matching stored member paths are hydrated through the store's deserialization options.<br/>
        /// </remarks>
        /// <exception cref="KeyNotFoundException">No matching live record exists, including any required State filter.<br/></exception>
        public TOut GetAs<TOut>(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, bool useMemoryIndex = false)
            => GetAs<TOut>(uniquePropPath.GetPropertyPath(), uniquePropValue, useMemoryIndex);

        /// <summary>
        /// Locates the first matching record by a supported scalar property value and hydrates it into the requested CLR shape.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="uniquePropPath">Property path or member expression whose supported scalar value is expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null supported scalar value matched by the property lookup.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <returns>The hydrated record or requested projection.<br/></returns>
        /// <remarks>
        /// The property is expected to identify one record; use a unique index to enforce that expectation. The fallback lookup returns the first matching identity rather than checking for a second match.<br/>
        /// The target shape may be unrelated to the original serialized type; matching stored member paths are hydrated through the store's deserialization options.<br/>
        /// </remarks>
        /// <exception cref="KeyNotFoundException">No matching live record exists, including any required State filter.<br/></exception>
        public TOut GetAs<TOut>(string uniquePropPath, object uniquePropValue, bool useMemoryIndex = false)
        {
            _ = useMemoryIndex;
            if (TryGetAs(uniquePropPath, uniquePropValue, out TOut? record))
                return record!;
            throw new KeyNotFoundException($"The prop path '{uniquePropPath}' and value '{uniquePropValue}' returned no records.");
        }

        /// <summary>
        /// Retrieves the record identified by <paramref name="recordID"/> as <typeparamref name="TOut"/> only when its state matches the supplied criteria.
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.</param>
        /// <param name="state">State value persisted in the metadata table.</param>
        /// <param name="mask">Optional bitmask ensuring <c>(State &amp; mask) == state</c>.</param>
        /// <returns>The materialized record cast to <typeparamref name="TOut"/> when the identifier and state match.</returns>
        public TOut GetAs<TOut>(ulong recordID, long state, long? mask = null)
        {
            if (TryGetAs(recordID, state, out TOut? record, mask))
                return record!;
            var maskSuffix = mask.HasValue ? $" and mask '{mask.Value}'" : string.Empty;
            throw new KeyNotFoundException($"No record found with recordID '{recordID}' matching state '{state}'{maskSuffix}.");
        }

        /// <summary>
        /// Retains the historical projected State-qualified retrieval signature while directing callers to persistent-only State selection.<br/>
        /// </summary>
        /// <typeparam name="TOut">Projection type materialized from the stored payload.<br/></typeparam>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The matching materialized projection.<br/></returns>
        [Obsolete("State is always persistent. Use GetAs<TOut>(recordID, state, mask) instead.")]
        public TOut GetAs<TOut>(ulong recordID, long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return GetAs<TOut>(recordID, state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> projected State retrieval calls that omitted a mask.<br/></summary>
        /// <typeparam name="TOut">Projection type materialized from the stored payload.<br/></typeparam>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The matching materialized projection.<br/></returns>
        [Obsolete("State is always persistent. Use GetAs<TOut>(recordID, state) instead.")]
        public TOut GetAs<TOut>(ulong recordID, long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return GetAs<TOut>(recordID, state);
        }

        /// <summary>
        /// One complete simple-key index uses LibraDex grouping metadata; multi-key, missing-key, null-key, or unavailable-index shapes use one authoritative Fractal/Inheto pass.<br/>
        /// Computed selectors require complete backend-neutral semantics before they can participate.<br/>
        /// </summary>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>A <see cref="GroupedRecords"/> container with group details and drill-down conditions.</returns>
        public GroupedRecords GetGroupings(params ISelector[] groupKeys)
            => GetGroupings(null, groupKeys);

        /// <summary>
        /// One complete simple-key index uses LibraDex grouping metadata; multi-key, missing-key, null-key, or unavailable-index shapes use one authoritative Fractal/Inheto pass.<br/>
        /// Computed selectors require complete backend-neutral semantics before they can participate.<br/>
        /// </summary>
        /// <param name="name">Optional label for the grouped result-set.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>A <see cref="GroupedRecords"/> container with group details and drill-down conditions.</returns>
        public GroupedRecords GetGroupings(string? name, params ISelector[] groupKeys)
        {
            var groups = IterateGroupings(groupKeys).ToList();
            return new GroupedRecords(groups, where: null, name: name);
        }

        /// <summary>
        /// </summary>
        /// <param name="useMemoryIndex">When <see langword="true"/>, returns only volatile index names; otherwise returns persistent and volatile index names.<br/></param>
        /// <returns>Logical names suitable for <see cref="GetIndex(string, bool)"/> and <see cref="GetIndexInfo(string, bool)"/>.<br/></returns>
        public string[] GetIndexNames(bool useMemoryIndex = false)
            => GetIndexes(useMemoryIndex).Select(static index => index.LogicalPrefix).ToArray();

        /// <summary>
        /// Retrieves and materializes the requested identities in caller-supplied order.<br/>
        /// Duplicate identities remain duplicate results, and an absent or cross-store identity stops the operation with the same exception contract as <see cref="Get(ulong)"/>.<br/>
        /// Each pooled Fractal payload is borrowed only for its synchronous materialization and is returned before the next identity is opened.<br/>
        /// </summary>
        /// <param name="recordIDs">Ordered identities to retrieve.<br/></param>
        /// <returns>A read-only result list preserving input order and duplicates.<br/></returns>
        public IReadOnlyList<T> GetMany(IEnumerable<ulong> recordIDs) => GetManyAs<T>(recordIDs);

        /// <summary>
        /// Retrieves the requested identities as a caller-selected materialization shape while preserving caller-supplied order.<br/>
        /// The method performs one Fractal identity lookup per input element, holds at most one encoded record lease at a time, and does not copy complete encoded payloads.<br/>
        /// Duplicate identities are materialized independently; an absent identity throws rather than returning a partial result.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization or reprojection shape.<br/></typeparam>
        /// <param name="recordIDs">Ordered identities to retrieve. Every identity must belong to this store and exist when reached.<br/></param>
        /// <returns>A read-only result list preserving input order and duplicates.<br/></returns>
        public IReadOnlyList<TOut> GetManyAs<TOut>(IEnumerable<ulong> recordIDs)
        {
            using var operation = EnterStoreOperationScope();
            ArgumentNullException.ThrowIfNull(recordIDs);

            int capacity = recordIDs switch
            {
                ICollection<ulong> collection => collection.Count,
                IReadOnlyCollection<ulong> collection => collection.Count,
                _ => 0
            };
            var results = capacity == 0 ? new List<TOut>() : new List<TOut>(capacity);
            foreach (var recordID in recordIDs)
            {
                EnsureRecordBelongsToCurrentStore(recordID, nameof(recordIDs));

                if (!TryMaterializeValidatedRecordCore(recordID, nameof(GetManyAs), out TOut? entity))
                    throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
                results.Add(entity!);
            }

            return results.AsReadOnly();
        }

        /// <summary>
        /// Retrieves and materializes the requested identities using the selected missing-record behavior.<br/>
        /// Caller order and duplicates are preserved; <see cref="MissingRecordBehavior.Skip"/> removes absent positions, while <see cref="MissingRecordBehavior.PreserveDefault"/> inserts <see langword="default"/> for each absent identity.<br/>
        /// Cross-store identities always throw because they are invalid for this store rather than missing from it.<br/>
        /// </summary>
        /// <param name="recordIDs">Ordered identities to retrieve.<br/></param>
        /// <param name="missingRecordBehavior">Behavior applied to same-store identities that no longer exist.<br/></param>
        /// <returns>A read-only result list following the selected missing-record and ordering contract.<br/></returns>
        public IReadOnlyList<T?> GetMany(
            IEnumerable<ulong> recordIDs,
            MissingRecordBehavior missingRecordBehavior)
            => GetManyAs<T>(recordIDs, missingRecordBehavior);

        /// <summary>
        /// Retrieves the requested identities as a caller-selected materialization shape using the selected missing-record behavior.<br/>
        /// The method performs one Fractal lookup per input identity, holds at most one encoded-record lease at a time, and never buffers complete encoded payloads.<br/>
        /// <see cref="MissingRecordBehavior.PreserveDefault"/> produces <see langword="null"/> for reference or nullable projections and <see langword="default"/> for non-nullable structs.<br/>
        /// Cross-store identities always throw before materialization.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization or reprojection shape.<br/></typeparam>
        /// <param name="recordIDs">Ordered identities to retrieve.<br/></param>
        /// <param name="missingRecordBehavior">Behavior applied to same-store identities that no longer exist.<br/></param>
        /// <returns>A read-only result list following the selected missing-record, projection, and ordering contract.<br/></returns>
        public IReadOnlyList<TOut?> GetManyAs<TOut>(
            IEnumerable<ulong> recordIDs,
            MissingRecordBehavior missingRecordBehavior)
        {
            using var operation = EnterStoreOperationScope();
            ArgumentNullException.ThrowIfNull(recordIDs);
            if (missingRecordBehavior is < MissingRecordBehavior.Throw or > MissingRecordBehavior.PreserveDefault)
                throw new ArgumentOutOfRangeException(nameof(missingRecordBehavior));

            int capacity = recordIDs switch
            {
                ICollection<ulong> collection => collection.Count,
                IReadOnlyCollection<ulong> collection => collection.Count,
                _ => 0
            };
            var results = capacity == 0 ? new List<TOut?>() : new List<TOut?>(capacity);
            foreach (ulong recordID in recordIDs)
            {
                EnsureRecordBelongsToCurrentStore(recordID, nameof(recordIDs));

                if (TryMaterializeValidatedRecordCore(recordID, nameof(GetManyAs), out TOut? entity))
                {
                    results.Add(entity);
                    continue;
                }

                if (missingRecordBehavior == MissingRecordBehavior.Throw)
                    throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
                if (missingRecordBehavior == MissingRecordBehavior.PreserveDefault)
                    results.Add(default);
            }

            return results.AsReadOnly();
        }

        private Inheto.InhetoBinary GetInhetoBinary(ulong recordID, string operationName)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));

            if (!rbase.data.TryGetOne(recordID, out var rec))
                throw new KeyNotFoundException($"{operationName}: no record found with recordID of '{recordID}'");

            using (rec)
            {
                Inheto.InhetoBinary binary = CreateRecordBinary(recordID, rec.DataSpan.ToArray());
                binary.SetMaterializationContext(recordID);
                return binary;
            }
        }

        /// <summary>
        /// Retrieves one record as a detached decoded and decompressed INHETO binary without materializing a CLR object.<br/>
        /// The returned binary owns its payload and remains valid after this method returns.<br/>
        /// </summary>
        /// <param name="recordID">Store-owned identity to retrieve.<br/></param>
        /// <returns>Detached decoded INHETO binary for the requested record.<br/></returns>
        public RecordBinary GetBinary(ulong recordID)
            => new(GetInhetoBinary(recordID, nameof(GetBinary)));

        /// <summary>
        /// Attempts to retrieve one record as a detached decoded and decompressed INHETO binary without materializing a CLR object.<br/>
        /// A cross-store identity remains invalid and throws; only an absent same-store record returns <see langword="false"/>.<br/>
        /// </summary>
        /// <param name="recordID">Store-owned identity to retrieve.<br/></param>
        /// <param name="binary">Receives a detached decoded record binary when the record exists; otherwise a default invalid value.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetBinary(ulong recordID, out RecordBinary binary)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.TryGetOne(recordID, out var record))
            {
                binary = default;
                return false;
            }

            using (record)
            {
                var native = CreateRecordBinary(recordID, record.DataSpan.ToArray());
                native.SetMaterializationContext(recordID);
                binary = new(native);
                return true;
            }
        }

        /// <summary>
        /// Retrieves the exact stored Fractal application payload for one record without decrypting, decompressing, or parsing it.<br/>
        /// The returned array is caller-owned and can be retained or transferred independently of this store.<br/>
        /// </summary>
        /// <param name="recordID">Store-owned identity to retrieve.<br/></param>
        /// <returns>Caller-owned exact stored payload bytes.<br/></returns>
        public byte[] GetStoredPayload(ulong recordID)
        {
            if (TryGetStoredPayload(recordID, out byte[]? payload))
                return payload!;
            throw new KeyNotFoundException($"{nameof(GetStoredPayload)}: no record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Attempts to retrieve the exact stored Fractal application payload without decrypting, decompressing, or parsing it.<br/>
        /// A cross-store identity remains invalid and throws; only an absent same-store record returns <see langword="false"/>.<br/>
        /// </summary>
        /// <param name="recordID">Store-owned identity to retrieve.<br/></param>
        /// <param name="payload">Receives caller-owned exact stored payload bytes when the record exists; otherwise <see langword="null"/>.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetStoredPayload(ulong recordID, out byte[]? payload)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.TryGetOne(recordID, out var record))
            {
                payload = null;
                return false;
            }

            using (record)
            {
                payload = record.DataSpan.ToArray();
                return true;
            }
        }

        /// <summary>
        /// Retrieves exact stored Fractal application payloads in caller-supplied identity order.<br/>
        /// Each result is caller-owned and has not been decrypted, decompressed, or parsed by Abraxas.<br/>
        /// </summary>
        /// <param name="recordIDs">Ordered store-owned identities to retrieve.<br/></param>
        /// <returns>Read-only list of caller-owned exact stored payload arrays preserving caller order and duplicates.<br/></returns>
        public IReadOnlyList<byte[]> GetStoredPayloads(IEnumerable<ulong> recordIDs)
        {
            ArgumentNullException.ThrowIfNull(recordIDs);
            var payloads = new List<byte[]>();
            foreach (ulong recordID in recordIDs)
                payloads.Add(GetStoredPayload(recordID));
            return payloads.AsReadOnly();
        }

        /// <summary>
        /// Streams detached exact stored Fractal application payloads in deterministic identity order.<br/>
        /// Each yielded array is caller-owned and has not been decrypted, decompressed, or parsed by Abraxas.<br/>
        /// </summary>
        /// <returns>Lazy sequence of caller-owned exact stored payload arrays.<br/></returns>
        public IEnumerable<byte[]> IterateStoredPayloads()
        {
            foreach (ulong recordID in IterateRecordIDs())
                yield return GetStoredPayload(recordID);
        }

        /// <summary>
        /// Streams detached exact stored Fractal application payloads for records selected by the supplied condition.<br/>
        /// Condition evaluation may need ordinary payload access, but each yielded array is the original stored payload without transform decoding.<br/>
        /// </summary>
        /// <param name="where">Condition used to select records.<br/></param>
        /// <param name="sort">Optional sort descriptors applied before enumeration.<br/></param>
        /// <returns>Lazy sequence of caller-owned exact stored payload arrays.<br/></returns>
        public IEnumerable<byte[]> IterateStoredPayloads(Condition<T>.EndCondition where, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            foreach (ulong recordID in IterateRecordIDs(where, sort))
                yield return GetStoredPayload(recordID);
        }

        /// <summary>
        /// Opens a borrowed reader over every exact stored Fractal application payload in deterministic identity order.<br/>
        /// The reader exposes current bytes without decrypting, decompressing, or parsing them.<br/>
        /// </summary>
        /// <returns>Disposable reader positioned before the first stored payload.<br/></returns>
        public StoredPayloadReader OpenStoredPayloadReader()
            => new(CreateAllReaderCore(CreateAllBookmarkContext()));

        /// <summary>
        /// Opens a borrowed reader over exact stored Fractal application payloads selected by a condition.<br/>
        /// Condition evaluation may need ordinary payload access, but each returned current payload remains unmodified stored bytes.<br/>
        /// </summary>
        /// <param name="where">Condition used to select records.<br/></param>
        /// <returns>Disposable reader positioned before the first selected stored payload.<br/></returns>
        public StoredPayloadReader OpenStoredPayloadReader(Condition<T>.EndCondition where)
        {
            ArgumentNullException.ThrowIfNull(where);
            return new(CreateConditionReaderCore(where, Array.Empty<SortBy>(), nameof(OpenStoredPayloadReader), CreateConditionBookmarkContext(where)));
        }

        /// <summary>
        /// Opens a borrowed reader over exact stored Fractal application payloads selected and ordered by a condition.<br/>
        /// Condition evaluation and ordering can inspect decoded payloads when needed, while each returned current payload remains unmodified stored bytes.<br/>
        /// </summary>
        /// <param name="where">Condition used to select records.<br/></param>
        /// <param name="sort">Sort descriptors applied before reader traversal.<br/></param>
        /// <returns>Disposable reader positioned before the first selected stored payload.<br/></returns>
        public StoredPayloadReader OpenStoredPayloadReader(Condition<T>.EndCondition where, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(sort);
            return new(CreateConditionReaderCore(where, sort, nameof(OpenStoredPayloadReader), CreateConditionBookmarkContext(where, sort)));
        }

        /// <summary>
        /// Opens a borrowed reader over decoded and decompressed INHETO binaries in deterministic identity order.<br/>
        /// The reader does not materialize CLR objects and owns each current binary only until reader movement or disposal.<br/>
        /// </summary>
        /// <returns>Disposable reader positioned before the first INHETO binary.<br/></returns>
        public RecordBinaryReader OpenBinaryReader()
            => new(CreateAllReaderCore(CreateAllBookmarkContext()));

        /// <summary>
        /// Opens a borrowed reader over decoded and decompressed INHETO binaries selected by a condition.<br/>
        /// The reader does not materialize CLR objects and owns each current binary only until reader movement or disposal.<br/>
        /// </summary>
        /// <param name="where">Condition used to select records.<br/></param>
        /// <returns>Disposable reader positioned before the first selected INHETO binary.<br/></returns>
        public RecordBinaryReader OpenBinaryReader(Condition<T>.EndCondition where)
        {
            ArgumentNullException.ThrowIfNull(where);
            return new(CreateConditionReaderCore(where, Array.Empty<SortBy>(), nameof(OpenBinaryReader), CreateConditionBookmarkContext(where)));
        }

        /// <summary>
        /// Opens a borrowed reader over decoded and decompressed INHETO binaries selected and ordered by a condition.<br/>
        /// The reader does not materialize CLR objects and owns each current binary only until reader movement or disposal.<br/>
        /// </summary>
        /// <param name="where">Condition used to select records.<br/></param>
        /// <param name="sort">Sort descriptors applied before reader traversal.<br/></param>
        /// <returns>Disposable reader positioned before the first selected INHETO binary.<br/></returns>
        public RecordBinaryReader OpenBinaryReader(Condition<T>.EndCondition where, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(sort);
            return new(CreateConditionReaderCore(where, sort, nameof(OpenBinaryReader), CreateConditionBookmarkContext(where, sort)));
        }

        /// <summary>
        /// Exports one decoded INHETO record representation inside a versioned, integrity-protected transfer package.<br/>
        /// The package includes source identity, durable store identity, and persistent State without changing the exact-byte contract of <see cref="GetBinary"/>.<br/>
        /// </summary>
        /// <param name="recordID">Store-owned identity to export.<br/></param>
        /// <returns>Caller-owned <c>ABRXPAY01</c> package containing decoded INHETO bytes.<br/></returns>
        public byte[] ExportInhetoPayloadPackage(ulong recordID)
        {
            bool hasState = TryGetState(recordID, out long state);
            Inheto.InhetoBinary binary = GetInhetoBinary(recordID, nameof(ExportInhetoPayloadPackage));
            return RecordPayloadPackageCodec.Encode(
                RecordPayloadPackageRepresentation.InhetoBinary,
                recordID,
                storeRuntime.InstanceID,
                hasState,
                state,
                binary.RawBytes.AsReadOnlySpan);
        }

        /// <summary>
        /// Exports one exact stored Fractal application payload inside a versioned, integrity-protected transfer package.<br/>
        /// The package carries source binding metadata required to decrypt a Commercial encrypted payload before destination re-persistence.<br/>
        /// </summary>
        /// <param name="recordID">Store-owned identity to export.<br/></param>
        /// <returns>Caller-owned <c>ABRXPAY01</c> package containing exact stored payload bytes.<br/></returns>
        public byte[] ExportStoredPayloadPackage(ulong recordID)
        {
            bool hasState = TryGetState(recordID, out long state);
            byte[] payload = GetStoredPayload(recordID);
            return RecordPayloadPackageCodec.Encode(
                RecordPayloadPackageRepresentation.StoredPayload,
                recordID,
                storeRuntime.InstanceID,
                hasState,
                state,
                payload);
        }

        /// <summary>
        /// Imports one versioned RecordStore payload package through the destination store's ordinary record, transform, and index-maintenance pipeline.<br/>
        /// A decoded INHETO package is parsed directly; a stored-payload package is first decoded using its recorded source identity and store binding.<br/>
        /// An occupied matching identity can retain authenticated <c>PortableByRecordId</c> ciphertext exactly during an explicit overwrite; every other representation and conflict route is persisted under the destination policy.<br/>
        /// </summary>
        /// <param name="package">Complete <c>ABRXPAY01</c> package returned by an export API.<br/></param>
        /// <param name="options">Optional collision policy; omission rejects an occupied matching source identity.<br/></param>
        /// <returns>Source identity, destination identity, representation, conflict action, and mutation outcome.<br/></returns>
        public RecordPayloadImportResult ImportPayloadPackage(
            ReadOnlyMemory<byte> package,
            RecordPayloadImportOptions? options = null)
        {
            RecordPayloadPackage decoded = RecordPayloadPackageCodec.Read(package);
            options ??= new RecordPayloadImportOptions();
            if (!Enum.IsDefined(options.ConflictMode))
                throw new ArgumentOutOfRangeException(nameof(options), "The import conflict mode is invalid.");

            Inheto.InhetoBinary sourceBinary = decoded.Representation switch
            {
                RecordPayloadPackageRepresentation.InhetoBinary => Inheto.InhetoBinary.FromStream(
                    new BufferStream(decoded.Payload),
                    NativeDeserializationOptions),
                RecordPayloadPackageRepresentation.StoredPayload => CreateImportedStoredRecordBinary(
                    decoded.SourceRecordId,
                    decoded.SourceStoreInstanceId,
                    decoded.Payload),
                _ => throw new InvalidDataException("The RecordStore payload package representation is unsupported.")
            };

            T? record = sourceBinary.ToObject<T>(NativeDeserializationOptions);
            if (record is null)
                throw new InvalidDataException("The RecordStore payload package decoded to a null record.");

            bool sourceIdentityBelongsToDestination = RowIDGenerator.IsValidRecordID(decoded.SourceRecordId, storeID);
            bool existing = sourceIdentityBelongsToDestination && rbase.data.RecordExists(decoded.SourceRecordId);
            long? importedState = decoded.HasState ? decoded.State : null;
            ulong destinationRecordId;
            bool overwroteExisting = false;
            bool replayedPortableStoredPayload = false;

            if (existing && options.ConflictMode == RecordPayloadImportConflictMode.Throw)
            {
                throw new InvalidOperationException(
                    $"Imported record identity '{decoded.SourceRecordId}' already exists in this store. Select {nameof(RecordPayloadImportConflictMode)}.{nameof(RecordPayloadImportConflictMode.Overwrite)} or {nameof(RecordPayloadImportConflictMode)}.{nameof(RecordPayloadImportConflictMode.ReencryptWithNewId)} explicitly.");
            }

            if (existing && options.ConflictMode == RecordPayloadImportConflictMode.Overwrite)
            {
                replayedPortableStoredPayload = decoded.Representation == RecordPayloadPackageRepresentation.StoredPayload &&
                    IsPortableStoredPayload(decoded.Payload);
                destinationRecordId = replayedPortableStoredPayload
                    ? PersistPortableStoredPayload(
                        decoded.SourceRecordId,
                        record,
                        sourceBinary,
                        decoded.Payload,
                        importedState,
                        nameof(ImportPayloadPackage))
                    : PersistRecord(
                        decoded.SourceRecordId,
                        record,
                        importedState,
                        nameof(ImportPayloadPackage),
                        isNewRecord: false,
                        replaceStateWhenMissing: true);
                overwroteExisting = true;
            }
            else
            {
                destinationRecordId = PersistNewRecord(
                    record,
                    importedState,
                    nameof(ImportPayloadPackage));
            }

            return new RecordPayloadImportResult(
                decoded.SourceRecordId,
                destinationRecordId,
                decoded.Representation,
                options.ConflictMode,
                overwroteExisting,
                RewrittenThroughDestinationPolicy: !replayedPortableStoredPayload);
        }

        /// <summary>
        /// The caller owns the live <paramref name="record"/> scope and must call this before disposing the record because the payload span is read in-place.<br/>
        /// </summary>
        /// <typeparam name="TOut">Projection or record type to materialize from the INHETO payload.<br/></typeparam>
        /// <param name="recordID">Record identifier being materialized.<br/></param>
        /// <param name="record">Live FractalKVS record whose payload should be decoded.<br/></param>
        /// <param name="operationName">Operation name used for materialization event telemetry.<br/></param>
        /// <returns>The materialized object or projection.<br/></returns>
        private TOut MaterializeFractalRecordFast<TOut>(ulong recordID, Record record, string operationName)
        {
            var ib = CreateRecordBinary(recordID, record.Data);
            ib.SetMaterializationContext(recordID);

            return MaterializeReaderObject<TOut>(ib, recordID, operationName);
        }

        /// <summary>
        /// Materializes the specified records as INHETO binaries in request order.<br/>
        /// This is useful for transport and advanced payload-level workflows without forcing object rehydration.<br/>
        /// </summary>
        /// <param name="recordIDs">Record IDs to retrieve. Each ID must belong to this store.<br/></param>
        /// <returns>Read-only list of INHETO binaries matching the requested IDs.<br/></returns>
        public IReadOnlyList<RecordBinary> GetBinaries(IEnumerable<ulong> recordIDs)
        {
            ArgumentNullException.ThrowIfNull(recordIDs);

            var results = new List<RecordBinary>();
            foreach (var recordID in recordIDs)
                results.Add(new(GetInhetoBinary(recordID, nameof(GetBinaries))));

            return results.AsReadOnly();
        }

        /// <summary>
        /// Streams detached decoded record binaries in the same native natural order as IterateRecordIDs.<br/>
        /// </summary>
        /// <returns>Lazy sequence of INHETO binaries.<br/></returns>
        public IEnumerable<RecordBinary> IterateBinaries()
        {
            foreach (var recordID in IterateRecordIDs())
                yield return new(GetInhetoBinary(recordID, nameof(IterateBinaries)));
        }

        /// <summary>
        /// Streams INHETO binaries for records matching the supplied condition.<br/>
        /// </summary>
        /// <param name="where">Condition used to filter the result set.<br/></param>
        /// <param name="sort">Optional sort descriptors applied before enumeration.<br/></param>
        /// <returns>Lazy sequence of INHETO binaries for matching records.<br/></returns>
        public IEnumerable<RecordBinary> IterateBinaries(Condition<T>.EndCondition where, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            foreach (var recordID in IterateRecordIDs(where, sort))
                yield return new(GetInhetoBinary(recordID, nameof(IterateBinaries)));
        }

        /// <summary>
        /// </summary>
        /// <param name="useMemoryIndex">When <see langword="true"/>, returns only unique volatile index names; otherwise returns unique persistent and volatile index names.<br/></param>
        /// <returns>Logical unique-index names suitable for backend-neutral discovery and management.<br/></returns>
        public string[] GetUniqueIndexNames(bool useMemoryIndex = false)
            => GetIndexes(useMemoryIndex)
                .Where(static index => index.IsUnique)
                .Select(static index => index.LogicalPrefix)
                .ToArray();

        /// <summary>
        /// Reads one selected value from a store-owned record without hydrating the complete root object.<br/>
        /// </summary>
        /// <typeparam name="VType">Requested CLR type of the selected value.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPath">Property path or member expression identifying the selected value.<br/></param>
        /// <returns>The converted selected value; a present record may yield null or the requested type's default for a missing value.<br/></returns>
        /// <exception cref="KeyNotFoundException">The same-store record does not exist.<br/></exception>
        public VType GetValue<VType>(ulong recordID, Expression<Func<T, object>> propPath) => GetValue<VType>(recordID, propPath.GetPropertyPath());

        /// <summary>
        /// Reads one selected value from a store-owned record without hydrating the complete root object.<br/>
        /// </summary>
        /// <typeparam name="VType">Requested CLR type of the selected value.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPath">Property path or member expression identifying the selected value.<br/></param>
        /// <returns>The converted selected value; a present record may yield null or the requested type's default for a missing value.<br/></returns>
        /// <exception cref="KeyNotFoundException">The same-store record does not exist.<br/></exception>
        public VType GetValue<VType>(ulong recordID, string propPath)
        {
            if (TryGetValue(recordID, propPath, out VType? value))
                return value!;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Reads selected runtime-shaped values from one record payload without hydrating the complete root object.<br/>
        /// </summary>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPaths">One or more non-empty property paths or member expressions in result order.<br/></param>
        /// <returns>Selected values in the supplied property-path order, or null when the same-store record is absent.<br/></returns>
        /// <remarks>
        /// This list-returning overload differs from the typed tuple overloads: an absent same-store record returns null instead of throwing for absence.<br/>
        /// </remarks>
        public List<object?>? GetValues(ulong recordID, params string[] propPaths)
        {
            _ = TryGetValues(recordID, out List<object?>? values, propPaths);
            return values;
        }

        /// <summary>
        /// Reads selected runtime-shaped values from one record payload without hydrating the complete root object.<br/>
        /// </summary>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPaths">One or more non-empty property paths or member expressions in result order.<br/></param>
        /// <returns>Selected values in the supplied property-path order, or null when the same-store record is absent.<br/></returns>
        /// <remarks>
        /// This list-returning overload differs from the typed tuple overloads: an absent same-store record returns null instead of throwing for absence.<br/>
        /// </remarks>
        public List<object?>? GetValues(ulong recordID, params Expression<Func<T, object>>[] propPaths)
        {
            _ = TryGetValues(recordID, out List<object?>? values, propPaths);
            return values;
        }

        /// <summary>
        /// Reads 2 selected values from one borrowed record payload and returns them as a typed value tuple.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPaths">Exactly 2 non-empty Inheto property paths in tuple order.<br/></param>
        /// <returns>Decoded values in the supplied property-path order.<br/></returns>
        /// <exception cref="KeyNotFoundException">The same-store record does not exist.<br/></exception>
        public (T1?, T2?) GetValues<T1, T2>(ulong recordID, params string[] propPaths)
        {
            if (TryGetValues(recordID, out (T1?, T2?) values, propPaths))
                return values;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Reads three typed properties from one identity while borrowing a single Fractal payload.<br/>
        /// The tuple preserves property-path order and an absent record throws consistently with the other throwing <c>GetValues</c> overloads.<br/>
        /// </summary>
        /// <typeparam name="T1">First requested property type.<br/></typeparam>
        /// <typeparam name="T2">Second requested property type.<br/></typeparam>
        /// <typeparam name="T3">Third requested property type.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="propPaths">Exactly three Inheto property paths in tuple order.<br/></param>
        /// <returns>The three decoded values in supplied path order.<br/></returns>
        public (T1?, T2?, T3?) GetValues<T1, T2, T3>(ulong recordID, params string[] propPaths)
        {
            if (TryGetValues(recordID, out (T1?, T2?, T3?) values, propPaths))
                return values;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Reads 4 selected values from one borrowed record payload and returns them as a typed value tuple.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPaths">Exactly 4 non-empty Inheto property paths in tuple order.<br/></param>
        /// <returns>Decoded values in the supplied property-path order.<br/></returns>
        /// <exception cref="KeyNotFoundException">The same-store record does not exist.<br/></exception>
        public (T1?, T2?, T3?, T4?) GetValues<T1, T2, T3, T4>(ulong recordID, params string[] propPaths)
        {
            if (TryGetValues(recordID, out (T1?, T2?, T3?, T4?) values, propPaths))
                return values;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Reads 5 selected values from one borrowed record payload and returns them as a typed value tuple.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <typeparam name="T5">Requested CLR type of the fifth selected value.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPaths">Exactly 5 non-empty Inheto property paths in tuple order.<br/></param>
        /// <returns>Decoded values in the supplied property-path order.<br/></returns>
        /// <exception cref="KeyNotFoundException">The same-store record does not exist.<br/></exception>
        public (T1?, T2?, T3?, T4?, T5?) GetValues<T1, T2, T3, T4, T5>(ulong recordID, params string[] propPaths)
        {
            if (TryGetValues(recordID, out (T1?, T2?, T3?, T4?, T5?) values, propPaths))
                return values;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Reads 6 selected values from one borrowed record payload and returns them as a typed value tuple.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <typeparam name="T5">Requested CLR type of the fifth selected value.<br/></typeparam>
        /// <typeparam name="T6">Requested CLR type of the sixth selected value.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPaths">Exactly 6 non-empty Inheto property paths in tuple order.<br/></param>
        /// <returns>Decoded values in the supplied property-path order.<br/></returns>
        /// <exception cref="KeyNotFoundException">The same-store record does not exist.<br/></exception>
        public (T1?, T2?, T3?, T4?, T5?, T6?) GetValues<T1, T2, T3, T4, T5, T6>(ulong recordID, params string[] propPaths)
        {
            if (TryGetValues(recordID, out (T1?, T2?, T3?, T4?, T5?, T6?) values, propPaths))
                return values;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// Reads 7 selected values from one borrowed record payload and returns them as a typed value tuple.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested CLR type of the first selected value.<br/></typeparam>
        /// <typeparam name="T2">Requested CLR type of the second selected value.<br/></typeparam>
        /// <typeparam name="T3">Requested CLR type of the third selected value.<br/></typeparam>
        /// <typeparam name="T4">Requested CLR type of the fourth selected value.<br/></typeparam>
        /// <typeparam name="T5">Requested CLR type of the fifth selected value.<br/></typeparam>
        /// <typeparam name="T6">Requested CLR type of the sixth selected value.<br/></typeparam>
        /// <typeparam name="T7">Requested CLR type of the seventh selected value.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; an absent same-store record follows this overload's documented absence behavior.<br/></param>
        /// <param name="propPaths">Exactly 7 non-empty Inheto property paths in tuple order.<br/></param>
        /// <returns>Decoded values in the supplied property-path order.<br/></returns>
        /// <exception cref="KeyNotFoundException">The same-store record does not exist.<br/></exception>
        public (T1?, T2?, T3?, T4?, T5?, T6?, T7?) GetValues<T1, T2, T3, T4, T5, T6, T7>(ulong recordID, params string[] propPaths)
        {
            if (TryGetValues(recordID, out (T1?, T2?, T3?, T4?, T5?, T6?, T7?) values, propPaths))
                return values;
            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");
        }

        /// <summary>
        /// One complete simple-key index uses LibraDex grouping metadata; multi-key, computed-key, missing-key, null-key, or unavailable-index shapes use one authoritative Fractal/Inheto pass.<br/>
        /// Computed keys require an executable and durable backend-neutral selector descriptor so group identity and returned drill-down conditions preserve the complete expression.<br/>
        /// </summary>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>An enumeration of <see cref="GroupInfo"/> instances describing each group.</returns>
        public IEnumerable<GroupInfo> IterateGroupings(params ISelector[] groupKeys)
        {
            ArgumentNullException.ThrowIfNull(groupKeys);
            if (groupKeys.Length == 0)
                throw new ArgumentException("At least one grouping selector must be specified.", nameof(groupKeys));

            if (TryIterateGroupingsByLibraDex(groupKeys, out var groups))
                return groups;

            return IterateSemanticGroupings(groupKeys);
        }

        /// <summary>
        /// Lazily materializes the provided record IDs as <typeparamref name="T"/> instances in parallel using the fast reader path and yields them as they are ready.
        /// </summary>
        /// <param name="recordIDs">The record identifiers to materialize; each must belong to this store.</param>
        /// <param name="maxDegreeOfParallelism">Optional override for worker count. Defaults to the configured GetManyParallelThreadCount or processor count.</param>
        /// <param name="channelCapacity">Optional override for the bounded channel capacity. Defaults to 4x the degree or 32, whichever is larger.</param>
        /// <param name="cancellationToken">Token to cancel enumeration and stop background workers.</param>
        /// <returns>An unordered sequence of materialized <typeparamref name="T"/> instances corresponding to the requested identifiers.</returns>
        /// <remarks>
        /// Disposing the enumerator stops and joins background workers without cancelling the caller-owned token.<br/>
        /// Disposal waits for in-progress synchronous hydration callbacks or storage reads before releasing worker resources.<br/>
        /// </remarks>
        public IEnumerable<T> IterateManyFast(IEnumerable<ulong> recordIDs, int? maxDegreeOfParallelism = null, int? channelCapacity = null, CancellationToken cancellationToken = default) => IterateManyFastAs<T>(recordIDs, maxDegreeOfParallelism, channelCapacity, cancellationToken);

        /// <summary>
        /// Lazily materializes the provided record IDs as <typeparamref name="TOut"/> instances in parallel using the fast reader path and yields them as they are ready.
        /// </summary>
        /// <param name="recordIDs">The record identifiers to materialize; each must belong to this store.</param>
        /// <param name="maxDegreeOfParallelism">Optional override for worker count. Defaults to the configured GetManyParallelThreadCount or processor count.</param>
        /// <param name="channelCapacity">Optional override for the bounded channel capacity. Defaults to 4x the degree or 32, whichever is larger.</param>
        /// <param name="cancellationToken">Token to cancel enumeration and stop background workers.</param>
        /// <returns>An unordered sequence of materialized <typeparamref name="TOut"/> instances corresponding to the requested identifiers.<br/></returns>
        /// <remarks>
        /// Disposing the enumerator stops and joins background workers without cancelling the caller-owned token.<br/>
        /// Disposal waits for in-progress synchronous hydration callbacks or storage reads before releasing worker resources.<br/>
        /// </remarks>
        public IEnumerable<TOut> IterateManyFastAs<TOut>(IEnumerable<ulong> recordIDs, int? maxDegreeOfParallelism = null, int? channelCapacity = null, CancellationToken cancellationToken = default)
        {
            using var operation = EnterStoreOperationScope();
            ArgumentNullException.ThrowIfNull(recordIDs);
            cancellationToken.ThrowIfCancellationRequested();

            var ids = recordIDs.ToArray();
            foreach (var recordID in ids)
            {
                EnsureRecordBelongsToCurrentStore(recordID, nameof(recordIDs));
            }

            if (ids.Length == 0)
                yield break;

            var degree = maxDegreeOfParallelism
                ?? rbase.config?.FractalStoreConfig.GetManyParallelThreadCount
                ?? Environment.ProcessorCount;

            if (degree < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Degree must be at least 1.");

            var capacity = channelCapacity ?? Math.Max(degree * 4, 32);
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(channelCapacity), "Channel capacity must be at least 1.");

            var channel = Channel.CreateBounded<TOut>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var producerToken = producerCancellation.Token;
            var options = new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = producerToken };

            var producer = Task.Run(async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(ids, options, async (recordID, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var record = rbase.data.GetOne(recordID);
                        if (record.IsEmpty)
                            throw new KeyNotFoundException($"No record found with recordID of '{recordID}'");

                        using (record)
                        {
                            var entity = MaterializeFractalRecordFast<TOut>(recordID, record, nameof(IterateManyFastAs));
                            await channel.Writer.WriteAsync(entity, ct).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, producerToken);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool canRead = channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
                    if (!canRead)
                        break;

                    while (channel.Reader.TryRead(out var item))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return item;
                    }
                }
            }
            finally
            {
                // Stop blocked writes before joining; borrowed records and lifecycle scopes must outlive every worker.
                if (!producer.IsCompleted)
                    producerCancellation.Cancel();

                try
                {
                    producer.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (producerToken.IsCancellationRequested)
                {
                    // A producer cancelled before Task.Run starts has no channel error to observe.
                }
            }
        }

        /// <summary>
        /// Enumerates record IDs using the Fractal hash index for this store.<br/>
        /// This is the native primary-order walk used by the public unsorted iteration path.<br/>
        /// </summary>
        internal IEnumerable<ulong> IterateRecordIDsFromFractal()
        {
            using var operation = EnterStoreOperationScope();
            foreach (var id in rbase.data.IterateRecordIDsByGroupKey(storeID))
                yield return id;
        }

        /// <summary>
        /// Enumerates record IDs using the native FractalKVS store walk.<br/>
        /// </summary>
        public IEnumerable<ulong> IterateRecordIDs()
            => IterateRecordIDsFromFractal();

        /// <summary>
        /// Materializes every live identity in this store using Fractal's complete-list natural-order fast path.<br/>
        /// The returned list is detached and mutable by the caller; modifying it never changes the RecordStore.<br/>
        /// Unlike <see cref="IterateRecordIDs()"/>, this method trades streaming first-row latency and bounded working memory for batched physical header reads, and is intended when the caller already requires the entire population.<br/>
        /// </summary>
        /// <returns>A detached list containing every live store identity in the same order as <see cref="IterateRecordIDs()"/>.<br/></returns>
        public List<ulong> ListRecordIDs()
        {
            using var operation = EnterStoreOperationScope();
            return rbase.data.ListRecordIDsByGroupKey(storeID);
        }

        /// <summary>
        /// Materializes every identity selected by one completed condition using the fastest complete-source route available while preserving the selected source's public order.<br/>
        /// A complete LibraDex plan consumes its maintained cursor directly; an unavoidable Fractal scan uses single-threaded physical bulk reads and restores exact Fractal natural order before returning.<br/>
        /// Unlike <see cref="IterateRecordIDs(Condition{T}.EndCondition, SortBy[])"/>, this method intentionally trades lazy first-row delivery and bounded match memory for complete-list locality and throughput.<br/>
        /// </summary>
        /// <param name="where">Completed Abraxas condition selecting the returned identities.<br/></param>
        /// <returns>A detached mutable list in the same logical source order as condition iteration.<br/></returns>
        public List<ulong> ListRecordIDs(Condition<T>.EndCondition where)
        {
            ArgumentNullException.ThrowIfNull(where);
            var identities = new List<ulong>();
            foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                where,
                nameof(ListRecordIDs),
                includePayload: false,
                preferCompletePhysicalFractalScan: true))
            {
                identities.Add(match.RecordId);
            }

            return identities;
        }

        /// <summary>
        /// Enumerates record IDs in the order defined by an Abraxas record index.<br/>
        /// This is the small public handle form intended for indexes returned by <see cref="GetIndex(string, bool)"/> or <see cref="TryGetIndex(string, out RecordIndex, bool)"/>.<br/>
        /// </summary>
        /// <param name="index">Index handle that identifies the property path to order by.<br/></param>
        /// <param name="direction">Requested retrieval direction.<br/></param>
        /// <returns>Record identifiers in the requested index order.<br/></returns>
        public IEnumerable<ulong> IterateRecordIDs(RecordIndex index, RetrievalDirection direction = RetrievalDirection.Ascending)
        {
            if (TryIterateRecordIDsByLibraDexIndexes(new[] { index }, direction, allowIndexSupportCreation: false, out var ids))
            {
                foreach (var id in ids)
                    yield return id;

                yield break;
            }

            throw new NotSupportedException("Indexed record iteration requires a registered ordered index.");
        }

        /// <summary>
        /// Enumerates record IDs in lexicographic order across multiple Abraxas record indexes.<br/>
        /// The first index supplies the primary ordered stream; later indexes order only equal-key runs through reusable index support.<br/>
        /// </summary>
        /// <param name="allowIndexSupportCreation">When true, Abraxas may create or extend reusable LibraDex inverse support for the supplied indexes.<br/></param>
        /// <param name="indexes">Indexes in priority order.<br/></param>
        /// <returns>Record identifiers in requested multi-index order.<br/></returns>
        public IEnumerable<ulong> IterateRecordIDs(bool allowIndexSupportCreation, params RecordIndex[] indexes)
        {
            foreach (var id in IterateRecordIDs(RetrievalDirection.Ascending, allowIndexSupportCreation, indexes))
                yield return id;
        }

        /// <summary>
        /// Enumerates record IDs in lexicographic order across multiple Abraxas record indexes.<br/>
        /// Passing <see cref="RetrievalDirection.Descending"/> reverses the whole requested order rather than applying mixed per-key direction.<br/>
        /// </summary>
        /// <param name="direction">Whole-order traversal direction requested by the caller.<br/></param>
        /// <param name="allowIndexSupportCreation">When true, Abraxas may create or extend reusable LibraDex inverse support for the supplied indexes.<br/></param>
        /// <param name="indexes">Indexes in priority order.<br/></param>
        /// <returns>Record identifiers in requested multi-index order.<br/></returns>
        public IEnumerable<ulong> IterateRecordIDs(RetrievalDirection direction, bool allowIndexSupportCreation, params RecordIndex[] indexes)
        {
            if (indexes is null || indexes.Length == 0)
            {
                foreach (var id in IterateRecordIDs())
                    yield return id;

                yield break;
            }

            if (TryIterateRecordIDsByLibraDexIndexes(indexes, direction, allowIndexSupportCreation, out var ids))
            {
                foreach (var id in ids)
                    yield return id;

                yield break;
            }

            if (indexes.Length == 1)
            {
                foreach (var id in IterateRecordIDs(indexes[0], direction))
                    yield return id;

                yield break;
            }

            throw new NotSupportedException("Multi-index ordered iteration requires registered LibraDex ordered indexes.");
        }

        /// <summary>
        /// Enumerates record IDs in the order defined by a named Abraxas record index.<br/>
        /// The identifier may be the logical index name, a physical index name, or the indexed property path such as <c>.Age</c> or <c>Age</c>.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Index name or property path.<br/></param>
        /// <param name="direction">Requested retrieval direction.<br/></param>
        /// <returns>Record identifiers in the requested index order.<br/></returns>
        public IEnumerable<ulong> IterateRecordIDs(string indexIdentifier, RetrievalDirection direction = RetrievalDirection.Ascending)
        {
            if (!TryGetOrCreateAutoLibraDexIndex(indexIdentifier, AutoIndexUse.Ordering, out var index))
                throw new KeyNotFoundException($"No index found for '{indexIdentifier}', and auto-indexing could not create a volatile ordered helper index.");

            foreach (var id in IterateRecordIDs(index, direction))
                yield return id;
        }

        /// <summary>
        /// Existing or auto-created LibraDex indexes are reused for compatible uniform-direction shapes; other shapes use the authoritative Fractal/Inheto fallback.<br/>
        /// </summary>
        /// <param name="sort">Property-path descriptors in lexicographic priority order.<br/></param>
        /// <returns>Ordered record identities, or native Fractal order when no descriptors are supplied.<br/></returns>
        public IEnumerable<ulong> IterateRecordIDs(params SortBy[] sort)
        {
            if (sort is null || sort.Length == 0)
                return IterateRecordIDs();

            if (TryIterateCompleteStoreRecordIDsByLibraDex(sort, out IEnumerable<ulong> ordered))
            {
                OnOrderingExecutionPlanned(
                    nameof(IterateRecordIDs),
                    OrderingExecutionPlanKind.IndexDirectCompleteStore,
                    sort,
                    completeStorePopulation: true,
                    "A mutation-converged complete LibraDex index shape supplies the complete store order directly.");
                return ordered;
            }

            OnOrderingExecutionPlanned(
                nameof(IterateRecordIDs),
                OrderingExecutionPlanKind.DefensiveCandidateOrdering,
                sort,
                completeStorePopulation: true,
                "No complete compatible direct index shape was eligible; Abraxas retained candidate validation and authoritative fallback semantics.");
            return OrderRecordIDsNatively(IterateRecordIDs(), ignoreCase: false, sort);
        }

        /// <summary>
        /// Case-folded shapes use the direct Fractal/Inheto fallback unless a future backend-neutral text-order contract can prove equivalent reusable index semantics.<br/>
        /// </summary>
        /// <param name="ignoreCase">When true, compares text keys with ordinal case folding.<br/></param>
        /// <param name="sort">Property-path descriptors in lexicographic priority order.<br/></param>
        /// <returns>Ordered record identities, or native Fractal order when no descriptors are supplied.<br/></returns>
        public IEnumerable<ulong> IterateRecordIDs(bool ignoreCase, params SortBy[] sort)
        {
            if (sort is null || sort.Length == 0)
                return IterateRecordIDs();

            if (!ignoreCase)
                return IterateRecordIDs(sort);

            OnOrderingExecutionPlanned(
                nameof(IterateRecordIDs),
                OrderingExecutionPlanKind.DefensiveCandidateOrdering,
                sort,
                completeStorePopulation: true,
                "Case-folded ordering requires the authoritative compatibility route unless an exact folded ordering contract is requested explicitly.");
            return OrderRecordIDsNatively(IterateRecordIDs(), ignoreCase, sort);
        }

        /// <summary>
        /// Lazily executes a completed condition through LibraDex when fully indexable, or through a Fractal/Inheto scan otherwise.<br/>
        /// Planning and operand materialization occur once when enumeration begins, while record payload evaluation remains lazy.<br/>
        /// </summary>
        /// <param name="where">The completed Abraxas condition to execute.<br/></param>
        /// <param name="operationName">The public operation name reported through condition lifecycle hooks.<br/></param>
        /// <returns>Matching record identifiers from the selected execution path.<br/></returns>
        private IEnumerable<ulong> IterateRecordIDsByLibraDexConditionOrThrow(Condition<T>.EndCondition where, string operationName)
        {
            ArgumentNullException.ThrowIfNull(where);

            foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                where,
                operationName,
                includePayload: false))
            {
                yield return match.RecordId;
            }
        }

        /// <summary>
        /// Enumerates authoritative identities matching the condition without hydrating complete record objects.<br/>
        /// Supplied sort components apply lexicographic ordering; without them, traversal follows the selected condition source.<br/>
        /// </summary>
        /// <param name="Where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="sort">Optional property-sort descriptors in lexicographic priority order; no descriptors retain the selected source order.<br/></param>
        /// <returns>Matching store-owned record identities in requested sort order, or the selected source order when no sort is supplied.<br/></returns>
        /// <remarks>
        /// Ordering may reuse a compatible maintained index or prepare an authoritative candidate order according to the store's ordering policy.<br/>
        /// </remarks>
        public IEnumerable<ulong> IterateRecordIDs(Condition<T>.EndCondition Where, params SortBy[] sort)
        {
            return sort is { Length: > 0 }
                ? IterateConditionRecordIDsInRequestedOrder(Where, ignoreCase: false, sort, nameof(IterateRecordIDs))
                : IterateRecordIDsByLibraDexConditionOrThrow(Where, nameof(IterateRecordIDs));
        }

        /// <summary>
        /// Enumerates authoritative identities matching the condition without hydrating complete record objects.<br/>
        /// Supplied sort components apply lexicographic ordering; without them, traversal follows the selected condition source.<br/>
        /// </summary>
        /// <param name="Where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <param name="sort">Optional property-sort descriptors in lexicographic priority order; no descriptors retain the selected source order.<br/></param>
        /// <returns>Matching store-owned record identities in requested sort order, or the selected source order when no sort is supplied.<br/></returns>
        /// <remarks>
        /// Ordering may reuse a compatible maintained index or prepare an authoritative candidate order according to the store's ordering policy.<br/>
        /// </remarks>
        public IEnumerable<ulong> IterateRecordIDs(Condition<T>.EndCondition Where, bool useMemoryIndex, params SortBy[] sort)
        {
            return sort is { Length: > 0 }
                ? IterateConditionRecordIDsInRequestedOrder(Where, ignoreCase: false, sort, nameof(IterateRecordIDs))
                : IterateRecordIDsByLibraDexConditionOrThrow(Where, nameof(IterateRecordIDs));
        }

        /// <summary>
        /// Enumerates authoritative identities matching the condition without hydrating complete record objects.<br/>
        /// Supplied sort components apply lexicographic ordering; without them, traversal follows the selected condition source.<br/>
        /// </summary>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="ignoreCase">Whether text sort-key comparison uses ordinal case folding in the authoritative ordering fallback.<br/></param>
        /// <param name="propPaths">Optional ascending sort property paths in lexicographic priority order.<br/></param>
        /// <returns>Matching store-owned record identities in requested sort order, or the selected source order when no sort is supplied.<br/></returns>
        /// <remarks>
        /// Ordering may reuse a compatible maintained index or prepare an authoritative candidate order according to the store's ordering policy.<br/>
        /// </remarks>
        public IEnumerable<ulong> IterateRecordIDs(Condition<T>.EndCondition where, bool ignoreCase, params string[] propPaths)
        {
            if (propPaths is not { Length: > 0 })
                return IterateRecordIDsByLibraDexConditionOrThrow(where, nameof(IterateRecordIDs));

            SortBy[] sort = BuildSortDescriptors(ignoreCase, propPaths);
            return IterateConditionRecordIDsInRequestedOrder(where, ignoreCase, sort, nameof(IterateRecordIDs));
        }

        /// <summary>
        /// Enumerates record identifiers by developer-defined state through Abraxas' ordered State index.<br/>
        /// Exact state lookup uses the ordered backend directly; masked lookup preserves the existing <c>(State &amp; mask) == state</c> rule over State tuples.<br/>
        /// </summary>
        /// <param name="state">State value to compare against.<br/></param>
        /// <param name="mask">Optional bitmask. When provided, records satisfy <c>(State &amp; mask) == state</c>.<br/></param>
        /// <returns>Identifiers belonging to this store that match the supplied state criteria.<br/></returns>
        public IEnumerable<ulong> IterateRecordIDs(long state, long? mask = null)
        {
            if (TryIterateRecordIDsByLibraDexState(state, mask, out var libraDexIds))
            {
                foreach (var id in libraDexIds)
                    yield return id;

                yield break;
            }

            throw new InvalidOperationException(
                $"Operation '{nameof(IterateRecordIDs)}' requires this store's reserved persistent LibraDex State index.");
        }

        /// <summary>
        /// Retains the historical State-iteration signature while directing callers to persistent-only State selection.<br/>
        /// </summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching record identities.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordIDs(state, mask) instead.")]
        public IEnumerable<ulong> IterateRecordIDs(long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordIDs(state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State iteration calls that omitted a mask.<br/></summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching record identities.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordIDs(state) instead.")]
        public IEnumerable<ulong> IterateRecordIDs(long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordIDs(state);
        }

        /// <summary>
        /// Enumerates authoritative identities matching the condition without hydrating complete record objects.<br/>
        /// Supplied sort components apply lexicographic ordering; without them, traversal follows the selected condition source.<br/>
        /// </summary>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <param name="ignoreCase">Whether text sort-key comparison uses ordinal case folding in the authoritative ordering fallback.<br/></param>
        /// <param name="propPaths">Optional ascending sort property paths in lexicographic priority order.<br/></param>
        /// <returns>Matching store-owned record identities in requested sort order, or the selected source order when no sort is supplied.<br/></returns>
        /// <remarks>
        /// Ordering may reuse a compatible maintained index or prepare an authoritative candidate order according to the store's ordering policy.<br/>
        /// </remarks>
        public IEnumerable<ulong> IterateRecordIDs(Condition<T>.EndCondition where, bool useMemoryIndex, bool ignoreCase, params string[] propPaths)
        {
            if (propPaths is not { Length: > 0 })
                return IterateRecordIDsByLibraDexConditionOrThrow(where, nameof(IterateRecordIDs));

            SortBy[] sort = BuildSortDescriptors(ignoreCase, propPaths);
            return IterateConditionRecordIDsInRequestedOrder(where, ignoreCase, sort, nameof(IterateRecordIDs));
        }

        /// <summary>
        /// Lazily yields every record in this store using the underlying Fractal payloads.
        /// </summary>
        /// <returns>An enumeration of materialized <typeparamref name="T"/> instances.</returns>
        public IEnumerable<T> IterateRecords()
            => IterateRows(Readers.Open());

        /// <summary>
        /// Lazily yields records in the order defined by an Abraxas record index.<br/>
        /// This overload keeps retrieval vocabulary Abraxas-focused while allowing callers to reuse index handles returned by lookup methods.<br/>
        /// </summary>
        /// <param name="index">Index handle that identifies the property path to order by.<br/></param>
        /// <param name="direction">Requested retrieval direction.<br/></param>
        /// <returns>An enumeration of materialized records in the requested index order.<br/></returns>
        public IEnumerable<T> IterateRecords(RecordIndex index, RetrievalDirection direction = RetrievalDirection.Ascending)
        {
            foreach (var id in IterateRecordIDs(index, direction))
                yield return Get(id)!;
        }

        /// <summary>
        /// Lazily yields records in the order defined by a named Abraxas record index.<br/>
        /// The identifier may be the logical index name, a physical index name, or the indexed property path such as <c>.Age</c> or <c>Age</c>.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Index name or property path.<br/></param>
        /// <param name="direction">Requested retrieval direction.<br/></param>
        /// <returns>An enumeration of materialized records in the requested index order.<br/></returns>
        public IEnumerable<T> IterateRecords(string indexIdentifier, RetrievalDirection direction = RetrievalDirection.Ascending)
        {
            if (!TryGetOrCreateAutoLibraDexIndex(indexIdentifier, AutoIndexUse.Ordering, out var index))
                throw new KeyNotFoundException($"No index found for '{indexIdentifier}', and auto-indexing could not create a volatile ordered helper index.");

            foreach (var record in IterateRecords(index, direction))
                yield return record;
        }

        /// <summary>
        /// Lazily yields records in lexicographic order across multiple Abraxas record indexes.<br/>
        /// The ordered identity stream is produced by <see cref="IterateRecordIDs(bool, RecordIndex[])"/> and each record is hydrated through the existing store retrieval path.<br/>
        /// </summary>
        /// <param name="allowIndexSupportCreation">When true, Abraxas may create or extend reusable LibraDex inverse support for the supplied indexes.<br/></param>
        /// <param name="indexes">Indexes in priority order.<br/></param>
        /// <returns>Records in requested multi-index order.<br/></returns>
        public IEnumerable<T> IterateRecords(bool allowIndexSupportCreation, params RecordIndex[] indexes)
        {
            foreach (var record in IterateRecords(RetrievalDirection.Ascending, allowIndexSupportCreation, indexes))
                yield return record;
        }

        /// <summary>
        /// Lazily yields records in lexicographic order across multiple Abraxas record indexes.<br/>
        /// Passing <see cref="RetrievalDirection.Descending"/> reverses the whole requested order rather than applying mixed per-key direction.<br/>
        /// </summary>
        /// <param name="direction">Whole-order traversal direction requested by the caller.<br/></param>
        /// <param name="allowIndexSupportCreation">When true, Abraxas may create or extend reusable LibraDex inverse support for the supplied indexes.<br/></param>
        /// <param name="indexes">Indexes in priority order.<br/></param>
        /// <returns>Records in requested multi-index order.<br/></returns>
        public IEnumerable<T> IterateRecords(RetrievalDirection direction, bool allowIndexSupportCreation, params RecordIndex[] indexes)
        {
            foreach (var id in IterateRecordIDs(direction, allowIndexSupportCreation, indexes))
                yield return Get(id)!;
        }

        /// <summary>
        /// Lazily yields records filtered by the supplied condition builder.
        /// </summary>
        /// <param name="where">Condition used to filter records.</param>
        /// <param name="sort">Optional property-sort descriptors applied before the filtered records are materialized.<br/></param>
        /// <returns>An enumeration of materialized <typeparamref name="T"/> instances that satisfy the condition.</returns>
        public IEnumerable<T> IterateRecords(Condition<T>.EndCondition where, params SortBy[] sort)
            => IterateRows(OpenRows(where, sort));

        /// <summary>
        /// Enumerates this store's records, applying any supplied ordering before hydration.<br/>
        /// </summary>
        /// <param name="ignoreCase">Whether text sort-key comparison uses ordinal case folding in the authoritative ordering fallback.<br/></param>
        /// <param name="sort">Optional property-sort descriptors in lexicographic priority order; no descriptors retain the selected source order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<T> IterateRecords(bool ignoreCase, params SortBy[] sort)
        {
            foreach (var id in IterateRecordIDs(ignoreCase, sort))
                yield return Get(id);
        }

        /// <summary>
        /// Enumerates records selected by the supplied condition, applying any requested ordering before hydration.<br/>
        /// </summary>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <param name="sort">Optional property-sort descriptors in lexicographic priority order; no descriptors retain the selected source order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<T> IterateRecords(Condition<T>.EndCondition where, bool useMemoryIndex, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            _ = useMemoryIndex;
            return IterateRows(OpenRows(where, sort));
        }

        /// <summary>
        /// Enumerates records selected by the supplied condition, applying any requested ordering before hydration.<br/>
        /// </summary>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="ignoreCase">Whether text sort-key comparison uses ordinal case folding in the authoritative ordering fallback.<br/></param>
        /// <param name="propPaths">Optional ascending sort property paths in lexicographic priority order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<T> IterateRecords(Condition<T>.EndCondition where, bool ignoreCase, params string[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(where);

            foreach (var recordId in IterateRecordIDs(where, ignoreCase, propPaths))
            {
                yield return Get(recordId);
            }
        }

        /// <summary>
        /// Lazily yields records filtered by <paramref name="state"/> and, when supplied, a bitmask.
        /// </summary>
        /// <param name="state">State value stored in the metadata table.</param>
        /// <param name="mask">Optional bitmask. When provided, records satisfy <c>(State &amp; mask) == state</c>.</param>
        /// <returns>An enumeration of materialized <typeparamref name="T"/> instances.</returns>
        public IEnumerable<T> IterateRecords(long state, long? mask = null)
            => IterateRows(Readers.Open(state, mask));

        /// <summary>
        /// Retains the historical State-filtered record iteration signature while directing callers to persistent-only State selection.<br/>
        /// </summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching materialized records.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecords(state, mask) instead.")]
        public IEnumerable<T> IterateRecords(long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecords(state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State record iteration calls that omitted a mask.<br/></summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching materialized records.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecords(state) instead.")]
        public IEnumerable<T> IterateRecords(long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecords(state);
        }

        /// <summary>
        /// Enumerates records selected by the supplied condition, applying any requested ordering before hydration.<br/>
        /// </summary>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <param name="ignoreCase">Whether text sort-key comparison uses ordinal case folding in the authoritative ordering fallback.<br/></param>
        /// <param name="propPaths">Optional ascending sort property paths in lexicographic priority order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<T> IterateRecords(Condition<T>.EndCondition where, bool useMemoryIndex, bool ignoreCase, params string[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(where);

            foreach (var recordId in IterateRecordIDs(where, useMemoryIndex, ignoreCase, propPaths))
            {
                yield return Get(recordId);
            }
        }

        /// <summary>
        /// Enumerates this store's records, applying any supplied ordering before hydration.<br/>
        /// Each result is hydrated into the caller-selected CLR shape; stored path matching does not require inheritance from the original record type.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        public IEnumerable<TOut> IterateRecordsAs<TOut>()
            => IterateRowsAs<TOut>(Readers.Open());

        /// <summary>
        /// Enumerates records selected by the supplied condition, applying any requested ordering before hydration.<br/>
        /// Each result is hydrated into the caller-selected CLR shape; stored path matching does not require inheritance from the original record type.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="sort">Optional property-sort descriptors in lexicographic priority order; no descriptors retain the selected source order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<TOut> IterateRecordsAs<TOut>(Condition<T>.EndCondition where, params SortBy[] sort)
            => IterateRowsAs<TOut>(OpenRows(where, sort));

        /// <summary>
        /// Enumerates records selected by the supplied condition, applying any requested ordering before hydration.<br/>
        /// Each result is hydrated into the caller-selected CLR shape; stored path matching does not require inheritance from the original record type.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <param name="sort">Optional property-sort descriptors in lexicographic priority order; no descriptors retain the selected source order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<TOut> IterateRecordsAs<TOut>(Condition<T>.EndCondition where, bool useMemoryIndex, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            _ = useMemoryIndex;
            return IterateRowsAs<TOut>(OpenRows(where, sort));
        }

        /// <summary>
        /// Enumerates records selected by the supplied condition, applying any requested ordering before hydration.<br/>
        /// Each result is hydrated into the caller-selected CLR shape; stored path matching does not require inheritance from the original record type.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="ignoreCase">Whether text sort-key comparison uses ordinal case folding in the authoritative ordering fallback.<br/></param>
        /// <param name="propPaths">Optional ascending sort property paths in lexicographic priority order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<TOut> IterateRecordsAs<TOut>(Condition<T>.EndCondition where, bool ignoreCase, params string[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(where);

            foreach (var recordId in IterateRecordIDs(where, ignoreCase, propPaths))
            {
                yield return GetAs<TOut>(recordId);
            }
        }

        /// <summary>
        /// Enumerates records whose persistent State matches the supplied value and optional bitmask.<br/>
        /// Each result is hydrated into the caller-selected CLR shape; stored path matching does not require inheritance from the original record type.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="state">Persistent State value matched exactly or through the supplied bitmask.<br/></param>
        /// <param name="mask">Optional bitmask requiring (State &amp; mask) == state.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        public IEnumerable<TOut> IterateRecordsAs<TOut>(long state, long? mask = null)
            => IterateRowsAs<TOut>(Readers.Open(state, mask));

        /// <summary>
        /// Retains the historical projected State-iteration signature while directing callers to persistent-only State selection.<br/>
        /// </summary>
        /// <typeparam name="TOut">Projection type materialized from each matching payload.<br/></typeparam>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching materialized projections.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordsAs<TOut>(state, mask) instead.")]
        public IEnumerable<TOut> IterateRecordsAs<TOut>(long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordsAs<TOut>(state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> projected State iteration calls that omitted a mask.<br/></summary>
        /// <typeparam name="TOut">Projection type materialized from each matching payload.<br/></typeparam>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching materialized projections.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordsAs<TOut>(state) instead.")]
        public IEnumerable<TOut> IterateRecordsAs<TOut>(long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordsAs<TOut>(state);
        }

        /// <summary>
        /// Enumerates records selected by the supplied condition, applying any requested ordering before hydration.<br/>
        /// Each result is hydrated into the caller-selected CLR shape; stored path matching does not require inheritance from the original record type.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="where">Completed condition selecting matching authoritative records.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; current execution selects available maintained indexes independently of this parameter.<br/></param>
        /// <param name="ignoreCase">Whether text sort-key comparison uses ordinal case folding in the authoritative ordering fallback.<br/></param>
        /// <param name="propPaths">Optional ascending sort property paths in lexicographic priority order.<br/></param>
        /// <returns>A sequence of hydrated records or projections in the selected source or requested sort order.<br/></returns>
        /// <remarks>
        /// Ordering may require preparation and is not a promise of constant-memory or immediate first-result traversal.<br/>
        /// </remarks>
        public IEnumerable<TOut> IterateRecordsAs<TOut>(Condition<T>.EndCondition where, bool useMemoryIndex, bool ignoreCase, params string[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(where);

            foreach (var recordId in IterateRecordIDs(where, useMemoryIndex, ignoreCase, propPaths))
            {
                yield return GetAs<TOut>(recordId);
            }
        }

        /// <summary>Iterates all records yielding their IDs alongside materialized instances.<br/></summary>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds()
            => IterateRowsWithIds(Readers.Open());

        /// <summary>Iterates records matching the condition, returning IDs with materialized instances in sort order when provided.<br/></summary>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(Condition<T>.EndCondition where, params SortBy[] sort)
            => IterateRowsWithIds(OpenRows(where, sort));

        /// <summary>Iterates records with optional case-insensitive sorting, yielding IDs with materialized instances.<br/></summary>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(bool ignoreCase, params SortBy[] sort)
            => IterateWithIdsCore(IterateRecordIDs(ignoreCase, sort), Get);

        /// <summary>Iterates records matching the condition using the selected index source, yielding IDs with materialized instances.<br/></summary>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(Condition<T>.EndCondition where, bool useMemoryIndex, params SortBy[] sort)
            => IterateRowsWithIds(OpenRows(where, sort));

        /// <summary>Iterates records with dynamic sort descriptors built from property paths, yielding IDs with materialized instances.<br/></summary>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(Condition<T>.EndCondition where, bool ignoreCase, params string[] propPaths)
            => IterateWithIdsCore(IterateRecordIDs(where, ignoreCase, propPaths), Get);

        /// <summary>Iterates records filtered by state/mask, yielding IDs with materialized instances.<br/></summary>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(long state, long? mask = null)
            => IterateRowsWithIds(Readers.Open(state, mask));

        /// <summary>Retains the historical State-filtered identity/record signature while State remains persistent-only.<br/></summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching identities paired with materialized records.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordsWithIds(state, mask) instead.")]
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordsWithIds(state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State identity/record calls that omitted a mask.<br/></summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching identities paired with materialized records.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordsWithIds(state) instead.")]
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordsWithIds(state);
        }

        /// <summary>Iterates records with dynamic sort descriptors and index selection, yielding IDs with materialized instances.<br/></summary>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsWithIds(Condition<T>.EndCondition where, bool useMemoryIndex, bool ignoreCase, params string[] propPaths)
            => IterateWithIdsCore(IterateRecordIDs(where, useMemoryIndex, ignoreCase, propPaths), Get);

        /// <summary>Iterates all records yielding IDs with materialized projections of type <typeparamref name="TOut"/>.<br/></summary>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>()
            => IterateRowsWithIdsAs<TOut>(Readers.Open());

        /// <summary>Iterates projected records matching the condition and optional sort, yielding IDs with materialized projections.<br/></summary>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(Condition<T>.EndCondition where, params SortBy[] sort)
            => IterateRowsWithIdsAs<TOut>(OpenRows(where, sort));

        /// <summary>Iterates projected records with optional case-insensitive sorting, yielding IDs with materialized projections.<br/></summary>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(bool ignoreCase, params SortBy[] sort)
            => IterateWithIdsCore(IterateRecordIDs(ignoreCase, sort), GetAs<TOut>);

        /// <summary>Iterates projected records using the selected index source, yielding IDs with materialized projections.<br/></summary>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(Condition<T>.EndCondition where, bool useMemoryIndex, params SortBy[] sort)
            => IterateRowsWithIdsAs<TOut>(OpenRows(where, sort));

        /// <summary>Iterates projected records using dynamic sort descriptors, yielding IDs with materialized projections.<br/></summary>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(Condition<T>.EndCondition where, bool ignoreCase, params string[] propPaths)
            => IterateWithIdsCore(IterateRecordIDs(where, ignoreCase, propPaths), GetAs<TOut>);

        /// <summary>Iterates projected records filtered by state/mask, yielding IDs with materialized projections.<br/></summary>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(long state, long? mask = null)
            => IterateRowsWithIdsAs<TOut>(Readers.Open(state, mask));

        /// <summary>Retains the historical projected State-filtered identity/record signature while State remains persistent-only.<br/></summary>
        /// <typeparam name="TOut">Projection type materialized from each matching payload.<br/></typeparam>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching identities paired with materialized projections.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordsWithIdsAs<TOut>(state, mask) instead.")]
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordsWithIdsAs<TOut>(state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> projected State identity/record calls that omitted a mask.<br/></summary>
        /// <typeparam name="TOut">Projection type materialized from each matching payload.<br/></typeparam>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Matching identities paired with materialized projections.<br/></returns>
        [Obsolete("State is always persistent. Use IterateRecordsWithIdsAs<TOut>(state) instead.")]
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return IterateRecordsWithIdsAs<TOut>(state);
        }

        /// <summary>Iterates projected records with dynamic sort descriptors and index selection, yielding IDs with materialized projections.<br/></summary>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsWithIdsAs<TOut>(Condition<T>.EndCondition where, bool useMemoryIndex, bool ignoreCase, params string[] propPaths)
            => IterateWithIdsCore(IterateRecordIDs(where, useMemoryIndex, ignoreCase, propPaths), GetAs<TOut>);

        /// <summary>
        /// Opens the canonical flexible row model for one legacy iterator request that may include property sorting.<br/>
        /// Empty sorting keeps the condition planner's natural selected-source order; non-empty sorting preserves the iterator overload's historical explicit order without restoring a second public reader family.<br/>
        /// </summary>
        /// <param name="where">Completed condition selecting the logical population.<br/></param>
        /// <param name="sort">Optional detached property-sort descriptors.<br/></param>
        /// <returns>A canonical row reader positioned before the first selected row.<br/></returns>
        private RowReader OpenRows(Condition<T>.EndCondition where, SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(sort);
            return sort.Length == 0
                ? Readers.Open(where)
                : new RowReader(this, CreateConditionRowPlan(where, sort), nameof(ReaderOperations.OpenSorted));
        }

        /// <summary>
        /// Adapts a canonical row reader to the established lazy base-record iterator surface.<br/>
        /// The reader remains the sole owner of cursor and borrowed-payload lifetime and is disposed when enumeration completes or stops early.<br/>
        /// </summary>
        /// <param name="reader">Canonical reader whose current rows are materialized only as enumeration advances.<br/></param>
        /// <returns>A lazy sequence of this store's declared record shape.<br/></returns>
        private static IEnumerable<T> IterateRows(RowReader reader)
        {
            using (reader)
            {
                while (reader.Read())
                    yield return reader.Get();
            }
        }

        /// <summary>
        /// Adapts a canonical row reader to a lazy alternate-shape iterator without introducing a typed-reader factory.<br/>
        /// Reprojection remains demand-driven for each row and preserves the reader's disposal boundary.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested object or projection shape.<br/></typeparam>
        /// <param name="reader">Canonical reader whose current rows supply the alternate shape.<br/></param>
        /// <returns>A lazy sequence of reprojected records.<br/></returns>
        private static IEnumerable<TOut> IterateRowsAs<TOut>(RowReader reader)
        {
            using (reader)
            {
                while (reader.Read())
                    yield return reader.GetAs<TOut>();
            }
        }

        /// <summary>
        /// Adapts a canonical row reader to the established identity-and-record iterator surface.<br/>
        /// Identity is read directly from the cursor and the payload is materialized once only when the iterator advances.<br/>
        /// </summary>
        /// <param name="reader">Canonical reader supplying both current identity and payload.<br/></param>
        /// <returns>A lazy sequence of identities paired with base records.<br/></returns>
        private static IEnumerable<(ulong ID, T Record)> IterateRowsWithIds(RowReader reader)
        {
            using (reader)
            {
                while (reader.Read())
                    yield return (reader.ID, reader.Get());
            }
        }

        /// <summary>
        /// Adapts a canonical row reader to the established identity-and-projection iterator surface.<br/>
        /// The identity remains cursor-native and alternate-shape materialization occurs exactly once per yielded row.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested object or projection shape.<br/></typeparam>
        /// <param name="reader">Canonical reader supplying the current identity and payload.<br/></param>
        /// <returns>A lazy sequence of identities paired with reprojected records.<br/></returns>
        private static IEnumerable<(ulong ID, TOut Record)> IterateRowsWithIdsAs<TOut>(RowReader reader)
        {
            using (reader)
            {
                while (reader.Read())
                    yield return (reader.ID, reader.GetAs<TOut>());
            }
        }

        /// <summary>
        /// Lazily materializes records in parallel using the fast reader path and yields them as they are ready.
        /// </summary>
        /// <param name="maxDegreeOfParallelism">Optional override for worker count. Defaults to the configured GetManyParallelThreadCount or processor count.</param>
        /// <param name="channelCapacity">Optional override for the bounded channel capacity. Defaults to 4x the degree or 32, whichever is larger.</param>
        /// <param name="cancellationToken">Token to cancel enumeration and stop background workers.</param>
        /// <returns>An unordered sequence of materialized <typeparamref name="T"/> instances.</returns>
        /// <remarks>
        /// Disposing the enumerator stops and joins background workers without cancelling the caller-owned token.<br/>
        /// Disposal waits for in-progress synchronous hydration callbacks or storage reads before releasing worker resources.<br/>
        /// </remarks>
        public IEnumerable<T> IterateRecordsFast(int? maxDegreeOfParallelism = null, int? channelCapacity = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var degree = maxDegreeOfParallelism
                ?? rbase.config?.FractalStoreConfig.GetManyParallelThreadCount
                ?? Environment.ProcessorCount;

            if (degree < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Degree must be at least 1.");

            var capacity = channelCapacity ?? Math.Max(degree * 4, 32);
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(channelCapacity), "Channel capacity must be at least 1.");

            var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var producerToken = producerCancellation.Token;
            var options = new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = producerToken };

            var producer = Task.Run(async () =>
            {
                try
                {
                    var ids = IterateRecordIDs();
                    await Parallel.ForEachAsync(ids, options, async (recordId, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var record = rbase.data.GetOne(recordId);
                        if (record.IsEmpty)
                            throw new KeyNotFoundException($"No record found with recordID of '{recordId}'");

                        using (record)
                        {
                            var entity = MaterializeFractalRecordFast<T>(recordId, record, nameof(IterateRecordsFast));
                            await channel.Writer.WriteAsync(entity, ct).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, producerToken);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool canRead = channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
                    if (!canRead)
                        break;

                    while (channel.Reader.TryRead(out var item))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return item;
                    }
                }
            }
            finally
            {
                // Stop blocked writes before joining; borrowed records and lifecycle scopes must outlive every worker.
                if (!producer.IsCompleted)
                    producerCancellation.Cancel();

                try
                {
                    producer.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (producerToken.IsCancellationRequested)
                {
                    // A producer cancelled before Task.Run starts has no channel error to observe.
                }
            }
        }

        /// <summary>Lazily materializes records in parallel, yielding tuples of record IDs and instances as they are ready.<br/></summary>
        /// <param name="maxDegreeOfParallelism">Optional override for worker count; defaults to the configured GetManyParallelThreadCount or processor count.<br/></param>
        /// <param name="channelCapacity">Optional override for the bounded channel capacity; defaults to 4x the degree or 32.<br/></param>
        /// <param name="cancellationToken">Token to cancel enumeration and stop background workers.<br/></param>
        /// <returns>An unordered sequence of <c>(ID, Record)</c> tuples for materialized records.<br/></returns>
        /// <remarks>
        /// Disposing the enumerator stops and joins background workers without cancelling the caller-owned token.<br/>
        /// Disposal waits for in-progress synchronous hydration callbacks or storage reads before releasing worker resources.<br/>
        /// </remarks>
        public IEnumerable<(ulong ID, T Record)> IterateRecordsFastWithIds(int? maxDegreeOfParallelism = null, int? channelCapacity = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var degree = maxDegreeOfParallelism
                ?? rbase.config?.FractalStoreConfig.GetManyParallelThreadCount
                ?? Environment.ProcessorCount;

            if (degree < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Degree must be at least 1.");

            var capacity = channelCapacity ?? Math.Max(degree * 4, 32);
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(channelCapacity), "Channel capacity must be at least 1.");

            var channel = Channel.CreateBounded<(ulong ID, T Record)>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var producerToken = producerCancellation.Token;
            var options = new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = producerToken };

            var producer = Task.Run(async () =>
            {
                try
                {
                    var ids = IterateRecordIDs();
                    await Parallel.ForEachAsync(ids, options, async (recordId, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var record = rbase.data.GetOne(recordId);
                        if (record.IsEmpty)
                            throw new KeyNotFoundException($"No record found with recordID of '{recordId}'");

                        using (record)
                        {
                            var entity = MaterializeFractalRecordFast<T>(recordId, record, nameof(IterateRecordsFastWithIds));
                            await channel.Writer.WriteAsync((recordId, entity), ct).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, producerToken);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool canRead = channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
                    if (!canRead)
                        break;

                    while (channel.Reader.TryRead(out var item))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return item;
                    }
                }
            }
            finally
            {
                // Stop blocked writes before joining; borrowed records and lifecycle scopes must outlive every worker.
                if (!producer.IsCompleted)
                    producerCancellation.Cancel();

                try
                {
                    producer.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (producerToken.IsCancellationRequested)
                {
                    // A producer cancelled before Task.Run starts has no channel error to observe.
                }
            }
        }

        /// <summary>
        /// Hydrates this store's records into the requested CLR shape using parallel workers and a bounded result channel.<br/>
        /// Results are emitted by worker completion and have no stable ordering guarantee.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection shape.<br/></typeparam>
        /// <param name="maxDegreeOfParallelism">Optional maximum worker concurrency; null uses GetManyParallelThreadCount or the processor count.<br/></param>
        /// <param name="channelCapacity">Optional positive bound on buffered completed results; null uses the operation's configured or calculated default.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, worker traversal, evaluation, and result consumption.<br/></param>
        /// <returns>An unordered sequence of hydrated projections.<br/></returns>
        /// <remarks>
        /// The consumer waits synchronously for channel results; this is an IEnumerable API, not asynchronous enumeration.<br/>
        /// Disposing the enumerator stops and joins its background workers without cancelling the caller-owned token.<br/>
        /// Disposal waits for any in-progress synchronous hydration callback or storage read to return before releasing worker resources.<br/>
        /// </remarks>
        public IEnumerable<TOut> IterateRecordsFastAs<TOut>(int? maxDegreeOfParallelism = null, int? channelCapacity = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var degree = maxDegreeOfParallelism
                ?? rbase.config?.FractalStoreConfig.GetManyParallelThreadCount
                ?? Environment.ProcessorCount;

            if (degree < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Degree must be at least 1.");

            var capacity = channelCapacity ?? Math.Max(degree * 4, 32);
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(channelCapacity), "Channel capacity must be at least 1.");

            var channel = Channel.CreateBounded<TOut>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var producerToken = producerCancellation.Token;
            var options = new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = producerToken };

            var producer = Task.Run(async () =>
            {
                try
                {
                    var ids = IterateRecordIDs();
                    await Parallel.ForEachAsync(ids, options, async (recordId, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var record = rbase.data.GetOne(recordId);
                        if (record.IsEmpty)
                            throw new KeyNotFoundException($"No record found with recordID of '{recordId}'");

                        using (record)
                        {
                            var entity = MaterializeFractalRecordFast<TOut>(recordId, record, nameof(IterateRecordsFastAs));
                            await channel.Writer.WriteAsync(entity, ct).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, producerToken);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool canRead = channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
                    if (!canRead)
                        break;

                    while (channel.Reader.TryRead(out var item))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return item;
                    }
                }
            }
            finally
            {
                // Stop blocked writes before joining; borrowed records and lifecycle scopes must outlive every worker.
                if (!producer.IsCompleted)
                    producerCancellation.Cancel();

                try
                {
                    producer.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (producerToken.IsCancellationRequested)
                {
                    // A producer cancelled before Task.Run starts has no channel error to observe.
                }
            }
        }

        /// <summary>Lazily materializes projected records in parallel, yielding tuples of record IDs and projections as they are ready.<br/></summary>
        /// <param name="maxDegreeOfParallelism">Optional override for worker count; defaults to the configured GetManyParallelThreadCount or processor count.<br/></param>
        /// <param name="channelCapacity">Optional override for the bounded channel capacity; defaults to 4x the degree or 32.<br/></param>
        /// <param name="cancellationToken">Token to cancel enumeration and stop background workers.<br/></param>
        /// <returns>An unordered sequence of <c>(ID, Projection)</c> tuples for materialized records.<br/></returns>
        /// <remarks>
        /// Disposing the enumerator stops and joins background workers without cancelling the caller-owned token.<br/>
        /// Disposal waits for in-progress synchronous hydration callbacks or storage reads before releasing worker resources.<br/>
        /// </remarks>
        public IEnumerable<(ulong ID, TOut Record)> IterateRecordsFastWithIdsAs<TOut>(int? maxDegreeOfParallelism = null, int? channelCapacity = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var degree = maxDegreeOfParallelism
                ?? rbase.config?.FractalStoreConfig.GetManyParallelThreadCount
                ?? Environment.ProcessorCount;

            if (degree < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Degree must be at least 1.");

            var capacity = channelCapacity ?? Math.Max(degree * 4, 32);
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(channelCapacity), "Channel capacity must be at least 1.");

            var channel = Channel.CreateBounded<(ulong ID, TOut Record)>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var producerToken = producerCancellation.Token;
            var options = new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = producerToken };

            var producer = Task.Run(async () =>
            {
                try
                {
                    var ids = IterateRecordIDs();
                    await Parallel.ForEachAsync(ids, options, async (recordId, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var record = rbase.data.GetOne(recordId);
                        if (record.IsEmpty)
                            throw new KeyNotFoundException($"No record found with recordID of '{recordId}'");

                        using (record)
                        {
                            var entity = MaterializeFractalRecordFast<TOut>(recordId, record, nameof(IterateRecordsFastWithIdsAs));
                            await channel.Writer.WriteAsync((recordId, entity), ct).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, producerToken);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool canRead = channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
                    if (!canRead)
                        break;

                    while (channel.Reader.TryRead(out var item))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return item;
                    }
                }
            }
            finally
            {
                // Stop blocked writes before joining; borrowed records and lifecycle scopes must outlive every worker.
                if (!producer.IsCompleted)
                    producerCancellation.Cancel();

                try
                {
                    producer.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (producerToken.IsCancellationRequested)
                {
                    // A producer cancelled before Task.Run starts has no channel error to observe.
                }
            }
        }

        /// <summary>
        /// Persists <paramref name="record"/> using a newly allocated record identifier.
        /// </summary>
        /// <param name="record">The record instance to persist.</param>
        /// <param name="state">Optional developer-owned State value stored in the reserved persistent LibraDex State index.</param>
        /// <returns>The generated record identifier.</returns>
        public ulong Put(T record, long? state = null)
        {
            return PersistNewRecord(record, state, nameof(Put));
        }

        /// <summary>
        /// Persists <paramref name="record"/> using the supplied <paramref name="recordID"/>.
        /// Existing payloads with the same identifier are replaced.
        /// </summary>
        /// <param name="recordID">Identifier that must have been generated by this store.</param>
        /// <param name="record">The record instance to persist.</param>
        /// <param name="state">Optional developer-owned State value stored in the reserved persistent LibraDex State index.</param>
        /// <returns>The persisted record identifier.</returns>
        public ulong Put(ulong recordID, T record, long? state = null)
        {
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            var exists = rbase.data.RecordExists(recordID);
            if (!exists)
                throw new InvalidOperationException("recordID does not exist in this store; cannot overwrite.");

            return PersistRecord(recordID, record, state, nameof(Put), isNewRecord: false);
        }

        /// <summary>
        /// Persists the supplied records with freshly allocated record identifiers and updates every registered ordered index.<br/>
        /// </summary>
        /// <param name="records">The records to persist.<br/></param>
        /// <param name="state">Optional developer-owned State value stored in the reserved persistent LibraDex State index.<br/></param>
        /// <returns>The generated record identifiers in write order.<br/></returns>
        public IReadOnlyList<ulong> PutMany(IEnumerable<T> records, long? state = null)
        {
            ArgumentNullException.ThrowIfNull(records);

            var ids = records.TryGetNonEnumeratedCount(out var count)
                ? new List<ulong>(count)
                : new List<ulong>();

            foreach (var record in records)
            {
                if (record is null)
                    throw new ArgumentNullException(nameof(records), "Records collection contains a null entry.");

                var recordID = PersistNewRecord(record, state, nameof(PutMany));
                ids.Add(recordID);
            }

            return ids;
        }

        /// <summary>
        /// </summary>
        /// <param name="records">The records to persist.</param>
        /// <param name="state">Optional developer-owned State value stored in the reserved persistent LibraDex State index.</param>
        /// <returns>A handle containing the generated identifiers and the indexing batch used to monitor progress.</returns>
        /// <remarks>
        /// The caller owns the returned <see cref="IndexingBatchHandle"/> and should dispose it when observation is complete.
        /// </remarks>
        public PutManyFastResult PutManyFast(IEnumerable<T> records, long? state = null)
        {
            ArgumentNullException.ThrowIfNull(records);

            if (records is IReadOnlyList<T> readOnlyList)
            {
                if (readOnlyList.Count == 0)
                    return CreateEmptyPutManyFastResult();

                return PutManyFastFromList(readOnlyList, readOnlyList.Count, state);
            }

            if (records is IList<T> list)
            {
                if (list.Count == 0)
                    return CreateEmptyPutManyFastResult();

                if (list is IReadOnlyList<T> listAsReadOnly)
                    return PutManyFastFromList(listAsReadOnly, list.Count, state);

                return PutManyFastFromMutableList(list, list.Count, state);
            }

            var materialized = records.ToList();
            if (materialized.Count == 0)
                return CreateEmptyPutManyFastResult();

            return PutManyFastFromList(materialized, materialized.Count, state);
        }

        /// <summary>
        /// </summary>
        /// <param name="records">The source sequence that provides the records to persist.</param>
        /// <param name="recordCount">Exact number of elements to process from the sequence.</param>
        /// <param name="state">Optional developer-owned State value stored in the reserved persistent LibraDex State index.</param>
        /// <returns>A handle containing the generated identifiers and the indexing batch used to monitor progress.</returns>
        /// <remarks>
        /// When <paramref name="records"/> does not expose random access, the sequence is streamed without materializing the entire collection in memory.
        /// </remarks>
        public PutManyFastResult PutManyFast(IEnumerable<T> records, int recordCount, long? state = null)
        {
            ArgumentNullException.ThrowIfNull(records);
            if (recordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count must be non-negative.");

            if (recordCount == 0)
                return CreateEmptyPutManyFastResult();

            if (records is IReadOnlyList<T> readOnlyList)
            {
                if (recordCount > readOnlyList.Count)
                    throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count exceeds the number of available records.");

                return PutManyFastFromList(readOnlyList, recordCount, state);
            }

            if (records is IList<T> list)
            {
                if (recordCount > list.Count)
                    throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count exceeds the number of available records.");

                if (list is IReadOnlyList<T> listAsReadOnly)
                    return PutManyFastFromList(listAsReadOnly, recordCount, state);

                return PutManyFastFromMutableList(list, recordCount, state);
            }

            return PutManyFastStreaming(records, recordCount, state);
        }

        /// <summary>
        /// Persists all supplied records through the automatic fast route while applying call-scoped worker and progress controls.<br/>
        /// Existing overloads remain the zero-ceremony default; this overload is intended for bounded execution, diagnostics, and controlled ST/MT comparisons.<br/>
        /// </summary>
        /// <param name="records">Records to persist with freshly generated monotonic store identities.<br/></param>
        /// <param name="options">Call-scoped worker maximum and optional bounded progress observer.<br/></param>
        /// <param name="state">Optional developer-owned State value; non-null State may require the per-record compatibility route.<br/></param>
        /// <returns>Generated identities in source order and the operation's completed indexing handle.<br/></returns>
        public PutManyFastResult PutManyFast(
            IEnumerable<T> records,
            PutManyFastOptions options,
            long? state = null)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(options);
            _ = ResolvePutManyFastWorkerCount(options);

            if (records is IReadOnlyList<T> readOnlyList)
                return PutManyFastFromList(readOnlyList, readOnlyList.Count, state, options);

            if (records is IList<T> list)
            {
                if (list is IReadOnlyList<T> listAsReadOnly)
                    return PutManyFastFromList(listAsReadOnly, list.Count, state, options);
                return PutManyFastFromMutableList(list, list.Count, state, options);
            }

            var materialized = records.ToList();
            return PutManyFastFromList(materialized, materialized.Count, state, options);
        }

        /// <summary>
        /// Persists an exact prefix of the supplied sequence through the automatic fast route while applying call-scoped worker and progress controls.<br/>
        /// Random-access sources retain the coordinated bulk path; non-random sequences stream through the correctness-preserving compatibility path.<br/>
        /// </summary>
        /// <param name="records">Source sequence providing the records to persist.<br/></param>
        /// <param name="recordCount">Exact number of source elements to consume.<br/></param>
        /// <param name="options">Call-scoped worker maximum and optional bounded progress observer.<br/></param>
        /// <param name="state">Optional developer-owned State value; non-null State may require the per-record compatibility route.<br/></param>
        /// <returns>Generated identities in source order and the operation's completed indexing handle.<br/></returns>
        public PutManyFastResult PutManyFast(
            IEnumerable<T> records,
            int recordCount,
            PutManyFastOptions options,
            long? state = null)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(options);
            if (recordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count must be non-negative.");
            _ = ResolvePutManyFastWorkerCount(options);

            if (records is IReadOnlyList<T> readOnlyList)
            {
                if (recordCount > readOnlyList.Count)
                    throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count exceeds the number of available records.");
                return PutManyFastFromList(readOnlyList, recordCount, state, options);
            }

            if (records is IList<T> list)
            {
                if (recordCount > list.Count)
                    throw new ArgumentOutOfRangeException(nameof(recordCount), "Record count exceeds the number of available records.");
                if (list is IReadOnlyList<T> listAsReadOnly)
                    return PutManyFastFromList(listAsReadOnly, recordCount, state, options);
                return PutManyFastFromMutableList(list, recordCount, state, options);
            }

            return PutManyFastStreaming(records, recordCount, state, options);
        }

        /// <summary>
        /// Returns the number of distinct values for a simple property path using a reusable ordered index.<br/>
        /// If no matching index exists and auto-indexing allows distinct helpers, Abraxas creates a volatile helper index and reuses it for later calls.<br/>
        /// </summary>
        /// <param name="propPath">Simple property path to evaluate.<br/></param>
        /// <param name="where">Optional LibraDex-translatable filter predicate.<br/></param>
        /// <param name="useMemoryIndex">Retained compatibility parameter; this implementation selects available maintained or permitted auto-created indexes independently of this value.<br/></param>
        /// <returns>Distinct value count for matching records.<br/></returns>
        public ulong DistinctCount(string propPath, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            if (!TryGetOrCreateAutoLibraDexIndex(propPath, AutoIndexUse.Distinct, out var index))
                throw new NotSupportedException($"DistinctCount requires an ordered index or auto-index support for simple property path '{propPath}'.");

            return where is null
                ? CountDistinct(index)
                : CountDistinctWhere(index, where);
        }

        /// <summary>
        /// Returns the number of distinct values for an expression-resolved simple property path using a reusable ordered index.<br/>
        /// If no matching index exists and auto-indexing allows distinct helpers, Abraxas creates a volatile helper index and reuses it for later calls.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the target simple property path.<br/></param>
        /// <param name="where">Optional LibraDex-translatable filter predicate.<br/></param>
        /// <param name="useMemoryIndex">Retained compatibility parameter; this implementation selects available maintained or permitted auto-created indexes independently of this value.<br/></param>
        /// <returns>Distinct value count for matching records.<br/></returns>
        public ulong DistinctCount(Expression<Func<T, object>> propPath, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return DistinctCount(PropPath.FromExpression(propPath).Path, where, useMemoryIndex);
        }

        /// <summary>
        /// Counts distinct non-null values from a direct or backend-neutral computed selector.<br/>
        /// Direct selectors retain their ordered-index route; computed selectors use one authoritative Fractal/Inheto pass.<br/>
        /// </summary>
        /// <param name="selector">Selector describing the aggregate expression.<br/></param>
        /// <param name="where">Optional filter predicate.<br/></param>
        /// <param name="useMemoryIndex">Retained compatibility parameter; this implementation selects available maintained or permitted auto-created indexes independently of this value.<br/></param>
        /// <returns>The number of distinct present, non-null values among matching records.<br/></returns>
        public ulong DistinctCount(ISelector selector, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (TryResolveLegacySimpleSelectorPath(selector, out var path))
                return DistinctCount(path, where, useMemoryIndex);
            _ = useMemoryIndex;
            using var lifecycle = EnterStoreOperationScope();

            if (!TryCreateStoreSelectorReader(selector, out var readValue))
            {
                throw new NotSupportedException(
                    "DistinctCount(ISelector, ...) requires a direct property or a computed selector with complete backend-neutral semantics.");
            }

            var distinct = new HashSet<object?>(DirectGroupingScalarEqualityComparer.Instance);
            foreach (Inheto.InhetoBinary payload in IterateAuthoritativeGroupingPayloads(where, nameof(DistinctCount)))
            {
                var candidate = readValue(payload);
                if (candidate.HasValue && candidate.Value is not null)
                    distinct.Add(candidate.Value);
            }
            return checked((ulong)distinct.Count);
        }

        /// <summary>
        /// Returns distinct values for a simple property path using a reusable ordered index.<br/>
        /// If no matching index exists and auto-indexing allows distinct helpers, Abraxas creates a volatile helper index and reuses it for later calls.<br/>
        /// </summary>
        /// <param name="propPath">Simple property path to project distinct values from.<br/></param>
        /// <param name="limit">Optional maximum number of values to return.<br/></param>
        /// <param name="where">Optional LibraDex-translatable filter applied before distinct projection.<br/></param>
        /// <param name="useMemoryIndex">Retained compatibility parameter; this implementation selects available maintained or permitted auto-created indexes independently of this value.<br/></param>
        /// <returns>Read-only list of distinct values in ordered-index key order.<br/></returns>
        public IReadOnlyList<object?> DistinctValues(string propPath, int? limit = null, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            if (limit.HasValue && limit.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");

            if (!TryGetOrCreateAutoLibraDexIndex(propPath, AutoIndexUse.Distinct, out var index) ||
                !TryGetDistinctValuesByLibraDex(index, where, limit, out var values))
            {
                throw new NotSupportedException($"DistinctValues requires an ordered index or auto-index support for simple property path '{propPath}'.");
            }

            return values;
        }

        /// <summary>
        /// Returns distinct values for an expression-resolved simple property path using a reusable ordered index.<br/>
        /// If no matching index exists and auto-indexing allows distinct helpers, Abraxas creates a volatile helper index and reuses it for later calls.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the target simple property path.<br/></param>
        /// <param name="limit">Optional maximum number of values to return.<br/></param>
        /// <param name="where">Optional LibraDex-translatable filter applied before distinct projection.<br/></param>
        /// <param name="useMemoryIndex">Retained compatibility parameter; this implementation selects available maintained or permitted auto-created indexes independently of this value.<br/></param>
        /// <returns>Read-only list of distinct values in ordered-index key order.<br/></returns>
        public IReadOnlyList<object?> DistinctValues(Expression<Func<T, object>> propPath, int? limit = null, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return DistinctValues(PropPath.FromExpression(propPath).Path, limit, where, useMemoryIndex);
        }

        /// <summary>
        /// Returns distinct values from a direct or backend-neutral computed selector.<br/>
        /// Computed values are returned without an ordering guarantee; missing, null, and invalid computations share one distinct null bucket.<br/>
        /// </summary>
        /// <param name="selector">Selector expression used for distinct projection.<br/></param>
        /// <param name="limit">Optional maximum number of values to return.<br/></param>
        /// <param name="where">Optional filter applied before distinct projection.<br/></param>
        /// <param name="useMemoryIndex">Retained compatibility parameter; this implementation selects available maintained or permitted auto-created indexes independently of this value.<br/></param>
        /// <returns>Distinct matching values up to the optional limit; computed selectors do not guarantee ordering.<br/></returns>
        public IReadOnlyList<object?> DistinctValues(ISelector selector, int? limit = null, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (limit.HasValue && limit.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
            if (TryResolveLegacySimpleSelectorPath(selector, out var path))
                return DistinctValues(path, limit, where, useMemoryIndex);
            _ = useMemoryIndex;
            using var lifecycle = EnterStoreOperationScope();

            if (!TryCreateStoreSelectorReader(selector, out var readValue))
            {
                throw new NotSupportedException(
                    "DistinctValues(ISelector, ...) requires a direct property or a computed selector with complete backend-neutral semantics.");
            }

            var seen = new HashSet<object?>(DirectGroupingScalarEqualityComparer.Instance);
            var values = new List<object?>();
            foreach (Inheto.InhetoBinary payload in IterateAuthoritativeGroupingPayloads(where, nameof(DistinctValues)))
            {
                var candidate = readValue(payload);
                object? value = candidate.HasValue ? candidate.Value : null;
                if (!seen.Add(value))
                    continue;
                values.Add(value);
                if (limit.HasValue && values.Count == limit.Value)
                    break;
            }
            return values.AsReadOnly();
        }

        /// <summary>
        /// Returns typed distinct values for an expression-based property path without applying engine-side ordering.<br/>
        /// </summary>
        /// <typeparam name="TValue">Requested value type for each distinct element.<br/></typeparam>
        /// <param name="propPath">Expression resolving to the target property path.<br/></param>
        /// <param name="limit">Optional maximum number of values to return.<br/></param>
        /// <param name="where">Optional filter applied before distinct projection.<br/></param>
        /// <param name="useMemoryIndex">When true, uses memory indexing metadata for path/index resolution.<br/></param>
        /// <returns>Read-only list of typed distinct values.<br/></returns>
        public IReadOnlyList<TValue?> DistinctValues<TValue>(Expression<Func<T, object>> propPath, int? limit = null, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            var raw = DistinctValues(propPath, limit, where, useMemoryIndex);
            var values = new List<TValue?>(raw.Count);
            for (var i = 0; i < raw.Count; i++)
            {
                var value = raw[i];
                if (value is null)
                {
                    values.Add(default);
                    continue;
                }

                if (value is TValue typed)
                {
                    values.Add(typed);
                    continue;
                }

                values.Add((TValue?)Convert.ChangeType(value, typeof(TValue), CultureInfo.InvariantCulture));
            }

            return values.AsReadOnly();
        }

        /// <summary>
        /// Returns the number of records whose ordered State index entry matches the supplied value/mask rule.<br/>
        /// This helper keeps <c>State</c> semantics unopinionated and can be used for lifecycle/TTL-style policies when desired.<br/>
        /// </summary>
        /// <param name="state">State value to compare against.</param>
        /// <param name="mask">Optional mask. When provided, records satisfy <c>(State &amp; mask) == state</c>.</param>
        /// <returns>Record count matching the supplied state rule.</returns>
        public ulong CountByState(long state, long? mask = null)
        {
            if (TryCountByLibraDexState(state, mask, out var count))
                return count;

            throw new InvalidOperationException(
                $"Operation '{nameof(CountByState)}' requires this store's reserved persistent LibraDex State index.");
        }

        /// <summary>
        /// Retains the historical State-count signature while directing callers to persistent-only State selection.<br/>
        /// </summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Record count matching the supplied State rule.<br/></returns>
        [Obsolete("State is always persistent. Use CountByState(state, mask) instead.")]
        public ulong CountByState(long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return CountByState(state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State-count calls that omitted a mask.<br/></summary>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Record count matching the supplied State value.<br/></returns>
        [Obsolete("State is always persistent. Use CountByState(state) instead.")]
        public ulong CountByState(long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return CountByState(state);
        }

        /// <summary>
        /// Returns a histogram of <c>State</c> values to record counts.<br/>
        /// This provides a lightweight, engine-level summary for developer-defined state conventions.<br/>
        /// </summary>
        /// <returns>Read-only dictionary keyed by state value with per-state counts.</returns>
        public IReadOnlyDictionary<long, ulong> GetStateHistogram()
        {
            if (TryGetLibraDexStateHistogram(out var libraDexHistogram))
                return libraDexHistogram;

            throw new InvalidOperationException(
                $"Operation '{nameof(GetStateHistogram)}' requires this store's reserved persistent LibraDex State index.");
        }

        /// <summary>
        /// Retains the historical State-histogram signature while directing callers to persistent-only State diagnostics.<br/>
        /// </summary>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>Read-only dictionary keyed by State value with per-State counts.<br/></returns>
        [Obsolete("State is always persistent. Use GetStateHistogram() instead.")]
        public IReadOnlyDictionary<long, ulong> GetStateHistogram(bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return GetStateHistogram();
        }

        /// <summary>
        /// Repairs the authoritative persistent State index by removing tuples whose record identity is absent from Fractal or belongs to another store.<br/>
        /// Missing State values are valid and are never synthesized; LibraDex is intentionally the only authoritative copy of assigned developer State.<br/>
        /// The operation does not hydrate record payloads and returns the exact number of orphan tuples removed.<br/>
        /// </summary>
        /// <returns>The number of orphan State tuples removed.<br/></returns>
        public ulong RepairStateIndex()
            => RepairLibraDexStateIndex();

        /// <summary>
        /// Simple single-key counts reuse complete LibraDex grouping metadata when available; other supported shapes scan authoritative Fractal/Inheto payloads once and reduce compact in-memory group state.<br/>
        /// Direct selectors use eligible LibraDex indexes; durably described computed group keys and aggregate selectors use one authoritative Fractal/Inheto pass.<br/>
        /// Runtime-only selectors remain outside this contract because they do not provide reconstructable group identity.<br/>
        /// </summary>
        /// <param name="aggregate">Aggregate operator to compute per group.</param>
        /// <param name="aggregateSelector">Selector expression used by non-count aggregates.</param>
        /// <param name="where">Optional row filter applied before grouping.</param>
        /// <param name="name">Optional label attached to the resulting <see cref="GroupedRecords"/> container.</param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection follows LibraDex registration and authoritative-payload fallback.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Materialized grouped aggregate rows and their optional source condition.</returns>
        public GroupedRecords GetAggregateGroupings(
            AggregateFunction aggregate,
            ISelector? aggregateSelector,
            Condition<T>.EndCondition? where = null,
            string? name = null,
            bool useMemoryIndex = false,
            params ISelector[] groupKeys)
        {
            _ = useMemoryIndex;
            ArgumentNullException.ThrowIfNull(groupKeys);
            if (groupKeys.Length == 0)
                throw new ArgumentException("At least one grouping selector must be specified.", nameof(groupKeys));
            if (!Enum.IsDefined(aggregate))
                throw new ArgumentOutOfRangeException(nameof(aggregate), aggregate, "Unknown grouped aggregate function.");
            if (aggregate == AggregateFunction.Count && where is null)
            {
                var countGroups = IterateGroupings(groupKeys).ToList();
                for (var i = 0; i < countGroups.Count; i++)
                    countGroups[i].Name = name ?? string.Empty;
                return new GroupedRecords(countGroups, where: null, name);
            }

            string? aggregatePath = null;
            if (aggregate != AggregateFunction.Count)
            {
                ArgumentNullException.ThrowIfNull(aggregateSelector);
                _ = TryResolveLegacySimpleSelectorPath(aggregateSelector, out aggregatePath);
            }

            if (aggregatePath is not null && TryMaterializeAggregateGroupingsByLibraDex(
                aggregate,
                aggregatePath,
                where,
                name,
                groupKeys,
                out GroupedRecords indexedGroups))
            {
                return indexedGroups;
            }

            return MaterializeSemanticAggregateGroupings(aggregate, aggregateSelector, where, name, groupKeys);
        }

        /// <summary>
        /// The aggregate and grouping selectors are resolved as simple property paths and use the backend-neutral grouped reducer.<br/>
        /// </summary>
        /// <param name="aggregatePropPath">Property path used by the MIN aggregate.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.Min"/> populated.</returns>
        public GroupedRecords GetMinGroupings(string aggregatePropPath, params ISelector[] groupKeys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(aggregatePropPath);
            return GetAggregateGroupings(AggregateFunction.Min, Calc.PropPath(aggregatePropPath), groupKeys: groupKeys);
        }

        /// <summary>
        /// </summary>
        /// <param name="aggregatePropPath">Expression resolving to the MIN aggregate property path.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.Min"/> populated.</returns>
        public GroupedRecords GetMinGroupings(Expression<Func<T, object>> aggregatePropPath, params ISelector[] groupKeys)
        {
            ArgumentNullException.ThrowIfNull(aggregatePropPath);
            return GetMinGroupings(aggregatePropPath.GetPropertyPath(allowExtensionMethods: false), groupKeys);
        }

        /// <summary>
        /// </summary>
        /// <param name="aggregateSelector">Selector used by the MIN aggregate.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.Min"/> populated.</returns>
        public GroupedRecords GetMinGroupings(ISelector aggregateSelector, params ISelector[] groupKeys)
            => GetAggregateGroupings(AggregateFunction.Min, aggregateSelector, groupKeys: groupKeys);

        /// <summary>
        /// The aggregate and grouping selectors are resolved as simple property paths and use the backend-neutral grouped reducer.<br/>
        /// </summary>
        /// <param name="aggregatePropPath">Property path used by the MAX aggregate.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.Max"/> populated.</returns>
        public GroupedRecords GetMaxGroupings(string aggregatePropPath, params ISelector[] groupKeys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(aggregatePropPath);
            return GetAggregateGroupings(AggregateFunction.Max, Calc.PropPath(aggregatePropPath), groupKeys: groupKeys);
        }

        /// <summary>
        /// </summary>
        /// <param name="aggregatePropPath">Expression resolving to the MAX aggregate property path.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.Max"/> populated.</returns>
        public GroupedRecords GetMaxGroupings(Expression<Func<T, object>> aggregatePropPath, params ISelector[] groupKeys)
        {
            ArgumentNullException.ThrowIfNull(aggregatePropPath);
            return GetMaxGroupings(aggregatePropPath.GetPropertyPath(allowExtensionMethods: false), groupKeys);
        }

        /// <summary>
        /// </summary>
        /// <param name="aggregateSelector">Selector used by the MAX aggregate.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.Max"/> populated.</returns>
        public GroupedRecords GetMaxGroupings(ISelector aggregateSelector, params ISelector[] groupKeys)
            => GetAggregateGroupings(AggregateFunction.Max, aggregateSelector, groupKeys: groupKeys);

        /// <summary>
        /// Null or missing aggregate values are ignored, matching <c>COUNT(DISTINCT ...)</c> behavior.<br/>
        /// </summary>
        /// <param name="aggregatePropPath">Property path used by the distinct count aggregate.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.DistinctCount"/> populated.</returns>
        public GroupedRecords GetDistinctCountGroupings(string aggregatePropPath, params ISelector[] groupKeys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(aggregatePropPath);
            return GetAggregateGroupings(AggregateFunction.DistinctCount, Calc.PropPath(aggregatePropPath), groupKeys: groupKeys);
        }

        /// <summary>
        /// </summary>
        /// <param name="aggregatePropPath">Expression resolving to the distinct count aggregate property path.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.DistinctCount"/> populated.</returns>
        public GroupedRecords GetDistinctCountGroupings(Expression<Func<T, object>> aggregatePropPath, params ISelector[] groupKeys)
        {
            ArgumentNullException.ThrowIfNull(aggregatePropPath);
            return GetDistinctCountGroupings(aggregatePropPath.GetPropertyPath(allowExtensionMethods: false), groupKeys);
        }

        /// <summary>
        /// </summary>
        /// <param name="aggregateSelector">Selector used by the distinct count aggregate.</param>
        /// <param name="groupKeys">One or more grouping selectors.</param>
        /// <returns>Grouped records with <see cref="GroupInfo.DistinctCount"/> populated.</returns>
        public GroupedRecords GetDistinctCountGroupings(ISelector aggregateSelector, params ISelector[] groupKeys)
            => GetAggregateGroupings(AggregateFunction.DistinctCount, aggregateSelector, groupKeys: groupKeys);

        /// <summary>
        /// Returns the minimum value for the provided property path among rows matching the supplied state predicate.<br/>
        /// This is useful for app-level lifecycle policies where state tracks processing or retention phases.<br/>
        /// </summary>
        /// <param name="propPath">Property path to evaluate.</param>
        /// <param name="state">State value to compare against.</param>
        /// <param name="mask">Optional mask. When provided, rows satisfy <c>(State &amp; mask) == state</c>.</param>
        /// <returns>The minimum non-null value, or <c>null</c> when no records match.<br/></returns>
        public object? Min(string propPath, long state, long? mask = null)
            => ExecuteAggregateByState(propPath, state, mask, StateAggregateKind.Minimum);

        /// <summary>Retains the historical State-filtered minimum signature while State remains persistent-only.<br/></summary>
        /// <param name="propPath">Property path to evaluate.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The minimum non-null value, or null when no records match.<br/></returns>
        [Obsolete("State is always persistent. Use Min(propPath, state, mask) instead.")]
        public object? Min(string propPath, long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Min(propPath, state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State minimum calls that omitted a mask.<br/></summary>
        /// <param name="propPath">Property path to evaluate.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The minimum non-null value, or null when no records match.<br/></returns>
        [Obsolete("State is always persistent. Use Min(propPath, state) instead.")]
        public object? Min(string propPath, long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Min(propPath, state);
        }

        /// <summary>
        /// Returns the maximum value for the provided property path among rows matching the supplied state predicate.<br/>
        /// This is useful for app-level lifecycle policies where state tracks processing or retention phases.<br/>
        /// </summary>
        /// <param name="propPath">Property path to evaluate.</param>
        /// <param name="state">State value to compare against.</param>
        /// <param name="mask">Optional mask. When provided, rows satisfy <c>(State &amp; mask) == state</c>.</param>
        /// <returns>The maximum non-null value, or <c>null</c> when no records match.<br/></returns>
        public object? Max(string propPath, long state, long? mask = null)
            => ExecuteAggregateByState(propPath, state, mask, StateAggregateKind.Maximum);

        /// <summary>Retains the historical State-filtered maximum signature while State remains persistent-only.<br/></summary>
        /// <param name="propPath">Property path to evaluate.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The maximum non-null value, or null when no records match.<br/></returns>
        [Obsolete("State is always persistent. Use Max(propPath, state, mask) instead.")]
        public object? Max(string propPath, long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Max(propPath, state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State maximum calls that omitted a mask.<br/></summary>
        /// <param name="propPath">Property path to evaluate.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The maximum non-null value, or null when no records match.<br/></returns>
        [Obsolete("State is always persistent. Use Max(propPath, state) instead.")]
        public object? Max(string propPath, long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return Max(propPath, state);
        }

        /// <summary>
        /// Returns the distinct value count for the provided property path among rows matching the supplied state predicate.<br/>
        /// This is useful for app-level lifecycle policies where state tracks processing or retention phases.<br/>
        /// </summary>
        /// <param name="propPath">Property path to evaluate.</param>
        /// <param name="state">State value to compare against.</param>
        /// <param name="mask">Optional mask. When provided, rows satisfy <c>(State &amp; mask) == state</c>.</param>
        /// <returns>The number of distinct non-null values among matching records.<br/></returns>
        public ulong DistinctCount(string propPath, long state, long? mask = null)
            => (ulong)ExecuteAggregateByState(propPath, state, mask, StateAggregateKind.DistinctCount)!;

        /// <summary>Retains the historical State-filtered distinct-count signature while State remains persistent-only.<br/></summary>
        /// <param name="propPath">Property path to evaluate.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The number of distinct non-null values among matching records.<br/></returns>
        [Obsolete("State is always persistent. Use DistinctCount(propPath, state, mask) instead.")]
        public ulong DistinctCount(string propPath, long state, long? mask, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return DistinctCount(propPath, state, mask);
        }

        /// <summary>Retains historical named <c>useMemoryIndex</c> State distinct-count calls that omitted a mask.<br/></summary>
        /// <param name="propPath">Property path to evaluate.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns>The number of distinct non-null values among matching records.<br/></returns>
        [Obsolete("State is always persistent. Use DistinctCount(propPath, state) instead.")]
        public ulong DistinctCount(string propPath, long state, bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return DistinctCount(propPath, state);
        }








        //====== TYPES ======
        private sealed class ConditionParameterSubscriptionSentinel : IDisposable
        {
            //Shared/Static Members
            public static readonly ConditionParameterSubscriptionSentinel Instance = new();








            //======  CONSTRUCTORS  ======
            private ConditionParameterSubscriptionSentinel()
            {
            }








            //------ Public Methods -----
            //======  METHODS  ======
            public void Dispose()
            {
            }
        }

        private sealed class ScopedDelegateDisposable : IDisposable
        {
            //======  FIELDS  ======
            private readonly Action disposeAction;
            private bool disposed;








            //======  CONSTRUCTORS  ======
            public ScopedDelegateDisposable(Action disposeAction)
            {
                this.disposeAction = disposeAction ?? throw new ArgumentNullException(nameof(disposeAction));
            }








            //------ Public Methods -----
            //======  METHODS  ======
            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                disposeAction();
            }
        }








        /// <summary>
        /// Describes one backend-neutral sort key for record retrieval operations.<br/>
        /// </summary>
        public readonly record struct SortBy
        {
            /// <summary>
            /// Creates a backend-neutral sort descriptor for one property path.<br/>
            /// </summary>
            /// <param name="key">Normalized property path that provides the sort key.<br/></param>
            /// <param name="ignoreCase">When true, text fallback comparison uses ordinal case folding.<br/></param>
            /// <param name="descending">When true, this key is ordered descending.<br/></param>
            public SortBy(PropPath key, bool ignoreCase = false, bool descending = false)
            {
                if (string.IsNullOrWhiteSpace(key.Path))
                    throw new ArgumentException("Sort property path cannot be empty.", nameof(key));

                Key = key;
                IgnoreCase = ignoreCase;
                Descending = descending;
            }

            /// <summary>
            /// Creates a backend-neutral sort descriptor for one property path and strongly typed per-key direction.<br/>
            /// </summary>
            /// <param name="key">Normalized property path that provides the sort key.<br/></param>
            /// <param name="direction">Direction applied to this ordering component.<br/></param>
            /// <param name="ignoreCase">When true, text fallback comparison uses ordinal case folding.<br/></param>
            public SortBy(PropPath key, SortDirection direction, bool ignoreCase = false)
                : this(key, ignoreCase, direction == SortDirection.Descending)
            {
            }

            /// <summary>
            /// Gets the backend-neutral property path that provides this sort key.<br/>
            /// </summary>
            public PropPath Key { get; }

            /// <summary>
            /// Gets whether text fallback comparison uses ordinal case folding.<br/>
            /// </summary>
            public bool IgnoreCase { get; }

            /// <summary>
            /// Gets whether this key is ordered descending.<br/>
            /// </summary>
            public bool Descending { get; }

            //======  METHODS  ======








            /// <summary>
            /// Creates a property-ordering component from a path or strongly typed ordering tuple.<br/>
            /// </summary>
            /// <param name="propPath">Property path or tuple containing a path and optional direction/case-folding preference.<br/></param>
            /// <returns>A property sort descriptor; omitted direction is ascending and omitted case folding is false.<br/></returns>
            /// <remarks>
            /// Case folding requests ordinal text comparison in the authoritative ordering fallback; this conversion does not create an index.<br/>
            /// </remarks>
            public static implicit operator SortBy(string propPath) => new(new PropPath(propPath));

            /// <summary>
            /// Creates a property-ordering component from a path or strongly typed ordering tuple.<br/>
            /// </summary>
            /// <param name="propPath">Property path or tuple containing a path and optional direction/case-folding preference.<br/></param>
            /// <returns>A property sort descriptor; omitted direction is ascending and omitted case folding is false.<br/></returns>
            /// <remarks>
            /// Case folding requests ordinal text comparison in the authoritative ordering fallback; this conversion does not create an index.<br/>
            /// </remarks>
            public static implicit operator SortBy(PropPath propPath) => new(propPath);

            /// <summary>
            /// Creates a property-ordering component from a path or strongly typed ordering tuple.<br/>
            /// </summary>
            /// <param name="value">Property path or tuple containing a path and optional direction/case-folding preference.<br/></param>
            /// <returns>A property sort descriptor; omitted direction is ascending and omitted case folding is false.<br/></returns>
            /// <remarks>
            /// Case folding requests ordinal text comparison in the authoritative ordering fallback; this conversion does not create an index.<br/>
            /// </remarks>
            public static implicit operator SortBy((string PropPath, SortDirection Direction) value)
                => new(new PropPath(value.PropPath), value.Direction);

            /// <summary>
            /// Creates a property-ordering component from a path or strongly typed ordering tuple.<br/>
            /// </summary>
            /// <param name="value">Property path or tuple containing a path and optional direction/case-folding preference.<br/></param>
            /// <returns>A property sort descriptor; omitted direction is ascending and omitted case folding is false.<br/></returns>
            /// <remarks>
            /// Case folding requests ordinal text comparison in the authoritative ordering fallback; this conversion does not create an index.<br/>
            /// </remarks>
            public static implicit operator SortBy((PropPath PropPath, SortDirection Direction) value)
                => new(value.PropPath, value.Direction);

            /// <summary>
            /// Creates a property-ordering component from a path or strongly typed ordering tuple.<br/>
            /// </summary>
            /// <param name="value">Property path or tuple containing a path and optional direction/case-folding preference.<br/></param>
            /// <returns>A property sort descriptor; omitted direction is ascending and omitted case folding is false.<br/></returns>
            /// <remarks>
            /// Case folding requests ordinal text comparison in the authoritative ordering fallback; this conversion does not create an index.<br/>
            /// </remarks>
            public static implicit operator SortBy((string PropPath, bool IgnoreCase) value)
                => new(new PropPath(value.PropPath), ignoreCase: value.IgnoreCase);

            /// <summary>
            /// Creates a property-ordering component from a path or strongly typed ordering tuple.<br/>
            /// </summary>
            /// <param name="value">Property path or tuple containing a path and optional direction/case-folding preference.<br/></param>
            /// <returns>A property sort descriptor; omitted direction is ascending and omitted case folding is false.<br/></returns>
            /// <remarks>
            /// Case folding requests ordinal text comparison in the authoritative ordering fallback; this conversion does not create an index.<br/>
            /// </remarks>
            public static implicit operator SortBy((string PropPath, SortDirection Direction, bool IgnoreCase) value)
                => new(new PropPath(value.PropPath), value.Direction, value.IgnoreCase);
        }

        /// <summary>
        /// Computes the approximate on-disk size for this store from the authoritative Fractal data/index files and persistent LibraDex catalog.<br/>
        /// Fractal and LibraDex files are shared by stores in the same RecordBase, so the result is a RecordBase-level approximation rather than per-store byte attribution.<br/>
        /// </summary>
        /// <param name="detailFlags">Controls which size components are included in the total.<br/></param>
        /// <returns>Total size in bytes for the requested components.<br/></returns>
        public ulong GetSize(RecordStoreSizeDetailFlags detailFlags = RecordStoreSizeDetailFlags.All)
        {
            using var operation = EnterStoreOperationScope();
            if (detailFlags == RecordStoreSizeDetailFlags.None)
                return 0;

            ulong total = 0;
            if (detailFlags.HasFlag(RecordStoreSizeDetailFlags.Records))
                total += rbase.GetFileSize(RecordBase.FileSizeDetailFlags.FractalData);
            if (detailFlags.HasFlag(RecordStoreSizeDetailFlags.Indexes))
                total += rbase.GetFileSize(RecordBase.FileSizeDetailFlags.FractalIndex | RecordBase.FileSizeDetailFlags.LibraDexCatalog);

            return total;
        }

        /// <summary>
        /// Lightweight health summary for a single record store.<br/>
        /// </summary>
        public readonly record struct HealthSnapshot(
            DateTime UtcTimestamp,
            string StoreName,
            byte StoreId,
            ulong RecordCount,
            int IndexCount,
            int UniqueIndexCount,
            bool MemoryIndexingEnabled,
            ulong StoreSizeBytes)
        {
            /// <summary>
            /// Gets whether the store's intrinsic persistent State index is bound and its persisted contract has been validated.<br/>
            /// </summary>
            public bool StateIndexReady { get; init; }

            /// <summary>
            /// Gets the number of identities currently carrying an assigned developer-owned State value.<br/>
            /// This count is separate from ordinary index inventory and does not expose the reserved physical index name.<br/>
            /// </summary>
            public ulong StateTupleCount { get; init; }
        }

        /// <summary>
        /// Returns a health snapshot for this record store, including counts, index metadata, size, and cache telemetry.<br/>
        /// </summary>
        /// <param name="useMemoryIndex">When <see langword="true"/>, health counts include only volatile indexes; otherwise persistent and volatile indexes are included.<br/></param>
        /// <returns>Current health summary snapshot.<br/></returns>
        public HealthSnapshot GetHealthSnapshot(bool useMemoryIndex = false)
        {
            var indexes = GetIndexes(useMemoryIndex).ToList();
            var uniqueIndexes = indexes.Count(i => i.IsUnique);
            bool stateIndexReady = TryGetLibraDexStateTupleCount(out ulong stateTupleCount);
            return new HealthSnapshot(
                UtcTimestamp: DateTime.UtcNow,
                StoreName: name,
                StoreId: storeID,
                RecordCount: Count,
                IndexCount: indexes.Count,
                UniqueIndexCount: uniqueIndexes,
                MemoryIndexingEnabled: MemoryIndexingExists,
                StoreSizeBytes: GetSize())
            {
                StateIndexReady = stateIndexReady,
                StateTupleCount = stateTupleCount
            };
        }

        /// <summary>
        /// Preserves special RecordBase records (IDs 1-10), resets the Fractal store, then reapplies them.<br/>
        /// This is only safe when the RecordBase contains a single store, since it wipes all Fractal data.<br/>
        /// </summary>
        private void PreserveSpecialRecordsAndResetFractalStore()
        {
            if (rbase?.data is null)
                return;

            var preserved = new List<(ulong id, byte[] data)>();
            for (ulong id = 1; id <= 10; id++)
            {
                if (!rbase.data.TryGetOne(id, out var rec))
                    continue;

                try
                {
                    preserved.Add((id, rec.Data.ToArray()));
                }
                finally
                {
                    rec.Dispose();
                }
            }

            rbase.data.DeleteAll();

            for (int i = 0; i < preserved.Count; i++)
            {
                var (id, data) = preserved[i];
                rbase.data.PutOne(id, data);
            }
        }

        /// <summary>
        /// Deletes one record from authoritative Fractal storage and removes its maintained LibraDex property and State tuples.<br/>
        /// Deletion events bracket the authoritative mutation using the original <c>Delete</c> operation name.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="zeroWrite">When <see langword="true"/>, requests zeroing of the deleted Fractal payload bytes.<br/></param>
        /// <returns><see langword="true"/> when the Fractal record existed and was deleted; otherwise <see langword="false"/>.<br/></returns>
        public bool Delete(ulong recordID, bool zeroWrite = false)
        {
            rbase.EnsureWriteAllowed(nameof(Delete));
            EnsureFunctionIndexProjectionsAvailable(nameof(Delete));
            using var partialIndexMutation = EnterPartialIndexMutationScope();
            EnsureLibraDexConvergence(nameof(Delete));
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.RecordExists(recordID))
                return false;

            OnRecordDeleting(recordID, zeroWrite, nameof(Delete));
            var priorBinary = GetInhetoBinary(recordID, nameof(Delete));
            storeRuntime.ClearAppliedIndexSourceUsn();
            if (!rbase.data.DeleteOne(recordID, zeroWrite))
                return false;

            try
            {
                TryApplyLibraDexDelete(recordID, priorBinary);
                TryApplyLibraDexStateDelete(recordID);
                FinalizeLibraDexPartialDelete();
                SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
            }
            catch (Exception indexFailure)
            {
                TryRepairLibraDexAfterFractalMutation(
                    nameof(Delete),
                    recordID,
                    indexFailure,
                    pendingStateIntentRetained: false);
            }

            OnRecordDeleted(recordID, zeroWrite, nameof(Delete));
            return true;
        }

        /// <summary>
        /// Deletes every record owned by this store from authoritative Fractal storage and clears all maintained LibraDex tuples.<br/>
        /// A single-store RecordBase uses the existing special-record-preserving Fractal reset; a multi-store RecordBase deletes only identities carrying this store's group key.<br/>
        /// </summary>
        /// <param name="compactAfter">When <see langword="true"/>, runs online Fractal defragmentation until idle after deletion.<br/></param>
        /// <param name="compactTimeout">Maximum time allowed for the optional online defragmentation pass; the default is five seconds.<br/></param>
        public void DeleteAll(bool compactAfter = false, TimeSpan? compactTimeout = null)
        {
            rbase.EnsureWriteAllowed(nameof(DeleteAll));
            EnsureFunctionIndexProjectionsAvailable(nameof(DeleteAll));
            var compactTimeoutValue = compactTimeout ?? TimeSpan.FromSeconds(5);
            var soleStorePhysicalReset = false;
            using (EnterPartialIndexMutationScope())
            {
                EnsureLibraDexConvergence(nameof(DeleteAll));
                var storeInfos = rbase.GetStoreInfos(includeRemoved: true);
                soleStorePhysicalReset = storeInfos is not null && storeInfos.Count == 1;
                if (Count != 0)
                {
                    storeRuntime.ClearAppliedIndexSourceUsn();
                    if (soleStorePhysicalReset)
                    {
                        PreserveSpecialRecordsAndResetFractalStore();
                    }
                    else
                    {
                        _ = rbase.data.DeleteByGroupKey(storeID);
                    }

                    try
                    {
                        TryApplyLibraDexDeleteAll(out _);
                        FinalizeLibraDexPartialDeleteAll();
                        SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
                    }
                    catch (Exception indexFailure)
                    {
                        TryRepairLibraDexAfterFractalMutation(
                            nameof(DeleteAll),
                            recordID: null,
                            indexFailure,
                            pendingStateIntentRetained: false);
                    }
                }
            }

            if (soleStorePhysicalReset)
                _ = rbase.Compact();
            else if (compactAfter)
                RunOnlineDefragUntilIdle(compactTimeoutValue);
        }

        /// <summary>
        /// Deletes the requested records from authoritative Fractal storage and removes their maintained LibraDex property and State tuples.<br/>
        /// Missing identities are ignored, duplicate identities can contribute at most one returned deletion, and input identities from another store are rejected.<br/>
        /// </summary>
        /// <param name="recordIDs">Record identities to delete; every supplied identity must belong to this store.<br/></param>
        /// <param name="zeroWrite">When <see langword="true"/>, requests zeroing of each deleted Fractal payload's bytes.<br/></param>
        /// <param name="compactAfter">When <see langword="true"/>, runs online Fractal defragmentation after at least one successful deletion.<br/></param>
        /// <param name="compactTimeout">Maximum time allowed for the optional online defragmentation pass; the default is five seconds.<br/></param>
        /// <returns>Successfully deleted record identities in first-success input order.<br/></returns>
        public IReadOnlyList<ulong> DeleteMany(IEnumerable<ulong> recordIDs, bool zeroWrite = false, bool compactAfter = false, TimeSpan? compactTimeout = null)
        {
            rbase.EnsureWriteAllowed(nameof(DeleteMany));
            EnsureFunctionIndexProjectionsAvailable(nameof(DeleteMany));
            using var partialIndexMutation = EnterPartialIndexMutationScope();
            EnsureLibraDexConvergence(nameof(DeleteMany));
            ArgumentNullException.ThrowIfNull(recordIDs);

            var compactTimeoutValue = compactTimeout ?? TimeSpan.FromSeconds(5);
            var deletedIds = new List<ulong>();
            foreach (var recordID in recordIDs)
            {
                EnsureRecordBelongsToCurrentStore(recordID, nameof(recordIDs));
                if (!rbase.data.RecordExists(recordID))
                    continue;

                OnRecordDeleting(recordID, zeroWrite, nameof(DeleteMany));
                var priorBinary = GetInhetoBinary(recordID, nameof(DeleteMany));
                storeRuntime.ClearAppliedIndexSourceUsn();
                if (!rbase.data.DeleteOne(recordID, zeroWrite))
                    continue;

                try
                {
                    TryApplyLibraDexDelete(recordID, priorBinary);
                    TryApplyLibraDexStateDelete(recordID);
                    FinalizeLibraDexPartialDelete();
                    SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
                }
                catch (Exception indexFailure)
                {
                    TryRepairLibraDexAfterFractalMutation(
                        nameof(DeleteMany),
                        recordID,
                        indexFailure,
                        pendingStateIntentRetained: false);
                }

                OnRecordDeleted(recordID, zeroWrite, nameof(DeleteMany));
                deletedIds.Add(recordID);
            }

            if (compactAfter && deletedIds.Count > 0)
                RunOnlineDefragUntilIdle(compactTimeoutValue);

            return deletedIds.AsReadOnly();
        }

        /// <summary>
        /// Existing or auto-created LibraDex indexes provide reusable ordering when every descriptor has one shared direction and no case folding is requested.<br/>
        /// Mixed-direction, case-folded, missing-key, unsupported, or disabled-auto-index shapes fall back to one authoritative Fractal/Inheto read per surviving record.<br/>
        /// Duplicate identities are returned once, identities missing from Fractal are ignored, and complete key ties use record identity for deterministic backend parity.<br/>
        /// </summary>
        /// <param name="recordIDs">Record identities to order; every supplied identity must belong to this store.<br/></param>
        /// <param name="sort">Property-path descriptors in lexicographic priority order.<br/></param>
        /// <param name="useMemoryIndex">Retained compatibility parameter; this implementation selects available maintained or permitted auto-created indexes independently of this value.<br/></param>
        /// <returns>A read-only list containing the surviving identities in requested order.<br/></returns>
        public IReadOnlyList<ulong> Sort(IEnumerable<ulong> recordIDs, bool useMemoryIndex = false, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(recordIDs);
            return OrderRecordIDsNatively(recordIDs, ignoreCase: false, sort);
        }

        /// <summary>
        /// Materializes and orders one candidate identity stream through LibraDex when its reusable ordered shape is complete, otherwise through Fractal/Inheto scalar keys.<br/>
        /// Candidate identities are validated, deduplicated in first-appearance order, and filtered against authoritative Fractal existence before backend selection.<br/>
        /// </summary>
        /// <param name="recordIDs">Candidate identities to order.<br/></param>
        /// <param name="ignoreCase">When true, compares every text key with ordinal case folding in the direct fallback.<br/></param>
        /// <param name="sort">Requested lexicographic sort descriptors.<br/></param>
        /// <returns>A read-only ordered identity list.<br/></returns>
        private IReadOnlyList<ulong> OrderRecordIDsNatively(IEnumerable<ulong> recordIDs, bool ignoreCase, SortBy[]? sort)
        {
            ArgumentNullException.ThrowIfNull(recordIDs);

            var candidates = new List<ulong>();
            var seenCandidates = new HashSet<ulong>();
            foreach (var recordID in recordIDs)
            {
                EnsureRecordBelongsToCurrentStore(recordID, nameof(recordIDs));
                if (seenCandidates.Add(recordID) && rbase.data.RecordExists(recordID))
                    candidates.Add(recordID);
            }

            if (candidates.Count < 2 || sort is null || sort.Length == 0)
                return candidates.AsReadOnly();

            if (TryOrderRecordIDsWithLibraDex(candidates, ignoreCase, sort, out var ordered))
                return ordered;

            switch (Ordering.Fallback)
            {
                case OrderingFallback.AllowCandidateSort:
                    break;
                case OrderingFallback.RequireIndex:
                    throw new InvalidOperationException(
                        "Ordering requires a complete compatible LibraDex index under Ordering.Fallback = OrderingFallback.RequireIndex. " +
                        "Create persistent or memory indexes for every requested sort path, explicitly enable AutoIndexing for Ordering, " +
                        "or select OrderingFallback.AllowCandidateSort.");
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(Ordering.Fallback),
                        Ordering.Fallback,
                        "Unknown ordering fallback policy.");
            }

            var rows = new List<(ulong RecordID, object?[] Keys)>(candidates.Count);
            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var recordID = candidates[candidateIndex];
                if (!rbase.data.TryGetOne(recordID, out var storedRecord))
                    continue;

                using (storedRecord)
                {
                    var payload = CreateRecordBinary(recordID, storedRecord.Data);
                    var keys = new object?[sort.Length];
                    for (var keyIndex = 0; keyIndex < sort.Length; keyIndex++)
                    {
                        var path = sort[keyIndex].Key.Path;
                        if (string.IsNullOrWhiteSpace(path))
                            throw new ArgumentException("Sort descriptors must contain a property path.", nameof(sort));

                        _ = TryReadLegacyAggregateValue(payload, path, out keys[keyIndex]);
                    }

                    rows.Add((recordID, keys));
                }
            }

            rows.Sort((left, right) => CompareDirectSortRows(left, right, sort, ignoreCase));
            var result = new List<ulong>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
                result.Add(rows[i].RecordID);

            return result.AsReadOnly();
        }

        /// <summary>
        /// Attempts reusable LibraDex ordering for one complete candidate set without hydrating record payloads.<br/>
        /// The optimized branch accepts only uniform direction and case-sensitive descriptors; other shapes deliberately return <see langword="false"/> for the direct fallback.<br/>
        /// A complete identity-count check prevents nullable or missing index keys from silently dropping records.<br/>
        /// </summary>
        /// <param name="candidates">Distinct existing candidate identities in first-appearance order.<br/></param>
        /// <param name="ignoreCase">Global case-folding request.<br/></param>
        /// <param name="sort">Requested sort descriptors.<br/></param>
        /// <param name="ordered">Complete LibraDex-ordered candidates when successful.<br/></param>
        /// <returns><see langword="true"/> when LibraDex produced every candidate exactly once; otherwise <see langword="false"/>.<br/></returns>
        private bool TryOrderRecordIDsWithLibraDex(IReadOnlyList<ulong> candidates, bool ignoreCase, SortBy[] sort, out IReadOnlyList<ulong> ordered)
        {
            ordered = default!;
            if (candidates.Count == 0 || sort.Length == 0 || ignoreCase)
                return false;

            var descending = sort[0].Descending;
            var indexes = new RecordIndex[sort.Length];
            for (var i = 0; i < sort.Length; i++)
            {
                var descriptor = sort[i];
                if (string.IsNullOrWhiteSpace(descriptor.Key.Path) ||
                    descriptor.IgnoreCase ||
                    descriptor.Descending != descending ||
                    !TryGetOrCreateAutoLibraDexIndex(descriptor.Key.Path, AutoIndexUse.Ordering, out indexes[i]))
                {
                    return false;
                }
            }

            var direction = descending ? RetrievalDirection.Descending : RetrievalDirection.Ascending;
            if (!TryIterateRecordIDsByLibraDexIndexes(indexes, direction, allowIndexSupportCreation: true, out var orderedStream))
                return false;

            var candidateSet = new HashSet<ulong>(candidates);
            var seen = new HashSet<ulong>();
            var result = new List<ulong>(candidates.Count);
            foreach (var recordID in orderedStream)
            {
                if (candidateSet.Contains(recordID) && seen.Add(recordID))
                    result.Add(recordID);
            }

            if (result.Count != candidates.Count)
                return false;

            ordered = result.AsReadOnly();
            return true;
        }

        /// <summary>
        /// Compares two direct-sort rows lexicographically and uses record identity for complete key ties.<br/>
        /// Null or missing values sort before non-null values in ascending order and after them in descending order.<br/>
        /// </summary>
        /// <param name="left">Left direct-sort row.<br/></param>
        /// <param name="right">Right direct-sort row.<br/></param>
        /// <param name="sort">Sort descriptors controlling priority, direction, and text folding.<br/></param>
        /// <param name="ignoreCase">Global text-folding request.<br/></param>
        /// <returns>A standard negative, zero, or positive comparison result.<br/></returns>
        private static int CompareDirectSortRows(
            (ulong RecordID, object?[] Keys) left,
            (ulong RecordID, object?[] Keys) right,
            SortBy[] sort,
            bool ignoreCase)
        {
            for (var i = 0; i < sort.Length; i++)
            {
                var descriptor = sort[i];
                var leftKey = left.Keys[i];
                var rightKey = right.Keys[i];
                int comparison;
                if (leftKey is string leftText && rightKey is string rightText)
                {
                    comparison = string.Compare(
                        leftText,
                        rightText,
                        ignoreCase || descriptor.IgnoreCase
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal);
                }
                else
                {
                    comparison = CompareLibraDexSortKey(leftKey, rightKey);
                }

                if (comparison == 0)
                    continue;

                if (!descriptor.Descending)
                    return comparison;

                return comparison < 0 ? 1 : -1;
            }

            return left.RecordID.CompareTo(right.RecordID);
        }

        /// <summary>
        /// Missing properties and explicit null values share one grouping bucket with a null key.<br/>
        /// </summary>
        /// <param name="groupKeys">Durably identified selectors that define the composite grouping key.<br/></param>
        /// <returns>A deferred enumeration over materialized compact group buckets in first-appearance order.<br/></returns>
        private IEnumerable<GroupInfo> IterateSemanticGroupings(ISelector[] groupKeys)
        {
            ResolvedGroupingSelector[] selectors = ResolveGroupingSelectors(groupKeys);
            return Enumerate();

            IEnumerable<GroupInfo> Enumerate()
            {
                var buckets = new Dictionary<object?[], DirectGroupingAccumulator>(DirectGroupingKeyComparer.Instance);
                var ordered = new List<DirectGroupingAccumulator>();
                foreach (Inheto.InhetoBinary payload in IterateAuthoritativeGroupingPayloads(where: null, nameof(IterateGroupings)))
                {
                    object?[] keyValues = ReadGroupingKeys(payload, selectors);
                    if (!buckets.TryGetValue(keyValues, out DirectGroupingAccumulator? bucket))
                    {
                        bucket = new DirectGroupingAccumulator(keyValues, AggregateFunction.Count);
                        buckets.Add(keyValues, bucket);
                        ordered.Add(bucket);
                    }

                    bucket.AddRow(aggregateValueExists: false, aggregateValue: null);
                }

                for (var i = 0; i < ordered.Count; i++)
                    yield return CreateSemanticGroupingInfo(
                        groupKeys,
                        selectors.Select(static selector => selector.Identity).ToArray(),
                        ordered[i],
                        name: null);
            }
        }

        /// <summary>
        /// Reduces one grouped aggregate over authoritative Fractal/Inheto payloads in a single record pass.<br/>
        /// Only compact composite keys and per-group aggregate state are retained; complete records are never accumulated.<br/>
        /// </summary>
        /// <param name="aggregate">Aggregate operation to execute inside each group.<br/></param>
        /// <param name="aggregateSelector">Direct or durably computed selector for non-count operations.<br/></param>
        /// <param name="where">Optional planned condition selecting source records.<br/></param>
        /// <param name="name">Optional result and group label.<br/></param>
        /// <param name="groupKeys">Direct or durably computed selectors defining each composite group.<br/></param>
        /// <returns>A materialized backend-neutral grouped result.<br/></returns>
        private GroupedRecords MaterializeSemanticAggregateGroupings(
            AggregateFunction aggregate,
            ISelector? aggregateSelector,
            Condition<T>.EndCondition? where,
            string? name,
            ISelector[] groupKeys)
        {
            ResolvedGroupingSelector[] selectors = ResolveGroupingSelectors(groupKeys);
            Func<Inheto.InhetoBinary, (bool HasValue, object? Value)>? aggregateReader =
                aggregate == AggregateFunction.Count
                    ? null
                    : ResolveSelectorReader(
                        aggregateSelector ?? throw new ArgumentNullException(nameof(aggregateSelector)),
                        "grouped aggregate");
            var buckets = new Dictionary<object?[], DirectGroupingAccumulator>(DirectGroupingKeyComparer.Instance);
            var ordered = new List<DirectGroupingAccumulator>();
            foreach (Inheto.InhetoBinary payload in IterateAuthoritativeGroupingPayloads(where, nameof(GetAggregateGroupings)))
            {
                object?[] keyValues = ReadGroupingKeys(payload, selectors);
                if (!buckets.TryGetValue(keyValues, out DirectGroupingAccumulator? bucket))
                {
                    bucket = new DirectGroupingAccumulator(keyValues, aggregate);
                    buckets.Add(keyValues, bucket);
                    ordered.Add(bucket);
                }

                (bool HasValue, object? Value) aggregateValue = aggregateReader is null
                    ? default
                    : aggregateReader(payload);
                bucket.AddRow(aggregateValue.HasValue, aggregateValue.Value);
            }

            var groups = new List<GroupInfo>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
                groups.Add(CreateSemanticGroupingInfo(
                    groupKeys,
                    selectors.Select(static selector => selector.Identity).ToArray(),
                    ordered[i],
                    name));
            return new GroupedRecords(groups, where, name);
        }

        /// <summary>
        /// Resolves every grouping selector to a reusable semantic reader and complete canonical identity.<br/>
        /// Direct untyped selectors retain the legacy property reader; computed selectors require both an executable descriptor and durable condition-wire representation.<br/>
        /// </summary>
        /// <param name="groupKeys">Grouping selectors to validate.<br/></param>
        /// <returns>Resolved selectors aligned with the supplied selector order.<br/></returns>
        private ResolvedGroupingSelector[] ResolveGroupingSelectors(ISelector[] groupKeys)
        {
            ArgumentNullException.ThrowIfNull(groupKeys);
            if (groupKeys.Length == 0)
                throw new ArgumentException("At least one grouping selector must be specified.", nameof(groupKeys));

            var resolved = new ResolvedGroupingSelector[groupKeys.Length];
            for (var i = 0; i < groupKeys.Length; i++)
            {
                ArgumentNullException.ThrowIfNull(groupKeys[i]);
                ISelector selector = groupKeys[i];
                Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader =
                    ResolveSelectorReader(selector, $"grouping selector ordinal {i}");
                string identity;
                if (TryResolveLegacySimpleSelectorPath(selector, out string directPath))
                {
                    identity = directPath;
                }
                else
                {
                    identity = ConditionDescriptor<T>.ToCanonicalSelectorString(selector);
                }

                resolved[i] = new ResolvedGroupingSelector(identity, reader);
            }

            return resolved;
        }

        /// <summary>
        /// Resolves one selector to a reusable boxed reader before authoritative record traversal begins.<br/>
        /// Typed direct and computed selectors share the native semantic reader; an untyped direct path retains the established Inheto property reader for source compatibility.<br/>
        /// </summary>
        /// <param name="selector">Selector whose values will be read repeatedly.<br/></param>
        /// <param name="role">Diagnostic role reported when no backend-neutral execution contract exists.<br/></param>
        /// <returns>A reusable value reader that distinguishes missing or null values from ordinary values.<br/></returns>
        private Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> ResolveSelectorReader(
            ISelector selector,
            string role)
        {
            if (TryCreateStoreSelectorReader(selector, out var reader))
                return reader;
            if (TryResolveLegacySimpleSelectorPath(selector, out string path))
            {
                return payload => TryReadLegacyAggregateValue(payload, path, out object? value)
                    ? (true, value)
                    : (false, null);
            }

            throw new NotSupportedException(
                $"The {role} does not expose an executable backend-neutral selector descriptor.");
        }

        /// <summary>
        /// Conditions flow through the shared direct/candidate/residual planner and reuse its retained payload.<br/>
        /// </summary>
        /// <param name="where">Optional record condition.<br/></param>
        /// <param name="operationName">Public operation name used in planner diagnostics.<br/></param>
        /// <returns>Authoritative Inheto payloads for selected records.<br/></returns>
        private IEnumerable<Inheto.InhetoBinary> IterateAuthoritativeGroupingPayloads(
            Condition<T>.EndCondition? where,
            string operationName)
        {
            if (where is not null)
            {
                foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(where, operationName, includePayload: true))
                {
                    Inheto.InhetoBinary payload = match.Payload
                        ?? throw new InvalidDataException($"{operationName} requested a grouped match without a retained Inheto payload.");
                    payload.SetMaterializationContext(match.RecordId);
                    yield return payload;
                }
                yield break;
            }

            foreach (var storedRecord in IterateFractalRecordsByGroupKey(storeID))
            {
                using (storedRecord)
                {
                    Inheto.InhetoBinary payload = CreateRecordBinary(storedRecord.Header.ID, storedRecord.Data);
                    payload.SetMaterializationContext(storedRecord.Header.ID);
                    yield return payload;
                }
            }
        }

        /// <summary>
        /// Reads one composite grouping key from an authoritative Inheto payload.<br/>
        /// A missing property is represented as null so it shares the explicit-null grouping bucket.<br/>
        /// </summary>
        /// <param name="payload">Authoritative record payload.<br/></param>
        /// <param name="selectors">Resolved grouping readers.<br/></param>
        /// <returns>Composite key values aligned with <paramref name="selectors"/>.<br/></returns>
        private static object?[] ReadGroupingKeys(
            Inheto.InhetoBinary payload,
            ResolvedGroupingSelector[] selectors)
        {
            var values = new object?[selectors.Length];
            for (var i = 0; i < selectors.Length; i++)
            {
                (bool HasValue, object? Value) candidate = selectors[i].Reader(payload);
                values[i] = candidate.HasValue ? candidate.Value : null;
            }
            return values;
        }

        /// <summary>
        /// Converts one completed direct grouping accumulator into the stable legacy <see cref="GroupInfo"/> result shape.<br/>
        /// Drill-down equality predicates are composed for every supported grouping key value.<br/>
        /// </summary>
        /// <param name="groupKeys">Original grouping selectors used for drill-down conditions.<br/></param>
        /// <param name="identities">Canonical group identities aligned with the original selectors.<br/></param>
        /// <param name="bucket">Completed compact grouping accumulator.<br/></param>
        /// <param name="name">Optional group label.<br/></param>
        /// <returns>One materialized group result.<br/></returns>
        private GroupInfo CreateSemanticGroupingInfo(
            ISelector[] groupKeys,
            string[] identities,
            DirectGroupingAccumulator bucket,
            string? name)
        {
            Condition<T>.ContinueOrEndCondition? whereClause = null;
            for (var i = 0; i < groupKeys.Length; i++)
                whereClause = AppendGroupingEquality(groupKeys[i], bucket.KeyValues[i], whereClause);

            return new GroupInfo(
                this,
                whereClause?.EndCondition,
                Array.AsReadOnly((string[])identities.Clone()),
                Array.AsReadOnly((object?[])bucket.KeyValues.Clone()),
                bucket.Count,
                bucket.Sum,
                bucket.Average,
                bucket.Minimum,
                bucket.Maximum,
                bucket.DistinctCount,
                name);
        }

        /// <summary>
        /// Holds one grouping selector's durable identity beside the reader reused for every authoritative payload.<br/>
        /// This keeps reflection, descriptor parsing, and delegate construction out of the record hot path.<br/>
        /// </summary>
        /// <param name="Identity">Canonical direct path or complete computed selector expression.<br/></param>
        /// <param name="Reader">Reusable reader that preserves missing/null state.<br/></param>
        private sealed record ResolvedGroupingSelector(
            string Identity,
            Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> Reader);

        /// <summary>
        /// Maintains compact state for one backend-neutral grouped aggregate bucket.<br/>
        /// Numeric sums and means use Decimal accumulation before conversion to the legacy nullable Double result properties.<br/>
        /// </summary>
        private sealed class DirectGroupingAccumulator
        {
            private readonly AggregateFunction aggregate;
            private readonly HashSet<object?>? distinctValues;
            private decimal numericSum;
            private ulong aggregateCount;
            private object? minimum;
            private object? maximum;

            /// <summary>
            /// Creates an accumulator for one composite grouping key and aggregate operation.<br/>
            /// </summary>
            /// <param name="keyValues">Composite grouping key values.<br/></param>
            /// <param name="aggregate">Aggregate operation executed for this bucket.<br/></param>
            internal DirectGroupingAccumulator(object?[] keyValues, AggregateFunction aggregate)
            {
                KeyValues = keyValues;
                this.aggregate = aggregate;
                if (aggregate == AggregateFunction.DistinctCount)
                    distinctValues = new HashSet<object?>(DirectGroupingScalarEqualityComparer.Instance);
            }

            internal object?[] KeyValues { get; }
            internal ulong Count { get; private set; }
            internal double? Sum => aggregate == AggregateFunction.Sum && aggregateCount != 0
                ? Convert.ToDouble(numericSum, CultureInfo.InvariantCulture)
                : null;
            internal double? Average => aggregate == AggregateFunction.Average && aggregateCount != 0
                ? Convert.ToDouble(numericSum / aggregateCount, CultureInfo.InvariantCulture)
                : null;
            internal object? Minimum => aggregate == AggregateFunction.Min ? minimum : null;
            internal object? Maximum => aggregate == AggregateFunction.Max ? maximum : null;
            internal ulong? DistinctCount => aggregate == AggregateFunction.DistinctCount
                ? checked((ulong)distinctValues!.Count)
                : null;

            /// <summary>
            /// Adds one source row and its optional aggregate scalar to this bucket.<br/>
            /// Null or missing aggregate values are ignored by every non-count aggregate.<br/>
            /// </summary>
            /// <param name="aggregateValueExists">Whether the aggregate property exists in the source payload.<br/></param>
            /// <param name="aggregateValue">Unwrapped aggregate scalar when present.<br/></param>
            internal void AddRow(bool aggregateValueExists, object? aggregateValue)
            {
                Count = checked(Count + 1);
                if (!aggregateValueExists || aggregateValue is null || aggregate == AggregateFunction.Count)
                    return;

                switch (aggregate)
                {
                    case AggregateFunction.Sum:
                    case AggregateFunction.Average:
                        numericSum = checked(numericSum + LegacyPathAggregateAccumulator.ToLegacyAggregateDecimal(aggregateValue));
                        aggregateCount = checked(aggregateCount + 1);
                        break;
                    case AggregateFunction.Min:
                        if (minimum is null || CompareLibraDexSortKey(aggregateValue, minimum) < 0)
                            minimum = aggregateValue;
                        break;
                    case AggregateFunction.Max:
                        if (maximum is null || CompareLibraDexSortKey(aggregateValue, maximum) > 0)
                            maximum = aggregateValue;
                        break;
                    case AggregateFunction.DistinctCount:
                        distinctValues!.Add(aggregateValue);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(aggregate), aggregate, "Unknown grouped aggregate function.");
                }
            }
        }

        /// <summary>
        /// Compares composite direct-grouping keys with ordinal strings and structural byte-array semantics.<br/>
        /// Other scalar values use their CLR equality and hash contracts because every selector resolves to one stable property type.<br/>
        /// </summary>
        private sealed class DirectGroupingKeyComparer : IEqualityComparer<object?[]>
        {
            internal static readonly DirectGroupingKeyComparer Instance = new();

            /// <inheritdoc/>
            public bool Equals(object?[]? left, object?[]? right)
            {
                if (ReferenceEquals(left, right))
                    return true;
                if (left is null || right is null || left.Length != right.Length)
                    return false;
                for (var i = 0; i < left.Length; i++)
                {
                    if (!DirectGroupingScalarEqualityComparer.Instance.Equals(left[i], right[i]))
                        return false;
                }
                return true;
            }

            /// <inheritdoc/>
            public int GetHashCode(object?[] values)
            {
                var hash = new HashCode();
                for (var i = 0; i < values.Length; i++)
                    hash.Add(DirectGroupingScalarEqualityComparer.Instance.GetHashCode(values[i]));
                return hash.ToHashCode();
            }
        }

        /// <summary>
        /// Provides scalar equality for direct grouping keys and distinct aggregate values.<br/>
        /// Byte arrays use content equality; strings use ordinal equality; null hashes to zero.<br/>
        /// </summary>
        private sealed class DirectGroupingScalarEqualityComparer : IEqualityComparer<object?>
        {
            internal static readonly DirectGroupingScalarEqualityComparer Instance = new();

            /// <inheritdoc/>
            public new bool Equals(object? left, object? right)
            {
                if (ReferenceEquals(left, right))
                    return true;
                if (left is byte[] leftBytes && right is byte[] rightBytes)
                    return leftBytes.AsSpan().SequenceEqual(rightBytes);
                return left?.Equals(right) == true;
            }

            /// <inheritdoc/>
            public int GetHashCode(object? value)
            {
                if (value is null)
                    return 0;
                if (value is byte[] bytes)
                {
                    var hash = new HashCode();
                    for (var i = 0; i < bytes.Length; i++)
                        hash.Add(bytes[i]);
                    return hash.ToHashCode();
                }
                if (value is string text)
                    return StringComparer.Ordinal.GetHashCode(text);
                return value.GetHashCode();
            }
        }

        //====== TYPES ======
        /// <summary>
        /// Represents the outcome of <see cref="PutManyFast(IEnumerable{T}, long?)"/> exposing the generated identifiers and indexing batch handle.
        /// </summary>
        public readonly record struct PutManyFastResult(IReadOnlyList<ulong> RecordIds, IndexingBatchHandle IndexingBatch);

        /// <summary>
        /// Flags describing which size components to include when computing the store size.<br/>
        /// </summary>
        [Flags]
        public enum RecordStoreSizeDetailFlags
        {
            /// <summary>Includes no size components and returns zero.<br/></summary>
            None = 0,
            /// <summary>Includes the shared RecordBase's authoritative record-data file size, not per-store byte attribution.<br/></summary>
            Records = 1,
            /// <summary>Includes the shared record-index file and persistent index-catalog sizes, not volatile index memory.<br/></summary>
            Indexes = 2,
            /// <summary>Includes both record-data and persistent-index file size components.<br/></summary>
            All = Records | Indexes
        }
    }

    /// <summary>
    /// Describes the retrieval direction for ordered record iteration.<br/>
    /// The default direction is <see cref="Ascending"/> so no argument is needed for common ordered reads.<br/>
    /// </summary>
    public enum RetrievalDirection
    {
        /// <summary>Traverses the established key order in ascending direction.<br/></summary>
        Ascending = 0,
        /// <summary>Traverses the established key order in descending direction.<br/></summary>
        Descending = 1
    }

    /// <summary>
    /// Describes the physical direction support requested when creating an ordered Abraxas index.<br/>
    /// This is separate from retrieval direction because index creation can affect storage and write-path cost.<br/>
    /// </summary>
    public enum IndexDirection
    {
        /// <summary>Requests ascending physical direction support when creating an index.<br/></summary>
        Ascending = 0,
        /// <summary>Requests descending physical direction support when creating an index.<br/></summary>
        Descending = 1,
        /// <summary>Requests physical direction support for both ascending and descending traversal.<br/></summary>
        Bidirectional = 2
    }

    /// <summary>
    /// Describes the text representations maintained by an Abraxas text index.<br/>
    /// Exact preserves original text identity, Folded supports case-insensitive lookup, and SortKey supports culture-stable text ordering without query-time collation work.<br/>
    /// </summary>
    [Flags]
    public enum TextIndexProfile
    {
        /// <summary>
        /// Maintains exact text keys only.<br/>
        /// This is the lowest-cost text index profile and is appropriate for case-sensitive equality and ordinal ordering scenarios.<br/>
        /// </summary>
        Exact = 0,
        /// <summary>
        /// Maintains normalized, case-folded text for no-case point and prefix lookup.<br/>
        /// Exact text storage remains implicit for every profile.<br/>
        /// </summary>
        Folded = 1,
        /// <summary>
        /// Maintains culture sort-key bytes for ordered or range comparison.<br/>
        /// Exact text storage remains implicit for every profile.<br/>
        /// </summary>
        SortKey = 2,
        /// <summary>
        /// Maintains a case-preserving Unicode canonical Form-C representation.<br/>
        /// This supports canonical-equivalence semantics without making comparison case-insensitive.<br/>
        /// </summary>
        Normalized = 4,
        /// <summary>
        /// Maintains exact and folded text keys.<br/>
        /// This supports case-sensitive lookup plus case-insensitive point lookup without maintaining sort-key data.<br/>
        /// </summary>
        ExactAndFolded = Exact | Folded,
        /// <summary>
        /// Maintains exact text keys and sort-key data.<br/>
        /// This supports case-sensitive lookup plus stable text ordering without maintaining folded lookup data.<br/>
        /// </summary>
        ExactAndSortKey = Exact | SortKey,
        /// <summary>
        /// Maintains exact, folded, and sort-key text representations.<br/>
        /// This is the widest text profile and should be selected deliberately because it increases index maintenance work.<br/>
        /// </summary>
        ExactFoldedAndSortKey = Exact | Folded | SortKey,
        /// <summary>
        /// Maintains exact and case-preserving canonical Form-C text.<br/>
        /// </summary>
        ExactAndNormalized = Exact | Normalized,
        /// <summary>
        /// Maintains exact, folded, and case-preserving canonical Form-C text.<br/>
        /// </summary>
        ExactFoldedAndNormalized = Exact | Folded | Normalized,
        /// <summary>
        /// Maintains exact, sort-key, and case-preserving canonical Form-C text.<br/>
        /// </summary>
        ExactSortKeyAndNormalized = Exact | SortKey | Normalized,
        /// <summary>
        /// Maintains every supported text representation.<br/>
        /// Select this deliberately because every projection adds persistent space and mutation work.<br/>
        /// </summary>
        All = Folded | SortKey | Normalized
    }

    internal interface IRecordIndexOwner
    {
        bool RemoveIndex(RecordIndex index);
        bool RebuildIndex(RecordIndex index);
        bool AddTextSubIndex(RecordIndex index, CompareOptions compareOptions, CultureInfo culture);
    }

    /// <summary>
    /// Small Abraxas-facing handle for an index that can be used by ordered retrieval APIs.<br/>
    /// Instances are returned by <c>GetIndex</c> and <c>TryGetIndex</c>; callers do not need storage-engine terms or internal index names for normal use.<br/>
    /// </summary>
    public readonly struct RecordIndex
    {
        private readonly IRecordIndexOwner? owner;

        internal RecordIndex(string name, string keyPath, bool isUnique, IRecordIndexOwner? owner)
        {
            Name = name;
            KeyPath = keyPath;
            IsUnique = isUnique;
            this.owner = owner;
        }

        /// <summary>
        /// Stable Abraxas index name, normally the logical index prefix created by the store.<br/>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Simple property path that provides ordered retrieval, when available.<br/>
        /// </summary>
        public string KeyPath { get; }

        /// <summary>
        /// True when the index was created as unique.<br/>
        /// </summary>
        public bool IsUnique { get; }

        /// <summary>
        /// Tests whether this lightweight handle was issued by the supplied store owner.<br/>
        /// This internal guard prevents a same-named index from another store being substituted into an exact-index reader request.<br/>
        /// </summary>
        /// <param name="candidateOwner">Store owner expected to have issued this handle.<br/></param>
        /// <returns><see langword="true"/> only when the owner references are identical.<br/></returns>
        internal bool IsOwnedBy(IRecordIndexOwner candidateOwner) => ReferenceEquals(owner, candidateOwner);

        /// <summary>
        /// Removes this index from its owning store.<br/>
        /// The call routes through the same store-level removal path as <c>RemoveIndex(RecordIndex)</c>.<br/>
        /// </summary>
        /// <returns><c>true</c> when an index or ordered backend was removed; otherwise <c>false</c>.<br/></returns>
        /// <exception cref="InvalidOperationException">Thrown when this handle was not produced by a store lookup or creation method.<br/></exception>
        public bool Remove()
        {
            if (owner is null)
                throw new InvalidOperationException("This RecordIndex is not attached to a RecordStore.");

            return owner.RemoveIndex(this);
        }

        /// <summary>
        /// Rebuilds this ordinary index from the owning store's authoritative Fractal/Inheto records.<br/>
        /// </summary>
        /// <returns><c>true</c> when the attached ordinary index was rebuilt; otherwise <c>false</c>.<br/></returns>
        /// <exception cref="InvalidOperationException">Thrown when this handle was not produced by a store lookup or creation method.<br/></exception>
        public bool Rebuild()
        {
            if (owner is null)
                throw new InvalidOperationException("This RecordIndex is not attached to a RecordStore.");

            return owner.RebuildIndex(this);
        }

        /// <summary>
        /// Adds one culture-aware comparison subindex beneath this text index.<br/>
        /// Exact culture plus comparison options form semantic identity; an already existing profile is an idempotent no-op and returns <c>false</c>.<br/>
        /// </summary>
        /// <param name="compareOptions">The exact .NET comparison options used to generate ordered sort keys.<br/></param>
        /// <param name="cultureName">A .NET named culture, or null/empty for invariant culture.<br/></param>
        /// <returns><c>true</c> when a new subindex was added; <c>false</c> when the exact profile already existed.<br/></returns>
        /// <exception cref="InvalidOperationException">Thrown when this handle is detached or does not represent a text index.<br/></exception>
        public bool AddSubIndex(CompareOptions compareOptions, string? cultureName = null)
        {
            if (owner is null)
                throw new InvalidOperationException("This RecordIndex is not attached to a RecordStore.");

            CultureInfo culture = string.IsNullOrEmpty(cultureName)
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(cultureName);
            return owner.AddTextSubIndex(this, compareOptions, culture);
        }

        /// <summary>
        /// Adds one culture-aware comparison subindex beneath this text index using an already resolved .NET culture.<br/>
        /// Exact culture plus comparison options form semantic identity; an already existing profile is an idempotent no-op and returns <c>false</c>.<br/>
        /// </summary>
        /// <param name="compareOptions">The exact .NET comparison options used to generate ordered sort keys.<br/></param>
        /// <param name="culture">The .NET culture whose collation rules define the subindex.<br/></param>
        /// <returns><c>true</c> when a new subindex was added; <c>false</c> when the exact profile already existed.<br/></returns>
        /// <exception cref="InvalidOperationException">Thrown when this handle is detached or does not represent a text index.<br/></exception>
        public bool AddSubIndex(CompareOptions compareOptions, CultureInfo culture)
        {
            if (owner is null)
                throw new InvalidOperationException("This RecordIndex is not attached to a RecordStore.");

            ArgumentNullException.ThrowIfNull(culture);
            return owner.AddTextSubIndex(this, compareOptions, culture);
        }
    }

    /// <summary>
    /// Describes one culture-aware sort-key subindex maintained beneath an Abraxas text index.<br/>
    /// Culture and comparison options together form exact semantic identity; runtime ordering adopts only an equal LibraDex profile and otherwise falls back to prepared evaluation.<br/>
    /// Null or empty culture names select invariant culture.<br/>
    /// </summary>
    /// <param name="CultureName">The .NET culture name, or null/empty for invariant culture.<br/></param>
    /// <param name="CompareOptions">The exact comparison options used to generate sort-key bytes.<br/></param>
    public readonly record struct TextSortProfile(
        string? CultureName = null,
        CompareOptions CompareOptions = CompareOptions.IgnoreCase);

    /// <summary>
    /// Describes one exact existing index used as a component of an explicit semantic ordering request.<br/>
    /// Application code normally supplies a bare <see cref="RecordIndex"/> for ascending order or a <c>(RecordIndex, SortDirection)</c> tuple for an explicit direction.<br/>
    /// </summary>
    public readonly record struct IndexSort
    {
        /// <summary>
        /// Creates one exact-index ordering component.<br/>
        /// </summary>
        /// <param name="index">Attached Abraxas index handle that provides the ordered key.<br/></param>
        /// <param name="direction">Direction applied to this ordering component.<br/></param>
        public IndexSort(RecordIndex index, SortDirection direction = SortDirection.Ascending)
        {
            if (string.IsNullOrWhiteSpace(index.Name))
                throw new ArgumentException("An exact-index sort component requires an attached named index.", nameof(index));

            Index = index;
            Direction = direction;
        }

        /// <summary>Gets the exact attached index handle supplying this ordering component.<br/></summary>
        public RecordIndex Index { get; }

        /// <summary>Gets the per-key semantic ordering direction.<br/></summary>
        public SortDirection Direction { get; }

        /// <summary>
        /// Creates an exact-index ordering component from an attached index handle and, when supplied, an explicit direction.<br/>
        /// </summary>
        /// <param name="index">Attached named index handle, optionally paired with an explicit component direction.<br/></param>
        /// <returns>An exact-index sort component; a bare handle uses ascending order.<br/></returns>
        public static implicit operator IndexSort(RecordIndex index) => new(index);

        /// <summary>
        /// Creates an exact-index ordering component from an attached index handle and, when supplied, an explicit direction.<br/>
        /// </summary>
        /// <param name="value">Attached named index handle, optionally paired with an explicit component direction.<br/></param>
        /// <returns>An exact-index sort component; a bare handle uses ascending order.<br/></returns>
        public static implicit operator IndexSort((RecordIndex Index, SortDirection Direction) value)
            => new(value.Index, value.Direction);
    }

    /// <summary>
    /// Describes the direction of one component in an explicit semantic ordering request.<br/>
    /// This is distinct from <see cref="RetrievalDirection"/>, which traverses one already established complete order.<br/>
    /// </summary>
    public enum SortDirection
    {
        /// <summary>Orders this semantic ordering component from smaller to larger values.<br/></summary>
        Ascending = 0,
        /// <summary>Orders this semantic ordering component from larger to smaller values.<br/></summary>
        Descending = 1
    }

    /// <summary>
    /// Defines how direct batch retrieval handles a same-store identity that does not exist when reached.<br/>
    /// Cross-store identities remain invalid and always throw regardless of this setting.<br/>
    /// </summary>
    public enum MissingRecordBehavior
    {
        /// <summary>Stops retrieval with <see cref="KeyNotFoundException"/> at the first missing identity.<br/></summary>
        Throw = 0,

        /// <summary>Omits missing identities while preserving the relative order and duplicates of present results.<br/></summary>
        Skip = 1,

        /// <summary>Preserves each missing input position with <see langword="default"/> for the requested result shape.<br/></summary>
        PreserveDefault = 2
    }
}
