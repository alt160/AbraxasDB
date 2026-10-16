using LibraDex;

namespace AbraxasDB
{
    public partial class RecordBase
    {
        private const ulong StoreRegistryRecordId = 1;

        /// <summary>
        /// Soft-removes one active store without deleting its Fractal records, Abraxas intent, or LibraDex indexes.<br/>
        /// Existing facades are invalidated immediately after admitted operations drain; <see cref="RestoreStore(string)"/> makes the retained store available through a new runtime generation.<br/>
        /// </summary>
        /// <param name="name">The case-insensitive store name to remove.<br/></param>
        /// <returns><c>true</c> when an active store changed to soft-removed; <c>false</c> when it was already soft-removed or absent.<br/></returns>
        public bool RemoveStore(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            BeginMaintenanceAsync(nameof(RemoveStore), CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                StoreInfo? removed = null;
                var changed = false;
                using (EnterLibraDexLifecycleExclusiveScope(CancellationToken.None))
                {
                    lock (_storeCatalogLock)
                    {
                        var stores = ReadStoreInfos();
                        if (stores.TryGetValue(name, out var info))
                        {
                            if (info.Lifecycle == StoreLifecycleState.HardDeletePending ||
                                info.Lifecycle == StoreLifecycleState.PurgePending)
                            {
                                throw new InvalidOperationException($"Store '{name}' has an incomplete destructive lifecycle operation and cannot be soft-removed.");
                            }
                            if (info.Lifecycle == StoreLifecycleState.Active)
                            {
                                info.Lifecycle = StoreLifecycleState.SoftRemoved;
                                stores[name] = info;
                                WriteStoreInfos(stores);
                                removed = info;
                                changed = true;
                            }
                        }
                    }

                    if (removed is not null)
                        InvalidateStoreRuntime(removed, StoreLifecycleState.SoftRemoved);
                }

                CompleteMaintenanceAsync("Completed").GetAwaiter().GetResult();
                return changed;
            }
            catch (Exception exception)
            {
                try { CompleteMaintenanceAsync("Failed", exception.Message).GetAwaiter().GetResult(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Restores one soft-removed store without rebuilding retained Fractal records or LibraDex indexes.<br/>
        /// Restoration assigns a new runtime generation so facades invalidated by the earlier removal can never become usable again.<br/>
        /// </summary>
        /// <param name="name">The case-insensitive soft-removed store name to restore.<br/></param>
        /// <returns><c>true</c> when a soft-removed store became active; <c>false</c> when it was already active or absent.<br/></returns>
        public bool RestoreStore(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            BeginMaintenanceAsync(nameof(RestoreStore), CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                var restored = false;
                using (EnterLibraDexLifecycleExclusiveScope(CancellationToken.None))
                {
                    lock (_storeCatalogLock)
                    {
                        var stores = ReadStoreInfos();
                        if (stores.TryGetValue(name, out var info))
                        {
                            if (info.Lifecycle == StoreLifecycleState.HardDeletePending ||
                                info.Lifecycle == StoreLifecycleState.PurgePending)
                            {
                                throw new InvalidOperationException($"Store '{name}' has an incomplete destructive lifecycle operation and cannot be restored.");
                            }
                            if (info.Lifecycle == StoreLifecycleState.SoftRemoved)
                            {
                                info.InstanceID = Guid.NewGuid();
                                info.Lifecycle = StoreLifecycleState.Active;
                                stores[name] = info;
                                WriteStoreInfos(stores);
                                restored = true;
                            }
                        }
                    }
                }

                CompleteMaintenanceAsync("Completed").GetAwaiter().GetResult();
                return restored;
            }
            catch (Exception exception)
            {
                try { CompleteMaintenanceAsync("Failed", exception.Message).GetAwaiter().GetResult(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Irreversibly deletes one store's logical Fractal records, Abraxas-owned intent, and complete persistent or volatile LibraDex index reality.<br/>
        /// A durable pending state hides the store before destructive work begins and makes every cleanup step restartable; physical byte sanitization remains a separate future <c>PurgeStore</c> contract.<br/>
        /// The compact byte store identifier becomes reusable only after all logical cleanup and final registry removal succeed.<br/>
        /// </summary>
        /// <param name="name">The case-insensitive active or soft-removed store name to delete.<br/></param>
        /// <returns><c>true</c> when a store existed and its hard deletion completed; otherwise <c>false</c>.<br/></returns>
        public bool DeleteStore(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            BeginMaintenanceAsync(nameof(DeleteStore), CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                StoreInfo? deleting = null;
                using (EnterLibraDexLifecycleExclusiveScope(CancellationToken.None))
                {
                    lock (_storeCatalogLock)
                    {
                        var stores = ReadStoreInfos();
                        if (stores.TryGetValue(name, out var found))
                        {
                            deleting = found;
                            if (deleting.Lifecycle == StoreLifecycleState.PurgePending)
                                throw new InvalidOperationException($"Store '{name}' has an incomplete purge and cannot be hard-deleted through {nameof(DeleteStore)}.");
                            if (deleting.InstanceID == Guid.Empty)
                                deleting.InstanceID = Guid.NewGuid();
                            deleting.Lifecycle = StoreLifecycleState.HardDeletePending;
                            stores[name] = deleting;
                            WriteStoreInfos(stores);
                        }
                    }

                    if (deleting is not null)
                        CompleteHardDelete(deleting);
                }

                CompleteMaintenanceAsync("Completed").GetAwaiter().GetResult();
                return deleting is not null;
            }
            catch (Exception exception)
            {
                try { CompleteMaintenanceAsync("Failed", exception.Message).GetAwaiter().GetResult(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Enters the shared RecordBase operation barrier and then verifies that one facade generation is still active.<br/>
        /// The ordering prevents invalidation from racing an admitted direct-Fractal or LibraDex operation without adding per-call metadata deserialization or allocation beyond the existing lifecycle scope.<br/>
        /// </summary>
        /// <param name="runtime">The shared runtime generation captured by the store facade.<br/></param>
        /// <returns>The RecordBase operation scope that must remain held for the complete top-level operation.<br/></returns>
        internal IDisposable EnterStoreOperationScope(StoreRuntimeState runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            var scope = EnterLibraDexOperationScope();
            try
            {
                runtime.EnsureActive();
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        private StoresInfo ReadStoreInfos()
        {
            if (!data.TryGetOne(StoreRegistryRecordId, out var record))
                return new StoresInfo();

            try
            {
                var binary = Inheto.InhetoBinary.FromStream(new BufferStream(record.Data), null);
                var stores = binary.ToObject<StoresInfo>() ?? new StoresInfo();
                ValidateStoreInfoNames(stores);
                return stores;
            }
            finally
            {
                record.Dispose();
            }
        }

        private void WriteStoreInfos(StoresInfo stores)
        {
            ArgumentNullException.ThrowIfNull(stores);
            ValidateStoreInfoNames(stores);
            var binary = Inheto.InhetoBinary.FromObject(stores, null);
            data.PutOne(StoreRegistryRecordId, binary.RawBytes.AsReadOnlySpan);
        }

        private void EnsureStoreCanOpen(StoreInfo info)
        {
            if (info.Lifecycle == StoreLifecycleState.SoftRemoved)
                throw new StoreUnavailableException(info.Name, StoreLifecycleState.SoftRemoved, $"Store '{info.Name}' is soft-removed. Call {nameof(RestoreStore)}() before opening it.");
            if (info.Lifecycle == StoreLifecycleState.HardDeletePending)
                throw new StoreUnavailableException(info.Name, StoreLifecycleState.HardDeletePending, $"Store '{info.Name}' has an incomplete hard deletion and cannot be opened.");
            if (info.Lifecycle == StoreLifecycleState.PurgePending)
                throw new StoreUnavailableException(info.Name, StoreLifecycleState.PurgePending, $"Store '{info.Name}' has an incomplete purge and cannot be opened.");
        }

        private StoreInfo EnsureStoreInstanceIdentity(string name, StoreInfo info)
        {
            if (info.InstanceID != Guid.Empty)
                return info;

            lock (_storeCatalogLock)
            {
                var stores = ReadStoreInfos();
                if (!stores.TryGetValue(name, out var current))
                    throw new InvalidOperationException($"Store '{name}' disappeared while its runtime identity was being initialized.");
                if (current.InstanceID == Guid.Empty)
                {
                    current.InstanceID = Guid.NewGuid();
                    stores[name] = current;
                    WriteStoreInfos(stores);
                }
                return current;
            }
        }

        private StoreRuntimeState GetOrCreateStoreRuntime(StoreInfo info)
        {
            if (info.InstanceID == Guid.Empty)
                throw new InvalidDataException($"Store '{info.Name}' does not have a durable runtime identity.");

            lock (_storeCatalogLock)
            {
                if (_storeRuntimes.TryGetValue(info.InstanceID, out var runtime))
                    return runtime;

                runtime = new StoreRuntimeState(info.Name, info.InstanceID);
                _storeRuntimes.Add(info.InstanceID, runtime);
                return runtime;
            }
        }

        private void InvalidateStoreRuntime(StoreInfo info, StoreLifecycleState state)
        {
            StoreRuntimeState? runtime = null;
            lock (_storeCatalogLock)
            {
                if (info.InstanceID != Guid.Empty &&
                    _storeRuntimes.Remove(info.InstanceID, out var existing))
                {
                    runtime = existing;
                }
            }
            runtime?.Invalidate(state);

            List<ILibraDexRebindableRecordStore> facades = new();
            lock (_liveRecordStoresGate)
            {
                for (var i = _liveRecordStores.Count - 1; i >= 0; i--)
                {
                    if (!_liveRecordStores[i].TryGetTarget(out var facade))
                    {
                        _liveRecordStores.RemoveAt(i);
                        continue;
                    }
                    if (facade.StoreInstanceID != info.InstanceID)
                        continue;

                    facades.Add(facade);
                    _liveRecordStores.RemoveAt(i);
                }
            }

            for (var i = 0; i < facades.Count; i++)
                facades[i].InvalidateStore(state);
        }

        private void CompleteHardDelete(StoreInfo info)
        {
            var stores = ReadStoreInfos();
            if (stores.Count == 1 &&
                stores.TryGetValue(info.Name, out var current) &&
                current.ID == info.ID &&
                current.InstanceID == info.InstanceID)
            {
                CompleteStorePurge(info);
                return;
            }

            RemoveStoreReality(info, StoreLifecycleState.HardDeletePending);
            RemoveStoreRegistryEntry(info, StoreLifecycleState.HardDeletePending);
        }

        private void RemoveStoreReality(
            StoreInfo info,
            StoreLifecycleState unavailableState,
            bool deleteFractalRecords = true)
        {
            InvalidateStoreRuntime(info, unavailableState);
            _ = libraDexCatalog.Indexes.DropIndexSet(info.Name);
            RemoveStoreCatalogEntries(info.ID);

            if (!deleteFractalRecords)
                return;

            _ = data.DeleteByGroupKey(info.ID);
        }

        private void RemoveStoreRegistryEntry(StoreInfo info, StoreLifecycleState expectedLifecycle)
        {
            lock (_storeCatalogLock)
            {
                var stores = ReadStoreInfos();
                if (!stores.TryGetValue(info.Name, out var current))
                    return;
                if (current.ID != info.ID ||
                    current.InstanceID != info.InstanceID ||
                    current.Lifecycle != expectedLifecycle)
                {
                    throw new InvalidDataException($"Store '{info.Name}' changed identity while its hard deletion was being completed.");
                }

                stores.Remove(info.Name);
                WriteStoreInfos(stores);
            }
        }

        private void RemoveStoreCatalogEntries(byte storeID)
        {
            lock (_partialIndexCatalogLock)
            {
                var catalog = ReadPartialIndexCatalog();
                var keys = catalog.Entries
                    .Where(static pair => pair.Value.StoreID != 0)
                    .Where(pair => pair.Value.StoreID == storeID)
                    .Select(static pair => pair.Key)
                    .ToArray();
                for (var i = 0; i < keys.Length; i++)
                    catalog.Entries.Remove(keys[i]);
                if (keys.Length != 0)
                    WritePartialIndexCatalog(catalog);
            }

            lock (_ordinaryIndexRemovalCatalogLock)
            {
                var catalog = ReadOrdinaryIndexRemovalCatalog();
                var keys = catalog.Entries
                    .Where(pair => pair.Value.StoreID == storeID)
                    .Select(static pair => pair.Key)
                    .ToArray();
                for (var i = 0; i < keys.Length; i++)
                    catalog.Entries.Remove(keys[i]);
                if (keys.Length != 0)
                    WriteOrdinaryIndexRemovalCatalog(catalog);
            }

            lock (_applicationCodecCatalogLock)
            {
                var catalog = ReadApplicationCodecCatalog();
                var keys = catalog.Bindings
                    .Where(pair => pair.Value.StoreID == storeID)
                    .Select(static pair => pair.Key)
                    .ToArray();
                for (var i = 0; i < keys.Length; i++)
                    catalog.Bindings.Remove(keys[i]);
                if (keys.Length != 0)
                    WriteApplicationCodecCatalog(catalog);
            }

            lock (_ordinaryIndexIntentCatalogLock)
            {
                var catalog = ReadOrdinaryIndexIntentCatalog();
                var keys = catalog.Entries
                    .Where(pair => pair.Value.StoreID == storeID)
                    .Select(static pair => pair.Key)
                    .ToArray();
                for (var i = 0; i < keys.Length; i++)
                    catalog.Entries.Remove(keys[i]);
                if (keys.Length != 0)
                    WriteOrdinaryIndexIntentCatalog(catalog);
            }
        }

        private void RecoverPendingStoreDeletes()
        {
            StoreInfo[] pending;
            lock (_storeCatalogLock)
            {
                pending = ReadStoreInfos().Values
                    .Where(static info => info.Lifecycle == StoreLifecycleState.HardDeletePending)
                    .ToArray();
            }
            if (pending.Length == 0)
                return;

            BeginMaintenanceAsync("DeleteStoreRecovery", CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                using (EnterLibraDexLifecycleExclusiveScope(CancellationToken.None))
                {
                    for (var i = 0; i < pending.Length; i++)
                        CompleteHardDelete(pending[i]);
                }
                CompleteMaintenanceAsync("Completed").GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                try { CompleteMaintenanceAsync("Failed", exception.Message).GetAwaiter().GetResult(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Validates persisted store identity before a hydrated registry escapes or a replacement registry is serialized.<br/>
        /// Names must be nonblank and agree with their dictionary keys using the registry's ordinal case-insensitive contract.<br/>
        /// Does not normalize text, repair entries, require optional descriptions/types, or add checks to record-processing paths.<br/>
        /// Enumerates the concrete dictionary without allocating validation state on the successful path.<br/>
        /// </summary>
        /// <param name="stores">Non-null hydrated or write-bound registry whose entries must have usable identities.<br/></param>
        /// <exception cref="InvalidDataException">A key is blank, an entry/name is missing, or the entry's name differs from its registry key.<br/></exception>
        private static void ValidateStoreInfoNames(StoresInfo stores)
        {
            foreach (var pair in stores)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    throw new InvalidDataException("The store registry contains a blank store key.");
                if (pair.Value is null || string.IsNullOrWhiteSpace(pair.Value.Name))
                    throw new InvalidDataException($"Store registry entry '{pair.Key}' has no valid store name.");
                if (!string.Equals(pair.Key, pair.Value.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Store registry entry '{pair.Key}' has mismatched store name '{pair.Value.Name}'.");
            }
        }
    }

    /// <summary>
    /// Describes the durable availability state of one RecordBase store definition.<br/>
    /// The zero value is active so StoreInfo payloads written before lifecycle support remain openable without migration ceremony.<br/>
    /// </summary>
    public enum StoreLifecycleState
    {
        /// <summary>
        /// The store definition is available for ordinary discovery and typed opening.<br/>
        /// </summary>
        Active = 0,
        /// <summary>
        /// The store is hidden from ordinary use while its records and indexes are retained for restoration.<br/>
        /// </summary>
        SoftRemoved = 1,
        /// <summary>
        /// A durable logical-deletion intent is retained while restartable record, index, and registry cleanup remains incomplete.<br/>
        /// </summary>
        HardDeletePending = 2,
        /// <summary>
        /// A durable purge intent is retained while physical residual-data removal and publication remain incomplete.<br/>
        /// </summary>
        PurgePending = 3
    }

    /// <summary>
    /// Reports that a RecordStore facade belongs to a store generation that is no longer available.<br/>
    /// Existing facades remain invalid after restoration or same-name recreation so stale references cannot cross logical store generations.<br/>
    /// </summary>
    public sealed class StoreUnavailableException : InvalidOperationException
    {
        /// <summary>
        /// Creates one store-unavailable failure with the durable state that invalidated the facade.<br/>
        /// </summary>
        /// <param name="storeName">The unavailable store name.<br/></param>
        /// <param name="state">The lifecycle state that rejected the operation.<br/></param>
        /// <param name="message">Developer-facing failure detail.<br/></param>
        public StoreUnavailableException(string storeName, StoreLifecycleState state, string message)
            : base(message)
        {
            StoreName = storeName;
            State = state;
        }

        /// <summary>
        /// Gets the logical store name whose facade generation is unavailable.<br/>
        /// </summary>
        public string StoreName { get; }

        /// <summary>
        /// Gets the lifecycle state that invalidated the facade generation.<br/>
        /// </summary>
        public StoreLifecycleState State { get; }
    }

    internal sealed class StoreRuntimeState
    {
        private readonly object mutationGate = new();
        private readonly object mutationDiagnosticsGate = new();
        private readonly Dictionary<string, HashSet<object?>> compatibleUniqueReservations = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> readSnapshotOwners = new();
        private RecordStoreMutationDiagnosticsAccumulator? mutationDiagnostics;
        private long appliedIndexSourceUsn = -1;
        private int activeCompatibleMutations;
        private int activeReadSnapshots;
        private int compatibleMutationWaiters;
        private int completedCompatibleMutations;
        private int exclusiveMutationWaiters;
        private bool compatibleCohortClosing;
        private bool compatibleCohortRequiresRepair;
        private Exception? compatibleCohortFailure;
        private long compatibleCohortGeneration;
        private long completedCompatibleCohortGeneration;
        private Exception? completedCompatibleCohortFailure;
        private int state;

        internal StoreRuntimeState(string storeName, Guid instanceID)
        {
            StoreName = storeName;
            InstanceID = instanceID;
        }

        internal string StoreName { get; }
        internal Guid InstanceID { get; }
        internal StoreLifecycleState State => (StoreLifecycleState)Volatile.Read(ref state);

        /// <summary>
        /// Enters the mutation lane shared by every live facade for this exact store generation.<br/>
        /// The monitor is reentrant so nested Abraxas helpers retain their existing composition while separate facades cannot advance one store's Fractal USN and LibraDex checkpoint out of order.<br/>
        /// </summary>
        /// <returns>A scope that releases the shared mutation lane exactly once.<br/></returns>
        internal IDisposable EnterMutationScope()
        {
            if (Monitor.IsEntered(mutationGate))
            {
                ThrowIfCurrentThreadOwnsReadSnapshot();
                Monitor.Enter(mutationGate);
                return new StoreRuntimeMutationScope(mutationGate);
            }

            RecordStoreMutationDiagnosticsAccumulator? diagnostics = Volatile.Read(ref mutationDiagnostics);
            long waitStarted = diagnostics is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
            bool contended = !Monitor.TryEnter(mutationGate);
            bool requestRecorded = false;
            if (contended)
            {
                diagnostics?.RecordGateRequestStarted();
                requestRecorded = diagnostics is not null;
                Monitor.Enter(mutationGate);
            }

            try
            {
                ThrowIfCurrentThreadOwnsReadSnapshot();
                if (activeCompatibleMutations != 0 || activeReadSnapshots != 0)
                {
                    if (!requestRecorded)
                    {
                        diagnostics?.RecordGateRequestStarted();
                        requestRecorded = diagnostics is not null;
                    }
                    contended = true;
                    checked { exclusiveMutationWaiters++; }
                    try
                    {
                        while (activeCompatibleMutations != 0 || activeReadSnapshots != 0)
                            Monitor.Wait(mutationGate);
                    }
                    finally
                    {
                        exclusiveMutationWaiters--;
                    }
                }

                if (diagnostics is null)
                    return new StoreRuntimeMutationScope(mutationGate);

                long acquired = System.Diagnostics.Stopwatch.GetTimestamp();
                diagnostics.RecordGateEntered(acquired - waitStarted, contended);
                return new StoreRuntimeMutationScope(mutationGate, diagnostics, acquired);
            }
            catch
            {
                Monitor.Exit(mutationGate);
                throw;
            }
        }

        /// <summary>
        /// Enters a shared, non-thread-affine read-snapshot lane that excludes every authoritative or derived store mutation until disposal.<br/>
        /// Multiple read snapshots may coexist, while already-waiting mutations receive admission priority so repeated readers cannot starve writes.<br/>
        /// </summary>
        /// <returns>A scope that may be disposed on a different thread and releases exactly one active read snapshot.<br/></returns>
        internal IDisposable EnterReadSnapshotScope()
        {
            int openingThreadId = Environment.CurrentManagedThreadId;
            lock (mutationGate)
            {
                while (activeCompatibleMutations != 0 ||
                       compatibleCohortClosing ||
                       exclusiveMutationWaiters != 0 ||
                       compatibleMutationWaiters != 0)
                {
                    Monitor.Wait(mutationGate);
                }

                checked { activeReadSnapshots++; }
                if (readSnapshotOwners.TryGetValue(openingThreadId, out int owned))
                    readSnapshotOwners[openingThreadId] = checked(owned + 1);
                else
                    readSnapshotOwners.Add(openingThreadId, 1);
                return new StoreRuntimeReadSnapshotScope(this, openingThreadId);
            }
        }

        /// <summary>
        /// Enters the serialized preparation boundary for one compatible data mutation while allowing already-admitted compatible writers to continue physical work.<br/>
        /// The preparation boundary blocks lifecycle, schema, reset, repair, and unsupported mutation operations, but it does not wait for the current compatible cohort unless that cohort is already closing.<br/>
        /// A new admission waits behind an already-requested exclusive mutation so sustained bulk traffic cannot starve structural maintenance.<br/>
        /// </summary>
        /// <returns>A thread-affine preparation scope that reports whether this caller starts a new convergence cohort.<br/></returns>
        internal StoreRuntimeCompatiblePreparationScope EnterCompatibleMutationPreparationScope()
        {
            Monitor.Enter(mutationGate);
            bool recordedWaiter = false;
            try
            {
                ThrowIfCurrentThreadOwnsReadSnapshot();
                while (compatibleCohortClosing || exclusiveMutationWaiters != 0 || activeReadSnapshots != 0)
                {
                    if (!recordedWaiter)
                    {
                        checked { compatibleMutationWaiters++; }
                        recordedWaiter = true;
                    }
                    Monitor.Wait(mutationGate);
                }

                if (recordedWaiter)
                {
                    compatibleMutationWaiters--;
                    recordedWaiter = false;
                }

                return new StoreRuntimeCompatiblePreparationScope(
                    this,
                    startsCohort: activeCompatibleMutations == 0,
                    activeCompatibleMutations == 0
                        ? checked(compatibleCohortGeneration + 1)
                        : compatibleCohortGeneration);
            }
            catch
            {
                if (recordedWaiter)
                {
                    compatibleMutationWaiters--;
                    Monitor.PulseAll(mutationGate);
                }
                Monitor.Exit(mutationGate);
                throw;
            }
        }

        /// <summary>
        /// Reserves every non-null key in one unique-index domain while the caller owns the compatible preparation boundary.<br/>
        /// Existing LibraDex uniqueness is validated separately; this process-local reservation closes only the interval in which another admitted writer has preflighted a key but has not yet published it.<br/>
        /// </summary>
        /// <param name="domain">Stable physical index name defining the uniqueness domain.<br/></param>
        /// <param name="keys">Prepared runtime keys for the submitted mutation.<br/></param>
        /// <param name="comparer">Structural comparer matching Abraxas runtime-key equality.<br/></param>
        /// <returns>A reservation released after the caller finishes physical index maintenance.<br/></returns>
        internal IDisposable ReserveCompatibleUniqueKeys(
            string domain,
            IReadOnlyList<object?> keys,
            IEqualityComparer<object?> comparer)
        {
            if (!Monitor.IsEntered(mutationGate))
                throw new SynchronizationLockException("Compatible unique keys can be reserved only while mutation preparation is owned.");

            if (!compatibleUniqueReservations.TryGetValue(domain, out HashSet<object?>? reserved))
            {
                reserved = new HashSet<object?>(comparer);
                compatibleUniqueReservations.Add(domain, reserved);
            }

            var added = new List<object?>();
            try
            {
                for (int index = 0; index < keys.Count; index++)
                {
                    object? key = keys[index];
                    if (key is null)
                        continue;
                    if (!reserved.Add(key))
                        throw new InvalidOperationException($"Unique index '{domain}' has an equal key reserved by another active writer.");
                    added.Add(key);
                }
            }
            catch
            {
                for (int index = 0; index < added.Count; index++)
                    reserved.Remove(added[index]);
                if (reserved.Count == 0)
                    compatibleUniqueReservations.Remove(domain);
                throw;
            }

            return new StoreRuntimeUniqueReservation(this, domain, added);
        }

        /// <summary>
        /// Admits one fully prepared compatible mutation into the current convergence cohort and releases the preparation monitor.<br/>
        /// The first admission establishes a fresh cohort generation; later admissions join it until the first member begins completion.<br/>
        /// </summary>
        /// <param name="preparation">Preparation scope currently owning the mutation monitor.<br/></param>
        /// <returns>A non-thread-affine cohort lease completed after Fractal and LibraDex work finish.<br/></returns>
        internal StoreRuntimeCompatibleMutationScope AdmitCompatibleMutation(StoreRuntimeCompatiblePreparationScope preparation)
        {
            if (!ReferenceEquals(preparation.Owner, this))
                throw new InvalidOperationException("The compatible mutation preparation scope is not active for this store runtime.");
            if (!Monitor.IsEntered(mutationGate))
                throw new SynchronizationLockException("Compatible mutation admission requires the preparation monitor.");
            if (activeCompatibleMutations != 0 && preparation.Generation != compatibleCohortGeneration)
                throw new InvalidOperationException("The compatible mutation cohort changed while admission was being prepared.");
            bool startsCohort = activeCompatibleMutations == 0;
            int admittedCount = checked(activeCompatibleMutations + 1);
            if (!preparation.TryConsume())
                throw new InvalidOperationException("The compatible mutation preparation scope has already been consumed.");

            if (activeCompatibleMutations == 0)
            {
                compatibleCohortGeneration = preparation.Generation;
                completedCompatibleMutations = 0;
                compatibleCohortClosing = false;
                compatibleCohortRequiresRepair = false;
                compatibleCohortFailure = null;
            }

            activeCompatibleMutations = admittedCount;
            Volatile.Read(ref mutationDiagnostics)?.RecordCompatibleMutationAdmission(admittedCount, startsCohort);
            Monitor.Exit(mutationGate);
            return new StoreRuntimeCompatibleMutationScope(this, compatibleCohortGeneration);
        }

        /// <summary>
        /// Completes one compatible mutation, closes the cohort to new admissions, and elects the final member to publish or repair the shared convergence proof.<br/>
        /// Every cohort member waits for the same finalization result, ensuring no successful API return precedes Fractal-to-LibraDex convergence.<br/>
        /// </summary>
        /// <param name="generation">Cohort generation captured at admission.<br/></param>
        /// <param name="requiresRepair">Whether this member observed uncertain or failed companion maintenance.<br/></param>
        /// <param name="failure">First physical failure associated with this member, when available.<br/></param>
        /// <param name="finalize">Callback that publishes the current source USN or repairs all maintained indexes.<br/></param>
        /// <returns>The shared finalization failure, or <see langword="null"/> after successful convergence.<br/></returns>
        internal Exception? CompleteCompatibleMutation(
            long generation,
            bool requiresRepair,
            Exception? failure,
            Action<bool, Exception?> finalize)
        {
            ArgumentNullException.ThrowIfNull(finalize);
            bool finalizer = false;
            bool finalizerRequiresRepair = false;
            Exception? finalizerFailure = null;
            lock (mutationGate)
            {
                if (generation != compatibleCohortGeneration || activeCompatibleMutations == 0)
                    throw new InvalidOperationException("The compatible mutation lease does not belong to the active cohort.");

                compatibleCohortClosing = true;
                if (requiresRepair)
                {
                    compatibleCohortRequiresRepair = true;
                    compatibleCohortFailure ??= failure;
                }

                checked { completedCompatibleMutations++; }
                if (completedCompatibleMutations == activeCompatibleMutations)
                {
                    finalizer = true;
                    finalizerRequiresRepair = compatibleCohortRequiresRepair;
                    finalizerFailure = compatibleCohortFailure;
                }
                else
                {
                    while (completedCompatibleCohortGeneration != generation)
                        Monitor.Wait(mutationGate);
                    return completedCompatibleCohortFailure;
                }
            }

            if (finalizer)
            {
                Exception? finalizationFailure = null;
                try
                {
                    if (finalizerRequiresRepair)
                        Volatile.Read(ref mutationDiagnostics)?.RecordCompatibleMutationRepair();
                    finalize(finalizerRequiresRepair, finalizerFailure);
                }
                catch (Exception exception)
                {
                    finalizationFailure = exception;
                }

                lock (mutationGate)
                {
                    completedCompatibleCohortGeneration = generation;
                    completedCompatibleCohortFailure = finalizationFailure;
                    activeCompatibleMutations = 0;
                    completedCompatibleMutations = 0;
                    compatibleCohortClosing = false;
                    compatibleCohortRequiresRepair = false;
                    compatibleCohortFailure = null;
                    Monitor.PulseAll(mutationGate);
                    return finalizationFailure;
                }
            }

            return null;
        }

        /// <summary>
        /// Tests the process-local fast readiness proof shared by every facade for this store generation.<br/>
        /// A fresh process or any retained State intent starts unproven and therefore falls through to the durable LibraDex checkpoint.<br/>
        /// </summary>
        /// <param name="sourceUsn">Current authoritative Fractal store USN.<br/></param>
        /// <returns><see langword="true"/> when this runtime has already proven the same source USN.<br/></returns>
        internal bool IsIndexSourceUsnApplied(ulong sourceUsn)
            => unchecked((ulong)Interlocked.Read(ref appliedIndexSourceUsn)) == sourceUsn;

        /// <summary>
        /// Publishes the current proven source USN after checkpoint advancement and State-intent clearance complete.<br/>
        /// </summary>
        /// <param name="sourceUsn">Authoritative Fractal store USN represented by LibraDex.<br/></param>
        internal void SetAppliedIndexSourceUsn(ulong sourceUsn)
            => Interlocked.Exchange(ref appliedIndexSourceUsn, unchecked((long)sourceUsn));

        /// <summary>
        /// Invalidates the process-local readiness shortcut before retaining intent or beginning uncertain derived maintenance.<br/>
        /// </summary>
        internal void ClearAppliedIndexSourceUsn()
            => Interlocked.Exchange(ref appliedIndexSourceUsn, -1);

        internal void EnsureActive()
        {
            var current = State;
            if (current != StoreLifecycleState.Active)
                throw new StoreUnavailableException(StoreName, current, $"Store facade '{StoreName}' is no longer active because its store was {Describe(current)}.");
        }

        internal void Invalidate(StoreLifecycleState newState)
        {
            if (newState == StoreLifecycleState.Active)
                throw new ArgumentOutOfRangeException(nameof(newState), newState, "Invalidation requires an unavailable lifecycle state.");
            Interlocked.Exchange(ref state, (int)newState);
        }

        private static string Describe(StoreLifecycleState state)
            => state == StoreLifecycleState.SoftRemoved
                ? "soft-removed"
                : state == StoreLifecycleState.PurgePending
                    ? "purged"
                    : "deleted";

        /// <summary>
        /// Releases one shared store-generation mutation monitor.<br/>
        /// </summary>
        private sealed class StoreRuntimeMutationScope : IDisposable
        {
            private object? gate;
            private RecordStoreMutationDiagnosticsAccumulator? diagnostics;
            private readonly long acquiredTimestamp;

            /// <summary>
            /// Captures the already-entered shared mutation monitor.<br/>
            /// </summary>
            /// <param name="gate">Monitor object entered by the current thread.<br/></param>
            internal StoreRuntimeMutationScope(object gate)
            {
                this.gate = gate;
            }

            /// <summary>
            /// Captures an outermost measured mutation-monitor ownership interval.<br/>
            /// </summary>
            /// <param name="gate">Monitor object entered by the current thread.<br/></param>
            /// <param name="diagnostics">Enabled session accumulator that observed acquisition.<br/></param>
            /// <param name="acquiredTimestamp">Stopwatch timestamp immediately after ownership was obtained.<br/></param>
            internal StoreRuntimeMutationScope(
                object gate,
                RecordStoreMutationDiagnosticsAccumulator diagnostics,
                long acquiredTimestamp)
            {
                this.gate = gate;
                this.diagnostics = diagnostics;
                this.acquiredTimestamp = acquiredTimestamp;
            }

            /// <summary>
            /// Releases the shared monitor once.<br/>
            /// </summary>
            public void Dispose()
            {
                var current = Interlocked.Exchange(ref gate, null);
                if (current is not null)
                {
                    RecordStoreMutationDiagnosticsAccumulator? currentDiagnostics = Interlocked.Exchange(ref diagnostics, null);
                    if (currentDiagnostics is not null)
                        currentDiagnostics.RecordGateHold(System.Diagnostics.Stopwatch.GetTimestamp() - acquiredTimestamp);
                    Monitor.PulseAll(current);
                    Monitor.Exit(current);
                }
            }
        }

        /// <summary>
        /// Releases one process-local compatible unique-key reservation after physical mutation work no longer needs to exclude another preflight.<br/>
        /// </summary>
        /// <param name="domain">Physical unique-index domain that owns the keys.<br/></param>
        /// <param name="keys">Keys previously added to the domain's active reservation set.<br/></param>
        private void ReleaseCompatibleUniqueKeys(string domain, IReadOnlyList<object?> keys)
        {
            lock (mutationGate)
            {
                if (!compatibleUniqueReservations.TryGetValue(domain, out HashSet<object?>? reserved))
                    return;

                for (int index = 0; index < keys.Count; index++)
                    reserved.Remove(keys[index]);
                if (reserved.Count == 0)
                    compatibleUniqueReservations.Remove(domain);
            }
        }

        /// <summary>
        /// Owns the thread-affine serialized preparation interval preceding one compatible physical mutation.<br/>
        /// Disposal without admission releases the monitor; successful admission consumes the scope and transfers responsibility to a non-thread-affine cohort lease.<br/>
        /// </summary>
        internal sealed class StoreRuntimeCompatiblePreparationScope : IDisposable
        {
            private int consumed;

            /// <summary>
            /// Captures one already-entered compatible preparation boundary.<br/>
            /// </summary>
            /// <param name="owner">Store runtime owning the mutation monitor.<br/></param>
            /// <param name="startsCohort">Whether this caller must establish convergence before admission.<br/></param>
            /// <param name="generation">Existing or next compatible cohort generation.<br/></param>
            internal StoreRuntimeCompatiblePreparationScope(StoreRuntimeState owner, bool startsCohort, long generation)
            {
                Owner = owner;
                StartsCohort = startsCohort;
                Generation = generation;
            }

            /// <summary>Gets the store runtime owning this preparation interval.<br/></summary>
            internal StoreRuntimeState Owner { get; }

            /// <summary>Gets whether this caller starts a new cohort and must establish a converged baseline.<br/></summary>
            internal bool StartsCohort { get; }

            /// <summary>Gets the compatible cohort generation to join or establish.<br/></summary>
            internal long Generation { get; }

            /// <summary>
            /// Atomically transfers monitor-release responsibility to successful compatible admission.<br/>
            /// </summary>
            /// <returns><see langword="true"/> only for the first consume attempt.<br/></returns>
            internal bool TryConsume()
                => Interlocked.Exchange(ref consumed, 1) == 0;

            /// <summary>
            /// Releases the preparation monitor when the scope was not consumed by admission.<br/>
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref consumed, 1) == 0)
                    Monitor.Exit(Owner.mutationGate);
            }
        }

        /// <summary>
        /// Represents one admitted compatible mutation whose physical work may complete on a different thread.<br/>
        /// </summary>
        internal sealed class StoreRuntimeCompatibleMutationScope
        {
            private StoreRuntimeState? owner;

            /// <summary>
            /// Captures the runtime and cohort generation after atomic admission.<br/>
            /// </summary>
            /// <param name="owner">Store runtime coordinating convergence.<br/></param>
            /// <param name="generation">Admitted compatible cohort generation.<br/></param>
            internal StoreRuntimeCompatibleMutationScope(StoreRuntimeState owner, long generation)
            {
                this.owner = owner;
                Generation = generation;
            }

            /// <summary>Gets the compatible cohort generation captured at admission.<br/></summary>
            internal long Generation { get; }

            /// <summary>
            /// Completes this member exactly once and waits for shared cohort finalization.<br/>
            /// </summary>
            /// <param name="requiresRepair">Whether this member observed uncertain derived maintenance.<br/></param>
            /// <param name="failure">Physical failure associated with this member, when available.<br/></param>
            /// <param name="finalize">Shared final publication or repair callback elected onto the final cohort member.<br/></param>
            /// <returns>The shared finalization failure, or <see langword="null"/> when convergence completed.<br/></returns>
            internal Exception? Complete(bool requiresRepair, Exception? failure, Action<bool, Exception?> finalize)
            {
                StoreRuntimeState current = Interlocked.Exchange(ref owner, null)
                    ?? throw new InvalidOperationException("The compatible mutation scope has already completed.");
                return current.CompleteCompatibleMutation(Generation, requiresRepair, failure, finalize);
            }
        }

        /// <summary>
        /// Owns one unique-index key reservation across compatible preparation and physical publication.<br/>
        /// </summary>
        private sealed class StoreRuntimeUniqueReservation : IDisposable
        {
            private StoreRuntimeState? owner;
            private readonly string domain;
            private readonly IReadOnlyList<object?> keys;

            /// <summary>
            /// Captures keys already inserted into one runtime reservation domain.<br/>
            /// </summary>
            /// <param name="owner">Store runtime owning the reservation table.<br/></param>
            /// <param name="domain">Physical unique-index domain.<br/></param>
            /// <param name="keys">Exact keys added by this reservation.<br/></param>
            internal StoreRuntimeUniqueReservation(StoreRuntimeState owner, string domain, IReadOnlyList<object?> keys)
            {
                this.owner = owner;
                this.domain = domain;
                this.keys = keys;
            }

            /// <summary>
            /// Releases the reservation exactly once.<br/>
            /// </summary>
            public void Dispose()
            {
                StoreRuntimeState? current = Interlocked.Exchange(ref owner, null);
                current?.ReleaseCompatibleUniqueKeys(domain, keys);
            }
        }

        /// <summary>
        /// Gets the currently enabled mutation accumulator for optional phase attribution.<br/>
        /// A null value keeps ordinary persistence on its existing unmeasured path.<br/>
        /// </summary>
        internal RecordStoreMutationDiagnosticsAccumulator? ActiveMutationDiagnostics
            => Volatile.Read(ref mutationDiagnostics);

        /// <summary>
        /// Enables one opt-in diagnostic interval for this exact store generation.<br/>
        /// </summary>
        /// <returns>A session that captures counters and disables this accumulator when disposed.<br/></returns>
        internal RecordStoreMutationDiagnosticsSession BeginMutationDiagnostics()
        {
            var accumulator = new RecordStoreMutationDiagnosticsAccumulator();
            lock (mutationDiagnosticsGate)
            {
                if (mutationDiagnostics is not null)
                    throw new InvalidOperationException($"Store '{StoreName}' already has an active mutation diagnostic session.");
                Volatile.Write(ref mutationDiagnostics, accumulator);
            }

            return new RecordStoreMutationDiagnosticsSession(accumulator, CompleteMutationDiagnostics);
        }

        /// <summary>
        /// Disables a diagnostic interval only when it still owns the active accumulator slot.<br/>
        /// </summary>
        /// <param name="completed">Accumulator owned by the disposing session.<br/></param>
        private void CompleteMutationDiagnostics(RecordStoreMutationDiagnosticsAccumulator completed)
        {
            lock (mutationDiagnosticsGate)
            {
                if (ReferenceEquals(mutationDiagnostics, completed))
                    Volatile.Write(ref mutationDiagnostics, null);
            }
        }

        /// <summary>
        /// Rejects a mutation initiated by a thread that opened an undisposed read snapshot, preventing a self-deadlock while preserving the snapshot's immutability contract.<br/>
        /// The mutation monitor must be owned before this check reads the per-opening-thread table.<br/>
        /// </summary>
        private void ThrowIfCurrentThreadOwnsReadSnapshot()
        {
            if (!Monitor.IsEntered(mutationGate))
                throw new SynchronizationLockException("Read-snapshot ownership must be inspected while the mutation monitor is owned.");
            if (readSnapshotOwners.ContainsKey(Environment.CurrentManagedThreadId))
            {
                throw new InvalidOperationException(
                    $"Store '{StoreName}' cannot be mutated on a thread that owns an active unordered read snapshot. Dispose the reader before mutating the store.");
            }
        }

        /// <summary>
        /// Releases one shared read snapshot and wakes mutations or readers waiting for a safe admission boundary.<br/>
        /// The opening-thread identity is bookkeeping only, so disposal remains valid from any thread.<br/>
        /// </summary>
        /// <param name="openingThreadId">Managed thread that originally admitted the snapshot.<br/></param>
        private void ReleaseReadSnapshot(int openingThreadId)
        {
            lock (mutationGate)
            {
                if (!readSnapshotOwners.TryGetValue(openingThreadId, out int owned) || owned <= 0 || activeReadSnapshots <= 0)
                    throw new InvalidOperationException("The store read-snapshot lease is not active.");

                if (owned == 1)
                    readSnapshotOwners.Remove(openingThreadId);
                else
                    readSnapshotOwners[openingThreadId] = owned - 1;
                activeReadSnapshots--;
                Monitor.PulseAll(mutationGate);
            }
        }

        /// <summary>
        /// Represents one shared store read snapshot whose disposal is idempotent and not thread-affine.<br/>
        /// </summary>
        private sealed class StoreRuntimeReadSnapshotScope : IDisposable
        {
            private StoreRuntimeState? owner;
            private readonly int openingThreadId;

            /// <summary>
            /// Captures one already-admitted snapshot and its opening thread for self-deadlock detection.<br/>
            /// </summary>
            /// <param name="owner">Store runtime coordinating readers and mutations.<br/></param>
            /// <param name="openingThreadId">Managed thread that admitted the snapshot.<br/></param>
            internal StoreRuntimeReadSnapshotScope(StoreRuntimeState owner, int openingThreadId)
            {
                this.owner = owner;
                this.openingThreadId = openingThreadId;
            }

            /// <summary>
            /// Releases the snapshot exactly once and permits a waiting mutation to advance when the final reader completes.<br/>
            /// </summary>
            public void Dispose()
            {
                StoreRuntimeState? current = Interlocked.Exchange(ref owner, null);
                current?.ReleaseReadSnapshot(openingThreadId);
            }
        }
    }
}
