using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AbraxasDB
{
    /// <summary>
    /// Provides completion observation and detached progress snapshots for one indexing batch.<br/>
    /// Current synchronous bulk-write paths return a completed handle; the handle does not itself schedule storage work.<br/>
    /// The caller owns this pooled handle and must stop using it after disposal; retain a snapshot instead when results must outlive the handle.<br/>
    /// </summary>
    public sealed class IndexingBatchHandle : IDisposable, IAsyncDisposable
    {
        private static TaskCompletionSource<IndexingBatchSnapshot> CreateCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<IndexingBatchSnapshot> _completionSource = CreateCompletionSource();
        private readonly IndexingBatchHandlePool? _pool;
        private int _scheduledCount;
        private int _processedCount;
        private int _deadLetterCount;
        private readonly ConcurrentBag<ulong> _failedRecordIds = new();
        private long _createdUtcTicks;
        private long _completedUtcTicks;
        private int _disposeState;

        internal IndexingBatchHandle(IndexingBatchHandlePool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <summary>Gets the identifier assigned to this batch for the current handle lease.<br/></summary>
        public Guid BatchId { get; private set; }
        /// <summary>Gets the operation sequence number assigned by the owning RecordBase; it is not a persistent record identity.<br/></summary>
        public long OperationId { get; private set; }
        /// <summary>Gets whether completion observation is available; terminal dead-letter outcomes also count toward completion.<br/></summary>
        public bool IsCompleted => _completionSource.Task.IsCompleted;

        internal void Initialize(long operationId, int scheduledCount, bool completeImmediately)
        {
            if (scheduledCount < 0)
                throw new ArgumentOutOfRangeException(nameof(scheduledCount));

            OperationId = operationId;
            BatchId = Guid.NewGuid();
            _completionSource = CreateCompletionSource();
            _createdUtcTicks = DateTime.UtcNow.Ticks;
            Volatile.Write(ref _scheduledCount, scheduledCount);
            Volatile.Write(ref _processedCount, completeImmediately ? scheduledCount : 0);
            Volatile.Write(ref _deadLetterCount, 0);
            ClearFailed();
            Volatile.Write(ref _completedUtcTicks, completeImmediately ? _createdUtcTicks : 0);
            Volatile.Write(ref _disposeState, 0);

            if (completeImmediately)
                _completionSource.TrySetResult(BuildSnapshot());
        }

        /// <summary>
        /// Returns the final completion snapshot when available, otherwise copies the currently observed counters and failed identities.<br/>
        /// A live snapshot observes counters individually rather than locking the complete batch; it does not wait for completion.<br/>
        /// </summary>
        /// <returns>A detached batch summary that remains usable after this handle is disposed.<br/></returns>
        public IndexingBatchSnapshot GetSnapshot()
            => _completionSource.Task.IsCompleted ? _completionSource.Task.Result : BuildSnapshot();

        /// <summary>
        /// Waits for this batch's completion snapshot, returning immediately when completion is already available.<br/>
        /// Cancellation applies only to this wait and does not cancel the batch or its storage mutations.<br/>
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation of the caller's wait, not of the indexing operation.<br/></param>
        /// <returns>The final snapshot, including any terminal dead-letter outcomes.<br/></returns>
        /// <exception cref="OperationCanceledException">The wait was cancelled before completion was observed.<br/></exception>
        public ValueTask<IndexingBatchSnapshot> WaitAsync(CancellationToken cancellationToken = default)
        {
            if (!cancellationToken.CanBeCanceled)
                return new ValueTask<IndexingBatchSnapshot>(_completionSource.Task);

            return new ValueTask<IndexingBatchSnapshot>(_completionSource.Task.WaitAsync(cancellationToken));
        }

        private IndexingBatchSnapshot BuildSnapshot()
        {
            var scheduled = Volatile.Read(ref _scheduledCount);
            var processed = Volatile.Read(ref _processedCount);
            var deadLetters = Volatile.Read(ref _deadLetterCount);
            var createdTicks = Volatile.Read(ref _createdUtcTicks);
            var completedTicks = Volatile.Read(ref _completedUtcTicks);
            var failed = _failedRecordIds.ToArray();

            var created = createdTicks == 0 ? DateTime.MinValue : new DateTime(createdTicks, DateTimeKind.Utc);
            DateTime? completed = completedTicks == 0 ? null : new DateTime(completedTicks, DateTimeKind.Utc);
            int pending = Math.Max(0, scheduled - processed - deadLetters);
            double? percent = scheduled == 0 ? 100d : Math.Clamp((double)processed / scheduled * 100d, 0d, 100d);

            return new IndexingBatchSnapshot(
                BatchId,
                OperationId,
                scheduled,
                processed,
                pending,
                deadLetters,
                created,
                completed,
                percent,
                failed);
        }

        private void Reset()
        {
            BatchId = Guid.Empty;
            OperationId = 0;
            Volatile.Write(ref _scheduledCount, 0);
            Volatile.Write(ref _processedCount, 0);
            Volatile.Write(ref _deadLetterCount, 0);
            Volatile.Write(ref _createdUtcTicks, 0);
            Volatile.Write(ref _completedUtcTicks, 0);
            ClearFailed();
            _completionSource = CreateCompletionSource();
            Volatile.Write(ref _disposeState, 0);
        }

        private void ReturnToPool()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) == 1)
                return;

            if (_pool is null || !_completionSource.Task.IsCompleted)
                return;

            Reset();
            _pool.Return(this);
        }

        private void ClearFailed()
        {
            while (_failedRecordIds.TryTake(out _)) { }
        }

        /// <summary>
        /// Releases the handle lease, returning a completed handle to its pool.<br/>
        /// This does not cancel or wait for unfinished work; finish observation before disposing and do not access or dispose this handle again afterward.<br/>
        /// </summary>
        public void Dispose() => ReturnToPool();

        /// <summary>
        /// Releases the handle lease using the same non-waiting behavior as <see cref="Dispose()"/>.<br/>
        /// Asynchronous disposal does not await batch completion; use <see cref="WaitAsync(CancellationToken)"/> first when completion must be observed.<br/>
        /// Do not access or dispose this pooled handle again after release.<br/>
        /// </summary>
        /// <returns>An already completed disposal operation.<br/></returns>
        public ValueTask DisposeAsync()
        {
            ReturnToPool();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Contains detached counts, timestamps, and terminal failed identities observed for one indexing batch.<br/>
    /// Completion includes both successfully processed and dead-lettered records, whereas the percentage measures successful processing only.<br/>
    /// </summary>
    /// <param name="BatchId">Identifier of the observed batch.<br/></param>
    /// <param name="OperationId">Operation sequence number assigned by the owning RecordBase.<br/></param>
    /// <param name="ScheduledCount">Number of records scheduled for this batch.<br/></param>
    /// <param name="ProcessedCount">Number of successfully processed records reported by the batch.<br/></param>
    /// <param name="PendingCount">Remaining scheduled records after subtracting successful and dead-letter outcomes, bounded below by zero.<br/></param>
    /// <param name="DeadLetterCount">Number of records reported as terminal failures.<br/></param>
    /// <param name="CreatedUtc">UTC time when the batch handle was initialized.<br/></param>
    /// <param name="CompletedUtc">UTC time when completion was recorded, or null while completion has not been recorded.<br/></param>
    /// <param name="PercentComplete">Successful processed count divided by scheduled count, clamped to 0 through 100; an empty batch reports 100.<br/></param>
    /// <param name="FailedRecordIds">Detached list of dead-letter record identities; enumeration order is unspecified.<br/></param>
    public readonly record struct IndexingBatchSnapshot(
        Guid BatchId,
        long OperationId,
        int ScheduledCount,
        int ProcessedCount,
        int PendingCount,
        int DeadLetterCount,
        DateTime CreatedUtc,
        DateTime? CompletedUtc,
        double? PercentComplete,
        IReadOnlyList<ulong> FailedRecordIds);

    internal sealed class IndexingBatchHandlePool
    {
        private readonly ConcurrentBag<IndexingBatchHandle> _pool = new();

        public IndexingBatchHandle Rent()
            => _pool.TryTake(out var handle) ? handle : new IndexingBatchHandle(this);

        public void Return(IndexingBatchHandle handle)
            => _pool.Add(handle);
    }
}
