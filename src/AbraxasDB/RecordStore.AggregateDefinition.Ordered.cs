using System.Linq.Expressions;
using System.Numerics;
using Inheto;
using LibraDex;

namespace AbraxasDB;

/// <summary>
/// Controls bounded-memory preparation for exact ordered aggregate reductions.<br/>
/// Values beyond <see cref="MemoryValueLimit"/> spill into a private file-backed LibraDex index which is removed with the aggregate reader state.<br/>
/// </summary>
public sealed class AggregateOrderedOptions
{
    /// <summary>Gets the maximum values retained in memory for each group before exact ordered state spills to disk.<br/></summary>
    public int MemoryValueLimit { get; init; } = 512;

    /// <summary>Gets an optional directory for disposable spill catalogs; null uses the operating-system temporary directory.<br/></summary>
    public string? SpillDirectory { get; init; }
}

public partial class RecordStore<T>
{
    public sealed partial class AggregateFacade
    {
        public sealed partial class AggregateDefinition<TKey>
        {
            /// <summary>Computes the exact median after bounded buffering and transparent file-backed LibraDex spill when required.<br/></summary>
            public AggregateDefinition<TKey, double?> AddMedian<TNumber>(Expression<Func<T, TNumber>> value, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(owner.CreatePercentileReducer<TKey, TNumber>(owner.CreateExpressionReader(value), 0.5d, options));

            /// <summary>Computes the exact median for one validated textual numeric path with bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, double?> AddMedian<TNumber>(string path, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(owner.CreatePercentileReducer<TKey, TNumber>(owner.CreatePathReader<TNumber>(path), 0.5d, options));

            /// <summary>Computes one exact linearly interpolated percentile in the inclusive range zero through one.<br/></summary>
            public AggregateDefinition<TKey, double?> AddPercentile<TNumber>(Expression<Func<T, TNumber>> value, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(owner.CreatePercentileReducer<TKey, TNumber>(owner.CreateExpressionReader(value), percentile, options));

            /// <summary>Computes one exact linearly interpolated percentile over a validated textual numeric path.<br/></summary>
            public AggregateDefinition<TKey, double?> AddPercentile<TNumber>(string path, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(owner.CreatePercentileReducer<TKey, TNumber>(owner.CreatePathReader<TNumber>(path), percentile, options));

            /// <summary>Returns the smallest value among those tied for greatest frequency, using bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, TValue?> AddMode<TValue>(Expression<Func<T, TValue>> value, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(owner.CreateModeReducer<TKey, TValue>(owner.CreateExpressionReader(value), options));

            /// <summary>Returns the smallest mode for one validated textual value path, or null when the group has no values.<br/></summary>
            public AggregateDefinition<TKey, TValue?> AddMode<TValue>(string path, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(owner.CreateModeReducer<TKey, TValue>(owner.CreatePathReader<TValue>(path), options));
        }

        public sealed partial class AggregateDefinition<TKey, T1>
        {
            /// <summary>Computes the exact median after bounded buffering and transparent file-backed LibraDex spill when required.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddMedian<TNumber>(Expression<Func<T, TNumber>> value, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), 0.5d, options));

            /// <summary>Computes the exact median for one validated textual numeric path with bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddMedian<TNumber>(string path, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), 0.5d, options));

            /// <summary>Computes one exact linearly interpolated percentile in the inclusive range zero through one.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddPercentile<TNumber>(Expression<Func<T, TNumber>> value, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), percentile, options));

            /// <summary>Computes one exact linearly interpolated percentile over a validated textual numeric path.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddPercentile<TNumber>(string path, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), percentile, options));

            /// <summary>Returns the smallest value among those tied for greatest frequency, using bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue?> AddMode<TValue>(Expression<Func<T, TValue>> value, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreateExpressionReader(value), options));

            /// <summary>Returns the smallest mode for one validated textual value path, or null when the group has no values.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue?> AddMode<TValue>(string path, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), options));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2>
        {
            /// <summary>Computes the exact median after bounded buffering and transparent file-backed LibraDex spill when required.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddMedian<TNumber>(Expression<Func<T, TNumber>> value, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), 0.5d, options));

            /// <summary>Computes the exact median for one validated textual numeric path with bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddMedian<TNumber>(string path, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), 0.5d, options));

            /// <summary>Computes one exact linearly interpolated percentile in the inclusive range zero through one.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddPercentile<TNumber>(Expression<Func<T, TNumber>> value, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), percentile, options));

            /// <summary>Computes one exact linearly interpolated percentile over a validated textual numeric path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddPercentile<TNumber>(string path, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), percentile, options));

            /// <summary>Returns the smallest value among those tied for greatest frequency, using bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue?> AddMode<TValue>(Expression<Func<T, TValue>> value, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreateExpressionReader(value), options));

            /// <summary>Returns the smallest mode for one validated textual value path, or null when the group has no values.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue?> AddMode<TValue>(string path, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), options));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3>
        {
            /// <summary>Computes the exact median after bounded buffering and transparent file-backed LibraDex spill when required.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddMedian<TNumber>(Expression<Func<T, TNumber>> value, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), 0.5d, options));

            /// <summary>Computes the exact median for one validated textual numeric path with bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddMedian<TNumber>(string path, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), 0.5d, options));

            /// <summary>Computes one exact linearly interpolated percentile in the inclusive range zero through one.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddPercentile<TNumber>(Expression<Func<T, TNumber>> value, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), percentile, options));

            /// <summary>Computes one exact linearly interpolated percentile over a validated textual numeric path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddPercentile<TNumber>(string path, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), percentile, options));

            /// <summary>Returns the smallest value among those tied for greatest frequency, using bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue?> AddMode<TValue>(Expression<Func<T, TValue>> value, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreateExpressionReader(value), options));

            /// <summary>Returns the smallest mode for one validated textual value path, or null when the group has no values.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue?> AddMode<TValue>(string path, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), options));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4>
        {
            /// <summary>Computes the exact median after bounded buffering and transparent file-backed LibraDex spill when required.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddMedian<TNumber>(Expression<Func<T, TNumber>> value, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), 0.5d, options));

            /// <summary>Computes the exact median for one validated textual numeric path with bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddMedian<TNumber>(string path, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), 0.5d, options));

            /// <summary>Computes one exact linearly interpolated percentile in the inclusive range zero through one.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddPercentile<TNumber>(Expression<Func<T, TNumber>> value, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), percentile, options));

            /// <summary>Computes one exact linearly interpolated percentile over a validated textual numeric path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddPercentile<TNumber>(string path, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), percentile, options));

            /// <summary>Returns the smallest value among those tied for greatest frequency, using bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue?> AddMode<TValue>(Expression<Func<T, TValue>> value, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreateExpressionReader(value), options));

            /// <summary>Returns the smallest mode for one validated textual value path, or null when the group has no values.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue?> AddMode<TValue>(string path, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), options));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4, T5>
        {
            /// <summary>Computes the exact median after bounded buffering and transparent file-backed LibraDex spill when required.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddMedian<TNumber>(Expression<Func<T, TNumber>> value, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), 0.5d, options));

            /// <summary>Computes the exact median for one validated textual numeric path with bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddMedian<TNumber>(string path, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), 0.5d, options));

            /// <summary>Computes one exact linearly interpolated percentile in the inclusive range zero through one.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddPercentile<TNumber>(Expression<Func<T, TNumber>> value, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), percentile, options));

            /// <summary>Computes one exact linearly interpolated percentile over a validated textual numeric path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddPercentile<TNumber>(string path, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), percentile, options));

            /// <summary>Returns the smallest value among those tied for greatest frequency, using bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue?> AddMode<TValue>(Expression<Func<T, TValue>> value, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreateExpressionReader(value), options));

            /// <summary>Returns the smallest mode for one validated textual value path, or null when the group has no values.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue?> AddMode<TValue>(string path, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), options));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6>
        {
            /// <summary>Computes the exact median after bounded buffering and transparent file-backed LibraDex spill when required.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddMedian<TNumber>(Expression<Func<T, TNumber>> value, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), 0.5d, options));

            /// <summary>Computes the exact median for one validated textual numeric path with bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddMedian<TNumber>(string path, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), 0.5d, options));

            /// <summary>Computes one exact linearly interpolated percentile in the inclusive range zero through one.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddPercentile<TNumber>(Expression<Func<T, TNumber>> value, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), percentile, options));

            /// <summary>Computes one exact linearly interpolated percentile over a validated textual numeric path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddPercentile<TNumber>(string path, double percentile, AggregateOrderedOptions? options = null)
                where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
                => Add(Owner.CreatePercentileReducer<TKey, TNumber>(Owner.CreatePathReader<TNumber>(path), percentile, options));

            /// <summary>Returns the smallest value among those tied for greatest frequency, using bounded buffering and transparent spill.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue?> AddMode<TValue>(Expression<Func<T, TValue>> value, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreateExpressionReader(value), options));

            /// <summary>Returns the smallest mode for one validated textual value path, or null when the group has no values.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue?> AddMode<TValue>(string path, AggregateOrderedOptions? options = null)
                where TValue : struct, IComparable<TValue>
                => Add(Owner.CreateModeReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), options));
        }

        private sealed class OrderedAggregateState<TValue> : IDisposable
            where TValue : struct, IComparable<TValue>
        {
            private const string SpillGroup = "aggregate";
            private const string SpillIndex = "values";
            private readonly int memoryValueLimit;
            private readonly string spillDirectory;
            private readonly List<TValue> memory;
            private Catalog? catalog;
            private CatalogIdentityGroupIndexes? group;
            private LibraDexIndex<TValue, ulong>? index;
            private string? spillPath;
            private ulong count;
            private bool disposed;

            /// <summary>Creates one per-group exact ordered accumulator with an immutable buffer/spill policy snapshot.<br/></summary>
            internal OrderedAggregateState(AggregateOrderedOptions? options)
            {
                int requestedLimit = options?.MemoryValueLimit ?? 512;
                if (requestedLimit < 0)
                    throw new ArgumentOutOfRangeException(nameof(options), "The ordered aggregate memory-value limit cannot be negative.");
                memoryValueLimit = requestedLimit;
                spillDirectory = string.IsNullOrWhiteSpace(options?.SpillDirectory)
                    ? Path.Combine(Path.GetTempPath(), "AbraxasDB", "Aggregates")
                    : Path.GetFullPath(options.SpillDirectory);
                memory = new List<TValue>(Math.Min(memoryValueLimit, 4096));
            }

            /// <summary>Gets the number of contributing values without enumerating retained or spilled keys.<br/></summary>
            internal ulong Count => count;

            /// <summary>Adds one value, promoting buffered state into a private ordered spill catalog only when the configured bound is crossed.<br/></summary>
            internal void Add(TValue value)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (index is null && memory.Count < memoryValueLimit)
                {
                    memory.Add(value);
                    count = checked(count + 1);
                    return;
                }

                EnsureSpill();
                index!.Add(value, count);
                count = checked(count + 1);
            }

            /// <summary>Enumerates all contributing values in ascending order without materializing spilled state back into managed memory.<br/></summary>
            internal IEnumerable<TValue> IterateOrdered()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (index is null)
                {
                    memory.Sort();
                    foreach (TValue value in memory)
                        yield return value;
                    yield break;
                }

                foreach (LibraDexRuntimeTuple tuple in group!.IterateTuples(index, QueryDirection.Ascending))
                    yield return (TValue)tuple.Key!;
            }

            /// <summary>Releases the private spill catalog and removes its disposable file; purely in-memory groups perform no I/O.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                index?.Dispose();
                catalog?.Dispose();
                if (spillPath is not null && File.Exists(spillPath))
                    File.Delete(spillPath);
            }

            /// <summary>Creates the private file-backed index once and transfers every buffered value before accepting more input.<br/></summary>
            private void EnsureSpill()
            {
                if (index is not null)
                    return;

                Directory.CreateDirectory(spillDirectory);
                spillPath = Path.Combine(spillDirectory, $"ordered-{Guid.NewGuid():N}.lbdx");
                try
                {
                    catalog = Catalog.Create(spillPath, CatalogOptions.UInt64Identities);
                    group = catalog.Indexes.IndexSet(SpillGroup);
                    index = group.Create<TValue, ulong>(SpillIndex, IndexKeys.NonUnique);
                    for (int i = 0; i < memory.Count; i++)
                        index.Add(memory[i], checked((ulong)i));
                    memory.Clear();
                }
                catch
                {
                    index?.Dispose();
                    catalog?.Dispose();
                    index = null;
                    catalog = null;
                    if (File.Exists(spillPath))
                        File.Delete(spillPath);
                    spillPath = null;
                    throw;
                }
            }
        }

        private AggregateReducer<TKey, OrderedAggregateState<TNumber>, double?> CreatePercentileReducer<TKey, TNumber>(
            PayloadReader<TNumber> read,
            double percentile,
            AggregateOrderedOptions? options)
            where TKey : notnull
            where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
        {
            if (double.IsNaN(percentile) || percentile < 0d || percentile > 1d)
                throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be a finite value from zero through one.");
            var snapshot = new AggregateOrderedOptions
            {
                MemoryValueLimit = options?.MemoryValueLimit ?? 512,
                SpillDirectory = options?.SpillDirectory
            };
            return new AggregateReducer<TKey, OrderedAggregateState<TNumber>, double?>(
                () => new OrderedAggregateState<TNumber>(snapshot),
                (ref OrderedAggregateState<TNumber> state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TNumber> value = read.Read(payload);
                    if (value.HasValue)
                        state.Add(value.Value);
                },
                (in OrderedAggregateState<TNumber> state, TKey _) => CompletePercentile(state, percentile),
                static state => state.Dispose(),
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TNumber>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref OrderedAggregateState<TNumber> state, TKey _, ulong identity) =>
                        {
                            ReadValue<TNumber> value = indexed(identity);
                            if (value.HasValue)
                                state.Add(value.Value);
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref OrderedAggregateState<TNumber> state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TNumber> value = ReadCompositeValue<TNumber>(values, ordinal);
                        if (value.HasValue)
                            state.Add(value.Value);
                    };
                });
        }

        private AggregateReducer<TKey, OrderedAggregateState<TValue>, TValue?> CreateModeReducer<TKey, TValue>(
            PayloadReader<TValue> read,
            AggregateOrderedOptions? options)
            where TKey : notnull
            where TValue : struct, IComparable<TValue>
        {
            var snapshot = new AggregateOrderedOptions
            {
                MemoryValueLimit = options?.MemoryValueLimit ?? 512,
                SpillDirectory = options?.SpillDirectory
            };
            return new AggregateReducer<TKey, OrderedAggregateState<TValue>, TValue?>(
                () => new OrderedAggregateState<TValue>(snapshot),
                (ref OrderedAggregateState<TValue> state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TValue> value = read.Read(payload);
                    if (value.HasValue)
                        state.Add(value.Value);
                },
                static (in OrderedAggregateState<TValue> state, TKey _) => CompleteMode(state),
                static state => state.Dispose(),
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref OrderedAggregateState<TValue> state, TKey _, ulong identity) =>
                        {
                            ReadValue<TValue> value = indexed(identity);
                            if (value.HasValue)
                                state.Add(value.Value);
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref OrderedAggregateState<TValue> state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TValue> value = ReadCompositeValue<TValue>(values, ordinal);
                        if (value.HasValue)
                            state.Add(value.Value);
                    };
                });
        }

        /// <summary>Reads only the two neighboring ordered positions required for an exact linearly interpolated percentile.<br/></summary>
        private static double? CompletePercentile<TNumber>(OrderedAggregateState<TNumber> state, double percentile)
            where TNumber : struct, INumber<TNumber>, IComparable<TNumber>
        {
            if (state.Count == 0)
                return null;

            double position = percentile * (state.Count - 1d);
            ulong lowerPosition = checked((ulong)Math.Floor(position));
            ulong upperPosition = checked((ulong)Math.Ceiling(position));
            TNumber lower = default;
            TNumber upper = default;
            ulong ordinal = 0;
            foreach (TNumber value in state.IterateOrdered())
            {
                if (ordinal == lowerPosition)
                    lower = value;
                if (ordinal == upperPosition)
                {
                    upper = value;
                    break;
                }
                ordinal++;
            }

            double lowerNumber = double.CreateChecked(lower);
            if (lowerPosition == upperPosition)
                return lowerNumber;
            double upperNumber = double.CreateChecked(upper);
            return lowerNumber + ((upperNumber - lowerNumber) * (position - lowerPosition));
        }

        /// <summary>Counts adjacent equal values in one ordered stream and keeps the smallest value on a frequency tie.<br/></summary>
        private static TValue? CompleteMode<TValue>(OrderedAggregateState<TValue> state)
            where TValue : struct, IComparable<TValue>
        {
            if (state.Count == 0)
                return null;

            bool assigned = false;
            TValue current = default;
            TValue mode = default;
            ulong currentCount = 0;
            ulong modeCount = 0;
            foreach (TValue value in state.IterateOrdered())
            {
                if (!assigned || value.CompareTo(current) != 0)
                {
                    assigned = true;
                    current = value;
                    currentCount = 1;
                }
                else
                {
                    currentCount = checked(currentCount + 1);
                }

                if (currentCount > modeCount)
                {
                    mode = current;
                    modeCount = currentCount;
                }
            }
            return mode;
        }
    }
}
