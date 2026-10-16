using Inheto;
using LibraDex;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private readonly object partialIndexLifecycleLock = new();
        private LibraDexPartialIndex[] libraDexPartialIndexes = Array.Empty<LibraDexPartialIndex>();

        /// <summary>
        /// Enters the store-local mutation gate shared by partial-index creation, uniqueness preflight, authoritative writes, and tuple maintenance.<br/>
        /// Holding one reentrant monitor across the complete operation prevents two writers from both passing uniqueness preflight and prevents backfill from observing an unmaintained transition.<br/>
        /// </summary>
        /// <returns>A scope that releases the mutation gate exactly once.<br/></returns>
        private IDisposable EnterPartialIndexMutationScope()
        {
            var lifecycle = EnterStoreOperationScope();
            IDisposable? storeMutation = null;
            try
            {
                storeMutation = storeRuntime.EnterMutationScope();
                Monitor.Enter(partialIndexLifecycleLock);
                return new PartialIndexMutationScope(partialIndexLifecycleLock, storeMutation, lifecycle);
            }
            catch
            {
                storeMutation?.Dispose();
                lifecycle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates, backfills, validates, and publishes one static file- or memory-backed partial LibraDex index.<br/>
        /// Both backing kinds require a complete system-persistable condition; only the file-backed form writes catalog and codec intent.<br/>
        /// </summary>
        /// <param name="keyPath">Simple property path used as the index key.<br/></param>
        /// <param name="unique">Whether indexed members must have distinct keys.<br/></param>
        /// <param name="where">Complete static condition that defines index membership.<br/></param>
        /// <param name="logicalName">Optional stable management name; a deterministic name is generated when omitted.<br/></param>
        /// <param name="textProfile">Text projection profile for string keys, or <c>null</c> for ordinary scalar creation.<br/></param>
        /// <param name="direction">Requested physical traversal direction.<br/></param>
        /// <param name="ownerCatalog">Volatile owner, or <see langword="null"/> for a persistent definition.<br/></param>
        /// <returns><c>true</c> when a new definition was created; <c>false</c> when the exact definition already exists and is ready.<br/></returns>
        private bool CreateStaticPartialIndex(
            string keyPath,
            bool unique,
            Condition<T>.EndCondition where,
            string? logicalName,
            TextIndexProfile? textProfile,
            IndexDirection direction,
            Catalog? ownerCatalog = null)
        {
            ArgumentNullException.ThrowIfNull(where);
            var descriptor = where.ToDescriptor();
            if (!descriptor.IsSystemPersistable)
            {
                throw new InvalidOperationException(
                    "Partial-index conditions must be complete and system-persistable. " +
                    "Inspect the descriptor Parameters collection and bind every external value before index creation.");
            }
            if (!where.builder.TryBuildSemanticEvaluator(NativeDeserializationOptions, out var evaluator))
                throw new NotSupportedException("The partial-index condition cannot be evaluated from durable Inheto record payloads.");
            if (!TryResolveSimplePropertyType(keyPath, out var keyType, out Inheto.PropPathCheck pathCheck))
                throw new InvalidOperationException($"Partial-index key path '{keyPath}' does not resolve on '{typeof(T).FullName}'.");
            keyPath = pathCheck.CanonicalPath;

            keyType = NormalizeValueType(keyType);
            if (keyType == typeof(string))
                textProfile ??= TextIndexProfile.Exact;
            var fingerprint = descriptor.SemanticFingerprint;
            logicalName = string.IsNullOrWhiteSpace(logicalName)
                ? BuildPartialLogicalName(keyPath, fingerprint)
                : logicalName.Trim();
            var physicalName = BuildPartialPhysicalName(storeID, logicalName, keyPath, fingerprint, unique, textProfile, direction);
            var package = descriptor.Serialize();

            ThrowIfLibraDexLogicalNameBackingConflicts(logicalName, ownerCatalog);

            lock (partialIndexLifecycleLock)
            {
                var current = libraDexPartialIndexes;
                for (var i = 0; i < current.Length; i++)
                {
                    var existing = current[i];
                    if (!string.Equals(existing.LogicalName, logicalName, StringComparison.Ordinal))
                        continue;
                    if (!ReferenceEquals(existing.OwnerCatalog, ownerCatalog))
                        throw new IndexContractException(IndexContractFailureKind.Configuration, $"Partial index '{logicalName}' is already attached with the other backing kind.", existing.PhysicalName, logicalName);
                    if (!string.Equals(existing.KeyPath, keyPath, StringComparison.Ordinal) ||
                        !string.Equals(existing.Descriptor.SemanticFingerprint, fingerprint, StringComparison.Ordinal) ||
                        existing.Unique != unique || existing.Direction != direction ||
                        (keyType == typeof(string) ? existing.TextProfile ?? TextIndexProfile.Exact : existing.TextProfile) != textProfile)
                    {
                        throw new InvalidOperationException(
                            $"Partial-index logical name '{logicalName}' already identifies a different definition.");
                    }
                    return false;
                }

                var hasCustomCodec = TryGetCustomBinaryCodec(keyType, keyPath, out var customCodec);
                var preparedCodecBinding = false;
                IIndex? index = null;
                LibraDexPartialIndex? runtime = null;

                try
                {
                    if (hasCustomCodec && ownerCatalog is null)
                    {
                        var indexes = libraDexIdentityIndexes
                            ?? throw new InvalidOperationException("Persistent LibraDex indexes are not bound for this store.");
                        var expected = CreateApplicationCodecBinding(
                            customCodec,
                            ApplicationCodecIndexKind.Partial,
                            logicalName,
                            physicalName,
                            keyPath,
                            keyType,
                            ready: true);
                        if (indexes.HasIndex(physicalName))
                        {
                            RequireReadyApplicationCodecBinding(expected);
                        }
                        else
                        {
                            PersistPreparingApplicationCodecBinding(expected);
                            preparedCodecBinding = true;
                        }
                    }

                    index = CreateOrOpenPartialPhysicalIndex(
                        physicalName,
                        keyPath,
                        keyType,
                        unique,
                        textProfile,
                        direction,
                        ownerCatalog);
                    ClearPartialPhysicalIndex(index, ownerCatalog);
                    runtime = new LibraDexPartialIndex(
                        logicalName,
                        physicalName,
                        keyPath,
                        keyType,
                        unique,
                        BuildPartialCapabilities(keyType, keyPath, unique),
                        index,
                        descriptor,
                        evaluator,
                        textProfile,
                        direction,
                        appliedSourceUsn: 0,
                        ready: false,
                        TryGetPartialCustomKeyReader(keyType, keyPath),
                        TryGetPartialCustomPayloadReader(keyType, keyPath),
                        CreateClrContinuationReader(keyPath, keyType),
                        ownerCatalog);
                    BackfillPartialIndex(runtime);
                    var sourceUsn = rbase.data.GetUSN(storeID);
                    runtime.SetAppliedSourceUsn(sourceUsn);
                    runtime.SetReady(true);
                    if (preparedCodecBinding)
                    {
                        PersistApplicationCodecBinding(
                            customCodec,
                            ApplicationCodecIndexKind.Partial,
                            logicalName,
                            physicalName,
                            keyPath,
                            keyType,
                            ready: true);
                    }
                    if (ownerCatalog is null)
                        rbase.UpsertPartialIndexCatalogEntry(runtime.ToCatalogEntry(storeID, package));
                    PublishPartialIndex(runtime);
                    return true;
                }
                catch
                {
                    runtime?.SetReady(false);
                    if (index is not null)
                        ClearPartialPhysicalIndex(index, ownerCatalog);
                    if (preparedCodecBinding)
                    {
                        try
                        {
                            rbase.RemoveApplicationCodecBinding(
                                storeID,
                                ApplicationCodecIndexKind.Partial,
                                logicalName);
                        }
                        catch
                        {
                        }
                    }
                    throw;
                }
            }
        }

        /// <summary>
        /// Rehydrates and attaches every durable partial-index definition for this store when its persistent LibraDex group is bound.<br/>
        /// A matching persisted source USN permits direct attachment; missing, stale, or unavailable readiness proof forces a strict clear-and-rebuild before publication.<br/>
        /// </summary>
        private void RegisterExistingLibraDexPartialIndexes()
        {
            if (libraDexIdentityIndexes is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return;

            var catalogEntries = rbase.GetPartialIndexCatalogEntries(storeID);
            lock (partialIndexLifecycleLock)
            {
                for (var i = 0; i < catalogEntries.Length; i++)
                {
                    var catalogEntry = catalogEntries[i];
                    try
                    {
                        var descriptor = ConditionDescriptor<T>.Deserialize(catalogEntry.ConditionPackage);
                        if (!descriptor.IsSystemPersistable ||
                            !string.Equals(descriptor.SemanticFingerprint, catalogEntry.ConditionFingerprint, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                $"Partial index '{catalogEntry.LogicalName}' has an incomplete or contradictory condition package.");
                        }
                        var condition = descriptor.Rehydrate().Build();
                        if (!condition.builder.TryBuildSemanticEvaluator(NativeDeserializationOptions, out var evaluator))
                            throw new NotSupportedException($"Partial index '{catalogEntry.LogicalName}' condition is not payload-evaluable.");
                        var keyType = ConditionWireTypes.Resolve(catalogEntry.KeyTypeCode);
                        var textProfile = catalogEntry.TextProfile < 0
                            ? (TextIndexProfile?)null
                            : (TextIndexProfile)catalogEntry.TextProfile;
                        var direction = (IndexDirection)catalogEntry.Direction;
                        var hasRuntimeCodec = TryGetCustomBinaryCodec(
                            keyType,
                            catalogEntry.KeyPath,
                            out var customCodec);
                        if (rbase.TryGetApplicationCodecBinding(
                                storeID,
                                ApplicationCodecIndexKind.Partial,
                                catalogEntry.LogicalName,
                                out var persistedCodecBinding) &&
                            !hasRuntimeCodec)
                        {
                            throw new IndexContractException(
                                IndexContractFailureKind.CodecIdentity,
                                $"Partial index '{catalogEntry.LogicalName}' requires application codec '{persistedCodecBinding.FormatId}' version {persistedCodecBinding.FormatVersion}, but runtime setup did not register a compatible Inheto binary index encoding.",
                                catalogEntry.LogicalName,
                                catalogEntry.KeyPath);
                        }
                        if (hasRuntimeCodec)
                        {
                            RequireReadyApplicationCodecBinding(
                                CreateApplicationCodecBinding(
                                    customCodec,
                                    ApplicationCodecIndexKind.Partial,
                                    catalogEntry.LogicalName,
                                    catalogEntry.PhysicalName,
                                    catalogEntry.KeyPath,
                                    keyType,
                                    ready: true));
                        }
                        var index = CreateOrOpenPartialPhysicalIndex(
                            catalogEntry.PhysicalName,
                            catalogEntry.KeyPath,
                            keyType,
                            catalogEntry.Unique,
                            textProfile,
                            direction);
                        var runtime = new LibraDexPartialIndex(
                            catalogEntry.LogicalName,
                            catalogEntry.PhysicalName,
                            catalogEntry.KeyPath,
                            keyType,
                            catalogEntry.Unique,
                            BuildPartialCapabilities(keyType, catalogEntry.KeyPath, catalogEntry.Unique),
                            index,
                            descriptor,
                            evaluator,
                            textProfile,
                            direction,
                            catalogEntry.AppliedSourceUsn,
                            ready: false,
                            TryGetPartialCustomKeyReader(keyType, catalogEntry.KeyPath),
                            TryGetPartialCustomPayloadReader(keyType, catalogEntry.KeyPath),
                            CreateClrContinuationReader(catalogEntry.KeyPath, keyType));

                        var sourceUsn = rbase.data.GetUSN(storeID);
                        if (!catalogEntry.Ready || catalogEntry.AppliedSourceUsn != sourceUsn)
                        {
                            ClearPartialPhysicalIndex(index);
                            BackfillPartialIndex(runtime);
                            sourceUsn = rbase.data.GetUSN(storeID);
                            runtime.SetAppliedSourceUsn(sourceUsn);
                            runtime.SetReady(true);
                            rbase.UpsertPartialIndexCatalogEntry(
                                runtime.ToCatalogEntry(storeID, catalogEntry.ConditionPackage));
                        }
                        else
                        {
                            runtime.SetReady(true);
                        }

                        PublishPartialIndex(runtime);
                    }
                    catch (Exception exception)
                    {
                        catalogEntry.Ready = false;
                        try { rbase.UpsertPartialIndexCatalogEntry(catalogEntry); } catch { }
                        throw new InvalidDataException(
                            $"Persistent partial index '{catalogEntry.LogicalName}' could not be rehydrated and rebuilt safely.",
                            exception);
                    }
                }
            }
        }

        /// <summary>
        /// Advances ready memory-backed partial indexes after a successful persistent-catalog rebind and convergence check.<br/>
        /// Catalog compaction can advance the authoritative source USN without changing records or the surviving volatile tuples; failed or unavailable entries remain unavailable and are never marked ready here.<br/>
        /// </summary>
        private void AdvanceMemoryPartialWatermarksAfterPersistentRebind()
        {
            lock (partialIndexLifecycleLock)
            {
                ulong sourceUsn = rbase.data.GetUSN(storeID);
                if (!storeRuntime.IsIndexSourceUsnApplied(sourceUsn))
                    return;
                var entries = libraDexPartialIndexes;
                for (var i = 0; i < entries.Length; i++)
                {
                    LibraDexPartialIndex entry = entries[i];
                    if (entry.OwnerCatalog is not null && entry.IsReady)
                        entry.SetAppliedSourceUsn(sourceUsn);
                }
            }
        }

        /// <summary>
        /// Strictly backfills one unpublished partial index from authoritative Fractal/Inheto records.<br/>
        /// Payload, predicate, key, and uniqueness failures abort the entire build; no record is silently skipped.<br/>
        /// </summary>
        /// <param name="entry">Unpublished runtime index definition to populate.<br/></param>
        private void BackfillPartialIndex(LibraDexPartialIndex entry)
        {
            foreach (var recordID in IterateRecordIDs())
            {
                var binary = GetInhetoBinary(recordID, nameof(BackfillPartialIndex));
                if (!entry.MembershipEvaluator(binary))
                    continue;
                if (entry.CustomPayloadReader is not null)
                {
                    if (!TryReadBorrowedPartialIndexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                        throw new InvalidDataException(
                            $"Partial index '{entry.LogicalName}' could not read fixed-binary key path '{entry.KeyPath}' for record '{recordID}'.");

                    LibraDexGenericInsertResult borrowedResult = RequirePartialFixedBinaryIndex(entry).Insert(payload.Span, recordID);
                    if (!borrowedResult.Inserted &&
                        !RequirePartialFixedBinaryIndex(entry).ContainsTuple(payload.Span, recordID))
                    {
                        throw new IndexContractException(
                            entry.Unique
                                ? IndexContractFailureKind.UniqueConstraint
                                : IndexContractFailureKind.BackendMutation,
                            $"Partial index '{entry.LogicalName}' rejected its fixed-binary key for record '{recordID}'.",
                            entry.LogicalName,
                            entry.KeyPath,
                            recordID);
                    }
                    continue;
                }

                LibraDexSimpleIndex scalarBridge = CreatePartialSimpleIndexBridge(entry);
                if (entry.Unique)
                    _ = TryValidateBorrowedLibraDexString(binary, scalarBridge, recordID, entry.LogicalName);
                if (TryInsertBorrowedLibraDexString(binary, scalarBridge, recordID, out LibraDexGenericInsertResult stringResult))
                {
                    EnsureBorrowedLibraDexStringInsertAccepted(binary, scalarBridge, recordID, stringResult);
                    continue;
                }

                if (TryInsertTypedLibraDexScalar(binary, scalarBridge, recordID, out LibraDexGenericInsertResult typedResult))
                {
                    if (!typedResult.Inserted)
                    {
                        if (!TryReadPartialIndexKey(binary, entry, out object typedKey))
                            throw new InvalidDataException(
                                $"Partial index '{entry.LogicalName}' could not reread a rejected typed scalar key for record '{recordID}'.");

                        EnsureLibraDexInsertAccepted(scalarBridge, typedKey, recordID, typedResult);
                    }
                    continue;
                }

                if (!TryReadPartialIndexKey(binary, entry, out var key))
                    throw new InvalidDataException(
                        $"Partial index '{entry.LogicalName}' could not read key path '{entry.KeyPath}' for record '{recordID}'.");

                var result = entry.Index.Insert(key, recordID);
                if (!result.Inserted)
                {
                    throw new IndexContractException(
                        entry.Unique
                            ? IndexContractFailureKind.UniqueConstraint
                            : IndexContractFailureKind.BackendMutation,
                        $"Partial index '{entry.LogicalName}' rejected key '{key ?? "<null>"}' for record '{recordID}'. " +
                        "A unique-key collision or physical insertion failure occurred.",
                        entry.LogicalName,
                        entry.KeyPath,
                        recordID);
                }
            }
        }

        /// <summary>
        /// Applies the newly persisted payload to every ready partial index and advances each in-memory source-USN watermark.<br/>
        /// Any predicate, key, or insertion failure marks the affected index unavailable before the exception escapes.<br/>
        /// </summary>
        /// <param name="recordID">Persisted record identity.<br/></param>
        /// <param name="binary">New authoritative Inheto payload.<br/></param>
        private void TryApplyLibraDexPartialUpsert(ulong recordID, InhetoBinary binary)
        {
            lock (partialIndexLifecycleLock)
            {
                var entries = libraDexPartialIndexes;
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (!entry.IsReady)
                        continue;
                    try
                    {
                        if (entry.MembershipEvaluator(binary))
                        {
                            if (entry.CustomPayloadReader is not null)
                            {
                                if (!TryReadBorrowedPartialIndexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                                    throw new InvalidDataException(
                                        $"Could not read partial-index fixed-binary key '{entry.KeyPath}' for record '{recordID}'.");

                                IFixedBinaryKeyIndex<ulong> fixedIndex = RequirePartialFixedBinaryIndex(entry);
                                LibraDexGenericInsertResult borrowedResult = fixedIndex.Insert(payload.Span, recordID);
                                if (!borrowedResult.Inserted && !fixedIndex.ContainsTuple(payload.Span, recordID))
                                {
                                    throw new IndexContractException(
                                        entry.Unique
                                            ? IndexContractFailureKind.UniqueConstraint
                                            : IndexContractFailureKind.BackendMutation,
                                        $"Partial index '{entry.LogicalName}' rejected its fixed-binary key for record '{recordID}'.",
                                        entry.LogicalName,
                                        entry.KeyPath,
                                        recordID);
                                }

                                entry.SetAppliedSourceUsn(rbase.data.GetUSN(storeID));
                                continue;
                            }

                            LibraDexSimpleIndex scalarBridge = CreatePartialSimpleIndexBridge(entry);
                            if (TryInsertBorrowedLibraDexString(binary, scalarBridge, recordID, out LibraDexGenericInsertResult stringResult))
                            {
                                EnsureBorrowedLibraDexStringInsertAccepted(binary, scalarBridge, recordID, stringResult);
                                entry.SetAppliedSourceUsn(rbase.data.GetUSN(storeID));
                                continue;
                            }

                            if (TryInsertTypedLibraDexScalar(binary, scalarBridge, recordID, out LibraDexGenericInsertResult typedResult))
                            {
                                if (!typedResult.Inserted)
                                {
                                    if (!TryReadPartialIndexKey(binary, entry, out object typedKey))
                                        throw new InvalidDataException(
                                            $"Could not reread rejected partial-index typed scalar key '{entry.KeyPath}' for record '{recordID}'.");

                                    EnsureLibraDexInsertAccepted(scalarBridge, typedKey, recordID, typedResult);
                                }

                                entry.SetAppliedSourceUsn(rbase.data.GetUSN(storeID));
                                continue;
                            }

                            if (!TryReadPartialIndexKey(binary, entry, out var key))
                                throw new InvalidDataException(
                                    $"Could not read partial-index key '{entry.KeyPath}' for record '{recordID}'.");
                            var result = entry.Index.Insert(key, recordID);
                            if (!result.Inserted && !entry.Index.Identities.Exists(recordID))
                            {
                                throw new IndexContractException(
                                    entry.Unique
                                        ? IndexContractFailureKind.UniqueConstraint
                                        : IndexContractFailureKind.BackendMutation,
                                    $"Partial index '{entry.LogicalName}' rejected key '{key ?? "<null>"}' for record '{recordID}'.",
                                    entry.LogicalName,
                                    entry.KeyPath,
                                    recordID);
                            }
                        }
                        entry.SetAppliedSourceUsn(rbase.data.GetUSN(storeID));
                    }
                    catch (IndexContractException)
                    {
                        MarkPartialIndexUnavailable(entry);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        MarkPartialIndexUnavailable(entry);
                        throw new IndexContractException(
                            IndexContractFailureKind.BackendMutation,
                            $"Partial index '{entry.LogicalName}' became unavailable while applying record '{recordID}'.",
                            entry.LogicalName,
                            entry.KeyPath,
                            recordID,
                            exception);
                    }
                }
            }
        }

        /// <summary>
        /// Preflights every ready unique partial index before an authoritative record write can replace the prior payload.<br/>
        /// An unchanged key owned by the same updating record is permitted; any other existing indexed key rejects the write before Fractal or any maintained index is mutated.<br/>
        /// </summary>
        /// <param name="recordID">Identity that will own the new payload.<br/></param>
        /// <param name="binary">Proposed serialized payload.<br/></param>
        /// <param name="isNewRecord">Whether no prior authoritative payload exists for this identity.<br/></param>
        private void ValidateLibraDexPartialUniqueUpsert(
            ulong recordID,
            InhetoBinary binary,
            bool isNewRecord)
        {
            lock (partialIndexLifecycleLock)
            {
                var entries = libraDexPartialIndexes;
                InhetoBinary? priorBinary = null;
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (!entry.IsReady || !entry.Unique || !entry.MembershipEvaluator(binary))
                        continue;
                    if (entry.CustomPayloadReader is not null)
                    {
                        if (!TryReadBorrowedPartialIndexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                            throw new InvalidDataException(
                                $"Could not read proposed unique partial-index fixed-binary key '{entry.KeyPath}' for record '{recordID}'.");
                        if (RequirePartialFixedBinaryIndex(entry).ContainsOtherIdentity(payload.Span, recordID))
                        {
                            throw new IndexContractException(
                                IndexContractFailureKind.UniqueConstraint,
                                $"Unique partial index '{entry.LogicalName}' already contains the supplied non-null key for another record.",
                                entry.LogicalName,
                                entry.KeyPath,
                                recordID);
                        }
                        continue;
                    }

                    LibraDexSimpleIndex scalarBridge = CreatePartialSimpleIndexBridge(entry);
                    if (TryValidateBorrowedLibraDexString(binary, scalarBridge, recordID, entry.LogicalName))
                        continue;

                    if (TryValidateTypedLibraDexScalar(binary, scalarBridge, recordID))
                        continue;

                    if (!TryReadPartialIndexKey(binary, entry, out var proposedKey))
                        throw new InvalidDataException(
                            $"Could not read proposed unique partial-index key '{entry.KeyPath}' for record '{recordID}'.");
                    if (proposedKey is null)
                        continue;

                    var retainsOwnKey = false;
                    if (!isNewRecord)
                    {
                        priorBinary ??= GetInhetoBinary(recordID, nameof(ValidateLibraDexPartialUniqueUpsert));
                        if (entry.MembershipEvaluator(priorBinary) &&
                            TryReadPartialIndexKey(priorBinary, entry, out var priorKey))
                        {
                            retainsOwnKey = PartialIndexKeysEqual(priorKey, proposedKey);
                        }
                    }

                    if (!retainsOwnKey && entry.Index.Keys.Exists(proposedKey))
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.UniqueConstraint,
                            $"Unique partial index '{entry.LogicalName}' already contains the supplied non-null key for another record.",
                            entry.LogicalName,
                            entry.KeyPath,
                            recordID);
                    }
                }
            }
        }

        /// <summary>
        /// Compares boxed partial-index keys while preserving structural equality for fixed or variable binary keys.<br/>
        /// </summary>
        /// <param name="left">Existing key value.<br/></param>
        /// <param name="right">Proposed key value.<br/></param>
        /// <returns><c>true</c> when the keys are semantically equal for update preflight.<br/></returns>
        private static bool PartialIndexKeysEqual(object? left, object? right)
            => left is byte[] leftBytes && right is byte[] rightBytes
                ? leftBytes.AsSpan().SequenceEqual(rightBytes)
                : Equals(left, right);

        /// <summary>
        /// Removes an existing payload's exact tuple from every ready partial index whose membership predicate it satisfies.<br/>
        /// The source-USN watermark is finalized only after the caller confirms the authoritative Fractal deletion.<br/>
        /// </summary>
        /// <param name="recordID">Existing record identity.<br/></param>
        /// <param name="binary">Existing authoritative Inheto payload.<br/></param>
        private void TryApplyLibraDexPartialDelete(ulong recordID, InhetoBinary binary)
        {
            lock (partialIndexLifecycleLock)
            {
                var entries = libraDexPartialIndexes;
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (!entry.IsReady)
                        continue;
                    try
                    {
                        if (!entry.MembershipEvaluator(binary))
                            continue;
                        if (entry.CustomPayloadReader is not null)
                        {
                            if (!TryReadBorrowedPartialIndexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                                throw new InvalidDataException(
                                    $"Could not read partial-index fixed-binary key '{entry.KeyPath}' for record '{recordID}'.");
                            if (!RequirePartialFixedBinaryIndex(entry).Delete(payload.Span, recordID))
                                throw new InvalidDataException(
                                    $"Partial index '{entry.LogicalName}' did not contain the expected fixed-binary tuple for record '{recordID}'.");
                            continue;
                        }

                        LibraDexSimpleIndex scalarBridge = CreatePartialSimpleIndexBridge(entry);
                        if (TryDeleteBorrowedLibraDexString(binary, scalarBridge, recordID))
                            continue;

                        if (TryDeleteTypedLibraDexScalar(binary, scalarBridge, recordID))
                            continue;

                        if (!TryReadPartialIndexKey(binary, entry, out var key))
                            throw new InvalidDataException(
                                $"Could not read partial-index key '{entry.KeyPath}' for record '{recordID}'.");
                        if (!entry.Index.Delete(key, recordID))
                            throw new InvalidDataException(
                                $"Partial index '{entry.LogicalName}' did not contain the expected tuple for record '{recordID}'.");
                    }
                    catch (IndexContractException)
                    {
                        MarkPartialIndexUnavailable(entry);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        MarkPartialIndexUnavailable(entry);
                        throw new IndexContractException(
                            IndexContractFailureKind.BackendMutation,
                            $"Partial index '{entry.LogicalName}' became unavailable while deleting record '{recordID}'.",
                            entry.LogicalName,
                            entry.KeyPath,
                            recordID,
                            exception);
                    }
                }
            }
        }

        /// <summary>
        /// Advances all still-ready partial indexes to the current source USN after an authoritative delete succeeds.<br/>
        /// </summary>
        private void FinalizeLibraDexPartialDelete()
        {
            lock (partialIndexLifecycleLock)
            {
                var sourceUsn = rbase.data.GetUSN(storeID);
                var entries = libraDexPartialIndexes;
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].IsReady)
                        entries[i].SetAppliedSourceUsn(sourceUsn);
                }
            }
        }

        /// <summary>
        /// Clears every ready partial index after a store-wide authoritative deletion and advances its source-USN watermark.<br/>
        /// </summary>
        private void FinalizeLibraDexPartialDeleteAll()
        {
            lock (partialIndexLifecycleLock)
            {
                var sourceUsn = rbase.data.GetUSN(storeID);
                var entries = libraDexPartialIndexes;
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (!entry.IsReady)
                        continue;
                    ClearPartialPhysicalIndex(entry.Index, entry.OwnerCatalog);
                    entry.SetAppliedSourceUsn(sourceUsn);
                }
            }
        }

        /// <summary>
        /// Returns whether one ready partial-index definition is current for the authoritative source-store USN.<br/>
        /// </summary>
        /// <param name="entry">Runtime partial-index entry to verify.<br/></param>
        /// <returns><c>true</c> only when readiness and exact USN parity are both present.<br/></returns>
        private bool IsPartialIndexUsable(LibraDexPartialIndex entry)
            => entry.IsReady && entry.AppliedSourceUsn == rbase.data.GetUSN(storeID);

        /// <summary>
        /// Resolves one logical query key path to a ready owner-scoped partial index only when its registered static predicate is structurally implied by the complete query.<br/>
        /// Multiple qualifying definitions are deterministic by logical name; unproven, stale, and non-string aliases fail closed in this first planner slice.<br/>
        /// </summary>
        /// <param name="queryDescriptor">Complete static query semantics used for conservative implication proof.<br/></param>
        /// <param name="logicalIndexName">Direct property-path identity emitted by the condition leaf.<br/></param>
        /// <param name="ownerCatalog">Volatile owner, or <see langword="null"/> for a file-backed candidate.<br/></param>
        /// <param name="entry">Authorized current partial-index runtime entry when successful.<br/></param>
        /// <returns><c>true</c> when a safe alias is available; otherwise <c>false</c>.<br/></returns>
        private bool TryResolvePartialIndexAlias(
            ConditionDescriptor<T> queryDescriptor,
            string logicalIndexName,
            Catalog? ownerCatalog,
            out LibraDexPartialIndex? entry)
        {
            entry = null;
            if (queryDescriptor is null || string.IsNullOrWhiteSpace(logicalIndexName))
                return false;

            var entries = libraDexPartialIndexes;
            for (var i = 0; i < entries.Length; i++)
            {
                var candidate = entries[i];
                if (!ReferenceEquals(candidate.OwnerCatalog, ownerCatalog) ||
                    candidate.KeyType != typeof(string) ||
                    !string.Equals(candidate.KeyPath, logicalIndexName, StringComparison.Ordinal) ||
                    !IsPartialIndexUsable(candidate) ||
                    !candidate.Descriptor.IsImpliedBy(queryDescriptor))
                {
                    continue;
                }

                if (entry is null ||
                    string.CompareOrdinal(candidate.LogicalName, entry.LogicalName) < 0)
                {
                    entry = candidate;
                }
            }

            return entry is not null;
        }

        /// <summary>
        /// Marks one runtime and durable partial-index definition unavailable so every planner path fails closed.<br/>
        /// Persistence is best-effort because the original mutation failure remains the primary developer-visible error.<br/>
        /// </summary>
        /// <param name="entry">Affected runtime index.<br/></param>
        private void MarkPartialIndexUnavailable(LibraDexPartialIndex entry)
        {
            entry.SetReady(false);
            if (entry.OwnerCatalog is not null)
                return;
            try
            {
                var package = entry.Descriptor.Serialize();
                rbase.UpsertPartialIndexCatalogEntry(entry.ToCatalogEntry(storeID, package));
            }
            catch
            {
            }
        }

        /// <summary>
        /// Marks every published partial index unavailable after an authoritative write fails inside the interval between old-tuple removal and completed new-tuple maintenance.<br/>
        /// This deliberately fails planner use closed because the storage exception may leave either the old or new Fractal payload authoritative while one or more physical partial indexes reflect neither complete state.<br/>
        /// Each definition remains durably inspectable and is rebuilt from authoritative Fractal/Inheto payloads when the store is reopened.<br/>
        /// </summary>
        private void MarkAllPartialIndexesUnavailable()
        {
            lock (partialIndexLifecycleLock)
            {
                var entries = libraDexPartialIndexes;
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].IsReady)
                        MarkPartialIndexUnavailable(entries[i]);
                }
            }
        }

        /// <summary>
        /// Publishes one fully built runtime entry by replacing a matching logical name or appending a new immutable registry cell.<br/>
        /// </summary>
        /// <param name="entry">Ready or explicitly unavailable entry to publish.<br/></param>
        private void PublishPartialIndex(LibraDexPartialIndex entry)
        {
            var current = libraDexPartialIndexes;
            for (var i = 0; i < current.Length; i++)
            {
                if (!string.Equals(current[i].LogicalName, entry.LogicalName, StringComparison.Ordinal))
                    continue;
                var replacement = (LibraDexPartialIndex[])current.Clone();
                replacement[i] = entry;
                libraDexPartialIndexes = replacement;
                return;
            }

            var appended = new LibraDexPartialIndex[current.Length + 1];
            Array.Copy(current, appended, current.Length);
            appended[current.Length] = entry;
            libraDexPartialIndexes = appended;
        }

        /// <summary>
        /// Removes one persistent partial index as a single Abraxas-owned lifecycle operation.<br/>
        /// Durable condition and codec intent are removed before the physical LibraDex definition, ensuring an interrupted drop cannot reattach the index on reopen.<br/>
        /// </summary>
        /// <param name="index">Store-owned public index handle selected by logical name or key path.<br/></param>
        /// <returns><see langword="true"/> when a matching partial index was removed; otherwise <see langword="false"/>.<br/></returns>
        private bool TryRemoveLibraDexPartialIndex(RecordIndex index)
        {
            var entries = libraDexPartialIndexes;
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!string.Equals(entry.LogicalName, index.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                entry.SetReady(false);
                if (entry.OwnerCatalog is null)
                {
                    rbase.RemovePartialIndexCatalogEntry(storeID, entry.LogicalName);
                    rbase.RemoveApplicationCodecBinding(
                        storeID,
                        ApplicationCodecIndexKind.Partial,
                        entry.LogicalName);
                }

                Exception? dropFailure = null;
                try
                {
                    _ = (entry.OwnerCatalog is null
                        ? libraDexIdentityIndexes
                        : entry.OwnerCatalog.Indexes.IndexSet(indexGroupName))?.Drop(entry.PhysicalName);
                }
                catch (Exception exception)
                {
                    dropFailure = exception;
                }

                var replacement = new LibraDexPartialIndex[entries.Length - 1];
                if (i != 0)
                    Array.Copy(entries, 0, replacement, 0, i);
                if (i + 1 < entries.Length)
                    Array.Copy(entries, i + 1, replacement, i, entries.Length - i - 1);
                libraDexPartialIndexes = replacement;

                if (dropFailure is not null)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.BackendMutation,
                        $"Partial index '{entry.LogicalName}' detached its Abraxas definition but could not drop its physical ordered-backend definition.",
                        entry.LogicalName,
                        entry.KeyPath,
                        innerException: dropFailure);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates or opens the reserved physical LibraDex shape for one partial-index definition.<br/>
        /// The physical name is never the developer key path, preventing generic reopen discovery from misclassifying it as a full index.<br/>
        /// </summary>
        /// <param name="physicalName">Reserved physical LibraDex identity derived from the complete index definition.<br/></param>
        /// <param name="keyPath">The exact record property path whose effective custom encoding may define the fixed key shape.<br/></param>
        /// <param name="keyType">Normalized CLR type read from the indexed property path.<br/></param>
        /// <param name="unique">Whether the physical shape rejects duplicate keys.<br/></param>
        /// <param name="textProfile">Text projection profile for string keys, or <c>null</c> for scalar keys.<br/></param>
        /// <param name="direction">Requested ordered traversal direction.<br/></param>
        /// <param name="ownerCatalog">Volatile owner, or <see langword="null"/> for the persistent group.<br/></param>
        /// <returns>The opened or newly created physical index handle.<br/></returns>
        private IIndex CreateOrOpenPartialPhysicalIndex(
            string physicalName,
            string keyPath,
            Type keyType,
            bool unique,
            TextIndexProfile? textProfile,
            IndexDirection direction,
            Catalog? ownerCatalog = null)
        {
            var indexes = ownerCatalog is null
                ? libraDexIdentityIndexes ?? throw new InvalidOperationException("Persistent LibraDex indexes are not bound for this store.")
                : ownerCatalog.Indexes.IndexSet(indexGroupName);
            // Partial-index uniqueness follows Abraxas's non-null-key contract. A non-unique physical
            // route preserves every null identity while the guarded preflight rejects non-null collisions.
            var keyContract = IndexKeys.NonUnique;
            var sortOrder = direction == IndexDirection.Descending
                ? LibraDexIndexSortOrder.Descending
                : LibraDexIndexSortOrder.Ascending;
            var options = new IndexOptions { Keys = keyContract, SortOrder = sortOrder };

            if (keyType == typeof(string))
            {
                var profile = textProfile ?? TextIndexProfile.Exact;
                return indexes.Define(physicalName)
                    .StringKeys()
                    .CreateOrOpen(ToLibraDexStringKeys(profile), ToLibraDexProjectionDirections(direction), sortOrder: sortOrder);
            }
            if (keyType == typeof(int))
                return indexes.Define(physicalName).Int32Keys<ulong>().CreateOrOpen(keyContract, options);
            if (keyType == typeof(long))
                return indexes.Define(physicalName).Int64Keys<ulong>().CreateOrOpen(keyContract, options);
            if (keyType == typeof(uint))
                return indexes.CreateOrOpen<uint, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(ulong))
                return indexes.CreateOrOpen<ulong, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(short))
                return indexes.CreateOrOpen<short, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(ushort))
                return indexes.CreateOrOpen<ushort, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(byte))
                return indexes.CreateOrOpen<byte, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(sbyte))
                return indexes.CreateOrOpen<sbyte, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(char))
                return indexes.CreateOrOpen<char, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(Int128))
                return indexes.CreateOrOpen<Int128, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(UInt128))
                return indexes.CreateOrOpen<UInt128, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(float))
                return indexes.Define(physicalName).SingleKeys<ulong>().CreateOrOpen(keyContract, options);
            if (keyType == typeof(double))
                return indexes.Define(physicalName).DoubleKeys<ulong>().CreateOrOpen(keyContract, options);
            if (keyType == typeof(decimal))
                return indexes.Define(physicalName).DecimalKeys<ulong>().CreateOrOpen(keyContract, options);
            if (keyType == typeof(bool))
                return indexes.CreateOrOpen<bool, ulong>(physicalName, keyContract, options);
            if (keyType == typeof(Guid))
                return indexes.Define(physicalName).GuidKeys<ulong>().CreateOrOpen(keyContract, options);
            if (keyType == typeof(DateTime))
                return indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<DateTime, ulong>(
                    DateKeys.ExactAndStructured,
                    DateTimeKeyEncoding.PrecisionSdt,
                    keyContract,
                    sortOrder: sortOrder), options);
            if (keyType == typeof(DateTimeOffset))
                return indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<DateTimeOffset, ulong>(
                    DateKeys.ExactAndStructured,
                    DateTimeKeyEncoding.PrecisionSdt,
                    keyContract,
                    sortOrder: sortOrder), options);
            if (keyType == typeof(DateOnly))
                return indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<DateOnly, ulong>(
                    DateKeys.ExactAndStructured,
                    keys: keyContract,
                    sortOrder: sortOrder), options);
            if (keyType == typeof(TimeOnly))
                return indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<TimeOnly, ulong>(
                    DateKeys.ExactAndStructured,
                    DateTimeKeyEncoding.PrecisionSdt,
                    keyContract,
                    sortOrder: sortOrder), options);
            if (keyType == typeof(TimeSpan))
                return indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<TimeSpan, ulong>(
                    DateKeys.Exact,
                    keys: keyContract,
                    sortOrder: sortOrder), options);
            if (TryGetCustomBinaryCodec(keyType, keyPath, out var codec))
                return indexes.Define(physicalName).Blob.Scalar<ulong>(codec.Width).CreateOrOpen(keyContract, options);

            throw new NotSupportedException(
                $"Partial-index key type '{keyType.FullName}' is not supported by the ordered backend.");
        }

        /// <summary>
        /// Clears all tuples from one reserved partial-index physical shape before rebuild or failure cleanup.<br/>
        /// </summary>
        /// <param name="index">Physical index to clear.<br/></param>
        /// <param name="ownerCatalog">Volatile owner, or <see langword="null"/> for the persistent group.<br/></param>
        private void ClearPartialPhysicalIndex(IIndex index, Catalog? ownerCatalog = null)
        {
            var indexes = ownerCatalog is null
                ? libraDexIdentityIndexes ?? throw new InvalidOperationException("Persistent LibraDex indexes are not bound for this store.")
                : ownerCatalog.Indexes.IndexSet(indexGroupName);
            _ = indexes.Define(index.Name).DeleteAll();
        }

        /// <summary>
        /// Reads one partial-index key through the same typed Inheto path used by ordinary maintained indexes.<br/>
        /// </summary>
        /// <param name="binary">Authoritative serialized record payload.<br/></param>
        /// <param name="entry">Partial-index definition describing the key path and type.<br/></param>
        /// <param name="key">Receives the typed key when the path can be read.<br/></param>
        /// <returns><c>true</c> when a key was read successfully; otherwise <c>false</c>.<br/></returns>
        private bool TryReadPartialIndexKey(InhetoBinary binary, LibraDexPartialIndex entry, out object key)
        {
            LibraDexSimpleIndex bridge = CreatePartialSimpleIndexBridge(entry);
            return TryReadLibraDexKey(binary, bridge, out key);
        }

        /// <summary>
        /// Projects one partial-index runtime descriptor into the stack-only ordinary-index shape shared by typed Inheto extraction and LibraDex mutation helpers.<br/>
        /// The projection retains the exact physical handle, key semantics, and custom readers without allocating or publishing a second runtime index object.<br/>
        /// </summary>
        /// <param name="entry">Partial-index runtime descriptor to project.<br/></param>
        /// <returns>A value-type bridge suitable for synchronous shared helper calls.<br/></returns>
        private static LibraDexSimpleIndex CreatePartialSimpleIndexBridge(LibraDexPartialIndex entry)
        {
            return new LibraDexSimpleIndex(
                entry.KeyPath,
                entry.KeyType,
                entry.Unique,
                entry.Capabilities,
                entry.Index,
                ownerCatalog: entry.OwnerCatalog,
                autoCreated: false,
                AutoIndexUse.None,
                DateTime.UtcNow,
                entry.CustomKeyReader,
                entry.CustomPayloadReader,
                calculatedReader: entry.CalculatedReader);
        }

        /// <summary>
        /// Borrows one custom fixed-width partial-index key directly from its authoritative Inheto payload.<br/>
        /// The memory remains owned by <paramref name="binary"/> and is consumed synchronously by LibraDex before this method's caller returns.<br/>
        /// </summary>
        private static bool TryReadBorrowedPartialIndexKey(
            InhetoBinary binary,
            LibraDexPartialIndex entry,
            out ReadOnlyMemory<byte> payload)
        {
            CustomBinaryPayloadReader reader = entry.CustomPayloadReader
                ?? throw new InvalidOperationException(
                    $"Partial index '{entry.LogicalName}' has no borrowed custom-binary reader.");
            return reader(binary, entry.KeyPath, out payload);
        }

        /// <summary>
        /// Requires the fixed-width span capability promised by a custom partial-index registration.<br/>
        /// </summary>
        private static IFixedBinaryKeyIndex<ulong> RequirePartialFixedBinaryIndex(LibraDexPartialIndex entry)
            => entry.Index as IFixedBinaryKeyIndex<ulong>
                ?? throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Partial index '{entry.LogicalName}' does not expose LibraDex's fixed-binary mutation contract.",
                    entry.LogicalName,
                    entry.KeyPath);

        /// <summary>
        /// Resolves the optional application codec reader used by a custom fixed-width partial-index key.<br/>
        /// </summary>
        /// <param name="keyType">Normalized CLR key type to inspect.<br/></param>
        /// <param name="keyPath">The exact key path used to prefer a property-specific encoding before a type-wide encoding.<br/></param>
        /// <returns>The registered payload reader for a supported custom type, or <c>null</c> for built-in types.<br/></returns>
        private CustomBinaryKeyReader? TryGetPartialCustomKeyReader(Type keyType, string keyPath)
            => TryGetCustomBinaryCodec(keyType, keyPath, out var codec) ? codec.ReadKey : null;

        /// <summary>
        /// Resolves the borrowed Inheto payload reader used by a custom fixed-width partial-index key.<br/>
        /// </summary>
        /// <param name="keyType">Normalized CLR key type to inspect.<br/></param>
        /// <param name="keyPath">The exact key path used to prefer a property-specific encoding before a type-wide encoding.<br/></param>
        /// <returns>The registered borrowed payload reader, or <c>null</c> for built-in types.<br/></returns>
        private CustomBinaryPayloadReader? TryGetPartialCustomPayloadReader(Type keyType, string keyPath)
            => TryGetCustomBinaryCodec(keyType, keyPath, out var codec) ? codec.ReadPayload : null;

        /// <summary>
        /// Computes public backend capabilities for one partial ordered index.<br/>
        /// </summary>
        /// <param name="keyType">Normalized CLR key type used by the physical index.<br/></param>
        /// <param name="keyPath">The exact key path used to resolve a property-specific or type-wide custom encoding.<br/></param>
        /// <param name="unique">Whether duplicate keys are prohibited.<br/></param>
        /// <returns>The equality, ordering, range, and uniqueness flags accurately supported by the physical shape.<br/></returns>
        private IndexBackendCapabilities BuildPartialCapabilities(Type keyType, string keyPath, bool unique)
        {
            var capabilities = IndexBackendCapabilities.Equality;
            if (!TryGetCustomBinaryCodec(keyType, keyPath, out var codec) || codec.OrderPreserving)
                capabilities |= IndexBackendCapabilities.Range | IndexBackendCapabilities.OrderedRead;
            if (unique)
                capabilities |= IndexBackendCapabilities.Unique;
            return capabilities;
        }

        /// <summary>
        /// Produces the deterministic low-friction logical name used when the developer does not supply one.<br/>
        /// </summary>
        /// <param name="keyPath">Developer-facing property path used as the index key.<br/></param>
        /// <param name="fingerprint">Stable semantic fingerprint of the membership condition.<br/></param>
        /// <returns>A deterministic logical management name.<br/></returns>
        private string BuildPartialLogicalName(string keyPath, string fingerprint)
            => "pidx_" + SanitizeTokenForIndex(keyPath, 36) + "_" + fingerprint[..8].ToLowerInvariant();

        /// <summary>
        /// Produces a reserved physical identity from every shape-defining property of a partial index.<br/>
        /// </summary>
        /// <param name="storeID">Owning Abraxas store identity.<br/></param>
        /// <param name="logicalName">Developer-facing or generated logical name.<br/></param>
        /// <param name="keyPath">Property path used as the index key.<br/></param>
        /// <param name="fingerprint">Stable membership-condition fingerprint.<br/></param>
        /// <param name="unique">Whether duplicate keys are prohibited.<br/></param>
        /// <param name="textProfile">Text projection profile for string keys, or <c>null</c> for scalar keys.<br/></param>
        /// <param name="direction">Requested ordered traversal direction.<br/></param>
        /// <returns>A reserved collision-resistant LibraDex physical identity.<br/></returns>
        private static string BuildPartialPhysicalName(
            byte storeID,
            string logicalName,
            string keyPath,
            string fingerprint,
            bool unique,
            TextIndexProfile? textProfile,
            IndexDirection direction)
        {
            var semantic = string.Join(
                "\n",
                storeID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                logicalName,
                keyPath,
                fingerprint,
                unique ? "unique" : "nonunique",
                textProfile?.ToString() ?? "scalar",
                direction.ToString());
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semantic))).ToLowerInvariant();
            return "__abx_partial_" + hash[..24];
        }

        /// <summary>
        /// Holds one runtime partial-index handle, its immutable membership semantics, and mutable readiness watermark.<br/>
        /// </summary>
        private sealed class LibraDexPartialIndex
        {
            private long appliedSourceUsn;
            private int ready;

            /// <summary>
            /// Captures one opened physical index together with its immutable persisted semantics and mutable readiness proof.<br/>
            /// </summary>
            /// <param name="logicalName">Developer-facing management identity.<br/></param>
            /// <param name="physicalName">Reserved LibraDex storage identity.<br/></param>
            /// <param name="keyPath">Property path used to extract index keys.<br/></param>
            /// <param name="keyType">Normalized CLR key type.<br/></param>
            /// <param name="unique">Whether duplicate keys are prohibited.<br/></param>
            /// <param name="capabilities">Accurate ordered-backend capability flags.<br/></param>
            /// <param name="index">Opened physical LibraDex index.<br/></param>
            /// <param name="descriptor">Complete static membership semantics.<br/></param>
            /// <param name="membershipEvaluator">Payload evaluator compiled from the descriptor.<br/></param>
            /// <param name="textProfile">String projection profile, or <c>null</c> for scalar keys.<br/></param>
            /// <param name="direction">Requested ordered traversal direction.<br/></param>
            /// <param name="appliedSourceUsn">Authoritative source USN represented by the physical tuples.<br/></param>
            /// <param name="ready">Whether planner paths may use the physical index.<br/></param>
            /// <param name="customKeyReader">Optional owned-key reader for custom fixed-width rebuild paths.<br/></param>
            /// <param name="customPayloadReader">Optional borrowed-payload reader for immediate custom fixed-width paths.<br/></param>
            /// <param name="calculatedReader">Optional Inheto-to-CLR continuation reader for an explicitly selected executable key path.<br/></param>
            /// <param name="ownerCatalog">Volatile owner, or <see langword="null"/> for the persistent definition.<br/></param>
            internal LibraDexPartialIndex(
                string logicalName,
                string physicalName,
                string keyPath,
                Type keyType,
                bool unique,
                IndexBackendCapabilities capabilities,
                IIndex index,
                ConditionDescriptor<T> descriptor,
                Func<InhetoBinary, bool> membershipEvaluator,
                TextIndexProfile? textProfile,
                IndexDirection direction,
                ulong appliedSourceUsn,
                bool ready,
                CustomBinaryKeyReader? customKeyReader,
                CustomBinaryPayloadReader? customPayloadReader,
                Func<InhetoBinary, (bool HasValue, object? Value)>? calculatedReader,
                Catalog? ownerCatalog = null)
            {
                LogicalName = logicalName;
                PhysicalName = physicalName;
                KeyPath = keyPath;
                KeyType = keyType;
                Unique = unique;
                Capabilities = capabilities;
                Index = index;
                Descriptor = descriptor;
                MembershipEvaluator = membershipEvaluator;
                TextProfile = textProfile;
                Direction = direction;
                this.appliedSourceUsn = unchecked((long)appliedSourceUsn);
                this.ready = ready ? 1 : 0;
                CustomKeyReader = customKeyReader;
                CustomPayloadReader = customPayloadReader;
                CalculatedReader = calculatedReader;
                OwnerCatalog = ownerCatalog;
            }

            internal string LogicalName { get; }
            internal string PhysicalName { get; }
            internal string KeyPath { get; }
            internal Type KeyType { get; }
            internal bool Unique { get; }
            internal IndexBackendCapabilities Capabilities { get; }
            internal IIndex Index { get; }
            internal ConditionDescriptor<T> Descriptor { get; }
            internal Func<InhetoBinary, bool> MembershipEvaluator { get; }
            internal TextIndexProfile? TextProfile { get; }
            internal IndexDirection Direction { get; }
            internal CustomBinaryKeyReader? CustomKeyReader { get; }
            internal CustomBinaryPayloadReader? CustomPayloadReader { get; }
            internal Func<InhetoBinary, (bool HasValue, object? Value)>? CalculatedReader { get; }
            internal Catalog? OwnerCatalog { get; }
            internal ulong AppliedSourceUsn => unchecked((ulong)Interlocked.Read(ref appliedSourceUsn));
            internal bool IsReady => Volatile.Read(ref ready) != 0;

            /// <summary>
            /// Atomically records the authoritative source USN represented by this physical index.<br/>
            /// </summary>
            /// <param name="value">Current authoritative Fractal store USN after successful maintenance.<br/></param>
            internal void SetAppliedSourceUsn(ulong value)
                => Interlocked.Exchange(ref appliedSourceUsn, unchecked((long)value));

            /// <summary>
            /// Atomically changes whether planner and management paths may use this physical index.<br/>
            /// </summary>
            /// <param name="value"><c>true</c> to publish planner eligibility; <c>false</c> to fail closed.<br/></param>
            internal void SetReady(bool value)
                => Volatile.Write(ref ready, value ? 1 : 0);

            /// <summary>
            /// Replaces only the physical index handle while preserving durable membership semantics, compiled evaluation, readiness, and source-USN proof.<br/>
            /// </summary>
            /// <param name="index">The exact reopened physical handle.<br/></param>
            /// <returns>A runtime descriptor identical to this instance except for its physical handle.<br/></returns>
            internal LibraDexPartialIndex WithIndex(IIndex index)
            {
                ArgumentNullException.ThrowIfNull(index);
                return new LibraDexPartialIndex(
                    LogicalName,
                    PhysicalName,
                    KeyPath,
                    KeyType,
                    Unique,
                    Capabilities,
                    index,
                    Descriptor,
                    MembershipEvaluator,
                    TextProfile,
                    Direction,
                    AppliedSourceUsn,
                    IsReady,
                    CustomKeyReader,
                    CustomPayloadReader,
                    CalculatedReader,
                    OwnerCatalog);
            }

            /// <summary>
            /// Creates a caller-owned durable catalog projection of this runtime entry.<br/>
            /// </summary>
            /// <param name="storeID">Owning Abraxas store identity.<br/></param>
            /// <param name="package">Serialized condition package to persist with the definition.<br/></param>
            /// <returns>A detached durable catalog entry whose byte arrays may be safely retained by the caller.<br/></returns>
            internal PartialIndexCatalogEntry ToCatalogEntry(byte storeID, byte[] package)
                => new()
                {
                    StoreID = storeID,
                    LogicalName = LogicalName,
                    PhysicalName = PhysicalName,
                    KeyPath = KeyPath,
                    KeyTypeCode = ConditionWireTypes.GetCode(KeyType),
                    Unique = Unique,
                    TextProfile = TextProfile.HasValue ? (int)TextProfile.Value : -1,
                    Direction = (int)Direction,
                    ConditionPackage = package.ToArray(),
                    ConditionFingerprint = Descriptor.SemanticFingerprint,
                    AppliedSourceUsn = AppliedSourceUsn,
                    Ready = IsReady
                };
        }

        /// <summary>
        /// Releases one monitor acquired by <see cref="EnterPartialIndexMutationScope"/> while tolerating repeated disposal by defensive caller code.<br/>
        /// </summary>
        private sealed class PartialIndexMutationScope : IDisposable
        {
            private readonly object gate;
            private readonly IDisposable storeMutation;
            private readonly IDisposable lifecycle;
            private int disposed;

            /// <summary>
            /// Captures the already-entered monitor that this scope owns.<br/>
            /// </summary>
            /// <param name="gate">Monitor object previously entered by the current thread.<br/></param>
            /// <param name="storeMutation">Store-generation mutation lease shared by every facade.<br/></param>
            /// <param name="lifecycle">RecordBase lifecycle lease held for the complete mutation window.<br/></param>
            internal PartialIndexMutationScope(object gate, IDisposable storeMutation, IDisposable lifecycle)
            {
                this.gate = gate;
                this.storeMutation = storeMutation;
                this.lifecycle = lifecycle;
            }

            /// <summary>
            /// Releases the owned monitor once.<br/>
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                    return;
                try
                {
                    Monitor.Exit(gate);
                }
                finally
                {
                    try
                    {
                        storeMutation.Dispose();
                    }
                    finally
                    {
                        lifecycle.Dispose();
                    }
                }
            }
        }
    }
}
