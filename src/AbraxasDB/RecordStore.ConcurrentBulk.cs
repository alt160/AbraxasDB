using Inheto;
using LibraDex;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Attempts one coordinated new-record bulk write whose authoritative Fractal payload mutations execute through Fractal's native multi-worker <c>PutMany</c> path.<br/>
        /// This route supports ordinary concurrent-insert indexes and routed composite indexes; State values, registered links, lifecycle callbacks, and unsupported derived indexes still require per-record work.<br/>
        /// The existing store-generation mutation scope remains held once around the complete bulk operation, excluding schema/lifecycle changes and other Abraxas writers while Fractal's internal workers safely mutate independent identities.<br/>
        /// IDs are allocated and payloads are serialized in source order before the physical write begins, so preparation failure cannot leave a partially submitted Fractal batch.<br/>
        /// The applied-source-USN proof is cleared before physical mutation and published once after the complete Fractal batch succeeds, avoiding the false convergence frontier produced by overlapping per-record checkpoint publication.<br/>
        /// </summary>
        /// <param name="source">Random-access source whose first <paramref name="recordCount"/> entries are written.<br/></param>
        /// <param name="recordCount">Exact number of source entries to write.<br/></param>
        /// <param name="state">Optional State value; a non-null value currently requires the ordinary serialized path.<br/></param>
        /// <param name="options">Optional call-scoped physical worker and progress controls.<br/></param>
        /// <param name="result">Receives generated identities and the completed indexing handle when the concurrent physical route is selected.<br/></param>
        /// <returns><see langword="true"/> when the coordinated Fractal route completed; otherwise <see langword="false"/> and the caller must use the ordinary path.<br/></returns>
        private bool TryPutManyFastConcurrentFractal(
            IReadOnlyList<T> source,
            int recordCount,
            long? state,
            PutManyFastOptions? options,
            out PutManyFastResult result)
        {
            result = default;
            if (recordCount == 0 ||
                state.HasValue ||
                Links.Count != 0 ||
                RecordPersisting is not null ||
                RecordPersisted is not null)
            {
                return false;
            }

            var config = rbase.config?.FractalStoreConfig;
            int threshold = config?.PutManyParallelMinRecordCount ?? int.MaxValue;
            int workerCount = ResolvePutManyFastWorkerCount(options);
            bool explicitWorkerSelection = options?.MaximumDegreeOfParallelism.HasValue == true;
            if (recordCount < threshold && !explicitWorkerSelection)
                return false;

            PutManyFastProgressReporter? progress = PutManyFastProgressReporter.Create(options, recordCount, workerCount);

            rbase.EnsureWriteAllowed(nameof(PutManyFast));
            EnsureFunctionIndexProjectionsAvailable(nameof(PutManyFast));
            IDisposable lifecycle = EnterStoreOperationScope();
            StoreRuntimeState.StoreRuntimeCompatiblePreparationScope? preparation = null;
            StoreRuntimeState.StoreRuntimeCompatibleMutationScope? cohort = null;
            List<IDisposable> uniqueReservations = new();
            LibraDexSimpleIndex[] simpleIndexes;
            LibraDexCompositeIndex[] compositeIndexes;
            ulong[] ids;
            (ulong ID, Memory<byte> Data)[] writes;
            object?[][] indexKeys;
            LibraDexCompositeKey[][] compositeKeys;
            try
            {
                preparation = storeRuntime.EnterCompatibleMutationPreparationScope();

                // Recheck immutable-by-replacement runtime snapshots after admission because index creation
                // and unsupported mutations use the exclusive store-runtime lane.
                if (Links.Count != 0 || !TryPrepareConcurrentIndexCapability(out simpleIndexes, out compositeIndexes))
                    return false;

                if (preparation.StartsCohort)
                    EnsureLibraDexConvergence(nameof(PutManyFast));
                Links.ValidateWrite(plan: null);

                progress?.BeginPhase(PutManyFastRoute.CoordinatedBulk, PutManyFastPhase.PreparingRecords);
                ids = new ulong[recordCount];
                var binaries = new InhetoBinary[recordCount];
                writes = new (ulong ID, Memory<byte> Data)[recordCount];
                for (int index = 0; index < recordCount; index++)
                {
                    T record = source[index];
                    EnsureRecordNotNull(record, RecordsParameterName);
                    ulong recordID = idGen.GetRowID(storeID);
                    ids[index] = recordID;
                    ObjectIDSetter?.Invoke(record, recordID);

                    InhetoBinary binary = InhetoBinary.FromObject(record, NativeSerializationOptions);
                    binaries[index] = binary;
                    ReadOnlyMemory<byte> storedPayload = EncodeRecordPayload(recordID, binary.RawBytes.AsReadOnlyMemory);
                    writes[index] = (
                        recordID,
                        MemoryMarshal.AsMemory(storedPayload));
                    progress?.Report(index + 1);
                }
                progress?.CompletePhase(recordCount);

                progress?.BeginPhase(PutManyFastRoute.CoordinatedBulk, PutManyFastPhase.PreparingIndexKeys);
                indexKeys = PrepareConcurrentSimpleIndexKeys(simpleIndexes, binaries, ids, progress);
                (compositeKeys, object?[][] compositeReservationKeys) =
                    PrepareConcurrentCompositeIndexKeys(compositeIndexes, binaries, ids);
                progress?.CompletePhase(recordCount);
                progress?.BeginPhase(PutManyFastRoute.CoordinatedBulk, PutManyFastPhase.ReservingUniqueKeys);
                uniqueReservations.AddRange(ReserveConcurrentSimpleUniqueKeys(simpleIndexes, indexKeys, ids));
                uniqueReservations.AddRange(ReserveConcurrentCompositeUniqueKeys(
                    compositeIndexes, compositeKeys, compositeReservationKeys, ids));
                progress?.CompletePhase(recordCount);
                if (preparation.StartsCohort)
                    storeRuntime.ClearAppliedIndexSourceUsn();
                cohort = storeRuntime.AdmitCompatibleMutation(preparation);
                preparation = null;
            }
            finally
            {
                preparation?.Dispose();
                if (cohort is null)
                {
                    DisposeCompatibleUniqueReservations(uniqueReservations);
                    lifecycle.Dispose();
                }
            }

            Exception? physicalFailure = null;
            Exception? authoritativeFailure = null;
            bool requiresRepair = false;
            try
            {
                progress?.BeginPhase(PutManyFastRoute.CoordinatedBulk, PutManyFastPhase.WritingFractal);
                    rbase.data.PutMany(
                        writes,
                        recordCount,
                        maximumDegreeOfParallelism: workerCount,
                    progress: physical => progress?.Report(physical.ProcessedCount));
                progress?.CompletePhase(recordCount);
            }
            catch (Exception exception)
            {
                authoritativeFailure = exception;
                physicalFailure = exception;
                requiresRepair = true;
            }

            if (authoritativeFailure is null)
            {
                try
                {
                    ApplyConcurrentSimpleIndexBatches(simpleIndexes, indexKeys, ids, progress);
                    ApplyConcurrentCompositeIndexBatches(compositeIndexes, compositeKeys, ids, progress);
                }
                catch (Exception exception)
                {
                    physicalFailure = exception;
                    requiresRepair = true;
                }
            }

            Exception? finalizationFailure;
            try
            {
                progress?.BeginPhase(PutManyFastRoute.CoordinatedBulk, PutManyFastPhase.PublishingConvergence);
                finalizationFailure = cohort.Complete(
                    requiresRepair,
                    physicalFailure,
                    FinalizeCompatibleMutationCohort);
            }
            finally
            {
                try
                {
                    DisposeCompatibleUniqueReservations(uniqueReservations);
                }
                finally
                {
                    lifecycle.Dispose();
                }
            }

            if (finalizationFailure is not null)
                ExceptionDispatchInfo.Capture(finalizationFailure).Throw();
            if (authoritativeFailure is not null)
                ExceptionDispatchInfo.Capture(authoritativeFailure).Throw();

            progress?.CompletePhase(recordCount);
            progress?.CompleteOperation();

            var batchHandle = rbase.CreateBatchHandle(recordCount, autoComplete: true);
            result = new PutManyFastResult(Array.AsReadOnly(ids), batchHandle);
            return true;
        }

        /// <summary>
        /// Reserves every prepared non-null key belonging to a unique ordinary index until this compatible mutation finishes physical publication.<br/>
        /// Durable LibraDex preflight rejects keys that are already committed; the store-runtime reservation rejects an equal key held by another admitted writer whose tuple is not yet visible.<br/>
        /// </summary>
        /// <param name="simpleIndexes">Stable ordinary-index descriptors accepted for concurrent insertion.<br/></param>
        /// <param name="indexKeys">Index-major prepared runtime keys.<br/></param>
        /// <param name="ids">Source-aligned generated identities used for typed failure context.<br/></param>
        /// <returns>Reservations that must remain owned through physical index publication.<br/></returns>
        private IDisposable[] ReserveConcurrentSimpleUniqueKeys(
            IReadOnlyList<LibraDexSimpleIndex> simpleIndexes,
            IReadOnlyList<object?[]> indexKeys,
            IReadOnlyList<ulong> ids)
        {
            var reservations = new List<IDisposable>();
            try
            {
                for (int index = 0; index < simpleIndexes.Count; index++)
                {
                    LibraDexSimpleIndex entry = simpleIndexes[index];
                    if (!entry.Unique)
                        continue;

                    try
                    {
                        reservations.Add(storeRuntime.ReserveCompatibleUniqueKeys(
                            entry.Index.Name,
                            indexKeys[index],
                            LibraDexRuntimeKeyComparer.Instance));
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.UniqueConstraint,
                            $"Unique index '{entry.Path}' contains a key reserved by another active compatible writer.",
                            entry.Index.Name,
                            entry.Path,
                            ids.Count == 0 ? null : ids[0],
                            exception);
                    }
                }

                return reservations.ToArray();
            }
            catch
            {
                DisposeCompatibleUniqueReservations(reservations);
                throw;
            }
        }

        /// <summary>
        /// Releases compatible unique-key reservations in reverse acquisition order without allocating a disposal wrapper.<br/>
        /// </summary>
        /// <param name="reservations">Reservations acquired during serialized mutation preparation.<br/></param>
        private static void DisposeCompatibleUniqueReservations(IReadOnlyList<IDisposable> reservations)
        {
            for (int index = reservations.Count - 1; index >= 0; index--)
                reservations[index].Dispose();
        }

        /// <summary>
        /// Publishes one cohort-wide applied-source-USN proof after successful physical work, or rebuilds every maintained companion from authoritative Fractal reality after any member reported uncertainty.<br/>
        /// The shared store runtime invokes this callback only after all admitted cohort members finish, while exclusive schema, reset, lifecycle, and convergence operations remain blocked.<br/>
        /// </summary>
        /// <param name="requiresRepair">Whether any cohort member reported failed or uncertain physical maintenance.<br/></param>
        /// <param name="failure">First reported cohort failure retained for typed repair escalation.<br/></param>
        private void FinalizeCompatibleMutationCohort(bool requiresRepair, Exception? failure)
        {
            if (requiresRepair)
            {
                TryRepairLibraDexAfterFractalMutation(
                    nameof(PutManyFast),
                    recordID: null,
                    failure ?? new InvalidOperationException("A compatible mutation cohort required convergence repair without a retained physical failure."),
                    pendingStateIntentRetained: false);
                return;
            }

            SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
        }

        /// <summary>
        /// Captures stable ordinary and composite index snapshots and verifies that each ordinary index can open LibraDex's non-generic concurrent insertion bridge.<br/>
        /// Routed composite indexes use their dedicated bounded node-page batch; regex, partial, calculated, function, virtual-property, and custom-binary indexes remain on the ordinary serialized path until their membership, projection, or codec conflict contracts receive dedicated batch implementations.<br/>
        /// Capability probes open and abort empty batches before Fractal mutation, so an unsupported shape cannot be discovered after authoritative payloads have changed.<br/>
        /// </summary>
        /// <param name="simpleIndexes">Receives the immutable ordinary-index descriptor snapshot accepted for the coordinated operation.<br/></param>
        /// <param name="compositeIndexes">Receives the immutable routed-composite descriptor snapshot accepted for the coordinated operation.<br/></param>
        /// <returns><see langword="true"/> when all maintained derived targets support this first concurrent slice; otherwise <see langword="false"/>.<br/></returns>
        private bool TryPrepareConcurrentIndexCapability(
            out LibraDexSimpleIndex[] simpleIndexes,
            out LibraDexCompositeIndex[] compositeIndexes)
        {
            simpleIndexes = libraDexSimpleIndexes;
            compositeIndexes = libraDexCompositeIndexes;
            if (libraDexRegexProjectionIndexes.Length != 0 ||
                libraDexPartialIndexes.Length != 0)
            {
                return false;
            }

            for (int index = 0; index < simpleIndexes.Length; index++)
            {
                LibraDexSimpleIndex entry = simpleIndexes[index];
                if (entry.IsFunctionIndex ||
                    entry.IsVirtualProperty ||
                    entry.CalculatedReader is not null ||
                    entry.CustomKeyReader is not null ||
                    entry.CustomPayloadReader is not null)
                {
                    return false;
                }

                if (!entry.Index.TryBeginConcurrentInsertBatch(out ILibraDexConcurrentInsertBatch? probe) || probe is null)
                    return false;
                using (probe)
                    probe.Abort();
            }

            return true;
        }

        /// <summary>
        /// Extracts every maintained ordinary-index key once from the already serialized Inheto payloads and completes all uniqueness checks before Fractal mutation begins.<br/>
        /// Existing-key validation delegates to the normal Abraxas preflight, while one structural runtime-key set per unique index rejects duplicate submitted keys that are not yet visible in LibraDex.<br/>
        /// The resulting index-major matrix is reused by every physical insertion worker and avoids re-reading Inheto paths during index publication.<br/>
        /// </summary>
        /// <param name="simpleIndexes">Stable ordinary-index descriptors accepted for concurrent insertion.<br/></param>
        /// <param name="binaries">Source-aligned serialized payloads retained for the complete operation.<br/></param>
        /// <param name="ids">Source-aligned generated Abraxas identities.<br/></param>
        /// <param name="progress">Optional bounded reporter receiving record-aligned key-extraction progress.<br/></param>
        /// <returns>Index-major runtime keys aligned with <paramref name="ids"/>.<br/></returns>
        private object?[][] PrepareConcurrentSimpleIndexKeys(
            LibraDexSimpleIndex[] simpleIndexes,
            IReadOnlyList<InhetoBinary> binaries,
            IReadOnlyList<ulong> ids,
            PutManyFastProgressReporter? progress)
        {
            var keys = new object?[simpleIndexes.Length][];
            var uniqueKeys = new HashSet<object?>?[simpleIndexes.Length];
            for (int index = 0; index < simpleIndexes.Length; index++)
            {
                keys[index] = new object?[binaries.Count];
                if (simpleIndexes[index].Unique)
                    uniqueKeys[index] = new HashSet<object?>(LibraDexRuntimeKeyComparer.Instance);
            }

            for (int recordIndex = 0; recordIndex < binaries.Count; recordIndex++)
            {
                InhetoBinary binary = binaries[recordIndex];
                ulong recordID = ids[recordIndex];
                ValidateLibraDexIndexUpsert(recordID, binary);
                for (int index = 0; index < simpleIndexes.Length; index++)
                {
                    LibraDexSimpleIndex entry = simpleIndexes[index];
                    if (!TryReadLibraDexKey(binary, entry, out object? key, strict: true))
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.KeyRead,
                            $"Index '{entry.Path}' could not read its configured key during concurrent bulk preparation.",
                            entry.Index.Name,
                            entry.Path,
                            recordID);
                    }

                    ValidateLibraDexBinaryKeyLength(entry, key, recordID);
                    keys[index][recordIndex] = key;
                    if (key is not null && uniqueKeys[index] is { } seen && !seen.Add(key))
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.UniqueConstraint,
                            $"Unique index '{entry.Path}' contains the same non-null key more than once in the submitted concurrent batch.",
                            entry.Index.Name,
                            entry.Path,
                            recordID);
                    }
                }

                progress?.Report(recordIndex + 1);
            }

            return keys;
        }

        /// <summary>
        /// Populates each accepted ordinary index through one locality-preserving LibraDex concurrent batch.<br/>
        /// One index is completed before the next begins, and the LibraDex batch cooperatively publishes at its configured action bound so a large source cannot retain an unbounded private shelf image.<br/>
        /// This deliberate per-index lane collapse avoids making workers compete over the same hot shelves while preserving Fractal's independent multi-worker payload stage and concurrency among separate compatible Abraxas callers.<br/>
        /// Any failure leaves the store convergence checkpoint invalid; the caller invokes Abraxas's existing authoritative Fractal-to-LibraDex repair before reporting success or failure.<br/>
        /// </summary>
        /// <param name="simpleIndexes">Stable ordinary-index descriptors receiving tuples.<br/></param>
        /// <param name="indexKeys">Index-major runtime keys prepared before Fractal mutation.<br/></param>
        /// <param name="ids">Source-aligned generated identities.<br/></param>
        /// <param name="progress">Optional bounded reporter receiving per-index tuple progress.<br/></param>
        private static void ApplyConcurrentSimpleIndexBatches(
            LibraDexSimpleIndex[] simpleIndexes,
            object?[][] indexKeys,
            IReadOnlyList<ulong> ids,
            PutManyFastProgressReporter? progress)
        {
            for (int index = 0; index < simpleIndexes.Length; index++)
            {
                LibraDexSimpleIndex entry = simpleIndexes[index];
                object?[] keys = indexKeys[index];
                progress?.BeginPhase(
                    PutManyFastRoute.CoordinatedBulk,
                    PutManyFastPhase.WritingLibraDexIndex,
                    index + 1,
                    simpleIndexes.Length,
                    entry.Path);
                int completedRecords = 0;
                int reportInterval = Math.Max(1, (ids.Count + 199) / 200);
                if (!entry.Index.TryBeginConcurrentInsertBatch(out ILibraDexConcurrentInsertBatch? batch) || batch is null)
                    throw new NotSupportedException($"Index '{entry.Path}' withdrew concurrent insertion capability after preflight.");

                using (batch)
                {
                    for (int recordIndex = 0; recordIndex < ids.Count; recordIndex++)
                    {
                        LibraDexGenericInsertResult inserted = batch.Insert(keys[recordIndex], ids[recordIndex]);
                        if (!inserted.Inserted)
                        {
                            throw new IndexContractException(
                                entry.Unique && keys[recordIndex] is not null
                                    ? IndexContractFailureKind.UniqueConstraint
                                    : IndexContractFailureKind.BackendMutation,
                                $"Index '{entry.Path}' rejected a prevalidated concurrent bulk tuple.",
                                entry.Index.Name,
                                entry.Path,
                                ids[recordIndex]);
                        }

                        completedRecords++;
                        if (completedRecords % reportInterval == 0)
                            progress?.Report(completedRecords);
                    }

                    LibraDexConcurrentBatchPublishResult publication = batch.Publish();
                    if (publication.AttemptedInsertCount != ids.Count || publication.InsertedCount != ids.Count)
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.BackendMutation,
                            $"Index '{entry.Path}' published {publication.InsertedCount:n0} of {publication.AttemptedInsertCount:n0} attempted concurrent-bulk tuples; {ids.Count:n0} were required.",
                            entry.Index.Name,
                            entry.Path);
                    }
                }

                progress?.CompletePhase(ids.Count);
            }
        }


        /// <summary>
        /// Extracts every composite tuple from the already serialized payloads before Fractal mutation.<br/>
        /// Complete non-null keys are checked structurally within each unique index; null-containing tuples remain eligible for duplicate routes under Abraxas's existing policy.<br/>
        /// Durable uniqueness is checked only after process-local reservations have been acquired, closing the preflight-to-publication race with another compatible writer.<br/>
        /// </summary>
        /// <param name="compositeIndexes">Stable routed-composite descriptors.<br/></param>
        /// <param name="binaries">Source-aligned serialized payloads.<br/></param>
        /// <param name="ids">Source-aligned generated identities.<br/></param>
        /// <returns>Index-major LibraDex keys and reservation keys aligned with <paramref name="ids"/>.<br/></returns>
        private static (LibraDexCompositeKey[][] Keys, object?[][] ReservationKeys)
            PrepareConcurrentCompositeIndexKeys(
                LibraDexCompositeIndex[] compositeIndexes,
                IReadOnlyList<InhetoBinary> binaries,
                IReadOnlyList<ulong> ids)
        {
            var keys = new LibraDexCompositeKey[compositeIndexes.Length][];
            var reservationKeys = new object?[compositeIndexes.Length][];
            for (int index = 0; index < compositeIndexes.Length; index++)
            {
                LibraDexCompositeIndex entry = compositeIndexes[index];
                keys[index] = new LibraDexCompositeKey[binaries.Count];
                reservationKeys[index] = new object?[binaries.Count];
                var seen = entry.Unique ? new HashSet<LibraDexCompositeKey>(CompositePreparedKeyComparer.Instance) : null;
                for (int recordIndex = 0; recordIndex < binaries.Count; recordIndex++)
                {
                    CompositeRuntimeKey runtime = ReadCompositeRuntimeKey(entry, binaries[recordIndex]);
                    LibraDexCompositeKey key = runtime.ToLibraDexKey();
                    keys[index][recordIndex] = key;
                    if (seen is null || runtime.ContainsNull)
                        continue;
                    if (!seen.Add(key))
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.UniqueConstraint,
                            $"Unique composite index '{entry.LogicalName}' contains the same complete non-null key more than once in the submitted concurrent batch.",
                            entry.PhysicalName,
                            entry.LogicalName,
                            ids[recordIndex]);
                    }

                    reservationKeys[index][recordIndex] = key;
                }
            }

            return (keys, reservationKeys);
        }

        /// <summary>
        /// Reserves all complete non-null unique composite keys before checking durable membership.<br/>
        /// A competing prepared writer therefore either holds an incompatible reservation or has already published a tuple visible to the subsequent exact-key preflight.<br/>
        /// Reservations stay owned until the caller finishes composite publication and cohort convergence.<br/>
        /// </summary>
        /// <param name="compositeIndexes">Stable routed-composite descriptors.<br/></param>
        /// <param name="keys">Index-major physical composite keys.<br/></param>
        /// <param name="reservationKeys">Index-major complete-key reservations, with null for exempt tuples.<br/></param>
        /// <param name="ids">Source-aligned generated identities for failure context.<br/></param>
        /// <returns>Reservations to release after physical publication.<br/></returns>
        private IDisposable[] ReserveConcurrentCompositeUniqueKeys(
            LibraDexCompositeIndex[] compositeIndexes,
            LibraDexCompositeKey[][] keys,
            object?[][] reservationKeys,
            IReadOnlyList<ulong> ids)
        {
            var reservations = new List<IDisposable>();
            try
            {
                for (int index = 0; index < compositeIndexes.Length; index++)
                {
                    LibraDexCompositeIndex entry = compositeIndexes[index];
                    if (!entry.Unique)
                        continue;

                    try
                    {
                        reservations.Add(storeRuntime.ReserveCompatibleUniqueKeys(
                            entry.PhysicalName,
                            reservationKeys[index],
                            CompositePreparedKeyComparer.Instance));
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.UniqueConstraint,
                            $"Unique composite index '{entry.LogicalName}' contains a key reserved by another active compatible writer.",
                            entry.PhysicalName,
                            entry.LogicalName,
                            ids.Count == 0 ? null : ids[0],
                            exception);
                    }

                    for (int recordIndex = 0; recordIndex < ids.Count; recordIndex++)
                    {
                        if (reservationKeys[index][recordIndex] is not null)
                        {
                            lock (entry.RoutedIndex)
                                ValidateLibraDexCompositeUniqueKey(entry, keys[index][recordIndex], ids[recordIndex]);
                        }
                    }
                }

                return reservations.ToArray();
            }
            catch
            {
                DisposeCompatibleUniqueReservations(reservations);
                throw;
            }
        }

        /// <summary>
        /// Publishes each prepared routed-composite population through LibraDex's bounded changed-node batch path.<br/>
        /// A partial or rejected publication triggers the caller's existing authoritative Fractal-to-LibraDex convergence repair.<br/>
        /// </summary>
        /// <param name="compositeIndexes">Stable routed-composite descriptors receiving tuples.<br/></param>
        /// <param name="keys">Index-major prepared composite keys.<br/></param>
        /// <param name="ids">Source-aligned generated identities.<br/></param>
        /// <param name="progress">Optional index-publication progress reporter.<br/></param>
        private static void ApplyConcurrentCompositeIndexBatches(
            LibraDexCompositeIndex[] compositeIndexes,
            LibraDexCompositeKey[][] keys,
            IReadOnlyList<ulong> ids,
            PutManyFastProgressReporter? progress)
        {
            for (int index = 0; index < compositeIndexes.Length; index++)
            {
                LibraDexCompositeIndex entry = compositeIndexes[index];
                progress?.BeginPhase(
                    PutManyFastRoute.CoordinatedBulk,
                    PutManyFastPhase.WritingLibraDexIndex,
                    index + 1,
                    compositeIndexes.Length,
                    entry.LogicalName);
                long inserted = entry.RoutedIndex.InsertBatch(keys[index], ids);
                if (inserted != ids.Count)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.BackendMutation,
                        $"Composite index '{entry.LogicalName}' published {inserted:n0} of {ids.Count:n0} required concurrent-bulk tuples.",
                        entry.PhysicalName,
                        entry.LogicalName);
                }

                progress?.CompletePhase(ids.Count);
            }
        }

        /// <summary>
        /// Compares prepared LibraDex composite keys structurally for both intra-batch validation and process-local reservations.<br/>
        /// Reusing the already allocated physical key container avoids boxing one additional runtime-key struct per complete unique tuple.<br/>
        /// </summary>
        private sealed class CompositePreparedKeyComparer : IEqualityComparer<LibraDexCompositeKey>, IEqualityComparer<object?>
        {
            internal static CompositePreparedKeyComparer Instance { get; } = new();

            /// <summary>
            /// Compares each positional value, using byte-content equality for binary parts and ordinary value equality for scalar or text parts.<br/>
            /// </summary>
            /// <param name="left">First prepared complete composite key.<br/></param>
            /// <param name="right">Second prepared complete composite key.<br/></param>
            /// <returns><see langword="true"/> when both complete keys route to equal exact parts.<br/></returns>
            public bool Equals(LibraDexCompositeKey? left, LibraDexCompositeKey? right)
            {
                if (ReferenceEquals(left, right))
                    return true;
                if (left is null || right is null || left.Count != right.Count)
                    return false;
                for (int index = 0; index < left.Count; index++)
                {
                    object? leftValue = left.Values[index].Value;
                    object? rightValue = right.Values[index].Value;
                    if (leftValue is byte[] leftBytes && rightValue is byte[] rightBytes)
                    {
                        if (!leftBytes.AsSpan().SequenceEqual(rightBytes))
                            return false;
                    }
                    else if (!object.Equals(leftValue, rightValue))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Hashes every positional value with the same binary-content treatment used by <see cref="Equals(LibraDexCompositeKey?, LibraDexCompositeKey?)"/>.<br/>
            /// </summary>
            /// <param name="key">Prepared complete composite key.<br/></param>
            /// <returns>A structural hash for intra-batch and reservation lookup.<br/></returns>
            public int GetHashCode(LibraDexCompositeKey key)
            {
                var hash = new HashCode();
                for (int index = 0; index < key.Count; index++)
                {
                    if (key.Values[index].Value is byte[] bytes)
                    {
                        for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
                            hash.Add(bytes[byteIndex]);
                    }
                    else
                    {
                        hash.Add(key.Values[index].Value);
                    }
                }

                return hash.ToHashCode();
            }

            /// <summary>
            /// Adapts prepared-key equality to the runtime reservation table without allocating a second key wrapper.<br/>
            /// </summary>
            /// <param name="left">First runtime reservation key.<br/></param>
            /// <param name="right">Second runtime reservation key.<br/></param>
            /// <returns><see langword="true"/> when both reservation keys are structurally equal.<br/></returns>
            public new bool Equals(object? left, object? right)
                => left is LibraDexCompositeKey leftKey && right is LibraDexCompositeKey rightKey
                    ? Equals(leftKey, rightKey)
                    : object.Equals(left, right);

            /// <summary>
            /// Adapts prepared-key hashing to the runtime reservation table.<br/>
            /// </summary>
            /// <param name="value">Runtime reservation key.<br/></param>
            /// <returns>The structural key hash, or zero for an exempt null reservation.<br/></returns>
            public int GetHashCode(object? value)
                => value is LibraDexCompositeKey key
                    ? GetHashCode(key)
                    : value?.GetHashCode() ?? 0;
        }
    }
}
