using System.Diagnostics;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Serializes opt-in <see cref="PutManyFastProgress"/> observations and throttles hot-loop updates without imposing work when no observer was supplied.<br/>
        /// The reporter never permits diagnostic callback failure to change authoritative Fractal or derived LibraDex mutation outcome.<br/>
        /// </summary>
        private sealed class PutManyFastProgressReporter
        {
            private readonly Action<PutManyFastProgress> observer;
            private readonly Stopwatch operationTimer = Stopwatch.StartNew();
            private readonly object publishGate = new();
            private readonly int totalRecords;
            private readonly int workerCount;
            private readonly int reportInterval;
            private Stopwatch phaseTimer = Stopwatch.StartNew();
            private PutManyFastRoute route;
            private PutManyFastPhase phase;
            private int currentIndexOrdinal;
            private int indexCount;
            private string? currentIndexPath;
            private int nextReportAt;

            /// <summary>
            /// Creates one operation-scoped reporter whose update count remains bounded to approximately two hundred observations per phase.<br/>
            /// </summary>
            /// <param name="observer">Developer callback receiving immutable progress snapshots.<br/></param>
            /// <param name="totalRecords">Exact number of records submitted to the operation.<br/></param>
            /// <param name="workerCount">Resolved maximum physical worker count for this call.<br/></param>
            private PutManyFastProgressReporter(
                Action<PutManyFastProgress> observer,
                int totalRecords,
                int workerCount)
            {
                this.observer = observer;
                this.totalRecords = totalRecords;
                this.workerCount = workerCount;
                reportInterval = Math.Max(1, (totalRecords + 199) / 200);
                nextReportAt = reportInterval;
            }

            /// <summary>
            /// Creates a reporter only when a caller opted into progress observation.<br/>
            /// Returning <see langword="null"/> keeps the ordinary hot path free of Stopwatch, lock, and callback allocations.<br/>
            /// </summary>
            /// <param name="options">Optional call-scoped bulk settings.<br/></param>
            /// <param name="totalRecords">Exact number of records submitted to the operation.<br/></param>
            /// <param name="workerCount">Resolved maximum physical worker count for this call.<br/></param>
            /// <returns>An operation reporter, or <see langword="null"/> when progress was not requested.<br/></returns>
            internal static PutManyFastProgressReporter? Create(
                PutManyFastOptions? options,
                int totalRecords,
                int workerCount)
                => options?.Progress is { } observer
                    ? new PutManyFastProgressReporter(observer, totalRecords, workerCount)
                    : null;

            /// <summary>
            /// Starts one measurable operation phase and publishes its zero-work boundary immediately.<br/>
            /// </summary>
            /// <param name="route">Physical Abraxas route selected for this operation.<br/></param>
            /// <param name="phase">Phase beginning execution.<br/></param>
            /// <param name="currentIndexOrdinal">One-based maintained-index ordinal, or zero outside index publication.<br/></param>
            /// <param name="indexCount">Total maintained indexes participating in the operation.<br/></param>
            /// <param name="currentIndexPath">Current maintained property path, or <see langword="null"/> outside index publication.<br/></param>
            internal void BeginPhase(
                PutManyFastRoute route,
                PutManyFastPhase phase,
                int currentIndexOrdinal = 0,
                int indexCount = 0,
                string? currentIndexPath = null)
            {
                lock (publishGate)
                {
                    this.route = route;
                    this.phase = phase;
                    this.currentIndexOrdinal = currentIndexOrdinal;
                    this.indexCount = indexCount;
                    this.currentIndexPath = currentIndexPath;
                    nextReportAt = reportInterval;
                    phaseTimer = Stopwatch.StartNew();
                    PublishLocked(0, phaseCompleted: false, operationCompleted: false);
                }
            }

            /// <summary>
            /// Publishes a monotonic phase count only after the next bounded reporting threshold is crossed.<br/>
            /// Concurrent callers are serialized only at an emitted observation rather than at every processed record or index tuple.<br/>
            /// </summary>
            /// <param name="completedRecords">Completed record-aligned units in the current phase.<br/></param>
            internal void Report(int completedRecords)
            {
                if (completedRecords < Volatile.Read(ref nextReportAt) && completedRecords < totalRecords)
                    return;

                lock (publishGate)
                {
                    if (completedRecords < nextReportAt && completedRecords < totalRecords)
                        return;

                    int completed = Math.Clamp(completedRecords, 0, totalRecords);
                    nextReportAt = completed >= totalRecords
                        ? int.MaxValue
                        : Math.Min(totalRecords, completed + reportInterval);
                    PublishLocked(completed, phaseCompleted: false, operationCompleted: false);
                }
            }

            /// <summary>
            /// Closes the current phase with its exact final count and elapsed duration.<br/>
            /// </summary>
            /// <param name="completedRecords">Exact completed record-aligned units for the phase.<br/></param>
            internal void CompletePhase(int completedRecords)
            {
                lock (publishGate)
                    PublishLocked(Math.Clamp(completedRecords, 0, totalRecords), phaseCompleted: true, operationCompleted: false);
            }

            /// <summary>
            /// Publishes the successful terminal snapshot after authoritative and derived persistence have converged.<br/>
            /// </summary>
            internal void CompleteOperation()
            {
                lock (publishGate)
                {
                    route = route == PutManyFastRoute.Unknown ? PutManyFastRoute.PerRecordFallback : route;
                    phase = PutManyFastPhase.Completed;
                    currentIndexOrdinal = 0;
                    indexCount = 0;
                    currentIndexPath = null;
                    phaseTimer.Restart();
                    PublishLocked(totalRecords, phaseCompleted: true, operationCompleted: true);
                }
            }

            /// <summary>
            /// Invokes the optional observer with one immutable snapshot while containing diagnostic callback failures.<br/>
            /// </summary>
            /// <param name="completedRecords">Completed record-aligned units in the current phase.<br/></param>
            /// <param name="phaseCompleted">Whether the current phase reached its success boundary.<br/></param>
            /// <param name="operationCompleted">Whether the complete mutation reached Fractal/LibraDex convergence.<br/></param>
            private void PublishLocked(int completedRecords, bool phaseCompleted, bool operationCompleted)
            {
                var snapshot = new PutManyFastProgress(
                    route,
                    phase,
                    totalRecords,
                    completedRecords,
                    workerCount,
                    currentIndexOrdinal,
                    indexCount,
                    currentIndexPath,
                    phaseTimer.Elapsed,
                    operationTimer.Elapsed,
                    phaseCompleted,
                    operationCompleted);
                try
                {
                    observer(snapshot);
                }
                catch
                {
                    // Progress is diagnostic. An observer must never turn a successful durable mutation into an apparent failure.
                }
            }
        }

        /// <summary>
        /// Resolves and validates the call-scoped physical worker maximum while preserving RecordBase configuration as the zero-ceremony default.<br/>
        /// </summary>
        /// <param name="options">Optional call-scoped bulk settings.<br/></param>
        /// <returns>A positive worker maximum suitable for Fractal and supported LibraDex stages.<br/></returns>
        private int ResolvePutManyFastWorkerCount(PutManyFastOptions? options)
        {
            int workerCount = options?.MaximumDegreeOfParallelism ??
                rbase.config?.FractalStoreConfig.PutManyParallelThreadCount ??
                Environment.ProcessorCount;
            if (workerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PutManyFastOptions.MaximumDegreeOfParallelism),
                    "Maximum degree of parallelism must be positive when supplied.");
            }

            return workerCount;
        }
    }

    /// <summary>
    /// Selects call-scoped behavior for <c>RecordStore&lt;T&gt;.PutManyFast</c> without changing existing overload defaults.<br/>
    /// Omit this type for the ordinary automatic route; specify only the controls a caller needs for diagnostics or bounded execution.<br/>
    /// </summary>
    public sealed class PutManyFastOptions
    {
        /// <summary>
        /// Gets or initializes the maximum physical workers available to coordinated bulk stages that benefit from independent work, including Fractal payload insertion.<br/>
        /// A value of one requests the same coordinated bulk algorithm without parallel workers; <see langword="null"/> uses RecordBase configuration.<br/>
        /// Abraxas may deliberately collapse a maintained index to one locality-preserving writer lane when multiple workers would contend over the same LibraDex shelves; this limit is a ceiling rather than a promise that every stage uses the requested count.<br/>
        /// </summary>
        public int? MaximumDegreeOfParallelism { get; init; }

        /// <summary>
        /// Gets or initializes an optional synchronous observer receiving bounded phase and count snapshots.<br/>
        /// Callbacks may occur on storage worker threads, are serialized by Abraxas, and are ignored if they throw so observation cannot alter persistence.<br/>
        /// </summary>
        public Action<PutManyFastProgress>? Progress { get; init; }
    }

    /// <summary>
    /// Identifies the physical Abraxas route selected for one <c>PutManyFast</c> call.<br/>
    /// </summary>
    public enum PutManyFastRoute
    {
        /// <summary>No physical route has yet been identified by the progress observation.<br/></summary>
        Unknown = 0,
        /// <summary>Prepared records and supported maintained indexes use coordinated bulk publication.<br/></summary>
        CoordinatedBulk = 1,
        /// <summary>Records use the ordinary per-record persistence path when the coordinated route is not selected.<br/></summary>
        PerRecordFallback = 2
    }

    /// <summary>
    /// Identifies the current durable or preparatory phase of one <c>PutManyFast</c> operation.<br/>
    /// </summary>
    public enum PutManyFastPhase
    {
        /// <summary>Record identities and serialized payloads are being prepared before coordinated physical writes.<br/></summary>
        PreparingRecords = 0,
        /// <summary>Maintained index keys are being extracted from the prepared record payloads.<br/></summary>
        PreparingIndexKeys = 1,
        /// <summary>Prepared unique keys are being reserved against conflicting writers before publication.<br/></summary>
        ReservingUniqueKeys = 2,
        /// <summary>Authoritative record payloads are being written through the underlying record store.<br/></summary>
        WritingFractal = 3,
        /// <summary>Prepared tuples are being published to the current maintained index; counts apply to this index phase.<br/></summary>
        WritingLibraDexIndex = 4,
        /// <summary>The mutation cohort is being finalized, including any required index convergence repair and checkpoint publication.<br/></summary>
        PublishingConvergence = 5,
        /// <summary>The fallback route is persisting records individually, including their maintained index updates.<br/></summary>
        WritingFallbackRecords = 6,
        /// <summary>The operation reached successful authoritative-record and maintained-index convergence.<br/></summary>
        Completed = 7
    }

    /// <summary>
    /// Reports bounded, immutable progress for one synchronous <c>PutManyFast</c> call.<br/>
    /// Counts are record-aligned within the named phase: during index publication they describe tuples completed for the current index rather than a fabricated whole-operation percentage.<br/>
    /// </summary>
    /// <param name="Route">Physical Abraxas mutation route selected for the call.<br/></param>
    /// <param name="Phase">Current preparation, Fractal, LibraDex, convergence, fallback, or terminal phase.<br/></param>
    /// <param name="TotalRecords">Exact number of submitted records.<br/></param>
    /// <param name="CompletedRecords">Completed record-aligned units in the current phase.<br/></param>
    /// <param name="WorkerCount">Maximum physical workers resolved for the call.<br/></param>
    /// <param name="CurrentIndexOrdinal">One-based current index ordinal, or zero outside index publication.<br/></param>
    /// <param name="IndexCount">Number of maintained indexes participating in coordinated publication.<br/></param>
    /// <param name="CurrentIndexPath">Current maintained property path, or <see langword="null"/> outside index publication.<br/></param>
    /// <param name="PhaseElapsed">Elapsed time in the current phase at snapshot creation.<br/></param>
    /// <param name="TotalElapsed">Elapsed time since the operation reporter was created.<br/></param>
    /// <param name="PhaseCompleted">Whether the current phase reached its successful terminal boundary.<br/></param>
    /// <param name="OperationCompleted">Whether Fractal and every maintained derived index converged successfully.<br/></param>
    public readonly record struct PutManyFastProgress(
        PutManyFastRoute Route,
        PutManyFastPhase Phase,
        int TotalRecords,
        int CompletedRecords,
        int WorkerCount,
        int CurrentIndexOrdinal,
        int IndexCount,
        string? CurrentIndexPath,
        TimeSpan PhaseElapsed,
        TimeSpan TotalElapsed,
        bool PhaseCompleted,
        bool OperationCompleted);
}
