using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using FractalKVS;
using LibraDex;

namespace AbraxasDB
{
    public partial class RecordBase : IDisposable,IAsyncDisposable
    {
        internal string name = null!;
        internal DirectoryInfo dataFilesLocation = null!;

        internal FractalStore data = null!;
        internal RecordBaseConfiguration config = null!;
        internal Catalog libraDexCatalog = null!;
        private readonly object _libraDexLifecycleGate = new();
        private readonly object _liveRecordStoresGate = new();
        private readonly object _storeCatalogLock = new();
        private readonly List<WeakReference<ILibraDexRebindableRecordStore>> _liveRecordStores = new();
        private readonly Dictionary<Guid, StoreRuntimeState> _storeRuntimes = new();
        private int _activeLibraDexOperations;
        private bool _libraDexLifecycleExclusivePending;
        private bool _libraDexLifecycleExclusive;
        private Exception? _libraDexLifecycleFault;
        internal RowIDGenerator idGen = null!;

        private readonly IndexingBatchHandlePool _batchHandlePool = new();
        private const ulong PartialIndexCatalogRecordId = 6;
        private const ulong OrdinaryIndexRemovalCatalogRecordId = 7;
        private const ulong ApplicationCodecCatalogRecordId = 8;
        private const ulong OrdinaryIndexIntentCatalogRecordId = 9;
        private const int RecordBaseStringSettingsID = 2;
        private const int RecordBaseNumericSettingsID = 3;
        private readonly object _partialIndexCatalogLock = new();
        private readonly object _ordinaryIndexRemovalCatalogLock = new();
        private readonly object _applicationCodecCatalogLock = new();
        private readonly object _ordinaryIndexIntentCatalogLock = new();
        private readonly ConcurrentBag<Catalog> _volatileIndexCatalogs = new();
        private long _nextBatchOperationId;

        /// <summary>
        /// Gets the actual on-disk index catalog format version used by this open RecordBase.<br/>
        /// The value comes from the opened file rather than the requested configuration, so a version-one RecordBase opened with recoverable-format creation enabled still reports version one until an explicit compaction migrates it.<br/>
        /// The read participates in the persistent-catalog lifecycle barrier and therefore cannot race a catalog replacement.<br/>
        /// </summary>
        public ushort IndexCatalogFileFormatVersion
        {
            get
            {
                using var lifecycle = EnterLibraDexOperationScope();
                return libraDexCatalog.Session.Superblock.FormatVersion;
            }
        }

        internal IndexingBatchHandle CreateBatchHandle(int scheduledCount, bool autoComplete = false)
        {
            if (scheduledCount < 0)
                throw new ArgumentOutOfRangeException(nameof(scheduledCount));

            var handle = _batchHandlePool.Rent();
            var operationId = Interlocked.Increment(ref _nextBatchOperationId);
            handle.Initialize(operationId, scheduledCount, autoComplete);
            return handle;
        }

        /// <summary>
        /// Registers the volatile LibraDex catalog owned by one open record-store instance so RecordBase disposal deterministically releases its in-memory key material.<br/>
        /// Registration is append-only because multiple named or typed stores may legitimately expose the same compact store identifier during setup.<br/>
        /// </summary>
        /// <param name="catalog">The store-owned volatile LibraDex catalog.<br/></param>
        internal void RegisterVolatileIndexCatalog(Catalog catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            _volatileIndexCatalogs.Add(catalog);
        }


        /// <summary>
        /// Gets the persistent LibraDex index set that belongs to one Abraxas store in this record base.<br/>
        /// The owning catalog is a RecordBase-level sidecar opened with UInt64 identities; each store name maps to one LibraDex identity universe.<br/>
        /// </summary>
        /// <param name="storeName">The Abraxas store name.<br/></param>
        /// <returns>The persistent LibraDex index set for the supplied store.<br/></returns>
        internal CatalogIdentityGroupIndexes GetLibraDexIndexSet(string storeName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
            return libraDexCatalog.IndexSet(storeName);
        }

        /// <summary>
        /// Registers one live RecordStore binding for persistent LibraDex catalog rebinding.<br/>
        /// Weak ownership keeps short-lived store facades collectible while allowing RecordBase maintenance to capture a stable strong snapshot for the exclusive maintenance window.<br/>
        /// </summary>
        /// <param name="store">The newly opened or created store binding.<br/></param>
        internal void RegisterLiveRecordStore(ILibraDexRebindableRecordStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            lock (_liveRecordStoresGate)
            {
                for (var i = _liveRecordStores.Count - 1; i >= 0; i--)
                {
                    if (!_liveRecordStores[i].TryGetTarget(out var existing))
                    {
                        _liveRecordStores.RemoveAt(i);
                        continue;
                    }
                    if (ReferenceEquals(existing, store))
                        return;
                }

                _liveRecordStores.Add(new WeakReference<ILibraDexRebindableRecordStore>(store));
            }
        }

        /// <summary>
        /// Enters one active persistent-LibraDex operation and prevents catalog replacement until the returned scope is released.<br/>
        /// New operations wait while lifecycle-exclusive maintenance is active; the lease is not thread-affine and may therefore span lazy enumeration or asynchronous continuations.<br/>
        /// </summary>
        /// <returns>A scope that releases the active-operation count exactly once.<br/></returns>
        internal IDisposable EnterLibraDexOperationScope()
        {
            lock (_libraDexLifecycleGate)
            {
                if (_libraDexLifecycleFault is not null)
                    throw new InvalidOperationException("The persistent LibraDex catalog lifecycle is faulted; close this RecordBase and reopen it before issuing more indexed operations.", _libraDexLifecycleFault);
                while (_libraDexLifecycleExclusive)
                    Monitor.Wait(_libraDexLifecycleGate);
                if (_libraDexLifecycleFault is not null)
                    throw new InvalidOperationException("The persistent LibraDex catalog lifecycle is faulted; close this RecordBase and reopen it before issuing more indexed operations.", _libraDexLifecycleFault);
                checked { _activeLibraDexOperations++; }
                return new LibraDexOperationScope(this);
            }
        }

        /// <summary>
        /// Enters lifecycle-exclusive LibraDex maintenance after every admitted operation releases its lease.<br/>
        /// While draining, admission remains open so nested/reentrant work owned by an already-admitted operation cannot deadlock; once quiescent, admission closes atomically before catalog maintenance starts.<br/>
        /// Cancellation is observed while waiting; a cancelled acquisition clears the pending-maintenance state before throwing.<br/>
        /// </summary>
        /// <param name="cancellationToken">Token observed while waiting for active operations to drain.<br/></param>
        /// <returns>A scope that reopens operation admission exactly once.<br/></returns>
        private IDisposable EnterLibraDexLifecycleExclusiveScope(CancellationToken cancellationToken)
        {
            lock (_libraDexLifecycleGate)
            {
                if (_libraDexLifecycleExclusive || _libraDexLifecycleExclusivePending)
                    throw new InvalidOperationException("LibraDex lifecycle-exclusive maintenance is already active.");

                _libraDexLifecycleExclusivePending = true;
                try
                {
                    while (_activeLibraDexOperations != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Monitor.Wait(_libraDexLifecycleGate, TimeSpan.FromMilliseconds(100));
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    _libraDexLifecycleExclusivePending = false;
                    _libraDexLifecycleExclusive = true;
                    return new LibraDexLifecycleExclusiveScope(this);
                }
                catch
                {
                    _libraDexLifecycleExclusivePending = false;
                    _libraDexLifecycleExclusive = false;
                    Monitor.PulseAll(_libraDexLifecycleGate);
                    throw;
                }
            }
        }

        /// <summary>
        /// Captures every currently live RecordStore binding and prunes collected weak registrations.<br/>
        /// Callers hold the returned strong references for the complete detach/reopen/rebind transaction.<br/>
        /// </summary>
        /// <returns>The current live store bindings in registration order.<br/></returns>
        private ILibraDexRebindableRecordStore[] CaptureLiveRecordStores()
        {
            lock (_liveRecordStoresGate)
            {
                var stores = new List<ILibraDexRebindableRecordStore>(_liveRecordStores.Count);
                for (var i = _liveRecordStores.Count - 1; i >= 0; i--)
                {
                    if (!_liveRecordStores[i].TryGetTarget(out var store))
                    {
                        _liveRecordStores.RemoveAt(i);
                        continue;
                    }
                    stores.Add(store);
                }

                stores.Reverse();
                return stores.ToArray();
            }
        }

        /// <summary>
        /// Creates a new persistent RecordBase only when its authoritative file family is absent.<br/>
        /// </summary>
        /// <param name="dataFilesLocation">Absolute or relative directory containing the RecordBase file family.<br/></param>
        /// <param name="name">Logical RecordBase basename, without a storage-file extension or directory components.<br/></param>
        /// <param name="config">Optional startup configuration; null creates a fresh default configuration. Opening does not silently rewrite existing formats.<br/></param>
        /// <returns>An open RecordBase whose resources must be disposed after its stores and operations are no longer in use.<br/></returns>
        /// <remarks>
        /// Relative directories resolve against the process current directory. Incomplete families and retained recovery/restore authority are validated rather than silently replaced.<br/>
        /// UNC and network directories may be accepted by the filesystem, but performance, locking, and durability depend on the underlying storage and network; local storage is the normal performance target.<br/>
        /// </remarks>
        public static RecordBase Create(string dataFilesLocation, string name, RecordBaseConfiguration? config = null)
        {
            var ret = new RecordBase
            {
                dataFilesLocation = new DirectoryInfo(dataFilesLocation),
                name = name
            };

            config ??= RecordBaseConfiguration.Default;
            ret.config = config;

            ValidateRecordBaseName(name);
            if (HasRecordBaseMembers(ret.dataFilesLocation.FullName, name))
            {
                throw new InvalidOperationException($"A recordbase with the name '{name}' already exists in location '{dataFilesLocation}'.");
            }
            ret.Init(createNew: true);
            ret.InitializeCoordinationStateCatalogForNewRecordBase();
            return ret;
        }

        /// <summary>
        /// Opens an existing complete RecordBase, or creates a new one only when its authoritative family is absent.<br/>
        /// </summary>
        /// <param name="dataFilesLocation">Absolute or relative directory containing the RecordBase file family.<br/></param>
        /// <param name="name">Logical RecordBase basename, without a storage-file extension or directory components.<br/></param>
        /// <param name="config">Optional startup configuration; null creates a fresh default configuration. Opening does not silently rewrite existing formats.<br/></param>
        /// <returns>An open RecordBase whose resources must be disposed after its stores and operations are no longer in use.<br/></returns>
        /// <remarks>
        /// Relative directories resolve against the process current directory. Incomplete families and retained recovery/restore authority are validated rather than silently replaced.<br/>
        /// UNC and network directories may be accepted by the filesystem, but performance, locking, and durability depend on the underlying storage and network; local storage is the normal performance target.<br/>
        /// </remarks>
        public static RecordBase CreateOrOpen(string dataFilesLocation, string name, RecordBaseConfiguration? config = null)
        {
            var ret = new RecordBase
            {
                dataFilesLocation = new DirectoryInfo(dataFilesLocation),
                name = name
            };

            config ??= RecordBaseConfiguration.Default;
            ret.config = config;

            ValidateRecordBaseName(name);
            bool existed = HasRecordBaseMembers(ret.dataFilesLocation.FullName, name);
            ret.Init(createNew: !existed);
            if (!existed)
                ret.InitializeCoordinationStateCatalogForNewRecordBase();
            return ret;
        }

        /// <summary>
        /// Opens an existing complete persistent RecordBase without creating a replacement for missing authoritative members.<br/>
        /// </summary>
        /// <param name="dataFilesLocation">Absolute or relative directory containing the RecordBase file family.<br/></param>
        /// <param name="name">Logical RecordBase basename, without a storage-file extension or directory components.<br/></param>
        /// <param name="config">Optional startup configuration; null creates a fresh default configuration. Opening does not silently rewrite existing formats.<br/></param>
        /// <returns>An open RecordBase whose resources must be disposed after its stores and operations are no longer in use.<br/></returns>
        /// <remarks>
        /// Relative directories resolve against the process current directory. Incomplete families and retained recovery/restore authority are validated rather than silently replaced.<br/>
        /// UNC and network directories may be accepted by the filesystem, but performance, locking, and durability depend on the underlying storage and network; local storage is the normal performance target.<br/>
        /// </remarks>
        public static RecordBase Open(string dataFilesLocation, string name, RecordBaseConfiguration? config = null)
        {
            var ret = new RecordBase
            {
                dataFilesLocation = new DirectoryInfo(dataFilesLocation),
                name = name
            };

            config ??= RecordBaseConfiguration.Default;
            ret.config = config;

            ValidateRecordBaseName(name);
            if (!HasRecordBaseMembers(ret.dataFilesLocation.FullName, name))
            {
                throw new InvalidOperationException($"A recordbase with the name '{name}' does not exist in location '{dataFilesLocation}'.");
            }

            ret.Init();
            return ret;
        }

        /// <summary>Initializes the chosen lifecycle and closes partially opened components on failure so restore rollback can proceed.<br/></summary>
        /// <param name="createNew">Whether the complete family was absent at the create boundary.<br/></param>
        /// <param name="activeRestore">Private publication workspace allowed during its own final reopen; ordinary callers cannot bypass retained-restore guards.<br/></param>
        private void Init(bool createNew = false, string? activeRestore = null)
        {
            try { InitCore(createNew, activeRestore); }
            catch
            {
                try { idGen?.Dispose(); } catch { }
                try { libraDexCatalog?.Dispose(); } catch { }
                try { data?.Close(); } catch { }
                throw;
            }
        }

        /// <summary>Opens existing authority only after partial-store guards, or creates a completely absent family.<br/></summary>
        /// <param name="createNew">Whether creation is authorized for this fully absent family.<br/></param>
        /// <param name="activeRestore">Only the current operation's retained originals may coexist with final validation.<br/></param>
        private void InitCore(bool createNew, string? activeRestore)
        {
            EnsureNoRetainedRestore(dataFilesLocation.FullName, name, activeRestore);
            RecoverInterruptedPurgeReplacementBeforeOpen();
            if (!createNew)
                ValidateCompleteRecordBase(dataFilesLocation.FullName, name, deep: false, CancellationToken.None);

            // Initialize the authoritative Fractal and LibraDex stores.
            data = new FractalStore(dataFilesLocation.FullName, name,
                config.FractalStoreConfig with { CreateIfNotExists = createNew });
            try { data.Open(); }
            catch (FractalKVS.FileCopiedOrMovedException exception)
            {
                throw new RecordBaseRelocationException(
                    dataFilesLocation.FullName, name, exception.StoredPath, exception.StoredName,
                    exception.CurrentName, exception.FileUuid, exception.IsIndexFile, exception);
            }

            // On startup, check for any previous maintenance operation that was left InProgress.
            // Stale markers are handled by operation type:
            // - Compact: fatal (requires operator attention)
            // - Optimize: warning/auto-finalize marker and continue
            // - Backup: warning/auto-finalize marker and continue
            try
            {
                var status = GetStringSetting("Maintenance.Status");
                if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase))
                {
                    var operation = GetStringSetting("Maintenance.Operation");
                    var lastHeartbeat = GetNumericSetting("Maintenance.LastHeartbeatTicks");
                    var started = GetNumericSetting("Maintenance.StartedTicks");
                    long lastTicks = lastHeartbeat ?? started ?? 0;

                    var isStale = false;
                    TimeSpan age = TimeSpan.Zero;
                    if (lastTicks > 0)
                    {
                        var last = new DateTime(lastTicks, DateTimeKind.Utc);
                        age = DateTime.UtcNow - last;
                        isStale = age > config.MaintenanceStaleAfter;
                    }
                    else
                    {
                        // no timestamps recorded; treat as stale
                        isStale = true;
                    }

                    if (isStale)
                    {
                        if (string.Equals(operation, "Compact", StringComparison.OrdinalIgnoreCase))
                        {
                            if (lastTicks > 0)
                                throw new IncompleteMaintanenceException($"Previous maintenance operation appears to be incomplete (operation=Compact, last heartbeat {age} ago).");
                            throw new IncompleteMaintanenceException("Previous maintenance operation appears to be incomplete (operation=Compact, no heartbeat recorded).");
                        }

                        if (string.Equals(operation, "Backup", StringComparison.OrdinalIgnoreCase))
                        {
                            TryFinalizeStaleMaintenanceMarker("Abandoned", "Recovered stale Backup marker during startup.");
                        }
                        else if (string.Equals(operation, "Optimize", StringComparison.OrdinalIgnoreCase))
                        {
                            TryFinalizeStaleMaintenanceMarker("Abandoned", "Recovered stale Optimize marker during startup.");
                        }
                        else if (string.Equals(operation, nameof(RemoveStore), StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(operation, nameof(RestoreStore), StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(operation, nameof(DeleteStore), StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(operation, "DeleteStoreRecovery", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(operation, nameof(PurgeStore), StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(operation, "PurgeStoreRecovery", StringComparison.OrdinalIgnoreCase))
                        {
                            TryFinalizeStaleMaintenanceMarker("Abandoned", $"Recovered stale {operation} marker during startup; durable StoreInfo lifecycle state remains authoritative.");
                        }
                        else
                        {
                            if (lastTicks > 0)
                                throw new IncompleteMaintanenceException($"Previous maintenance operation appears to be incomplete (operation={operation}, last heartbeat {age} ago).");
                            throw new IncompleteMaintanenceException($"Previous maintenance operation appears to be incomplete (operation={operation}, no heartbeat recorded).");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IncompleteMaintanenceException)
            {
                // rethrow exact type so callers can catch it specifically
                throw;
            }
            catch
            {
                // best-effort, ignore any errors reading settings here to avoid blocking startup
            }

            libraDexCatalog = createNew
                ? Catalog.Create(name, directory: dataFilesLocation.FullName, options: config.CreateLibraDexCatalogOptions())
                : Catalog.Open(name, directory: dataFilesLocation.FullName, options: config.CreateLibraDexCatalogOptions());
            idGen = new AbraxasDB.RowIDGenerator(Path.Combine(dataFilesLocation.FullName, name));
            RecoverPendingStoreDeletes();
            RecoverPendingStorePurges();

        }

        private void TryFinalizeStaleMaintenanceMarker(string finalStatus, string details)
        {
            try
            {
                SetStringSetting("Maintenance.Status", finalStatus);
                SetStringSetting("Maintenance.Details", details ?? string.Empty);
                SetNumericSetting("Maintenance.FinishedTicks", DateTime.UtcNow.Ticks);
                SetNumericSetting("Maintenance.LastHeartbeatTicks", DateTime.UtcNow.Ticks);
            }
            catch
            {
                // best-effort; startup should not fail for marker cleanup
            }
        }

        /// <summary>
        /// Attempts to auto-recover a stale in-progress maintenance marker using a safe waterfall check.<br/>
        /// 1) Try a non-blocking semutex probe. If we can acquire it, no live owner is holding maintenance.<br/>
        /// 2) If probe is indeterminate (exception), fall back to heartbeat age/staleness evaluation.<br/>
        /// Returns true when marker was recovered and writes may proceed.<br/>
        /// </summary>
        /// <param name="maintenanceOperation">Operation name currently recorded in maintenance state.<br/></param>
        /// <returns>True when stale marker was finalized; otherwise false.<br/></returns>
        private bool TryRecoverStaleMaintenanceMarker(string maintenanceOperation)
        {
            var op = string.IsNullOrWhiteSpace(maintenanceOperation) ? "Unknown" : maintenanceOperation;
            var semutexProbeIndeterminate = false;

            try
            {
                using var probe = new Semutex(data.UUID.ToString());
                var acquired = probe.WaitOne(0);
                if (acquired)
                {
                    try { probe.Release(); } catch { }
                    TryFinalizeStaleMaintenanceMarker("Abandoned", $"Recovered stale {op} marker during write gate check.");
                    return true;
                }
            }
            catch
            {
                semutexProbeIndeterminate = true;
            }

            if (!semutexProbeIndeterminate)
                return false;

            try
            {
                var lastHeartbeat = GetNumericSetting("Maintenance.LastHeartbeatTicks");
                var started = GetNumericSetting("Maintenance.StartedTicks");
                long lastTicks = lastHeartbeat ?? started ?? 0;

                if (lastTicks <= 0)
                {
                    TryFinalizeStaleMaintenanceMarker("Abandoned", $"Recovered stale {op} marker during write gate check (missing heartbeat).");
                    return true;
                }

                var age = DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc);
                if (age > config.MaintenanceStaleAfter)
                {
                    TryFinalizeStaleMaintenanceMarker("Abandoned", $"Recovered stale {op} marker during write gate check (heartbeat stale).");
                    return true;
                }
            }
            catch
            {
                // best-effort; leave marker untouched when we cannot determine liveness
            }

            return false;
        }

        /// <summary>
        /// Adds or replaces a named string value in the shared persistent string-settings record.<br/>
        /// </summary>
        /// <param name="name">Dictionary key identifying the setting.<br/></param>
        /// <param name="value">String or signed 64-bit setting value to persist.<br/></param>
        /// <remarks>
        /// The settings record is updated through read-modify-write; coordinate concurrent settings updates rather than assuming independent per-key atomic writes.<br/>
        /// </remarks>
        public void SetSetting(string name, string value)
        {
            var ibo = new Dictionary<string, string>();
            if (data.TryGetOne(RecordBaseStringSettingsID, out var rec))
            {
                var ib = Inheto.InhetoBinary.FromStream(new BufferStream(rec.Data), null);
                ibo = ib.ToObject<Dictionary<string, string>>();
            }
            ibo[name] = value;
            var ibnew = Inheto.InhetoBinary.FromObject(ibo, null);
            data.PutOne(2, ibnew.RawBytes.AsReadOnlySpan);


        }

        // Compatibility wrapper: newer maintenance code expects SetStringSetting
        /// <summary>
        /// Adds or replaces a named string value in the shared persistent string-settings record.<br/>
        /// This is a compatibility alias for SetSetting.<br/>
        /// </summary>
        /// <param name="name">Dictionary key identifying the setting.<br/></param>
        /// <param name="value">String or signed 64-bit setting value to persist.<br/></param>
        /// <remarks>
        /// The settings record is updated through read-modify-write; coordinate concurrent settings updates rather than assuming independent per-key atomic writes.<br/>
        /// </remarks>
        public void SetStringSetting(string name, string value) => SetSetting(name, value);

        /// <summary>
        /// Reads one named value from the persistent string-settings record.<br/>
        /// </summary>
        /// <param name="name">Dictionary key identifying the setting.<br/></param>
        /// <returns>The stored string, or an empty string when the settings record or requested value is absent.<br/></returns>
        public string GetStringSetting(string name)
        {
            if (data.TryGetOne(RecordBaseStringSettingsID, out var rec))
            {
                var ib = Inheto.InhetoBinary.FromStream(new BufferStream(rec.Data), null);
                return ib.ReadString($"!{name}") ?? "";
            }
            return "";

        }

        /// <summary>
        /// Adds or replaces a named Int64 value in the shared persistent numeric-settings record.<br/>
        /// </summary>
        /// <param name="name">Dictionary key identifying the setting.<br/></param>
        /// <param name="value">String or signed 64-bit setting value to persist.<br/></param>
        /// <remarks>
        /// The settings record is updated through read-modify-write; coordinate concurrent settings updates rather than assuming independent per-key atomic writes.<br/>
        /// </remarks>
        public void SetNumericSetting(string name, long value)
        {
            var ibo = new Dictionary<string, long>();
            if (data.TryGetOne(RecordBaseNumericSettingsID, out var rec))
            {
                var ib = Inheto.InhetoBinary.FromStream(new BufferStream(rec.Data), null);
                ibo = ib.ToObject<Dictionary<string, long>>();
            }
            ibo[name] = value;
            var ibnew = Inheto.InhetoBinary.FromObject(ibo, null);
            data.PutOne(3, ibnew.RawBytes.AsReadOnlySpan);
        }

        /// <summary>
        /// Reads one named Int64 value from the persistent numeric-settings record.<br/>
        /// </summary>
        /// <param name="name">Dictionary key identifying the setting.<br/></param>
        /// <returns>The decoded numeric value, or null when the settings record or requested numeric value is absent.<br/></returns>
        public long? GetNumericSetting(string name)
        {
            if (data.TryGetOne(RecordBaseNumericSettingsID, out var rec))
            {
                var ib = Inheto.InhetoBinary.FromStream(new BufferStream(rec.Data), null);
                return ib.ReadInt64($"!{name}");
            }
            return null;

        }

        /// <summary>
        /// Creates a typed store and optionally installs its application-owned conversion handlers before persisted index attachment begins.<br/>
        /// Supplying the setup callback is useful when custom Inheto values also participate in persistent ordinary or partial LibraDex indexes.<br/>
        /// </summary>
        /// <typeparam name="T">The store's record shape.<br/></typeparam>
        /// <param name="name">The unique store name.<br/></param>
        /// <param name="description">Optional developer-facing description.<br/></param>
        /// <param name="configureBeforeIndexAttachment">Optional setup callback invoked after the store options exist and before persistent indexes attach.<br/></param>
        /// <returns>The newly created store facade.<br/></returns>
        public RecordStore<T> CreateStore<T>(
            string name,
            string? description = "",
            Action<RecordStore<T>>? configureBeforeIndexAttachment = null
            )
        {
            using var lifecycle = EnterLibraDexOperationScope();
            EnsureWriteAllowed(nameof(CreateStore));
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var storesInfo = GetStoreInfos(includeRemoved: true) ?? new StoresInfo();
            if (storesInfo.TryGetValue(name, out var existing))
            {
                if (existing.Lifecycle == StoreLifecycleState.SoftRemoved)
                    throw new InvalidOperationException($"Store '{name}' is soft-removed. Use {nameof(RestoreStore)}() before opening it, or {nameof(DeleteStore)}() to delete it permanently.");
                if (existing.Lifecycle == StoreLifecycleState.HardDeletePending)
                    throw new InvalidOperationException($"Store '{name}' has an incomplete hard deletion that must finish before the name can be reused.");
                throw new InvalidOperationException($"A store with the name '{name}' already exists. Use {nameof(OpenStore)}() or {nameof(CreateOrOpenStore)}() to open the existing store.");
            }

            var storeInfo = new StoreInfo
            {
                Name = name,
                Description = description,
                ItemType = typeof(T),
                CreatedUTC = DateTime.UtcNow,
                InstanceID = Guid.NewGuid(),
                Lifecycle = StoreLifecycleState.Active
            };

            byte storeID = 0;
            for (int i = 1; i <= RowIDGenerator.MaximumStoreID; i++)
            {
                foreach (var info in storesInfo.Values)
                {
                    if (info.ID == i) goto NextNumber;
                }

                storeID = (byte)i;
                break;
NextNumber:
                continue;
            }
            if (storeID == 0)
            {
                throw new OverflowException(
                    $"This record base has exhausted all {RowIDGenerator.MaximumStoreID} store identities available in this compiled Abraxas edition.");
            }

            storeInfo.ID = storeID;
            idGen.InitGenerator(storeID, RowIDGenerator.GetNewStoreInitialCounter());
            SetStoreInfo(name, storeInfo);
            var runtime = GetOrCreateStoreRuntime(storeInfo);
            var store = new RecordStore<T>(this, name, storeID, runtime, configureBeforeIndexAttachment);
            RegisterLiveRecordStore(store);
            return store;
        }

        /// <summary>
        /// Opens a typed store and optionally installs its application-owned conversion handlers before persisted ordinary and partial indexes are rehydrated.<br/>
        /// The callback is deliberately earlier than index attachment so custom Inheto hydration and LibraDex key conversion observe the same handler contract during reopen.<br/>
        /// </summary>
        /// <typeparam name="T">The persisted store's record shape.<br/></typeparam>
        /// <param name="name">The existing store name.<br/></param>
        /// <param name="configureBeforeIndexAttachment">Optional setup callback invoked before persistent index attachment.<br/></param>
        /// <returns>The opened store facade.<br/></returns>
        public RecordStore<T> OpenStore<T>(
            string name,
            Action<RecordStore<T>>? configureBeforeIndexAttachment = null
            )
        {
            using var lifecycle = EnterLibraDexOperationScope();
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var storeInfo = GetStoreInfo(name, includeRemoved: true);
            if (storeInfo is null)
                throw new InvalidOperationException($"A store with the name '{name}' does not exist. Use {nameof(CreateStore)} to create it.");
            EnsureStoreCanOpen(storeInfo);
            storeInfo = EnsureStoreInstanceIdentity(name, storeInfo);
            if (storeInfo.ID == 0 || storeInfo.ID > RowIDGenerator.MaximumStoreID)
            {
                throw new OverflowException(
                    $"Store '{name}' uses identity {storeInfo.ID}, which exceeds the {RowIDGenerator.MaximumStoreID} store-identity limit compiled into this Abraxas edition.");
            }

            var existingType = storeInfo.ItemType ?? typeof(object);
            var requestedType = typeof(T);
            if (existingType != requestedType)
            {
                var existingTypeName = existingType.FullName ?? existingType.Name;
                var requestedTypeName = requestedType.FullName ?? requestedType.Name;
                throw new InvalidOperationException($"Store '{name}' was created for '{existingTypeName}' but is being opened as '{requestedTypeName}'.");
            }

            var runtime = GetOrCreateStoreRuntime(storeInfo);
            var store = new RecordStore<T>(this, name, storeInfo.ID, runtime, configureBeforeIndexAttachment);
            RegisterLiveRecordStore(store);
            return store;
        }

        /// <summary>
        /// Creates or opens a typed store while applying application-owned conversion handlers before persistent index attachment in either branch.<br/>
        /// The same setup callback can therefore be reused for first creation and every later reopen without exposing LibraDex lifecycle details to the application.<br/>
        /// </summary>
        /// <typeparam name="T">The store's record shape.<br/></typeparam>
        /// <param name="name">The stable store name.<br/></param>
        /// <param name="description">Optional description used only when a new store is created.<br/></param>
        /// <param name="configureBeforeIndexAttachment">Optional setup callback invoked before persistent index attachment.<br/></param>
        /// <returns>The created or opened store facade.<br/></returns>
        public RecordStore<T> CreateOrOpenStore<T>(
            string name,
            string? description = "",
            Action<RecordStore<T>>? configureBeforeIndexAttachment = null
            )
        {
            using var lifecycle = EnterLibraDexOperationScope();
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var storeInfo = GetStoreInfo(name, includeRemoved: true);
            if (storeInfo is null)
                return CreateStore<T>(name, description, configureBeforeIndexAttachment);
            EnsureStoreCanOpen(storeInfo);
            storeInfo = EnsureStoreInstanceIdentity(name, storeInfo);
            if (storeInfo.ID == 0 || storeInfo.ID > RowIDGenerator.MaximumStoreID)
            {
                throw new OverflowException(
                    $"Store '{name}' uses identity {storeInfo.ID}, which exceeds the {RowIDGenerator.MaximumStoreID} store-identity limit compiled into this Abraxas edition.");
            }

            var existingType = storeInfo.ItemType ?? typeof(object);
            var requestedType = typeof(T);
            if (existingType != requestedType)
            {
                var existingTypeName = existingType.FullName ?? existingType.Name;
                var requestedTypeName = requestedType.FullName ?? requestedType.Name;
                throw new InvalidOperationException($"Store '{name}' was created for '{existingTypeName}' but is being opened as '{requestedTypeName}'.");
            }

            var runtime = GetOrCreateStoreRuntime(storeInfo);
            var store = new RecordStore<T>(this, name, storeInfo.ID, runtime, configureBeforeIndexAttachment);
            RegisterLiveRecordStore(store);
            return store;
        }

        /// <summary>
        /// Tests whether an active store definition is visible under the supplied case-insensitive name.<br/>
        /// </summary>
        /// <param name="name">Non-empty store name to inspect.<br/></param>
        /// <returns>True when an active matching definition exists, otherwise false.<br/></returns>
        /// <remarks>
        /// Soft-removed and destructive-operation-pending stores are excluded; this does not test whether a record-store facade is already open.<br/>
        /// </remarks>
        public bool StoreExists(string name)
        {
            return GetStoreInfo(name) is not null;
        }

        /// <summary>
        /// Gets one active store definition by name, with optional access to soft-removed or deletion-pending metadata for administrative inspection.<br/>
        /// Ordinary discovery excludes unavailable stores so callers cannot mistake retained recovery metadata for an openable store.<br/>
        /// </summary>
        /// <param name="name">The case-insensitive store name to inspect.<br/></param>
        /// <param name="includeRemoved">Whether unavailable lifecycle states may be returned.<br/></param>
        /// <returns>The detached persisted store definition, or <c>null</c> when no matching visible store exists.<br/></returns>
        public StoreInfo? GetStoreInfo(string name, bool includeRemoved = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            lock (_storeCatalogLock)
            {
                var stores = ReadStoreInfos();
                if (!stores.TryGetValue(name, out var storeInfo))
                    return null;
                return includeRemoved || storeInfo.Lifecycle == StoreLifecycleState.Active
                    ? storeInfo
                    : null;
            }
        }

        /// <summary>
        /// Gets active store definitions, with optional inclusion of soft-removed and deletion-pending administrative entries.<br/>
        /// The returned dictionary is detached from the durable registry and can be inspected without holding the registry lock.<br/>
        /// </summary>
        /// <param name="includeRemoved">Whether unavailable lifecycle states should be included.<br/></param>
        /// <returns>A case-insensitive detached store-definition dictionary.<br/></returns>
        public StoresInfo? GetStoreInfos(bool includeRemoved = false)
        {
            lock (_storeCatalogLock)
            {
                var stores = ReadStoreInfos();
                if (includeRemoved)
                    return stores;

                var active = new StoresInfo();
                foreach (var pair in stores)
                {
                    if (pair.Value.Lifecycle == StoreLifecycleState.Active)
                        active[pair.Key] = pair.Value;
                }
                return active;
            }
        }

        internal void SetStoreInfo(string name, StoreInfo info)
        {
            lock (_storeCatalogLock)
            {
                var storesInfo = ReadStoreInfos();
                storesInfo[name] = info;
                WriteStoreInfos(storesInfo);
            }
        }

        /// <summary>
        /// Returns the total number of live records across every store hosted in this RecordBase.
        /// </summary>
        public ulong RecordCount()
        {
            ArgumentNullException.ThrowIfNull(data);
            var storeInfos = GetStoreInfos();
            if (storeInfos is null || storeInfos.Count == 0)
                return 0;

            const ulong StoreIdMask = 0xFF00_0000_0000_0000ul;
            ulong total = 0;

            foreach (var storeInfo in storeInfos.Values)
            {
                if (storeInfo is null)
                    continue;

                ulong expectedValue = ((ulong)storeInfo.ID) << 56;
                total += data.RecordCount(StoreIdMask, expectedValue);
            }

            return total;
        }

        // ---- maintenance state fields ----
        private Semutex? _maintenanceSemutex;
        private CancellationTokenSource? _maintenanceCts;
        private Task? _maintenanceHeartbeatTask;
        private string? _maintenanceInstanceId;
        private bool _maintenanceMarkerPersisted;
        private int _suppressMaintenanceHeartbeatWrites;
        private int _writeLockoutCount;

        /// <summary>
        /// Begins one cross-process-exclusive maintenance operation.<br/>
        /// Mutating maintenance persists an in-progress marker and heartbeat by default; read-only snapshot collection can suppress that marker so the backup image does not restore as an apparently live foreign maintenance owner.<br/>
        /// </summary>
        /// <param name="operation">Stable maintenance operation name used for diagnostics and optional durable state.<br/></param>
        /// <param name="ct">Token observed while acquiring the cross-process maintenance semaphore.<br/></param>
        /// <param name="persistMarker">Whether to persist and heartbeat an in-progress source mutation marker.<br/></param>
        private async Task BeginMaintenanceAsync(string operation, CancellationToken ct, bool persistMarker = true)
        {
            // Acquire semutex for cross-process exclusivity
            _maintenanceSemutex = new Semutex(data.UUID.ToString());
            bool acquired = await _maintenanceSemutex.WaitAsync(config.MaintenanceLockAcquireTimeout, ct).ConfigureAwait(false);
            if (!acquired) throw new InvalidOperationException("Could not acquire maintenance semaphore.");

            // durable instance id
            _maintenanceInstanceId = $"{Environment.ProcessId}:{Guid.NewGuid():D}";
            _maintenanceMarkerPersisted = persistMarker;
            if (!persistMarker)
                return;

            var startedTicks = DateTime.UtcNow.Ticks;
            SetStringSetting("Maintenance.Operation", operation);
            SetStringSetting("Maintenance.Status", "InProgress");
            SetStringSetting("Maintenance.InstanceId", _maintenanceInstanceId);
            SetNumericSetting("Maintenance.StartedTicks", startedTicks);
            SetNumericSetting("Maintenance.LastHeartbeatTicks", startedTicks);

            // Start heartbeat task
            var heartbeatInterval = config.GetMaintenanceHeartbeatInterval(RecordCount());
            _maintenanceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var hbCt = _maintenanceCts.Token;
            _maintenanceHeartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (!hbCt.IsCancellationRequested)
                    {
                        await Task.Delay(heartbeatInterval, hbCt).ConfigureAwait(false);
                        try
                        {
                            if (Volatile.Read(ref _suppressMaintenanceHeartbeatWrites) == 0)
                                SetNumericSetting("Maintenance.LastHeartbeatTicks", DateTime.UtcNow.Ticks);
                        }
                        catch { /* best-effort */ }
                    }
                }
                catch (OperationCanceledException) { }
            }, hbCt);
        }

        /// <summary>
        /// Finalize a maintenance operation by setting the final status and releasing the semutex.
        /// Ensures the final state is durably written before releasing the lock.
        /// </summary>
        private async Task CompleteMaintenanceAsync(string finalStatus, string? details = null)
        {
            try
            {
                _maintenanceCts?.Cancel();
                if (_maintenanceHeartbeatTask is not null)
                {
                    try { await _maintenanceHeartbeatTask.ConfigureAwait(false); } catch { }
                }

                if (_maintenanceMarkerPersisted)
                {
                    SetStringSetting("Maintenance.Status", finalStatus);
                    if (!string.IsNullOrEmpty(details)) SetStringSetting("Maintenance.Details", details ?? string.Empty);
                    SetNumericSetting("Maintenance.FinishedTicks", DateTime.UtcNow.Ticks);
                    SetNumericSetting("Maintenance.LastHeartbeatTicks", DateTime.UtcNow.Ticks);
                }
            }
            finally
            {
                try { _maintenanceSemutex?.Release(); } catch { }
                try { _maintenanceSemutex?.Dispose(); } catch { }
                _maintenanceSemutex = null;
                _maintenanceCts?.Dispose();
                _maintenanceCts = null;
                _maintenanceHeartbeatTask = null;
                _maintenanceInstanceId = null;
                _maintenanceMarkerPersisted = false;
            }
        }

        internal void EnsureWriteAllowed(string operationName)
        {
            if (Volatile.Read(ref _writeLockoutCount) > 0)
                throw new InvalidOperationException($"RecordBase is in write-lockout mode. Operation '{operationName}' is temporarily unavailable.");

            string? maintenanceStatus;
            try { maintenanceStatus = GetStringSetting("Maintenance.Status"); } catch { return; }
            if (!string.Equals(maintenanceStatus, "InProgress", StringComparison.OrdinalIgnoreCase))
                return;

            string? maintenanceOperation;
            try { maintenanceOperation = GetStringSetting("Maintenance.Operation"); } catch { return; }
            if (string.IsNullOrWhiteSpace(maintenanceOperation))
                maintenanceOperation = "Unknown";

            string? ownerInstanceId;
            try { ownerInstanceId = GetStringSetting("Maintenance.InstanceId"); } catch { return; }
            var sameOwner = !string.IsNullOrWhiteSpace(ownerInstanceId)
                && string.Equals(ownerInstanceId, _maintenanceInstanceId, StringComparison.Ordinal);
            if (sameOwner)
                return;

            if (TryRecoverStaleMaintenanceMarker(maintenanceOperation))
                return;

            var ownerText = string.IsNullOrWhiteSpace(ownerInstanceId) ? "unknown" : ownerInstanceId;
            throw new InvalidOperationException($"RecordBase is locked for maintenance '{maintenanceOperation}' by instance '{ownerText}'. Operation '{operationName}' is temporarily unavailable.");
        }

        private IDisposable EnterWriteLockoutScope()
        {
            Interlocked.Increment(ref _writeLockoutCount);
            return new WriteLockoutScope(this);
        }

        /// <summary>
        /// Inspects persistent ordered-index storage and returns owned disconnected maintenance evidence without mutating index topology.<br/>
        /// The operation participates in the RecordBase catalog lifecycle barrier so physical compaction cannot invalidate handles during assessment.<br/>
        /// </summary>
        /// <param name="minimumReclaimableBytes">Minimum exact reclaimable bytes required for a reported repack candidate.<br/></param>
        /// <returns>Owned index-only evidence, including attribution/completeness and nullable unknown values; not record-file accounting.<br/></returns>
        public IndexMaintenanceAssessment AssessIndexMaintenance(long minimumReclaimableBytes = 0)
        {
            using var lifecycle = EnterLibraDexOperationScope();
            return new IndexMaintenanceAssessment(libraDexCatalog.Maintenance.Assess(minimumReclaimableBytes));
        }

        /// <summary>
        /// Optimizes persistent ordered-index route and shelf topology using an explicit cumulative work budget; may increase backing-file size.<br/>
        /// </summary>
        /// <param name="options">Optional owned mode/work limit; null uses Bounded (256 traversal units), not a per-record limit.<br/></param>
        /// <returns>The structured completion, work-count, change-count, and incomplete-reason result.<br/></returns>
        public IndexMaintenanceResult Optimize(IndexMaintenanceOptions? options = null)
            => OptimizeAsync(options, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Asynchronously coordinates RecordBase maintenance admission, then synchronously executes live LibraDex topology optimization inside an exclusive catalog lifecycle window.<br/>
        /// Cancellation is observed while acquiring maintenance ownership and draining active catalog operations; LibraDex's connected optimizer completes its admitted bounded walk atomically.<br/>
        /// </summary>
        /// <param name="options">Optional owned mode/work limit; null preserves the bounded engine default.<br/></param>
        /// <param name="ct">Token observed before execution and while active operations drain.<br/></param>
        /// <returns>Owned exact completion/counters/reasons; incomplete supported work is not silently reported as success.<br/></returns>
        public async Task<IndexMaintenanceResult> OptimizeAsync(
            IndexMaintenanceOptions? options = null,
            CancellationToken ct = default)
        {
            await BeginMaintenanceAsync("Optimize", ct).ConfigureAwait(false);
            try
            {
                LibraDexMaintenanceResult result;
                using (EnterWriteLockoutScope())
                using (EnterLibraDexLifecycleExclusiveScope(ct))
                    result = libraDexCatalog.Maintenance.Optimize(IndexMaintenanceBinding.ToNative(options));

                await CompleteMaintenanceAsync(
                    result.Completed ? "Completed" : "Incomplete",
                    result.Completed ? null : result.Message).ConfigureAwait(false);
                return IndexMaintenanceBinding.ToPublic(result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await CompleteMaintenanceAsync("Cancelled", "Operation cancelled by caller").ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                try { await CompleteMaintenanceAsync("Failed", exception.Message).ConfigureAwait(false); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Rewrites reclaimable variable-payload shelves inside the live LibraDex catalog without claiming physical file truncation.<br/>
        /// Use <see cref="Compact()"/> when the goal is closed-file shadow rebuild and exact physical byte reclamation.<br/>
        /// </summary>
        /// <param name="options">Optional owned mode/work limit; null preserves the bounded engine default.<br/></param>
        /// <returns>The structured completion, work-count, change-count, and incomplete-reason result.<br/></returns>
        public IndexMaintenanceResult Repack(IndexMaintenanceOptions? options = null)
            => RepackAsync(options, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Asynchronously coordinates RecordBase maintenance admission, then synchronously repacks live LibraDex variable-payload shelves inside an exclusive catalog lifecycle window.<br/>
        /// Cancellation is observed while acquiring ownership and draining active operations.<br/>
        /// </summary>
        /// <param name="options">Optional owned mode/work limit; null preserves the bounded engine default.<br/></param>
        /// <param name="ct">Token observed before execution and while active operations drain.<br/></param>
        /// <returns>Owned exact completion/counters/reasons, with no claim of physical file truncation.<br/></returns>
        public async Task<IndexMaintenanceResult> RepackAsync(
            IndexMaintenanceOptions? options = null,
            CancellationToken ct = default)
        {
            await BeginMaintenanceAsync("Repack", ct).ConfigureAwait(false);
            try
            {
                LibraDexMaintenanceResult result;
                using (EnterWriteLockoutScope())
                using (EnterLibraDexLifecycleExclusiveScope(ct))
                    result = libraDexCatalog.Maintenance.Repack(IndexMaintenanceBinding.ToNative(options));

                await CompleteMaintenanceAsync(
                    result.Completed ? "Completed" : "Incomplete",
                    result.Completed ? null : result.Message).ConfigureAwait(false);
                return IndexMaintenanceBinding.ToPublic(result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await CompleteMaintenanceAsync("Cancelled", "Operation cancelled by caller").ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                try { await CompleteMaintenanceAsync("Failed", exception.Message).ConfigureAwait(false); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Runs bounded record-storage maintenance, then compacts the persistent ordered-index catalog.<br/>
        /// The synchronous facade drains active ordered-index operations, preserves live RecordStore descriptors, and delegates to <see cref="CompactAsync(RecordBaseCompactionOptions, CancellationToken)"/>.<br/>
        /// </summary>
        /// <returns>Completed live-store rebinding and exact IndexStorage metrics; no exact record-file reclamation is reported.<br/></returns>
        public RecordBaseCompactionResult Compact()
        {
            return CompactAsync(RecordBaseCompactionOptions.None, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Runs bounded record-storage maintenance and ordered-index catalog compaction, with an optional numeric record-index rebuild.<br/>
        /// </summary>
        /// <param name="options">Owned compact flags; RebuildRecordIndex optionally rebuilds numeric record lookup storage, not ordered query indexes.<br/></param>
        /// <returns>IndexStorage physical metrics and successful record-index rebuild status.<br/></returns>
        public RecordBaseCompactionResult Compact(RecordBaseCompactionOptions options)
        {
            return CompactAsync(options, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously compacts authoritative Fractal storage and the persistent LibraDex catalog.<br/>
        /// Cancellation is observed before maintenance starts, while active ordered-index operations drain, around Fractal defragmentation, and before closed-file catalog replacement.<br/>
        /// </summary>
        /// <param name="ct">Cancellation token observed at native maintenance boundaries.<br/></param>
        /// <returns>Owned index-only physical evidence after every live RecordStore has rebound successfully.<br/></returns>
        public Task<RecordBaseCompactionResult> CompactAsync(CancellationToken ct = default)
            => CompactAsync(RecordBaseCompactionOptions.None, ct);

        /// <summary>
        /// Asynchronously compacts authoritative Fractal storage and performs a rollback-protected closed-file shadow rebuild of the persistent LibraDex catalog.<br/>
        /// The operation acquires the durable cross-process marker, drains active ordered-index work, preserves reserved Abraxas records during Fractal maintenance, detaches persistent handles, closes and compacts the catalog, then reopens and transactionally rebinds every live store before admission resumes.<br/>
        /// </summary>
        /// <param name="options">Owned compact flags; None and RebuildRecordIndex are supported.<br/></param>
        /// <param name="ct">Cancellation token observed at native maintenance boundaries.<br/></param>
        /// <returns>Completed rebinding, signed IndexStorage byte/tuple evidence and record-index rebuild status. The bounded record-storage defrag pass does not report exact physical savings.<br/></returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when an undefined compact option is supplied.<br/></exception>
        public async Task<RecordBaseCompactionResult> CompactAsync(RecordBaseCompactionOptions options, CancellationToken ct = default)
        {
            const RecordBaseCompactionOptions supportedOptions = RecordBaseCompactionOptions.RebuildRecordIndex;
            var unsupportedOptions = options & ~supportedOptions;
            if (unsupportedOptions != RecordBaseCompactionOptions.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options,
                    $"Compaction supports only {nameof(RecordBaseCompactionOptions.RebuildRecordIndex)}.");
            }

            ct.ThrowIfCancellationRequested();
            await BeginMaintenanceAsync("Compact", ct).ConfigureAwait(false);
            try
            {
                LibraDexCompactionResult libraDexResult;
                using (EnterWriteLockoutScope())
                using (EnterLibraDexLifecycleExclusiveScope(ct))
                {
                    Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 1);
                    var preservedSpecial = CaptureSpecialRecords();
                    try
                    {
                        DeleteSpecialRecords();
                        ct.ThrowIfCancellationRequested();
                        RunFractalDefragUntilIdle(GetFractalDefragTimeout());
                        ct.ThrowIfCancellationRequested();
                    }
                    finally
                    {
                        RestoreSpecialRecords(preservedSpecial);
                        Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 0);
                    }

                    if (options.HasFlag(RecordBaseCompactionOptions.RebuildRecordIndex))
                    {
                        ct.ThrowIfCancellationRequested();
                        data.RebuildIndex();
                    }

                    ct.ThrowIfCancellationRequested();
                    libraDexResult = CompactAndRebindLibraDex(ct);
                }

                await CompleteMaintenanceAsync("Completed").ConfigureAwait(false);
                return IndexMaintenanceBinding.ToPublic(libraDexResult, (options & RecordBaseCompactionOptions.RebuildRecordIndex) != 0);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await CompleteMaintenanceAsync("Cancelled", "Operation cancelled by caller").ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                try { await CompleteMaintenanceAsync("Failed", ex.Message).ConfigureAwait(false); } catch { }
                throw;
            }
            finally
            {
                Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 0);
            }
        }

        /// <summary>
        /// Replaces the closed persistent LibraDex catalog and transactionally rebinds every live RecordStore to exact reopened physical handles.<br/>
        /// The caller must own the lifecycle-exclusive barrier; failure always attempts to reopen and rebind the authoritative path, and a failed rebind faults future indexed operations rather than exposing stale handles.<br/>
        /// </summary>
        /// <param name="cancellationToken">Token passed to closed-file LibraDex compaction after the old catalog closes.<br/></param>
        /// <param name="compactionOptions">Optional internal physical-compaction settings; Abraxas always retains the rollback image until live-store rebinding succeeds.<br/></param>
        /// <param name="rebindProof">Optional deterministic validation callback invoked after each successful live-store rebind.<br/></param>
        /// <returns>The exact completed LibraDex compaction result.<br/></returns>
        private LibraDexCompactionResult CompactAndRebindLibraDex(
            CancellationToken cancellationToken,
            LibraDexCompactionOptions? compactionOptions = null,
            Action<int>? rebindProof = null)
        {
            var stores = CaptureLiveRecordStores();
            for (var i = 0; i < stores.Length; i++)
                stores[i].DetachPersistentLibraDexBinding();

            var catalogPath = GetLibraDexCatalogPath();
            LibraDexCompactionResult? result = null;
            Exception? operationFailure = null;
            try
            {
                libraDexCatalog.Dispose();
                var retainRequested = compactionOptions?.KeepBackup == true;
                result = Catalog.Compact(
                    name,
                    options: new LibraDexCompactionOptions
                    {
                        CatalogOptions = config.CreateLibraDexCatalogOptions(),
                        KeepBackup = true,
                        BackupPath = compactionOptions?.BackupPath
                    },
                    cancellationToken: cancellationToken, directory: dataFilesLocation.FullName);

                Catalog reopened = Catalog.Open(catalogPath, config.CreateLibraDexCatalogOptions());
                try
                {
                    libraDexCatalog = reopened;
                    RebindLiveRecordStores(stores, reopened, rebindProof);
                }
                catch
                {
                    reopened.Dispose();
                    throw;
                }

                if (!retainRequested && result.Value.BackupPath is not null)
                {
                    try
                    {
                        File.Delete(result.Value.BackupPath);
                        result = result.Value with { BackupPath = null };
                    }
                    catch (IOException)
                    {
                        // The catalog transaction is complete. Preserve and report the still-valid rollback image
                        // rather than invalidating rebound live handles only because best-effort cleanup was denied.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // See the IOException path above. The returned result identifies the retained file.
                    }
                }

                lock (_libraDexLifecycleGate)
                    _libraDexLifecycleFault = null;
                return result.Value;
            }
            catch (Exception exception)
            {
                operationFailure = exception;
            }

            try
            {
                if (result?.BackupPath is not null)
                    RestoreLibraDexCompactionBackup(catalogPath, result.Value.BackupPath);

                Catalog restored = Catalog.Open(catalogPath, config.CreateLibraDexCatalogOptions());
                try
                {
                    libraDexCatalog = restored;
                    RebindLiveRecordStores(stores, restored, rebindProof: null);
                    lock (_libraDexLifecycleGate)
                        _libraDexLifecycleFault = null;
                }
                catch
                {
                    restored.Dispose();
                    throw;
                }
            }
            catch (Exception restorationFailure)
            {
                var lifecycleFailure = new AggregateException(
                    "LibraDex compaction failed and the original catalog could not be restored and rebound.",
                    operationFailure!,
                    restorationFailure);
                lock (_libraDexLifecycleGate)
                    _libraDexLifecycleFault = lifecycleFailure;
                throw lifecycleFailure;
            }

            ExceptionDispatchInfo.Capture(operationFailure!).Throw();
            throw new UnreachableException();
        }

        /// <summary>
        /// Reopens every captured store facade against one catalog instance after closed-file replacement or rollback.<br/>
        /// The optional proof callback runs after each successful facade rebind and is used only by deterministic lifecycle validation invoked through reflection.<br/>
        /// </summary>
        /// <param name="stores">Strong live-store snapshot retained for the complete replacement transaction.<br/></param>
        /// <param name="catalog">Open authoritative catalog whose groups own the replacement handles.<br/></param>
        /// <param name="rebindProof">Optional deterministic proof callback receiving the completed rebind count.<br/></param>
        private static void RebindLiveRecordStores(
            ILibraDexRebindableRecordStore[] stores,
            Catalog catalog,
            Action<int>? rebindProof)
        {
            for (var i = 0; i < stores.Length; i++)
            {
                stores[i].RebindPersistentLibraDexBinding(catalog.IndexSet(stores[i].LibraDexStoreName));
                rebindProof?.Invoke(i + 1);
            }
        }

        /// <summary>
        /// Atomically restores the retained pre-compaction catalog and removes the rejected installed replacement.<br/>
        /// The backup and source are siblings by default and LibraDex requires an explicit backup path to reside on the replacement volume.<br/>
        /// </summary>
        /// <param name="catalogPath">Installed catalog path currently holding the rejected replacement.<br/></param>
        /// <param name="backupPath">Retained original catalog path returned by LibraDex compaction.<br/></param>
        private static void RestoreLibraDexCompactionBackup(string catalogPath, string backupPath)
        {
            var rejectedPath = $"{catalogPath}.rebind-rejected-{Guid.NewGuid():N}";
            try
            {
                File.Replace(backupPath, catalogPath, rejectedPath, ignoreMetadataErrors: true);
            }
            finally
            {
                if (File.Exists(rejectedPath))
                    File.Delete(rejectedPath);
            }
        }


        /// <summary>
        /// Provides a case-insensitive dictionary of store names and their metadata; modifying this container alone does not persist catalog changes.<br/>
        /// </summary>
        public class StoresInfo : Dictionary<string, StoreInfo>
        {
            /// <summary>
            /// Creates an empty store-metadata dictionary using ordinal case-insensitive name comparison.<br/>
            /// </summary>
            public StoresInfo() : base(StringComparer.OrdinalIgnoreCase) { }
        }

        /// <summary>
        /// Reads a caller-owned snapshot of every durable partial-index definition for one record store.<br/>
        /// A missing catalog returns an empty array; malformed catalog data fails closed so callers cannot attach an index whose predicate or readiness proof is unknown.<br/>
        /// </summary>
        /// <param name="storeID">The Fractal group key whose partial-index definitions should be returned.<br/></param>
        /// <returns>Independent catalog-entry copies in deterministic logical-name order.<br/></returns>
        internal PartialIndexCatalogEntry[] GetPartialIndexCatalogEntries(byte storeID)
        {
            lock (_partialIndexCatalogLock)
            {
                var catalog = ReadPartialIndexCatalog();
                return catalog.Entries.Values
                    .Where(entry => entry.StoreID == storeID)
                    .OrderBy(entry => entry.LogicalName, StringComparer.Ordinal)
                    .Select(static entry => entry.Clone())
                    .ToArray();
            }
        }

        /// <summary>
        /// Atomically replaces or adds one durable partial-index definition in the Fractal system catalog.<br/>
        /// The complete catalog is serialized through Inheto and written as one special record so a reopen observes either the prior definition set or the replacement set.<br/>
        /// </summary>
        /// <param name="entry">Validated partial-index definition and readiness proof to persist.<br/></param>
        internal void UpsertPartialIndexCatalogEntry(PartialIndexCatalogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();
            lock (_partialIndexCatalogLock)
            {
                var catalog = ReadPartialIndexCatalog();
                catalog.Entries[entry.CatalogKey] = entry.Clone();
                WritePartialIndexCatalog(catalog);
                var verified = ReadPartialIndexCatalog();
                var found = verified.Entries.TryGetValue(entry.CatalogKey, out var restored);
                var fingerprintMatches = found &&
                    string.Equals(restored!.ConditionFingerprint, entry.ConditionFingerprint, StringComparison.Ordinal);
                var packageMatches = found &&
                    restored!.ConditionPackage.AsSpan().SequenceEqual(entry.ConditionPackage);
                if (!found || !fingerprintMatches || !packageMatches)
                {
                    throw new InvalidDataException(
                        $"Partial-index catalog entry '{entry.CatalogKey}' did not survive immediate Fractal/Inheto readback. " +
                        $"Found={found}; Entries={verified.Entries.Count}; FingerprintMatches={fingerprintMatches}; " +
                        $"PackageMatches={packageMatches}; RestoredPackageLength={restored?.ConditionPackage?.Length ?? -1}; " +
                        $"ExpectedPackageLength={entry.ConditionPackage.Length}.");
                }
            }
        }

        /// <summary>
        /// Removes one durable partial-index definition from the Fractal system catalog when present.<br/>
        /// The method returns without writing when the requested logical identity does not exist.<br/>
        /// </summary>
        /// <param name="storeID">The store group key that owns the definition.<br/></param>
        /// <param name="logicalName">The stable Abraxas-facing logical index name.<br/></param>
        internal void RemovePartialIndexCatalogEntry(byte storeID, string logicalName)
        {
            if (string.IsNullOrWhiteSpace(logicalName))
                throw new ArgumentException("Partial-index logical name cannot be empty.", nameof(logicalName));

            lock (_partialIndexCatalogLock)
            {
                var catalog = ReadPartialIndexCatalog();
                if (!catalog.Entries.Remove(PartialIndexCatalogEntry.BuildCatalogKey(storeID, logicalName)))
                    return;
                WritePartialIndexCatalog(catalog);
            }
        }

        /// <summary>
        /// Restores the versioned partial-index catalog from reserved Fractal system record 6.<br/>
        /// Absence yields a new catalog, while incompatible or malformed payloads throw so no persisted index can be treated as ready accidentally.<br/>
        /// </summary>
        /// <returns>The mutable catalog instance used only while holding the catalog lock.<br/></returns>
        private PartialIndexCatalogRecord ReadPartialIndexCatalog()
        {
            if (!data.TryGetOne(PartialIndexCatalogRecordId, out var record))
                return new PartialIndexCatalogRecord();

            try
            {
                var binary = Inheto.InhetoBinary.FromStream(new BufferStream(record.Data), null);
                var payload = binary.ToObject<byte[]>()
                    ?? throw new InvalidDataException("The partial-index catalog envelope restored as null.");
                var catalog = PartialIndexCatalogCodec.Decode(payload);
                catalog.Validate();
                return catalog;
            }
            finally
            {
                record.Dispose();
            }
        }

        /// <summary>
        /// Serializes and replaces reserved Fractal system record 6 with one validated partial-index catalog snapshot.<br/>
        /// </summary>
        /// <param name="catalog">Catalog snapshot to validate and persist.<br/></param>
        private void WritePartialIndexCatalog(PartialIndexCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            var binary = Inheto.InhetoBinary.FromObject(PartialIndexCatalogCodec.Encode(catalog));
            data.PutOne(PartialIndexCatalogRecordId, binary.RawBytes.AsReadOnlySpan);
        }

        /// <summary>
        /// Tests whether Abraxas has durably removed one ordinary persistent index for the supplied store.<br/>
        /// Tombstones are authoritative developer intent and suppress catalog-driven reattachment even when a prior physical-drop attempt was interrupted.<br/>
        /// </summary>
        /// <param name="storeID">The Fractal group key that owns the index.<br/></param>
        /// <param name="indexName">The logical or physical ordinary-index name being considered for attachment.<br/></param>
        /// <returns><c>true</c> when a matching durable removal tombstone exists; otherwise <c>false</c>.<br/></returns>
        internal bool IsOrdinaryIndexRemoved(byte storeID, string indexName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
            lock (_ordinaryIndexRemovalCatalogLock)
            {
                var catalog = ReadOrdinaryIndexRemovalCatalog();
                return catalog.Entries.Values.Any(
                    entry => entry.StoreID == storeID &&
                        (string.Equals(entry.LogicalName, indexName, StringComparison.Ordinal) ||
                         string.Equals(entry.PhysicalName, indexName, StringComparison.Ordinal)));
            }
        }

        /// <summary>
        /// Persists one ordinary-index removal tombstone before physical catalog mutation begins.<br/>
        /// Immediate readback verification prevents a physical drop from becoming the only durable evidence of developer removal intent.<br/>
        /// </summary>
        /// <param name="entry">The validated store/logical/physical removal identity to persist.<br/></param>
        internal void AddOrdinaryIndexRemoval(OrdinaryIndexRemovalEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();
            lock (_ordinaryIndexRemovalCatalogLock)
            {
                var catalog = ReadOrdinaryIndexRemovalCatalog();
                catalog.Entries[entry.CatalogKey] = entry.Clone();
                WriteOrdinaryIndexRemovalCatalog(catalog);
                var verified = ReadOrdinaryIndexRemovalCatalog();
                if (!verified.Entries.TryGetValue(entry.CatalogKey, out var restored) ||
                    !string.Equals(restored.PhysicalName, entry.PhysicalName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Ordinary-index removal tombstone '{entry.CatalogKey}' did not survive immediate Fractal/Inheto readback.");
                }
            }
        }

        /// <summary>
        /// Removes a durable ordinary-index tombstone after explicit recreation has successfully populated the replacement index.<br/>
        /// Missing entries are a no-op so idempotent creation and legacy catalogs retain low-friction behavior.<br/>
        /// </summary>
        /// <param name="storeID">The Fractal group key that owns the recreated index.<br/></param>
        /// <param name="logicalName">The stable Abraxas logical index name.<br/></param>
        internal void RemoveOrdinaryIndexRemoval(byte storeID, string logicalName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
            lock (_ordinaryIndexRemovalCatalogLock)
            {
                var catalog = ReadOrdinaryIndexRemovalCatalog();
                if (!catalog.Entries.Remove(OrdinaryIndexRemovalEntry.BuildCatalogKey(storeID, logicalName)))
                    return;
                WriteOrdinaryIndexRemovalCatalog(catalog);
            }
        }

        /// <summary>
        /// Restores the versioned ordinary-index removal catalog from reserved Fractal system record 7.<br/>
        /// Absence yields an empty catalog; malformed payloads fail closed so removed physical definitions cannot be silently reattached.<br/>
        /// </summary>
        /// <returns>The mutable catalog snapshot used only while holding the removal-catalog lock.<br/></returns>
        private OrdinaryIndexRemovalCatalogRecord ReadOrdinaryIndexRemovalCatalog()
        {
            if (!data.TryGetOne(OrdinaryIndexRemovalCatalogRecordId, out var record))
                return new OrdinaryIndexRemovalCatalogRecord();

            try
            {
                var binary = Inheto.InhetoBinary.FromStream(new BufferStream(record.Data), null);
                var payload = binary.ToObject<byte[]>()
                    ?? throw new InvalidDataException("The ordinary-index removal catalog envelope restored as null.");
                return OrdinaryIndexRemovalCatalogCodec.Decode(payload);
            }
            finally
            {
                record.Dispose();
            }
        }

        /// <summary>
        /// Serializes and replaces reserved Fractal system record 7 with one validated ordinary-index removal snapshot.<br/>
        /// </summary>
        /// <param name="catalog">The complete tombstone catalog to persist atomically.<br/></param>
        private void WriteOrdinaryIndexRemovalCatalog(OrdinaryIndexRemovalCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            var binary = Inheto.InhetoBinary.FromObject(OrdinaryIndexRemovalCatalogCodec.Encode(catalog));
            data.PutOne(OrdinaryIndexRemovalCatalogRecordId, binary.RawBytes.AsReadOnlySpan);
        }

        /// <summary>
        /// Reads one durable application-codec binding for an ordinary or partial index.<br/>
        /// The returned entry is detached from the catalog lock and may be compared safely by setup and reopen code.<br/>
        /// </summary>
        /// <param name="storeID">Owning Fractal store group.<br/></param>
        /// <param name="kind">Ordinary or partial Abraxas index ownership kind.<br/></param>
        /// <param name="logicalName">Stable Abraxas logical index identity.<br/></param>
        /// <param name="entry">Receives an independent binding copy when present.<br/></param>
        /// <returns><see langword="true"/> when a matching durable binding exists; otherwise <see langword="false"/>.<br/></returns>
        internal bool TryGetApplicationCodecBinding(
            byte storeID,
            ApplicationCodecIndexKind kind,
            string logicalName,
            out ApplicationCodecIndexBindingEntry entry)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
            lock (_applicationCodecCatalogLock)
            {
                var catalog = ReadApplicationCodecCatalog();
                if (catalog.Bindings.TryGetValue(
                    ApplicationCodecIndexBindingEntry.BuildCatalogKey(storeID, kind, logicalName),
                    out var found))
                {
                    entry = found.Clone();
                    return true;
                }
            }

            entry = default!;
            return false;
        }

        /// <summary>
        /// Atomically adds or replaces one application-owned codec binding in reserved Fractal system record 8.<br/>
        /// Immediate readback compares the complete binding so physical index publication never relies on an unverified metadata write.<br/>
        /// </summary>
        /// <param name="entry">Validated preparing or ready binding to persist.<br/></param>
        internal void UpsertApplicationCodecBinding(ApplicationCodecIndexBindingEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();
            lock (_applicationCodecCatalogLock)
            {
                var catalog = ReadApplicationCodecCatalog();
                catalog.Bindings[entry.CatalogKey] = entry.Clone();
                WriteApplicationCodecCatalog(catalog);
                var verified = ReadApplicationCodecCatalog();
                if (!verified.Bindings.TryGetValue(entry.CatalogKey, out var restored) ||
                    !restored.ContractEquals(entry, includeReady: true))
                {
                    throw new InvalidDataException(
                        $"Application-codec binding '{entry.CatalogKey}' did not survive immediate Fractal/Inheto readback.");
                }
            }
        }

        /// <summary>
        /// Removes one durable application-codec binding and prunes its format automatically through deterministic re-encoding.<br/>
        /// Missing bindings are a no-op, allowing ordinary-index removal and failed-creation cleanup to remain idempotent.<br/>
        /// </summary>
        /// <param name="storeID">Owning Fractal store group.<br/></param>
        /// <param name="kind">Ordinary or partial Abraxas index ownership kind.<br/></param>
        /// <param name="logicalName">Stable Abraxas logical index identity.<br/></param>
        internal void RemoveApplicationCodecBinding(
            byte storeID,
            ApplicationCodecIndexKind kind,
            string logicalName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
            lock (_applicationCodecCatalogLock)
            {
                var catalog = ReadApplicationCodecCatalog();
                if (!catalog.Bindings.Remove(
                    ApplicationCodecIndexBindingEntry.BuildCatalogKey(storeID, kind, logicalName)))
                {
                    return;
                }

                WriteApplicationCodecCatalog(catalog);
            }
        }

        /// <summary>
        /// Restores the bounded versioned application-codec catalog from reserved Fractal system record 8.<br/>
        /// Missing data produces an empty catalog; malformed identities, ordinals, contracts, or trailing data fail closed.<br/>
        /// </summary>
        /// <returns>The mutable catalog snapshot used only while holding the codec-catalog lock.<br/></returns>
        private ApplicationCodecCatalogRecord ReadApplicationCodecCatalog()
        {
            if (!data.TryGetOne(ApplicationCodecCatalogRecordId, out var record))
                return new ApplicationCodecCatalogRecord();

            try
            {
                var binary = Inheto.InhetoBinary.FromStream(new BufferStream(record.Data), null);
                var payload = binary.ToObject<byte[]>()
                    ?? throw new InvalidDataException("The application-codec catalog envelope restored as null.");
                return ApplicationCodecCatalogCodec.Decode(payload);
            }
            finally
            {
                record.Dispose();
            }
        }

        /// <summary>
        /// Serializes and atomically replaces reserved Fractal system record 8 with one validated codec-binding snapshot.<br/>
        /// Re-encoding rebuilds the unique format table, so removing the last binding automatically removes its unused string identity.<br/>
        /// </summary>
        /// <param name="catalog">Complete application-codec binding catalog to persist.<br/></param>
        private void WriteApplicationCodecCatalog(ApplicationCodecCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            var binary = Inheto.InhetoBinary.FromObject(ApplicationCodecCatalogCodec.Encode(catalog));
            data.PutOne(ApplicationCodecCatalogRecordId, binary.RawBytes.AsReadOnlySpan);
        }

        /// <summary>
        /// Returns detached ordinary-index definitions owned by one store.<br/>
        /// Definitions are persisted Abraxas intent and may describe either a ready index or an interrupted preparation that reopen must rebuild before publication.<br/>
        /// </summary>
        /// <param name="storeID">The Fractal group key that owns the definitions.<br/></param>
        /// <returns>A caller-owned snapshot ordered by stable catalog identity.<br/></returns>
        internal OrdinaryIndexIntentEntry[] GetOrdinaryIndexIntentEntries(byte storeID)
        {
            lock (_ordinaryIndexIntentCatalogLock)
            {
                return ReadOrdinaryIndexIntentCatalog().Entries.Values
                    .Where(entry => entry.StoreID == storeID)
                    .OrderBy(static entry => entry.CatalogKey, StringComparer.Ordinal)
                    .Select(static entry => entry.Clone())
                    .ToArray();
            }
        }

        /// <summary>
        /// Reads one durable ordinary-index definition by its stable store and logical identity.<br/>
        /// The returned value is detached from the catalog lock and safe for setup-time contract comparison.<br/>
        /// </summary>
        /// <param name="storeID">The Fractal group key that owns the definition.<br/></param>
        /// <param name="logicalName">The stable Abraxas logical index identity.<br/></param>
        /// <param name="entry">Receives an independent definition copy when present.<br/></param>
        /// <returns><c>true</c> when the definition exists; otherwise <c>false</c>.<br/></returns>
        internal bool TryGetOrdinaryIndexIntent(
            byte storeID,
            string logicalName,
            out OrdinaryIndexIntentEntry entry)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
            lock (_ordinaryIndexIntentCatalogLock)
            {
                var catalog = ReadOrdinaryIndexIntentCatalog();
                if (catalog.Entries.TryGetValue(
                    OrdinaryIndexIntentEntry.BuildCatalogKey(storeID, logicalName),
                    out var found))
                {
                    entry = found.Clone();
                    return true;
                }
            }

            entry = default!;
            return false;
        }

        /// <summary>
        /// Atomically adds or replaces one preparing or ready ordinary-index definition in reserved Fractal system record 9.<br/>
        /// Immediate readback verifies the complete definition before callers create, rebuild, or publish matching LibraDex reality.<br/>
        /// </summary>
        /// <param name="entry">The complete validated definition to persist.<br/></param>
        internal void UpsertOrdinaryIndexIntent(OrdinaryIndexIntentEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();
            lock (_ordinaryIndexIntentCatalogLock)
            {
                var catalog = ReadOrdinaryIndexIntentCatalog();
                catalog.Entries[entry.CatalogKey] = entry.Clone();
                WriteOrdinaryIndexIntentCatalog(catalog);
                var verified = ReadOrdinaryIndexIntentCatalog();
                if (!verified.Entries.TryGetValue(entry.CatalogKey, out var restored) ||
                    !restored.ContractEquals(entry, includeReady: true))
                {
                    throw new InvalidDataException(
                        $"Ordinary-index intent '{entry.CatalogKey}' did not survive immediate Fractal/Inheto readback.");
                }
            }
        }

        /// <summary>
        /// Removes one active ordinary-index definition after durable removal intent has already been recorded.<br/>
        /// Missing definitions are a no-op so removal of legacy physical indexes remains compatible and restartable.<br/>
        /// </summary>
        /// <param name="storeID">The Fractal group key that owns the definition.<br/></param>
        /// <param name="logicalName">The stable Abraxas logical index identity.<br/></param>
        internal void RemoveOrdinaryIndexIntent(byte storeID, string logicalName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
            lock (_ordinaryIndexIntentCatalogLock)
            {
                var catalog = ReadOrdinaryIndexIntentCatalog();
                if (!catalog.Entries.Remove(OrdinaryIndexIntentEntry.BuildCatalogKey(storeID, logicalName)))
                    return;
                WriteOrdinaryIndexIntentCatalog(catalog);
            }
        }

        /// <summary>
        /// Restores the bounded versioned ordinary-index intent catalog from reserved Fractal system record 9.<br/>
        /// Absence yields an empty catalog; malformed definitions fail closed before ambiguous runtime index semantics can attach.<br/>
        /// </summary>
        /// <returns>The mutable catalog snapshot used only while holding the ordinary-intent catalog lock.<br/></returns>
        private OrdinaryIndexIntentCatalogRecord ReadOrdinaryIndexIntentCatalog()
        {
            if (!data.TryGetOne(OrdinaryIndexIntentCatalogRecordId, out var record))
                return new OrdinaryIndexIntentCatalogRecord();

            try
            {
                var binary = Inheto.InhetoBinary.FromStream(new BufferStream(record.Data), null);
                var payload = binary.ToObject<byte[]>()
                    ?? throw new InvalidDataException("The ordinary-index intent catalog envelope restored as null.");
                return OrdinaryIndexIntentCatalogCodec.Decode(payload);
            }
            finally
            {
                record.Dispose();
            }
        }

        /// <summary>
        /// Serializes and atomically replaces reserved Fractal system record 9 with one validated ordinary-index definition snapshot.<br/>
        /// </summary>
        /// <param name="catalog">The complete ordinary-index intent catalog to persist.<br/></param>
        private void WriteOrdinaryIndexIntentCatalog(OrdinaryIndexIntentCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            var binary = Inheto.InhetoBinary.FromObject(OrdinaryIndexIntentCatalogCodec.Encode(catalog));
            data.PutOne(OrdinaryIndexIntentCatalogRecordId, binary.RawBytes.AsReadOnlySpan);
        }

        /// <summary>
        /// Carries one store's persisted identity, descriptive metadata, generation, and availability state.<br/>
        /// </summary>
        public class StoreInfo
        {
            /// <summary>
            /// Gets or sets the compact store identifier encoded in its record identities.<br/>
            /// </summary>
            public byte ID { get; set; }
            /// <summary>
            /// Gets or sets the store identity, matching its registry key under ordinal case-insensitive comparison.<br/>
            /// Creation or Inheto hydration supplies this member before catalog use; registry read/write boundaries reject missing or invalid names.<br/>
            /// A newly constructed or caller-modified detached metadata object is not itself a validated catalog entry.<br/>
            /// </summary>
            public string Name { get; set; } = null!;
            /// <summary>Gets or sets optional descriptive text; null and empty remain distinct metadata values.<br/></summary>
            public string? Description { get; set; }
            /// <summary>
            /// Gets or sets the originally recorded CLR type when available.<br/>
            /// Missing metadata or a persisted type name that cannot be resolved may leave this null; typed opening retains its existing fallback policy.<br/>
            /// </summary>
            public Type? ItemType { get; set; }
            /// <summary>
            /// Gets or sets the recorded UTC store-creation time.<br/>
            /// </summary>
            public DateTime CreatedUTC { get; set; }
            /// <summary>
            /// Gets or sets legacy physical index-name metadata; use the store's index-discovery API for the current maintained index inventory.<br/>
            /// </summary>
            public List<IndexPathVariants> IndexVariants { get; set; } = new();
            /// <summary>
            /// Gets or sets the durable store-generation identity used to invalidate stale facades across removal, restoration, or recreation.<br/>
            /// </summary>
            public Guid InstanceID { get; set; }
            /// <summary>
            /// Gets or sets durable store availability metadata; changing a detached metadata object does not perform a lifecycle operation.<br/>
            /// </summary>
            public StoreLifecycleState Lifecycle { get; set; }
            /// <summary>
            /// Gets or sets the retained residual-data policy for a pending purge; changing detached metadata does not initiate a purge.<br/>
            /// </summary>
            public StorePurgeResidualPolicy PurgeResidualPolicy { get; set; }
        }

        /// <summary>
        /// Carries retained legacy physical index-name variants associated with one path identifier.<br/>
        /// </summary>
        public class IndexPathVariants
        {
            /// <summary>
            /// Gets or sets the legacy identifier associated with this physical index-name group.<br/>
            /// </summary>
            public long PathId { get; set; }
            /// <summary>
            /// Gets or sets retained names for the group's base index variants.<br/>
            /// </summary>
            public List<string> BaseIndexNames { get; set; } = new();
            /// <summary>
            /// Gets or sets retained names for the group's culture sort-key variants.<br/>
            /// </summary>
            public List<string> CollationKeyIndexNames { get; set; } = new();
            /// <summary>
            /// Gets or sets retained names for the group's folded-text variants.<br/>
            /// </summary>
            public List<string> FoldedIndexNames { get; set; } = new();
        }

        /// <summary>
        /// Explicitly adopts the current directory/name of a complete RecordBase after a deliberate move or copy.<br/>
        /// Rewrites record-file ownership metadata, not record payloads, logical identities or index definitions.<br/>
        /// Keep the complete family offline and exclude concurrent opens/writers throughout adoption; the sharing preflight is not an inter-process lease.<br/>
        /// Missing authority, retained restore originals and pending runtime publication/recovery artifacts are rejected rather than created or bypassed.<br/>
        /// The new UTF-8 directory/filename must fit the space reserved in each existing ownership header; adoption does not enlarge that native reservation.<br/>
        /// This operation is not a multi-file atomic transaction; correct an underlying I/O failure before explicitly retrying.<br/>
        /// </summary>
        /// <param name="dataFilesLocation">Absolute or relative directory containing all six authoritative files; relative paths use the process current directory.<br/></param>
        /// <param name="name">Logical RecordBase basename, without an authoritative file extension.<br/></param>
        /// <param name="config">Optional reopen configuration; creation is always disabled for adoption.<br/></param>
        /// <exception cref="InvalidDataException">The family is incomplete or bounded structural preflight fails.<br/></exception>
        /// <exception cref="IOException">Files are in use, redirected, or retained recovery authority prevents adoption.<br/></exception>
        public static void TakeOwnership(string dataFilesLocation, string name, RecordBaseConfiguration? config = null)
        {
            ValidateRecordBaseName(name);
            string directory = new DirectoryInfo(dataFilesLocation).FullName;
            EnsureNoRetainedRestore(directory, name);
            ValidateCompleteRecordBase(directory, name, deep: false, CancellationToken.None);
            if (BackupRuntimeNames(name).Skip(1).Any(member => Path.Exists(Path.Combine(directory, member))))
                throw new IOException("Pending RecordBase recovery/publication must complete before explicit ownership adoption.");
            EnsureBackupDestination(directory, BackupMemberNames(name), overwrite: true);
            config ??= RecordBaseConfiguration.Default;
            FractalStore.TakeOwnership(directory, name, config.FractalStoreConfig with { CreateIfNotExists = false });
        }

        /// <summary>
        /// Explicitly adopts the complete offline RecordBase described by a caught <see cref="RecordBaseRelocationException"/>.<br/>
        /// Uses the attempted directory and exact logical basename, including names containing dots; no filename parsing or automatic retry occurs.<br/>
        /// Correct the reason for any failed adoption before retrying, then call Open or CreateOrOpen again.<br/>
        /// </summary>
        /// <param name="ex">Abraxas relocation failure whose current location the caller deliberately accepts.<br/></param>
        /// <param name="config">Optional reopen configuration; all complete-family and offline guards remain in effect.<br/></param>
        /// <exception cref="ArgumentNullException">The supplied exception is null.<br/></exception>
        public static void TakeOwnership(RecordBaseRelocationException ex, RecordBaseConfiguration? config = null)
        {
            ArgumentNullException.ThrowIfNull(ex);
            TakeOwnership(ex.CurrentDirectory, ex.RecordBaseName, config);
        }

        private int _disposed;
        /// <summary>
        /// Synchronously waits for RecordBase disposal to release owned catalogs, identity allocation, maintenance infrastructure, and record-file handles.<br/>
        /// </summary>
        /// <remarks>
        /// Finish dependent store operations before disposal. Cleanup attempts continue after individual failures, which are reported together; disposal does not delete persisted RecordBase files.<br/>
        /// </remarks>
        /// <exception cref="AggregateException">One or more resource-disconnection or cleanup checks failed.<br/></exception>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Releases owned catalogs and file handles and awaits shutdown of maintenance infrastructure.<br/>
        /// </summary>
        /// <returns>Completion of this disposal attempt; repeating disposal does not repeat resource cleanup.<br/></returns>
        /// <remarks>
        /// Finish dependent store operations before disposal. Cleanup attempts continue after individual failures, which are reported together; disposal does not delete persisted RecordBase files.<br/>
        /// </remarks>
        /// <exception cref="AggregateException">One or more resource-disconnection or cleanup checks failed.<br/></exception>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            var errors = new List<Exception>();

            IDisposable? libraDexLifecycle = null;
            try
            {
                libraDexLifecycle = EnterLibraDexLifecycleExclusiveScope(CancellationToken.None);

                // Dispose volatile LibraDex index catalogs.
                foreach (var catalog in _volatileIndexCatalogs)
                {
                    try
                    {
                        catalog.Dispose();
                    }
                    catch (Exception ex) { errors.Add(ex); }
                }

                // Dispose row ID generator
                try
                {
                    idGen?.Dispose();
                }
                catch (Exception ex) { errors.Add(ex); }

                if (libraDexCatalog is not null)
                {
                    try
                    {
                        libraDexCatalog.Dispose();
                    }
                    catch (Exception ex) { errors.Add(ex); }
                }

            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            finally
            {
                libraDexLifecycle?.Dispose();
            }

            // Stop any maintenance heartbeat/semutex infrastructure for this process.
            try
            {
                await StopMaintenanceInfrastructureAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { errors.Add(ex); }

            // Close Fractal file handles
            if (data is not null)
            {
                try
                {
                    data.Close();
                }
                catch (Exception ex) { errors.Add(ex); }
            }

            try
            {
                VerifyNoOpenFileHandles();
            }
            catch (Exception ex) { errors.Add(ex); }

            if (errors.Count > 0)
                throw new AggregateException("RecordBase dispose did not complete a clean disconnect of all resources.", errors);
        }

        private async Task StopMaintenanceInfrastructureAsync()
        {
            _maintenanceCts?.Cancel();
            if (_maintenanceHeartbeatTask is not null)
            {
                try { await _maintenanceHeartbeatTask.ConfigureAwait(false); } catch { }
            }

            try { _maintenanceSemutex?.Release(); } catch { }
            try { _maintenanceSemutex?.Dispose(); } catch { }
            _maintenanceSemutex = null;
            _maintenanceCts?.Dispose();
            _maintenanceCts = null;
            _maintenanceHeartbeatTask = null;
            _maintenanceInstanceId = null;
            _maintenanceMarkerPersisted = false;
        }

        private void VerifyNoOpenFileHandles()
        {
            var failures = new List<string>();
            var files = dataFilesLocation
                .GetFiles()
                .Where(f => f.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var acquired = false;

                for (int attempt = 0; attempt < 8 && !acquired; attempt++)
                {
                    file.Refresh();
                    if (!file.Exists)
                    {
                        acquired = true;
                        break;
                    }

                    try
                    {
                        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
                        acquired = true;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(40);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Thread.Sleep(40);
                    }
                }

                if (!acquired)
                    failures.Add(file.Name);
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"RecordBase dispose verification failed. Files still locked: {string.Join(", ", failures)}");
        }

        /// <summary>
        /// Computes the on-disk size of RecordBase-related files in the data folder.<br/>
        /// Flags control whether Fractal and LibraDex files are included in the total.<br/>
        /// The parameterless form measures only the authoritative Fractal and LibraDex files.<br/>
        /// </summary>
        /// <param name="detailFlags">Controls which RecordBase-related files are included.<br/></param>
        /// <returns>Total size in bytes for the requested file set.<br/></returns>
        public ulong GetFileSize(FileSizeDetailFlags detailFlags = FileSizeDetailFlags.Native)
        {
            if (detailFlags == FileSizeDetailFlags.None || dataFilesLocation is null)
                return 0;

            if (detailFlags.HasFlag(FileSizeDetailFlags.All))
                return GetAllRelatedFileSizes();

            ulong total = 0;
            if (detailFlags.HasFlag(FileSizeDetailFlags.FractalData) && data is not null)
                total += data.GetFileSize(FractalStore.FractalFileSizeDetailFlags.DataFile);
            if (detailFlags.HasFlag(FileSizeDetailFlags.FractalIndex) && data is not null)
                total += data.GetFileSize(FractalStore.FractalFileSizeDetailFlags.IndexFile);
            if (detailFlags.HasFlag(FileSizeDetailFlags.LibraDexCatalog))
                total += GetLibraDexCatalogFileSize();

            return total;
        }

        private ulong GetAllRelatedFileSizes()
        {
            var di = dataFilesLocation;
            if (di is null || !di.Exists)
                return 0;

            ulong total = 0;
            foreach (var file in di.GetFiles())
            {
                file.Refresh();
                if (!file.Exists)
                    continue;
                if (file.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    total += (ulong)file.Length;
            }

            return total;
        }

        private ulong GetLibraDexCatalogFileSize()
        {
            var path = GetLibraDexCatalogPath();
            var fi = new FileInfo(path);
            fi.Refresh();
            return fi.Exists ? (ulong)fi.Length : 0;
        }

        /// <summary>
        /// Returns the resolved primary file path for this RecordBase's named persistent LibraDex catalog.<br/>
        /// </summary>
        /// <returns>Full path to the LibraDex catalog file ending in ".lbdx".<br/></returns>
        private string GetLibraDexCatalogPath()
        {
            return Catalog.GetFilePath(name, dataFilesLocation.FullName);
        }

        /// <summary>
        /// Returns the normalized persistent LibraDex catalog path for an Abraxas-owned temporary index-build workspace.<br/>
        /// Callers use only its directory and basename; the returned persistent path remains authoritative and must never be replaced by a temporary run.<br/>
        /// </summary>
        /// <returns>The absolute sibling `.lbdx` catalog path for this RecordBase.<br/></returns>
        internal string GetLibraDexCatalogPathForIndexBackfill() => GetLibraDexCatalogPath();

        /// <summary>
        /// Computes a best-effort timeout for Fractal defrag based on the current data file size.<br/>
        /// Formula: 2s + 50ms per MB, clamped to [2s, 2m].<br/>
        /// </summary>
        /// <returns>The computed timeout duration.<br/></returns>
        private TimeSpan GetFractalDefragTimeout()
        {
            var bytes = data?.GetFileSize(FractalStore.FractalFileSizeDetailFlags.DataFile) ?? 0;
            var mb = bytes / (1024d * 1024d);
            var seconds = 2.0 + (0.05 * mb);
            if (seconds < 2.0)
                seconds = 2.0;
            if (seconds > 120.0)
                seconds = 120.0;
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// Runs Fractal online defrag and waits until it reaches idle or the timeout expires.<br/>
        /// Always stops the defrag thread on exit (best-effort).<br/>
        /// </summary>
        /// <param name="timeout">Maximum time to wait for idle state.<br/></param>
        private void RunFractalDefragUntilIdle(TimeSpan timeout)
        {
            if (data is null)
                return;

            data.StartOnlineDefrag();

            try
            {
                var sw = Stopwatch.StartNew();

                while (sw.Elapsed < timeout)
                {
                    var stats = data.GetOnlineDefragStats();
                    if (!stats.IsRunning || stats.State == FractalStore.OnlineDefragState.Stopped)
                        break;

                    if (stats.Iterations > 0 && stats.State == FractalStore.OnlineDefragState.IdleBackoff)
                        break;

                    Thread.Sleep(100);
                }
            }
            finally
            {
                data.StopOnlineDefrag();
            }
        }

        /// <summary>
        /// Captures RecordBase special records (IDs 1-10) into memory so defrag can safely trim without EOF churn.<br/>
        /// </summary>
        /// <returns>List of preserved records to restore later; empty when none are present.<br/></returns>
        private List<(ulong Id, byte[] Data)> CaptureSpecialRecords()
        {
            var preserved = new List<(ulong Id, byte[] Data)>();
            if (data is null)
                return preserved;

            for (ulong id = 1; id <= 10; id++)
            {
                if (!data.TryGetOne(id, out var rec))
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

            return preserved;
        }

        /// <summary>
        /// Deletes RecordBase special records (IDs 1-10) from the Fractal store.<br/>
        /// No-op when the Fractal store is unavailable or records are missing.<br/>
        /// </summary>
        private void DeleteSpecialRecords()
        {
            if (data is null)
                return;

            for (ulong id = 1; id <= 10; id++)
            {
                try { data.DeleteOne(id, zeroWrite: false); } catch { }
            }
        }

        /// <summary>
        /// Restores previously captured special records (IDs 1-10) after defrag completes.<br/>
        /// Always best-effort; restores are attempted even after exceptions via a finally path.<br/>
        /// </summary>
        /// <param name="preserved">Records captured via <see cref="CaptureSpecialRecords"/>.<br/></param>
        private void RestoreSpecialRecords(List<(ulong Id, byte[] Data)> preserved)
        {
            if (data is null || preserved is null || preserved.Count == 0)
                return;

            for (int i = 0; i < preserved.Count; i++)
            {
                var (id, bytes) = preserved[i];
                try { data.PutOne(id, bytes); } catch { }
            }
        }

        /// <summary>
        /// Options controlling how <see cref="CreateBackup"/> behaves.<br/>
        /// </summary>
        public sealed class BackupOptions
        {
            /// <summary>
            /// Must remain true: complete cross-component snapshots require write lockout and maintenance/lifecycle gates.<br/>
            /// False is rejected rather than labeling separately timed component copies as one coherent backup.<br/>
            /// </summary>
            public bool RequireLockout { get; set; } = true;

            /// <summary>
            /// When true, allows existing files in the destination folder to be overwritten.<br/>
            /// </summary>
            public bool OverwriteExisting { get; set; } = false;
        }

        /// <summary>
        /// Options controlling how backup archive creation behaves.<br/>
        /// This wraps snapshot copy behavior plus zip packaging options.<br/>
        /// </summary>
        public sealed class BackupArchiveOptions
        {
            /// <summary>
            /// Snapshot options used while collecting files before archiving.<br/>
            /// </summary>
            public BackupOptions SnapshotOptions { get; set; } = new BackupOptions();

            /// <summary>
            /// Compression mode used when writing the zip archive.<br/>
            /// </summary>
            public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

            /// <summary>
            /// When true, an existing archive at the target path is overwritten.<br/>
            /// </summary>
            public bool OverwriteExistingArchive { get; set; } = false;
        }

        /// <summary>
        /// Options controlling restore behavior for <see cref="RestoreBackup(string, string, string, RecordBaseConfiguration, RestoreBackupOptions)"/> APIs.<br/>
        /// </summary>
        public sealed class RestoreBackupOptions
        {
            /// <summary>
            /// When true, exact known runtime artifacts are retired along with the complete authoritative member set.<br/>
            /// Unrelated files and prefix-related RecordBases are never removed. Authoritative members are always restored as a complete set.<br/>
            /// </summary>
            public bool ClearExisting { get; set; } = true;

            /// <summary>
            /// When true, destination files are overwritten when a name collision occurs.<br/>
            /// </summary>
            public bool OverwriteExisting { get; set; } = true;

            /// <summary>
            /// Explicitly permits complete older snapshots without an integrity manifest.<br/>
            /// Required members, structural validation and failure-safe publication still apply; same-generation coherence cannot be proven.<br/>
            /// </summary>
            public bool AllowLegacyBackupWithoutManifest { get; set; } = false;
        }

        /// <summary>
        /// Result metadata returned by <see cref="CreateBackup"/>.<br/>
        /// </summary>
        public readonly record struct BackupResult(
            string TargetDirectory,
            int FileCount,
            ulong TotalBytes,
            DateTime UtcTimestamp);

        /// <summary>
        /// Result metadata returned by backup archive operations.<br/>
        /// </summary>
        public readonly record struct BackupArchiveResult(
            string ArchivePath,
            int FileCount,
            ulong TotalBytes,
            DateTime UtcTimestamp);

        /// <summary>
        /// Result metadata returned by restore operations.<br/>
        /// </summary>
        public readonly record struct RestoreBackupResult(
            string SourceDirectory,
            string TargetDirectory,
            int FileCount,
            ulong TotalBytes,
            BackupVerification Verification,
            DateTime UtcTimestamp);

        /// <summary>
        /// Summary of authoritative Fractal and LibraDex health for the current RecordBase.<br/>
        /// </summary>
        public readonly record struct HealthSnapshot(
            DateTime UtcTimestamp,
            ulong TotalRecordCount,
            int StoreCount,
            ulong FractalDataBytes,
            ulong FractalIndexBytes,
            ulong LibraDexCatalogBytes,
            ulong TotalBytes,
            bool IsOnlineDefragRunning,
            string OnlineDefragState);

        /// <summary>
        /// Creates a backup snapshot by copying all RecordBase-related files into <paramref name="targetDirectory"/>.<br/>
        /// Lockout mode is enabled by default so writes are blocked during copy and readers see a stable point-in-time snapshot.<br/>
        /// </summary>
        /// <param name="targetDirectory">Destination folder that will receive copied files.<br/></param>
        /// <param name="options">Backup behavior options. Null uses defaults.<br/></param>
        /// <returns>Metadata describing the created backup snapshot.<br/></returns>
        public BackupResult CreateBackup(string targetDirectory, BackupOptions? options = null)
            => CreateBackupAsync(targetDirectory, options, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Asynchronously creates a backup snapshot by copying all RecordBase-related files into <paramref name="targetDirectory"/>.<br/>
        /// </summary>
        /// <param name="targetDirectory">Destination folder that will receive copied files.<br/></param>
        /// <param name="options">Backup behavior options. Null uses defaults.<br/></param>
        /// <param name="ct">Cancellation token for the backup operation.<br/></param>
        /// <returns>Metadata describing the created backup snapshot.<br/></returns>
        public async Task<BackupResult> CreateBackupAsync(string targetDirectory, BackupOptions? options = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
            options ??= new BackupOptions();
            ct.ThrowIfCancellationRequested();
            if (!options.RequireLockout)
                throw new NotSupportedException("Complete RecordBase backup requires RequireLockout=true.");
            var fullTarget = ResolveBackupDirectory(targetDirectory);
            RejectBackupDirectoryAlias(dataFilesLocation.FullName, fullTarget);

            var maintenanceEntered = false;
            IDisposable? writeLockScope = null;
            IDisposable? lifecycleExclusiveScope = null;

            try
            {
                if (options.RequireLockout)
                {
                    writeLockScope = EnterWriteLockoutScope();
                    await BeginMaintenanceAsync("Backup", ct, persistMarker: false).ConfigureAwait(false);
                    maintenanceEntered = true;
                    Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 1);
                }

                if (options.RequireLockout)
                {
                    lifecycleExclusiveScope = EnterLibraDexLifecycleExclusiveScope(ct);
                }

                var snapshot = CreateCompleteBackup(fullTarget, options, ct);

                lifecycleExclusiveScope?.Dispose();
                lifecycleExclusiveScope = null;
                if (options.RequireLockout && maintenanceEntered)
                {
                    await CompleteMaintenanceAsync("Completed").ConfigureAwait(false);
                    maintenanceEntered = false;
                }

                return snapshot;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                lifecycleExclusiveScope?.Dispose();
                lifecycleExclusiveScope = null;
                if (options.RequireLockout && maintenanceEntered)
                {
                    await CompleteMaintenanceAsync("Cancelled", "Backup cancelled by caller").ConfigureAwait(false);
                    maintenanceEntered = false;
                }
                throw;
            }
            catch (Exception ex)
            {
                lifecycleExclusiveScope?.Dispose();
                lifecycleExclusiveScope = null;
                if (options.RequireLockout && maintenanceEntered)
                {
                    try
                    {
                        await CompleteMaintenanceAsync("Failed", ex.Message).ConfigureAwait(false);
                        maintenanceEntered = false;
                    }
                    catch { }
                }
                throw;
            }
            finally
            {
                Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 0);
                lifecycleExclusiveScope?.Dispose();
                writeLockScope?.Dispose();
            }
        }

        /// <summary>
        /// Creates a single-file backup archive (zip container) at <paramref name="targetArchivePath"/>.<br/>
        /// The archive is suitable for transport or long-term storage while preserving low-friction folder backup internals.<br/>
        /// </summary>
        /// <param name="targetArchivePath">Destination archive path (for example <c>WHERZIT_20260212.abxbak</c>).<br/></param>
        /// <param name="options">Archive and snapshot behavior options. Null uses defaults.<br/></param>
        /// <returns>Metadata describing the created archive backup.<br/></returns>
        public BackupArchiveResult CreateBackupArchive(string targetArchivePath, BackupArchiveOptions? options = null)
            => CreateBackupArchiveAsync(targetArchivePath, options, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Asynchronously creates a single-file backup archive (zip container) at <paramref name="targetArchivePath"/>.<br/>
        /// </summary>
        /// <param name="targetArchivePath">Destination archive path (for example <c>WHERZIT_20260212.abxbak</c>).<br/></param>
        /// <param name="options">Archive and snapshot behavior options. Null uses defaults.<br/></param>
        /// <param name="ct">Cancellation token for snapshot and archive creation.<br/></param>
        /// <returns>Metadata describing the created archive backup.<br/></returns>
        public async Task<BackupArchiveResult> CreateBackupArchiveAsync(string targetArchivePath, BackupArchiveOptions? options = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetArchivePath);
            options ??= new BackupArchiveOptions();
            ct.ThrowIfCancellationRequested();

            var fullArchivePath = Path.GetFullPath(targetArchivePath);
            var parent = Path.GetDirectoryName(fullArchivePath);
            if (string.IsNullOrWhiteSpace(parent))
                throw new InvalidOperationException("Target archive path must include a parent directory.");
            Directory.CreateDirectory(parent);
            RejectArchiveMemberCollision(fullArchivePath, dataFilesLocation.FullName, name);

            if (File.Exists(fullArchivePath))
            {
                if (!options.OverwriteExistingArchive)
                    throw new IOException($"Target archive already exists: '{fullArchivePath}'.");
                RejectBackupMemberAlias(fullArchivePath);
            }

            var tempFolder = Path.Combine(Path.GetTempPath(), $"abraxas_backup_{name}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempFolder);
            var stagedArchive = Path.Combine(parent, "." + Path.GetFileName(fullArchivePath) + ".backup-" + Guid.NewGuid().ToString("N"));

            try
            {
                var snapshot = await CreateBackupAsync(tempFolder, options.SnapshotOptions, ct).ConfigureAwait(false);
                ZipFile.CreateFromDirectory(tempFolder, stagedArchive, options.CompressionLevel, includeBaseDirectory: false);
                ct.ThrowIfCancellationRequested();
                File.Move(stagedArchive, fullArchivePath, overwrite: options.OverwriteExistingArchive);
                return new BackupArchiveResult(fullArchivePath, snapshot.FileCount, snapshot.TotalBytes, DateTime.UtcNow);
            }
            finally
            {
                try { Directory.Delete(tempFolder, true); } catch { }
                try { File.Delete(stagedArchive); } catch { }
            }
        }

        /// <summary>
        /// Restores a backup into an offline RecordBase folder/name target.<br/>
        /// Requires a complete snapshot and an offline destination with no other process using its files.<br/>
        /// Integrity is verified before ownership changes; originals are retained through final reopen validation and restored on handled failure/cancellation.<br/>
        /// This does not promise multi-file crash/power-loss atomicity or recover missing routing from payload bytes.<br/>
        /// </summary>
        /// <param name="sourceDirectory">Complete snapshot directory containing the exact authoritative members and integrity manifest.<br/></param>
        /// <param name="dataFilesLocation">Target data folder that will receive restored files.<br/></param>
        /// <param name="name">Exact RecordBase basename (for example, <c>WHERZIT</c>), not a filename prefix match.<br/></param>
        /// <param name="config">Optional configuration for staged and installed reopen validation.<br/></param>
        /// <param name="options">Restore behavior options. Null uses defaults.<br/></param>
        /// <returns>Metadata describing the restore operation.<br/></returns>
        public static RestoreBackupResult RestoreBackup(string sourceDirectory, string dataFilesLocation, string name, RecordBaseConfiguration? config = null, RestoreBackupOptions? options = null)
            => RestoreBackupAsync(sourceDirectory, dataFilesLocation, name, config, options, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Restores backup files into this RecordBase's data folder/name.<br/>
        /// The current instance must be disposed before calling this API so destination files are offline.<br/>
        /// </summary>
        /// <param name="sourceDirectory">Complete snapshot directory for this exact RecordBase name.<br/></param>
        /// <param name="options">Restore behavior options. Null uses defaults.<br/></param>
        /// <returns>Metadata describing the restore operation.<br/></returns>
        public RestoreBackupResult RestoreBackup(string sourceDirectory, RestoreBackupOptions? options = null)
        {
            if (Volatile.Read(ref _disposed) == 0)
                throw new InvalidOperationException("RestoreBackup requires an offline RecordBase instance. Dispose the current instance before restoring.");

            return RestoreBackup(sourceDirectory, dataFilesLocation.FullName, name, config, options);
        }

        /// <summary>
        /// Restores a backup into an offline RecordBase folder/name target.<br/>
        /// Requires a complete snapshot and an offline destination; handled failures/cancellation roll back the retained original set.<br/>
        /// Staging and copy I/O currently execute synchronously on the calling thread; this Task-returning API does not offload work.<br/>
        /// No multi-file crash/power-loss atomicity is promised.<br/>
        /// </summary>
        /// <param name="sourceDirectory">Complete snapshot directory containing the exact authoritative members and manifest.<br/></param>
        /// <param name="dataFilesLocation">Target data folder that will receive restored files.<br/></param>
        /// <param name="name">Exact RecordBase basename (for example, <c>WHERZIT</c>).<br/></param>
        /// <param name="config">Optional configuration for staged and installed reopen validation.<br/></param>
        /// <param name="options">Restore behavior options. Null uses defaults.<br/></param>
        /// <param name="ct">Cancellation observed between copy blocks, validation entries and publication phases; accepted cancellation rolls back installation.<br/></param>
        /// <returns>Metadata describing the restore operation.<br/></returns>
        public static Task<RestoreBackupResult> RestoreBackupAsync(
            string sourceDirectory,
            string dataFilesLocation,
            string name,
            RecordBaseConfiguration? config = null,
            RestoreBackupOptions? options = null,
            CancellationToken ct = default)
        {
            try
            {
                return Task.FromResult(RestoreCompleteBackup(sourceDirectory, dataFilesLocation, name,
                    config ?? RecordBaseConfiguration.Default, options ?? new RestoreBackupOptions(), ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<RestoreBackupResult>(ct);
            }
            catch (Exception exception)
            {
                return Task.FromException<RestoreBackupResult>(exception);
            }
        }

        /// <summary>
        /// Restores a backup archive (zip container) into an offline RecordBase folder/name target.<br/>
        /// The archive is extracted to a temporary folder and then restored using <see cref="RestoreBackup(string, string, string, RecordBaseConfiguration, RestoreBackupOptions)"/> semantics.<br/>
        /// </summary>
        /// <param name="sourceArchivePath">Path to the backup archive file.<br/></param>
        /// <param name="dataFilesLocation">Target data folder that will receive restored files.<br/></param>
        /// <param name="name">Exact RecordBase basename (for example, <c>WHERZIT</c>).<br/></param>
        /// <param name="config">Optional configuration for staged and installed reopen validation.<br/></param>
        /// <param name="options">Restore behavior options. Null uses defaults.<br/></param>
        /// <returns>Metadata describing the restore operation.<br/></returns>
        public static RestoreBackupResult RestoreBackupArchive(string sourceArchivePath, string dataFilesLocation, string name, RecordBaseConfiguration? config = null, RestoreBackupOptions? options = null)
            => RestoreBackupArchiveAsync(sourceArchivePath, dataFilesLocation, name, config, options, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Restores a backup archive into this RecordBase's data folder/name.<br/>
        /// The current instance must be disposed before calling this API so destination files are offline.<br/>
        /// </summary>
        /// <param name="sourceArchivePath">Path to the backup archive file.<br/></param>
        /// <param name="options">Restore behavior options. Null uses defaults.<br/></param>
        /// <returns>Metadata describing the restore operation.<br/></returns>
        public RestoreBackupResult RestoreBackupArchive(string sourceArchivePath, RestoreBackupOptions? options = null)
        {
            if (Volatile.Read(ref _disposed) == 0)
                throw new InvalidOperationException("RestoreBackupArchive requires an offline RecordBase instance. Dispose the current instance before restoring.");

            return RestoreBackupArchive(sourceArchivePath, dataFilesLocation.FullName, name, config, options);
        }

        /// <summary>
        /// Asynchronously restores a backup archive (zip container) into an offline RecordBase folder/name target.<br/>
        /// </summary>
        /// <param name="sourceArchivePath">Path to the backup archive file.<br/></param>
        /// <param name="dataFilesLocation">Target data folder that will receive restored files.<br/></param>
        /// <param name="name">Exact RecordBase basename (for example, <c>WHERZIT</c>).<br/></param>
        /// <param name="config">Optional configuration for staged and installed reopen validation.<br/></param>
        /// <param name="options">Restore behavior options. Null uses defaults.<br/></param>
        /// <param name="ct">Cancellation token for archive extraction and restore.<br/></param>
        /// <returns>Metadata describing the restore operation.<br/></returns>
        public static async Task<RestoreBackupResult> RestoreBackupArchiveAsync(
            string sourceArchivePath,
            string dataFilesLocation,
            string name,
            RecordBaseConfiguration? config = null,
            RestoreBackupOptions? options = null,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceArchivePath);
            ValidateRecordBaseName(name);
            ct.ThrowIfCancellationRequested();
            var fullArchive = Path.GetFullPath(sourceArchivePath);
            if (!File.Exists(fullArchive))
                throw new FileNotFoundException($"Backup archive '{fullArchive}' was not found.", fullArchive);
            RejectArchiveMemberCollision(fullArchive, dataFilesLocation, name);
            RejectBackupMemberAlias(fullArchive);

            var tempExtractFolder = Path.Combine(Path.GetTempPath(), $"abraxas_restore_{name}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtractFolder);

            try
            {
                ExtractCompleteBackupArchive(fullArchive, tempExtractFolder, name, ct);
                return await RestoreBackupAsync(tempExtractFolder, dataFilesLocation, name, config, options, ct).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(tempExtractFolder, true); } catch { }
            }
        }

        /// <summary>
        /// Returns a lightweight health snapshot for developer diagnostics and operations visibility.<br/>
        /// </summary>
        /// <returns>Current RecordBase health data.<br/></returns>
        public HealthSnapshot GetHealthSnapshot()
        {
            var storeInfos = GetStoreInfos();
            var stats = data.GetOnlineDefragStats();
            var fractDataBytes = data.GetFileSize(FractalStore.FractalFileSizeDetailFlags.DataFile);
            var fractIndexBytes = data.GetFileSize(FractalStore.FractalFileSizeDetailFlags.IndexFile);
            var libraDexCatalogBytes = GetLibraDexCatalogFileSize();
            var totalBytes = fractDataBytes + fractIndexBytes + libraDexCatalogBytes;

            return new HealthSnapshot(
                UtcTimestamp: DateTime.UtcNow,
                TotalRecordCount: RecordCount(),
                StoreCount: storeInfos?.Count ?? 0,
                FractalDataBytes: fractDataBytes,
                FractalIndexBytes: fractIndexBytes,
                LibraDexCatalogBytes: libraDexCatalogBytes,
                TotalBytes: totalBytes,
                IsOnlineDefragRunning: stats.IsRunning,
                OnlineDefragState: stats.State.ToString());
        }

        //====== TYPES ======
        /// <summary>
        /// Flags describing which RecordBase-related files to include when computing sizes.<br/>
        /// </summary>
        [Flags]
        public enum FileSizeDetailFlags
        {
            /// <summary>
            /// Includes no file-size components.<br/>
            /// </summary>
            None = 0,
            /// <summary>
            /// Includes the authoritative record-data file.<br/>
            /// </summary>
            FractalData = 1,
            /// <summary>
            /// Includes the primary record-index file.<br/>
            /// </summary>
            FractalIndex = 2,
            /// <summary>
            /// Includes the persistent secondary-index catalog, not volatile in-memory indexes.<br/>
            /// </summary>
            LibraDexCatalog = 4,
            /// <summary>
            /// Includes the authoritative Fractal data/index files and persistent LibraDex catalog.<br/>
            /// </summary>
            Native = FractalData | FractalIndex | LibraDexCatalog,
            /// <summary>
            /// Includes every authoritative RecordBase storage file.<br/>
            /// </summary>
            All = Native
        }

        private sealed class WriteLockoutScope : IDisposable
        {
            private readonly RecordBase owner;
            private int disposed;

            public WriteLockoutScope(RecordBase owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 1)
                    return;

                Interlocked.Decrement(ref owner._writeLockoutCount);
            }
        }

        /// <summary>
        /// Releases one active persistent-LibraDex operation lease without relying on thread affinity.<br/>
        /// </summary>
        private sealed class LibraDexOperationScope : IDisposable
        {
            private readonly RecordBase owner;
            private int disposed;

            /// <summary>
            /// Creates a scope for an operation count already incremented by the owner.<br/>
            /// </summary>
            /// <param name="owner">RecordBase that owns the lifecycle barrier.<br/></param>
            internal LibraDexOperationScope(RecordBase owner)
            {
                this.owner = owner;
            }

            /// <summary>
            /// Releases the active operation and wakes lifecycle maintenance when the final operation drains.<br/>
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                    return;
                lock (owner._libraDexLifecycleGate)
                {
                    owner._activeLibraDexOperations--;
                    if (owner._activeLibraDexOperations == 0)
                        Monitor.PulseAll(owner._libraDexLifecycleGate);
                }
            }
        }

        /// <summary>
        /// Releases lifecycle-exclusive maintenance and admits waiting LibraDex operations.<br/>
        /// </summary>
        private sealed class LibraDexLifecycleExclusiveScope : IDisposable
        {
            private readonly RecordBase owner;
            private int disposed;

            /// <summary>
            /// Creates a scope after the owner has observed quiescence and atomically blocked new admission.<br/>
            /// </summary>
            /// <param name="owner">RecordBase that owns the lifecycle barrier.<br/></param>
            internal LibraDexLifecycleExclusiveScope(RecordBase owner)
            {
                this.owner = owner;
            }

            /// <summary>
            /// Reopens operation admission and wakes all blocked callers.<br/>
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                    return;
                lock (owner._libraDexLifecycleGate)
                {
                    owner._libraDexLifecycleExclusive = false;
                    Monitor.PulseAll(owner._libraDexLifecycleGate);
                }
            }
        }
        /// <summary>
        /// Gets the maximum number of application stores supported by this compiled Abraxas edition in one RecordBase.<br/>
        /// Store identity zero is reserved for internal metadata and is not included in this limit.<br/>
        /// This is a structural capacity, not the number of stores currently open or remaining; reading it does not open files or allocate an identity.<br/>
        /// </summary>
        public static int MaximumStoreCount => RowIDGenerator.MaximumStoreID;

        /// <summary>
        /// Gets the largest per-store record-identity counter supported by this compiled Abraxas edition.<br/>
        /// This is the counter portion of a record identity, not a complete store-aware RecordID or a live-record quota.<br/>
        /// Counters advance monotonically: block reservation, unused reserved identities and reopening can consume capacity, and deleting records does not recover it.<br/>
        /// Reading this structural limit does not open files, allocate an identity or estimate remaining capacity.<br/>
        /// </summary>
        public static ulong MaximumRecordCounter => RowIDGenerator.MaximumRecordID;
    }

    /// <summary>
    /// Supplies the minimum RecordStore lifecycle contract required to replace and reopen a RecordBase-owned persistent LibraDex catalog.<br/>
    /// Implementations preserve volatile memory indexes while detaching and rebinding only file-backed handles.<br/>
    /// </summary>
    internal interface ILibraDexRebindableRecordStore
    {
        /// <summary>
        /// Gets the persistent LibraDex identity-group name owned by this store.<br/>
        /// </summary>
        string LibraDexStoreName { get; }

        /// <summary>
        /// Gets the durable store-generation identity captured when this facade was opened.<br/>
        /// </summary>
        Guid StoreInstanceID { get; }

        /// <summary>
        /// Invalidates this facade and releases its process-local volatile index state after coordinated removal.<br/>
        /// </summary>
        /// <param name="state">The unavailable lifecycle state that ended the facade generation.<br/></param>
        void InvalidateStore(StoreLifecycleState state);

        /// <summary>
        /// Detaches every file-backed LibraDex handle while retaining sufficient runtime descriptor state for exact rebinding.<br/>
        /// </summary>
        void DetachPersistentLibraDexBinding();

        /// <summary>
        /// Rebinds every persistent handle to a newly opened catalog identity group.<br/>
        /// </summary>
        /// <param name="indexes">The reopened persistent identity group.<br/></param>
        void RebindPersistentLibraDexBinding(CatalogIdentityGroupIndexes indexes);
    }

    /// <summary>
    /// Stores the versioned durable partial-index definitions owned by one RecordBase.<br/>
    /// Entries are keyed by store identity plus logical index name so different record stores and multiple predicates over the same key path remain independent.<br/>
    /// </summary>
    internal sealed class PartialIndexCatalogRecord
    {
        /// <summary>
        /// Gets or sets the catalog schema version written to reserved Fractal system record 6.<br/>
        /// </summary>
        public int FormatVersion { get; set; } = 1;

        /// <summary>
        /// Gets or sets durable partial-index entries keyed by their stable store/logical-name identity.<br/>
        /// </summary>
        public Dictionary<string, PartialIndexCatalogEntry> Entries { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Validates the catalog version, dictionary identity, and every contained entry before use or persistence.<br/>
        /// </summary>
        internal void Validate()
        {
            if (FormatVersion != 1)
                throw new InvalidDataException($"Partial-index catalog version '{FormatVersion}' is unsupported.");
            Entries ??= new Dictionary<string, PartialIndexCatalogEntry>(StringComparer.Ordinal);
            foreach (var pair in Entries)
            {
                if (pair.Value is null)
                    throw new InvalidDataException($"Partial-index catalog entry '{pair.Key}' is null.");
                pair.Value.Validate();
                if (!string.Equals(pair.Key, pair.Value.CatalogKey, StringComparison.Ordinal))
                    throw new InvalidDataException($"Partial-index catalog key '{pair.Key}' contradicts its entry identity '{pair.Value.CatalogKey}'.");
            }
        }
    }

    /// <summary>
    /// Describes one persistent partial index without exposing its companion engine handle.<br/>
    /// The serialized condition is authoritative for membership; the source USN proves which Fractal state the physical index fully represents.<br/>
    /// </summary>
    internal sealed class PartialIndexCatalogEntry
    {
        public byte StoreID { get; set; }
        public string LogicalName { get; set; } = string.Empty;
        public string PhysicalName { get; set; } = string.Empty;
        public string KeyPath { get; set; } = string.Empty;
        public string KeyTypeCode { get; set; } = string.Empty;
        public bool Unique { get; set; }
        public int TextProfile { get; set; } = -1;
        public int Direction { get; set; }
        public byte[] ConditionPackage { get; set; } = Array.Empty<byte>();
        public string ConditionFingerprint { get; set; } = string.Empty;
        public ulong AppliedSourceUsn { get; set; }
        public bool Ready { get; set; }

        /// <summary>
        /// Gets the deterministic dictionary key that separates record stores and logical index identities.<br/>
        /// </summary>
        internal string CatalogKey => BuildCatalogKey(StoreID, LogicalName);

        /// <summary>
        /// Creates the durable dictionary identity for one store-owned logical partial index.<br/>
        /// </summary>
        /// <param name="storeID">Owning Fractal group key.<br/></param>
        /// <param name="logicalName">Stable Abraxas-facing index name.<br/></param>
        /// <returns>A delimiter-safe catalog key.<br/></returns>
        internal static string BuildCatalogKey(byte storeID, string logicalName)
            => storeID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + logicalName;

        /// <summary>
        /// Creates a deep caller-owned copy so catalog lock scope never leaks mutable condition bytes.<br/>
        /// </summary>
        /// <returns>An independent entry copy.<br/></returns>
        internal PartialIndexCatalogEntry Clone()
            => new()
            {
                StoreID = StoreID,
                LogicalName = LogicalName,
                PhysicalName = PhysicalName,
                KeyPath = KeyPath,
                KeyTypeCode = KeyTypeCode,
                Unique = Unique,
                TextProfile = TextProfile,
                Direction = Direction,
                ConditionPackage = ConditionPackage.ToArray(),
                ConditionFingerprint = ConditionFingerprint,
                AppliedSourceUsn = AppliedSourceUsn,
                Ready = Ready
            };

        /// <summary>
        /// Rejects incomplete or contradictory persisted metadata before any physical index is opened or exposed to planning.<br/>
        /// </summary>
        internal void Validate()
        {
            if (StoreID == 0)
                throw new InvalidDataException("A partial index cannot belong to reserved system store 0.");
            if (string.IsNullOrWhiteSpace(LogicalName) ||
                string.IsNullOrWhiteSpace(PhysicalName) ||
                string.IsNullOrWhiteSpace(KeyPath) ||
                string.IsNullOrWhiteSpace(KeyTypeCode) ||
                string.IsNullOrWhiteSpace(ConditionFingerprint))
            {
                throw new InvalidDataException("Partial-index metadata is missing a required identity, key, type, or condition field.");
            }
            if (!PhysicalName.StartsWith("__abx_partial_", StringComparison.Ordinal))
                throw new InvalidDataException($"Partial-index physical name '{PhysicalName}' is outside the reserved Abraxas namespace.");
            if (ConditionPackage is null || ConditionPackage.Length == 0)
                throw new InvalidDataException($"Partial index '{LogicalName}' has no serialized membership condition.");
            if (TextProfile < -1 || TextProfile > (int)TextIndexProfile.All)
                throw new InvalidDataException($"Partial index '{LogicalName}' has unknown text profile '{TextProfile}'.");
            if ((uint)Direction > (uint)IndexDirection.Bidirectional)
                throw new InvalidDataException($"Partial index '{LogicalName}' has unknown direction '{Direction}'.");
        }
    }

    /// <summary>
    /// Encodes the partial-index catalog as a deterministic primitive payload inside its outer Inheto binary envelope.<br/>
    /// This keeps durable metadata independent of CLR member-discovery behavior and makes every field explicit and versioned.<br/>
    /// </summary>
    internal static class PartialIndexCatalogCodec
    {
        private const uint Magic = 0x58444950;
        private const int FormatVersion = 1;

        /// <summary>
        /// Writes every catalog field in deterministic catalog-key order.<br/>
        /// </summary>
        /// <param name="catalog">Validated catalog snapshot to encode.<br/></param>
        /// <returns>Caller-owned primitive payload bytes.<br/></returns>
        internal static byte[] Encode(PartialIndexCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(catalog.Entries.Count);
            foreach (var pair in catalog.Entries.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = pair.Value;
                writer.Write(entry.StoreID);
                writer.Write(entry.LogicalName);
                writer.Write(entry.PhysicalName);
                writer.Write(entry.KeyPath);
                writer.Write(entry.KeyTypeCode);
                writer.Write(entry.Unique);
                writer.Write(entry.TextProfile);
                writer.Write(entry.Direction);
                writer.Write(entry.ConditionPackage.Length);
                writer.Write(entry.ConditionPackage);
                writer.Write(entry.ConditionFingerprint);
                writer.Write(entry.AppliedSourceUsn);
                writer.Write(entry.Ready);
            }
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes and validates one bounded primitive catalog payload without constructing runtime index handles.<br/>
        /// Trailing bytes, invalid counts, incompatible versions, and contradictory entry identities fail closed.<br/>
        /// </summary>
        /// <param name="payload">Primitive payload restored from the outer Inheto envelope.<br/></param>
        /// <returns>A validated mutable catalog snapshot.<br/></returns>
        internal static PartialIndexCatalogRecord Decode(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Partial-index catalog magic header is invalid.");
            var version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Partial-index catalog version '{version}' is unsupported.");
            var count = reader.ReadInt32();
            if (count < 0 || count > 1_000_000)
                throw new InvalidDataException($"Partial-index catalog entry count '{count}' is invalid.");

            var catalog = new PartialIndexCatalogRecord();
            for (var i = 0; i < count; i++)
            {
                var entry = new PartialIndexCatalogEntry
                {
                    StoreID = reader.ReadByte(),
                    LogicalName = reader.ReadString(),
                    PhysicalName = reader.ReadString(),
                    KeyPath = reader.ReadString(),
                    KeyTypeCode = reader.ReadString(),
                    Unique = reader.ReadBoolean(),
                    TextProfile = reader.ReadInt32(),
                    Direction = reader.ReadInt32()
                };
                var packageLength = reader.ReadInt32();
                if (packageLength <= 0 || packageLength > 64 * 1024 * 1024)
                    throw new InvalidDataException($"Partial-index condition package length '{packageLength}' is invalid.");
                entry.ConditionPackage = reader.ReadBytes(packageLength);
                if (entry.ConditionPackage.Length != packageLength)
                    throw new EndOfStreamException("Partial-index condition package ended before its declared length.");
                entry.ConditionFingerprint = reader.ReadString();
                entry.AppliedSourceUsn = reader.ReadUInt64();
                entry.Ready = reader.ReadBoolean();
                entry.Validate();
                if (!catalog.Entries.TryAdd(entry.CatalogKey, entry))
                    throw new InvalidDataException($"Partial-index catalog contains duplicate key '{entry.CatalogKey}'.");
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException("Partial-index catalog contains unexpected trailing bytes.");
            catalog.Validate();
            return catalog;
        }
    }

    /// <summary>
    /// Stores durable Abraxas removal intent for ordinary persistent indexes.<br/>
    /// Physical LibraDex absence is storage reality; this catalog remains the authoritative reason a matching physical definition must not reattach.<br/>
    /// </summary>
    internal sealed class OrdinaryIndexRemovalCatalogRecord
    {
        public int FormatVersion { get; set; } = 1;
        public Dictionary<string, OrdinaryIndexRemovalEntry> Entries { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Validates the format and every stable store/logical-name dictionary identity.<br/>
        /// </summary>
        internal void Validate()
        {
            if (FormatVersion != 1)
                throw new InvalidDataException($"Ordinary-index removal catalog version '{FormatVersion}' is unsupported.");
            Entries ??= new Dictionary<string, OrdinaryIndexRemovalEntry>(StringComparer.Ordinal);
            foreach (var pair in Entries)
            {
                if (pair.Value is null)
                    throw new InvalidDataException($"Ordinary-index removal entry '{pair.Key}' is null.");
                pair.Value.Validate();
                if (!string.Equals(pair.Key, pair.Value.CatalogKey, StringComparison.Ordinal))
                    throw new InvalidDataException($"Ordinary-index removal key '{pair.Key}' contradicts its entry identity.");
            }
        }
    }

    /// <summary>
    /// Describes one durably removed ordinary persistent index without retaining a storage-engine handle.<br/>
    /// </summary>
    internal sealed class OrdinaryIndexRemovalEntry
    {
        public byte StoreID { get; set; }
        public string LogicalName { get; set; } = string.Empty;
        public string PhysicalName { get; set; } = string.Empty;

        internal string CatalogKey => BuildCatalogKey(StoreID, LogicalName);

        /// <summary>
        /// Creates the delimiter-safe durable identity for one removed ordinary index.<br/>
        /// </summary>
        internal static string BuildCatalogKey(byte storeID, string logicalName)
            => storeID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + logicalName;

        /// <summary>
        /// Creates an independent immutable-by-convention copy for catalog persistence and readback validation.<br/>
        /// </summary>
        internal OrdinaryIndexRemovalEntry Clone()
            => new() { StoreID = StoreID, LogicalName = LogicalName, PhysicalName = PhysicalName };

        /// <summary>
        /// Rejects reserved-store or incomplete identities before removal intent can suppress a catalog definition.<br/>
        /// </summary>
        internal void Validate()
        {
            if (StoreID == 0)
                throw new InvalidDataException("An ordinary-index removal cannot belong to reserved system store 0.");
            if (string.IsNullOrWhiteSpace(LogicalName) || string.IsNullOrWhiteSpace(PhysicalName))
                throw new InvalidDataException("Ordinary-index removal metadata requires logical and physical names.");
        }
    }

    /// <summary>
    /// Encodes ordinary-index removal intent as a deterministic bounded primitive payload.<br/>
    /// </summary>
    internal static class OrdinaryIndexRemovalCatalogCodec
    {
        private const uint Magic = 0x5452494F;
        private const int FormatVersion = 1;

        /// <summary>
        /// Writes every tombstone in deterministic catalog-key order.<br/>
        /// </summary>
        internal static byte[] Encode(OrdinaryIndexRemovalCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(catalog.Entries.Count);
            foreach (var pair in catalog.Entries.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.Write(pair.Value.StoreID);
                writer.Write(pair.Value.LogicalName);
                writer.Write(pair.Value.PhysicalName);
            }
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes and validates one bounded tombstone payload, rejecting duplicates and trailing bytes.<br/>
        /// </summary>
        internal static OrdinaryIndexRemovalCatalogRecord Decode(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Ordinary-index removal catalog magic header is invalid.");
            var version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Ordinary-index removal catalog version '{version}' is unsupported.");
            var count = reader.ReadInt32();
            if (count < 0 || count > 1_000_000)
                throw new InvalidDataException($"Ordinary-index removal catalog entry count '{count}' is invalid.");

            var catalog = new OrdinaryIndexRemovalCatalogRecord();
            for (var i = 0; i < count; i++)
            {
                var entry = new OrdinaryIndexRemovalEntry
                {
                    StoreID = reader.ReadByte(),
                    LogicalName = reader.ReadString(),
                    PhysicalName = reader.ReadString()
                };
                entry.Validate();
                if (!catalog.Entries.TryAdd(entry.CatalogKey, entry))
                    throw new InvalidDataException($"Ordinary-index removal catalog contains duplicate key '{entry.CatalogKey}'.");
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Ordinary-index removal catalog contains unexpected trailing bytes.");
            catalog.Validate();
            return catalog;
        }
    }

    /// <summary>
    /// Distinguishes full ordinary indexes from condition-owned partial indexes in the durable application-codec catalog.<br/>
    /// This prevents a generated partial logical name from colliding with an ordinary property-path identity.<br/>
    /// </summary>
    internal enum ApplicationCodecIndexKind : byte
    {
        Ordinary = 1,        Partial = 2
    }

    /// <summary>
    /// Stores every application-codec binding owned by one RecordBase.<br/>
    /// Format strings are held on entries for simple comparison but are deduplicated into a compact ordinal table by the binary codec.<br/>
    /// </summary>
    internal sealed class ApplicationCodecCatalogRecord
    {
        public int FormatVersion { get; set; } = 1;
        public Dictionary<string, ApplicationCodecIndexBindingEntry> Bindings { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Validates the catalog version and every dictionary-to-entry identity relationship.<br/>
        /// </summary>
        internal void Validate()
        {
            if (FormatVersion != 1)
                throw new InvalidDataException($"Application-codec catalog version '{FormatVersion}' is unsupported.");
            Bindings ??= new Dictionary<string, ApplicationCodecIndexBindingEntry>(StringComparer.Ordinal);
            foreach (var pair in Bindings)
            {
                if (pair.Value is null)
                    throw new InvalidDataException($"Application-codec binding '{pair.Key}' is null.");
                pair.Value.Validate();
                if (!string.Equals(pair.Key, pair.Value.CatalogKey, StringComparison.Ordinal))
                    throw new InvalidDataException($"Application-codec binding key '{pair.Key}' contradicts its entry identity.");
            }
        }
    }

    /// <summary>
    /// Describes the stable Abraxas contract connecting one runtime Inheto handler to one derived persistent LibraDex index.<br/>
    /// Executable delegates are deliberately absent; applications supply them again during typed store setup on every process open.<br/>
    /// </summary>
    internal sealed class ApplicationCodecIndexBindingEntry
    {
        internal const int MaximumFormatIdUtf8Bytes = 128;

        public byte StoreID { get; set; }
        public ApplicationCodecIndexKind Kind { get; set; }
        public string LogicalName { get; set; } = string.Empty;
        public string PhysicalName { get; set; } = string.Empty;
        public string KeyPath { get; set; } = string.Empty;
        public string KeyTypeCode { get; set; } = string.Empty;
        public string FormatId { get; set; } = string.Empty;
        public int FormatVersion { get; set; }
        public int FixedKeyWidth { get; set; }
        public bool OrderPreserving { get; set; }
        public bool Ready { get; set; }

        internal string CatalogKey => BuildCatalogKey(StoreID, Kind, LogicalName);

        /// <summary>
        /// Creates the delimiter-safe binding identity for one store-owned ordinary or partial index.<br/>
        /// </summary>
        internal static string BuildCatalogKey(
            byte storeID,
            ApplicationCodecIndexKind kind,
            string logicalName)
            => storeID.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ":" + ((byte)kind).ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ":" + logicalName;

        /// <summary>
        /// Produces an independent binding copy for lock-free setup comparison and catalog replacement.<br/>
        /// </summary>
        internal ApplicationCodecIndexBindingEntry Clone()
            => new()
            {
                StoreID = StoreID,
                Kind = Kind,
                LogicalName = LogicalName,
                PhysicalName = PhysicalName,
                KeyPath = KeyPath,
                KeyTypeCode = KeyTypeCode,
                FormatId = FormatId,
                FormatVersion = FormatVersion,
                FixedKeyWidth = FixedKeyWidth,
                OrderPreserving = OrderPreserving,
                Ready = Ready
            };

        /// <summary>
        /// Compares every persisted semantic and physical-shape field, optionally including publication readiness.<br/>
        /// </summary>
        internal bool ContractEquals(
            ApplicationCodecIndexBindingEntry other,
            bool includeReady)
        {
            ArgumentNullException.ThrowIfNull(other);
            return StoreID == other.StoreID &&
                   Kind == other.Kind &&
                   string.Equals(LogicalName, other.LogicalName, StringComparison.Ordinal) &&
                   string.Equals(PhysicalName, other.PhysicalName, StringComparison.Ordinal) &&
                   string.Equals(KeyPath, other.KeyPath, StringComparison.Ordinal) &&
                   string.Equals(KeyTypeCode, other.KeyTypeCode, StringComparison.Ordinal) &&
                   string.Equals(FormatId, other.FormatId, StringComparison.Ordinal) &&
                   FormatVersion == other.FormatVersion &&
                   FixedKeyWidth == other.FixedKeyWidth &&
                   OrderPreserving == other.OrderPreserving &&
                   (!includeReady || Ready == other.Ready);
        }

        /// <summary>
        /// Rejects incomplete, oversized, reserved-store, or contradictory codec-binding metadata before it can authorize attachment.<br/>
        /// </summary>
        internal void Validate()
        {
            if (StoreID == 0)
                throw new InvalidDataException("An application-codec binding cannot belong to reserved system store 0.");
            if (Kind is not (ApplicationCodecIndexKind.Ordinary or ApplicationCodecIndexKind.Partial))
                throw new InvalidDataException($"Application-codec binding kind '{Kind}' is unsupported.");
            if (string.IsNullOrWhiteSpace(LogicalName) ||
                string.IsNullOrWhiteSpace(PhysicalName) ||
                string.IsNullOrWhiteSpace(KeyPath) ||
                string.IsNullOrWhiteSpace(KeyTypeCode) ||
                string.IsNullOrWhiteSpace(FormatId))
            {
                throw new InvalidDataException("Application-codec binding metadata is missing an index, path, type, or format identity.");
            }
            if (!string.Equals(FormatId, FormatId.Trim(), StringComparison.Ordinal))
                throw new InvalidDataException("Application codec format IDs cannot contain leading or trailing whitespace.");
            if (System.Text.Encoding.UTF8.GetByteCount(FormatId) > MaximumFormatIdUtf8Bytes)
            {
                throw new InvalidDataException(
                    $"Application codec format ID '{FormatId}' exceeds {MaximumFormatIdUtf8Bytes} UTF-8 bytes.");
            }
            if (FormatVersion <= 0)
                throw new InvalidDataException("Application codec format versions must be positive integers.");
            if (FixedKeyWidth is not (8 or 16 or 32))
                throw new InvalidDataException($"Application codec fixed key width '{FixedKeyWidth}' is unsupported.");
        }
    }

    /// <summary>
    /// Encodes application-codec intent with one deduplicated format string/version table and compact per-binding ordinals.<br/>
    /// The deterministic primitive payload remains independent of CLR member discovery and never contains executable code.<br/>
    /// </summary>
    internal static class ApplicationCodecCatalogCodec
    {
        private const uint Magic = 0x43425841;
        private const int FormatVersion = 1;
        private const int MaximumEntryCount = 1_000_000;

        /// <summary>
        /// Writes unique format identities once, then writes bindings in deterministic catalog-key order using 32-bit table ordinals.<br/>
        /// </summary>
        internal static byte[] Encode(ApplicationCodecCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();

            var formats = catalog.Bindings.Values
                .Select(static entry => new ApplicationCodecFormatKey(entry.FormatId, entry.FormatVersion))
                .Distinct()
                .OrderBy(static format => format.FormatId, StringComparer.Ordinal)
                .ThenBy(static format => format.FormatVersion)
                .ToArray();
            var ordinals = new Dictionary<ApplicationCodecFormatKey, int>();
            for (var i = 0; i < formats.Length; i++)
                ordinals.Add(formats[i], i);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(formats.Length);
            for (var i = 0; i < formats.Length; i++)
            {
                writer.Write(formats[i].FormatId);
                writer.Write(formats[i].FormatVersion);
            }

            writer.Write(catalog.Bindings.Count);
            foreach (var pair in catalog.Bindings.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = pair.Value;
                writer.Write(entry.StoreID);
                writer.Write((byte)entry.Kind);
                writer.Write(entry.LogicalName);
                writer.Write(entry.PhysicalName);
                writer.Write(entry.KeyPath);
                writer.Write(entry.KeyTypeCode);
                writer.Write(ordinals[new ApplicationCodecFormatKey(entry.FormatId, entry.FormatVersion)]);
                writer.Write(entry.FixedKeyWidth);
                writer.Write(entry.OrderPreserving);
                writer.Write(entry.Ready);
            }
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes and validates bounded format and binding tables, rejecting invalid ordinals, duplicates, and trailing bytes.<br/>
        /// </summary>
        internal static ApplicationCodecCatalogRecord Decode(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Application-codec catalog magic header is invalid.");
            var version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Application-codec catalog version '{version}' is unsupported.");

            var formatCount = reader.ReadInt32();
            if (formatCount < 0 || formatCount > MaximumEntryCount)
                throw new InvalidDataException($"Application-codec format count '{formatCount}' is invalid.");
            var formats = new ApplicationCodecFormatKey[formatCount];
            var uniqueFormats = new HashSet<ApplicationCodecFormatKey>();
            for (var i = 0; i < formatCount; i++)
            {
                var format = new ApplicationCodecFormatKey(reader.ReadString(), reader.ReadInt32());
                ValidateFormat(format);
                if (!uniqueFormats.Add(format))
                    throw new InvalidDataException($"Application-codec catalog contains duplicate format '{format.FormatId}' version {format.FormatVersion}.");
                formats[i] = format;
            }

            var bindingCount = reader.ReadInt32();
            if (bindingCount < 0 || bindingCount > MaximumEntryCount)
                throw new InvalidDataException($"Application-codec binding count '{bindingCount}' is invalid.");
            var catalog = new ApplicationCodecCatalogRecord();
            for (var i = 0; i < bindingCount; i++)
            {
                var storeID = reader.ReadByte();
                var kind = (ApplicationCodecIndexKind)reader.ReadByte();
                var logicalName = reader.ReadString();
                var physicalName = reader.ReadString();
                var keyPath = reader.ReadString();
                var keyTypeCode = reader.ReadString();
                var formatOrdinal = reader.ReadInt32();
                if ((uint)formatOrdinal >= (uint)formats.Length)
                    throw new InvalidDataException($"Application-codec binding format ordinal '{formatOrdinal}' is invalid.");
                var format = formats[formatOrdinal];
                var entry = new ApplicationCodecIndexBindingEntry
                {
                    StoreID = storeID,
                    Kind = kind,
                    LogicalName = logicalName,
                    PhysicalName = physicalName,
                    KeyPath = keyPath,
                    KeyTypeCode = keyTypeCode,
                    FormatId = format.FormatId,
                    FormatVersion = format.FormatVersion,
                    FixedKeyWidth = reader.ReadInt32(),
                    OrderPreserving = reader.ReadBoolean(),
                    Ready = reader.ReadBoolean()
                };
                entry.Validate();
                if (!catalog.Bindings.TryAdd(entry.CatalogKey, entry))
                    throw new InvalidDataException($"Application-codec catalog contains duplicate binding '{entry.CatalogKey}'.");
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException("Application-codec catalog contains unexpected trailing bytes.");
            catalog.Validate();
            return catalog;
        }

        private static void ValidateFormat(ApplicationCodecFormatKey format)
        {
            if (string.IsNullOrWhiteSpace(format.FormatId) ||
                !string.Equals(format.FormatId, format.FormatId.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Application codec format IDs cannot be empty or contain outer whitespace.");
            }
            if (System.Text.Encoding.UTF8.GetByteCount(format.FormatId) >
                ApplicationCodecIndexBindingEntry.MaximumFormatIdUtf8Bytes)
            {
                throw new InvalidDataException(
                    $"Application codec format ID '{format.FormatId}' exceeds {ApplicationCodecIndexBindingEntry.MaximumFormatIdUtf8Bytes} UTF-8 bytes.");
            }
            if (format.FormatVersion <= 0)
                throw new InvalidDataException("Application codec format versions must be positive integers.");
        }

        private readonly record struct ApplicationCodecFormatKey(string FormatId, int FormatVersion);
    }

    /// <summary>
    /// Identifies the Abraxas-owned ordinary-index shape that must be recreated independently of physical LibraDex metadata.<br/>
    /// </summary>
    internal enum OrdinaryIndexIntentKind : byte
    {
        Scalar = 1,        Text = 2,        Binary = 3,        Calculated = 4,        FunctionCalculated = 5,        Composite = 6
    }

    /// <summary>
    /// Describes one ordered selector part of a persistent Abraxas composite index.<br/>
    /// The selector descriptor remains backend-neutral while the exact result type, text representation, and direction retain enough developer intent to recreate LibraDex reality.<br/>
    /// </summary>
    internal sealed class OrdinaryCompositeIndexPartIntent
    {
        public byte[] SelectorDescriptor { get; set; } = Array.Empty<byte>();
        public string ResultTypeCode { get; set; } = string.Empty;
        public TextIndexProfile TextProfile { get; set; }
        public IndexDirection Direction { get; set; }

        /// <summary>
        /// Creates an independent immutable-by-convention copy for catalog publication and comparison.<br/>
        /// </summary>
        internal OrdinaryCompositeIndexPartIntent Clone()
            => new()
            {
                SelectorDescriptor = (byte[])SelectorDescriptor.Clone(),
                ResultTypeCode = ResultTypeCode,
                TextProfile = TextProfile,
                Direction = Direction
            };

        /// <summary>
        /// Compares the complete persisted selector-part contract.<br/>
        /// </summary>
        internal bool ContractEquals(OrdinaryCompositeIndexPartIntent other)
            => other is not null &&
                SelectorDescriptor.AsSpan().SequenceEqual(other.SelectorDescriptor) &&
                string.Equals(ResultTypeCode, other.ResultTypeCode, StringComparison.Ordinal) &&
                TextProfile == other.TextProfile &&
                Direction == other.Direction;

        /// <summary>
        /// Rejects incomplete, unsupported, or contradictory composite-part metadata before physical index recovery.<br/>
        /// </summary>
        internal void Validate()
        {
            SelectorDescriptor ??= Array.Empty<byte>();
            ResultTypeCode ??= string.Empty;
            if (SelectorDescriptor.Length == 0 || string.IsNullOrWhiteSpace(ResultTypeCode))
                throw new InvalidDataException("Composite ordinary-index parts require a selector descriptor and exact result type.");
            if (!Enum.IsDefined(Direction))
                throw new InvalidDataException("Composite ordinary-index parts require a supported direction.");

            Type resultType = ConditionWireTypes.Resolve(ResultTypeCode);
            const TextIndexProfile supportedTextProfile = TextIndexProfile.Folded | TextIndexProfile.SortKey | TextIndexProfile.Normalized;
            if (resultType == typeof(string))
            {
                if ((TextProfile & ~supportedTextProfile) != 0)
                    throw new InvalidDataException("Composite text parts contain an unsupported text representation profile.");
            }
            else if (TextProfile != TextIndexProfile.Exact)
            {
                throw new InvalidDataException("Only composite string parts can define text representation profiles.");
            }
        }
    }

    /// <summary>
    /// Identifies the persisted LibraDex blob-key shape owned by one calculated <c>byte[]</c> selector index.<br/>
    /// The associated maximum-key field stores either the variable payload ceiling or the exact fixed width.<br/>
    /// </summary>
    internal enum CalculatedBinaryKeyStorage : byte
    {
        None = 0,
        VariableLength = 1,
        FixedLength = 2
    }

    /// <summary>
    /// Stores every active ordinary persistent index definition owned by one RecordBase.<br/>
    /// This is developer intent rather than physical index reality and therefore retains Abraxas-only nullable uniqueness semantics.<br/>
    /// </summary>
    internal sealed class OrdinaryIndexIntentCatalogRecord
    {
        public int FormatVersion { get; set; } = 7;
        public Dictionary<string, OrdinaryIndexIntentEntry> Entries { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Validates the catalog format and every stable store/logical-name identity.<br/>
        /// </summary>
        internal void Validate()
        {
            if (FormatVersion is not (5 or 6 or 7))
                throw new InvalidDataException($"Ordinary-index intent catalog version '{FormatVersion}' is unsupported.");
            FormatVersion = 7;
            Entries ??= new Dictionary<string, OrdinaryIndexIntentEntry>(StringComparer.Ordinal);
            foreach (var pair in Entries)
            {
                if (pair.Value is null)
                    throw new InvalidDataException($"Ordinary-index intent entry '{pair.Key}' is null.");
                pair.Value.Validate();
                if (!string.Equals(pair.Key, pair.Value.CatalogKey, StringComparison.Ordinal))
                    throw new InvalidDataException($"Ordinary-index intent key '{pair.Key}' contradicts its entry identity.");
            }
        }
    }

    /// <summary>
    /// Describes one active ordinary persistent index with enough Abraxas-owned shape to recreate interrupted LibraDex reality.<br/>
    /// </summary>
    internal sealed class OrdinaryIndexIntentEntry
    {
        public byte StoreID { get; set; }
        public OrdinaryIndexIntentKind Kind { get; set; }
        public string LogicalName { get; set; } = string.Empty;
        public string PhysicalName { get; set; } = string.Empty;
        public bool Unique { get; set; }
        public TextIndexProfile TextProfile { get; set; }
        public IndexDirection Direction { get; set; }
        public int MaximumKeyBytes { get; set; }
        public CalculatedBinaryKeyStorage CalculatedBinaryStorage { get; set; }
        public byte[] SelectorDescriptor { get; set; } = Array.Empty<byte>();
        public string ResultTypeCode { get; set; } = string.Empty;
        public string FunctionId { get; set; } = string.Empty;
        public int FunctionVersion { get; set; }
        public bool IncludeNullValues { get; set; }
        public bool IncludeEmptyStrings { get; set; }
        public TextSortProfile[] TextSortProfiles { get; set; } = Array.Empty<TextSortProfile>();
        public OrdinaryCompositeIndexPartIntent[] CompositeParts { get; set; } = Array.Empty<OrdinaryCompositeIndexPartIntent>();
        public int PopulationRevision { get; set; }
        public bool Ready { get; set; }

        internal string CatalogKey => BuildCatalogKey(StoreID, LogicalName);

        /// <summary>
        /// Creates the delimiter-safe durable identity for one active ordinary index.<br/>
        /// </summary>
        internal static string BuildCatalogKey(byte storeID, string logicalName)
            => storeID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + logicalName;

        /// <summary>
        /// Creates an independent immutable-by-convention copy for catalog persistence and setup-time comparison.<br/>
        /// </summary>
        internal OrdinaryIndexIntentEntry Clone()
            => new()
            {
                StoreID = StoreID,
                Kind = Kind,
                LogicalName = LogicalName,
                PhysicalName = PhysicalName,
                Unique = Unique,
                TextProfile = TextProfile,
                Direction = Direction,
                MaximumKeyBytes = MaximumKeyBytes,
                CalculatedBinaryStorage = CalculatedBinaryStorage,
                SelectorDescriptor = (byte[])SelectorDescriptor.Clone(),
                ResultTypeCode = ResultTypeCode,
                FunctionId = FunctionId,
                FunctionVersion = FunctionVersion,
                IncludeNullValues = IncludeNullValues,
                IncludeEmptyStrings = IncludeEmptyStrings,
                TextSortProfiles = (TextSortProfile[])TextSortProfiles.Clone(),
                CompositeParts = CompositeParts.Select(static part => part.Clone()).ToArray(),
                PopulationRevision = PopulationRevision,
                Ready = Ready
            };

        /// <summary>
        /// Compares the complete developer-owned index definition, optionally including its recovery publication state.<br/>
        /// </summary>
        /// <param name="other">The definition to compare.<br/></param>
        /// <param name="includeReady">Whether the preparing/ready state participates in equality.<br/></param>
        /// <returns><c>true</c> when both definitions express the same requested contract.<br/></returns>
        internal bool ContractEquals(OrdinaryIndexIntentEntry other, bool includeReady)
        {
            ArgumentNullException.ThrowIfNull(other);
            return StoreID == other.StoreID &&
                Kind == other.Kind &&
                string.Equals(LogicalName, other.LogicalName, StringComparison.Ordinal) &&
                string.Equals(PhysicalName, other.PhysicalName, StringComparison.Ordinal) &&
                Unique == other.Unique &&
                TextProfile == other.TextProfile &&
                Direction == other.Direction &&
                MaximumKeyBytes == other.MaximumKeyBytes &&
                CalculatedBinaryStorage == other.CalculatedBinaryStorage &&
                SelectorDescriptor.AsSpan().SequenceEqual(other.SelectorDescriptor) &&
                string.Equals(ResultTypeCode, other.ResultTypeCode, StringComparison.Ordinal) &&
                string.Equals(FunctionId, other.FunctionId, StringComparison.Ordinal) &&
                FunctionVersion == other.FunctionVersion &&
                IncludeNullValues == other.IncludeNullValues &&
                IncludeEmptyStrings == other.IncludeEmptyStrings &&
                TextSortProfiles.AsSpan().SequenceEqual(other.TextSortProfiles) &&
                CompositeParts.Length == other.CompositeParts.Length &&
                CompositeParts.Zip(other.CompositeParts).All(static pair => pair.First.ContractEquals(pair.Second)) &&
                (!includeReady || Ready == other.Ready);
        }

        /// <summary>
        /// Rejects reserved-store, incomplete, or shape-inconsistent definitions before they can drive physical index recovery.<br/>
        /// </summary>
        internal void Validate()
        {
            if (StoreID == 0)
                throw new InvalidDataException("An ordinary-index intent cannot belong to reserved system store 0.");
            if (Kind is not (OrdinaryIndexIntentKind.Scalar or OrdinaryIndexIntentKind.Text or OrdinaryIndexIntentKind.Binary or OrdinaryIndexIntentKind.Calculated or OrdinaryIndexIntentKind.FunctionCalculated or OrdinaryIndexIntentKind.Composite))
                throw new InvalidDataException($"Ordinary-index intent kind '{Kind}' is unsupported.");
            if (string.IsNullOrWhiteSpace(LogicalName) || string.IsNullOrWhiteSpace(PhysicalName))
                throw new InvalidDataException("Ordinary-index intent requires logical and physical names.");
            SelectorDescriptor ??= Array.Empty<byte>();
            ResultTypeCode ??= string.Empty;
            FunctionId ??= string.Empty;
            CompositeParts ??= Array.Empty<OrdinaryCompositeIndexPartIntent>();
            TextSortProfiles ??= Array.Empty<TextSortProfile>();
            bool isCalculated = Kind is OrdinaryIndexIntentKind.Calculated or OrdinaryIndexIntentKind.FunctionCalculated;
            bool isComposite = Kind == OrdinaryIndexIntentKind.Composite;
            if (Kind != OrdinaryIndexIntentKind.Text && TextSortProfiles.Length != 0)
                throw new InvalidDataException("Only text ordinary-index intent can define culture sort-key profiles.");
            if (Kind == OrdinaryIndexIntentKind.Text)
            {
                HashSet<(string Culture, CompareOptions Options)> uniqueSortProfiles = new();
                for (int i = 0; i < TextSortProfiles.Length; i++)
                {
                    TextSortProfile profile = TextSortProfiles[i];
                    CultureInfo culture = string.IsNullOrEmpty(profile.CultureName)
                        ? CultureInfo.InvariantCulture
                        : CultureInfo.GetCultureInfo(profile.CultureName);
                    _ = culture.CompareInfo.GetSortKey(string.Empty, profile.CompareOptions);
                    string cultureName = culture.Equals(CultureInfo.InvariantCulture) ? string.Empty : culture.Name;
                    TextSortProfiles[i] = new TextSortProfile(cultureName, profile.CompareOptions);
                    if (!uniqueSortProfiles.Add((cultureName, profile.CompareOptions)))
                        throw new InvalidDataException($"Text ordinary-index intent repeats culture sort-key profile '{cultureName}' with options '{profile.CompareOptions}'.");
                }
            }
            if (isComposite)
            {
                if (CompositeParts.Length < 2 || CompositeParts.Length > 16)
                    throw new InvalidDataException("Composite ordinary-index intent requires between 2 and 16 ordered selector parts.");
                if (SelectorDescriptor.Length != 0 || ResultTypeCode.Length != 0 || FunctionId.Length != 0 || FunctionVersion != 0 || IncludeNullValues || IncludeEmptyStrings || MaximumKeyBytes != 0 || CalculatedBinaryStorage != CalculatedBinaryKeyStorage.None || TextProfile != TextIndexProfile.Exact)
                    throw new InvalidDataException("Composite ordinary-index intent must retain selector metadata only in its ordered part definitions.");
                for (var i = 0; i < CompositeParts.Length; i++)
                    CompositeParts[i].Validate();
            }
            if (isCalculated)
            {
                if (string.IsNullOrWhiteSpace(ResultTypeCode))
                    throw new InvalidDataException("Calculated ordinary-index intent requires an exact result type.");
                if (Kind == OrdinaryIndexIntentKind.Calculated && SelectorDescriptor.Length == 0)
                    throw new InvalidDataException("Calculated ordinary-index intent requires a selector descriptor.");
                if (Kind == OrdinaryIndexIntentKind.FunctionCalculated)
                {
                    if (SelectorDescriptor.Length != 0 || string.IsNullOrWhiteSpace(FunctionId) || FunctionVersion <= 0)
                        throw new InvalidDataException("Function-calculated ordinary-index intent requires a stable function projection ID and positive version.");
                    if (System.Text.Encoding.UTF8.GetByteCount(FunctionId) > 256)
                        throw new InvalidDataException("Function projection IDs cannot exceed 256 UTF-8 bytes.");
                }
                else if (FunctionId.Length != 0 || FunctionVersion != 0)
                {
                    throw new InvalidDataException("Only function-calculated ordinary-index intent can define function projection identity.");
                }
                Type resultType = ConditionWireTypes.Resolve(ResultTypeCode);
                if (resultType == typeof(byte[]))
                {
                    if (CalculatedBinaryStorage == CalculatedBinaryKeyStorage.VariableLength && MaximumKeyBytes != 1023)
                        throw new InvalidDataException("Calculated variable-binary ordinary-index intent must use the standard 1,023-byte payload ceiling.");
                    if (CalculatedBinaryStorage == CalculatedBinaryKeyStorage.FixedLength && MaximumKeyBytes is not (8 or 16 or 32))
                        throw new InvalidDataException("Calculated fixed-binary ordinary-index intent must use an 8-, 16-, or 32-byte key width.");
                    if (CalculatedBinaryStorage == CalculatedBinaryKeyStorage.None)
                        throw new InvalidDataException("Calculated binary ordinary-index intent requires an explicit variable or fixed storage shape.");
                }
                else if (CalculatedBinaryStorage != CalculatedBinaryKeyStorage.None || MaximumKeyBytes != 0)
                {
                    throw new InvalidDataException("Only a calculated byte[] result can define a binary storage shape or key bound.");
                }
            }
            else if (!isComposite && (SelectorDescriptor.Length != 0 || ResultTypeCode.Length != 0 || FunctionId.Length != 0 || FunctionVersion != 0 || IncludeNullValues || IncludeEmptyStrings))
            {
                throw new InvalidDataException("Only calculated ordinary-index intent can define selector-result participation metadata.");
            }
            if (Kind == OrdinaryIndexIntentKind.Text)
            {
                const TextIndexProfile supportedTextProfile = TextIndexProfile.Folded | TextIndexProfile.SortKey | TextIndexProfile.Normalized;
                if ((TextProfile & ~supportedTextProfile) != 0 || !Enum.IsDefined(Direction))
                    throw new InvalidDataException("Text ordinary-index intent contains an unsupported profile or direction.");
                if (MaximumKeyBytes != 0)
                    throw new InvalidDataException("Text ordinary-index intent cannot define a binary key bound.");
            }
            else if (Kind == OrdinaryIndexIntentKind.Binary)
            {
                if (MaximumKeyBytes < 1)
                    throw new InvalidDataException("Binary ordinary-index intent requires a positive maximum key length.");
            }
            else if (!isCalculated && !isComposite && MaximumKeyBytes != 0)
            {
                throw new InvalidDataException("Scalar ordinary-index intent cannot define a binary key bound.");
            }
            if (!isCalculated && !isComposite && CalculatedBinaryStorage != CalculatedBinaryKeyStorage.None)
                throw new InvalidDataException("Only calculated ordinary-index intent can define calculated binary storage.");
            if (!isComposite && CompositeParts.Length != 0)
                throw new InvalidDataException("Only composite ordinary-index intent can define ordered selector parts.");
            if (PopulationRevision < 0)
                throw new InvalidDataException("Ordinary-index population revision cannot be negative.");
        }
    }

    /// <summary>
    /// Encodes ordinary-index definitions as one deterministic bounded primitive payload.<br/>
    /// </summary>
    internal static class OrdinaryIndexIntentCatalogCodec
    {
        private const uint Magic = 0x54494F41;
        private const int FormatVersion = 7;
        private const int MaximumEntryCount = 1_000_000;

        /// <summary>
        /// Writes every definition in deterministic catalog-key order without serializing runtime types or engine handles.<br/>
        /// </summary>
        internal static byte[] Encode(OrdinaryIndexIntentCatalogRecord catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(catalog.Entries.Count);
            foreach (var pair in catalog.Entries.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = pair.Value;
                writer.Write(entry.StoreID);
                writer.Write((byte)entry.Kind);
                writer.Write(entry.LogicalName);
                writer.Write(entry.PhysicalName);
                writer.Write(entry.Unique);
                writer.Write((int)entry.TextProfile);
                writer.Write((int)entry.Direction);
                writer.Write(entry.MaximumKeyBytes);
                writer.Write((byte)entry.CalculatedBinaryStorage);
                writer.Write(entry.SelectorDescriptor.Length);
                writer.Write(entry.SelectorDescriptor);
                writer.Write(entry.ResultTypeCode);
                writer.Write(entry.FunctionId);
                writer.Write(entry.FunctionVersion);
                writer.Write(entry.IncludeNullValues);
                writer.Write(entry.IncludeEmptyStrings);
                writer.Write(entry.CompositeParts.Length);
                for (var i = 0; i < entry.CompositeParts.Length; i++)
                {
                    OrdinaryCompositeIndexPartIntent part = entry.CompositeParts[i];
                    writer.Write(part.SelectorDescriptor.Length);
                    writer.Write(part.SelectorDescriptor);
                    writer.Write(part.ResultTypeCode);
                    writer.Write((int)part.TextProfile);
                    writer.Write((int)part.Direction);
                }
                writer.Write(entry.TextSortProfiles.Length);
                for (var i = 0; i < entry.TextSortProfiles.Length; i++)
                {
                    TextSortProfile profile = entry.TextSortProfiles[i];
                    writer.Write(profile.CultureName ?? string.Empty);
                    writer.Write((int)profile.CompareOptions);
                }
                writer.Write(entry.PopulationRevision);
                writer.Write(entry.Ready);
            }
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes and validates one bounded definition payload, rejecting duplicate identities and trailing bytes.<br/>
        /// </summary>
        internal static OrdinaryIndexIntentCatalogRecord Decode(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Ordinary-index intent catalog magic header is invalid.");
            var version = reader.ReadInt32();
            if (version is not (1 or 2 or 3 or 4 or 5 or 6 or FormatVersion))
                throw new InvalidDataException($"Ordinary-index intent catalog version '{version}' is unsupported.");
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumEntryCount)
                throw new InvalidDataException($"Ordinary-index intent catalog entry count '{count}' is invalid.");

            var catalog = new OrdinaryIndexIntentCatalogRecord();
            for (var i = 0; i < count; i++)
            {
                var entry = new OrdinaryIndexIntentEntry
                {
                    StoreID = reader.ReadByte(),
                    Kind = (OrdinaryIndexIntentKind)reader.ReadByte(),
                    LogicalName = reader.ReadString(),
                    PhysicalName = reader.ReadString(),
                    Unique = reader.ReadBoolean(),
                    TextProfile = (TextIndexProfile)reader.ReadInt32(),
                    Direction = (IndexDirection)reader.ReadInt32(),
                    MaximumKeyBytes = reader.ReadInt32(),
                    CalculatedBinaryStorage = version >= 3
                        ? (CalculatedBinaryKeyStorage)reader.ReadByte()
                        : CalculatedBinaryKeyStorage.None
                };
                if (version >= 2)
                {
                    int selectorLength = reader.ReadInt32();
                    if (selectorLength < 0 || selectorLength > 16 * 1024 * 1024)
                        throw new InvalidDataException($"Ordinary-index selector descriptor length '{selectorLength}' is invalid.");
                    entry.SelectorDescriptor = reader.ReadBytes(selectorLength);
                    if (entry.SelectorDescriptor.Length != selectorLength)
                        throw new EndOfStreamException("Ordinary-index selector descriptor ended unexpectedly.");
                    entry.ResultTypeCode = reader.ReadString();
                    if (version >= 4)
                    {
                        entry.FunctionId = reader.ReadString();
                        entry.FunctionVersion = reader.ReadInt32();
                    }
                    entry.IncludeNullValues = reader.ReadBoolean();
                    entry.IncludeEmptyStrings = reader.ReadBoolean();
                }
                if (version >= 5)
                {
                    int partCount = reader.ReadInt32();
                    if (partCount < 0 || partCount > 16)
                        throw new InvalidDataException($"Ordinary-index composite part count '{partCount}' is invalid.");
                    var parts = new OrdinaryCompositeIndexPartIntent[partCount];
                    for (var partIndex = 0; partIndex < partCount; partIndex++)
                    {
                        int selectorLength = reader.ReadInt32();
                        if (selectorLength < 0 || selectorLength > 16 * 1024 * 1024)
                            throw new InvalidDataException($"Ordinary-index composite selector descriptor length '{selectorLength}' is invalid.");
                        byte[] selectorBytes = reader.ReadBytes(selectorLength);
                        if (selectorBytes.Length != selectorLength)
                            throw new EndOfStreamException("Ordinary-index composite selector descriptor ended unexpectedly.");
                        parts[partIndex] = new OrdinaryCompositeIndexPartIntent
                        {
                            SelectorDescriptor = selectorBytes,
                            ResultTypeCode = reader.ReadString(),
                            TextProfile = (TextIndexProfile)reader.ReadInt32(),
                            Direction = (IndexDirection)reader.ReadInt32()
                        };
                    }
                    entry.CompositeParts = parts;
                }
                if (version >= 6)
                {
                    int sortProfileCount = reader.ReadInt32();
                    if (sortProfileCount < 0 || sortProfileCount > 256)
                        throw new InvalidDataException($"Ordinary-index culture sort-key profile count '{sortProfileCount}' is invalid.");
                    var sortProfiles = new TextSortProfile[sortProfileCount];
                    for (var profileIndex = 0; profileIndex < sortProfileCount; profileIndex++)
                    {
                        sortProfiles[profileIndex] = new TextSortProfile(
                            reader.ReadString(),
                            (CompareOptions)reader.ReadInt32());
                    }
                    entry.TextSortProfiles = sortProfiles;
                }
                if (version >= 7)
                    entry.PopulationRevision = reader.ReadInt32();
                entry.Ready = reader.ReadBoolean();
                entry.Validate();
                if (!catalog.Entries.TryAdd(entry.CatalogKey, entry))
                    throw new InvalidDataException($"Ordinary-index intent catalog contains duplicate key '{entry.CatalogKey}'.");
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException("Ordinary-index intent catalog contains unexpected trailing bytes.");
            catalog.Validate();
            return catalog;
        }
    }
}
