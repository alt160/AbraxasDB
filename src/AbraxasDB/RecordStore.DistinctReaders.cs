using Inheto;
using System.Linq.Expressions;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Provides a forward-only typed reader over unique projected values.<br/>
        /// Indexed sources suppress adjacent equal keys with constant retained state; unordered sources retain one typed structural set whose size is proportional to the unique output count.<br/>
        /// Sorted fallback preparation stores and sorts only unique projected values rather than every contributing record identity.<br/>
        /// </summary>
        /// <typeparam name="TResult">Projected scalar or structural tuple result shape.<br/></typeparam>
        public sealed class DistinctReader<TResult> : IDisposable
        {
            private readonly ReaderSourcePlan sourcePlan;
            private readonly Func<IDistinctValueCursor<TResult>>? openValues;
            private readonly Func<ReaderCore, TResult> projector;
            private readonly IEqualityComparer<TResult> equalityComparer;
            private readonly Comparison<TResult>? comparison;
            private readonly bool sourceOrdered;
            private ReaderCore? core;
            private IDistinctValueCursor<TResult>? values;
            private HashSet<TResult>? seen;
            private TResult[]? prepared;
            private TResult current = default!;
            private TResult prior = default!;
            private int preparedNext;
            private long ordinal = -1;
            private bool hasCurrent;
            private bool hasPrior;
            private bool disposed;

            /// <summary>
            /// Creates one distinct projection reader over a reusable logical source.<br/>
            /// The source is opened immediately but no row is decoded, deduplicated, or sorted until the reader is consumed or explicitly prepared.<br/>
            /// </summary>
            internal DistinctReader(
                ReaderSourcePlan sourcePlan,
                Func<ReaderCore, TResult> projector,
                IEqualityComparer<TResult> equalityComparer,
                bool sourceOrdered,
                Comparison<TResult>? comparison = null)
            {
                this.sourcePlan = sourcePlan ?? throw new ArgumentNullException(nameof(sourcePlan));
                this.projector = projector ?? throw new ArgumentNullException(nameof(projector));
                this.equalityComparer = equalityComparer ?? throw new ArgumentNullException(nameof(equalityComparer));
                this.sourceOrdered = sourceOrdered;
                this.comparison = comparison;
                core = sourcePlan.OpenInitial();
            }

            /// <summary>
            /// Creates one key-only distinct reader over an exact reversible LibraDex value source.<br/>
            /// The typed source bypasses identity validation and Fractal payload resolution while retaining ordinary distinct-reader reset and disposal ownership.<br/>
            /// </summary>
            internal DistinctReader(
                Func<IDistinctValueCursor<TResult>> openValues,
                IEqualityComparer<TResult> equalityComparer,
                Comparison<TResult>? comparison = null)
            {
                this.openValues = openValues ?? throw new ArgumentNullException(nameof(openValues));
                this.equalityComparer = equalityComparer ?? throw new ArgumentNullException(nameof(equalityComparer));
                this.comparison = comparison;
                sourceOrdered = true;
                sourcePlan = null!;
                projector = null!;
                values = openValues();
            }

            /// <summary>
            /// Gets the unique value selected by the most recent successful <see cref="Read"/> call.<br/>
            /// The value remains valid until the reader advances, resets, seeks, or is disposed.<br/>
            /// </summary>
            /// <returns>The current typed scalar or structural tuple result.<br/></returns>
            /// <exception cref="InvalidOperationException">The reader is not positioned on a value.<br/></exception>
            public TResult Get()
                => hasCurrent ? current : throw new InvalidOperationException("The distinct reader is not positioned on a value.");

            /// <summary>Gets the zero-based unique-output ordinal, or -1 before the first successful read.<br/></summary>
            public long Ordinal => ordinal;

            /// <summary>
            /// Gets whether a sorted fallback has completed its unique-value collection and ordering work.<br/>
            /// Natural-order readers and already ordered index sources require no preparation and therefore report <see langword="true"/> immediately.<br/>
            /// </summary>
            public bool IsPrepared => comparison is null || sourceOrdered || prepared is not null;

            /// <summary>
            /// Prepares a sorted fallback without advancing the public unique-value cursor.<br/>
            /// Exact ordered index sources are already streamable and return immediately; other sorted sources scan once into a typed structural set and sort only its unique values.<br/>
            /// </summary>
            public void Prepare() => Prepare(CancellationToken.None);

            /// <summary>
            /// Prepares a sorted fallback while cooperatively observing cancellation.<br/>
            /// A canceled preparation publishes no partial array; the reader is reset to its original source beginning so a later preparation can retry safely.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed during unique-value collection and immediately before sorting.<br/></param>
            public void Prepare(CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (comparison is null || sourceOrdered || prepared is not null)
                    return;

                var unique = new HashSet<TResult>(equalityComparer);
                try
                {
                    while (TryReadCandidate(out TResult candidate))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        unique.Add(candidate);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    var values = new TResult[unique.Count];
                    unique.CopyTo(values);
                    Array.Sort(values, comparison);
                    prepared = values;
                    preparedNext = 0;
                }
                catch
                {
                    ResetSource();
                    throw;
                }
            }

            /// <summary>
            /// Advances to the next unique projected value.<br/>
            /// Unordered scans retain a typed structural set; ordered sources compare only with the immediately prior key; sorted fallbacks prepare on first use.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when <see cref="Get()"/> is available; otherwise <see langword="false"/>.<br/></returns>
            public bool Read()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                hasCurrent = false;

                if (comparison is not null && !sourceOrdered)
                {
                    Prepare();
                    if ((uint)preparedNext >= (uint)prepared!.Length)
                        return false;
                    current = prepared[preparedNext++];
                    hasCurrent = true;
                    ordinal++;
                    return true;
                }

                if (sourceOrdered)
                {
                    while (TryReadCandidate(out TResult candidate))
                    {
                        if (hasPrior && equalityComparer.Equals(prior, candidate))
                            continue;
                        prior = candidate;
                        hasPrior = true;
                        current = candidate;
                        hasCurrent = true;
                        ordinal++;
                        return true;
                    }
                    return false;
                }

                seen ??= new HashSet<TResult>(equalityComparer);
                while (TryReadCandidate(out TResult candidate))
                {
                    if (!seen.Add(candidate))
                        continue;
                    current = candidate;
                    hasCurrent = true;
                    ordinal++;
                    return true;
                }
                return false;
            }

            /// <summary>Aliases <see cref="Read"/> for ordinary enumerator vocabulary.<br/></summary>
            /// <returns><see langword="true"/> when another unique value is available.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>
            /// Loads unique values into caller-owned storage without allocating a result collection.<br/>
            /// The reader advances once for each populated destination element.<br/>
            /// </summary>
            /// <param name="destination">Span receiving unique projected values.<br/></param>
            /// <returns>The number of elements populated before the span filled or the reader was exhausted.<br/></returns>
            public int Load(Span<TResult> destination)
            {
                int count = 0;
                while (count < destination.Length && Read())
                    destination[count++] = Get();
                return count;
            }

            /// <summary>
            /// Pulls at most the requested number of unique values into a newly allocated exact-length array.<br/>
            /// Zero results return the shared empty array; callers that own storage should prefer <see cref="Load"/>.<br/>
            /// </summary>
            /// <param name="count">Maximum number of unique values to retrieve; must not be negative.<br/></param>
            /// <returns>An exact-length array containing the retrieved values.<br/></returns>
            public TResult[] Pull(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0)
                    return Array.Empty<TResult>();

                var result = new TResult[count];
                int loaded = Load(result);
                if (loaded == count)
                    return result;
                if (loaded == 0)
                    return Array.Empty<TResult>();
                Array.Resize(ref result, loaded);
                return result;
            }

            /// <summary>
            /// Advances past at most the requested number of unique outputs.<br/>
            /// Deduplication still evaluates contributing rows because source-row skipping cannot determine how many unique outputs were bypassed.<br/>
            /// </summary>
            /// <param name="count">Maximum unique outputs to skip; must not be negative.<br/></param>
            /// <returns>The number of unique outputs skipped.<br/></returns>
            public int Skip(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                int skipped = 0;
                while (skipped < count && Read())
                    skipped++;
                hasCurrent = false;
                return skipped;
            }

            /// <summary>
            /// Repositions the reader to the beginning of the same distinct projection.<br/>
            /// A prepared sorted unique array is reused; streaming readers reopen their original source and clear only retained deduplication state.<br/>
            /// </summary>
            public void Reset()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                hasCurrent = false;
                current = default!;
                ordinal = -1;
                if (prepared is not null)
                {
                    preparedNext = 0;
                    return;
                }

                ResetSource();
                seen?.Clear();
                hasPrior = false;
                prior = default!;
            }

            /// <summary>Releases the underlying source cursor and retained distinct state.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                hasCurrent = false;
                current = default!;
                prior = default!;
                seen = null;
                prepared = null;
                values?.Dispose();
                core?.Dispose();
            }

            /// <summary>Reads one candidate from either the exact typed key cursor or the ordinary semantic row source.<br/></summary>
            private bool TryReadCandidate(out TResult candidate)
            {
                if (values is not null)
                    return values.Read(out candidate);
                if (core!.Read(requireBinary: false))
                {
                    candidate = projector(core);
                    return true;
                }
                candidate = default!;
                return false;
            }

            /// <summary>Reopens the active logical source without retaining state from its previous traversal.<br/></summary>
            private void ResetSource()
            {
                if (openValues is not null)
                {
                    IDistinctValueCursor<TResult>? previous = values;
                    values = null;
                    previous?.Dispose();
                    values = openValues();
                    return;
                }
                ReplaceCore(sourcePlan.OpenBeginning());
            }

            /// <summary>Replaces the source cursor without publishing partially reset state.<br/></summary>
            private void ReplaceCore(ReaderCore replacement)
            {
                ReaderCore? previous = core;
                core = replacement;
                previous?.Dispose();
            }
        }

        /// <summary>Defines the minimal owned typed-value movement required by an exact indexed distinct stream.<br/></summary>
        internal interface IDistinctValueCursor<TResult> : IDisposable
        {
            /// <summary>Advances to the next physical source value without resolving its associated record identity or payload.<br/></summary>
            bool Read(out TResult value);
        }

        /// <summary>Adapts one exact generic LibraDex reader to key-only distinct projection.<br/></summary>
        private sealed class LibraDexDistinctValueCursor<TValue> : IDistinctValueCursor<TValue?>
        {
            private readonly LibraDex.LibraDexRangeReader<TValue, ulong> reader;

            /// <summary>Owns the supplied LibraDex range reader until this cursor is disposed.<br/></summary>
            internal LibraDexDistinctValueCursor(LibraDex.LibraDexRangeReader<TValue, ulong> reader)
                => this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

            /// <summary>Advances and decodes only the next index key; associated identities are intentionally untouched.<br/></summary>
            public bool Read(out TValue? value)
            {
                if (reader.TryReadNextKey(out TValue key))
                {
                    value = key;
                    return true;
                }
                value = default;
                return false;
            }

            /// <summary>Releases the underlying coherent LibraDex range reader.<br/></summary>
            public void Dispose() => reader.Dispose();
        }

        /// <summary>
        /// Opens a typed distinct-value reader for one direct property path.<br/>
        /// An exact complete ordered index suppresses adjacent key runs without a set; otherwise the reader streams authoritative Fractal/Inheto values through a typed structural set.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctReader<TValue>(string propPath)
            => OpenDistinctPathReader<TValue>(CreateAllRowPlan(), propPath, sorted: false, default);

        /// <summary>
        /// Opens a typed distinct-value reader for one direct property path over records selected by a completed condition.<br/>
        /// The public shape is independent of index topology; filtered fallback retains only unique projected values rather than selected records.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctReader<TValue>(Condition<T>.EndCondition where, string propPath)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenDistinctPathReader<TValue>(CreateConditionRowPlan(where), propPath, sorted: false, default);
        }

        /// <summary>
        /// Opens a typed distinct-value reader from a direct property expression.<br/>
        /// The expression is reduced once to the same canonical path used by textual overloads.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctReader<TValue>(Expression<Func<T, TValue>> property)
        {
            ArgumentNullException.ThrowIfNull(property);
            return OpenDistinctReader<TValue>(PropPath.FromExpression(property).Path);
        }

        /// <summary>
        /// Opens a typed distinct-value reader for one reusable direct or calculated selector.<br/>
        /// Selector execution is compiled once before iteration; computed fallback values use a typed set and never pass through an <see cref="object"/> projection.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctReader<TValue>(Selector<T>.IValue<TValue> selector)
            => OpenDistinctSelectorReader(CreateAllRowPlan(), selector, sorted: false, descending: false);

        /// <summary>
        /// Opens a typed distinct-value reader for one reusable selector over a completed condition.<br/>
        /// The condition chooses the source population while the selector defines only the projected distinct value.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctReader<TValue>(Condition<T>.EndCondition where, Selector<T>.IValue<TValue> selector)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenDistinctSelectorReader(CreateConditionRowPlan(where), selector, sorted: false, descending: false);
        }

        /// <summary>
        /// Opens a typed distinct structural-pair reader for two direct property paths.<br/>
        /// The fallback key is a value tuple with component-aware structural equality, avoiding object arrays and numeric boxing.<br/>
        /// </summary>
        public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctReader<TValue1, TValue2>(string propPath1, string propPath2)
            => OpenDistinctPairPathReader<TValue1, TValue2>(CreateAllRowPlan(), propPath1, propPath2, sorted: false, default, default);

        /// <summary>
        /// Opens a typed distinct structural-pair reader over records selected by a completed condition.<br/>
        /// One payload visit projects both components and the retained set stores only unique value tuples.<br/>
        /// </summary>
        public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctReader<TValue1, TValue2>(Condition<T>.EndCondition where, string propPath1, string propPath2)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenDistinctPairPathReader<TValue1, TValue2>(CreateConditionRowPlan(where), propPath1, propPath2, sorted: false, default, default);
        }

        /// <summary>
        /// Opens a typed distinct structural-pair reader from two direct property expressions.<br/>
        /// Both expressions are normalized once and then evaluated from the same borrowed payload visit.<br/>
        /// </summary>
        public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctReader<TValue1, TValue2>(Expression<Func<T, TValue1>> property1, Expression<Func<T, TValue2>> property2)
        {
            ArgumentNullException.ThrowIfNull(property1);
            ArgumentNullException.ThrowIfNull(property2);
            return OpenDistinctReader<TValue1, TValue2>(PropPath.FromExpression(property1).Path, PropPath.FromExpression(property2).Path);
        }

        /// <summary>
        /// Opens a typed distinct structural-pair reader from two reusable direct or calculated selectors.<br/>
        /// Both selector evaluators are compiled once and the hot loop retains a typed tuple set rather than object containers.<br/>
        /// </summary>
        public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctReader<TValue1, TValue2>(Selector<T>.IValue<TValue1> selector1, Selector<T>.IValue<TValue2> selector2)
            => OpenDistinctPairSelectorReader(CreateAllRowPlan(), selector1, selector2);

        /// <summary>
        /// Opens a sorted typed distinct-value reader for one direct property path.<br/>
        /// Exact compatible indexes stream adjacent unique keys; fallback preparation sorts only the unique projected values.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctSortedReader<TValue>(SortBy value)
            => OpenDistinctPathReader<TValue>(CreateAllRowPlan(), value.Key.Path, sorted: true, value);

        /// <summary>
        /// Opens a sorted typed distinct-value reader over a completed condition.<br/>
        /// The first read or explicit preparation scans the selected population only when no exact compatible ordered source can be adopted.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctSortedReader<TValue>(Condition<T>.EndCondition where, SortBy value)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenDistinctPathReader<TValue>(CreateConditionRowPlan(where), value.Key.Path, sorted: true, value);
        }

        /// <summary>
        /// Opens a sorted typed distinct-value reader for one reusable direct or calculated selector.<br/>
        /// Fallback preparation retains and sorts only unique selector outputs.<br/>
        /// </summary>
        public DistinctReader<TValue?> OpenDistinctSortedReader<TValue>(Selector<T>.IValue<TValue> selector, bool descending = false)
            => OpenDistinctSelectorReader(CreateAllRowPlan(), selector, sorted: true, descending);

        /// <summary>
        /// Opens a lexicographically sorted distinct structural-pair reader.<br/>
        /// Each component retains its own direction and case-folding descriptor; fallback storage remains one typed tuple per unique output.<br/>
        /// </summary>
        public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctSortedReader<TValue1, TValue2>(SortBy value1, SortBy value2)
            => OpenDistinctPairPathReader<TValue1, TValue2>(CreateAllRowPlan(), value1.Key.Path, value2.Key.Path, sorted: true, value1, value2);

        /// <summary>
        /// Opens a lexicographically sorted distinct structural-pair reader over a completed condition.<br/>
        /// Preparation is deferred and stores only unique projected tuples when an exact ordered source cannot satisfy the request.<br/>
        /// </summary>
        public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctSortedReader<TValue1, TValue2>(Condition<T>.EndCondition where, SortBy value1, SortBy value2)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenDistinctPairPathReader<TValue1, TValue2>(CreateConditionRowPlan(where), value1.Key.Path, value2.Key.Path, sorted: true, value1, value2);
        }

        /// <summary>Creates one direct-path distinct reader and adopts an exact ordered index only when the complete source population and value semantics match.<br/></summary>
        private DistinctReader<TValue?> OpenDistinctPathReader<TValue>(ReaderSourcePlan sourcePlan, string propPath, bool sorted, SortBy sort)
        {
            string path = ValidateDistinctPath<TValue>(propPath);
            bool descending = sorted && sort.Descending;
            if (sourcePlan.IsCompleteStorePopulation &&
                TryCreateExactDistinctValueSource<TValue>(path, descending, out Func<IDistinctValueCursor<TValue?>>? openValues))
            {
                Comparison<TValue?>? indexedComparison = sorted
                    ? (left, right) => CompareDistinctValue(left, right, sort.IgnoreCase, sort.Descending)
                    : null;
                return new DistinctReader<TValue?>(
                    openValues!,
                    DistinctScalarComparer<TValue>.Instance,
                    indexedComparison);
            }

            ReaderSourcePlan selected = sourcePlan;
            bool ordered = false;
            SortBy requested = sorted ? sort : new SortBy(new PropPath(path));
            if (TryCreateExactIndexSortPlan(sourcePlan, new[] { requested }, out ReaderSourcePlan? exact))
            {
                selected = exact!;
                ordered = true;
            }

            IEqualityComparer<TValue?> comparer = DistinctScalarComparer<TValue>.Instance;
            Comparison<TValue?>? comparison = sorted
                ? (left, right) => CompareDistinctValue(left, right, sort.IgnoreCase, sort.Descending)
                : null;
            return new DistinctReader<TValue?>(
                selected,
                core => ReadDistinctPath<TValue>(core, path),
                comparer,
                ordered,
                comparison);
        }

        /// <summary>
        /// Finds one exact complete reversible scalar index and returns a replayable key-only cursor factory.<br/>
        /// Nullable, calculated, string-collation, partial-population, multi-key-per-identity, and nonmatching CLR shapes are rejected so fallback semantics remain authoritative.<br/>
        /// </summary>
        private bool TryCreateExactDistinctValueSource<TValue>(
            string canonicalPath,
            bool descending,
            out Func<IDistinctValueCursor<TValue?>>? openValues)
        {
            openValues = null;
            PropPathCheck path = InhetoBinary.CheckPropPath(typeof(T), canonicalPath);
            if (!path.IsValid || path.ResultType != typeof(TValue) || typeof(TValue) == typeof(string) || typeof(TValue) == typeof(DateTimeOffset))
                return false;

            LibraDexSimpleIndex[] candidates = libraDexSimpleIndexes;
            for (int i = 0; i < candidates.Length; i++)
            {
                LibraDexSimpleIndex candidate = candidates[i];
                if (!string.Equals(candidate.Path, canonicalPath, StringComparison.Ordinal) ||
                    candidate.CalculatedSelector is not null ||
                    !candidate.IncludeNullValues ||
                    !candidate.IncludeEmptyStrings ||
                    candidate.KeyType != typeof(TValue) ||
                    (candidate.Capabilities & IndexBackendCapabilities.OrderedRead) == 0 ||
                    candidate.Index.IdentityKeyMultiplicity != LibraDex.IdentityKeyMultiplicity.SingleKeyPerIdentity ||
                    candidate.Index is not LibraDex.LibraDexIndex<TValue, ulong> typedIndex)
                {
                    continue;
                }

                bool physicalDescending = candidate.Index.LogicalShape?.SortOrder == LibraDex.LibraDexIndexSortOrder.Descending;
                LibraDex.QueryDirection direction = physicalDescending == descending
                    ? LibraDex.QueryDirection.Ascending
                    : LibraDex.QueryDirection.Descending;
                openValues = () => new LibraDexDistinctValueCursor<TValue>(typedIndex.OpenReader(direction));
                return true;
            }
            return false;
        }

        /// <summary>Creates one selector-backed distinct reader after compiling its evaluator once outside the hot loop.<br/></summary>
        private DistinctReader<TValue?> OpenDistinctSelectorReader<TValue>(ReaderSourcePlan sourcePlan, Selector<T>.IValue<TValue> selector, bool sorted, bool descending)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (selector is ISelectorStructure { SimplePropertyPath: string path })
            {
                return OpenDistinctPathReader<TValue>(
                    sourcePlan,
                    path,
                    sorted,
                    new SortBy(new PropPath(path), descending: descending));
            }
            if (!TryCreateStoreSelectorReader<TValue>(selector, out var readValue))
                throw new NotSupportedException($"Selector result '{typeof(TValue).FullName}' does not have an executable Abraxas scalar reader.");

            Comparison<TValue?>? comparison = sorted
                ? (left, right) => CompareDistinctValue(left, right, ignoreCase: false, descending)
                : null;
            return new DistinctReader<TValue?>(
                sourcePlan,
                core =>
                {
                    (bool hasValue, TValue value) = readValue(core.Binary);
                    return hasValue ? value : default;
                },
                DistinctScalarComparer<TValue>.Instance,
                sourceOrdered: false,
                comparison);
        }

        /// <summary>Creates one direct-path structural-pair reader using a typed tuple comparer and unique-only sort preparation.<br/></summary>
        private DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctPairPathReader<TValue1, TValue2>(
            ReaderSourcePlan sourcePlan,
            string propPath1,
            string propPath2,
            bool sorted,
            SortBy sort1,
            SortBy sort2)
        {
            string path1 = ValidateDistinctPath<TValue1>(propPath1);
            string path2 = ValidateDistinctPath<TValue2>(propPath2);
            var comparer = DistinctPairComparer<TValue1, TValue2>.Instance;
            Comparison<(TValue1? Value1, TValue2? Value2)>? comparison = sorted
                ? (left, right) =>
                {
                    int result = CompareDistinctValue(left.Value1, right.Value1, sort1.IgnoreCase, sort1.Descending);
                    return result != 0
                        ? result
                        : CompareDistinctValue(left.Value2, right.Value2, sort2.IgnoreCase, sort2.Descending);
                }
                : null;
            return new DistinctReader<(TValue1? Value1, TValue2? Value2)>(
                sourcePlan,
                core => (ReadDistinctPath<TValue1>(core, path1), ReadDistinctPath<TValue2>(core, path2)),
                comparer,
                sourceOrdered: false,
                comparison);
        }

        /// <summary>Creates one calculated-selector structural-pair reader after compiling both evaluators once.<br/></summary>
        private DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctPairSelectorReader<TValue1, TValue2>(
            ReaderSourcePlan sourcePlan,
            Selector<T>.IValue<TValue1> selector1,
            Selector<T>.IValue<TValue2> selector2)
        {
            ArgumentNullException.ThrowIfNull(selector1);
            ArgumentNullException.ThrowIfNull(selector2);
            if (!TryCreateStoreSelectorReader<TValue1>(selector1, out var read1) ||
                !TryCreateStoreSelectorReader<TValue2>(selector2, out var read2))
                throw new NotSupportedException("One or more distinct selectors do not have an executable Abraxas scalar reader.");

            return new DistinctReader<(TValue1? Value1, TValue2? Value2)>(
                sourcePlan,
                core =>
                {
                    (bool has1, TValue1 value1) = read1(core.Binary);
                    (bool has2, TValue2 value2) = read2(core.Binary);
                    return (has1 ? value1 : default, has2 ? value2 : default);
                },
                DistinctPairComparer<TValue1, TValue2>.Instance,
                sourceOrdered: false);
        }

        /// <summary>Validates one direct property path and exact requested result type once before streaming starts.<br/></summary>
        private static string ValidateDistinctPath<TValue>(string propPath)
        {
            PropPathCheck check = InhetoBinary.CheckPropPath(typeof(T), propPath);
            if (!check.IsValid || check.ResultType is null)
                throw new ArgumentException($"Value path '{propPath}' is not valid for '{typeof(T).FullName}'.", nameof(propPath));
            Type actual = Nullable.GetUnderlyingType(check.ResultType) ?? check.ResultType;
            Type requested = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
            if (actual != requested)
                throw new InvalidCastException($"Value path '{check.CanonicalPath}' resolves to '{actual.FullName}', not requested '{requested.FullName}'.");
            return check.CanonicalPath;
        }

        /// <summary>Reads one direct value from an exact reversible index key when available, otherwise from authoritative Inheto bytes.<br/></summary>
        private TValue? ReadDistinctPath<TValue>(ReaderCore core, string canonicalPath)
        {
            if (core.TryProjectCurrentIndexValue(canonicalPath, out TValue? projected))
                return projected;
            return core.Binary.ReadPropAs<TValue>(canonicalPath, NativeDeserializationOptions);
        }

        /// <summary>Compares one typed scalar without boxing value types and preserves structural binary and optional text-case semantics.<br/></summary>
        private static int CompareDistinctValue<TValue>(TValue? left, TValue? right, bool ignoreCase, bool descending)
        {
            int result;
            if (left is null)
                result = right is null ? 0 : -1;
            else if (right is null)
                result = 1;
            else if (typeof(TValue) == typeof(byte[]))
                result = ((byte[])(object)left).AsSpan().SequenceCompareTo((byte[])(object)right);
            else if (typeof(TValue) == typeof(string))
                result = string.Compare((string)(object)left, (string)(object)right, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            else
                result = Comparer<TValue>.Default.Compare(left, right);
            return descending ? -result : result;
        }

        /// <summary>Provides typed scalar equality with structural byte-array handling and no value-type boxing.<br/></summary>
        private sealed class DistinctScalarComparer<TValue> : IEqualityComparer<TValue?>
        {
            internal static DistinctScalarComparer<TValue> Instance { get; } = new();

            public bool Equals(TValue? x, TValue? y)
            {
                if (typeof(TValue) == typeof(byte[]))
                {
                    if (x is null || y is null)
                        return x is null && y is null;
                    return ((byte[])(object)x).AsSpan().SequenceEqual((byte[])(object)y);
                }
                return EqualityComparer<TValue?>.Default.Equals(x, y);
            }

            public int GetHashCode(TValue? obj)
            {
                if (obj is null)
                    return 0;
                if (typeof(TValue) != typeof(byte[]))
                    return EqualityComparer<TValue?>.Default.GetHashCode(obj);
                var hash = new HashCode();
                hash.AddBytes((byte[])(object)obj);
                return hash.ToHashCode();
            }
        }

        /// <summary>Provides component-aware typed structural equality for pair projections without allocating object arrays.<br/></summary>
        private sealed class DistinctPairComparer<TValue1, TValue2> : IEqualityComparer<(TValue1? Value1, TValue2? Value2)>
        {
            internal static DistinctPairComparer<TValue1, TValue2> Instance { get; } = new();

            public bool Equals((TValue1? Value1, TValue2? Value2) x, (TValue1? Value1, TValue2? Value2) y)
                => DistinctScalarComparer<TValue1>.Instance.Equals(x.Value1, y.Value1) &&
                   DistinctScalarComparer<TValue2>.Instance.Equals(x.Value2, y.Value2);

            public int GetHashCode((TValue1? Value1, TValue2? Value2) obj)
                => HashCode.Combine(
                    DistinctScalarComparer<TValue1>.Instance.GetHashCode(obj.Value1),
                    DistinctScalarComparer<TValue2>.Instance.GetHashCode(obj.Value2));
        }
    }
}
