using System.Buffers;
using System.Text.Json;
using FractalKVS;
using LibraDex;

namespace AbraxasDB
{
    public partial class RecordBase
    {
        private const string PurgeJournalSuffix = ".abraxas-purge.json";

        /// <summary>
        /// Irreversibly removes one store and rebuilds the active Fractal and LibraDex files without that store's records, mutation history, index keys, or abandoned index extents.<br/>
        /// The default deletes retired source files after validated replacement; <see cref="StorePurgeResidualPolicy.ZeroThenDelete"/> first overwrites every byte of each retired RecordBase artifact and durably flushes it.<br/>
        /// Existing backups, filesystem snapshots, and copies outside this RecordBase file family are intentionally outside the operation's scope.<br/>
        /// </summary>
        /// <param name="storeName">Case-insensitive active or soft-removed store name to purge.<br/></param>
        /// <param name="residualPolicy">How retired source and rollback files are handled after the clean active RecordBase has been installed and validated.<br/></param>
        /// <returns><c>true</c> when the store existed and the requested purge completed; otherwise <c>false</c>.<br/></returns>
        /// <exception cref="StorePurgeResidualCleanupException">Thrown after successful active replacement when one or more retired artifacts could not complete the requested residual cleanup; startup recovery will retry from the durable journal.<br/></exception>
        public bool PurgeStore(
            string storeName,
            StorePurgeResidualPolicy residualPolicy = StorePurgeResidualPolicy.Delete)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
            if (!Enum.IsDefined(residualPolicy))
                throw new ArgumentOutOfRangeException(nameof(residualPolicy), residualPolicy, "The store-purge residual policy is not defined.");

            BeginMaintenanceAsync(nameof(PurgeStore), CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                StoreInfo? purging = null;
                using (EnterWriteLockoutScope())
                using (EnterLibraDexLifecycleExclusiveScope(CancellationToken.None))
                {
                    Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 1);
                    try
                    {
                        lock (_storeCatalogLock)
                        {
                            var stores = ReadStoreInfos();
                            if (stores.TryGetValue(storeName, out var found))
                            {
                                if (found.Lifecycle == StoreLifecycleState.HardDeletePending)
                                    throw new InvalidOperationException($"Store '{storeName}' has an incomplete hard deletion and cannot begin a purge.");
                                if (found.Lifecycle == StoreLifecycleState.PurgePending)
                                    residualPolicy = found.PurgeResidualPolicy;
                                if (found.InstanceID == Guid.Empty)
                                    found.InstanceID = Guid.NewGuid();
                                found.Lifecycle = StoreLifecycleState.PurgePending;
                                found.PurgeResidualPolicy = residualPolicy;
                                stores[storeName] = found;
                                WriteStoreInfos(stores);
                                purging = found;
                            }
                        }

                        if (purging is not null)
                            CompleteStorePurge(purging);
                    }
                    finally
                    {
                        Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 0);
                    }
                }

                CompleteMaintenanceAsync("Completed").GetAwaiter().GetResult();
                return purging is not null;
            }
            catch (StorePurgeResidualCleanupException exception)
            {
                try
                {
                    CompleteMaintenanceAsync(
                        "ResidualCleanupPending",
                        exception.Message).GetAwaiter().GetResult();
                }
                catch
                {
                }
                throw;
            }
            catch (Exception exception)
            {
                try { CompleteMaintenanceAsync("Failed", exception.Message).GetAwaiter().GetResult(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Completes a durable pending purge after normal Fractal and LibraDex startup has established coherent active owners.<br/>
        /// A missing StoreInfo with a surviving journal means active replacement already committed and only retired-file cleanup remains.<br/>
        /// </summary>
        private void RecoverPendingStorePurges()
        {
            StoreInfo[] pending;
            lock (_storeCatalogLock)
            {
                pending = ReadStoreInfos().Values
                    .Where(static info => info.Lifecycle == StoreLifecycleState.PurgePending)
                    .ToArray();
            }
            var journal = TryReadPurgeJournal();
            if (pending.Length == 0 && journal is null)
                return;

            BeginMaintenanceAsync("PurgeStoreRecovery", CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                using (EnterWriteLockoutScope())
                using (EnterLibraDexLifecycleExclusiveScope(CancellationToken.None))
                {
                    Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 1);
                    try
                    {
                        for (var i = 0; i < pending.Length; i++)
                            CompleteStorePurge(pending[i]);

                        if (pending.Length == 0 && journal is not null)
                            CompleteResidualCleanup(journal);
                    }
                    finally
                    {
                        Volatile.Write(ref _suppressMaintenanceHeartbeatWrites, 0);
                    }
                }
                CompleteMaintenanceAsync("Completed").GetAwaiter().GetResult();
            }
            catch (StorePurgeResidualCleanupException exception)
            {
                try { CompleteMaintenanceAsync("ResidualCleanupPending", exception.Message).GetAwaiter().GetResult(); } catch { }
                throw;
            }
            catch (Exception exception)
            {
                try { CompleteMaintenanceAsync("Failed", exception.Message).GetAwaiter().GetResult(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Resolves a crash-interrupted multi-file Fractal replacement before the normal store constructor opens any potentially mixed family.<br/>
        /// Prepared journals have not started replacement and remain for ordinary post-open recovery; replacing or later phases are always completed forward because logical purge intent was already durable.<br/>
        /// </summary>
        private void RecoverInterruptedPurgeReplacementBeforeOpen()
        {
            var journal = TryReadPurgeJournal();
            if (journal is null || journal.Phase != StorePurgePhase.FractalReplacing)
                return;

            CompleteFractalForwardReplacement(journal);
            FractalStore.TakeOwnership(
                dataFilesLocation.FullName,
                name,
                config.FractalStoreConfig);
            journal.Phase = StorePurgePhase.ActiveInstalled;
            WritePurgeJournal(journal);
        }

        /// <summary>
        /// Executes or resumes the logical removal, dense shadow creation, LibraDex compaction, Fractal replacement, registry finalization, and requested residual cleanup for one pending store.<br/>
        /// Every destructive replacement is forward-recoverable from the external purge journal, which deliberately lives outside the Fractal family being replaced.<br/>
        /// </summary>
        /// <param name="info">Durable StoreInfo identity whose lifecycle is <see cref="StoreLifecycleState.PurgePending"/>.<br/></param>
        private void CompleteStorePurge(StoreInfo info)
        {
            var lifecycle = info.Lifecycle;
            if (lifecycle != StoreLifecycleState.PurgePending &&
                lifecycle != StoreLifecycleState.HardDeletePending)
            {
                throw new InvalidOperationException(
                    $"Store '{info.Name}' cannot use physical replacement from lifecycle state '{lifecycle}'.");
            }

            RemoveStoreReality(info, lifecycle, deleteFractalRecords: false);

            var journal = TryReadPurgeJournal();
            if (journal is not null &&
                (!string.Equals(journal.StoreName, info.Name, StringComparison.OrdinalIgnoreCase) ||
                 journal.StoreID != info.ID ||
                 journal.StoreInstanceID != info.InstanceID))
            {
                throw new InvalidOperationException(
                    $"A purge journal for store '{journal.StoreName}' must complete before store '{info.Name}' can be purged.");
            }

            if (journal is null)
            {
                var token = Guid.NewGuid().ToString("N");
                var stageDirectory = Path.Combine(
                    dataFilesLocation.FullName,
                    "." + name + ".purge-" + token);
                Directory.CreateDirectory(stageDirectory);
                var compacted = data.CreateCompactedCopyExcludingGroup(
                    stageDirectory,
                    name,
                    info.ID,
                    CancellationToken.None);
                journal = StorePurgeJournal.Create(
                    info,
                    name,
                    dataFilesLocation.FullName,
                    stageDirectory,
                    compacted.Paths,
                    info.PurgeResidualPolicy);
                WritePurgeJournal(journal);
            }

            if (journal.Phase == StorePurgePhase.Prepared)
            {
                if (!File.Exists(journal.LibraDexRetiredPath))
                {
                    _ = CompactAndRebindLibraDex(
                        CancellationToken.None,
                        new LibraDexCompactionOptions
                        {
                            CatalogOptions = config.CreateLibraDexCatalogOptions(),
                            KeepBackup = true,
                            BackupPath = journal.LibraDexRetiredPath
                        });
                }
                journal.Phase = StorePurgePhase.LibraDexCompacted;
                WritePurgeJournal(journal);
            }

            if (journal.Phase <= StorePurgePhase.LibraDexCompacted)
            {
                journal.Phase = StorePurgePhase.FractalReplacing;
                WritePurgeJournal(journal);
                data.Close();
                try
                {
                    CompleteFractalForwardReplacement(journal);
                }
                catch
                {
                    CompleteFractalForwardReplacement(journal);
                }

                FractalStore.TakeOwnership(
                    dataFilesLocation.FullName,
                    name,
                    config.FractalStoreConfig);
                var reopened = new FractalStore(
                    dataFilesLocation.FullName,
                    name,
                    config.FractalStoreConfig);
                reopened.Open();
                data = reopened;
                if (data.IterateRecordIDsByGroupKey(info.ID).Any())
                    throw new InvalidDataException($"Installed Fractal purge replacement retained records for store '{info.Name}'.");
                journal.Phase = StorePurgePhase.ActiveInstalled;
                WritePurgeJournal(journal);
            }

            if (journal.Phase == StorePurgePhase.ActiveInstalled)
            {
                RemoveStoreRegistryEntry(info, lifecycle);
                journal.Phase = StorePurgePhase.ResidualCleanupPending;
                WritePurgeJournal(journal);
            }

            CompleteResidualCleanup(journal);
        }

        /// <summary>
        /// Completes each staged-to-live Fractal member replacement idempotently and preserves the former live member at its deterministic retired path.<br/>
        /// A consumed staged member with an existing live member is treated as already installed; a missing staged and live member is a fatal incomplete family.<br/>
        /// </summary>
        /// <param name="journal">Durable purge journal containing aligned stage, live, and retired member paths.<br/></param>
        private static void CompleteFractalForwardReplacement(StorePurgeJournal journal)
        {
            for (var i = 0; i < journal.FractalLivePaths.Length; i++)
            {
                var stagedPath = journal.FractalStagedPaths[i];
                var livePath = journal.FractalLivePaths[i];
                var retiredPath = journal.FractalRetiredPaths[i];
                if (!File.Exists(stagedPath))
                {
                    if (!File.Exists(livePath))
                        throw new InvalidDataException($"Fractal purge replacement is missing both staged and live member '{livePath}'.");
                    continue;
                }

                if (File.Exists(retiredPath))
                    throw new InvalidDataException($"Fractal purge retired path already exists while its staged member remains: '{retiredPath}'.");
                if (File.Exists(livePath))
                    File.Replace(stagedPath, livePath, retiredPath, ignoreMetadataErrors: true);
                else
                    File.Move(stagedPath, livePath);
            }
        }

        /// <summary>
        /// Applies the requested disposition to every retired Fractal/LibraDex artifact, then removes the durable journal and empty staging directory.<br/>
        /// Failure retains the journal and throws a typed partial-completion exception because the clean active RecordBase is already authoritative and must not be rolled back.<br/>
        /// </summary>
        /// <param name="journal">Durable active-install proof plus exact retired artifact paths.<br/></param>
        private void CompleteResidualCleanup(StorePurgeJournal journal)
        {
            ulong completedBytes = 0;
            var remaining = GetExistingRetiredPaths(journal);
            try
            {
                for (var i = 0; i < remaining.Length; i++)
                {
                    var path = remaining[i];
                    var length = checked((ulong)new FileInfo(path).Length);
                    if (journal.ResidualPolicy == StorePurgeResidualPolicy.ZeroThenDelete)
                        ZeroAndDeleteRetiredFile(path);
                    else
                        File.Delete(path);
                    completedBytes = checked(completedBytes + length);
                }

                try
                {
                    if (Directory.Exists(journal.StageDirectory) &&
                        !Directory.EnumerateFileSystemEntries(journal.StageDirectory).Any())
                    {
                        Directory.Delete(journal.StageDirectory);
                    }
                }
                catch
                {
                }

                var journalPath = GetPurgeJournalPath();
                if (File.Exists(journalPath))
                    File.Delete(journalPath);
            }
            catch (Exception exception)
            {
                journal.Phase = StorePurgePhase.ResidualCleanupPending;
                WritePurgeJournal(journal);
                throw new StorePurgeResidualCleanupException(
                    journal.StoreName,
                    journal.StoreID,
                    journal.ResidualPolicy,
                    completedBytes,
                    GetExistingRetiredPaths(journal),
                    exception);
            }
        }

        /// <summary>
        /// Overwrites one complete retired file through a reusable bounded zero buffer, durably flushes the logical file contents, and deletes the file only after flush succeeds.<br/>
        /// This provides file-content hygiene for subsequently copied artifacts and deliberately makes no physical-media claim below the filesystem.<br/>
        /// </summary>
        /// <param name="path">Exact retired artifact path to overwrite and delete.<br/></param>
        private static void ZeroAndDeleteRetiredFile(string path)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                Array.Clear(buffer);
                using (var stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Write,
                           FileShare.None,
                           buffer.Length,
                           FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    var remaining = stream.Length;
                    stream.Position = 0;
                    while (remaining > 0)
                    {
                        var count = (int)Math.Min(remaining, buffer.Length);
                        stream.Write(buffer, 0, count);
                        remaining -= count;
                    }
                    stream.Flush(flushToDisk: true);
                }
                File.Delete(path);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        private string GetPurgeJournalPath()
            => Path.Combine(dataFilesLocation.FullName, "." + name + PurgeJournalSuffix);

        private StorePurgeJournal? TryReadPurgeJournal()
        {
            var path = GetPurgeJournalPath();
            if (!File.Exists(path))
                return null;
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<StorePurgeJournal>(bytes)
                ?? throw new InvalidDataException($"Purge journal '{path}' restored as null.");
        }

        private void WritePurgeJournal(StorePurgeJournal journal)
        {
            ArgumentNullException.ThrowIfNull(journal);
            var path = GetPurgeJournalPath();
            var temporaryPath = path + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(journal);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }

        private static string[] GetExistingRetiredPaths(StorePurgeJournal journal)
            => journal.FractalRetiredPaths
                .Append(journal.LibraDexRetiredPath)
                .Where(File.Exists)
                .ToArray();
    }

    /// <summary>
    /// Selects how source/rollback files retired by a successful store purge are disposed after the clean active RecordBase has been validated.<br/>
    /// The policy affects only the current RecordBase file family; backups, snapshots, and external copies remain outside its scope.<br/>
    /// </summary>
    public enum StorePurgeResidualPolicy
    {
        /// <summary>
        /// Deletes retired source and rollback files after the clean active RecordBase has been installed and validated.<br/>
        /// The filesystem may retain recoverable remnants according to its own allocation and storage behavior.<br/>
        /// </summary>
        Delete = 0,

        /// <summary>
        /// Overwrites every byte of each retired source and rollback file with zeros, durably flushes the overwrite, and then deletes the file.<br/>
        /// This is RecordBase-file residual hygiene and does not claim physical-media secure erasure.<br/>
        /// </summary>
        ZeroThenDelete = 1
    }

    internal enum StorePurgePhase
    {
        Prepared = 0,
        LibraDexCompacted = 1,
        FractalReplacing = 2,
        ActiveInstalled = 3,
        ResidualCleanupPending = 4
    }

    internal sealed class StorePurgeJournal
    {
        public int Version { get; set; } = 1;
        public string StoreName { get; set; } = string.Empty;
        public byte StoreID { get; set; }
        public Guid StoreInstanceID { get; set; }
        public StorePurgeResidualPolicy ResidualPolicy { get; set; }
        public StorePurgePhase Phase { get; set; }
        public string StageDirectory { get; set; } = string.Empty;
        public string LibraDexRetiredPath { get; set; } = string.Empty;
        public string[] FractalStagedPaths { get; set; } = Array.Empty<string>();
        public string[] FractalLivePaths { get; set; } = Array.Empty<string>();
        public string[] FractalRetiredPaths { get; set; } = Array.Empty<string>();

        internal static StorePurgeJournal Create(
            RecordBase.StoreInfo info,
            string recordBaseName,
            string liveDirectory,
            string stageDirectory,
            IReadOnlyList<string> stagedPaths,
            StorePurgeResidualPolicy residualPolicy)
        {
            var livePaths = new[]
            {
                Path.Combine(liveDirectory, recordBaseName + ".fractD"),
                Path.Combine(liveDirectory, recordBaseName + ".fractD.shm"),
                Path.Combine(liveDirectory, recordBaseName + ".fractX"),
                Path.Combine(liveDirectory, recordBaseName + ".fractX.shm")
            };
            return new StorePurgeJournal
            {
                StoreName = info.Name,
                StoreID = info.ID,
                StoreInstanceID = info.InstanceID,
                ResidualPolicy = residualPolicy,
                Phase = StorePurgePhase.Prepared,
                StageDirectory = stageDirectory,
                LibraDexRetiredPath = Path.Combine(stageDirectory, recordBaseName + ".lbdx.retired"),
                FractalStagedPaths = stagedPaths.ToArray(),
                FractalLivePaths = livePaths,
                FractalRetiredPaths = livePaths
                    .Select(path => Path.Combine(stageDirectory, Path.GetFileName(path) + ".retired"))
                    .ToArray()
            };
        }
    }

    /// <summary>
    /// Reports that a store is absent from the validated active RecordBase but one or more retired source artifacts did not complete their requested residual cleanup.<br/>
    /// The active purge is irreversible; the durable journal and startup recovery retry only zeroing/deletion of the listed retired files.<br/>
    /// </summary>
    public sealed class StorePurgeResidualCleanupException : IOException
    {
        internal StorePurgeResidualCleanupException(
            string storeName,
            byte storeID,
            StorePurgeResidualPolicy residualPolicy,
            ulong completedBytes,
            IReadOnlyList<string> remainingPaths,
            Exception innerException)
            : base(
                $"Store '{storeName}' was purged from the active RecordBase, but residual policy '{residualPolicy}' remains incomplete for {remainingPaths.Count} retired artifact(s).",
                innerException)
        {
            StoreName = storeName;
            StoreID = storeID;
            ResidualPolicy = residualPolicy;
            CompletedBytes = completedBytes;
            RemainingPaths = remainingPaths;
        }

        /// <summary>
        /// Gets the case-preserved name of the store already removed from the active RecordBase.<br/>
        /// </summary>
        public string StoreName { get; }

        /// <summary>
        /// Gets the durable one-byte store identifier that was purged.<br/>
        /// </summary>
        public byte StoreID { get; }

        /// <summary>
        /// Gets the residual cleanup policy whose processing remains incomplete.<br/>
        /// </summary>
        public StorePurgeResidualPolicy ResidualPolicy { get; }

        /// <summary>
        /// Gets the total length of retired artifacts successfully processed before the cleanup failure.<br/>
        /// </summary>
        public ulong CompletedBytes { get; }

        /// <summary>
        /// Gets the retired artifact paths that still existed when the cleanup failure was reported.<br/>
        /// </summary>
        public IReadOnlyList<string> RemainingPaths { get; }

        /// <summary>
        /// Gets a value confirming that the target store is already absent from the validated active RecordBase.<br/>
        /// </summary>
        public bool ActivePurgeCompleted => true;

        /// <summary>
        /// Gets a value confirming that the durable purge journal will cause startup recovery to retry residual cleanup.<br/>
        /// </summary>
        public bool AutomaticRecoveryScheduled => true;
    }
}
