using System.Diagnostics;
using System.Threading;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Begins one opt-in measurement session for the mutation lane shared by every facade of this store generation.<br/>
        /// The disabled path retains the existing monitor acquisition without timestamp, allocation, or counter work.<br/>
        /// Only one session may be active for a store generation because concurrent sessions would make ownership and reset boundaries ambiguous.<br/>
        /// </summary>
        /// <returns>A disposable session that can capture cumulative mutation diagnostics until it is disposed.<br/></returns>
        internal RecordStoreMutationDiagnosticsSession BeginMutationDiagnostics()
            => storeRuntime.BeginMutationDiagnostics();

        /// <summary>
        /// Counts the maintained LibraDex targets visited by one new-record upsert before the convergence checkpoint is published.<br/>
        /// The count is a logical target count rather than a claim about lower-level DataKernel write calls or text-projection subroutes.<br/>
        /// </summary>
        /// <returns>The current number of ordinary, composite, regex, and partial index targets.<br/></returns>
        private int GetMaintainedLibraDexMutationTargetCount()
            => libraDexSimpleIndexes.Length +
               libraDexCompositeIndexes.Length +
               libraDexRegexProjectionIndexes.Length +
               libraDexPartialIndexes.Length;
    }

    /// <summary>
    /// Owns one opt-in RecordStore mutation measurement interval.<br/>
    /// Capture may be called repeatedly while the session is active; disposal disables future measurement without invalidating previously captured snapshots.<br/>
    /// </summary>
    internal sealed class RecordStoreMutationDiagnosticsSession : IDisposable
    {
        private readonly RecordStoreMutationDiagnosticsAccumulator accumulator;
        private Action<RecordStoreMutationDiagnosticsAccumulator>? release;

        /// <summary>
        /// Creates a session around one shared store-generation accumulator.<br/>
        /// </summary>
        /// <param name="accumulator">Accumulator receiving enabled mutation measurements.<br/></param>
        /// <param name="release">Callback that detaches this exact accumulator from its store generation.<br/></param>
        internal RecordStoreMutationDiagnosticsSession(
            RecordStoreMutationDiagnosticsAccumulator accumulator,
            Action<RecordStoreMutationDiagnosticsAccumulator> release)
        {
            this.accumulator = accumulator;
            this.release = release;
        }

        /// <summary>
        /// Captures a thread-safe cumulative snapshot without stopping the measurement session.<br/>
        /// </summary>
        /// <returns>Current fixed-counter diagnostics for the session interval.<br/></returns>
        internal RecordStoreMutationDiagnostics Capture()
            => accumulator.Capture();

        /// <summary>
        /// Disables this exact diagnostic session once.<br/>
        /// In-flight mutation scopes retain their accumulator reference and finish their final measurement safely.<br/>
        /// </summary>
        public void Dispose()
        {
            Action<RecordStoreMutationDiagnosticsAccumulator>? current = Interlocked.Exchange(ref release, null);
            current?.Invoke(accumulator);
        }
    }

    /// <summary>
    /// Reports cumulative opt-in diagnostics for one RecordStore mutation measurement interval.<br/>
    /// Gate wait is summed across contending callers and can therefore exceed wall time; gate hold is measured only for outermost mutation scopes and represents serialized useful-work occupancy.<br/>
    /// Phase timings are nested within completed-record work and must not be added to gate hold as independent elapsed time.<br/>
    /// </summary>
    /// <param name="GateAcquisitionCount">Outermost store-generation mutation acquisitions.<br/></param>
    /// <param name="ContendedGateAcquisitionCount">Acquisitions that could not enter the mutation monitor immediately.<br/></param>
    /// <param name="MaximumQueuedWriters">Largest simultaneous set of callers waiting to acquire the mutation monitor.<br/></param>
    /// <param name="TotalGateWait">Summed outermost mutation-monitor wait time across callers.<br/></param>
    /// <param name="MaximumGateWait">Largest single outermost mutation-monitor wait.<br/></param>
    /// <param name="TotalGateHold">Summed outermost mutation-monitor ownership time.<br/></param>
    /// <param name="MaximumGateHold">Largest single outermost mutation-monitor ownership interval.<br/></param>
    /// <param name="CompletedRecordCount">Successfully completed record persistence operations measured by the session.<br/></param>
    /// <param name="RecordWorkTime">Summed elapsed time inside measured record persistence bodies.<br/></param>
    /// <param name="RecordAllocatedBytes">Thread-local managed allocation attributed to measured record persistence bodies.<br/></param>
    /// <param name="SerializationTime">Summed record preparation and Inheto serialization time.<br/></param>
    /// <param name="FunctionKeyPreparationTime">Summed function-index projection preparation time.<br/></param>
    /// <param name="FractalWriteTime">Summed authoritative Fractal write time.<br/></param>
    /// <param name="LibraDexMaintenanceTime">Summed maintained-index, State, and convergence-checkpoint time after Fractal persistence.<br/></param>
    /// <param name="FractalWriteCount">Authoritative Fractal put attempts measured by the session.<br/></param>
    /// <param name="MaintainedIndexTargetCount">Logical maintained-index targets visited across measured new-record upserts.<br/></param>
    /// <param name="ConvergenceCheckpointPublicationCount">Applied-source-USN checkpoint publication attempts after successful derived maintenance.<br/></param>
    /// <param name="CompatibleMutationAdmissionCount">Compatible physical mutations admitted without holding the exclusive store-wide lane.<br/></param>
    /// <param name="MaximumConcurrentCompatibleMutations">Largest simultaneously admitted compatible cohort membership.<br/></param>
    /// <param name="CompatibleMutationCohortCount">Number of compatible convergence cohorts established.<br/></param>
    /// <param name="CompatibleMutationRepairCount">Compatible cohorts finalized through authoritative repair instead of direct checkpoint publication.<br/></param>
    internal readonly record struct RecordStoreMutationDiagnostics(
        long GateAcquisitionCount,
        long ContendedGateAcquisitionCount,
        int MaximumQueuedWriters,
        TimeSpan TotalGateWait,
        TimeSpan MaximumGateWait,
        TimeSpan TotalGateHold,
        TimeSpan MaximumGateHold,
        long CompletedRecordCount,
        TimeSpan RecordWorkTime,
        long RecordAllocatedBytes,
        TimeSpan SerializationTime,
        TimeSpan FunctionKeyPreparationTime,
        TimeSpan FractalWriteTime,
        TimeSpan LibraDexMaintenanceTime,
        long FractalWriteCount,
        long MaintainedIndexTargetCount,
        long ConvergenceCheckpointPublicationCount,
        long CompatibleMutationAdmissionCount,
        int MaximumConcurrentCompatibleMutations,
        long CompatibleMutationCohortCount,
        long CompatibleMutationRepairCount);

    /// <summary>
    /// Accumulates one enabled mutation diagnostic interval using fixed interlocked counters only.<br/>
    /// The type intentionally stores Stopwatch ticks so the hot measurement path avoids TimeSpan construction and shared collection allocation.<br/>
    /// </summary>
    internal sealed class RecordStoreMutationDiagnosticsAccumulator
    {
        private long gateAcquisitionCount;
        private long contendedGateAcquisitionCount;
        private int queuedWriters;
        private int maximumQueuedWriters;
        private long totalGateWaitTicks;
        private long maximumGateWaitTicks;
        private long totalGateHoldTicks;
        private long maximumGateHoldTicks;
        private long completedRecordCount;
        private long recordWorkTicks;
        private long recordAllocatedBytes;
        private long serializationTicks;
        private long functionKeyPreparationTicks;
        private long fractalWriteTicks;
        private long libraDexMaintenanceTicks;
        private long fractalWriteCount;
        private long maintainedIndexTargetCount;
        private long convergenceCheckpointPublicationCount;
        private long compatibleMutationAdmissionCount;
        private int maximumConcurrentCompatibleMutations;
        private long compatibleMutationCohortCount;
        private long compatibleMutationRepairCount;

        /// <summary>
        /// Records one caller beginning an outermost mutation acquisition and updates queue high-water.<br/>
        /// </summary>
        internal void RecordGateRequestStarted()
        {
            int current = Interlocked.Increment(ref queuedWriters);
            UpdateMaximum(ref maximumQueuedWriters, current);
        }

        /// <summary>
        /// Records completion of one outermost mutation acquisition attempt.<br/>
        /// </summary>
        /// <param name="waitTicks">Stopwatch ticks between acquisition request and ownership.<br/></param>
        /// <param name="contended">Whether immediate monitor entry failed.<br/></param>
        internal void RecordGateEntered(long waitTicks, bool contended)
        {
            if (contended)
                Interlocked.Decrement(ref queuedWriters);
            Interlocked.Increment(ref gateAcquisitionCount);
            Interlocked.Add(ref totalGateWaitTicks, waitTicks);
            UpdateMaximum(ref maximumGateWaitTicks, waitTicks);
            if (contended)
                Interlocked.Increment(ref contendedGateAcquisitionCount);
        }

        /// <summary>
        /// Records one completed outermost mutation-monitor ownership interval.<br/>
        /// </summary>
        /// <param name="holdTicks">Stopwatch ticks spent owning the shared mutation monitor.<br/></param>
        internal void RecordGateHold(long holdTicks)
        {
            Interlocked.Add(ref totalGateHoldTicks, holdTicks);
            UpdateMaximum(ref maximumGateHoldTicks, holdTicks);
        }

        /// <summary>
        /// Records one record persistence body after its success or failure boundary is known.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Stopwatch ticks spent in the measured record body.<br/></param>
        /// <param name="allocatedBytes">Managed bytes allocated by the current thread during the record body.<br/></param>
        /// <param name="completed">Whether persistence and lifecycle notification completed successfully.<br/></param>
        internal void RecordRecordWork(long elapsedTicks, long allocatedBytes, bool completed)
        {
            Interlocked.Add(ref recordWorkTicks, elapsedTicks);
            Interlocked.Add(ref recordAllocatedBytes, Math.Max(0, allocatedBytes));
            if (completed)
                Interlocked.Increment(ref completedRecordCount);
        }

        /// <summary>
        /// Adds one record's preparation and serialization duration.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Measured Stopwatch ticks.<br/></param>
        internal void RecordSerialization(long elapsedTicks)
            => Interlocked.Add(ref serializationTicks, elapsedTicks);

        /// <summary>
        /// Adds one record's function-key preparation duration.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Measured Stopwatch ticks.<br/></param>
        internal void RecordFunctionKeyPreparation(long elapsedTicks)
            => Interlocked.Add(ref functionKeyPreparationTicks, elapsedTicks);

        /// <summary>
        /// Adds one authoritative Fractal put duration and attempt.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Measured Stopwatch ticks.<br/></param>
        internal void RecordFractalWrite(long elapsedTicks)
        {
            Interlocked.Add(ref fractalWriteTicks, elapsedTicks);
            Interlocked.Increment(ref fractalWriteCount);
        }

        /// <summary>
        /// Adds one post-Fractal LibraDex maintenance interval and its logical publication work.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Measured Stopwatch ticks.<br/></param>
        /// <param name="maintainedTargets">Logical derived-index targets visited by the upsert.<br/></param>
        /// <param name="checkpointPublications">Convergence-checkpoint publication attempts.<br/></param>
        internal void RecordLibraDexMaintenance(long elapsedTicks, int maintainedTargets, int checkpointPublications)
        {
            Interlocked.Add(ref libraDexMaintenanceTicks, elapsedTicks);
            Interlocked.Add(ref maintainedIndexTargetCount, maintainedTargets);
            Interlocked.Add(ref convergenceCheckpointPublicationCount, checkpointPublications);
        }

        /// <summary>
        /// Records one compatible mutation admission and raises the actual simultaneous-writer high-water.<br/>
        /// </summary>
        /// <param name="activeMutations">Compatible members admitted in the current cohort after this admission.<br/></param>
        /// <param name="startsCohort">Whether this admission established a new convergence cohort.<br/></param>
        internal void RecordCompatibleMutationAdmission(int activeMutations, bool startsCohort)
        {
            Interlocked.Increment(ref compatibleMutationAdmissionCount);
            UpdateMaximum(ref maximumConcurrentCompatibleMutations, activeMutations);
            if (startsCohort)
                Interlocked.Increment(ref compatibleMutationCohortCount);
        }

        /// <summary>
        /// Records one compatible cohort whose final member selected authoritative repair.<br/>
        /// </summary>
        internal void RecordCompatibleMutationRepair()
            => Interlocked.Increment(ref compatibleMutationRepairCount);

        /// <summary>
        /// Captures the current counters with independent atomic reads.<br/>
        /// The result is observational and may span slightly different instants when callers capture during active mutation.<br/>
        /// </summary>
        /// <returns>Fixed cumulative snapshot converted to TimeSpan values.<br/></returns>
        internal RecordStoreMutationDiagnostics Capture()
            => new(
                Interlocked.Read(ref gateAcquisitionCount),
                Interlocked.Read(ref contendedGateAcquisitionCount),
                Volatile.Read(ref maximumQueuedWriters),
                ToTimeSpan(Interlocked.Read(ref totalGateWaitTicks)),
                ToTimeSpan(Interlocked.Read(ref maximumGateWaitTicks)),
                ToTimeSpan(Interlocked.Read(ref totalGateHoldTicks)),
                ToTimeSpan(Interlocked.Read(ref maximumGateHoldTicks)),
                Interlocked.Read(ref completedRecordCount),
                ToTimeSpan(Interlocked.Read(ref recordWorkTicks)),
                Interlocked.Read(ref recordAllocatedBytes),
                ToTimeSpan(Interlocked.Read(ref serializationTicks)),
                ToTimeSpan(Interlocked.Read(ref functionKeyPreparationTicks)),
                ToTimeSpan(Interlocked.Read(ref fractalWriteTicks)),
                ToTimeSpan(Interlocked.Read(ref libraDexMaintenanceTicks)),
                Interlocked.Read(ref fractalWriteCount),
                Interlocked.Read(ref maintainedIndexTargetCount),
                Interlocked.Read(ref convergenceCheckpointPublicationCount),
                Interlocked.Read(ref compatibleMutationAdmissionCount),
                Volatile.Read(ref maximumConcurrentCompatibleMutations),
                Interlocked.Read(ref compatibleMutationCohortCount),
                Interlocked.Read(ref compatibleMutationRepairCount));

        /// <summary>
        /// Atomically raises one maximum counter without lowering a value published by another caller.<br/>
        /// </summary>
        /// <param name="target">Shared maximum counter.<br/></param>
        /// <param name="candidate">Candidate high-water value.<br/></param>
        private static void UpdateMaximum(ref int target, int candidate)
        {
            int current = Volatile.Read(ref target);
            while (candidate > current)
            {
                int observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }

        /// <summary>
        /// Atomically raises one timestamp maximum without lowering a value published by another caller.<br/>
        /// </summary>
        /// <param name="target">Shared maximum tick counter.<br/></param>
        /// <param name="candidate">Candidate Stopwatch ticks.<br/></param>
        private static void UpdateMaximum(ref long target, long candidate)
        {
            long current = Interlocked.Read(ref target);
            while (candidate > current)
            {
                long observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }

        /// <summary>
        /// Converts non-negative Stopwatch ticks to a TimeSpan without assuming Stopwatch uses TimeSpan ticks.<br/>
        /// </summary>
        /// <param name="stopwatchTicks">Elapsed Stopwatch ticks.<br/></param>
        /// <returns>Equivalent elapsed TimeSpan.<br/></returns>
        private static TimeSpan ToTimeSpan(long stopwatchTicks)
            => stopwatchTicks <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(stopwatchTicks / (double)Stopwatch.Frequency);
    }
}
