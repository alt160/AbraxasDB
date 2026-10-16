using Inheto;
using LibraDex;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private LibraDexGroupedBackfillContext? activeLibraDexGroupedBackfill;

        /// <summary>
        /// Gets measurements from the most recently completed grouped ordinary-index backfill on this store facade.<br/>
        /// The value is replaced only after every staged physical index has completed and the descriptor set has been attached together.<br/>
        /// </summary>
        public IndexBackfillGroupDiagnostics? LastIndexBackfillGroupDiagnostics { get; private set; }

        /// <summary>
        /// Creates a set of new persistent scalar or text indexes through one authoritative Fractal scan and one Inheto view per record.<br/>
        /// Calls made by <paramref name="definitions"/> create empty physical definitions and stage their descriptors without exposing an unpopulated runtime index.<br/>
        /// After definition capture, each authoritative record remains alive only while every staged target extracts its compact typed key; the payload and Inheto view are then released before any full-chunk insertion work begins.<br/>
        /// Retained tuples share one operation-wide <see cref="IndexBackfillOptions.MemoryBudgetBytes"/> policy and each target also observes <see cref="IndexBackfillOptions.MaximumSortRunTupleCount"/> as a pathological-count ceiling.<br/>
        /// Compatible scalar and exact-string populations become bounded sorted runs feeding one native packed publication, while unsupported compatibility shapes retain their configured insertion/publication policy.<br/>
        /// The runtime descriptor array is replaced once after all targets finish; a failure drops every newly staged definition and restores prior Abraxas intent metadata before the exception is rethrown.<br/>
        /// This first grouped contract accepts only newly created persistent direct scalar and text indexes; existing live-index generation replacement remains a separate detached-swap lifecycle.<br/>
        /// </summary>
        /// <param name="definitions">Index-definition callback that calls ordinary <c>CreateIndex</c> and <c>CreateIndex</c> methods on the supplied store.<br/></param>
        /// <param name="cancellationToken">Cancellation observed between authoritative records and before each bounded publication.<br/></param>
        public void CreateIndexesAtomically(
            Action<RecordStore<T>> definitions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definitions);
            rbase.EnsureWriteAllowed(nameof(CreateIndexesAtomically));
            using var mutation = EnterPartialIndexMutationScope();
            if (activeLibraDexGroupedBackfill is not null)
                throw new InvalidOperationException("Grouped index definition scopes cannot be nested on the same store.");
            if (IndexBackfill.Mode != IndexBackfillMode.Optimized)
                throw new InvalidOperationException("Grouped index creation requires optimized backfill mode.");

            LastIndexBackfillGroupDiagnostics = null;
            var context = new LibraDexGroupedBackfillContext(
                rbase.GetOrdinaryIndexIntentEntries(storeID),
                replaceExisting: false);
            ExecuteLibraDexGroupedBackfill(definitions, context, cancellationToken);
        }

        /// <summary>
        /// Rebuilds an existing set of persistent exact text and scalar-8 indexes from one authoritative Fractal/Inheto scan, then publishes every replacement root through one LibraDex catalog-directory generation.<br/>
        /// Prior complete roots and Abraxas descriptors remain authoritative throughout extraction and detached construction; cancellation or failure before directory publication leaves the complete prior sibling set attached live and after reopen.<br/>
        /// The current native replacement surface accepts exact forward text without maintained projections and `SS8-8` scalar/date shapes whose null/empty key-state routes are empty.<br/>
        /// </summary>
        /// <param name="definitions">Index-definition callback declaring only existing ordinary indexes with exactly matching contracts.<br/></param>
        /// <param name="cancellationToken">Cancellation observed through extraction, spill/merge preparation, detached construction, and the final pre-publication boundary.<br/></param>
        public void ReplaceIndexesAtomically(
            Action<RecordStore<T>> definitions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definitions);
            rbase.EnsureWriteAllowed(nameof(ReplaceIndexesAtomically));
            using var mutation = EnterPartialIndexMutationScope();
            if (activeLibraDexGroupedBackfill is not null)
                throw new InvalidOperationException("Grouped index definition scopes cannot be nested on the same store.");
            if (IndexBackfill.Mode != IndexBackfillMode.Optimized)
                throw new InvalidOperationException("Grouped index replacement requires optimized backfill mode.");

            LastIndexBackfillGroupDiagnostics = null;
            var context = new LibraDexGroupedBackfillContext(
                rbase.GetOrdinaryIndexIntentEntries(storeID),
                replaceExisting: true);
            ExecuteLibraDexGroupedBackfill(definitions, context, cancellationToken);
        }

        /// <summary>
        /// Executes one new-index or replacement-index grouped lifecycle while the caller owns the store schema/mutation exclusion scope.<br/>
        /// </summary>
        private void ExecuteLibraDexGroupedBackfill(
            Action<RecordStore<T>> definitions,
            LibraDexGroupedBackfillContext context,
            CancellationToken cancellationToken)
        {
            activeLibraDexGroupedBackfill = context;
            try
            {
                definitions(this);
                if (context.Staged.Count == 0)
                    return;

                cancellationToken.ThrowIfCancellationRequested();
                IndexBackfillGroupDiagnostics diagnostics = PopulateLibraDexGroupedBackfill(
                    context,
                    cancellationToken);
                PublishLibraDexGroupedBackfill(context);
                LastIndexBackfillGroupDiagnostics = diagnostics;
                LastIndexBackfillDiagnostics = diagnostics.Indexes[^1].Diagnostics;
            }
            catch
            {
                RollBackLibraDexGroupedBackfill(context);
                throw;
            }
            finally
            {
                activeLibraDexGroupedBackfill = null;
            }
        }

        /// <summary>
        /// Diverts one ordinary registration into the active grouped scope before population or runtime attachment occurs.<br/>
        /// A repeated definition inside the same callback is idempotent, while a pre-existing physical index is rejected because this contract does not yet perform detached live-generation replacement.<br/>
        /// </summary>
        /// <param name="entry">New empty physical-index descriptor.<br/></param>
        /// <param name="indexes">Owning persistent identity group.<br/></param>
        /// <param name="ownerCatalog">Volatile owner, which grouped persistent creation does not accept.<br/></param>
        /// <param name="intent">Preparing persistent Abraxas definition.<br/></param>
        /// <param name="indexExisted">Whether the physical definition preceded this registration call.<br/></param>
        /// <returns><see langword="true"/> when an active grouped scope handled the registration; otherwise <see langword="false"/>.<br/></returns>
        private bool TryStageLibraDexGroupedBackfill(
            LibraDexSimpleIndex entry,
            CatalogIdentityGroupIndexes indexes,
            Catalog? ownerCatalog,
            OrdinaryIndexIntentEntry? intent,
            bool indexExisted)
        {
            LibraDexGroupedBackfillContext? context = activeLibraDexGroupedBackfill;
            if (context is null)
                return false;
            if (ownerCatalog is not null)
                throw new NotSupportedException("Grouped index creation currently accepts persistent indexes only.");
            if (indexExisted)
            {
                if (context.Contains(entry.Path))
                    return true;
                if (!context.ReplaceExisting)
                    return true;
            }
            else if (context.ReplaceExisting)
            {
                throw new InvalidOperationException(
                    $"Grouped index replacement requires an existing complete physical index for '{entry.Path}'.");
            }
            if (intent is null)
                throw new InvalidOperationException($"Grouped persistent index '{entry.Path}' did not produce durable preparing intent.");

            context.Stage(entry, indexes, intent, indexExisted);
            return true;
        }

        /// <summary>
        /// Scans authoritative records once in physical Fractal order through a pooled contiguous read window, constructs one Inheto view for each record, and feeds every staged typed target before advancing that window.<br/>
        /// Full tuple chunks are flushed only after the shared record scope ends, preventing storage insertion latency from extending borrowed Fractal payload lifetime.<br/>
        /// </summary>
        /// <param name="context">Complete set of staged empty index definitions.<br/></param>
        /// <param name="cancellationToken">Cancellation observed at record and publication boundaries.<br/></param>
        /// <returns>Aggregate and per-index measurements for the completed grouped population.<br/></returns>
        private IndexBackfillGroupDiagnostics PopulateLibraDexGroupedBackfill(
            LibraDexGroupedBackfillContext context,
            CancellationToken cancellationToken)
        {
            (long memoryBudgetBytes, bool automaticMemoryBudget) = IndexBackfill.ResolveMemoryBudget();
            string catalogPath = rbase.GetLibraDexCatalogPathForIndexBackfill();
            string spillDirectory = string.IsNullOrWhiteSpace(IndexBackfill.SpillDirectory)
                ? Path.GetDirectoryName(catalogPath)!
                : Path.GetFullPath(IndexBackfill.SpillDirectory);
            string spillPrefix = $"{Path.GetFileName(catalogPath)}.build.{Guid.NewGuid():N}";
            var execution = new LibraDexGroupedBackfillExecutionOptions(
                memoryBudgetBytes,
                automaticMemoryBudget,
                IndexBackfill.GetValidatedBatchSize(),
                IndexBackfill.GetValidatedMergeFanIn(),
                spillDirectory,
                spillPrefix,
                IndexBackfill.Mode,
                IndexBackfill.UseNativeSortedBuild,
                IndexBackfill.SortWithinBatch,
                IndexBackfill.UseIdentityGroupBatch,
                IndexBackfill.UseTextIdentityGroupBatch);
            LibraDexGroupedBackfillTarget[] targets = context.Staged
                .Select((staged, index) => CreateLibraDexGroupedBackfillTarget(staged, execution, index))
                .ToArray();
            try
            {
            var totalTimer = Stopwatch.StartNew();
            long extractionTicks = 0;
            int recordsRead = 0;
            int peakBuffered = 0;
            long peakBufferedBytes = 0;
            string? firstSpillTrigger = null;

            using var records = rbase.data.OpenGroupBulkRecordReader(storeID);
            while (records.Read())
            {
                ulong recordID = records.ID;
                cancellationToken.ThrowIfCancellationRequested();
                long extractionStarted = Stopwatch.GetTimestamp();
                try
                {
                    InhetoBinary binary = CreateRecordBinary(recordID, records.Data);
                    binary.SetMaterializationContext(recordID);
                    for (int i = 0; i < targets.Length; i++)
                        targets[i].Extract(recordID, binary);
                }
                catch (IndexContractException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Grouped index backfill could not read authoritative record '{recordID}'.",
                        recordID: recordID,
                        innerException: exception);
                }
                extractionTicks += Stopwatch.GetTimestamp() - extractionStarted;
                recordsRead++;

                int buffered = 0;
                for (int i = 0; i < targets.Length; i++)
                    buffered = checked(buffered + targets[i].BufferedCount);
                peakBuffered = Math.Max(peakBuffered, buffered);

                long bufferedBytes = GetAggregateBufferedBytes(targets);
                peakBufferedBytes = Math.Max(peakBufferedBytes, bufferedBytes);
                for (int i = 0; i < targets.Length; i++)
                    targets[i].FlushIfFull(cancellationToken);

                bufferedBytes = GetAggregateBufferedBytes(targets);
                if (execution.AutomaticMemoryBudget && (recordsRead & 0x0FFF) == 0)
                {
                    long pressureBudget = IndexBackfillOptions.ResolveAutomaticMemoryBudget();
                    execution.ContractMemoryBudget(pressureBudget);
                }
                while (bufferedBytes > execution.EffectiveMemoryBudgetBytes)
                {
                    LibraDexGroupedBackfillTarget? selected = null;
                    long selectedBytes = -1;
                    for (int i = 0; i < targets.Length; i++)
                    {
                        LibraDexGroupedBackfillTarget candidate = targets[i];
                        long candidateBytes = candidate.BufferedBytes;
                        if (candidate.CanFlushBuffered && candidateBytes > selectedBytes)
                        {
                            selected = candidate;
                            selectedBytes = candidateBytes;
                        }
                    }
                    if (selected is null)
                        break;
                    firstSpillTrigger ??= "Aggregate memory budget";
                    long before = bufferedBytes;
                    selected.FlushCurrent(cancellationToken);
                    bufferedBytes = GetAggregateBufferedBytes(targets);
                    if (bufferedBytes >= before)
                        break;
                }
            }

            if (context.ReplaceExisting)
                CompleteDetachedLibraDexGroupedBackfill(context, targets, cancellationToken);
            else
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    try
                    {
                        targets[i].Complete(cancellationToken);
                    }
                    catch (IndexContractException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.BackendMutation,
                            $"Grouped index '{targets[i].Path}' failed while completing its physical backfill.",
                            indexName: targets[i].Path,
                            keyPath: targets[i].Path,
                            innerException: exception);
                    }
                }
            }
            totalTimer.Stop();

            var indexDiagnostics = new IndexBackfillGroupIndexDiagnostics[targets.Length];
            int totalTuples = 0;
            int totalPublications = 0;
            long sortTicks = 0;
            long insertTicks = 0;
            long publishTicks = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                LibraDexGroupedBackfillTarget target = targets[i];
                IndexBackfillDiagnostics diagnostics = target.CreateDiagnostics(recordsRead);
                indexDiagnostics[i] = new IndexBackfillGroupIndexDiagnostics(target.Path, diagnostics);
                totalTuples = checked(totalTuples + target.TupleCount);
                totalPublications = checked(totalPublications + target.PublishedBatchCount);
                sortTicks += target.SortTicks;
                insertTicks += target.InsertTicks;
                publishTicks += target.PublishTicks;
            }

            return new IndexBackfillGroupDiagnostics(
                recordsRead,
                recordsRead,
                targets.Length,
                totalTuples,
                BackfillElapsed(extractionTicks),
                BackfillElapsed(sortTicks),
                BackfillElapsed(insertTicks),
                BackfillElapsed(publishTicks),
                totalTimer.Elapsed,
                peakBuffered,
                targets.Length > int.MaxValue / execution.MaximumSortRunTupleCount
                    ? int.MaxValue
                    : targets.Length * execution.MaximumSortRunTupleCount,
                totalPublications,
                indexDiagnostics)
            {
                SortedRunCount = targets.Sum(static target => target.SortedRunCount),
                SpillBytes = targets.Sum(static target => target.SpillBytes),
                MergePassCount = targets.Sum(static target => target.MergePassCount),
                PeakMergeQueueCount = targets.Max(static target => target.PeakMergeQueueCount),
                ConfiguredMemoryBudgetBytes = execution.AutomaticMemoryBudget
                    ? null
                    : memoryBudgetBytes,
                EffectiveMemoryBudgetBytes = execution.EffectiveMemoryBudgetBytes,
                AutomaticMemoryBudget = execution.AutomaticMemoryBudget,
                MaximumSortRunTupleCount = execution.MaximumSortRunTupleCount,
                PeakBufferedBytes = peakBufferedBytes,
                FirstSpillTrigger = firstSpillTrigger ??
                    (targets.Any(static target => target.SortedRunCount > 0)
                        ? "Maximum sort-run tuple count"
                        : null),
                SpillDirectory = spillDirectory
            };
            }
            finally
            {
                for (int i = 0; i < targets.Length; i++)
                    targets[i].Dispose();
            }
        }

        /// <summary>
        /// Creates the typed bounded target matching one staged direct scalar or exact-string descriptor.<br/>
        /// Type selection occurs once per index before the authoritative record loop and leaves all per-record reads strongly typed.<br/>
        /// </summary>
        /// <param name="staged">Staged empty physical definition.<br/></param>
        /// <param name="execution">Immutable operation-wide memory, spill, cancellation, and publication controls shared by the grouped targets.<br/></param>
        /// <param name="targetOrdinal">This target's stable declaration ordinal used to distinguish its spill files within the grouped operation.<br/></param>
        /// <returns>A typed grouped extraction and publication target.<br/></returns>
        private LibraDexGroupedBackfillTarget CreateLibraDexGroupedBackfillTarget(
            LibraDexGroupedBackfillEntry staged,
            LibraDexGroupedBackfillExecutionOptions execution,
            int targetOrdinal)
        {
            LibraDexSimpleIndex entry = staged.Entry;
            Type type = Nullable.GetUnderlyingType(entry.KeyType) ?? entry.KeyType;
            if (type == typeof(string) && entry.Index is LibraDexStringScalar8Index)
                return new LibraDexGroupedStringBackfillTarget(this, staged, execution, targetOrdinal);
            if (type == typeof(bool)) return new LibraDexGroupedScalarBackfillTarget<bool>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadBoolean(path));
            if (type == typeof(byte)) return new LibraDexGroupedScalarBackfillTarget<byte>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadByte(path));
            if (type == typeof(sbyte)) return new LibraDexGroupedScalarBackfillTarget<sbyte>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadSByte(path));
            if (type == typeof(short)) return new LibraDexGroupedScalarBackfillTarget<short>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadInt16(path));
            if (type == typeof(ushort)) return new LibraDexGroupedScalarBackfillTarget<ushort>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadUInt16(path));
            if (type == typeof(char)) return new LibraDexGroupedScalarBackfillTarget<char>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadChar(path));
            if (type == typeof(int)) return new LibraDexGroupedScalarBackfillTarget<int>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadInt32(path));
            if (type == typeof(uint)) return new LibraDexGroupedScalarBackfillTarget<uint>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadUInt32(path));
            if (type == typeof(long)) return new LibraDexGroupedScalarBackfillTarget<long>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadInt64(path));
            if (type == typeof(ulong)) return new LibraDexGroupedScalarBackfillTarget<ulong>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadUInt64(path));
            if (type == typeof(Int128)) return new LibraDexGroupedScalarBackfillTarget<Int128>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadInt128(path));
            if (type == typeof(UInt128)) return new LibraDexGroupedScalarBackfillTarget<UInt128>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadUInt128(path));
            if (type == typeof(float)) return new LibraDexGroupedScalarBackfillTarget<float>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadSingle(path));
            if (type == typeof(double)) return new LibraDexGroupedScalarBackfillTarget<double>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadDouble(path));
            if (type == typeof(decimal)) return new LibraDexGroupedScalarBackfillTarget<decimal>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadDecimal(path));
            if (type == typeof(Guid)) return new LibraDexGroupedScalarBackfillTarget<Guid>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadGuid(path));
            if (type == typeof(DateTime)) return new LibraDexGroupedScalarBackfillTarget<DateTime>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadDateTime(path));
            if (type == typeof(DateTimeOffset)) return new LibraDexGroupedScalarBackfillTarget<DateTimeOffset>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadDateTimeOffset(path));
            if (type == typeof(DateOnly)) return new LibraDexGroupedScalarBackfillTarget<DateOnly>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadDateOnly(path));
            if (type == typeof(TimeOnly)) return new LibraDexGroupedScalarBackfillTarget<TimeOnly>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadTimeOnly(path));
            if (type == typeof(TimeSpan)) return new LibraDexGroupedScalarBackfillTarget<TimeSpan>(this, staged, execution, targetOrdinal, static (binary, path) => binary.ReadTimeSpan(path));
            throw new NotSupportedException(
                $"Grouped optimized backfill does not yet support direct index '{entry.Path}' with key type '{entry.KeyType.FullName}'.");
        }

        /// <summary>
        /// Sums tuple storage retained by all active grouped targets after one authoritative record has been released.<br/>
        /// The bounded sibling count keeps exact aggregation cheaper and more predictable than cross-target allocation callbacks.<br/>
        /// </summary>
        /// <param name="targets">Active grouped sibling targets.<br/></param>
        /// <returns>Conservatively accounted managed bytes retained by tuple buffers and key arenas.<br/></returns>
        private static long GetAggregateBufferedBytes(LibraDexGroupedBackfillTarget[] targets)
        {
            long bytes = 0;
            for (int i = 0; i < targets.Length; i++)
                bytes = checked(bytes + targets[i].BufferedBytes);
            return bytes;
        }

        /// <summary>
        /// Stores one immutable operation snapshot plus the only permitted runtime contraction of an automatic memory budget.<br/>
        /// Developer option changes after grouped population begins cannot alter tuple, merge, or spill geometry for the active build.<br/>
        /// </summary>
        private sealed class LibraDexGroupedBackfillExecutionOptions
        {
            internal LibraDexGroupedBackfillExecutionOptions(
                long effectiveMemoryBudgetBytes,
                bool automaticMemoryBudget,
                int maximumSortRunTupleCount,
                int mergeFanIn,
                string spillDirectory,
                string spillPrefix,
                IndexBackfillMode mode,
                bool useNativeSortedBuild,
                bool sortWithinBatch,
                bool useIdentityGroupBatch,
                bool useTextIdentityGroupBatch)
            {
                EffectiveMemoryBudgetBytes = effectiveMemoryBudgetBytes;
                AutomaticMemoryBudget = automaticMemoryBudget;
                MaximumSortRunTupleCount = maximumSortRunTupleCount;
                MergeFanIn = mergeFanIn;
                SpillDirectory = spillDirectory;
                SpillPrefix = spillPrefix;
                Mode = mode;
                UseNativeSortedBuild = useNativeSortedBuild;
                SortWithinBatch = sortWithinBatch;
                UseIdentityGroupBatch = useIdentityGroupBatch;
                UseTextIdentityGroupBatch = useTextIdentityGroupBatch;
            }

            internal long EffectiveMemoryBudgetBytes { get; private set; }
            internal bool AutomaticMemoryBudget { get; }
            internal int MaximumSortRunTupleCount { get; }
            internal int MergeFanIn { get; }
            internal string SpillDirectory { get; }
            internal string SpillPrefix { get; }
            internal IndexBackfillMode Mode { get; }
            internal bool UseNativeSortedBuild { get; }
            internal bool SortWithinBatch { get; }
            internal bool UseIdentityGroupBatch { get; }
            internal bool UseTextIdentityGroupBatch { get; }

            /// <summary>
            /// Applies later GC pressure only when it reduces the automatic operation budget.<br/>
            /// Explicit developer budgets and larger later automatic estimates never change an active build.<br/>
            /// </summary>
            /// <param name="candidateBytes">Fresh automatic budget candidate.<br/></param>
            internal void ContractMemoryBudget(long candidateBytes)
            {
                if (AutomaticMemoryBudget && candidateBytes > 0 && candidateBytes < EffectiveMemoryBudgetBytes)
                    EffectiveMemoryBudgetBytes = candidateBytes;
            }
        }

        /// <summary>
        /// Prepares all bounded sources, builds their unreachable native roots, publishes the mixed sibling set once, and reopens descriptors on the new slot generations.<br/>
        /// </summary>
        private void CompleteDetachedLibraDexGroupedBackfill(
            LibraDexGroupedBackfillContext context,
            IReadOnlyList<LibraDexGroupedBackfillTarget> targets,
            CancellationToken cancellationToken)
        {
            var scalarTargets = new List<LibraDexGroupedBackfillTarget>();
            var scalarRequests = new List<Scalar8Scalar8DetachedBuildRequest>();
            var variableTargets = new List<LibraDexGroupedBackfillTarget>();
            var variableRequests = new List<VarKeyScalar8DetachedBuildRequest>();
            for (int i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LibraDexGroupedBackfillTarget target = targets[i];
                target.PrepareDetached(cancellationToken);
                if (target.TryCreateScalarDetachedRequest(out Scalar8Scalar8DetachedBuildRequest scalar))
                {
                    scalarTargets.Add(target);
                    scalarRequests.Add(scalar);
                    continue;
                }
                if (target.TryCreateVariableDetachedRequest(out VarKeyScalar8DetachedBuildRequest variable))
                {
                    variableTargets.Add(target);
                    variableRequests.Add(variable);
                    continue;
                }
                throw new NotSupportedException($"Grouped replacement index '{target.Path}' does not expose a native detached build request.");
            }

            if (variableRequests.Count == 0)
            {
                Scalar8Scalar8DetachedBuildGroupResult result = scalarTargets[0].ReplaceScalarDetachedGroup(
                    CollectionsMarshal.AsSpan(scalarRequests),
                    cancellationToken);
                for (int i = 0; i < result.Builds.Count; i++)
                    scalarTargets[i].ApplyScalarDetachedBuild(result.Builds[i].Build);
                scalarTargets[0].AddDetachedPublicationTime(result.DirectoryPublicationTime);
            }
            else
            {
                NativeSortedDetachedBuildGroupResult result = variableTargets[0].ReplaceMixedDetachedGroup(
                    CollectionsMarshal.AsSpan(scalarRequests),
                    CollectionsMarshal.AsSpan(variableRequests),
                    cancellationToken);
                for (int i = 0; i < result.ScalarBuilds.Count; i++)
                    scalarTargets[i].ApplyScalarDetachedBuild(result.ScalarBuilds[i].Build);
                for (int i = 0; i < result.VariableBuilds.Count; i++)
                    variableTargets[i].ApplyVariableDetachedBuild(result.VariableBuilds[i].Build);
                variableTargets[0].AddDetachedPublicationTime(result.DirectoryPublicationTime);
            }

            for (int i = 0; i < targets.Count; i++)
                targets[i].ReleasePreparedSource();

            for (int i = 0; i < context.Staged.Count; i++)
            {
                LibraDexGroupedBackfillEntry staged = context.Staged[i];
                staged.Entry = staged.Entry.WithIndex(staged.Indexes.Index(staged.Entry.Index.Name));
            }
            for (int i = 0; i < targets.Count; i++)
                targets[i].ValidateDetachedPopulation(cancellationToken);
        }

        /// <summary>
        /// Marks every staged durable intent ready and atomically replaces the runtime descriptor snapshot after physical population has completed.<br/>
        /// The store mutation gate remains held by the public caller across metadata and descriptor publication.<br/>
        /// </summary>
        /// <param name="context">Successfully populated staged definitions.<br/></param>
        private void PublishLibraDexGroupedBackfill(LibraDexGroupedBackfillContext context)
        {
            for (int i = 0; i < context.Staged.Count; i++)
                CompleteOrdinaryIndexRegistration(ownerCatalog: null, context.Staged[i].Intent);

            lock (libraDexIndexLock)
            {
                LibraDexSimpleIndex[] current = libraDexSimpleIndexes;
                if (context.ReplaceExisting)
                {
                    LibraDexSimpleIndex[] replacements = (LibraDexSimpleIndex[])current.Clone();
                    for (int i = 0; i < context.Staged.Count; i++)
                    {
                        LibraDexGroupedBackfillEntry staged = context.Staged[i];
                        int offset = Array.FindIndex(replacements, existing => string.Equals(existing.Path, staged.Entry.Path, StringComparison.Ordinal));
                        if (offset < 0)
                            throw new InvalidOperationException($"Grouped replacement index '{staged.Entry.Path}' is no longer attached.");
                        replacements[offset] = staged.Entry;
                    }
                    libraDexSimpleIndexes = replacements;
                    return;
                }
                var paths = new HashSet<string>(current.Select(static entry => entry.Path), StringComparer.Ordinal);
                for (int i = 0; i < context.Staged.Count; i++)
                {
                    if (!paths.Add(context.Staged[i].Entry.Path))
                        throw new InvalidOperationException($"Grouped index '{context.Staged[i].Entry.Path}' became attached before grouped publication completed.");
                }

                var appended = new LibraDexSimpleIndex[current.Length + context.Staged.Count];
                Array.Copy(current, appended, current.Length);
                for (int i = 0; i < context.Staged.Count; i++)
                    appended[current.Length + i] = context.Staged[i].Entry;
                libraDexSimpleIndexes = appended;
            }
        }

        /// <summary>
        /// Drops each newly created staged physical definition and restores the intent snapshot captured before the grouped callback began.<br/>
        /// When a physical drop cannot be confirmed, a durable removal tombstone prevents a later catalog scan from attaching the rejected partial definition.<br/>
        /// </summary>
        /// <param name="context">Possibly partially populated grouped context.<br/></param>
        private void RollBackLibraDexGroupedBackfill(LibraDexGroupedBackfillContext context)
        {
            for (int i = context.Staged.Count - 1; i >= 0; i--)
            {
                LibraDexGroupedBackfillEntry staged = context.Staged[i];
                bool dropped = staged.Existed;
                if (!staged.Existed)
                {
                    try
                    {
                        _ = staged.Indexes.Drop(staged.Entry.Path);
                        dropped = !staged.Indexes.HasIndex(staged.Entry.Path);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    if (context.PriorIntents.TryGetValue(staged.Entry.Path, out OrdinaryIndexIntentEntry? prior))
                        rbase.UpsertOrdinaryIndexIntent(prior);
                    else
                        rbase.RemoveOrdinaryIndexIntent(storeID, staged.Entry.Path);
                }
                catch
                {
                }

                if (!dropped)
                {
                    try
                    {
                        rbase.AddOrdinaryIndexRemoval(new OrdinaryIndexRemovalEntry
                        {
                            StoreID = storeID,
                            LogicalName = staged.Entry.Path,
                            PhysicalName = staged.Entry.Path
                        });
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Stores staged physical definitions and the pre-scope durable intent snapshot used for rollback.<br/>
        /// </summary>
        private sealed class LibraDexGroupedBackfillContext
        {
            private readonly HashSet<string> paths = new(StringComparer.Ordinal);

            /// <summary>
            /// Captures the detached pre-scope intent snapshot.<br/>
            /// </summary>
            /// <param name="priorIntents">All ordinary intents owned by this store before definition capture.<br/></param>
            /// <param name="replaceExisting">Whether already complete definitions must be rebuilt through detached replacement rather than reused.<br/></param>
            internal LibraDexGroupedBackfillContext(
                IEnumerable<OrdinaryIndexIntentEntry> priorIntents,
                bool replaceExisting)
            {
                ReplaceExisting = replaceExisting;
                PriorIntents = priorIntents.ToDictionary(
                    static intent => intent.LogicalName,
                    static intent => intent.Clone(),
                    StringComparer.Ordinal);
            }

            /// <summary>Gets staged definitions in developer declaration order.<br/></summary>
            internal List<LibraDexGroupedBackfillEntry> Staged { get; } = new();

            /// <summary>Gets whether the scope requires detached replacement of already complete definitions.<br/></summary>
            internal bool ReplaceExisting { get; }

            /// <summary>Gets the durable intent snapshot that preceded the grouped operation.<br/></summary>
            internal IReadOnlyDictionary<string, OrdinaryIndexIntentEntry> PriorIntents { get; }

            /// <summary>
            /// Tests whether the callback already staged a matching logical path.<br/>
            /// </summary>
            /// <param name="path">Canonical logical path.<br/></param>
            /// <returns><see langword="true"/> when the path is already staged.<br/></returns>
            internal bool Contains(string path) => paths.Contains(path);

            /// <summary>
            /// Appends one newly created empty definition exactly once.<br/>
            /// </summary>
            /// <param name="entry">Runtime descriptor withheld until publication.<br/></param>
            /// <param name="indexes">Owning identity group used for rollback.<br/></param>
            /// <param name="intent">Preparing persistent definition.<br/></param>
            /// <param name="existed">Whether the physical definition existed before this scope, allowing rollback to distinguish replacements from newly created definitions.<br/></param>
            internal void Stage(
                LibraDexSimpleIndex entry,
                CatalogIdentityGroupIndexes indexes,
                OrdinaryIndexIntentEntry intent,
                bool existed)
            {
                if (!paths.Add(entry.Path))
                    return;
                Staged.Add(new LibraDexGroupedBackfillEntry(entry, indexes, intent, existed));
            }
        }

        /// <summary>
        /// Couples one withheld runtime descriptor to its rollback owner and durable preparing intent.<br/>
        /// </summary>
        private sealed class LibraDexGroupedBackfillEntry
        {
            /// <summary>Initializes one staged logical definition and its physical ownership state.<br/></summary>
            /// <param name="entry">Runtime descriptor withheld until grouped publication.<br/></param>
            /// <param name="indexes">Owning identity group used to restore or remove the physical definition during rollback.<br/></param>
            /// <param name="intent">Preparing persistent definition retained until grouped publication completes.<br/></param>
            /// <param name="existed">Whether the physical definition existed before this scope, distinguishing a replacement from a newly created definition.<br/></param>
            internal LibraDexGroupedBackfillEntry(
                LibraDexSimpleIndex entry,
                CatalogIdentityGroupIndexes indexes,
                OrdinaryIndexIntentEntry intent,
                bool existed)
            {
                Entry = entry;
                Indexes = indexes;
                Intent = intent;
                Existed = existed;
            }

            internal LibraDexSimpleIndex Entry { get; set; }
            internal CatalogIdentityGroupIndexes Indexes { get; }
            internal OrdinaryIndexIntentEntry Intent { get; }
            internal bool Existed { get; }
        }

        /// <summary>
        /// Defines the bounded typed extraction and publication contract used by one grouped sibling index.<br/>
        /// </summary>
        private abstract class LibraDexGroupedBackfillTarget : IDisposable
        {
            /// <summary>
            /// Initializes one target with its owning store and immutable physical descriptor.<br/>
            /// </summary>
            /// <param name="owner">Owning record store.<br/></param>
            /// <param name="staged">Staged physical descriptor.<br/></param>
            /// <param name="execution">Immutable operation-wide memory, spill, cancellation, and publication controls shared by the grouped targets.<br/></param>
            /// <param name="targetOrdinal">This target's stable declaration ordinal used to distinguish its spill files within the grouped operation.<br/></param>
            protected LibraDexGroupedBackfillTarget(
                RecordStore<T> owner,
                LibraDexGroupedBackfillEntry staged,
                LibraDexGroupedBackfillExecutionOptions execution,
                int targetOrdinal)
            {
                Owner = owner;
                Staged = staged;
                Entry = staged.Entry;
                Execution = execution;
                TargetOrdinal = targetOrdinal;
            }

            /// <summary>Gets the owning record store.<br/></summary>
            protected RecordStore<T> Owner { get; }

            /// <summary>Gets the staged definition owning this target.<br/></summary>
            protected LibraDexGroupedBackfillEntry Staged { get; }

            /// <summary>Gets the staged physical descriptor.<br/></summary>
            protected LibraDexSimpleIndex Entry { get; }

            /// <summary>Gets the immutable grouped operation controls.<br/></summary>
            protected LibraDexGroupedBackfillExecutionOptions Execution { get; }

            /// <summary>Gets this target's stable declaration ordinal for spill filenames.<br/></summary>
            protected int TargetOrdinal { get; }

            /// <summary>Gets the canonical logical path.<br/></summary>
            internal string Path => Entry.Path;

            /// <summary>Gets the current bounded tuple count.<br/></summary>
            internal abstract int BufferedCount { get; }

            /// <summary>Gets conservatively accounted managed bytes retained by the current tuple buffer.<br/></summary>
            internal abstract long BufferedBytes { get; }

            /// <summary>Gets whether a non-empty current buffer can be released by one sorted run or bounded publication.<br/></summary>
            internal abstract bool CanFlushBuffered { get; }

            /// <summary>Gets whether this target completed through a shape-native packed builder.<br/></summary>
            protected virtual bool NativeBuildUsed => false;

            /// <summary>Releases one current buffer because the aggregate operation budget was crossed.<br/></summary>
            /// <param name="cancellationToken">Cancellation observed before sorting, writing, or publication.<br/></param>
            internal abstract void FlushCurrent(CancellationToken cancellationToken);

            /// <summary>Finalizes the bounded source without changing a live replacement root.<br/></summary>
            internal virtual void PrepareDetached(CancellationToken cancellationToken)
                => throw new NotSupportedException($"Index '{Path}' does not support native detached replacement.");

            /// <summary>Attempts to create an `SS8-8` detached request.<br/></summary>
            internal virtual bool TryCreateScalarDetachedRequest(out Scalar8Scalar8DetachedBuildRequest request)
            {
                request = default;
                return false;
            }

            /// <summary>Attempts to create a `VS8` detached request.<br/></summary>
            internal virtual bool TryCreateVariableDetachedRequest(out VarKeyScalar8DetachedBuildRequest request)
            {
                request = default;
                return false;
            }

            /// <summary>Publishes a scalar-only detached sibling set through the owning session.<br/></summary>
            internal virtual Scalar8Scalar8DetachedBuildGroupResult ReplaceScalarDetachedGroup(
                ReadOnlySpan<Scalar8Scalar8DetachedBuildRequest> requests,
                CancellationToken cancellationToken)
                => throw new NotSupportedException($"Index '{Path}' cannot anchor a scalar detached replacement group.");

            /// <summary>Publishes a mixed scalar/text detached sibling set through the owning session.<br/></summary>
            internal virtual NativeSortedDetachedBuildGroupResult ReplaceMixedDetachedGroup(
                ReadOnlySpan<Scalar8Scalar8DetachedBuildRequest> scalarRequests,
                ReadOnlySpan<VarKeyScalar8DetachedBuildRequest> variableRequests,
                CancellationToken cancellationToken)
                => throw new NotSupportedException($"Index '{Path}' cannot anchor a mixed detached replacement group.");

            /// <summary>Attributes one completed `SS8-8` root build to this target's diagnostics without boxing.<br/></summary>
            internal virtual void ApplyScalarDetachedBuild(Scalar8Scalar8SortedBuildResult build)
                => throw new NotSupportedException($"Index '{Path}' cannot accept detached build diagnostics.");

            /// <summary>Attributes one completed `VS8` root build to this target's diagnostics without boxing.<br/></summary>
            internal virtual void ApplyVariableDetachedBuild(VarKeyScalar8SortedBuildResult build)
                => throw new NotSupportedException($"Index '{Path}' cannot accept detached build diagnostics.");

            /// <summary>Adds the shared directory publication duration to one target so group totals count it once.<br/></summary>
            internal void AddDetachedPublicationTime(TimeSpan elapsed)
            {
                PublishTicks += checked((long)Math.Round(elapsed.TotalSeconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero));
            }

            /// <summary>Validates the newly reopened detached population through the same logical contract as initial creation.<br/></summary>
            internal virtual void ValidateDetachedPopulation(CancellationToken cancellationToken)
            {
            }

            /// <summary>
            /// Releases a completed native tuple source after LibraDex has synchronously consumed every tuple.<br/>
            /// Detached grouped replacement keeps all sources alive until its one atomic physical publication returns, then invokes this hook for every target.<br/>
            /// </summary>
            internal virtual void ReleasePreparedSource()
            {
            }

            /// <summary>Gets the complete admitted tuple count.<br/></summary>
            internal int TupleCount { get; private protected set; }

            /// <summary>Gets the largest per-index tuple buffer observed.<br/></summary>
            internal int PeakBufferedCount { get; private protected set; }

            /// <summary>Gets the largest retained byte count observed for this target.<br/></summary>
            internal long PeakBufferedBytes { get; private protected set; }

            /// <summary>Gets completed non-empty publication count.<br/></summary>
            internal int PublishedBatchCount { get; private protected set; }

            /// <summary>Gets accumulated bounded-sort timestamp ticks.<br/></summary>
            internal long SortTicks { get; private protected set; }

            /// <summary>Gets accumulated typed-insertion timestamp ticks.<br/></summary>
            internal long InsertTicks { get; private protected set; }

            /// <summary>Gets accumulated batch-publication timestamp ticks.<br/></summary>
            internal long PublishTicks { get; private protected set; }

            /// <summary>Gets the number of initial sorted spill runs produced by this target.<br/></summary>
            internal int SortedRunCount { get; private protected set; }

            /// <summary>Gets cumulative temporary tuple bytes written across initial runs and merge outputs.<br/></summary>
            internal long SpillBytes { get; private protected set; }

            /// <summary>Gets the number of bounded k-way merge passes completed by this target.<br/></summary>
            internal int MergePassCount { get; private protected set; }

            /// <summary>Gets the largest number of run heads retained by this target's merge queue.<br/></summary>
            internal int PeakMergeQueueCount { get; private protected set; }

            /// <summary>
            /// Extracts one compact typed key from the shared live Inheto view.<br/>
            /// </summary>
            /// <param name="recordID">Authoritative record identity.<br/></param>
            /// <param name="binary">Shared record-local Inheto view.<br/></param>
            internal abstract void Extract(ulong recordID, InhetoBinary binary);

            /// <summary>
            /// Publishes the current chunk when it has reached the configured bound.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before publication.<br/></param>
            internal abstract void FlushIfFull(CancellationToken cancellationToken);

            /// <summary>
            /// Publishes the final non-empty chunk.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before final publication.<br/></param>
            internal abstract void Complete(CancellationToken cancellationToken);

            /// <summary>
            /// Builds the existing per-index diagnostics shape from grouped measurements.<br/>
            /// </summary>
            /// <param name="recordsRead">Shared authoritative record count.<br/></param>
            /// <returns>Per-index grouped diagnostics.<br/></returns>
            internal IndexBackfillDiagnostics CreateDiagnostics(int recordsRead)
                => new(
                    Execution.Mode,
                    IndexBackfillMode.Optimized,
                    recordsRead,
                    TupleCount,
                    TimeSpan.Zero,
                    BackfillElapsed(SortTicks),
                    BackfillElapsed(InsertTicks),
                    BackfillElapsed(PublishTicks),
                    PeakBufferedCount,
                    PublishedBatchCount)
                {
                    SortedRunCount = SortedRunCount,
                    SpillBytes = SpillBytes,
                    MergePassCount = MergePassCount,
                    PeakMergeQueueCount = PeakMergeQueueCount,
                    NativeBuildUsed = NativeBuildUsed,
                    EffectiveMemoryBudgetBytes = Execution.EffectiveMemoryBudgetBytes,
                    MaximumSortRunTupleCount = Execution.MaximumSortRunTupleCount,
                    PeakBufferedBytes = PeakBufferedBytes,
                    SpillDirectory = Execution.SpillDirectory
                };

            /// <summary>
            /// Releases temporary run readers/files owned by the target after completion or rollback.<br/>
            /// </summary>
            public virtual void Dispose()
            {
            }

            /// <summary>
            /// Enables one identity-group batch with the grouped sorted-write intent.<br/>
            /// </summary>
            /// <returns>The enabled batch manager.<br/></returns>
            protected CatalogIdentityGroupBatchManager EnableBatch()
            {
                CatalogIdentityGroupBatchManager batch = Owner.GetLibraDexBackfillBatch(Entry);
                if (batch.IsEnabled)
                    throw new InvalidOperationException($"Index '{Entry.Path}' found its LibraDex identity-group batch already enabled.");
                batch.Enable(new LibraDexWriteIntent(
                    Execution.SortWithinBatch ? LibraDexWriteOrder.Sorted : LibraDexWriteOrder.Default,
                    LibraDexWriteVolume.Thousands,
                    LibraDexWriteLocality.Broad,
                    LibraDexWritePriority.WriteSpeed));
                return batch;
            }
        }

        /// <summary>
        /// Retains strongly typed scalar tuples for one grouped sibling index.<br/>
        /// </summary>
        /// <typeparam name="TKey">Supported scalar key type.<br/></typeparam>
        private sealed class LibraDexGroupedScalarBackfillTarget<TKey> : LibraDexGroupedBackfillTarget
            where TKey : struct
        {
            private readonly Func<InhetoBinary, string, TKey?> readKey;
            private readonly List<(ulong RecordID, TKey? Key)> tuples;
            private readonly HashSet<TKey>? uniqueKeys;
            private readonly LibraDexIndex<TKey, ulong> index;
            private readonly Scalar8Scalar8BackfillSpill? nativeSpill;
            private readonly List<ulong>? nativeNullRecordIDs;
            private readonly bool nativeBuildEligible;
            private bool nativeSpillPrepared;

            /// <summary>
            /// Initializes one strongly typed scalar target.<br/>
            /// </summary>
            /// <param name="owner">Owning record store.<br/></param>
            /// <param name="staged">Staged scalar descriptor.<br/></param>
            /// <param name="readKey">Typed Inheto key reader selected once before traversal.<br/></param>
            /// <param name="execution">Immutable operation-wide memory, spill, cancellation, and publication controls shared by the grouped targets.<br/></param>
            /// <param name="targetOrdinal">This target's stable declaration ordinal used to distinguish its spill files within the grouped operation.<br/></param>
            internal LibraDexGroupedScalarBackfillTarget(
                RecordStore<T> owner,
                LibraDexGroupedBackfillEntry staged,
                LibraDexGroupedBackfillExecutionOptions execution,
                int targetOrdinal,
                Func<InhetoBinary, string, TKey?> readKey)
                : base(owner, staged, execution, targetOrdinal)
            {
                LibraDexSimpleIndex entry = staged.Entry;
                this.readKey = readKey;
                index = RequireTypedScalarIndex<TKey>(entry);
                nativeBuildEligible = execution.UseNativeSortedBuild &&
                    index.SupportsScalar8Scalar8SortedBuild;
                tuples = new List<(ulong RecordID, TKey? Key)>(
                    nativeBuildEligible ? 0 : Math.Min(execution.MaximumSortRunTupleCount, 4_096));
                uniqueKeys = entry.Unique ? new HashSet<TKey>() : null;
                if (nativeBuildEligible)
                {
                    nativeSpill = new Scalar8Scalar8BackfillSpill(
                        execution.MaximumSortRunTupleCount,
                        execution.MergeFanIn,
                        execution.SpillDirectory,
                        $"{execution.SpillPrefix}.i{targetOrdinal:D3}");
                    nativeNullRecordIDs = new List<ulong>();
                }
            }

            /// <inheritdoc/>
            internal override int BufferedCount => nativeSpill is null
                ? tuples.Count
                : checked(nativeSpill.BufferedCount + nativeNullRecordIDs!.Count);

            /// <inheritdoc/>
            internal override long BufferedBytes => nativeSpill is null
                ? checked((long)tuples.Capacity * Unsafe.SizeOf<(ulong RecordID, TKey? Key)>())
                : checked(nativeSpill.BufferedBytes + (long)nativeNullRecordIDs!.Capacity * sizeof(ulong));

            /// <inheritdoc/>
            internal override bool CanFlushBuffered => BufferedCount != 0;

            /// <inheritdoc/>
            protected override bool NativeBuildUsed => nativeBuildEligible && TupleCount != 0;

            /// <inheritdoc/>
            internal override void FlushCurrent(CancellationToken cancellationToken)
            {
                if (nativeBuildEligible)
                {
                    nativeSpill!.SpillCurrent(cancellationToken);
                    FlushNativeNulls(cancellationToken);
                }
                else
                    Flush(cancellationToken);
            }

            /// <inheritdoc/>
            internal override void PrepareDetached(CancellationToken cancellationToken)
            {
                if (!nativeBuildEligible)
                    throw new NotSupportedException($"Index '{Entry.Path}' is not an `SS8-8` native replacement shape.");
                if (nativeNullRecordIDs!.Count != 0)
                    throw new NotSupportedException($"Index '{Entry.Path}' detached replacement does not yet publish null key-state identities.");
                PrepareNativeSpill(cancellationToken);
            }

            /// <inheritdoc/>
            internal override bool TryCreateScalarDetachedRequest(out Scalar8Scalar8DetachedBuildRequest request)
            {
                if (!nativeBuildEligible || !nativeSpillPrepared)
                {
                    request = default;
                    return false;
                }
                request = index.CreateDetachedSortedBuildRequest(nativeSpill!, identityMultiplicityAlreadyValidated: true);
                return true;
            }

            /// <inheritdoc/>
            internal override Scalar8Scalar8DetachedBuildGroupResult ReplaceScalarDetachedGroup(
                ReadOnlySpan<Scalar8Scalar8DetachedBuildRequest> requests,
                CancellationToken cancellationToken)
                => index.ReplaceFromSortedEncoded(requests, cancellationToken);

            /// <inheritdoc/>
            internal override void ApplyScalarDetachedBuild(Scalar8Scalar8SortedBuildResult build)
            {
                InsertTicks += checked((long)Math.Round(build.StorageBuildTime.TotalSeconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero));
                PublishedBatchCount++;
            }

            /// <inheritdoc/>
            internal override void Extract(ulong recordID, InhetoBinary binary)
            {
                TKey? key;
                try
                {
                    key = readKey(binary, Entry.Path);
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Index '{Entry.Path}' could not read authoritative record '{recordID}' during grouped population.",
                        Entry.Index.Name,
                        Entry.Path,
                        recordID,
                        exception);
                }

                if (!key.HasValue && !Entry.IncludeNullValues)
                    return;
                if (key.HasValue && uniqueKeys is not null && !uniqueKeys.Add(key.Value))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.UniqueConstraint,
                        $"Unique index '{Entry.Path}' found the same non-null key on more than one existing record.",
                        Entry.Index.Name,
                        Entry.Path,
                        recordID);
                }

                if (nativeBuildEligible)
                {
                    if (key.HasValue)
                        nativeSpill!.Add(new Scalar8Scalar8SortedTuple(index.EncodeKey8(key.Value), recordID));
                    else
                        nativeNullRecordIDs!.Add(recordID);
                }
                else
                {
                    tuples.Add((recordID, key));
                }
                TupleCount++;
                PeakBufferedCount = Math.Max(PeakBufferedCount, BufferedCount);
                PeakBufferedBytes = Math.Max(PeakBufferedBytes, BufferedBytes);
            }

            /// <inheritdoc/>
            internal override void FlushIfFull(CancellationToken cancellationToken)
            {
                if (nativeBuildEligible)
                {
                    if (BufferedCount >= Execution.MaximumSortRunTupleCount)
                    {
                        nativeSpill!.SpillCurrent(cancellationToken);
                        FlushNativeNulls(cancellationToken);
                    }
                    return;
                }
                if (tuples.Count >= Execution.MaximumSortRunTupleCount)
                    Flush(cancellationToken);
            }

            /// <inheritdoc/>
            internal override void Complete(CancellationToken cancellationToken)
            {
                if (nativeBuildEligible)
                {
                    CompleteNativeBuild(cancellationToken);
                    FlushNativeNulls(cancellationToken);
                }
                else
                    Flush(cancellationToken);
            }

            /// <summary>
            /// Attempts one native packed build when the complete scalar population remained inside one configured tuple chunk.<br/>
            /// Null key-state rows and physical shapes other than `SS8-8` return to the established per-item path before storage mutation begins.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before child storage publication.<br/></param>
            /// <returns><see langword="true"/> when the native builder completed; otherwise <see langword="false"/> for compatibility fallback.<br/></returns>
            private void CompleteNativeBuild(CancellationToken cancellationToken)
            {
                Scalar8Scalar8BackfillSpill spill = nativeSpill ??
                    throw new InvalidOperationException($"Index '{Entry.Path}' did not initialize its native spill source.");
                if (spill.Count == 0)
                    return;

                PrepareNativeSpill(cancellationToken);
                long started = Stopwatch.GetTimestamp();
                try
                {
                    LibraDexSortedBuildDiagnostics diagnostics = index.BuildFromSortedEncoded(
                        spill,
                        identityMultiplicityAlreadyValidated: true,
                        cancellationToken);

                    long elapsed = Stopwatch.GetTimestamp() - started;
                    long publicationTicks = checked((long)Math.Round(
                        diagnostics.RootPublicationTime.TotalSeconds * Stopwatch.Frequency,
                        MidpointRounding.AwayFromZero));
                    PublishTicks += Math.Min(elapsed, publicationTicks);
                    InsertTicks += Math.Max(0, elapsed - publicationTicks);
                    PublishedBatchCount++;
                }
                finally
                {
                    ReleasePreparedSource();
                }
            }

            /// <summary>
            /// Finalizes the scalar spill source once and copies its bounded-run measurements into the target diagnostics.<br/>
            /// </summary>
            private void PrepareNativeSpill(CancellationToken cancellationToken)
            {
                if (nativeSpillPrepared)
                    return;
                Scalar8Scalar8BackfillSpill spill = nativeSpill ??
                    throw new InvalidOperationException($"Index '{Entry.Path}' did not initialize its native spill source.");
                cancellationToken.ThrowIfCancellationRequested();
                spill.Complete(cancellationToken);
                SortTicks += spill.PreparationTicks;
                SortedRunCount = spill.SortedRunCount;
                SpillBytes = spill.SpillBytes;
                MergePassCount = spill.MergePassCount;
                PeakMergeQueueCount = spill.PeakMergeQueueCount;
                nativeSpillPrepared = true;
            }

            /// <inheritdoc/>
            internal override void ReleasePreparedSource() => nativeSpill?.Dispose();

            /// <summary>
            /// Inserts the bounded null-key identities through LibraDex's dedicated key-state route after the authoritative record payload has been released.<br/>
            /// Null routes remain outside the first shape-specific packed builder, while the staged Abraxas descriptor keeps both paths hidden until grouped publication completes.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before bounded null-route mutation begins.<br/></param>
            private void FlushNativeNulls(CancellationToken cancellationToken)
            {
                List<ulong>? identities = nativeNullRecordIDs;
                if (identities is null || identities.Count == 0)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < identities.Count; i++)
                {
                    LibraDexGenericInsertResult result = Entry.Index.Insert(null, identities[i]);
                    if (!result.Inserted)
                        EnsureLibraDexInsertAccepted(Entry, null, identities[i], result);
                }
                InsertTicks += Stopwatch.GetTimestamp() - started;
                identities.Clear();
                if (identities.Capacity > 4_096)
                    identities.Capacity = 4_096;
            }

            /// <inheritdoc/>
            public override void Dispose()
            {
                nativeSpill?.Dispose();
                base.Dispose();
            }

            /// <summary>
            /// Sorts, inserts, and publishes the current typed scalar chunk once.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before mutation begins.<br/></param>
            private void Flush(CancellationToken cancellationToken)
            {
                if (tuples.Count == 0)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                if (Execution.SortWithinBatch && tuples.Count > 1)
                {
                    long started = Stopwatch.GetTimestamp();
                    tuples.Sort(static (left, right) =>
                    {
                        int keyOrder = CompareNullableBackfillKeys(left.Key, right.Key);
                        return keyOrder != 0 ? keyOrder : left.RecordID.CompareTo(right.RecordID);
                    });
                    SortTicks += Stopwatch.GetTimestamp() - started;
                }

                if (!Execution.UseIdentityGroupBatch)
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (int i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key.HasValue
                            ? index.Insert(tuple.Key.Value, tuple.RecordID)
                            : Entry.Index.Insert(null, tuple.RecordID);
                        if (!result.Inserted)
                            EnsureLibraDexInsertAccepted(Entry, tuple.Key, tuple.RecordID, result);
                    }
                    InsertTicks += Stopwatch.GetTimestamp() - insertStarted;
                    tuples.Clear();
                    if (tuples.Capacity > 4_096)
                        tuples.Capacity = 4_096;
                    return;
                }

                CatalogIdentityGroupBatchManager batch = EnableBatch();
                try
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (int i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key.HasValue
                            ? index.Insert(tuple.Key.Value, tuple.RecordID)
                            : Entry.Index.Insert(null, tuple.RecordID);
                        if (!result.Inserted)
                            EnsureLibraDexInsertAccepted(Entry, tuple.Key, tuple.RecordID, result);
                    }
                    InsertTicks += Stopwatch.GetTimestamp() - insertStarted;

                    long publishStarted = Stopwatch.GetTimestamp();
                    _ = batch.PublishAndDisable();
                    PublishTicks += Stopwatch.GetTimestamp() - publishStarted;
                    PublishedBatchCount++;
                }
                catch
                {
                    if (batch.IsEnabled)
                        _ = batch.AbortAndDisable();
                    throw;
                }
                tuples.Clear();
                if (tuples.Capacity > 4_096)
                    tuples.Capacity = 4_096;
            }
        }

        /// <summary>
        /// Retains copied UTF-8 payloads for one grouped logical string sibling without materializing CLR strings.<br/>
        /// </summary>
        private sealed class LibraDexGroupedStringBackfillTarget : LibraDexGroupedBackfillTarget
        {
            private readonly List<(ulong RecordID, byte[]? Key)> tuples;
            private readonly HashSet<byte[]>? uniqueKeys;
            private readonly LibraDexStringScalar8Index index;
            private readonly VarKeyScalar8BackfillSpill? nativeSpill;
            private readonly Scalar8Scalar8BackfillSpill? nativeSentinelSpill;
            private readonly bool nativeBuildEligible;
            private bool nativeSpillPrepared;
            private int nativeNullRecordCount;
            private int nativeEmptyRecordCount;
            private long compatibilityKeyBytes;

            /// <summary>
            /// Initializes one grouped exact-string target.<br/>
            /// </summary>
            /// <param name="owner">Owning record store.<br/></param>
            /// <param name="staged">Staged logical string descriptor.<br/></param>
            /// <param name="execution">Immutable operation-wide memory, spill, cancellation, and publication controls shared by the grouped targets.<br/></param>
            /// <param name="targetOrdinal">This target's stable declaration ordinal used to distinguish its spill files within the grouped operation.<br/></param>
            internal LibraDexGroupedStringBackfillTarget(
                RecordStore<T> owner,
                LibraDexGroupedBackfillEntry staged,
                LibraDexGroupedBackfillExecutionOptions execution,
                int targetOrdinal)
                : base(owner, staged, execution, targetOrdinal)
            {
                LibraDexSimpleIndex entry = staged.Entry;
                index = RequireLibraDexStringIndex(entry);
                LibraDexIndexShapeSpec shape = index.LogicalShape;
                nativeBuildEligible = execution.UseNativeSortedBuild &&
                    shape.StringKeys == StringKeys.Exact &&
                    shape.Directions == LibraDexProjectionDirectionSet.Forward &&
                    shape.Projections.Count == 1;
                tuples = new List<(ulong RecordID, byte[]? Key)>(
                    nativeBuildEligible ? 0 : Math.Min(execution.MaximumSortRunTupleCount, 4_096));
                uniqueKeys = entry.Unique && !nativeBuildEligible
                    ? new HashSet<byte[]>(LibraDexBackfillByteArrayComparer.Instance)
                    : null;
                if (nativeBuildEligible)
                {
                    nativeSpill = new VarKeyScalar8BackfillSpill(
                        execution.MaximumSortRunTupleCount,
                        execution.MergeFanIn,
                        execution.SpillDirectory,
                        $"{execution.SpillPrefix}.i{targetOrdinal:D3}");
                    nativeSentinelSpill = new Scalar8Scalar8BackfillSpill(
                        execution.MaximumSortRunTupleCount,
                        execution.MergeFanIn,
                        execution.SpillDirectory,
                        $"{execution.SpillPrefix}.i{targetOrdinal:D3}.sentinel");
                }
            }

            /// <inheritdoc/>
            internal override int BufferedCount => nativeSpill is null
                ? tuples.Count
                : checked(nativeSpill.BufferedCount + nativeSentinelSpill!.BufferedCount);

            /// <inheritdoc/>
            internal override long BufferedBytes => nativeSpill is null
                ? checked((long)tuples.Capacity * Unsafe.SizeOf<(ulong RecordID, byte[]? Key)>() + compatibilityKeyBytes)
                : checked(nativeSpill.BufferedBytes + nativeSentinelSpill!.BufferedBytes);

            /// <inheritdoc/>
            internal override bool CanFlushBuffered => BufferedCount != 0;

            /// <inheritdoc/>
            protected override bool NativeBuildUsed => nativeBuildEligible && TupleCount != 0;

            /// <inheritdoc/>
            internal override void FlushCurrent(CancellationToken cancellationToken)
            {
                if (nativeBuildEligible)
                {
                    nativeSpill!.SpillCurrent(cancellationToken);
                    nativeSentinelSpill!.SpillCurrent(cancellationToken);
                }
                else
                    Flush(cancellationToken);
            }

            /// <inheritdoc/>
            internal override void PrepareDetached(CancellationToken cancellationToken)
            {
                if (!nativeBuildEligible)
                    throw new NotSupportedException($"Index '{Entry.Path}' is not an exact-forward native `VS8` replacement shape.");
                if (nativeNullRecordCount != 0 || nativeEmptyRecordCount != 0)
                    throw new NotSupportedException($"Index '{Entry.Path}' detached replacement does not yet publish null or empty key-state identities.");
                PrepareNativeSpill(cancellationToken);
            }

            /// <inheritdoc/>
            internal override bool TryCreateVariableDetachedRequest(out VarKeyScalar8DetachedBuildRequest request)
            {
                if (!nativeBuildEligible || !nativeSpillPrepared)
                {
                    request = default;
                    return false;
                }
                request = index.CreateDetachedExactSortedBuildRequest(
                    nativeSpill!,
                    allowDuplicateKeys: !Entry.Unique,
                    identityMultiplicityAlreadyValidated: true);
                return true;
            }

            /// <inheritdoc/>
            internal override NativeSortedDetachedBuildGroupResult ReplaceMixedDetachedGroup(
                ReadOnlySpan<Scalar8Scalar8DetachedBuildRequest> scalarRequests,
                ReadOnlySpan<VarKeyScalar8DetachedBuildRequest> variableRequests,
                CancellationToken cancellationToken)
                => index.ReplaceNativeSortedRootsAtomically(scalarRequests, variableRequests, cancellationToken);

            /// <inheritdoc/>
            internal override void ApplyVariableDetachedBuild(VarKeyScalar8SortedBuildResult build)
            {
                InsertTicks += checked((long)Math.Round(build.ChildBuildTime.TotalSeconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero));
                PublishedBatchCount++;
            }

            /// <inheritdoc/>
            internal override void ValidateDetachedPopulation(CancellationToken cancellationToken)
                => Owner.ValidateLibraDexExactStringPopulation(Staged.Entry, RequireLibraDexStringIndex(Staged.Entry), TupleCount, cancellationToken);

            /// <inheritdoc/>
            internal override void Extract(ulong recordID, InhetoBinary binary)
            {
                bool hasKey;
                ReadOnlyMemory<byte> utf8;
                try
                {
                    hasKey = binary.TryReadStringUtf8Payload(Entry.Path, out utf8);
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Index '{Entry.Path}' could not read authoritative record '{recordID}' during grouped string population.",
                        Entry.Index.Name,
                        Entry.Path,
                        recordID,
                        exception);
                }

                if (!hasKey && !Entry.IncludeNullValues)
                    return;
                if (hasKey && utf8.Length == 0 && !Entry.IncludeEmptyStrings)
                    return;

                if (nativeBuildEligible)
                {
                    if (!hasKey)
                    {
                        nativeSentinelSpill!.Add(new Scalar8Scalar8SortedTuple(0, recordID));
                        nativeNullRecordCount++;
                    }
                    else if (utf8.Length == 0)
                    {
                        nativeSentinelSpill!.Add(new Scalar8Scalar8SortedTuple(1, recordID));
                        nativeEmptyRecordCount++;
                    }
                    else
                    {
                        ReadOnlySpan<byte> key = utf8.Span;
                        int encodedLength = index.GetExactEncodedKeyLengthForNativeBuild(key);
                        Span<byte> encodedKey = stackalloc byte[encodedLength];
                        index.EncodeExactUtf8KeyForNativeBuild(key, encodedKey);
                        nativeSpill!.Add(encodedKey, recordID);
                    }
                }
                else
                {
                    byte[]? key = hasKey ? utf8.ToArray() : null;
                    if (key is not null && uniqueKeys is not null && !uniqueKeys.Add(key))
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.UniqueConstraint,
                            $"Unique index '{Entry.Path}' found the same non-null text key on more than one existing record.",
                            Entry.Index.Name,
                            Entry.Path,
                            recordID);
                    }
                    tuples.Add((recordID, key));
                    if (key is not null)
                        compatibilityKeyBytes = checked(compatibilityKeyBytes + key.Length + 24L);
                }
                TupleCount++;
                PeakBufferedCount = Math.Max(PeakBufferedCount, BufferedCount);
                PeakBufferedBytes = Math.Max(PeakBufferedBytes, BufferedBytes);
            }

            /// <inheritdoc/>
            internal override void FlushIfFull(CancellationToken cancellationToken)
            {
                if (nativeBuildEligible)
                {
                    nativeSpill!.SpillIfFull(cancellationToken);
                    nativeSentinelSpill!.SpillIfFull(cancellationToken);
                    return;
                }

                if (tuples.Count >= Execution.MaximumSortRunTupleCount)
                    Flush(cancellationToken);
            }

            /// <inheritdoc/>
            internal override void Complete(CancellationToken cancellationToken)
            {
                if (nativeBuildEligible)
                    CompleteNativeBuild(cancellationToken);
                else
                    Flush(cancellationToken);
                Owner.ValidateLibraDexExactStringPopulation(Entry, index, TupleCount, cancellationToken);
            }

            /// <summary>
            /// Publishes the complete bounded UTF-8 population through LibraDex's existing native empty-root <c>VS8</c> builder.<br/>
            /// Exact forward populations larger than the configured memory bound are sorted into bounded runs, merged, and exposed through a paged seekable tuple source before any native storage mutation begins.<br/>
            /// Fractal identities are authoritative and monotonic, so the native builder can skip a second identity-multiplicity set while still validating key order and physical tuple count.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before stable-root publication.<br/></param>
            private void CompleteNativeBuild(CancellationToken cancellationToken)
            {
                VarKeyScalar8BackfillSpill spill = nativeSpill ??
                    throw new InvalidOperationException($"Index '{Entry.Path}' did not initialize its native VS8 spill source.");
                if (spill.Count == 0 && nativeNullRecordCount == 0 && nativeEmptyRecordCount == 0)
                    return;

                PrepareNativeSpill(cancellationToken);
                Scalar8Scalar8BackfillSpill sentinels = nativeSentinelSpill ??
                    throw new InvalidOperationException($"Index '{Entry.Path}' did not initialize its bounded null/empty identity source.");
                long started = Stopwatch.GetTimestamp();
                try
                {
                    long built = index.ReplaceExactFromSortedEncoded(
                        spill,
                        new Scalar8IdentityListRange(sentinels, 0, nativeNullRecordCount, expectedKey: 0),
                        new Scalar8IdentityListRange(sentinels, nativeNullRecordCount, nativeEmptyRecordCount, expectedKey: 1),
                        allowDuplicateKeys: !Entry.Unique,
                        identityMultiplicityAlreadyValidated: true,
                        cancellationToken);
                    InsertTicks += Stopwatch.GetTimestamp() - started;
                    if (built != TupleCount)
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.BackendMutation,
                            $"Index '{Entry.Path}' native UTF-8 build published {built:N0} tuples from {TupleCount:N0} authoritative inputs.",
                            Entry.Index.Name,
                            Entry.Path);
                    }

                    PublishedBatchCount++;
                }
                catch (InvalidOperationException exception)
                    when (Entry.Unique && exception.Message.Contains("duplicate encoded key", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.UniqueConstraint,
                        $"Unique index '{Entry.Path}' found the same non-null text key on more than one existing record.",
                        Entry.Index.Name,
                        Entry.Path,
                        innerException: exception);
                }
                finally
                {
                    ReleasePreparedSource();
                }
            }

            /// <summary>
            /// Finalizes the exact-string spill source once and copies its bounded-run measurements into the target diagnostics.<br/>
            /// </summary>
            private void PrepareNativeSpill(CancellationToken cancellationToken)
            {
                if (nativeSpillPrepared)
                    return;
                VarKeyScalar8BackfillSpill spill = nativeSpill ??
                    throw new InvalidOperationException($"Index '{Entry.Path}' did not initialize its native VS8 spill source.");
                Scalar8Scalar8BackfillSpill sentinels = nativeSentinelSpill ??
                    throw new InvalidOperationException($"Index '{Entry.Path}' did not initialize its bounded null/empty identity source.");
                cancellationToken.ThrowIfCancellationRequested();
                spill.Complete(cancellationToken);
                sentinels.Complete(cancellationToken);
                SortTicks += checked(spill.PreparationTicks + sentinels.PreparationTicks);
                SortedRunCount = checked(spill.SortedRunCount + sentinels.SortedRunCount);
                SpillBytes = checked(spill.SpillBytes + sentinels.SpillBytes);
                MergePassCount = checked(spill.MergePassCount + sentinels.MergePassCount);
                PeakMergeQueueCount = Math.Max(spill.PeakMergeQueueCount, sentinels.PeakMergeQueueCount);
                nativeSpillPrepared = true;
            }

            /// <inheritdoc/>
            internal override void ReleasePreparedSource()
            {
                nativeSpill?.Dispose();
                nativeSentinelSpill?.Dispose();
            }

            /// <inheritdoc/>
            public override void Dispose()
            {
                nativeSpill?.Dispose();
                nativeSentinelSpill?.Dispose();
                base.Dispose();
            }

            /// <summary>
            /// Sorts, inserts, and publishes the current UTF-8 tuple chunk once.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before mutation begins.<br/></param>
            private void Flush(CancellationToken cancellationToken)
            {
                if (tuples.Count == 0)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                if (Execution.SortWithinBatch && tuples.Count > 1)
                {
                    long started = Stopwatch.GetTimestamp();
                    tuples.Sort(static (left, right) =>
                    {
                        int keyOrder = LibraDexBackfillByteArrayComparer.CompareNullable(left.Key, right.Key);
                        return keyOrder != 0 ? keyOrder : left.RecordID.CompareTo(right.RecordID);
                    });
                    SortTicks += Stopwatch.GetTimestamp() - started;
                }

                if (!Execution.UseTextIdentityGroupBatch)
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (int i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key is null
                            ? index.Insert(null, tuple.RecordID)
                            : index.InsertUtf8(tuple.Key, tuple.RecordID);
                        if (!result.Inserted && !LibraDexStringTupleExists(index, tuple.Key, tuple.RecordID))
                        {
                            throw new IndexContractException(
                                Entry.Unique && tuple.Key is not null
                                    ? IndexContractFailureKind.UniqueConstraint
                                    : IndexContractFailureKind.BackendMutation,
                                $"Index '{Entry.Path}' rejected a validated grouped string tuple.",
                                Entry.Index.Name,
                                Entry.Path,
                                tuple.RecordID);
                        }
                    }
                    InsertTicks += Stopwatch.GetTimestamp() - insertStarted;
                    tuples.Clear();
                    compatibilityKeyBytes = 0;
                    if (tuples.Capacity > 4_096)
                        tuples.Capacity = 4_096;
                    return;
                }

                CatalogIdentityGroupBatchManager batch = EnableBatch();
                try
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (int i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key is null
                            ? index.Insert(null, tuple.RecordID)
                            : index.InsertUtf8(tuple.Key, tuple.RecordID);
                        if (!result.Inserted && !LibraDexStringTupleExists(index, tuple.Key, tuple.RecordID))
                        {
                            throw new IndexContractException(
                                Entry.Unique && tuple.Key is not null
                                    ? IndexContractFailureKind.UniqueConstraint
                                    : IndexContractFailureKind.BackendMutation,
                                $"Index '{Entry.Path}' rejected a validated grouped string tuple.",
                                Entry.Index.Name,
                                Entry.Path,
                                tuple.RecordID);
                        }
                    }
                    InsertTicks += Stopwatch.GetTimestamp() - insertStarted;

                    long publishStarted = Stopwatch.GetTimestamp();
                    _ = batch.PublishAndDisable();
                    PublishTicks += Stopwatch.GetTimestamp() - publishStarted;
                    PublishedBatchCount++;
                }
                catch (LibraDexWriteContextVarKeyScalar8TopologyFallbackException)
                {
                    if (batch.IsEnabled)
                        _ = batch.AbortAndDisable();

                    long replayStarted = Stopwatch.GetTimestamp();
                    for (int i = 0; i < tuples.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key is null
                            ? index.Insert(null, tuple.RecordID)
                            : index.InsertUtf8(tuple.Key, tuple.RecordID);
                        if (!result.Inserted && !LibraDexStringTupleExists(index, tuple.Key, tuple.RecordID))
                        {
                            throw new IndexContractException(
                                Entry.Unique && tuple.Key is not null
                                    ? IndexContractFailureKind.UniqueConstraint
                                    : IndexContractFailureKind.BackendMutation,
                                $"Index '{Entry.Path}' rejected a validated grouped string tuple during serialized topology replay.",
                                Entry.Index.Name,
                                Entry.Path,
                                tuple.RecordID);
                        }
                    }
                    InsertTicks += Stopwatch.GetTimestamp() - replayStarted;
                }
                catch
                {
                    if (batch.IsEnabled)
                        _ = batch.AbortAndDisable();
                    throw;
                }
                tuples.Clear();
                compatibilityKeyBytes = 0;
                if (tuples.Capacity > 4_096)
                    tuples.Capacity = 4_096;
            }
        }

        /// <summary>
        /// Produces bounded sorted encoded tuple runs, merges them with a bounded fan-in, and exposes the final fixed-width spill through a one-page seekable cache.<br/>
        /// The helper owns only its exact GUID-qualified sibling spill files and removes those files after the native builder completes or the grouped backfill unwinds.<br/>
        /// </summary>
        private sealed class Scalar8Scalar8BackfillSpill : IScalar8Scalar8SortedTupleSource, IDisposable
        {
            private const int TupleSize = sizeof(ulong) * 2;
            private const int PageTupleCount = 4_096;
            private readonly int runCapacity;
            private readonly int mergeFanIn;
            private readonly string? configuredBaseDirectory;
            private readonly string filePrefix;
            private readonly List<Scalar8Scalar8SortedTuple> buffered;
            private readonly List<string> runPaths = new();
            private readonly List<string> createdPaths = new();
            private byte[]? finalPage;
            private bool finalInMemory;
            private FileStream? finalStream;
            private string? finalPath;
            private int finalPageStart = -1;
            private int finalPageCount;
            private int pathOrdinal;
            private bool completed;
            private bool disposed;

            /// <summary>
            /// Initializes one bounded run producer for an authoritative scalar index population.<br/>
            /// </summary>
            /// <param name="runCapacity">Maximum encoded tuples retained before one sorted run is spilled.<br/></param>
            /// <param name="mergeFanIn">Maximum run readers admitted to one merge group.<br/></param>
            /// <param name="configuredBaseDirectory">Resolved spill directory; normally the directory containing the LibraDex catalog, or the caller's explicit override.<br/></param>
            /// <param name="filePrefix">Non-empty operation-and-target filename prefix used to distinguish this spill producer's temporary run files.<br/></param>
            internal Scalar8Scalar8BackfillSpill(
                int runCapacity,
                int mergeFanIn,
                string? configuredBaseDirectory,
                string filePrefix)
            {
                if (runCapacity < 1)
                    throw new ArgumentOutOfRangeException(nameof(runCapacity));
                if (mergeFanIn < 2)
                    throw new ArgumentOutOfRangeException(nameof(mergeFanIn));
                this.runCapacity = runCapacity;
                this.mergeFanIn = mergeFanIn;
                this.configuredBaseDirectory = configuredBaseDirectory;
                this.filePrefix = string.IsNullOrWhiteSpace(filePrefix)
                    ? throw new ArgumentException("A scalar spill file prefix is required.", nameof(filePrefix))
                    : filePrefix;
                buffered = new List<Scalar8Scalar8SortedTuple>(Math.Min(runCapacity, 4_096));
            }

            /// <inheritdoc/>
            public int Count { get; private set; }

            /// <summary>Gets the number of encoded tuples currently retained in managed memory.<br/></summary>
            internal int BufferedCount => buffered.Count;

            /// <summary>Gets conservatively retained scalar tuple-array bytes.<br/></summary>
            internal long BufferedBytes => checked((long)buffered.Capacity * TupleSize);

            /// <summary>Gets the largest managed tuple-run population retained by this helper.<br/></summary>
            internal int PeakBufferedCount { get; private set; }

            /// <summary>Gets the number of initial bounded sorted runs written before merge passes.<br/></summary>
            internal int SortedRunCount { get; private set; }

            /// <summary>Gets cumulative tuple bytes written across initial run and merge files.<br/></summary>
            internal long SpillBytes { get; private set; }

            /// <summary>Gets the number of bounded merge passes used to reach one final run.<br/></summary>
            internal int MergePassCount { get; private set; }

            /// <summary>Gets the largest number of run heads simultaneously retained by a merge queue.<br/></summary>
            internal int PeakMergeQueueCount { get; private set; }

            /// <summary>Gets timestamp ticks spent sorting, writing, and merging temporary runs.<br/></summary>
            internal long PreparationTicks { get; private set; }

            /// <summary>
            /// Appends one already encoded tuple to the active bounded run.<br/>
            /// </summary>
            /// <param name="tuple">Encoded key and identity produced from one authoritative record.<br/></param>
            internal void Add(Scalar8Scalar8SortedTuple tuple)
            {
                ThrowIfUnavailable();
                buffered.Add(tuple);
                Count = checked(Count + 1);
                PeakBufferedCount = Math.Max(PeakBufferedCount, buffered.Count);
            }

            /// <summary>
            /// Sorts and spills the active run when it reaches the configured managed-memory bound.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before sorting and while writing tuples.<br/></param>
            internal void SpillIfFull(CancellationToken cancellationToken)
            {
                if (buffered.Count >= runCapacity)
                    SpillCurrent(cancellationToken);
            }

            /// <summary>
            /// Sorts and spills the current non-empty encoded run even when a sibling null-key buffer caused the aggregate bound to be reached first.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before sorting and while writing tuples.<br/></param>
            internal void SpillCurrent(CancellationToken cancellationToken)
            {
                if (buffered.Count == 0)
                    return;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    SpillCurrentRun(cancellationToken);
                }
                finally
                {
                    PreparationTicks += Stopwatch.GetTimestamp() - started;
                }
            }

            /// <summary>
            /// Finalizes the stable ordinal source, retaining an in-memory array when no spill was required or producing one merged fixed-width run otherwise.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before each sort, write, and merge group.<br/></param>
            internal void Complete(CancellationToken cancellationToken)
            {
                ThrowIfUnavailable();
                if (completed)
                    throw new InvalidOperationException("The scalar backfill spill source has already been completed.");
                long started = Stopwatch.GetTimestamp();
                try
                {
                    if (runPaths.Count == 0)
                    {
                        SortBuffered();
                        finalInMemory = true;
                        completed = true;
                        return;
                    }

                    if (buffered.Count > 0)
                        SpillCurrentRun(cancellationToken);
                    finalPath = MergeRuns(cancellationToken);
                    finalStream = new FileStream(
                        finalPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 64 * 1024,
                        FileOptions.RandomAccess);
                    finalPage = new byte[checked(PageTupleCount * TupleSize)];
                    completed = true;
                }
                finally
                {
                    PreparationTicks += Stopwatch.GetTimestamp() - started;
                }
            }

            /// <inheritdoc/>
            public Scalar8Scalar8SortedTuple GetTuple(int index)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!completed)
                    throw new InvalidOperationException("The scalar backfill spill source must be completed before ordinal reads begin.");
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                if (finalInMemory)
                    return buffered[index];
                if (finalStream is null || finalPage is null)
                    throw new ObjectDisposedException(nameof(Scalar8Scalar8BackfillSpill));

                if (index < finalPageStart || index >= finalPageStart + finalPageCount)
                    LoadFinalPage(index);
                int byteOffset = checked((index - finalPageStart) * TupleSize);
                return ReadTuple(finalPage.AsSpan(byteOffset, TupleSize));
            }

            /// <summary>
            /// Releases the final reader and deletes only this helper's exact GUID-qualified sibling spill files.<br/>
            /// Cleanup is best-effort so an operating-system handle race cannot replace the primary backfill result with a cleanup exception.<br/>
            /// </summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                finalStream?.Dispose();
                finalStream = null;
                finalPage = null;
                buffered.Clear();
                buffered.Capacity = 0;
                for (int i = 0; i < createdPaths.Count; i++)
                {
                    try
                    {
                        if (File.Exists(createdPaths[i]))
                            File.Delete(createdPaths[i]);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            /// <summary>
            /// Sorts the active encoded run and writes it to a new fixed-width temporary file.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before sorting and periodically while writing.<br/></param>
            private void SpillCurrentRun(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (buffered.Count == 0)
                    return;
                SortBuffered();
                string path = CreateRunPath("run");
                WriteRun(path, buffered, cancellationToken);
                runPaths.Add(path);
                SortedRunCount++;
                SpillBytes = checked(SpillBytes + (long)buffered.Count * TupleSize);
                buffered.Clear();
                if (buffered.Capacity > 4_096)
                    buffered.Capacity = 4_096;
            }

            /// <summary>
            /// Sorts the active managed run in the exact encoded order consumed by LibraDex routers and shelves.<br/>
            /// </summary>
            private void SortBuffered()
            {
                if (buffered.Count > 1)
                    buffered.Sort(static (left, right) => CompareTuples(left, right));
            }

            /// <summary>
            /// Repeatedly merges run groups within the configured fan-in until one stable final run remains.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before each pass and merge-group write.<br/></param>
            /// <returns>Path of the single final fixed-width run.<br/></returns>
            private string MergeRuns(CancellationToken cancellationToken)
            {
                List<string> current = new(runPaths);
                while (current.Count > 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var next = new List<string>((current.Count + mergeFanIn - 1) / mergeFanIn);
                    for (int start = 0; start < current.Count; start += mergeFanIn)
                    {
                        int count = Math.Min(mergeFanIn, current.Count - start);
                        if (count == 1)
                        {
                            next.Add(current[start]);
                            continue;
                        }

                        string output = CreateRunPath("merge");
                        MergeRunGroup(current.GetRange(start, count), output, cancellationToken);
                        next.Add(output);
                    }
                    current = next;
                    MergePassCount++;
                }
                return current[0];
            }

            /// <summary>
            /// Merges one bounded group of sorted fixed-width runs using one queued head per input stream.<br/>
            /// </summary>
            /// <param name="inputs">Sorted input run paths, bounded by the configured fan-in.<br/></param>
            /// <param name="outputPath">New output run path owned by this helper.<br/></param>
            /// <param name="cancellationToken">Cancellation observed before opening and periodically while writing tuples.<br/></param>
            private void MergeRunGroup(
                IReadOnlyList<string> inputs,
                string outputPath,
                CancellationToken cancellationToken)
            {
                var readers = new Scalar8Scalar8RunReader[inputs.Count];
                try
                {
                    var queue = new PriorityQueue<Scalar8Scalar8MergeItem, (ulong Key, ulong Identity, int Reader)>();
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        readers[i] = new Scalar8Scalar8RunReader(inputs[i]);
                        if (readers[i].TryRead(out Scalar8Scalar8SortedTuple tuple))
                            queue.Enqueue(new Scalar8Scalar8MergeItem(i, tuple), (tuple.Key, tuple.Identity, i));
                    }
                    PeakMergeQueueCount = Math.Max(PeakMergeQueueCount, queue.Count);

                    long outputTuples = 0;
                    using var output = new FileStream(
                        outputPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 64 * 1024,
                        FileOptions.SequentialScan);
                    Span<byte> bytes = stackalloc byte[TupleSize];
                    while (queue.TryDequeue(out Scalar8Scalar8MergeItem item, out _))
                    {
                        if ((outputTuples & 0x0FFF) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        WriteTuple(bytes, item.Tuple);
                        output.Write(bytes);
                        outputTuples++;
                        if (readers[item.ReaderIndex].TryRead(out Scalar8Scalar8SortedTuple next))
                            queue.Enqueue(new Scalar8Scalar8MergeItem(item.ReaderIndex, next), (next.Key, next.Identity, item.ReaderIndex));
                    }
                    output.Flush(flushToDisk: false);
                    SpillBytes = checked(SpillBytes + outputTuples * TupleSize);
                }
                finally
                {
                    for (int i = 0; i < readers.Length; i++)
                        readers[i]?.Dispose();
                }
            }

            /// <summary>
            /// Writes one sorted managed run using the stable fixed-width spill representation.<br/>
            /// </summary>
            /// <param name="path">New run path owned by this helper.<br/></param>
            /// <param name="tuples">Sorted encoded tuples.<br/></param>
            /// <param name="cancellationToken">Cancellation observed periodically while writing.<br/></param>
            private static void WriteRun(
                string path,
                IReadOnlyList<Scalar8Scalar8SortedTuple> tuples,
                CancellationToken cancellationToken)
            {
                using var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.SequentialScan);
                Span<byte> bytes = stackalloc byte[TupleSize];
                for (int i = 0; i < tuples.Count; i++)
                {
                    if ((i & 0x0FFF) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    WriteTuple(bytes, tuples[i]);
                    stream.Write(bytes);
                }
                stream.Flush(flushToDisk: false);
            }

            /// <summary>
            /// Loads the bounded page containing one requested final-run ordinal.<br/>
            /// </summary>
            /// <param name="index">Requested tuple ordinal.<br/></param>
            private void LoadFinalPage(int index)
            {
                if (finalStream is null || finalPage is null)
                    throw new ObjectDisposedException(nameof(Scalar8Scalar8BackfillSpill));
                finalPageStart = index - (index % PageTupleCount);
                finalPageCount = Math.Min(PageTupleCount, Count - finalPageStart);
                int byteCount = checked(finalPageCount * TupleSize);
                finalStream.Position = checked((long)finalPageStart * TupleSize);
                finalStream.ReadExactly(finalPage.AsSpan(0, byteCount));
            }

            /// <summary>
            /// Creates the next unique run path beneath this helper's resolved spill directory.<br/>
            /// </summary>
            /// <param name="kind">Short run-kind label used only for diagnostics and cleanup inspection.<br/></param>
            /// <returns>Unused absolute temporary file path.<br/></returns>
            private string CreateRunPath(string kind)
            {
                string baseDirectory = string.IsNullOrWhiteSpace(configuredBaseDirectory)
                    ? throw new InvalidOperationException("The scalar spill directory was not resolved at operation start.")
                    : Path.GetFullPath(configuredBaseDirectory);
                Directory.CreateDirectory(baseDirectory);
                string path = Path.Combine(baseDirectory, $"{filePrefix}.{kind}-{pathOrdinal++:D6}.ss88run.tmp");
                createdPaths.Add(path);
                return path;
            }

            /// <summary>
            /// Compares two encoded tuples in the exact global order required by the native builder.<br/>
            /// </summary>
            private static int CompareTuples(Scalar8Scalar8SortedTuple left, Scalar8Scalar8SortedTuple right)
            {
                int keyOrder = left.Key.CompareTo(right.Key);
                return keyOrder != 0 ? keyOrder : left.Identity.CompareTo(right.Identity);
            }

            /// <summary>
            /// Writes one encoded tuple into the fixed-width temporary representation.<br/>
            /// </summary>
            private static void WriteTuple(Span<byte> destination, Scalar8Scalar8SortedTuple tuple)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(destination, tuple.Key);
                BinaryPrimitives.WriteUInt64LittleEndian(destination[sizeof(ulong)..], tuple.Identity);
            }

            /// <summary>
            /// Reads one encoded tuple from the fixed-width temporary representation.<br/>
            /// </summary>
            private static Scalar8Scalar8SortedTuple ReadTuple(ReadOnlySpan<byte> source)
                => new(
                    BinaryPrimitives.ReadUInt64LittleEndian(source),
                    BinaryPrimitives.ReadUInt64LittleEndian(source[sizeof(ulong)..]));

            /// <summary>
            /// Rejects mutation after source completion and every operation after disposal.<br/>
            /// </summary>
            private void ThrowIfUnavailable()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (completed)
                    throw new InvalidOperationException("The scalar backfill spill source has already been completed.");
            }

            /// <summary>
            /// Stores one merge-queue head together with the reader that will produce its successor.<br/>
            /// </summary>
            private readonly record struct Scalar8Scalar8MergeItem(
                int ReaderIndex,
                Scalar8Scalar8SortedTuple Tuple);

            /// <summary>
            /// Reads one sorted fixed-width run sequentially with a buffered file stream.<br/>
            /// </summary>
            private sealed class Scalar8Scalar8RunReader : IDisposable
            {
                private readonly FileStream stream;

                /// <summary>
                /// Opens one immutable sorted run for sequential merge consumption.<br/>
                /// </summary>
                /// <param name="path">Existing run path owned by the surrounding spill helper.<br/></param>
                internal Scalar8Scalar8RunReader(string path)
                {
                    stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 64 * 1024,
                        FileOptions.SequentialScan);
                }

                /// <summary>
                /// Reads the next complete fixed-width tuple or reports clean end-of-run.<br/>
                /// </summary>
                /// <param name="tuple">Decoded encoded tuple when available.<br/></param>
                /// <returns><see langword="true"/> for one tuple; <see langword="false"/> only at a clean record boundary.<br/></returns>
                internal bool TryRead(out Scalar8Scalar8SortedTuple tuple)
                {
                    Span<byte> bytes = stackalloc byte[TupleSize];
                    int read = stream.Read(bytes);
                    if (read == 0)
                    {
                        tuple = default;
                        return false;
                    }
                    while (read < TupleSize)
                    {
                        int next = stream.Read(bytes[read..]);
                        if (next == 0)
                            throw new InvalidDataException("A scalar backfill spill run ended inside a fixed-width tuple.");
                        read += next;
                    }
                    tuple = ReadTuple(bytes);
                    return true;
                }

                /// <summary>Closes the run reader.<br/></summary>
                public void Dispose() => stream.Dispose();
            }
        }

        /// <summary>
        /// Owns compact encoded variable-key tuples in pooled fixed-size byte slabs and 16-byte offset/length/identity descriptors.<br/>
        /// No tuple owns a separate key array, and clearing a completed run returns its slabs and oversized descriptor array to their pools.<br/>
        /// </summary>
        /// <summary>
        /// Projects one key-homogeneous ordinal range from a bounded scalar tuple source as an identity-only list without copying identities.<br/>
        /// The adapter lets LibraDex stream null and empty string key-state identities from the same RAM-first/spill-controlled machinery used by ordinary scalar tuples.<br/>
        /// </summary>
        private sealed class Scalar8IdentityListRange : IReadOnlyList<ulong>
        {
            private readonly IScalar8Scalar8SortedTupleSource source;
            private readonly int start;
            private readonly ulong expectedKey;

            /// <summary>
            /// Initializes one immutable identity projection over a validated ordinal range.<br/>
            /// </summary>
            /// <param name="source">Stable sorted scalar tuple source that remains alive while LibraDex consumes this view.<br/></param>
            /// <param name="start">First tuple ordinal included in the view.<br/></param>
            /// <param name="count">Number of identities included in the view.<br/></param>
            /// <param name="expectedKey">Sentinel discriminator required on every tuple in the range.<br/></param>
            internal Scalar8IdentityListRange(
                IScalar8Scalar8SortedTupleSource source,
                int start,
                int count,
                ulong expectedKey)
            {
                ArgumentNullException.ThrowIfNull(source);
                if (start < 0 || count < 0 || start > source.Count - count)
                    throw new ArgumentOutOfRangeException(nameof(count), "The scalar identity projection must remain inside its stable source.");
                this.source = source;
                this.start = start;
                Count = count;
                this.expectedKey = expectedKey;
            }

            /// <inheritdoc/>
            public int Count { get; }

            /// <inheritdoc/>
            public ulong this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)Count)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    Scalar8Scalar8SortedTuple tuple = source.GetTuple(checked(start + index));
                    if (tuple.Key != expectedKey)
                        throw new InvalidDataException($"The bounded sentinel source returned discriminator {tuple.Key} where {expectedKey} was required.");
                    return tuple.Identity;
                }
            }

            /// <inheritdoc/>
            public IEnumerator<ulong> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                    yield return this[i];
            }

            /// <inheritdoc/>
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class VarKeyScalar8TupleArena : IVarKeyScalar8SortedTupleSource, IDisposable
        {
            private const int SlabSize = 64 * 1024;
            private const int InitialDescriptorCapacity = 256;
            private readonly List<byte[]> slabs = new();
            private VarKeyScalar8ArenaTuple[] descriptors = ArrayPool<VarKeyScalar8ArenaTuple>.Shared.Rent(InitialDescriptorCapacity);
            private int activeSlabIndex = -1;
            private int activeSlabUsed;
            private bool disposed;

            /// <inheritdoc/>
            public int Count { get; private set; }

            /// <summary>Gets pooled array capacity currently retained by this arena.<br/></summary>
            internal long RetainedBytes
            {
                get
                {
                    long bytes = checked((long)descriptors.Length * 16);
                    for (int i = 0; i < slabs.Count; i++)
                        bytes = checked(bytes + slabs[i].Length);
                    return bytes;
                }
            }

            /// <summary>
            /// Copies one already encoded key into the current pooled slab and appends its compact descriptor.<br/>
            /// </summary>
            /// <param name="key">Non-empty encoded key whose length does not exceed the `VS8` physical maximum.<br/></param>
            /// <param name="identity">Encoded scalar identity paired with the key.<br/></param>
            internal void Add(ReadOnlySpan<byte> key, ulong identity)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (key.Length <= 0 || key.Length > 1024)
                    throw new ArgumentOutOfRangeException(nameof(key), key.Length, "A compact VS8 tuple key must contain 1 through 1,024 bytes.");
                EnsureDescriptorCapacity(checked(Count + 1));
                if (activeSlabIndex < 0 || slabs[activeSlabIndex].Length - activeSlabUsed < key.Length)
                {
                    byte[] slab = ArrayPool<byte>.Shared.Rent(SlabSize);
                    slabs.Add(slab);
                    activeSlabIndex = slabs.Count - 1;
                    activeSlabUsed = 0;
                }

                key.CopyTo(slabs[activeSlabIndex].AsSpan(activeSlabUsed, key.Length));
                descriptors[Count++] = new VarKeyScalar8ArenaTuple(
                    identity,
                    activeSlabIndex,
                    checked((ushort)activeSlabUsed),
                    checked((ushort)key.Length));
                activeSlabUsed += key.Length;
            }

            /// <inheritdoc/>
            public ReadOnlySpan<byte> GetKey(int index)
            {
                VarKeyScalar8ArenaTuple tuple = GetDescriptor(index);
                return slabs[tuple.SlabIndex].AsSpan(tuple.Offset, tuple.Length);
            }

            /// <inheritdoc/>
            public ulong GetIdentity(int index) => GetDescriptor(index).Identity;

            /// <inheritdoc/>
            public int CompareKeys(int leftIndex, int rightIndex)
            {
                VarKeyScalar8ArenaTuple left = GetDescriptor(leftIndex);
                VarKeyScalar8ArenaTuple right = GetDescriptor(rightIndex);
                return GetKey(left).SequenceCompareTo(GetKey(right));
            }

            /// <summary>Orders descriptors by encoded key then identity without moving key payload bytes.<br/></summary>
            internal void Sort()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (Count > 1)
                    descriptors.AsSpan(0, Count).Sort(new VarKeyScalar8ArenaTupleComparer(this));
            }

            /// <summary>
            /// Releases every retained key slab and resets the descriptor population for another bounded page or run.<br/>
            /// Oversized descriptor storage is returned so aggregate accounting reflects the memory actually retained by the active source.<br/>
            /// </summary>
            internal void Clear()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                for (int i = 0; i < slabs.Count; i++)
                    ArrayPool<byte>.Shared.Return(slabs[i]);
                slabs.Clear();
                activeSlabIndex = -1;
                activeSlabUsed = 0;
                Count = 0;
                if (descriptors.Length > InitialDescriptorCapacity * 2)
                {
                    ArrayPool<VarKeyScalar8ArenaTuple>.Shared.Return(descriptors);
                    descriptors = ArrayPool<VarKeyScalar8ArenaTuple>.Shared.Rent(InitialDescriptorCapacity);
                }
            }

            /// <summary>Returns all pooled storage owned by this arena.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                Clear();
                disposed = true;
                ArrayPool<VarKeyScalar8ArenaTuple>.Shared.Return(descriptors);
                descriptors = [];
            }

            /// <summary>Ensures one descriptor slot without allocating per tuple.<br/></summary>
            private void EnsureDescriptorCapacity(int required)
            {
                if (required <= descriptors.Length)
                    return;
                int capacity = Math.Max(required, checked(descriptors.Length * 2));
                VarKeyScalar8ArenaTuple[] replacement = ArrayPool<VarKeyScalar8ArenaTuple>.Shared.Rent(capacity);
                descriptors.AsSpan(0, Count).CopyTo(replacement);
                ArrayPool<VarKeyScalar8ArenaTuple>.Shared.Return(descriptors);
                descriptors = replacement;
            }

            /// <summary>Returns one validated compact descriptor.<br/></summary>
            private VarKeyScalar8ArenaTuple GetDescriptor(int index)
            {
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return descriptors[index];
            }

            /// <summary>Returns one descriptor key without ordinal validation inside sort comparisons.<br/></summary>
            private ReadOnlySpan<byte> GetKey(VarKeyScalar8ArenaTuple tuple)
                => slabs[tuple.SlabIndex].AsSpan(tuple.Offset, tuple.Length);

            /// <summary>Stores one 16-byte arena descriptor.<br/></summary>
            private readonly record struct VarKeyScalar8ArenaTuple(
                ulong Identity,
                int SlabIndex,
                ushort Offset,
                ushort Length);

            /// <summary>Compares arena descriptors without key allocation.<br/></summary>
            private sealed class VarKeyScalar8ArenaTupleComparer : IComparer<VarKeyScalar8ArenaTuple>
            {
                private readonly VarKeyScalar8TupleArena owner;

                internal VarKeyScalar8ArenaTupleComparer(VarKeyScalar8TupleArena owner) => this.owner = owner;

                /// <inheritdoc/>
                public int Compare(VarKeyScalar8ArenaTuple left, VarKeyScalar8ArenaTuple right)
                {
                    int keyOrder = owner.GetKey(left).SequenceCompareTo(owner.GetKey(right));
                    return keyOrder != 0 ? keyOrder : left.Identity.CompareTo(right.Identity);
                }
            }
        }

        /// <summary>
        /// Produces bounded sorted encoded `VS8` tuple runs, merges them with bounded fan-in, and exposes the final variable-width stream through one tuple-page cache.<br/>
        /// Each record stores key length, scalar identity, and already encoded key bytes; a fixed-width sidecar stores ordinal offsets so recursive LibraDex planning remains seekable without retaining the full string population.<br/>
        /// The helper owns only its GUID-qualified sibling spill files and removes them after successful build or grouped-backfill rollback.<br/>
        /// </summary>
        private sealed class VarKeyScalar8BackfillSpill : IVarKeyScalar8SortedTupleSource, IDisposable
        {
            private const int RecordHeaderSize = sizeof(int) + sizeof(ulong);
            private const int PageTupleCount = 4_096;
            private readonly int runCapacity;
            private readonly int mergeFanIn;
            private readonly string? configuredBaseDirectory;
            private readonly string filePrefix;
            private readonly VarKeyScalar8TupleArena buffered = new();
            private readonly List<string> runPaths = new();
            private readonly List<string> createdPaths = new();
            private bool finalInMemory;
            private VarKeyScalar8TupleArena? finalPage;
            private FileStream? finalStream;
            private FileStream? finalOffsets;
            private int finalPageStart = -1;
            private int finalPageCount;
            private int pathOrdinal;
            private bool completed;
            private bool disposed;

            /// <summary>
            /// Initializes one bounded run producer for encoded variable-key/scalar-identity tuples.<br/>
            /// </summary>
            /// <param name="runCapacity">Maximum tuples retained before one sorted run is spilled.<br/></param>
            /// <param name="mergeFanIn">Maximum sequential run readers admitted to one merge group.<br/></param>
            /// <param name="configuredBaseDirectory">Resolved spill directory; normally the directory containing the LibraDex catalog, or the caller's explicit override.<br/></param>
            /// <param name="filePrefix">Non-empty operation-and-target filename prefix used to distinguish this spill producer's temporary run files.<br/></param>
            internal VarKeyScalar8BackfillSpill(
                int runCapacity,
                int mergeFanIn,
                string? configuredBaseDirectory,
                string filePrefix)
            {
                if (runCapacity < 1)
                    throw new ArgumentOutOfRangeException(nameof(runCapacity));
                if (mergeFanIn < 2)
                    throw new ArgumentOutOfRangeException(nameof(mergeFanIn));
                this.runCapacity = runCapacity;
                this.mergeFanIn = mergeFanIn;
                this.configuredBaseDirectory = configuredBaseDirectory;
                this.filePrefix = string.IsNullOrWhiteSpace(filePrefix)
                    ? throw new ArgumentException("A variable-key spill file prefix is required.", nameof(filePrefix))
                    : filePrefix;
            }

            /// <inheritdoc/>
            public int Count { get; private set; }

            /// <summary>Gets the current managed run population.<br/></summary>
            internal int BufferedCount => buffered.Count;

            /// <summary>Gets pooled descriptor and key-slab bytes retained by the current run.<br/></summary>
            internal long BufferedBytes => buffered.RetainedBytes;

            /// <summary>Gets the largest managed tuple run retained by this helper.<br/></summary>
            internal int PeakBufferedCount { get; private set; }

            /// <summary>Gets the number of initial sorted runs written.<br/></summary>
            internal int SortedRunCount { get; private set; }

            /// <summary>Gets cumulative encoded tuple bytes written across runs, merge outputs, and the final ordinal-offset sidecar.<br/></summary>
            internal long SpillBytes { get; private set; }

            /// <summary>Gets the number of bounded merge passes completed.<br/></summary>
            internal int MergePassCount { get; private set; }

            /// <summary>Gets the largest merge-head queue observed.<br/></summary>
            internal int PeakMergeQueueCount { get; private set; }

            /// <summary>Gets timestamp ticks spent sorting, writing, merging, and creating ordinal offsets.<br/></summary>
            internal long PreparationTicks { get; private set; }

            /// <summary>
            /// Appends one already encoded ordinary tuple to the active bounded run.<br/>
            /// </summary>
            /// <param name="key">Borrowed encoded key copied into the compact arena.<br/></param>
            /// <param name="identity">Authoritative scalar identity.<br/></param>
            internal void Add(ReadOnlySpan<byte> key, ulong identity)
            {
                ThrowIfUnavailable();
                if (key.Length == 0)
                    throw new ArgumentException("The VS8 spill accepts only non-empty encoded ordinary keys.", nameof(key));
                buffered.Add(key, identity);
                Count = checked(Count + 1);
                PeakBufferedCount = Math.Max(PeakBufferedCount, buffered.Count);
            }

            /// <summary>
            /// Sorts and spills the active run when it reaches the configured bound.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before and during run creation.<br/></param>
            internal void SpillIfFull(CancellationToken cancellationToken)
            {
                if (buffered.Count >= runCapacity)
                    SpillCurrent(cancellationToken);
            }

            /// <summary>
            /// Finalizes a stable seekable source, retaining a sorted array only when no spill was required and otherwise producing one merged run plus ordinal-offset sidecar.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed through sorting, merge, and sidecar construction.<br/></param>
            internal void Complete(CancellationToken cancellationToken)
            {
                ThrowIfUnavailable();
                long started = Stopwatch.GetTimestamp();
                try
                {
                    if (runPaths.Count == 0)
                    {
                        SortBuffered();
                        finalInMemory = true;
                        completed = true;
                        return;
                    }

                    SpillCurrentRun(cancellationToken);
                    string mergedPath = MergeRuns(cancellationToken);
                    string offsetsPath = CreateRunPath("offsets");
                    BuildOrdinalOffsets(mergedPath, offsetsPath, cancellationToken);
                    finalStream = new FileStream(mergedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
                    finalOffsets = new FileStream(offsetsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
                    finalPage = new VarKeyScalar8TupleArena();
                    completed = true;
                }
                finally
                {
                    PreparationTicks += Stopwatch.GetTimestamp() - started;
                }
            }

            /// <inheritdoc/>
            public ReadOnlySpan<byte> GetKey(int index)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!completed)
                    throw new InvalidOperationException("The VS8 spill source must be completed before ordinal reads begin.");
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                if (finalInMemory)
                    return buffered.GetKey(index);
                if (index < finalPageStart || index >= finalPageStart + finalPageCount)
                    LoadFinalPage(index);
                return finalPage!.GetKey(index - finalPageStart);
            }

            /// <inheritdoc/>
            public ulong GetIdentity(int index)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!completed)
                    throw new InvalidOperationException("The VS8 spill source must be completed before ordinal reads begin.");
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                if (finalInMemory)
                    return buffered.GetIdentity(index);
                if (index < finalPageStart || index >= finalPageStart + finalPageCount)
                    LoadFinalPage(index);
                return finalPage!.GetIdentity(index - finalPageStart);
            }

            /// <inheritdoc/>
            public int CompareKeys(int leftIndex, int rightIndex)
            {
                if (finalInMemory)
                    return buffered.CompareKeys(leftIndex, rightIndex);
                Span<byte> left = stackalloc byte[1024];
                ReadOnlySpan<byte> leftKey = GetKey(leftIndex);
                int leftLength = leftKey.Length;
                leftKey.CopyTo(left);
                return left[..leftLength].SequenceCompareTo(GetKey(rightIndex));
            }

            /// <summary>Closes readers and removes every temporary run owned by this helper.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                finalStream?.Dispose();
                finalOffsets?.Dispose();
                finalPage?.Dispose();
                finalPage = null;
                buffered.Dispose();
                for (int i = 0; i < createdPaths.Count; i++)
                {
                    try
                    {
                        if (File.Exists(createdPaths[i]))
                            File.Delete(createdPaths[i]);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            /// <summary>
            /// Sorts and writes the current non-empty managed run.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before sorting and periodically while writing.<br/></param>
            internal void SpillCurrent(CancellationToken cancellationToken)
            {
                if (buffered.Count == 0)
                    return;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    SpillCurrentRun(cancellationToken);
                }
                finally
                {
                    PreparationTicks += Stopwatch.GetTimestamp() - started;
                }
            }

            /// <summary>
            /// Sorts and writes the active tuple run without changing aggregate preparation timing.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed at bounded write intervals.<br/></param>
            private void SpillCurrentRun(CancellationToken cancellationToken)
            {
                if (buffered.Count == 0)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                SortBuffered();
                string path = CreateRunPath("run");
                long bytes = WriteRun(path, buffered, cancellationToken);
                runPaths.Add(path);
                SortedRunCount++;
                SpillBytes = checked(SpillBytes + bytes);
                buffered.Clear();
            }

            /// <summary>Sorts the active run in exact encoded key-then-identity order.<br/></summary>
            private void SortBuffered()
            {
                if (buffered.Count > 1)
                    buffered.Sort();
            }

            /// <summary>
            /// Repeatedly merges bounded groups of runs until one final globally sorted stream remains.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed before each merge group.<br/></param>
            /// <returns>Path of the one final sorted run.<br/></returns>
            private string MergeRuns(CancellationToken cancellationToken)
            {
                List<string> current = new(runPaths);
                while (current.Count > 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var next = new List<string>((current.Count + mergeFanIn - 1) / mergeFanIn);
                    for (int start = 0; start < current.Count; start += mergeFanIn)
                    {
                        int count = Math.Min(mergeFanIn, current.Count - start);
                        if (count == 1)
                        {
                            next.Add(current[start]);
                            continue;
                        }
                        string output = CreateRunPath("merge");
                        long bytes = MergeRunGroup(current.GetRange(start, count), output, cancellationToken);
                        SpillBytes = checked(SpillBytes + bytes);
                        next.Add(output);
                    }
                    current = next;
                    MergePassCount++;
                }
                return current[0];
            }

            /// <summary>
            /// Merges one bounded group using only one decoded tuple per input reader.<br/>
            /// </summary>
            /// <param name="inputs">Sorted run paths bounded by the configured fan-in.<br/></param>
            /// <param name="outputPath">New merged run path.<br/></param>
            /// <param name="cancellationToken">Cancellation observed periodically while emitting tuples.<br/></param>
            /// <returns>Encoded bytes written to the merged output.<br/></returns>
            private long MergeRunGroup(IReadOnlyList<string> inputs, string outputPath, CancellationToken cancellationToken)
            {
                var readers = new VarKeyScalar8RunReader[inputs.Count];
                try
                {
                    for (int i = 0; i < inputs.Count; i++)
                        readers[i] = new VarKeyScalar8RunReader(inputs[i]);
                    var queue = new PriorityQueue<int, int>(new VarKeyScalar8RunReaderPriorityComparer(readers));
                    for (int i = 0; i < readers.Length; i++)
                        if (readers[i].TryRead())
                            queue.Enqueue(i, i);
                    PeakMergeQueueCount = Math.Max(PeakMergeQueueCount, queue.Count);
                    long bytesWritten = 0;
                    long tupleOrdinal = 0;
                    using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
                    while (queue.TryDequeue(out int readerIndex, out _))
                    {
                        if ((tupleOrdinal++ & 0x0FFF) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        VarKeyScalar8RunReader reader = readers[readerIndex];
                        bytesWritten = checked(bytesWritten + WriteTuple(output, reader.Key, reader.Identity));
                        if (reader.TryRead())
                            queue.Enqueue(readerIndex, readerIndex);
                    }
                    output.Flush(flushToDisk: false);
                    return bytesWritten;
                }
                finally
                {
                    for (int i = 0; i < readers.Length; i++)
                        readers[i]?.Dispose();
                }
            }

            /// <summary>
            /// Writes one sorted managed run in the variable-width spill representation.<br/>
            /// </summary>
            /// <param name="path">New run path.<br/></param>
            /// <param name="tuples">Sorted encoded tuples.<br/></param>
            /// <param name="cancellationToken">Cancellation observed periodically while writing.<br/></param>
            /// <returns>Total encoded bytes written.<br/></returns>
            private static long WriteRun(string path, IVarKeyScalar8SortedTupleSource tuples, CancellationToken cancellationToken)
            {
                long bytesWritten = 0;
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
                for (int i = 0; i < tuples.Count; i++)
                {
                    if ((i & 0x0FFF) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    bytesWritten = checked(bytesWritten + WriteTuple(stream, tuples.GetKey(i), tuples.GetIdentity(i)));
                }
                stream.Flush(flushToDisk: false);
                return bytesWritten;
            }

            /// <summary>
            /// Writes one span-backed variable-width tuple and returns its exact record length.<br/>
            /// </summary>
            /// <param name="stream">Destination run stream.<br/></param>
            /// <param name="key">Borrowed encoded key bytes.<br/></param>
            /// <param name="identity">Encoded scalar identity.<br/></param>
            /// <returns>Header plus key payload byte count.<br/></returns>
            private static int WriteTuple(Stream stream, ReadOnlySpan<byte> key, ulong identity)
            {
                Span<byte> header = stackalloc byte[RecordHeaderSize];
                BinaryPrimitives.WriteInt32LittleEndian(header, key.Length);
                BinaryPrimitives.WriteUInt64LittleEndian(header[sizeof(int)..], identity);
                stream.Write(header);
                stream.Write(key);
                return checked(RecordHeaderSize + key.Length);
            }

            /// <summary>
            /// Builds a fixed-width ordinal-offset sidecar for one final sorted variable-width run.<br/>
            /// </summary>
            /// <param name="runPath">Final immutable tuple run.<br/></param>
            /// <param name="offsetsPath">New sidecar path receiving one little-endian Int64 offset per tuple.<br/></param>
            /// <param name="cancellationToken">Cancellation observed periodically during the sequential scan.<br/></param>
            private void BuildOrdinalOffsets(string runPath, string offsetsPath, CancellationToken cancellationToken)
            {
                using var input = new FileStream(runPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                using var output = new FileStream(offsetsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
                Span<byte> header = stackalloc byte[RecordHeaderSize];
                Span<byte> offsetBytes = stackalloc byte[sizeof(long)];
                long logicalOffset = 0;
                for (int i = 0; i < Count; i++)
                {
                    if ((i & 0x0FFF) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    BinaryPrimitives.WriteInt64LittleEndian(offsetBytes, logicalOffset);
                    output.Write(offsetBytes);
                    input.ReadExactly(header);
                    int keyLength = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (keyLength <= 0 || keyLength > 1024)
                        throw new InvalidDataException($"The VS8 spill run contains invalid key length {keyLength:N0} at ordinal {i:N0}.");
                    input.Seek(keyLength, SeekOrigin.Current);
                    logicalOffset = checked(logicalOffset + RecordHeaderSize + keyLength);
                }
                if (logicalOffset != input.Length)
                    throw new InvalidDataException($"The VS8 spill run length {input.Length:N0} does not match the indexed tuple boundary {logicalOffset:N0}.");
                output.Flush(flushToDisk: false);
                SpillBytes = checked(SpillBytes + (long)Count * sizeof(long));
            }

            /// <summary>
            /// Loads the bounded tuple page containing one requested ordinal using the offset sidecar for its first physical byte position.<br/>
            /// </summary>
            /// <param name="index">Requested tuple ordinal.<br/></param>
            private void LoadFinalPage(int index)
            {
                if (finalStream is null || finalOffsets is null || finalPage is null)
                    throw new ObjectDisposedException(nameof(VarKeyScalar8BackfillSpill));
                finalPageStart = index - index % PageTupleCount;
                finalPageCount = Math.Min(PageTupleCount, Count - finalPageStart);
                Span<byte> offsetBytes = stackalloc byte[sizeof(long)];
                finalOffsets.Position = checked((long)finalPageStart * sizeof(long));
                finalOffsets.ReadExactly(offsetBytes);
                finalStream.Position = BinaryPrimitives.ReadInt64LittleEndian(offsetBytes);
                finalPage.Clear();
                for (int i = 0; i < finalPageCount; i++)
                    ReadTuple(finalStream, finalPage);
            }

            /// <summary>
            /// Reads one variable-width tuple directly into a compact page arena without allocating a key array.<br/>
            /// </summary>
            /// <param name="stream">Run stream positioned at a tuple header.<br/></param>
            /// <param name="arena">Page arena receiving the decoded key and identity.<br/></param>
            private static void ReadTuple(Stream stream, VarKeyScalar8TupleArena arena)
            {
                Span<byte> header = stackalloc byte[RecordHeaderSize];
                stream.ReadExactly(header);
                int keyLength = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (keyLength <= 0 || keyLength > 1024)
                    throw new InvalidDataException($"A VS8 spill tuple declares invalid key length {keyLength:N0}.");
                Span<byte> key = stackalloc byte[keyLength];
                stream.ReadExactly(key);
                arena.Add(key, BinaryPrimitives.ReadUInt64LittleEndian(header[sizeof(int)..]));
            }

            /// <summary>
            /// Creates one unique temporary path below this spill owner's exact directory.<br/>
            /// </summary>
            /// <param name="kind">Short diagnostic run kind.<br/></param>
            /// <returns>Unused absolute file path.<br/></returns>
            private string CreateRunPath(string kind)
            {
                string baseDirectory = string.IsNullOrWhiteSpace(configuredBaseDirectory)
                    ? throw new InvalidOperationException("The variable-key spill directory was not resolved at operation start.")
                    : Path.GetFullPath(configuredBaseDirectory);
                Directory.CreateDirectory(baseDirectory);
                string path = Path.Combine(baseDirectory, $"{filePrefix}.{kind}-{pathOrdinal++:D6}.vs8run.tmp");
                createdPaths.Add(path);
                return path;
            }

            /// <summary>Rejects mutation after completion and every operation after disposal.<br/></summary>
            private void ThrowIfUnavailable()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (completed)
                    throw new InvalidOperationException("The VS8 backfill spill source has already been completed.");
            }

            /// <summary>Compares reusable run-reader heads by encoded key, identity, then reader ordinal.<br/></summary>
            private sealed class VarKeyScalar8RunReaderPriorityComparer : IComparer<int>
            {
                private readonly VarKeyScalar8RunReader[] readers;

                internal VarKeyScalar8RunReaderPriorityComparer(VarKeyScalar8RunReader[] readers)
                    => this.readers = readers;

                /// <inheritdoc/>
                public int Compare(int left, int right)
                {
                    int keyOrder = readers[left].Key.SequenceCompareTo(readers[right].Key);
                    if (keyOrder != 0)
                        return keyOrder;
                    int identityOrder = readers[left].Identity.CompareTo(readers[right].Identity);
                    return identityOrder != 0 ? identityOrder : left.CompareTo(right);
                }
            }

            /// <summary>Reads one sorted variable-width run sequentially.<br/></summary>
            private sealed class VarKeyScalar8RunReader : IDisposable
            {
                private readonly FileStream stream;
                private readonly byte[] keyBuffer = GC.AllocateUninitializedArray<byte>(1024);
                private int keyLength;

                /// <summary>
                /// Opens one immutable sorted run for sequential merge consumption.<br/>
                /// </summary>
                /// <param name="path">Existing run path owned by the surrounding spill helper.<br/></param>
                internal VarKeyScalar8RunReader(string path)
                {
                    stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                }

                /// <summary>
                /// Reads the next complete tuple or reports clean end-of-run.<br/>
                /// </summary>
                /// <returns><see langword="true"/> for one tuple; otherwise <see langword="false"/> at a clean boundary.<br/></returns>
                internal bool TryRead()
                {
                    if (stream.Position == stream.Length)
                    {
                        keyLength = 0;
                        return false;
                    }
                    Span<byte> header = stackalloc byte[RecordHeaderSize];
                    stream.ReadExactly(header);
                    keyLength = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (keyLength <= 0 || keyLength > keyBuffer.Length)
                        throw new InvalidDataException($"A VS8 spill tuple declares invalid key length {keyLength:N0}.");
                    Identity = BinaryPrimitives.ReadUInt64LittleEndian(header[sizeof(int)..]);
                    stream.ReadExactly(keyBuffer.AsSpan(0, keyLength));
                    return true;
                }

                /// <summary>Gets the current reusable key head.<br/></summary>
                internal ReadOnlySpan<byte> Key => keyBuffer.AsSpan(0, keyLength);

                /// <summary>Gets the current scalar identity head.<br/></summary>
                internal ulong Identity { get; private set; }

                /// <summary>Closes the run stream.<br/></summary>
                public void Dispose() => stream.Dispose();
            }
        }
    }

    /// <summary>
    /// Reports one sibling's typed extraction and publication measurements from a grouped backfill.<br/>
    /// </summary>
    /// <param name="Path">Canonical indexed property path.<br/></param>
    /// <param name="Diagnostics">Existing per-index optimized diagnostics shape.<br/></param>
    public sealed record IndexBackfillGroupIndexDiagnostics(
        string Path,
        IndexBackfillDiagnostics Diagnostics);

    /// <summary>
    /// Reports aggregate measurements for one grouped multi-index backfill.<br/>
    /// Materialization count is expected to equal authoritative records read regardless of sibling index count.<br/>
    /// </summary>
    /// <param name="RecordsRead">Authoritative Fractal records scanned once.<br/></param>
    /// <param name="InhetoMaterializationCount">Record-local Inheto views created.<br/></param>
    /// <param name="IndexCount">Sibling physical indexes populated.<br/></param>
    /// <param name="TotalTupleCount">Sum of admitted tuples across every sibling.<br/></param>
    /// <param name="ExtractionTime">Time spent acquiring payloads and extracting every sibling key.<br/></param>
    /// <param name="SortTime">Sum of per-index bounded-sort time.<br/></param>
    /// <param name="InsertTime">Sum of typed insertion time.<br/></param>
    /// <param name="PublishTime">Sum of identity-group publication time.<br/></param>
    /// <param name="TotalTime">Complete grouped population time before descriptor attachment.<br/></param>
    /// <param name="PeakBufferedTupleCount">Largest aggregate queued tuple count observed after one record's keys were extracted.<br/></param>
        /// <param name="MaximumBufferedTupleCapacity">Sum of per-target pathological tuple ceilings; ordinary retained memory is governed by <see cref="EffectiveMemoryBudgetBytes"/>.<br/></param>
    /// <param name="PublishedBatchCount">Total non-empty sibling chunk publications.<br/></param>
    /// <param name="Indexes">Per-index measurements in developer declaration order.<br/></param>
    public sealed record IndexBackfillGroupDiagnostics(
        int RecordsRead,
        int InhetoMaterializationCount,
        int IndexCount,
        int TotalTupleCount,
        TimeSpan ExtractionTime,
        TimeSpan SortTime,
        TimeSpan InsertTime,
        TimeSpan PublishTime,
        TimeSpan TotalTime,
        int PeakBufferedTupleCount,
        int MaximumBufferedTupleCapacity,
        int PublishedBatchCount,
        IReadOnlyList<IndexBackfillGroupIndexDiagnostics> Indexes)
    {
        /// <summary>Gets the number of initial sorted spill runs produced across all sibling indexes.<br/></summary>
        public int SortedRunCount { get; init; }

        /// <summary>Gets cumulative temporary tuple bytes written across initial runs and merge outputs for all siblings.<br/></summary>
        public long SpillBytes { get; init; }

        /// <summary>Gets the sum of bounded k-way merge passes completed by sibling indexes.<br/></summary>
        public int MergePassCount { get; init; }

        /// <summary>Gets the largest run-head queue observed by any sibling index.<br/></summary>
        public int PeakMergeQueueCount { get; init; }

        /// <summary>Gets the explicit developer memory budget, or null when automatic policy was selected.<br/></summary>
        public long? ConfiguredMemoryBudgetBytes { get; init; }

        /// <summary>Gets the final effective aggregate memory budget after any allowed pressure contraction.<br/></summary>
        public long EffectiveMemoryBudgetBytes { get; init; }

        /// <summary>Gets whether the memory budget was selected automatically.<br/></summary>
        public bool AutomaticMemoryBudget { get; init; }

        /// <summary>Gets the per-target maximum tuple count for one sorted run.<br/></summary>
        public int MaximumSortRunTupleCount { get; init; }

        /// <summary>Gets the largest aggregate conservatively accounted tuple-buffer allocation observed.<br/></summary>
        public long PeakBufferedBytes { get; init; }

        /// <summary>Gets the first bound that required a run or bounded publication, when any.<br/></summary>
        public string? FirstSpillTrigger { get; init; }

        /// <summary>Gets the resolved spill directory for this operation, whether or not a run was required.<br/></summary>
        public string? SpillDirectory { get; init; }
    }
}
