using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AbraxasDB;

public partial class RecordStore<T>
{
    public sealed partial class AggregateFacade
    {
        /// <summary>
        /// Opens a deferred sorted aggregate reader for a one-value result definition.<br/>
        /// Ordering expressions are compiled only when the reader is prepared explicitly or by its first consuming operation.<br/>
        /// </summary>
        /// <typeparam name="TKey">Grouping-key type retained by the definition.<br/></typeparam>
        /// <typeparam name="T1">Aggregate result type.<br/></typeparam>
        /// <param name="definition">Completed aggregate definition owned by this store.<br/></param>
        /// <param name="order">Ordering declaration in decreasing lexicographic priority.<br/></param>
        /// <param name="options">Optional in-memory run bound and spill directory.<br/></param>
        /// <returns>A typed unprepared reader over the completely ordered aggregate results.<br/></returns>
        public AggregateReader<T1> OpenSorted<TKey, T1>(
            AggregateDefinition<TKey, T1> definition,
            Action<AggregateOrder<T1>> order,
            AggregateSortOptions? options = null)
            where TKey : notnull
            => OpenSortedDefinition(definition, order, options);

        /// <summary>
        /// Opens a deferred sorted aggregate reader for a two-value tuple result definition.<br/>
        /// Ordering is applied to completed aggregate tuples rather than source records.<br/>
        /// </summary>
        /// <typeparam name="TKey">Grouping-key type retained by the definition.<br/></typeparam>
        /// <typeparam name="T1">First aggregate result type.<br/></typeparam>
        /// <typeparam name="T2">Second aggregate result type.<br/></typeparam>
        /// <param name="definition">Completed aggregate definition owned by this store.<br/></param>
        /// <param name="order">Ordering declaration in decreasing lexicographic priority.<br/></param>
        /// <param name="options">Optional in-memory run bound and spill directory.<br/></param>
        /// <returns>A typed unprepared reader over the completely ordered aggregate tuples.<br/></returns>
        public AggregateReader<(T1 Value1, T2 Value2)> OpenSorted<TKey, T1, T2>(
            AggregateDefinition<TKey, T1, T2> definition,
            Action<AggregateOrder<(T1 Value1, T2 Value2)>> order,
            AggregateSortOptions? options = null)
            where TKey : notnull
            => OpenSortedDefinition(definition, order, options);

        /// <summary>
        /// Opens a deferred sorted aggregate reader for a three-value tuple result definition.<br/>
        /// Preparation completes aggregation before establishing the requested result order.<br/>
        /// </summary>
        /// <typeparam name="TKey">Grouping-key type retained by the definition.<br/></typeparam>
        /// <typeparam name="T1">First aggregate result type.<br/></typeparam>
        /// <typeparam name="T2">Second aggregate result type.<br/></typeparam>
        /// <typeparam name="T3">Third aggregate result type.<br/></typeparam>
        /// <param name="definition">Completed aggregate definition owned by this store.<br/></param>
        /// <param name="order">Ordering declaration in decreasing lexicographic priority.<br/></param>
        /// <param name="options">Optional in-memory run bound and spill directory.<br/></param>
        /// <returns>A typed unprepared reader over the completely ordered aggregate tuples.<br/></returns>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3)> OpenSorted<TKey, T1, T2, T3>(
            AggregateDefinition<TKey, T1, T2, T3> definition,
            Action<AggregateOrder<(T1 Value1, T2 Value2, T3 Value3)>> order,
            AggregateSortOptions? options = null)
            where TKey : notnull
            => OpenSortedDefinition(definition, order, options);

        /// <summary>
        /// Opens a deferred sorted aggregate reader for a four-value tuple result definition.<br/>
        /// Value-only result tuples transparently use bounded external runs when their configured memory bound is crossed.<br/>
        /// </summary>
        /// <typeparam name="TKey">Grouping-key type retained by the definition.<br/></typeparam>
        /// <typeparam name="T1">First aggregate result type.<br/></typeparam>
        /// <typeparam name="T2">Second aggregate result type.<br/></typeparam>
        /// <typeparam name="T3">Third aggregate result type.<br/></typeparam>
        /// <typeparam name="T4">Fourth aggregate result type.<br/></typeparam>
        /// <param name="definition">Completed aggregate definition owned by this store.<br/></param>
        /// <param name="order">Ordering declaration in decreasing lexicographic priority.<br/></param>
        /// <param name="options">Optional in-memory run bound and spill directory.<br/></param>
        /// <returns>A typed unprepared reader over the completely ordered aggregate tuples.<br/></returns>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4)> OpenSorted<TKey, T1, T2, T3, T4>(
            AggregateDefinition<TKey, T1, T2, T3, T4> definition,
            Action<AggregateOrder<(T1 Value1, T2 Value2, T3 Value3, T4 Value4)>> order,
            AggregateSortOptions? options = null)
            where TKey : notnull
            => OpenSortedDefinition(definition, order, options);

        /// <summary>
        /// Opens a deferred sorted aggregate reader for a five-value tuple result definition.<br/>
        /// The returned reader retains the ordinary Prepare, Read, Load, and Pull consumption contract.<br/>
        /// </summary>
        /// <typeparam name="TKey">Grouping-key type retained by the definition.<br/></typeparam>
        /// <typeparam name="T1">First aggregate result type.<br/></typeparam>
        /// <typeparam name="T2">Second aggregate result type.<br/></typeparam>
        /// <typeparam name="T3">Third aggregate result type.<br/></typeparam>
        /// <typeparam name="T4">Fourth aggregate result type.<br/></typeparam>
        /// <typeparam name="T5">Fifth aggregate result type.<br/></typeparam>
        /// <param name="definition">Completed aggregate definition owned by this store.<br/></param>
        /// <param name="order">Ordering declaration in decreasing lexicographic priority.<br/></param>
        /// <param name="options">Optional in-memory run bound and spill directory.<br/></param>
        /// <returns>A typed unprepared reader over the completely ordered aggregate tuples.<br/></returns>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5)> OpenSorted<TKey, T1, T2, T3, T4, T5>(
            AggregateDefinition<TKey, T1, T2, T3, T4, T5> definition,
            Action<AggregateOrder<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5)>> order,
            AggregateSortOptions? options = null)
            where TKey : notnull
            => OpenSortedDefinition(definition, order, options);

        /// <summary>
        /// Opens a deferred sorted aggregate reader for a six-value tuple result definition.<br/>
        /// The complete result order is published atomically only after successful preparation.<br/>
        /// </summary>
        /// <typeparam name="TKey">Grouping-key type retained by the definition.<br/></typeparam>
        /// <typeparam name="T1">First aggregate result type.<br/></typeparam>
        /// <typeparam name="T2">Second aggregate result type.<br/></typeparam>
        /// <typeparam name="T3">Third aggregate result type.<br/></typeparam>
        /// <typeparam name="T4">Fourth aggregate result type.<br/></typeparam>
        /// <typeparam name="T5">Fifth aggregate result type.<br/></typeparam>
        /// <typeparam name="T6">Sixth aggregate result type.<br/></typeparam>
        /// <param name="definition">Completed aggregate definition owned by this store.<br/></param>
        /// <param name="order">Ordering declaration in decreasing lexicographic priority.<br/></param>
        /// <param name="options">Optional in-memory run bound and spill directory.<br/></param>
        /// <returns>A typed unprepared reader over the completely ordered aggregate tuples.<br/></returns>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6)> OpenSorted<TKey, T1, T2, T3, T4, T5, T6>(
            AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6> definition,
            Action<AggregateOrder<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6)>> order,
            AggregateSortOptions? options = null)
            where TKey : notnull
            => OpenSortedDefinition(definition, order, options);

        /// <summary>
        /// Opens a deferred sorted aggregate reader for a seven-value tuple result definition.<br/>
        /// Ordering remains strongly typed across the largest supported flat aggregate result shape.<br/>
        /// </summary>
        /// <typeparam name="TKey">Grouping-key type retained by the definition.<br/></typeparam>
        /// <typeparam name="T1">First aggregate result type.<br/></typeparam>
        /// <typeparam name="T2">Second aggregate result type.<br/></typeparam>
        /// <typeparam name="T3">Third aggregate result type.<br/></typeparam>
        /// <typeparam name="T4">Fourth aggregate result type.<br/></typeparam>
        /// <typeparam name="T5">Fifth aggregate result type.<br/></typeparam>
        /// <typeparam name="T6">Sixth aggregate result type.<br/></typeparam>
        /// <typeparam name="T7">Seventh aggregate result type.<br/></typeparam>
        /// <param name="definition">Completed aggregate definition owned by this store.<br/></param>
        /// <param name="order">Ordering declaration in decreasing lexicographic priority.<br/></param>
        /// <param name="options">Optional in-memory run bound and spill directory.<br/></param>
        /// <returns>A typed unprepared reader over the completely ordered aggregate tuples.<br/></returns>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6, T7 Value7)> OpenSorted<TKey, T1, T2, T3, T4, T5, T6, T7>(
            AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, T7> definition,
            Action<AggregateOrder<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6, T7 Value7)>> order,
            AggregateSortOptions? options = null)
            where TKey : notnull
            => OpenSortedDefinition(definition, order, options);

        /// <summary>Validates one aggregate result ordering and captures its deferred execution state.<br/></summary>
        private AggregateReader<TResult> OpenSortedDefinition<TKey, TResult>(
            AggregateDefinitionBase<TKey, TResult> definition,
            Action<AggregateOrder<TResult>> configure,
            AggregateSortOptions? options)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(configure);
            if (!ReferenceEquals(store, definition.Owner.store) || distinctValues != definition.Owner.distinctValues)
                throw new InvalidOperationException("An aggregate definition can only be opened by its owning store and multiplicity facade.");

            var order = new AggregateOrder<TResult>();
            configure(order);
            AggregateOrderPlan<TResult> plan = order.Capture();
            AggregateSortSettings settings = AggregateSortSettings.From(options);
            return new AggregateReader<TResult>(() => PrepareAggregateSort(definition.Plan.Prepare, plan, settings));
        }
    }

    /// <summary>Consumes one aggregate source and returns either an in-memory order or an external merge enumerator.<br/></summary>
    private static IEnumerator<TResult> PrepareAggregateSort<TResult>(
        Func<IEnumerator<TResult>> prepare,
        AggregateOrderPlan<TResult> order,
        AggregateSortSettings settings)
    {
        IComparer<TResult> comparer = order.CreateComparer();
        bool canSpill = typeof(TResult).IsValueType && !RuntimeHelpers.IsReferenceOrContainsReferences<TResult>();
        var values = new List<TResult>(Math.Min(settings.MemoryResultLimit, 4096));
        List<string>? runs = null;

        try
        {
            using IEnumerator<TResult> source = prepare();
            while (source.MoveNext())
            {
                values.Add(source.Current);
                if (!canSpill)
                    continue;
                if (runs is null && values.Count > settings.MemoryResultLimit)
                {
                    runs = new List<string>();
                    WriteAggregateRun(values, comparer, settings.SpillDirectory, runs);
                }
                else if (runs is not null && values.Count == settings.MemoryResultLimit)
                {
                    WriteAggregateRun(values, comparer, settings.SpillDirectory, runs);
                }
            }

            if (runs is null)
            {
                values.Sort(comparer);
                return values.GetEnumerator();
            }

            if (values.Count != 0)
                WriteAggregateRun(values, comparer, settings.SpillDirectory, runs);
            return new AggregateMergeEnumerator<TResult>(runs.ToArray(), comparer);
        }
        catch
        {
            if (runs is not null)
                DeleteAggregateRuns(runs);
            throw;
        }
    }

    /// <summary>Sorts and writes one fixed-size value-only run, then releases its managed row buffer.<br/></summary>
    private static void WriteAggregateRun<TResult>(
        List<TResult> values,
        IComparer<TResult> comparer,
        string directory,
        List<string> runs)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"aggregate-sort-{Guid.NewGuid():N}.run");
        try
        {
            values.Sort(comparer);
            int size = Unsafe.SizeOf<TResult>();
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan);
            byte[]? heap = size > 512 ? GC.AllocateUninitializedArray<byte>(size) : null;
            Span<byte> bytes = heap is null ? stackalloc byte[size] : heap;
            for (int i = 0; i < values.Count; i++)
            {
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(bytes), values[i]);
                stream.Write(bytes);
            }
            runs.Add(path);
            values.Clear();
        }
        catch
        {
            if (File.Exists(path))
                File.Delete(path);
            throw;
        }
    }

    /// <summary>Removes every disposable external-sort run after failure or reader disposal.<br/></summary>
    private static void DeleteAggregateRuns(IEnumerable<string> runs)
    {
        foreach (string path in runs)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>Owns one fixed-size sorted run and exposes exactly one current value to the merge heap.<br/></summary>
    private sealed class AggregateRunCursor<TResult> : IDisposable
    {
        private readonly FileStream stream;
        private readonly byte[] bytes;

        /// <summary>Opens one completed run for sequential reads.<br/></summary>
        internal AggregateRunCursor(string path, int ordinal)
        {
            Path = path;
            Ordinal = ordinal;
            bytes = GC.AllocateUninitializedArray<byte>(Unsafe.SizeOf<TResult>());
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        }

        /// <summary>Gets the disposable run path.<br/></summary>
        internal string Path { get; }

        /// <summary>Gets the deterministic run tie-break ordinal.<br/></summary>
        internal int Ordinal { get; }

        /// <summary>Gets the current typed result.<br/></summary>
        internal TResult Current { get; private set; } = default!;

        /// <summary>Reads the next exact fixed-size result, returning false only at a clean run boundary.<br/></summary>
        internal bool Read()
        {
            int read = stream.Read(bytes, 0, bytes.Length);
            if (read == 0)
            {
                Current = default!;
                return false;
            }
            if (read != bytes.Length)
                stream.ReadExactly(bytes.AsSpan(read));
            Current = Unsafe.ReadUnaligned<TResult>(ref bytes[0]);
            return true;
        }

        /// <summary>Closes the run stream; the merge owner removes the file after all streams close.<br/></summary>
        public void Dispose() => stream.Dispose();
    }

    /// <summary>Compares current run values and uses run ordinal only when every declared result key is equal.<br/></summary>
    private sealed class AggregateRunCursorComparer<TResult> : IComparer<AggregateRunCursor<TResult>>
    {
        private readonly IComparer<TResult> comparer;

        /// <summary>Captures the immutable typed result comparer.<br/></summary>
        internal AggregateRunCursorComparer(IComparer<TResult> comparer) => this.comparer = comparer;

        /// <summary>Provides the min-heap order used by the external merge.<br/></summary>
        public int Compare(AggregateRunCursor<TResult>? x, AggregateRunCursor<TResult>? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            int compared = comparer.Compare(x.Current, y.Current);
            return compared != 0 ? compared : x.Ordinal.CompareTo(y.Ordinal);
        }
    }

    /// <summary>Streams the exact merge of all sorted value-only runs and deletes every run on disposal.<br/></summary>
    private sealed class AggregateMergeEnumerator<TResult> : IEnumerator<TResult>
    {
        private readonly string[] paths;
        private readonly List<AggregateRunCursor<TResult>> runs;
        private readonly PriorityQueue<AggregateRunCursor<TResult>, AggregateRunCursor<TResult>> queue;
        private TResult current = default!;
        private bool disposed;

        /// <summary>Opens all run cursors and seeds one row per non-empty run into the merge heap.<br/></summary>
        internal AggregateMergeEnumerator(string[] paths, IComparer<TResult> comparer)
        {
            this.paths = paths;
            runs = new List<AggregateRunCursor<TResult>>(paths.Length);
            queue = new PriorityQueue<AggregateRunCursor<TResult>, AggregateRunCursor<TResult>>(
                paths.Length,
                new AggregateRunCursorComparer<TResult>(comparer));
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    var run = new AggregateRunCursor<TResult>(paths[i], i);
                    runs.Add(run);
                    if (run.Read())
                        queue.Enqueue(run, run);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Gets the most recently merged result.<br/></summary>
        public TResult Current => current;

        object System.Collections.IEnumerator.Current => Current!;

        /// <summary>Advances one result and replenishes only the run which supplied it.<br/></summary>
        public bool MoveNext()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!queue.TryDequeue(out AggregateRunCursor<TResult>? run, out _))
            {
                current = default!;
                return false;
            }
            current = run.Current;
            if (run.Read())
                queue.Enqueue(run, run);
            return true;
        }

        /// <summary>Closes all run streams and removes their disposable files, including after early reader disposal.<br/></summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            for (int i = 0; i < runs.Count; i++)
                runs[i].Dispose();
            DeleteAggregateRuns(paths);
        }

        void System.Collections.IEnumerator.Reset()
            => throw new NotSupportedException("Sorted aggregate result readers cannot be reset.");
    }

    /// <summary>Immutable preparation settings detached from caller option references.<br/></summary>
    private readonly record struct AggregateSortSettings(int MemoryResultLimit, string SpillDirectory)
    {
        /// <summary>Validates and captures one public option set.<br/></summary>
        internal static AggregateSortSettings From(AggregateSortOptions? options)
        {
            int limit = options?.MemoryResultLimit ?? AggregateSortOptions.DefaultMemoryResultLimit;
            if (limit < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Aggregate sort MemoryResultLimit must be at least one.");
            string directory = string.IsNullOrWhiteSpace(options?.SpillDirectory)
                ? Path.GetTempPath()
                : Path.GetFullPath(options.SpillDirectory);
            return new AggregateSortSettings(limit, directory);
        }
    }
}

/// <summary>
/// Configures bounded preparation for sorted aggregate-result readers.<br/>
/// Value-only structs and ValueTuples spill into private fixed-size merge runs after the memory bound; reference-containing results remain in memory so sorting never clones or changes object identity.<br/>
/// </summary>
public sealed class AggregateSortOptions
{
    /// <summary>Gets the default number of value-only aggregate results retained in one in-memory sort run.<br/></summary>
    public const int DefaultMemoryResultLimit = 4096;

    /// <summary>Gets the maximum value-only results retained in one run before external merge files are used.<br/></summary>
    public int MemoryResultLimit { get; init; } = DefaultMemoryResultLimit;

    /// <summary>Gets an optional directory for disposable aggregate-sort runs; null or empty uses the operating-system temporary directory.<br/></summary>
    public string? SpillDirectory { get; init; }
}

/// <summary>
/// Declares strongly typed aggregate-result ordering in decreasing lexicographic priority.<br/>
/// Each call appends one key; expressions are retained without compilation until the sorted reader prepares.<br/>
/// </summary>
/// <typeparam name="TResult">Complete aggregate result shape supplied by the definition.<br/></typeparam>
public sealed class AggregateOrder<TResult>
{
    private readonly List<Func<Comparison<TResult>>> components = new();

    /// <summary>
    /// Appends one ascending result key after every key already declared.<br/>
    /// Null and nullable values follow <see cref="Comparer{T}.Default"/> for the selected type.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact selected order-key type.<br/></typeparam>
    /// <param name="value">Typed aggregate-result expression evaluated during sorting.<br/></param>
    /// <returns>This declaration so additional tie-break keys can be appended.<br/></returns>
    public AggregateOrder<TResult> Ascending<TValue>(Expression<Func<TResult, TValue>> value)
        => Add(value, descending: false);

    /// <summary>
    /// Appends one descending result key after every key already declared.<br/>
    /// Descending comparison swaps operands rather than negating a comparison result, avoiding signed overflow.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact selected order-key type.<br/></typeparam>
    /// <param name="value">Typed aggregate-result expression evaluated during sorting.<br/></param>
    /// <returns>This declaration so additional tie-break keys can be appended.<br/></returns>
    public AggregateOrder<TResult> Descending<TValue>(Expression<Func<TResult, TValue>> value)
        => Add(value, descending: true);

    /// <summary>Captures an immutable set of deferred comparison factories.<br/></summary>
    internal AggregateOrderPlan<TResult> Capture()
    {
        if (components.Count == 0)
            throw new InvalidOperationException("A sorted aggregate reader requires at least one result-order expression.");
        return new AggregateOrderPlan<TResult>(components.ToArray());
    }

    /// <summary>Appends one deferred typed comparison without compiling its expression during OpenSorted.<br/></summary>
    private AggregateOrder<TResult> Add<TValue>(Expression<Func<TResult, TValue>> value, bool descending)
    {
        ArgumentNullException.ThrowIfNull(value);
        components.Add(() =>
        {
            Func<TResult, TValue> read = value.Compile();
            IComparer<TValue> comparer = Comparer<TValue>.Default;
            return descending
                ? (left, right) => comparer.Compare(read(right), read(left))
                : (left, right) => comparer.Compare(read(left), read(right));
        });
        return this;
    }
}

/// <summary>Owns immutable deferred comparison factories captured by one OpenSorted call.<br/></summary>
internal sealed class AggregateOrderPlan<TResult>
{
    private readonly Func<Comparison<TResult>>[] components;

    /// <summary>Captures caller-independent comparison factories.<br/></summary>
    internal AggregateOrderPlan(Func<Comparison<TResult>>[] components) => this.components = components;

    /// <summary>Compiles every ordering expression once when preparation begins.<br/></summary>
    internal IComparer<TResult> CreateComparer()
    {
        var comparisons = new Comparison<TResult>[components.Length];
        for (int i = 0; i < components.Length; i++)
            comparisons[i] = components[i]();
        return new AggregateResultComparer<TResult>(comparisons);
    }
}

/// <summary>Evaluates typed aggregate-result comparisons in declared priority order.<br/></summary>
internal sealed class AggregateResultComparer<TResult> : IComparer<TResult>
{
    private readonly Comparison<TResult>[] comparisons;

    /// <summary>Captures the compiled immutable comparison sequence.<br/></summary>
    internal AggregateResultComparer(Comparison<TResult>[] comparisons) => this.comparisons = comparisons;

    /// <summary>Returns the first non-equal declared key comparison.<br/></summary>
    public int Compare(TResult? x, TResult? y)
    {
        for (int i = 0; i < comparisons.Length; i++)
        {
            int compared = comparisons[i](x!, y!);
            if (compared != 0)
                return compared;
        }
        return 0;
    }
}
