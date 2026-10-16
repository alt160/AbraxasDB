using System.Buffers;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Removes every developer-owned State tuple while preserving all Fractal records and every ordinary/calculated LibraDex index.<br/>
        /// The operation is intentionally idempotent: a repeated call returns zero, and a caller may safely retry after an interruption because clearing has no inverse payload mutation.<br/>
        /// </summary>
        /// <returns>The number of assigned State tuples removed.<br/></returns>
        public ulong ClearAllStates()
        {
            const string operationName = nameof(ClearAllStates);
            rbase.EnsureWriteAllowed(operationName);
            using var mutation = EnterPartialIndexMutationScope();
            EnsureLibraDexConvergence(operationName);

            var indexes = libraDexIdentityIndexes
                ?? throw new InvalidOperationException($"Operation '{operationName}' requires this store's reserved persistent LibraDex State index.");
            _ = RequireLibraDexStateIndex(operationName);

            LibraDex.LibraDexIdentityMutationResult result;
            lock (libraDexIndexLock)
                result = indexes.Define(LibraDexStateIndexName).DeleteAll();

            if (result.ChangedCount > 0)
                rbase.data.AdvanceUSN(storeID);
            SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
            return checked((ulong)result.ChangedCount);
        }

        /// <summary>
        /// Removes State from records selected by one complete Abraxas condition without rewriting or deserializing their developer objects.<br/>
        /// Selection is frozen before the first State mutation, so a condition that reads State cannot skip, duplicate, or newly admit records as matching tuples are cleared.<br/>
        /// The frozen identity snapshot uses pooled fixed-size segments to avoid one large-object-heap allocation; each individual clear retains the existing replayable convergence intent.<br/>
        /// Cancellation may occur while capturing or between completed clears. Already completed clears remain durable and a later retry is safe because the operation is idempotent.<br/>
        /// </summary>
        /// <param name="where">Complete condition evaluated against the pre-mutation store reality.<br/></param>
        /// <param name="cancellationToken">Optional cancellation observed during capture and between record mutations.<br/></param>
        /// <returns>The number of records whose previously assigned State tuple was removed.<br/></returns>
        public ulong ClearStateWhere(
            Condition<T>.EndCondition where,
            CancellationToken cancellationToken = default)
        {
            const string operationName = nameof(ClearStateWhere);
            rbase.EnsureWriteAllowed(operationName);
            ArgumentNullException.ThrowIfNull(where);
            using var mutation = EnterPartialIndexMutationScope();
            EnsureLibraDexConvergence(operationName);
            using var identities = CaptureStateMutationIdentities(where, operationName, cancellationToken);

            ulong changed = 0;
            var enumerator = identities.GetEnumerator();
            while (enumerator.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ClearStateCore(enumerator.Current, operationName))
                    changed = checked(changed + 1);
            }

            return changed;
        }

        /// <summary>
        /// Assigns one State value to records selected by a complete Abraxas condition without rewriting or deserializing their developer objects.<br/>
        /// Selection is frozen before assignment, so conditions involving current State retain match-once semantics even though the target index changes during execution.<br/>
        /// Records without State receive a tuple, records with a different State are re-keyed, and records already carrying <paramref name="state"/> are allocation-light no-ops excluded from the returned count.<br/>
        /// Cancellation may occur while capturing or between completed assignments. Already completed assignments remain durable and a later retry is safe because setting the same value is idempotent.<br/>
        /// </summary>
        /// <param name="where">Complete condition evaluated against the pre-mutation store reality.<br/></param>
        /// <param name="state">Signed 64-bit developer State assigned to every frozen match.<br/></param>
        /// <param name="cancellationToken">Optional cancellation observed during capture and between record mutations.<br/></param>
        /// <returns>The number of records whose State was newly assigned or changed.<br/></returns>
        public ulong SetStateWhere(
            Condition<T>.EndCondition where,
            long state,
            CancellationToken cancellationToken = default)
        {
            const string operationName = nameof(SetStateWhere);
            rbase.EnsureWriteAllowed(operationName);
            ArgumentNullException.ThrowIfNull(where);
            using var mutation = EnterPartialIndexMutationScope();
            EnsureLibraDexConvergence(operationName);
            using var identities = CaptureStateMutationIdentities(where, operationName, cancellationToken);

            ulong changed = 0;
            var enumerator = identities.GetEnumerator();
            while (enumerator.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SetStateCore(enumerator.Current, state, operationName))
                    changed = checked(changed + 1);
            }

            return changed;
        }

        /// <summary>
        /// Captures the complete pre-mutation identity set for one condition into pooled fixed-size segments.<br/>
        /// Holding the shared store mutation gate around capture and consumption prevents another facade from changing Fractal or maintained indexes between those phases.<br/>
        /// </summary>
        /// <param name="where">Condition whose authoritative planned matches are captured.<br/></param>
        /// <param name="operationName">Public operation name used for planning events and diagnostics.<br/></param>
        /// <param name="cancellationToken">Cancellation token observed by the planned pipeline and capture loop.<br/></param>
        /// <returns>A disposable pooled identity snapshot.<br/></returns>
        private PooledStateIdentitySnapshot CaptureStateMutationIdentities(
            Condition<T>.EndCondition where,
            string operationName,
            CancellationToken cancellationToken)
        {
            var snapshot = new PooledStateIdentitySnapshot();
            try
            {
                foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                    where,
                    operationName,
                    includePayload: false,
                    cancellationToken: cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    snapshot.Add(match.RecordId);
                }

                return snapshot;
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Applies one State assignment after the caller has admitted the store mutation lane and validated record existence.<br/>
        /// The existing durable one-record intent remains the interruption boundary used by single-record and condition-based assignment alike.<br/>
        /// </summary>
        /// <param name="recordID">Existing record identity owned by this store.<br/></param>
        /// <param name="state">State value to assign.<br/></param>
        /// <param name="operationName">Public operation name reported by convergence failures.<br/></param>
        /// <returns><see langword="true"/> when State changed; otherwise <see langword="false"/>.<br/></returns>
        private bool SetStateCore(ulong recordID, long state, string operationName)
        {
            var index = RequireLibraDexStateIndex(operationName);
            lock (libraDexIndexLock)
            {
                if (index.TryGetSingleKey(recordID, out long current) && current == state)
                    return false;
            }

            var retainedStateIntent = RetainSetStateIntent(recordID, state, payload: null);
            try
            {
                rbase.data.AdvanceUSN(storeID);
                TryApplyLibraDexStateUpsert(recordID, state);
                ClearPendingStateIntent(retainedStateIntent);
                SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
            }
            catch (Exception indexFailure)
            {
                TryRepairLibraDexAfterFractalMutation(
                    operationName,
                    recordID,
                    indexFailure,
                    pendingStateIntentRetained: true,
                    fractalMutationCompleted: false);
            }

            return true;
        }

        /// <summary>
        /// Applies one State clear after the caller has admitted the store mutation lane and validated record existence.<br/>
        /// The same replayable intent used by the single-record API makes each completed member of a condition-based clear independently durable.<br/>
        /// </summary>
        /// <param name="recordID">Existing record identity owned by this store.<br/></param>
        /// <param name="operationName">Public operation name reported by convergence failures.<br/></param>
        /// <returns><see langword="true"/> when an assigned State tuple was removed; otherwise <see langword="false"/>.<br/></returns>
        private bool ClearStateCore(ulong recordID, string operationName)
        {
            var index = RequireLibraDexStateIndex(operationName);
            lock (libraDexIndexLock)
            {
                if (!index.TryGetSingleKey(recordID, out _))
                    return false;
            }

            var retainedStateIntent = RetainClearStateIntent(recordID);
            try
            {
                rbase.data.AdvanceUSN(storeID);
                TryApplyLibraDexStateDelete(recordID);
                ClearPendingStateIntent(retainedStateIntent);
                SetLibraDexAppliedSourceUsn(rbase.data.GetUSN(storeID));
            }
            catch (Exception indexFailure)
            {
                TryRepairLibraDexAfterFractalMutation(
                    operationName,
                    recordID,
                    indexFailure,
                    pendingStateIntentRetained: true,
                    fractalMutationCompleted: false);
            }

            return true;
        }

        /// <summary>
        /// Stores a frozen identity set in pooled 32-KiB segments, avoiding a contiguous large-object-heap buffer for broad condition mutations.<br/>
        /// </summary>
        private sealed class PooledStateIdentitySnapshot : IDisposable
        {
            private const int SegmentLength = 4096;
            private readonly List<ulong[]> segments = new();
            private ulong count;
            private bool disposed;

            /// <summary>
            /// Appends one identity to the snapshot.<br/>
            /// </summary>
            /// <param name="recordID">Identity selected by the pre-mutation condition.<br/></param>
            internal void Add(ulong recordID)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                int offset = checked((int)(count % SegmentLength));
                if (offset == 0)
                    segments.Add(ArrayPool<ulong>.Shared.Rent(SegmentLength));
                segments[^1][offset] = recordID;
                count = checked(count + 1);
            }

            /// <summary>
            /// Returns a non-boxing forward enumerator over the captured identity count.<br/>
            /// </summary>
            /// <returns>Snapshot enumerator.<br/></returns>
            internal Enumerator GetEnumerator()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return new Enumerator(segments, count);
            }

            /// <summary>
            /// Returns every rented segment to the shared pool without clearing value-type identity bytes.<br/>
            /// </summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                for (int i = 0; i < segments.Count; i++)
                    ArrayPool<ulong>.Shared.Return(segments[i], clearArray: false);
                segments.Clear();
                count = 0;
            }

            /// <summary>
            /// Walks pooled segments without allocating an iterator object or boxing a value-type enumerator.<br/>
            /// </summary>
            internal struct Enumerator
            {
                private readonly List<ulong[]> segments;
                private ulong remaining;
                private int segmentIndex;
                private int itemIndex;

                /// <summary>
                /// Creates an enumerator bounded by the exact captured identity count rather than the rented array capacities.<br/>
                /// </summary>
                /// <param name="segments">Rented identity segments.<br/></param>
                /// <param name="count">Exact number of initialized entries.<br/></param>
                internal Enumerator(List<ulong[]> segments, ulong count)
                {
                    this.segments = segments;
                    remaining = count;
                    segmentIndex = 0;
                    itemIndex = -1;
                    Current = default;
                }

                /// <summary>
                /// Gets the identity selected by the most recent successful move.<br/>
                /// </summary>
                internal ulong Current { get; private set; }

                /// <summary>
                /// Advances to the next initialized entry across segment boundaries.<br/>
                /// </summary>
                /// <returns><see langword="true"/> when another identity is available.<br/></returns>
                internal bool MoveNext()
                {
                    if (remaining == 0)
                        return false;

                    itemIndex++;
                    if (itemIndex == SegmentLength)
                    {
                        segmentIndex++;
                        itemIndex = 0;
                    }

                    Current = segments[segmentIndex][itemIndex];
                    remaining--;
                    return true;
                }
            }
        }
    }
}
