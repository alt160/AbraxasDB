using System.Diagnostics;
using System.Runtime;

namespace MeridianTrading;

/// <summary>
/// Samples the Meridian host process and CLR without forcing garbage collection or walking application objects.<br/>
/// Cumulative counters and the most recent GC snapshot remain clearly distinct from current process residency.<br/>
/// The sampler retains only the preceding timestamp, allocation counter, and CPU duration needed for low-cost rates.<br/>
/// </summary>
internal sealed class RuntimeMetricsSampler
{
    private readonly object synchronization = new();
    private bool hasPreviousSample;
    private long previousTimestamp;
    private long previousAllocatedBytes;
    private TimeSpan previousProcessorTime;

    /// <summary>
    /// Returns one low-overhead runtime observation for the Meridian .NET host process.<br/>
    /// Working set and private memory include native Abraxas dependencies loaded into this process but exclude separate WebView2 child processes.<br/>
    /// Managed bytes are a non-collecting current estimate; SOH, LOH, POH, fragmentation, load, and pause values describe the most recently completed GC.<br/>
    /// Collection and allocation counters are cumulative since process start, while rates are deltas from the preceding sample and are null for the first sample.<br/>
    /// </summary>
    internal object Sample()
    {
        lock (synchronization)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            long timestamp = Stopwatch.GetTimestamp();
            long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
            TimeSpan processorTime = process.TotalProcessorTime;
            double sampleSeconds = hasPreviousSample ? Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalSeconds : 0;
            double? allocationRateBytesPerSecond = hasPreviousSample && sampleSeconds > 0
                ? Math.Max(0, allocatedBytes - previousAllocatedBytes) / sampleSeconds
                : null;
            double? processorPercent = hasPreviousSample && sampleSeconds > 0
                ? Math.Max(0, (processorTime - previousProcessorTime).TotalSeconds) / sampleSeconds / Environment.ProcessorCount * 100
                : null;

            var gc = GC.GetGCMemoryInfo();
            var generationInfo = gc.GenerationInfo.ToArray();
            long gen0Size = generationInfo.Length > 0 ? generationInfo[0].SizeAfterBytes : 0;
            long gen1Size = generationInfo.Length > 1 ? generationInfo[1].SizeAfterBytes : 0;
            long gen2Size = generationInfo.Length > 2 ? generationInfo[2].SizeAfterBytes : 0;
            long lohSize = generationInfo.Length > 3 ? generationInfo[3].SizeAfterBytes : 0;
            long pohSize = generationInfo.Length > 4 ? generationInfo[4].SizeAfterBytes : 0;
            long gen0Fragmentation = generationInfo.Length > 0 ? generationInfo[0].FragmentationAfterBytes : 0;
            long gen1Fragmentation = generationInfo.Length > 1 ? generationInfo[1].FragmentationAfterBytes : 0;
            long gen2Fragmentation = generationInfo.Length > 2 ? generationInfo[2].FragmentationAfterBytes : 0;
            long lohFragmentation = generationInfo.Length > 3 ? generationInfo[3].FragmentationAfterBytes : 0;
            long pohFragmentation = generationInfo.Length > 4 ? generationInfo[4].FragmentationAfterBytes : 0;
            var generations = new[]
            {
                new { name = "Gen 0", sizeAfterBytes = gen0Size, fragmentationAfterBytes = gen0Fragmentation, collections = (int?)GC.CollectionCount(0) },
                new { name = "Gen 1", sizeAfterBytes = gen1Size, fragmentationAfterBytes = gen1Fragmentation, collections = (int?)GC.CollectionCount(1) },
                new { name = "Gen 2", sizeAfterBytes = gen2Size, fragmentationAfterBytes = gen2Fragmentation, collections = (int?)GC.CollectionCount(2) },
                new { name = "LOH", sizeAfterBytes = lohSize, fragmentationAfterBytes = lohFragmentation, collections = (int?)null },
                new { name = "POH", sizeAfterBytes = pohSize, fragmentationAfterBytes = pohFragmentation, collections = (int?)null }
            };
            double? memoryLoadPercent = gc.HighMemoryLoadThresholdBytes > 0
                ? (double)gc.MemoryLoadBytes / gc.HighMemoryLoadThresholdBytes * 100
                : null;

            previousTimestamp = timestamp;
            previousAllocatedBytes = allocatedBytes;
            previousProcessorTime = processorTime;
            hasPreviousSample = true;

            return new
            {
                sampledAt = DateTimeOffset.Now,
                processId = Environment.ProcessId,
                workingSetBytes = process.WorkingSet64,
                privateMemoryBytes = process.PrivateMemorySize64,
                peakWorkingSetBytes = process.PeakWorkingSet64,
                managedBytes = GC.GetTotalMemory(forceFullCollection: false),
                totalAllocatedBytes = allocatedBytes,
                allocationRateBytesPerSecond,
                processorPercent,
                processorTimeMs = processorTime.TotalMilliseconds,
                threadCount = process.Threads.Count,
                handleCount = process.HandleCount,
                uptimeMs = (DateTime.Now - process.StartTime).TotalMilliseconds,
                gcHeapSizeBytes = gc.HeapSizeBytes,
                totalCommittedBytes = gc.TotalCommittedBytes,
                fragmentedBytes = gc.FragmentedBytes,
                sohBytes = gen0Size + gen1Size + gen2Size,
                lohBytes = lohSize,
                pohBytes = pohSize,
                memoryLoadBytes = gc.MemoryLoadBytes,
                highMemoryLoadThresholdBytes = gc.HighMemoryLoadThresholdBytes,
                totalAvailableMemoryBytes = gc.TotalAvailableMemoryBytes,
                memoryLoadPercent,
                pinnedObjects = gc.PinnedObjectsCount,
                finalizationPending = gc.FinalizationPendingCount,
                promotedBytes = gc.PromotedBytes,
                lastGc = new
                {
                    index = gc.Index,
                    generation = gc.Generation,
                    compacted = gc.Compacted,
                    concurrent = gc.Concurrent,
                    pauseTimePercentage = gc.PauseTimePercentage,
                    pauseDurationsMs = gc.PauseDurations.ToArray().Select(duration => duration.TotalMilliseconds).ToArray()
                },
                generations,
                runtime = new
                {
                    version = Environment.Version.ToString(),
                    serverGc = GCSettings.IsServerGC,
                    latencyMode = GCSettings.LatencyMode.ToString(),
                    lohCompactionMode = GCSettings.LargeObjectHeapCompactionMode.ToString(),
                    processorCount = Environment.ProcessorCount,
                    is64Bit = Environment.Is64BitProcess
                },
                scope = "Meridian .NET host process; WebView2 child processes excluded",
                note = "No GC was requested. Current managed bytes come from GC.GetTotalMemory(false); generation sizes, fragmentation, load, promotion, pinning, and pauses describe the most recently completed GC."
            };
        }
    }
}
