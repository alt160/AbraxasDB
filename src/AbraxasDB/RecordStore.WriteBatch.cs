using System.Diagnostics;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Accepts incrementally arriving records and publishes them through bounded, independently converged Abraxas checkpoints.<br/>
        /// Filling the configured record bound commits automatically; <see cref="Commit"/> commits a partial checkpoint without closing the facade, and <see cref="Complete"/> commits the final tail and closes it.<br/>
        /// This is a progressive write batch rather than a rollback transaction: records from successful checkpoints remain authoritative if a later checkpoint fails or the uncommitted tail is aborted.<br/>
        /// </summary>
        public sealed class WriteBatch : IDisposable
        {
            private readonly RecordStore<T> store;
            private readonly RecordWriteBatchOptions options;
            private readonly long? state;
            private readonly List<T> bufferedRecords;
            private readonly List<ulong>? retainedRecordIds;
            private long committedRecordCount;
            private int checkpointCount;
            private bool completed;
            private bool aborted;
            private bool faulted;
            private bool disposed;

            /// <summary>
            /// Creates one sequential, bounded write facade over the owning store.<br/>
            /// Construction performs no storage mutation; the first full buffer or explicit commit starts persistence.<br/>
            /// </summary>
            /// <param name="store">Store that owns every submitted record and generated identity.<br/></param>
            /// <param name="options">Validated buffering, worker, retention, and observation settings.<br/></param>
            /// <param name="state">Optional persistent State value applied to every record in this batch.<br/></param>
            internal WriteBatch(RecordStore<T> store, RecordWriteBatchOptions? options, long? state)
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                this.options = options ?? new RecordWriteBatchOptions();
                if (this.options.CheckpointRecordCount < 1 ||
                    this.options.CheckpointRecordCount > RecordWriteBatchOptions.MaximumCheckpointRecordCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        this.options.CheckpointRecordCount,
                        $"Write-batch checkpoint size must be between 1 and {RecordWriteBatchOptions.MaximumCheckpointRecordCount:N0} records.");
                }
                if (this.options.MaximumDegreeOfParallelism is <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        "Write-batch maximum degree of parallelism must be positive when supplied.");
                }

                this.state = state;
                bufferedRecords = new List<T>(this.options.CheckpointRecordCount);
                retainedRecordIds = this.options.RetainRecordIds ? new List<ulong>() : null;
            }

            /// <summary>Gets the maximum records prepared by one automatic checkpoint.<br/></summary>
            public int CheckpointRecordCount => options.CheckpointRecordCount;

            /// <summary>Gets the records currently retained in the not-yet-submitted tail.<br/></summary>
            public int BufferedRecordCount => bufferedRecords.Count;

            /// <summary>Gets the records belonging to successfully converged checkpoints.<br/></summary>
            public long CommittedRecordCount => committedRecordCount;

            /// <summary>Gets the number of nonempty checkpoints successfully published.<br/></summary>
            public int CheckpointCount => checkpointCount;

            /// <summary>Gets whether <see cref="Complete"/> successfully closed this facade.<br/></summary>
            public bool IsCompleted => completed;

            /// <summary>Gets whether the not-yet-submitted tail was explicitly or implicitly aborted.<br/></summary>
            public bool IsAborted => aborted;

            /// <summary>Gets whether a checkpoint threw and made continuation ambiguous.<br/></summary>
            public bool IsFaulted => faulted;

            /// <summary>
            /// Buffers one record and automatically commits when the configured checkpoint bound is reached.<br/>
            /// The method does not retain serialized Inheto payloads across checkpoints; only the caller's current record objects remain buffered until the next commit.<br/>
            /// </summary>
            /// <param name="record">Record to persist with a newly generated monotonic store identity.<br/></param>
            public void Put(T record)
            {
                EnsureCanWrite();
                if (record is null)
                    throw new ArgumentNullException(nameof(record));

                bufferedRecords.Add(record);
                if (bufferedRecords.Count >= options.CheckpointRecordCount)
                    _ = CommitCore();
            }

            /// <summary>
            /// Lazily consumes a record sequence through <see cref="Put"/> without materializing the complete enumerable.<br/>
            /// Successful automatic checkpoints remain committed if source enumeration later throws; disposal then discards only the buffered tail.<br/>
            /// </summary>
            /// <param name="records">Sequential source whose elements are requested only as bounded buffer capacity becomes available.<br/></param>
            /// <returns>The number of source records accepted before enumeration completed.<br/></returns>
            public long PutMany(IEnumerable<T> records)
            {
                ArgumentNullException.ThrowIfNull(records);
                EnsureCanWrite();
                long accepted = 0;
                foreach (T record in records)
                {
                    Put(record);
                    accepted = checked(accepted + 1);
                }

                return accepted;
            }

            /// <summary>
            /// Commits the current nonempty checkpoint through the store's automatic <c>PutManyFast</c> route and keeps this facade open.<br/>
            /// An empty commit performs no storage publication and returns an empty result without advancing the checkpoint ordinal.<br/>
            /// </summary>
            /// <returns>Checkpoint identities, cumulative committed count, and elapsed persistence time.<br/></returns>
            public RecordWriteBatchCheckpointResult Commit()
            {
                EnsureCanWrite();
                return CommitCore();
            }

            /// <summary>
            /// Commits the final buffered tail, marks the batch successfully complete, and returns cumulative results.<br/>
            /// A completed facade rejects further puts, commits, completion, or abort attempts; disposing it remains harmless.<br/>
            /// </summary>
            /// <returns>Total committed records, checkpoint count, and optionally retained identities in source order.<br/></returns>
            public RecordWriteBatchResult Complete()
            {
                EnsureCanWrite();
                if (bufferedRecords.Count != 0)
                    _ = CommitCore();

                completed = true;
                IReadOnlyList<ulong> identities = retainedRecordIds is null
                    ? Array.Empty<ulong>()
                    : retainedRecordIds.AsReadOnly();
                return new RecordWriteBatchResult(
                    committedRecordCount,
                    checkpointCount,
                    identities,
                    options.RetainRecordIds);
            }

            /// <summary>
            /// Discards the currently buffered, never-submitted tail and closes the facade while preserving every prior committed checkpoint.<br/>
            /// This operation is not transaction rollback and does not remove records already returned by an earlier commit.<br/>
            /// </summary>
            /// <returns>The number of buffered records discarded without submission.<br/></returns>
            public int Abort()
            {
                EnsureCanWrite();
                int discarded = bufferedRecords.Count;
                bufferedRecords.Clear();
                aborted = true;
                return discarded;
            }

            /// <summary>
            /// Closes an incomplete facade by discarding only its buffered tail.<br/>
            /// Disposal never publishes implicitly because exception unwinding must not convert an incomplete producer loop into a successful final checkpoint.<br/>
            /// </summary>
            public void Dispose()
            {
                if (disposed)
                    return;

                if (!completed && !aborted)
                {
                    bufferedRecords.Clear();
                    aborted = true;
                }

                disposed = true;
            }

            /// <summary>
            /// Publishes the current buffer through one complete Abraxas mutation and clears object references before more source records are requested.<br/>
            /// The existing <c>PutManyFast</c> implementation remains responsible for capability selection, identity allocation, serialization, Fractal mutation, LibraDex maintenance, repair, and convergence.<br/>
            /// </summary>
            /// <returns>The successful checkpoint result, or an empty no-publication result when no records were buffered.<br/></returns>
            private RecordWriteBatchCheckpointResult CommitCore()
            {
                if (bufferedRecords.Count == 0)
                {
                    return new RecordWriteBatchCheckpointResult(
                        checkpointCount,
                        0,
                        committedRecordCount,
                        Array.Empty<ulong>(),
                        TimeSpan.Zero,
                        false);
                }

                int nextCheckpoint = checked(checkpointCount + 1);
                long committedBefore = committedRecordCount;
                int recordCount = bufferedRecords.Count;
                var timer = Stopwatch.StartNew();
                try
                {
                    var bulkOptions = new PutManyFastOptions
                    {
                        MaximumDegreeOfParallelism = options.MaximumDegreeOfParallelism,
                        Progress = progress => PublishProgress(nextCheckpoint, committedBefore, progress)
                    };
                    PutManyFastResult result = store.PutManyFast(
                        bufferedRecords,
                        recordCount,
                        bulkOptions,
                        state);
                    using (result.IndexingBatch)
                    {
                        if (result.RecordIds.Count != recordCount)
                        {
                            throw new InvalidDataException(
                                $"Write-batch checkpoint {nextCheckpoint:N0} returned {result.RecordIds.Count:N0} identities for {recordCount:N0} submitted records.");
                        }

                        committedRecordCount = checked(committedRecordCount + recordCount);
                        checkpointCount = nextCheckpoint;
                        if (retainedRecordIds is not null)
                        {
                            for (int index = 0; index < result.RecordIds.Count; index++)
                                retainedRecordIds.Add(result.RecordIds[index]);
                        }

                        timer.Stop();
                        var checkpoint = new RecordWriteBatchCheckpointResult(
                            nextCheckpoint,
                            recordCount,
                            committedRecordCount,
                            result.RecordIds,
                            timer.Elapsed,
                            true);
                        bufferedRecords.Clear();
                        PublishCheckpoint(checkpoint);
                        return checkpoint;
                    }
                }
                catch
                {
                    timer.Stop();
                    faulted = true;
                    throw;
                }
            }

            /// <summary>
            /// Converts one checkpoint-local <c>PutManyFast</c> observation into cumulative write-batch coordinates.<br/>
            /// Observer exceptions are contained by the underlying diagnostic progress contract and cannot alter persistence.<br/>
            /// </summary>
            /// <param name="checkpointNumber">One-based checkpoint currently executing.<br/></param>
            /// <param name="committedBefore">Records converged before this checkpoint began.<br/></param>
            /// <param name="progress">Checkpoint-local physical progress supplied by Abraxas.<br/></param>
            private void PublishProgress(int checkpointNumber, long committedBefore, PutManyFastProgress progress)
            {
                options.Progress?.Invoke(new RecordWriteBatchProgress(
                    checkpointNumber,
                    committedBefore,
                    checked(committedBefore + progress.CompletedRecords),
                    progress));
            }

            /// <summary>
            /// Publishes one completed-checkpoint notification without allowing diagnostic callback failure to misreport a successful storage mutation.<br/>
            /// </summary>
            /// <param name="checkpoint">Successful checkpoint result to observe.<br/></param>
            private void PublishCheckpoint(RecordWriteBatchCheckpointResult checkpoint)
            {
                try
                {
                    options.CheckpointCompleted?.Invoke(checkpoint);
                }
                catch
                {
                    // Checkpoint observation is diagnostic and cannot undo an already converged mutation.
                }
            }

            /// <summary>
            /// Rejects writes after any terminal or faulted state so callers cannot accidentally continue an ambiguous producer sequence.<br/>
            /// </summary>
            private void EnsureCanWrite()
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(WriteBatch));
                if (completed)
                    throw new InvalidOperationException("The write batch has already completed.");
                if (aborted)
                    throw new InvalidOperationException("The write batch has already aborted.");
                if (faulted)
                    throw new InvalidOperationException("The write batch cannot continue after a failed checkpoint.");
            }
        }
    }

    /// <summary>
    /// Configures one progressive <c>RecordStore&lt;T&gt;</c> write batch while preserving automatic storage-route selection.<br/>
    /// Omit this type for a 2,048-record bounded default; specify only the resource, worker, identity-retention, or observation controls the caller needs.<br/>
    /// </summary>
    public sealed class RecordWriteBatchOptions
    {
        /// <summary>Default maximum records prepared by one checkpoint.<br/></summary>
        public const int DefaultCheckpointRecordCount = 2_048;

        /// <summary>Largest supported record bound for one checkpoint.<br/></summary>
        public const int MaximumCheckpointRecordCount = 1_000_000;

        /// <summary>
        /// Gets or initializes the positive record count that triggers an automatic continuing commit.<br/>
        /// The final checkpoint may contain fewer records when <c>Complete()</c> is called.<br/>
        /// </summary>
        public int CheckpointRecordCount { get; init; } = DefaultCheckpointRecordCount;

        /// <summary>
        /// Gets or initializes the maximum physical workers available inside each checkpoint.<br/>
        /// A value of one requests the coordinated single-worker path; <see langword="null"/> uses RecordBase configuration.<br/>
        /// </summary>
        public int? MaximumDegreeOfParallelism { get; init; }

        /// <summary>
        /// Gets or initializes whether the completed batch result retains all generated identities in source order.<br/>
        /// Disable this for very large streams when checkpoint callbacks or record identity setters already consume identities incrementally.<br/>
        /// </summary>
        public bool RetainRecordIds { get; init; } = true;

        /// <summary>
        /// Gets or initializes an optional observer receiving checkpoint-local physical progress plus cumulative record coordinates.<br/>
        /// Callback failures are diagnostic-only and cannot alter persistence.<br/>
        /// </summary>
        public Action<RecordWriteBatchProgress>? Progress { get; init; }

        /// <summary>
        /// Gets or initializes an optional observer invoked after each nonempty checkpoint has fully converged.<br/>
        /// Callback failures are ignored because the checkpoint is already authoritative when observation occurs.<br/>
        /// </summary>
        public Action<RecordWriteBatchCheckpointResult>? CheckpointCompleted { get; init; }
    }

    /// <summary>
    /// Reports one physical checkpoint in both checkpoint-local and cumulative write-batch coordinates.<br/>
    /// </summary>
    /// <param name="CheckpointNumber">One-based nonempty checkpoint ordinal.<br/></param>
    /// <param name="CommittedBeforeCheckpoint">Records converged before the checkpoint started.<br/></param>
    /// <param name="CumulativeCompletedRecords">Committed prefix plus the current phase's completed record-aligned units.<br/></param>
    /// <param name="CheckpointProgress">Underlying Abraxas route, phase, worker, and timing observation.<br/></param>
    public readonly record struct RecordWriteBatchProgress(
        int CheckpointNumber,
        long CommittedBeforeCheckpoint,
        long CumulativeCompletedRecords,
        PutManyFastProgress CheckpointProgress);

    /// <summary>
    /// Describes one explicit or automatic write-batch checkpoint after successful Fractal/LibraDex convergence.<br/>
    /// </summary>
    /// <param name="CheckpointNumber">One-based published checkpoint ordinal, or the current count for an empty no-op commit.<br/></param>
    /// <param name="RecordCount">Records submitted by this checkpoint.<br/></param>
    /// <param name="TotalCommittedRecords">Cumulative records committed by the facade.<br/></param>
    /// <param name="RecordIds">Generated identities for this checkpoint in source order.<br/></param>
    /// <param name="Elapsed">Elapsed checkpoint persistence time.<br/></param>
    /// <param name="Published">Whether this result represents a nonempty physical publication.<br/></param>
    public readonly record struct RecordWriteBatchCheckpointResult(
        int CheckpointNumber,
        int RecordCount,
        long TotalCommittedRecords,
        IReadOnlyList<ulong> RecordIds,
        TimeSpan Elapsed,
        bool Published);

    /// <summary>
    /// Describes a successfully completed progressive write batch.<br/>
    /// </summary>
    /// <param name="TotalCommittedRecords">Total records committed across every checkpoint.<br/></param>
    /// <param name="CheckpointCount">Number of nonempty checkpoints published.<br/></param>
    /// <param name="RecordIds">All retained generated identities in source order, or an empty list when retention was disabled.<br/></param>
    /// <param name="RecordIdsRetained">Whether cumulative identity retention was enabled.<br/></param>
    public readonly record struct RecordWriteBatchResult(
        long TotalCommittedRecords,
        int CheckpointCount,
        IReadOnlyList<ulong> RecordIds,
        bool RecordIdsRetained);
}
