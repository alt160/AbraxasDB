using System.Buffers.Binary;
using Inheto;
using LibraDex;
using Standart.Hash.xxHash;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private const ulong LibraDexControlIdentity = 0;
        private const byte PendingStateIntentFormatVersion = 1;
        private const int PendingStateIntentWidth = 32;

        /// <summary>
        /// Opens or creates the two hidden LibraDex control indexes used by Abraxas to prove derived-index convergence and retain a replayable State change.<br/>
        /// Both indexes use one key per reserved identity, keeping steady-state reads and replacement writes bounded by LibraDex's inverse lookup rather than catalog scans.<br/>
        /// </summary>
        /// <param name="indexes">Persistent LibraDex identity group owned by this record store.<br/></param>
        private void EnsureLibraDexConvergenceIndexes(CatalogIdentityGroupIndexes indexes)
        {
            if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return;

            lock (libraDexIndexLock)
            {
                libraDexAppliedSourceUsnIndex ??= OpenValidatedLibraDexAppliedSourceUsnIndex(indexes);
                libraDexPendingStateIntentIndex ??= OpenValidatedLibraDexPendingStateIntentIndex(indexes);
            }
        }

        /// <summary>
        /// Opens the hidden UInt64 applied-source-USN checkpoint after validating its persisted scalar shape, or creates the exact shape when absent.<br/>
        /// The checkpoint is an assertion about LibraDex reality: it advances only after every maintained tuple set reflects the corresponding Fractal store USN.<br/>
        /// </summary>
        /// <param name="indexes">Persistent LibraDex identity group owned by this record store.<br/></param>
        /// <returns>Validated UInt64-key/UInt64-identity checkpoint index.<br/></returns>
        private static LibraDexIndex<ulong, ulong> OpenValidatedLibraDexAppliedSourceUsnIndex(
            CatalogIdentityGroupIndexes indexes)
        {
            var options = CreateLibraDexControlIndexOptions();
            if (!indexes.TryGetInfo(LibraDexAppliedSourceUsnIndexName, out CatalogIndexInfo info))
            {
                return indexes.Define(LibraDexAppliedSourceUsnIndexName)
                    .Scalar.Scalar<ulong, ulong>()
                    .Create(options: options);
            }

            if (!info.TryCreateShape(out LibraDexIndexShapeSpec? shape) ||
                shape.KeyType != typeof(ulong) ||
                shape.IdentityType != typeof(ulong) ||
                info.KeyFamily != CatalogIndexKeyFamily.Scalar ||
                info.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                info.KeyContract != IndexKeys.NonUnique ||
                info.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
            {
                throw new InvalidDataException(
                    $"Reserved LibraDex convergence index '{indexes.Name}/{LibraDexAppliedSourceUsnIndexName}' has an incompatible persisted contract.");
            }

            return indexes.Define(LibraDexAppliedSourceUsnIndexName)
                .Scalar.Scalar<ulong, ulong>()
                .Open(options: options);
        }

        /// <summary>
        /// Opens the hidden fixed-32 pending-State intent index after validating its persisted shape, or creates the exact shape when absent.<br/>
        /// Abraxas owns the encoded meaning; LibraDex only durably retains the opaque fixed-width key until replay completes.<br/>
        /// </summary>
        /// <param name="indexes">Persistent LibraDex identity group owned by this record store.<br/></param>
        /// <returns>Validated fixed-32-key/UInt64-identity intent index.<br/></returns>
        private static LibraDexIndex<byte[], ulong> OpenValidatedLibraDexPendingStateIntentIndex(
            CatalogIdentityGroupIndexes indexes)
        {
            var options = CreateLibraDexControlIndexOptions();
            if (!indexes.TryGetInfo(LibraDexPendingStateIntentIndexName, out CatalogIndexInfo info))
            {
                return indexes.Define(LibraDexPendingStateIntentIndexName)
                    .Blob.Scalar<ulong>(LibraDexScalarWidth.Bytes32)
                    .Create(options: options);
            }

            if (!info.TryCreateShape(out LibraDexIndexShapeSpec? shape) ||
                shape.KeyType != typeof(byte[]) ||
                shape.IdentityType != typeof(ulong) ||
                info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                info.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                info.KeyContract != IndexKeys.NonUnique ||
                info.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity ||
                info.VarKeyMaxKeyLength != PendingStateIntentWidth)
            {
                throw new InvalidDataException(
                    $"Reserved LibraDex State-intent index '{indexes.Name}/{LibraDexPendingStateIntentIndexName}' has an incompatible persisted contract.");
            }

            return indexes.Define(LibraDexPendingStateIntentIndexName)
                .Blob.Scalar<ulong>(LibraDexScalarWidth.Bytes32)
                .Open(options: options);
        }

        /// <summary>
        /// Creates the common one-key-per-control-identity contract used by hidden convergence indexes.<br/>
        /// </summary>
        /// <returns>A new mutable options instance suitable for one catalog create or open call.<br/></returns>
        private static IndexOptions CreateLibraDexControlIndexOptions()
            => new()
            {
                Keys = IndexKeys.NonUnique,
                IdentityKeyMultiplicity = IdentityKeyMultiplicity.SingleKeyPerIdentity
            };

        /// <summary>
        /// Verifies that the bound LibraDex reality matches the current Fractal store USN and repairs it synchronously when stale.<br/>
        /// A matching checkpoint with a retained State intent replays only that idempotent intent; a stale or absent checkpoint rebuilds maintained property and partial indexes from Fractal before advancing the checkpoint.<br/>
        /// </summary>
        /// <param name="operationName">Operation requiring a converged ordered companion.<br/></param>
        private void EnsureLibraDexConvergence(string operationName)
        {
            if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly || libraDexIdentityIndexes is null)
                return;

            var initialSourceUsn = rbase.data.GetUSN(storeID);
            if (storeRuntime.IsIndexSourceUsnApplied(initialSourceUsn))
                return;

            using var mutation = storeRuntime.EnterMutationScope();
            var sourceUsn = rbase.data.GetUSN(storeID);
            if (storeRuntime.IsIndexSourceUsnApplied(sourceUsn))
                return;

            var hasApplied = TryGetLibraDexAppliedSourceUsn(out var appliedUsn);
            var hasPendingState = TryReadPendingStateIntent(out var pendingState);
            if (hasApplied && appliedUsn == sourceUsn)
            {
                if (hasPendingState)
                {
                    ReplayPendingStateIntent(pendingState);
                    _ = RepairLibraDexStateIndex();
                    ClearPendingStateIntent(pendingState);
                }
                storeRuntime.SetAppliedIndexSourceUsn(sourceUsn);
                return;
            }

            try
            {
                RepairLibraDexConvergence(sourceUsn, hasPendingState ? pendingState : null);
            }
            catch (Exception exception) when (exception is not IndexConvergenceException)
            {
                throw new IndexConvergenceException(
                    operationName,
                    sourceUsn,
                    hasApplied ? appliedUsn : null,
                    recordMutationCompleted: false,
                    pendingStateIntentRetained: hasPendingState,
                    innerException: exception);
            }
        }

        /// <summary>
        /// Rebuilds every currently registered property and partial index from authoritative Fractal/Inheto records, reconciles State, and advances the source checkpoint last.<br/>
        /// Prepared ordinary populations validate all keys before clearing their targets; a failure leaves the prior checkpoint unchanged so no indexed query can mistake partial work for ready reality.<br/>
        /// </summary>
        /// <param name="sourceUsn">Fractal store USN that the completed repair must represent.<br/></param>
        /// <param name="pendingState">Optional retained State operation to replay during convergence.<br/></param>
        private void RepairLibraDexConvergence(ulong sourceUsn, PendingStateIntent? pendingState)
        {
            var simpleIndexes = libraDexSimpleIndexes;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                try
                {
                    var prepared = PrepareLibraDexSimpleIndexPopulation(entry);
                            RebuildPreparedLibraDexSimpleIndex(entry, prepared);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"LibraDex convergence repair failed while rebuilding simple index '{entry.Index.Name}' for path '{entry.Path}' and key type '{entry.KeyType.FullName}'.",
                        exception);
                }
            }

            var compositeIndexes = libraDexCompositeIndexes;
            for (var i = 0; i < compositeIndexes.Length; i++)
            {
                var entry = compositeIndexes[i];
                var prepared = PrepareLibraDexCompositeIndexPopulation(entry);
                RebuildPreparedLibraDexIndex(
                    entry.Index,
                    entry.OwnerCatalog,
                    () => ApplyLibraDexCompositeIndexPopulation(entry, prepared),
                    entry.PhysicalName,
                    entry.LogicalName);
            }

            var regexIndexes = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                var prepared = PrepareLibraDexRegexProjectionIndexPopulation(entry);
                RebuildPreparedLibraDexIndex(
                    entry.Index,
                    entry.OwnerCatalog,
                    () => ApplyLibraDexRegexProjectionIndexPopulation(entry, prepared),
                    entry.LogicalPrefix,
                    entry.SourcePath);
            }

            var partialIndexes = libraDexPartialIndexes;
            for (var i = 0; i < partialIndexes.Length; i++)
            {
                var entry = partialIndexes[i];
                entry.SetReady(false);
                ClearPartialPhysicalIndex(entry.Index, entry.OwnerCatalog);
                BackfillPartialIndex(entry);
                entry.SetAppliedSourceUsn(sourceUsn);
                entry.SetReady(true);
                if (entry.OwnerCatalog is null)
                    rbase.UpsertPartialIndexCatalogEntry(entry.ToCatalogEntry(storeID, entry.Descriptor.Serialize()));
            }

            if (pendingState.HasValue)
                ReplayPendingStateIntent(pendingState.Value);
            _ = RepairLibraDexStateIndex();
            if (pendingState.HasValue)
                ClearPendingStateIntent(pendingState.Value);
            SetLibraDexAppliedSourceUsn(sourceUsn);
        }

        /// <summary>
        /// Performs the one automatic convergence attempt required after Fractal has changed but a LibraDex mutation failed.<br/>
        /// Successful repair lets the original operation return normally; failed repair throws a typed partial-completion exception while leaving the stale checkpoint or retained State intent available for reopen recovery.<br/>
        /// </summary>
        /// <param name="operationName">Public mutation name.<br/></param>
        /// <param name="recordID">Affected record identity when the operation targets one record.<br/></param>
        /// <param name="mutationFailure">Original companion-index mutation failure.<br/></param>
        /// <param name="pendingStateIntentRetained">Whether a replayable State intent remains durable.<br/></param>
        /// <param name="fractalMutationCompleted">Whether the authoritative Fractal mutation completed; retained in a partial-completion exception if automatic index repair also fails.<br/></param>
        private void TryRepairLibraDexAfterFractalMutation(
            string operationName,
            ulong? recordID,
            Exception mutationFailure,
            bool pendingStateIntentRetained,
            bool fractalMutationCompleted = true)
        {
            var sourceUsn = rbase.data.GetUSN(storeID);
            var hasApplied = TryGetLibraDexAppliedSourceUsn(out var appliedUsn);
            try
            {
                PendingStateIntent? pending = TryReadPendingStateIntent(out var retained) ? retained : null;
                RepairLibraDexConvergence(sourceUsn, pending);
            }
            catch (Exception repairFailure)
            {
                storeRuntime.ClearAppliedIndexSourceUsn();
                MarkAllPartialIndexesUnavailable();
                throw new IndexConvergenceException(
                    operationName,
                    sourceUsn,
                    hasApplied ? appliedUsn : null,
                    fractalMutationCompleted,
                    pendingStateIntentRetained,
                    recordID,
                    new AggregateException(mutationFailure, repairFailure));
            }
        }

        /// <summary>
        /// Reads the one hidden applied-source-USN value without enumerating the checkpoint index.<br/>
        /// </summary>
        /// <param name="sourceUsn">Applied Fractal USN when present.<br/></param>
        /// <returns><see langword="true"/> when a checkpoint exists; otherwise <see langword="false"/>.<br/></returns>
        private bool TryGetLibraDexAppliedSourceUsn(out ulong sourceUsn)
        {
            var index = libraDexAppliedSourceUsnIndex
                ?? throw new InvalidOperationException("The LibraDex convergence checkpoint is unavailable.");
            lock (libraDexIndexLock)
                return index.TryGetSingleKey(LibraDexControlIdentity, out sourceUsn);
        }

        /// <summary>
        /// Replaces the one hidden applied-source-USN value after every maintained tuple set has converged.<br/>
        /// </summary>
        /// <param name="sourceUsn">Current authoritative Fractal store USN.<br/></param>
        private void SetLibraDexAppliedSourceUsn(ulong sourceUsn)
        {
            var index = libraDexAppliedSourceUsnIndex
                ?? throw new InvalidOperationException("The LibraDex convergence checkpoint is unavailable.");
            lock (libraDexIndexLock)
            {
                if (index.TryGetSingleKey(LibraDexControlIdentity, out ulong current))
                {
                    if (current != sourceUsn)
                        index.Rekey(LibraDexControlIdentity, current, sourceUsn);
                    storeRuntime.SetAppliedIndexSourceUsn(sourceUsn);
                    return;
                }

                var inserted = index.Insert(sourceUsn, LibraDexControlIdentity);
                if (!inserted.Inserted)
                    throw new InvalidOperationException("Could not establish the LibraDex applied-source-USN checkpoint.");
            }
            storeRuntime.SetAppliedIndexSourceUsn(sourceUsn);
        }

        /// <summary>
        /// Persists one replayable State assignment before an operation can change Fractal or the State index.<br/>
        /// Conditional intents include the intended serialized payload's length and xxHash64 so reopen recovery applies State only when Fractal reached that payload reality.<br/>
        /// </summary>
        /// <param name="recordID">Target record identity.<br/></param>
        /// <param name="state">State value to assign.<br/></param>
        /// <param name="payload">Intended serialized payload, or <c>null</c> for a State-only operation.<br/></param>
        private PendingStateIntent RetainSetStateIntent(ulong recordID, long state, ReadOnlyMemory<byte>? payload)
            => RetainPendingStateIntent(PendingStateIntent.CreateSet(recordID, state, payload));

        /// <summary>
        /// Persists one replayable State-clear operation before the State tuple can change.<br/>
        /// </summary>
        /// <param name="recordID">Target record identity.<br/></param>
        private PendingStateIntent RetainClearStateIntent(ulong recordID)
            => RetainPendingStateIntent(PendingStateIntent.CreateClear(recordID));

        /// <summary>
        /// Inserts or replaces the single fixed-width State intent associated with the hidden control identity.<br/>
        /// </summary>
        /// <param name="intent">Fully encoded pending intent.<br/></param>
        private PendingStateIntent RetainPendingStateIntent(PendingStateIntent intent)
        {
            storeRuntime.ClearAppliedIndexSourceUsn();
            var index = libraDexPendingStateIntentIndex
                ?? throw new InvalidOperationException("The LibraDex pending-State intent index is unavailable.");
            lock (libraDexIndexLock)
            {
                if (index.TryGetSingleKey(LibraDexControlIdentity, out _))
                    throw new InvalidOperationException("A prior pending Abraxas State intent was not recovered before a new mutation began.");

                Span<byte> encoded = stackalloc byte[PendingStateIntentWidth];
                intent.Encode(encoded);
                var inserted = ((IFixedBinaryKeyIndex<ulong>)index).Insert(encoded, LibraDexControlIdentity);
                if (!inserted.Inserted)
                    throw new InvalidOperationException("Could not retain the pending Abraxas State intent.");
            }
            return intent;
        }

        /// <summary>
        /// Reads and validates the one retained fixed-width State intent through LibraDex's inverse lookup.<br/>
        /// </summary>
        /// <param name="intent">Decoded pending intent when present.<br/></param>
        /// <returns><see langword="true"/> when an intent exists; otherwise <see langword="false"/>.<br/></returns>
        private bool TryReadPendingStateIntent(out PendingStateIntent intent)
        {
            var index = libraDexPendingStateIntentIndex
                ?? throw new InvalidOperationException("The LibraDex pending-State intent index is unavailable.");
            lock (libraDexIndexLock)
            {
                if (!index.TryGetSingleKey(LibraDexControlIdentity, out byte[] encoded))
                {
                    intent = default;
                    return false;
                }

                intent = PendingStateIntent.Decode(encoded);
                return true;
            }
        }

        /// <summary>
        /// Deletes the exact retained State intent after replay completes and immediately before the applied-source checkpoint advances.<br/>
        /// </summary>
        /// <param name="intent">Decoded or newly retained intent whose exact deterministic bytes should be removed.<br/></param>
        private void ClearPendingStateIntent(PendingStateIntent intent)
        {
            var index = libraDexPendingStateIntentIndex
                ?? throw new InvalidOperationException("The LibraDex pending-State intent index is unavailable.");
            Span<byte> encoded = stackalloc byte[PendingStateIntentWidth];
            intent.Encode(encoded);
            lock (libraDexIndexLock)
            {
                if (!((IFixedBinaryKeyIndex<ulong>)index).Delete(encoded, LibraDexControlIdentity))
                    throw new InvalidOperationException("Could not clear the exact pending Abraxas State intent.");
            }
        }

        /// <summary>
        /// Applies one retained State intent idempotently when its Fractal payload condition is satisfied.<br/>
        /// A conditional intent whose record is absent or whose bytes do not match is deliberately discarded as a pre-payload interruption rather than changing State for an uncompleted payload mutation.<br/>
        /// </summary>
        /// <param name="intent">Validated retained State intent.<br/></param>
        private void ReplayPendingStateIntent(PendingStateIntent intent)
        {
            if (!rbase.data.RecordExists(intent.RecordID))
                return;

            if (intent.IsPayloadConditional)
            {
                var binary = GetInhetoBinary(intent.RecordID, nameof(ReplayPendingStateIntent));
                var bytes = binary.RawBytes.AsReadOnlySpan;
                if (bytes.Length != intent.PayloadLength ||
                    xxHash64.ComputeHash(bytes, bytes.Length) != intent.PayloadHash)
                {
                    return;
                }
            }

            if (intent.Kind == PendingStateIntentKind.Set)
                TryApplyLibraDexStateUpsert(intent.RecordID, intent.State);
            else
                TryApplyLibraDexStateDelete(intent.RecordID);
        }

        /// <summary>
        /// Encodes the narrow Abraxas State operation retained only across a convergence window.<br/>
        /// </summary>
        private readonly struct PendingStateIntent
        {
            internal PendingStateIntent(
                PendingStateIntentKind kind,
                ulong recordID,
                long state,
                bool isPayloadConditional,
                ulong payloadHash,
                int payloadLength)
            {
                Kind = kind;
                RecordID = recordID;
                State = state;
                IsPayloadConditional = isPayloadConditional;
                PayloadHash = payloadHash;
                PayloadLength = payloadLength;
            }

            internal PendingStateIntentKind Kind { get; }
            internal ulong RecordID { get; }
            internal long State { get; }
            internal bool IsPayloadConditional { get; }
            internal ulong PayloadHash { get; }
            internal int PayloadLength { get; }

            /// <summary>
            /// Creates one State assignment intent and optionally binds it to exact intended Fractal payload bytes.<br/>
            /// </summary>
            /// <param name="recordID">Target record identity.<br/></param>
            /// <param name="state">State value to assign.<br/></param>
            /// <param name="payload">Intended payload, or <c>null</c> for State-only replay.<br/></param>
            /// <returns>Encoded immutable intent.<br/></returns>
            internal static PendingStateIntent CreateSet(ulong recordID, long state, ReadOnlyMemory<byte>? payload)
            {
                if (!payload.HasValue)
                    return Create(PendingStateIntentKind.Set, recordID, state, false, 0, 0);

                var intendedPayload = payload.GetValueOrDefault();
                var length = intendedPayload.Length;
                var hash = xxHash64.ComputeHash(intendedPayload.Span, length);
                return Create(PendingStateIntentKind.Set, recordID, state, true, hash, length);
            }

            /// <summary>
            /// Creates one unconditional State-clear intent.<br/>
            /// </summary>
            /// <param name="recordID">Target record identity.<br/></param>
            /// <returns>Encoded immutable intent.<br/></returns>
            internal static PendingStateIntent CreateClear(ulong recordID)
                => Create(PendingStateIntentKind.Clear, recordID, 0, false, 0, 0);

            private static PendingStateIntent Create(
                PendingStateIntentKind kind,
                ulong recordID,
                long state,
                bool conditional,
                ulong payloadHash,
                int payloadLength)
            {
                return new PendingStateIntent(kind, recordID, state, conditional, payloadHash, payloadLength);
            }

            /// <summary>
            /// Writes the deterministic fixed-width intent into caller-owned memory without allocating a key array.<br/>
            /// </summary>
            /// <param name="destination">Exactly 32 writable bytes consumed synchronously by LibraDex.<br/></param>
            internal void Encode(Span<byte> destination)
            {
                if (destination.Length != PendingStateIntentWidth)
                    throw new ArgumentException("Pending State intent encoding requires exactly 32 bytes.", nameof(destination));

                destination.Clear();
                destination[0] = PendingStateIntentFormatVersion;
                destination[1] = (byte)Kind;
                destination[2] = IsPayloadConditional ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(4, 8), RecordID);
                BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(12, 8), State);
                BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(20, 8), PayloadHash);
                BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(28, 4), PayloadLength);
            }

            /// <summary>
            /// Validates and decodes one fixed-width persisted intent.<br/>
            /// </summary>
            /// <param name="encoded">Exact LibraDex key bytes.<br/></param>
            /// <returns>Decoded immutable intent whose fields deterministically reproduce the exact deletion key without retaining another byte array.<br/></returns>
            internal static PendingStateIntent Decode(byte[] encoded)
            {
                if (encoded is null || encoded.Length != PendingStateIntentWidth)
                    throw new InvalidDataException("The pending State intent does not have the required 32-byte shape.");
                if (encoded[0] != PendingStateIntentFormatVersion ||
                    encoded[1] is not ((byte)PendingStateIntentKind.Set) and not ((byte)PendingStateIntentKind.Clear) ||
                    encoded[2] > 1 ||
                    encoded[3] != 0)
                {
                    throw new InvalidDataException("The pending State intent has an unsupported version, operation, or flags.");
                }

                var kind = (PendingStateIntentKind)encoded[1];
                var conditional = encoded[2] != 0;
                var recordID = BinaryPrimitives.ReadUInt64LittleEndian(encoded.AsSpan(4, 8));
                var state = BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(12, 8));
                var payloadHash = BinaryPrimitives.ReadUInt64LittleEndian(encoded.AsSpan(20, 8));
                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(28, 4));
                if (recordID == 0 ||
                    payloadLength < 0 ||
                    (kind == PendingStateIntentKind.Clear && conditional))
                {
                    throw new InvalidDataException("The pending State intent contains an invalid record, payload, or clear-operation contract.");
                }

                return new PendingStateIntent(kind, recordID, state, conditional, payloadHash, payloadLength);
            }
        }

        private enum PendingStateIntentKind : byte
        {
            Set = 1,
            Clear = 2
        }
    }
}
