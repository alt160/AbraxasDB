using System;
using System.Collections.Generic;
using System.Linq;
using FractalKVS;
using LibraDex;

namespace AbraxasDB
{
    /// <summary>
    /// Defines record-storage, durability, bulk-execution, index-format, and maintenance policies for a RecordBase; obtain an instance through Default or Create.<br/>
    /// </summary>
    public class RecordBaseConfiguration
    {
        private const int DefaultIndexingBatchSize = 64;
        private RecordBaseConfiguration() { ApplyDurabilityMode(DurabilityMode.Deferred); }

        private bool _enableFindFastParallel = true;
        private int _findFastParallelThreshold = 1024;
        private int? _maxFindFastParallelThreadCount;

        internal FractalStoreConfiguration FractalStoreConfig
        {
            get => _fractalStoreConfig;
            set
            {
                _fractalStoreConfig = value ?? throw new ArgumentNullException(nameof(value));
                _fractalStoreConfig.WriteBehavior = WriteBehaviorForMode(ActiveDurabilityMode);
                //_fractalStoreConfig.EnableIndexCycleDebug = true;
            }
        }
        private FractalStoreConfiguration _fractalStoreConfig = new FractalStoreConfiguration();

        /// <summary>
        /// Describes the durability intent that drives the Fractal store write behavior.<br/>
        /// </summary>
        public enum DurabilityMode
        {
            /// <summary>
            /// Requests immediate record-file persistence through write-through file handles.<br/>
            /// Actual persistence remains subject to operating-system and storage-device guarantees; this does not add multi-record transactions.<br/>
            /// </summary>
            Standard,
            /// <summary>
            /// Deferred durability that batches writes like the Fractal store defaults.<br/>
            /// </summary>
            Deferred
        }

        /// <summary>
        /// The active durability mode applied when the configuration is used to initialize stores.<br/>
        /// </summary>
        public DurabilityMode ActiveDurabilityMode { get; private set; } = DurabilityMode.Deferred;

        /// <summary>
        /// Validates an Abraxas durability policy and maps it to the existing internal write behavior at configuration time.<br/>
        /// No delegate, reflection, or per-record adapter is introduced by this mapping.<br/>
        /// </summary>
        /// <param name="durabilityMode">Defined public durability policy.<br/></param>
        /// <returns>The corresponding internal write behavior.<br/></returns>
        /// <exception cref="ArgumentOutOfRangeException">The durability policy is not defined.<br/></exception>
        private static FractalStoreWriteBehavior WriteBehaviorForMode(DurabilityMode durabilityMode)
            => durabilityMode switch
            {
                DurabilityMode.Deferred => FractalStoreWriteBehavior.Deferred,
                DurabilityMode.Standard => FractalStoreWriteBehavior.Immediate,
                _ => throw new ArgumentOutOfRangeException(nameof(durabilityMode), durabilityMode, "Unknown durability mode.")
            };

        /// <summary>
        /// Applies the requested durability mode to this configuration and keeps the Fractal store write behavior in sync.<br/>
        /// </summary>
        private void ApplyDurabilityMode(DurabilityMode mode)
        {
            FractalStoreWriteBehavior behavior = WriteBehaviorForMode(mode);
            ActiveDurabilityMode = mode;
            FractalStoreConfig.WriteBehavior = behavior;
        }

        /// <summary>
        /// A default, reasonable configuration with a good mix of performance and safety for general usage.<br/>
        /// This uses the <see cref="DurabilityMode.Deferred"/> durability mode.
        /// </summary>
        public static RecordBaseConfiguration Default
        {
            get
            {
                var ret = new RecordBaseConfiguration();

                ret.FractalStoreConfig = new FractalStoreConfiguration();
                ret.FractalStoreConfig.PutManyParallelMinRecordCount = 32768;
                ret.FractalStoreConfig.PutManyParallelThreadCount = Environment.ProcessorCount;
                ret.FractalStoreConfig.MaxChainedRecords = 4;
                ret.FractalStoreConfig.IndexGrowthStrategy = FanOutAlgorithms.Pow4xChain4x;
                ret.FractalStoreConfig.RootTierBucketCount = 64;
                ret.FractalStoreConfig.WriteBehavior = FractalStoreWriteBehavior.Deferred;
                ret.IndexingBatchSize = DefaultIndexingBatchSize;
                ret._enableFindFastParallel = true;
                ret._findFastParallelThreshold = 1024;
                ret.MaxFindFastParallelThreadCount = null;

                return ret;
            }
        }

        /// <summary>
        /// Creates a custom configuration using Abraxas-owned record-index and durability policies.<br/>
        /// Undefined enum values are rejected before any RecordBase files are opened; the existing storage defaults and representations are unchanged.<br/>
        /// </summary>
        /// <param name="putManyParallelMinRecordCount">The minimum record count to use for parallel inserts in the PutMany operation.</param>
        /// <param name="putManyParallelThreadCount">The number of threads to use for parallel processing of the PutMany operation.</param>
        /// <param name="maxChainedRecords">The maximum number of chained records.</param>
        /// <param name="indexGrowthStrategy">Growth policy for the primary record-storage index, not secondary-index shape selection.<br/></param>
        /// <param name="rootTierBucketCount">The initial number of buckets in the root tier.</param>
        /// <param name="durabilityMode">Record-file persistence policy; Deferred remains the default, while Standard requests write-through handles.<br/></param>
        /// <param name="indexingBatchSize">The batch size to use for indexing operations.</param>
        /// <param name="fastTrimTailBatchRecords">Maximum number of contiguous tombstoned tail records to trim per fast-trim pass.</param>
        /// <param name="useRecoverableIndexFileFormat">Whether new persistent index catalogs and explicit compaction replacements use the embedded recovery format; existing files are not rewritten merely by opening them.<br/></param>
        /// <returns>A <see cref="RecordBaseConfiguration"/> instance with the specified settings.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An enum value is undefined or the indexing batch size is not positive.<br/></exception>
        public static RecordBaseConfiguration Create(
            int putManyParallelMinRecordCount = 32768,
            int putManyParallelThreadCount = 4,
            int maxChainedRecords = 4,
            RecordIndexGrowthStrategy indexGrowthStrategy = RecordIndexGrowthStrategy.Pow4xChain4x,
            uint rootTierBucketCount = 64,
            DurabilityMode durabilityMode = DurabilityMode.Deferred,
            int indexingBatchSize = DefaultIndexingBatchSize,
            int fastTrimTailBatchRecords = 256,
            bool useRecoverableIndexFileFormat = false)
        {
            FanOutAlgorithms storageGrowthStrategy = ToStorageGrowthStrategy(indexGrowthStrategy);
            var config = new RecordBaseConfiguration();
            config.ApplyDurabilityMode(durabilityMode);
            config.FractalStoreConfig.PutManyParallelMinRecordCount = putManyParallelMinRecordCount;
            config.FractalStoreConfig.PutManyParallelThreadCount = putManyParallelThreadCount;
            config.FractalStoreConfig.MaxChainedRecords = maxChainedRecords;
            config.FractalStoreConfig.IndexGrowthStrategy = storageGrowthStrategy;
            config.FractalStoreConfig.RootTierBucketCount = rootTierBucketCount;
            config.IndexingBatchSize = ValidateIndexingBatchSize(indexingBatchSize);
            config.FastTrimTailBatchRecords = fastTrimTailBatchRecords;
            config.UseRecoverableIndexFileFormat = useRecoverableIndexFileFormat;

            return config;
        }

        /// <summary>
        /// Gets the record-count threshold for automatic parallel bulk insertion; explicit call-scoped worker selection can admit the coordinated route below this threshold.<br/>
        /// </summary>
        public int PutManyParallelMinRecordCount { get => FractalStoreConfig.PutManyParallelMinRecordCount; private set => FractalStoreConfig.PutManyParallelMinRecordCount = value; }
        /// <summary>
        /// Gets the default maximum worker count for supported bulk-write stages; individual maintained-index stages may deliberately use fewer workers.<br/>
        /// </summary>
        public int PutManyParallelThreadCount { get => FractalStoreConfig.PutManyParallelThreadCount; private set => FractalStoreConfig.PutManyParallelThreadCount = value; }
        /// <summary>
        /// Gets the primary record-index collision-chain length that prompts index expansion, not a maximum serialized-record length.<br/>
        /// </summary>
        public int MaxChainedRecords { get => FractalStoreConfig.MaxChainedRecords; private set => FractalStoreConfig.MaxChainedRecords = value; }
        /// <summary>Gets the Abraxas growth strategy for the primary record-storage index; secondary indexes are configured separately.<br/></summary>
        public RecordIndexGrowthStrategy IndexGrowthStrategy => FromStorageGrowthStrategy(FractalStoreConfig.IndexGrowthStrategy);
        /// <summary>
        /// Gets the initial root-tier bucket count for the primary record index; it is not a fixed final store capacity.<br/>
        /// </summary>
        public uint RootTierBucketCount { get => FractalStoreConfig.RootTierBucketCount; private set => FractalStoreConfig.RootTierBucketCount = value; }
        /// <summary>
        /// Gets the default maximum worker concurrency used by multi-record reads and legacy parallel hydration; FastFind's exact-worker selection is configured separately.<br/>
        /// </summary>
        public int GetManyParallelThreadCount { get => FractalStoreConfig.GetManyParallelThreadCount; private set => FractalStoreConfig.GetManyParallelThreadCount = value; }
        /// <summary>
        /// Gets or sets the maximum contiguous tombstoned tail records handled by one fast-trim pass; zero disables fast tail trimming.<br/>
        /// </summary>
        public int FastTrimTailBatchRecords { get => FractalStoreConfig.FastTrimTailBatchRecords; set => FractalStoreConfig.FastTrimTailBatchRecords = value; }
        /// <summary>
        /// Gets retained indexing-batch-size configuration metadata; current synchronous maintained-index paths do not consult this value or create a background indexing queue.<br/>
        /// </summary>
        public int IndexingBatchSize { get; private set; } = DefaultIndexingBatchSize;
        internal int? RowIDGeneratorBlockSize { get; set; } = 256;
        /// <summary>
        /// Gets or sets whether newly created persistent index catalogs use the embedded recovery format.<br/>
        /// Opening an existing version-one RecordBase with this setting does not rewrite it; a later explicit <see cref="RecordBase.Compact()"/> migrates the catalog through RecordBase's coordinated close, replacement, reopen, and live-store rebind path.<br/>
        /// The default is <see langword="false"/> so existing applications do not silently change their on-disk compatibility contract.<br/>
        /// </summary>
        public bool UseRecoverableIndexFileFormat { get; set; }
        /// <summary>
        /// Retains source compatibility with the former automatic FastFind threshold policy.<br/>
        /// FastFind now defaults to exactly one worker unless the caller supplies an exact count or explicitly passes <c>useParallel: true</c>; this value no longer changes route selection.<br/>
        /// </summary>
        [Obsolete("FastFind no longer selects parallel execution automatically. Supply an exact maxDegreeOfParallelism value at the call site.")]
        public bool EnableFindFastParallel
        {
            get => _enableFindFastParallel;
            set => _enableFindFastParallel = value;
        }
        /// <summary>
        /// Retains source compatibility with the former record-count threshold for automatic FastFind parallelism.<br/>
        /// The exact-worker contract does not consult a record-count threshold.<br/>
        /// </summary>
        [Obsolete("FastFind no longer uses an automatic parallel threshold. Supply an exact maxDegreeOfParallelism value at the call site.")]
        public int FindFastParallelThreshold
        {
            get => _findFastParallelThreshold;
            set => _findFastParallelThreshold = value < 0
                ? throw new ArgumentOutOfRangeException(nameof(FindFastParallelThreshold), "Threshold must be non-negative.")
                : value;
        }
        /// <summary>
        /// Gets or sets an optional FastFind worker limit; explicit exact-worker requests above it are rejected rather than silently reduced, while automatic counts requested through useParallel are bounded by it.<br/>
        /// </summary>
        public int? MaxFindFastParallelThreadCount
        {
            get => _maxFindFastParallelThreadCount;
            set
            {
                if (value.HasValue && value.Value < 1)
                    throw new ArgumentOutOfRangeException(nameof(MaxFindFastParallelThreadCount), "Max thread count must be at least 1 when specified.");
                _maxFindFastParallelThreadCount = value;
            }
        }

        /// <summary>
        /// How long a maintenance operation (optimize/compact) is allowed to be silent before it is considered stale/abandoned.<br/>
        /// Default: 15 minutes.
        /// </summary>
        public TimeSpan MaintenanceStaleAfter { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Gets or sets how long to wait when attempting to acquire the cross-process maintenance lock.<br/>
        /// The default is five seconds; this does not change the stale-operation heartbeat policy.<br/>
        /// </summary>
        public TimeSpan MaintenanceLockAcquireTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Heartbeat mapping thresholds used to select the heartbeat interval based on record count.<br/>
        /// Defaults implement the mapping: &lt;=10k =&gt; 1s, &lt;=100k =&gt; 2s, &lt;=500k =&gt; 5s, &gt;500k =&gt; 10s.<br/>
        /// These values are configurable so callers can tune behavior for very large or very small stores.
        /// </summary>
        public ulong HeartbeatThreshold1 { get; set; } = 10_000ul;
        /// <summary>
        /// Gets or sets the inclusive record-count upper bound for a two-second maintenance heartbeat after the first threshold has been exceeded.<br/>
        /// </summary>
        public ulong HeartbeatThreshold2 { get; set; } = 100_000ul;
        /// <summary>
        /// Gets or sets the inclusive record-count upper bound for a five-second maintenance heartbeat after the earlier thresholds have been exceeded.<br/>
        /// </summary>
        public ulong HeartbeatThreshold3 { get; set; } = 500_000ul;

        /// <summary>
        /// Returns the recommended heartbeat interval for the supplied record count using the configured thresholds.<br/>
        /// </summary>
        /// <param name="recordCount">Total number of records across stores.</param>
        public TimeSpan GetMaintenanceHeartbeatInterval(ulong recordCount)
        {
            if (recordCount <= HeartbeatThreshold1) return TimeSpan.FromSeconds(1);
            if (recordCount <= HeartbeatThreshold2) return TimeSpan.FromSeconds(2);
            if (recordCount <= HeartbeatThreshold3) return TimeSpan.FromSeconds(5);
            return TimeSpan.FromSeconds(10);
        }

        private static int ValidateIndexingBatchSize(int batchSize)
            => batchSize < 1
                ? throw new ArgumentOutOfRangeException(nameof(batchSize), "Indexing batch size must be greater than zero.")
                : batchSize;

        /// <summary>
        /// Creates one immutable LibraDex catalog-options snapshot for a RecordBase lifecycle boundary.<br/>
        /// A new instance prevents later configuration mutation from changing an already-open catalog while preserving Abraxas's mandatory UInt64 identity contract and the caller's selected creation/compaction format.<br/>
        /// </summary>
        /// <returns>Catalog options suitable for persistent RecordBase create, open, compaction, and rollback reopen operations.<br/></returns>
        internal CatalogOptions CreateLibraDexCatalogOptions()
            => new()
            {
                RequiredIdentityType = typeof(ulong),
                UseRecoverableFileFormat = UseRecoverableIndexFileFormat
            };

        /// <summary>
        /// Maps an Abraxas record-index strategy to the exact existing storage strategy at configuration time.<br/>
        /// Explicit cases avoid coupling public enum numbers to a component's representation and add no work to record traversal.<br/>
        /// </summary>
        /// <param name="indexGrowthStrategy">Defined Abraxas strategy selected by the caller.<br/></param>
        /// <returns>The corresponding internal record-storage strategy.<br/></returns>
        /// <exception cref="ArgumentOutOfRangeException">The strategy is not a defined supported value.<br/></exception>
        private static FanOutAlgorithms ToStorageGrowthStrategy(RecordIndexGrowthStrategy indexGrowthStrategy)
            => indexGrowthStrategy switch
            {
                RecordIndexGrowthStrategy.Pow2xChain4x => FanOutAlgorithms.Pow2xChain4x,
                RecordIndexGrowthStrategy.Pow4xChain4x => FanOutAlgorithms.Pow4xChain4x,
                RecordIndexGrowthStrategy.Pow8xChain4x => FanOutAlgorithms.Pow8xChain4x,
                RecordIndexGrowthStrategy.Pow16xChain4x => FanOutAlgorithms.Pow16xChain4x,
                RecordIndexGrowthStrategy.Pow32xChain4x => FanOutAlgorithms.Pow32xChain4x,
                RecordIndexGrowthStrategy.Pow64xChain4x => FanOutAlgorithms.Pow64xChain4x,
                RecordIndexGrowthStrategy.Pow8x256 => FanOutAlgorithms.Pow8x256,
                RecordIndexGrowthStrategy.Pow8422 => FanOutAlgorithms.Pow8422,
                RecordIndexGrowthStrategy.Pow15 => FanOutAlgorithms.Pow15,
                RecordIndexGrowthStrategy.Pow2 => FanOutAlgorithms.Pow2,
                RecordIndexGrowthStrategy.Pow3 => FanOutAlgorithms.Pow3,
                RecordIndexGrowthStrategy.Pow4 => FanOutAlgorithms.Pow4,
                RecordIndexGrowthStrategy.Pow8 => FanOutAlgorithms.Pow8,
                RecordIndexGrowthStrategy.Fixed64 => FanOutAlgorithms.Fixed64,
                RecordIndexGrowthStrategy.Fixed128 => FanOutAlgorithms.Fixed128,
                RecordIndexGrowthStrategy.Fixed256 => FanOutAlgorithms.Fixed256,
                RecordIndexGrowthStrategy.Fixed1k => FanOutAlgorithms.Fixed1k,
                RecordIndexGrowthStrategy.Fixed2k => FanOutAlgorithms.Fixed2k,
                RecordIndexGrowthStrategy.Fixed4k => FanOutAlgorithms.Fixed4k,
                RecordIndexGrowthStrategy.Fixed8k => FanOutAlgorithms.Fixed8k,
                RecordIndexGrowthStrategy.Fixed16k => FanOutAlgorithms.Fixed16k,
                RecordIndexGrowthStrategy.Fixed32k => FanOutAlgorithms.Fixed32k,
                RecordIndexGrowthStrategy.Fixed64k => FanOutAlgorithms.Fixed64k,
                _ => throw new ArgumentOutOfRangeException(nameof(indexGrowthStrategy), indexGrowthStrategy, "Unknown record-index growth strategy.")
            };

        /// <summary>
        /// Reads the public strategy from the internal storage configuration without reflection, boxing, or numeric enum casts.<br/>
        /// Unsupported internal values fail rather than reporting a misleading public strategy.<br/>
        /// </summary>
        /// <param name="strategy">Existing internal record-storage strategy.<br/></param>
        /// <returns>The equivalent Abraxas record-index strategy.<br/></returns>
        /// <exception cref="InvalidOperationException">The internal strategy has no supported public mapping.<br/></exception>
        private static RecordIndexGrowthStrategy FromStorageGrowthStrategy(FanOutAlgorithms strategy)
            => strategy switch
            {
                FanOutAlgorithms.Pow2xChain4x => RecordIndexGrowthStrategy.Pow2xChain4x,
                FanOutAlgorithms.Pow4xChain4x => RecordIndexGrowthStrategy.Pow4xChain4x,
                FanOutAlgorithms.Pow8xChain4x => RecordIndexGrowthStrategy.Pow8xChain4x,
                FanOutAlgorithms.Pow16xChain4x => RecordIndexGrowthStrategy.Pow16xChain4x,
                FanOutAlgorithms.Pow32xChain4x => RecordIndexGrowthStrategy.Pow32xChain4x,
                FanOutAlgorithms.Pow64xChain4x => RecordIndexGrowthStrategy.Pow64xChain4x,
                FanOutAlgorithms.Pow8x256 => RecordIndexGrowthStrategy.Pow8x256,
                FanOutAlgorithms.Pow8422 => RecordIndexGrowthStrategy.Pow8422,
                FanOutAlgorithms.Pow15 => RecordIndexGrowthStrategy.Pow15,
                FanOutAlgorithms.Pow2 => RecordIndexGrowthStrategy.Pow2,
                FanOutAlgorithms.Pow3 => RecordIndexGrowthStrategy.Pow3,
                FanOutAlgorithms.Pow4 => RecordIndexGrowthStrategy.Pow4,
                FanOutAlgorithms.Pow8 => RecordIndexGrowthStrategy.Pow8,
                FanOutAlgorithms.Fixed64 => RecordIndexGrowthStrategy.Fixed64,
                FanOutAlgorithms.Fixed128 => RecordIndexGrowthStrategy.Fixed128,
                FanOutAlgorithms.Fixed256 => RecordIndexGrowthStrategy.Fixed256,
                FanOutAlgorithms.Fixed1k => RecordIndexGrowthStrategy.Fixed1k,
                FanOutAlgorithms.Fixed2k => RecordIndexGrowthStrategy.Fixed2k,
                FanOutAlgorithms.Fixed4k => RecordIndexGrowthStrategy.Fixed4k,
                FanOutAlgorithms.Fixed8k => RecordIndexGrowthStrategy.Fixed8k,
                FanOutAlgorithms.Fixed16k => RecordIndexGrowthStrategy.Fixed16k,
                FanOutAlgorithms.Fixed32k => RecordIndexGrowthStrategy.Fixed32k,
                FanOutAlgorithms.Fixed64k => RecordIndexGrowthStrategy.Fixed64k,
                _ => throw new InvalidOperationException("The internal record-index strategy has no Abraxas mapping.")
            };
    }

    /// <summary>
    /// Selects how the primary record-storage index grows as more records occupy its buckets.<br/>
    /// The root uses the configured RootTierBucketCount; expanded tiers follow the selected strategy.<br/>
    /// These choices preserve the existing storage algorithms and do not select secondary-index key shapes.<br/>
    /// </summary>
    public enum RecordIndexGrowthStrategy : uint
    {
        /// <summary>Uses 2 times the root bucket count at tier one, then four times the configured chain length for deeper tiers.<br/></summary>
        Pow2xChain4x,

        /// <summary>Uses 4 times the root bucket count at tier one, then four times the configured chain length for deeper tiers.<br/></summary>
        Pow4xChain4x,

        /// <summary>Uses 8 times the root bucket count at tier one, then four times the configured chain length for deeper tiers.<br/></summary>
        Pow8xChain4x,

        /// <summary>Uses 16 times the root bucket count at tier one, then four times the configured chain length for deeper tiers.<br/></summary>
        Pow16xChain4x,

        /// <summary>Uses 32 times the root bucket count at tier one, then four times the configured chain length for deeper tiers.<br/></summary>
        Pow32xChain4x,

        /// <summary>Uses 64 times the root bucket count at tier one, then four times the configured chain length for deeper tiers.<br/></summary>
        Pow64xChain4x,

        /// <summary>Uses eight times the root bucket count at tier one, then 256 buckets for deeper tiers.<br/></summary>
        Pow8x256,

        /// <summary>Multiplies the previous bucket count by eight at tier one, four at tier two, then two at each deeper tier.<br/></summary>
        Pow8422,

        /// <summary>Multiplies the previous bucket count by 1.5 and applies the existing power-of-two rounding rule.<br/></summary>
        Pow15,

        /// <summary>Multiplies the previous bucket count by 2 at each expanded tier.<br/></summary>
        Pow2,

        /// <summary>Multiplies the previous bucket count by three and applies the existing power-of-two rounding rule.<br/></summary>
        Pow3,

        /// <summary>Multiplies the previous bucket count by 4 at each expanded tier.<br/></summary>
        Pow4,

        /// <summary>Multiplies the previous bucket count by 8 at each expanded tier.<br/></summary>
        Pow8,

        /// <summary>Uses 64 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed64,

        /// <summary>Uses 128 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed128,

        /// <summary>Uses 256 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed256,

        /// <summary>Uses 1024 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed1k,

        /// <summary>Uses 2048 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed2k,

        /// <summary>Uses 4096 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed4k,

        /// <summary>Uses 8192 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed8k,

        /// <summary>Uses 16384 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed16k,

        /// <summary>Uses 32768 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed32k,

        /// <summary>Uses 65536 buckets at every expanded tier; the root retains its configured bucket count.<br/></summary>
        Fixed64k,
    }
}
