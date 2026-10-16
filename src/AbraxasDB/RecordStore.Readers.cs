using System;
using System.Collections.Generic;
using FractalKVS;
using Inheto;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Opens the canonical allocation-conscious value reader over all records.<br/>
        /// Selected values are interpreted explicitly through <c>reader.Get(...).As...</c>; no row array or unrestricted target-type projection is created.<br/>
        /// </summary>
        /// <param name="valuePaths">Property, field, collection-key, collection-position, or admitted expression paths exposed by ordinal and canonical path.<br/></param>
        /// <returns>A value reader positioned before the first record.<br/></returns>
        public ValueDataReader OpenValuesReader(params string[] valuePaths)
        {
            string[] paths = ValidatePropPaths(valuePaths);
            ReaderCore core = paths.Length == 1 && TryCreateExactValueProjectionRowPlan(paths[0], out ReaderSourcePlan? indexedPlan)
                ? indexedPlan!.OpenInitial()
                : CreateAllReaderCore(CreateAllBookmarkContext());
            return new ValueDataReader(core, paths);
        }

        /// <summary>Opens the canonical allocation-conscious value reader for a completed condition.<br/></summary>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>A value reader positioned before the first matching record.<br/></returns>
        public ValueDataReader OpenValuesReader(Condition<T>.EndCondition where, params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(where);
            return new ValueDataReader(
                CreateConditionReaderCore(where, Array.Empty<SortBy>(), nameof(OpenValuesReader), CreateConditionBookmarkContext(where)),
                ValidatePropPaths(valuePaths));
        }

        /// <summary>Opens the canonical allocation-conscious value reader for a completed condition and explicit ordering.<br/></summary>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="sort">Property-path sort descriptors.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>A value reader positioned before the first matching record.<br/></returns>
        public ValueDataReader OpenValuesReader(Condition<T>.EndCondition where, SortBy[] sort, params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(sort);
            return new ValueDataReader(
                CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), CreateConditionBookmarkContext(where, sort)),
                ValidatePropPaths(valuePaths));
        }

        /// <summary>
        /// Opens the canonical value reader in the complete order of one exact developer-selected index.<br/>
        /// When a selected value path exactly matches the reversible index key, its <c>As...</c> accessor can reuse the already-current LibraDex key without resolving the Fractal payload.<br/>
        /// A named composite index similarly covers direct member paths represented by its separate key parts; an uncovered requested path opens the authoritative payload lazily.<br/>
        /// </summary>
        /// <param name="index">Exact store-owned index defining population and order.<br/></param>
        /// <param name="direction">Whole-index traversal direction.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>A value reader positioned before the first indexed row.<br/></returns>
        public ValueDataReader OpenValuesReader(RecordIndex index, RetrievalDirection direction, params string[] valuePaths)
            => new ValueDataReader(CreateIndexRowPlan(index, direction).OpenInitial(), ValidatePropPaths(valuePaths));

        /// <summary>Opens the canonical value reader in ascending order of one exact developer-selected index.<br/></summary>
        /// <param name="index">Exact store-owned index defining population and order.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>A value reader positioned before the first indexed row.<br/></returns>
        public ValueDataReader OpenValuesReader(RecordIndex index, params string[] valuePaths)
            => OpenValuesReader(index, RetrievalDirection.Ascending, valuePaths);

        /// <summary>Opens the canonical allocation-conscious value reader for persistent State and an optional mask.<br/></summary>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>A value reader positioned before the first matching record.<br/></returns>
        public ValueDataReader OpenValuesReader(long state, long? mask, params string[] valuePaths)
            => new ValueDataReader(
                CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)),
                ValidatePropPaths(valuePaths));

        /// <summary>Opens the canonical identity-aware value reader over all records.<br/></summary>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>An identity-aware value reader positioned before the first record.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(params string[] valuePaths)
        {
            string[] paths = ValidatePropPaths(valuePaths);
            ReaderCore core = paths.Length == 1 && TryCreateExactValueProjectionRowPlan(paths[0], out ReaderSourcePlan? indexedPlan)
                ? indexedPlan!.OpenInitial()
                : CreateAllReaderCore(CreateAllBookmarkContext());
            return new ValueDataReaderWithIds(core, paths);
        }

        /// <summary>Opens the canonical identity-aware value reader for a completed condition.<br/></summary>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>An identity-aware value reader positioned before the first matching record.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(Condition<T>.EndCondition where, params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(where);
            return new ValueDataReaderWithIds(
                CreateConditionReaderCore(where, Array.Empty<SortBy>(), nameof(OpenValuesReaderWithIds), CreateConditionBookmarkContext(where)),
                ValidatePropPaths(valuePaths));
        }

        /// <summary>Opens the canonical identity-aware value reader for a completed condition and explicit ordering.<br/></summary>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="sort">Property-path sort descriptors.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>An identity-aware value reader positioned before the first matching record.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(Condition<T>.EndCondition where, SortBy[] sort, params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(sort);
            return new ValueDataReaderWithIds(
                CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), CreateConditionBookmarkContext(where, sort)),
                ValidatePropPaths(valuePaths));
        }

        /// <summary>Opens the canonical identity-aware value reader in the complete order of one exact developer-selected index.<br/>
        /// Direct member paths stored as parts of a named composite index are read from its current key; uncovered paths retain lazy payload fallback.<br/></summary>
        /// <param name="index">Exact store-owned index defining population and order.<br/></param>
        /// <param name="direction">Whole-index traversal direction.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>An identity-aware value reader positioned before the first indexed row.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(RecordIndex index, RetrievalDirection direction, params string[] valuePaths)
            => new ValueDataReaderWithIds(CreateIndexRowPlan(index, direction).OpenInitial(), ValidatePropPaths(valuePaths));

        /// <summary>Opens the canonical identity-aware value reader in ascending order of one exact developer-selected index.<br/></summary>
        /// <param name="index">Exact store-owned index defining population and order.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>An identity-aware value reader positioned before the first indexed row.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(RecordIndex index, params string[] valuePaths)
            => OpenValuesReaderWithIds(index, RetrievalDirection.Ascending, valuePaths);

        /// <summary>Opens the canonical identity-aware value reader for persistent State and an optional mask.<br/></summary>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <param name="valuePaths">Selected value paths.<br/></param>
        /// <returns>An identity-aware value reader positioned before the first matching record.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(long state, long? mask, params string[] valuePaths)
            => new ValueDataReaderWithIds(
                CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)),
                ValidatePropPaths(valuePaths));

        /// <summary>
        /// Provides a forward-only Abraxas data reader whose current value has a compile-time result shape.<br/>
        /// The underlying Fractal payload is borrowed only during <see cref="Read"/>; <typeparamref name="TResult"/> must therefore own any data it retains after advancing.<br/>
        /// </summary>
        /// <typeparam name="TResult">Materialized current-value shape.<br/></typeparam>
        public sealed class DataReader<TResult> : IDisposable
        {
            private readonly RecordStore<T>? store;
            private readonly ReaderSourcePlan? sourcePlan;
            private ReaderCore core;
            private readonly Func<InhetoBinary, ulong, TResult> projector;
            private readonly Func<InhetoBinary, ulong, LinkReadPolicy, TResult>? noLinksProjector;
            private readonly LinkCollection? links;
            private readonly string resultShapeFingerprint;
            private TResult current = default!;
            private bool hasCurrent;

            /// <summary>
            /// Creates one typed facade over the shared reader engine.<br/>
            /// </summary>
            /// <param name="core">Reader engine that owns Fractal and operation lifetimes.<br/></param>
            /// <param name="projector">Per-row materializer executed against borrowed Inheto payload bytes.<br/></param>
            /// <param name="resultShapeFingerprint">Stable result-shape identity bound to continuation bookmarks; omission uses the default typed-value shape.<br/></param>
            /// <param name="noLinksProjector">Optional alternate object materializer that applies the selected link-suppression policy without following links.<br/></param>
            /// <param name="links">Optional owning store link registry required by object-reader NoLinks controls.<br/></param>
            internal DataReader(
                ReaderCore core,
                Func<InhetoBinary, ulong, TResult> projector,
                string? resultShapeFingerprint = null,
                Func<InhetoBinary, ulong, LinkReadPolicy, TResult>? noLinksProjector = null,
                LinkCollection? links = null)
            {
                this.core = core;
                this.projector = projector;
                this.noLinksProjector = noLinksProjector;
                this.links = links;
                this.resultShapeFingerprint = resultShapeFingerprint ??
                    BuildTypedValuesBookmarkShape(typeof(TResult), withIds: false, Array.Empty<string>());
                try
                {
                    core.BindResultShape(this.resultShapeFingerprint);
                }
                catch
                {
                    core.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Creates one typed facade over a reusable logical source plan.<br/>
            /// Reset, seek, fork, and sort all reopen the same semantic selection through the centralized reader planner.<br/>
            /// </summary>
            /// <param name="store">Owning record store.<br/></param>
            /// <param name="sourcePlan">Reusable logical source plan.<br/></param>
            /// <param name="projector">Per-row materializer executed against borrowed Inheto payload bytes.<br/></param>
            /// <param name="resultShapeFingerprint">Stable bookmark result-shape fingerprint.<br/></param>
            /// <param name="noLinksProjector">Optional alternate object materializer that applies the selected link-suppression policy without following links.<br/></param>
            internal DataReader(
                RecordStore<T> store,
                ReaderSourcePlan sourcePlan,
                Func<InhetoBinary, ulong, TResult> projector,
                string? resultShapeFingerprint = null,
                Func<InhetoBinary, ulong, LinkReadPolicy, TResult>? noLinksProjector = null)
                : this(sourcePlan.OpenInitial(), projector, resultShapeFingerprint, noLinksProjector, store.Links)
            {
                this.store = store;
                this.sourcePlan = sourcePlan;
            }

            /// <summary>Gets whether this reader can open independent cursors over its original logical selection.<br/></summary>
            public bool CanFork => sourcePlan?.CanReplay == true;

            /// <summary>Gets whether this reader can seek back to its original logical beginning.<br/></summary>
            public bool CanReset => CanFork;

            /// <summary>
            /// Gets the zero-based ordinal of the current row, or -1 before the first successful read.<br/>
            /// </summary>
            public long Ordinal => core.Ordinal;

            /// <summary>
            /// Gets an opaque continuation bookmark positioned at the next unread logical result.<br/>
            /// Reading this property does not advance or materialize the reader.<br/>
            /// </summary>
            public RecordBookmark Bookmark => core.Bookmark;

            /// <summary>
            /// Gets the materialized value at the current row.<br/>
            /// </summary>
            public TResult Current
                => hasCurrent ? current : throw new InvalidOperationException("The reader is not positioned on a row.");

            /// <summary>
            /// Gets the materialized value at the current row.<br/>
            /// This method matches the record-reader vocabulary while <see cref="Current"/> remains available for enumerator-style code.<br/>
            /// </summary>
            /// <returns>The current typed result without advancing the reader.<br/></returns>
            public TResult Get() => Current;

            /// <summary>
            /// Advances and materializes the next row.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when <see cref="Current"/> is available; otherwise <see langword="false"/>.<br/></returns>
            public bool Read()
            {
                hasCurrent = false;
                if (!core.Read(requireBinary: true))
                    return false;
                current = projector(core.Binary, core.ID);
                hasCurrent = true;
                return true;
            }

            /// <summary>
            /// Advances and materializes the next object result while suppressing every registered link.<br/>
            /// This control is available only on object readers; scalar value readers have no relationship hydration to suppress.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when <see cref="Current"/> is available; otherwise <see langword="false"/>.<br/></returns>
            public bool ReadNoLinks()
                => ReadNoLinks(RequireLinks().SuppressAll());

            /// <summary>
            /// Advances and materializes the next object result while suppressing only the named registered links.<br/>
            /// Unlisted links retain ordinary Inheto and application materializer behavior.<br/>
            /// </summary>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns><see langword="true"/> when <see cref="Current"/> is available; otherwise <see langword="false"/>.<br/></returns>
            public bool ReadNoLinks(string link, params string[] additionalLinks)
                => ReadNoLinks(RequireLinks().Suppress(link, additionalLinks));

            /// <summary>
            /// Advances and materializes the next row.<br/>
            /// This aliases <see cref="Read"/> for ordinary enumerator vocabulary.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when a current row is available.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>
            /// Advances past at most <paramref name="count"/> rows without materializing their result values.<br/>
            /// </summary>
            /// <param name="count">Maximum rows to skip; must not be negative.<br/></param>
            /// <returns>The number of rows actually skipped.<br/></returns>
            public int Skip(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0)
                    return 0;
                hasCurrent = false;
                return core.Skip(count);
            }

            /// <summary>
            /// Loads subsequent results into caller-owned storage.<br/>
            /// No collection is allocated; the reader advances once per populated element.<br/>
            /// </summary>
            /// <param name="destination">Span receiving materialized results.<br/></param>
            /// <returns>The number of destination elements populated before the span filled or the reader was exhausted.<br/></returns>
            public int Load(Span<TResult> destination)
            {
                int count = 0;
                while (count < destination.Length && Read())
                    destination[count++] = Current;
                return count;
            }

            /// <summary>
            /// Pulls at most <paramref name="count"/> subsequent results into a newly allocated exact-length array.<br/>
            /// A short final result is trimmed to its populated length; zero results return a shared empty array.<br/>
            /// </summary>
            /// <param name="count">Maximum number of results to retrieve; must not be negative.<br/></param>
            /// <returns>An exact-length array containing the retrieved results.<br/></returns>
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
            /// Reopens this reader's original logical selection at its beginning while preserving its projector and source optimizations.<br/>
            /// This is convenience vocabulary for <c>Seek(ReaderPosition.Beginning)</c>.<br/>
            /// </summary>
            public void Reset() => Seek(ReaderPosition.Beginning);

            /// <summary>Repositions this reader to the beginning of its reusable logical source.<br/></summary>
            /// <param name="position">Logical position to seek.<br/></param>
            public void Seek(ReaderPosition position)
            {
                if (position != ReaderPosition.Beginning)
                    throw new ArgumentOutOfRangeException(nameof(position));
                ReplaceCore(RequireReplayablePlan().OpenBeginning());
            }

            /// <summary>Repositions this reader through a compatible bookmark captured from the same selection and result shape.<br/></summary>
            /// <param name="bookmark">Opaque next-unread continuation bookmark.<br/></param>
            public void Seek(RecordBookmark bookmark)
                => ReplaceCore(RequireReplayablePlan().OpenBookmark(bookmark));

            /// <summary>Opens an independent typed reader over this reader's entire original selection without consuming the current cursor.<br/></summary>
            /// <returns>An independently disposable reader with the same projector and result shape.<br/></returns>
            public DataReader<TResult> Fork()
                => new(RequireStore(), RequireReplayablePlan(), projector, resultShapeFingerprint, noLinksProjector);

            /// <summary>Starts one independent unprepared sorted view over this reader's entire original selection.<br/></summary>
            /// <param name="sort">One or more exact property-path order descriptors.<br/></param>
            /// <returns>An independently disposable sorted row reader.<br/></returns>
            public SortedReader Sort(params SortBy[] sort)
            {
                ArgumentNullException.ThrowIfNull(sort);
                return RequireStore().OpenSortedReader(RequireReplayablePlan(), sort);
            }

            /// <summary>
            /// Starts an independently sorted row view from a direct or calculated typed selector while preserving this typed reader's independent cursor.<br/>
            /// The returned context performs no source walk until <see cref="SortedReader.Prepare()"/> or its first consuming operation establishes the complete stable order.<br/>
            /// </summary>
            /// <typeparam name="TOrder">Exact scalar ordering-key type inferred from the selector.<br/></typeparam>
            /// <param name="order">Direct or calculated durable selector defining the key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <returns>An independently disposable sorted row reader.<br/></returns>
            public SortedReader Sort<TOrder>(
                Selector<T>.IValue<TOrder> order,
                bool descending = false)
                => RequireStore().OpenSortedReader(RequireReplayablePlan(), order, descending);

            /// <summary>Starts one independent sorted view using durable direct or calculated selector semantics.<br/></summary>
            /// <param name="order">Primary direct or calculated selector including direction.<br/></param>
            /// <param name="thenBy">Optional lower-priority selector keys.<br/></param>
            /// <returns>An independently disposable sorted row reader.<br/></returns>
            public SortedReader Sort(IndexSelectorBuilder order, params IndexSelectorBuilder[] thenBy)
            {
                ArgumentNullException.ThrowIfNull(order);
                ArgumentNullException.ThrowIfNull(thenBy);
                var combined = new IndexSelectorBuilder[thenBy.Length + 1];
                combined[0] = order;
                Array.Copy(thenBy, 0, combined, 1, thenBy.Length);
                return RequireStore().OpenSortedReader(RequireReplayablePlan(), combined);
            }

            /// <summary>
            /// Starts one independent sorted view over this reader's original population using exact attached Abraxas indexes.<br/>
            /// The returned reader streams the primary index order directly for a complete-store source and intersects that order with this reader's replayable population for a subset source.<br/>
            /// This overload performs no payload-key reconstruction and preserves the mutation-converged completeness contract of each supplied index.<br/>
            /// </summary>
            /// <param name="order">Primary exact attached index and requested traversal direction.<br/></param>
            /// <param name="thenBy">Optional lower-priority exact attached indexes.<br/></param>
            /// <returns>An independently disposable exact-index sorted row reader.<br/></returns>
            public SortedReader Sort(IndexSort order, params IndexSort[] thenBy)
            {
                ArgumentNullException.ThrowIfNull(thenBy);
                var combined = new IndexSort[thenBy.Length + 1];
                combined[0] = order;
                Array.Copy(thenBy, 0, combined, 1, thenBy.Length);
                return RequireStore().OpenSortedReader(RequireReplayablePlan(), combined);
            }

            /// <summary>
            /// Releases the current Fractal record, identity source, and store-operation lease.<br/>
            /// </summary>
            public void Dispose()
            {
                hasCurrent = false;
                current = default!;
                core.Dispose();
            }

            /// <summary>Returns the reusable source plan or fails explicitly for a one-shot or legacy cursor.<br/></summary>
            private ReaderSourcePlan RequireReplayablePlan()
                => sourcePlan is { CanReplay: true }
                    ? sourcePlan
                    : throw new NotSupportedException("This reader source cannot be reset, forked, or independently sorted.");

            /// <summary>Returns the owning store required to reopen or transform the logical source.<br/></summary>
            private RecordStore<T> RequireStore()
                => store ?? throw new NotSupportedException("This legacy reader cursor does not retain a reusable store source.");

            /// <summary>Returns the link registry retained only by object readers.<br/></summary>
            private LinkCollection RequireLinks()
                => links ?? throw new NotSupportedException("NoLinks controls apply only to object readers.");

            /// <summary>Advances through the alternate link-suppressed object projector.<br/></summary>
            private bool ReadNoLinks(LinkReadPolicy policy)
            {
                Func<InhetoBinary, ulong, LinkReadPolicy, TResult> materializer = noLinksProjector ??
                    throw new NotSupportedException("NoLinks controls apply only to object readers.");
                hasCurrent = false;
                if (!core.Read(requireBinary: true))
                    return false;
                current = materializer(core.Binary, core.ID, policy);
                hasCurrent = true;
                return true;
            }

            /// <summary>Validates a replacement cursor before releasing the prior cursor so a failed seek leaves this reader usable.<br/></summary>
            /// <param name="replacement">Fresh source-specialized cursor.<br/></param>
            private void ReplaceCore(ReaderCore replacement)
            {
                ArgumentNullException.ThrowIfNull(replacement);
                try
                {
                    replacement.BindResultShape(resultShapeFingerprint);
                }
                catch
                {
                    replacement.Dispose();
                    throw;
                }

                ReaderCore previous = core;
                core = replacement;
                hasCurrent = false;
                current = default!;
                previous.Dispose();
            }
        }

        /// <summary>
        /// Provides allocation-conscious ordinal access to selected property paths on each current record.<br/>
        /// Unlike object projection, no row container or <see cref="object"/> array is created automatically.<br/>
        /// </summary>
        public class ValueDataReader : IDisposable
        {
            private readonly ReaderCore core;
            private readonly string[] paths;
            private readonly PropPathCheck[] pathPlans;

            /// <summary>
            /// Creates one dynamic property reader.<br/>
            /// </summary>
            /// <param name="core">Reader engine that owns record lifetime.<br/></param>
            /// <param name="paths">Validated property paths exposed by ordinal.<br/></param>
            internal ValueDataReader(ReaderCore core, string[] paths)
            {
                this.core = core;
                this.paths = paths;
                try
                {
                    pathPlans = PrepareValuePathPlans(paths);
                    core.BindResultShape(BuildDynamicValuesBookmarkShape(withIds: false, paths));
                }
                catch
                {
                    core.Dispose();
                    throw;
                }
            }

            /// <summary>Gets the number of selected property paths.<br/></summary>
            public int FieldCount => paths.Length;

            /// <summary>Gets the zero-based ordinal of the current row.<br/></summary>
            public long Ordinal => core.Ordinal;

            /// <summary>Gets an opaque continuation bookmark positioned at the next unread row.<br/></summary>
            public RecordBookmark Bookmark => core.Bookmark;

            /// <summary>Advances to the next row; its payload is opened only if a selected value is not covered by the current index key.<br/></summary>
            /// <returns><see langword="true"/> when property getters may be used.<br/></returns>
            public bool Read() => core.Read(requireBinary: false);

            /// <summary>Advances to the next row without resolving its payload until an uncovered value is requested.<br/></summary>
            /// <returns><see langword="true"/> when property getters may be used.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>
            /// Selects the current value at one zero-based ordinal for explicit typed interpretation through its <c>As...</c> members.<br/>
            /// The returned stack-only accessor does not allocate or decode the value until a typed member is requested.<br/>
            /// </summary>
            /// <param name="ordinal">Zero-based selected-value ordinal.<br/></param>
            /// <returns>An allocation-free current-row value accessor.<br/></returns>
            public ValueAccessor Get(int ordinal)
                => new(core, GetPathPlan(ordinal));

            /// <summary>
            /// Selects one configured current value by canonical property, field, collection-position, or collection-key path.<br/>
            /// The configured path list is scanned directly because dynamic value readers are intentionally bounded to small projection shapes and should not allocate a lookup dictionary.<br/>
            /// </summary>
            /// <param name="propPath">Configured Inheto value path; its leading dot and admitted parameterless-method parentheses are optional.<br/></param>
            /// <returns>An allocation-free current-row value accessor.<br/></returns>
            public ValueAccessor Get(string propPath)
            {
                PropPathCheck requested = InhetoBinary.CheckPropPath(typeof(T), propPath);
                if (!requested.IsValid)
                    throw new ArgumentException($"Value path '{propPath}' is not valid for '{typeof(T).FullName}'.", nameof(propPath));
                for (var i = 0; i < pathPlans.Length; i++)
                {
                    if (string.Equals(pathPlans[i].CanonicalPath, requested.CanonicalPath, StringComparison.Ordinal))
                        return new ValueAccessor(core, pathPlans[i]);
                }
                throw new KeyNotFoundException($"Value path '{requested.CanonicalPath}' was not selected when this reader was opened.");
            }

            /// <summary>
            /// Reads one selected current-row value using its exact CLR type without exposing an object-valued result.<br/>
            /// Indexed paths retain the existing covered-key shortcut; paths not covered by the active index use the ordinary lazy payload read.<br/>
            /// The non-generic <see cref="Get(string)"/> remains available for runtime-selected paths and named <c>As...</c> accessors.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact declared type of the selected value path.<br/></typeparam>
            /// <param name="propPath">A value path selected when this reader was opened.<br/></param>
            /// <returns>The typed value, or null/default when the member is null or missing.<br/></returns>
            public TValue? Get<TValue>(string propPath) => Get(propPath).As<TValue>();

            /// <summary>
            /// Borrows the selected stored string's natural UTF-8 payload without decoding or copying it.<br/>
            /// The returned span is valid only for the current row and must not be used after the next <see cref="Read"/>, <see cref="Skip"/>, or <see cref="Dispose"/> call.<br/>
            /// A present empty string returns an empty span; use <see cref="TryGetUtf8Span"/> when null or missing values are expected.<br/>
            /// UTF-8 byte offsets equal character offsets only when the caller's data contract guarantees compatible single-byte text, such as seven-bit ASCII.<br/>
            /// </summary>
            /// <param name="ordinal">Zero-based selected-property ordinal.<br/></param>
            /// <returns>The borrowed UTF-8 bytes for the current stored string.<br/></returns>
            /// <exception cref="InvalidOperationException">The reader is not positioned on a prepared row, or the selected property is null or missing.<br/></exception>
            /// <exception cref="InvalidCastException">The selected property is present but is not stored as a string.<br/></exception>
            public ReadOnlySpan<byte> GetUtf8Span(int ordinal)
            {
                string path = GetPath(ordinal);
                if (core.Binary.TryReadStringUtf8Payload(path, core.Options, out ReadOnlyMemory<byte> payload))
                    return payload.Span;

                throw new InvalidOperationException(
                    $"Property path '{path}' is null or missing in the current row.");
            }

            /// <summary>
            /// Attempts to borrow the selected stored string's natural UTF-8 payload without decoding or copying it.<br/>
            /// The returned span is valid only for the current row and must not be used after the next <see cref="Read"/>, <see cref="Skip"/>, or <see cref="Dispose"/> call.<br/>
            /// Null and missing values return <see langword="false"/>; a present empty string returns <see langword="true"/> with an empty span.<br/>
            /// UTF-8 byte offsets equal character offsets only when the caller's data contract guarantees compatible single-byte text, such as seven-bit ASCII.<br/>
            /// </summary>
            /// <param name="ordinal">Zero-based selected-property ordinal.<br/></param>
            /// <param name="value">Receives the borrowed UTF-8 bytes when the property contains a stored string.<br/></param>
            /// <returns><see langword="true"/> for a present string, including an empty string; otherwise <see langword="false"/>.<br/></returns>
            /// <exception cref="InvalidOperationException">The reader is not positioned on a prepared row.<br/></exception>
            /// <exception cref="InvalidCastException">The selected property is present but is not stored as a string.<br/></exception>
            public bool TryGetUtf8Span(int ordinal, out ReadOnlySpan<byte> value)
            {
                if (core.Binary.TryReadStringUtf8Payload(GetPath(ordinal), core.Options, out ReadOnlyMemory<byte> payload))
                {
                    value = payload.Span;
                    return true;
                }

                value = default;
                return false;
            }

            /// <summary>
            /// Reads the selected value using Inheto's runtime value shape.<br/>
            /// Prefer <see cref="Get(int)"/> and an explicit <c>As...</c> member when the caller knows the intended value family.<br/>
            /// </summary>
            /// <param name="ordinal">Zero-based selected-value ordinal.<br/></param>
            /// <returns>The runtime value, or <see langword="null"/> for a null or absent value.<br/></returns>
            public object? GetObject(int ordinal)
                => core.Binary.ReadProp(GetPath(ordinal), core.Options);

            /// <summary>Gets the normalized property path assigned to an ordinal.<br/></summary>
            /// <param name="ordinal">Zero-based selected-property ordinal.<br/></param>
            /// <returns>The configured property path.<br/></returns>
            public string GetPath(int ordinal)
                => (uint)ordinal < (uint)paths.Length ? paths[ordinal] : throw new ArgumentOutOfRangeException(nameof(ordinal));

            /// <summary>Gets the cached type-level path plan assigned to an ordinal.<br/></summary>
            private PropPathCheck GetPathPlan(int ordinal)
                => (uint)ordinal < (uint)pathPlans.Length ? pathPlans[ordinal] : throw new ArgumentOutOfRangeException(nameof(ordinal));

            /// <summary>Skips rows without decoding their Inheto payloads.<br/></summary>
            /// <param name="count">Maximum rows to skip.<br/></param>
            /// <returns>The number of rows skipped.<br/></returns>
            public int Skip(int count) => core.Skip(count);

            /// <summary>Releases the current record and reader resources.<br/></summary>
            public void Dispose() => core.Dispose();
        }

        /// <summary>
        /// Provides allocation-conscious ordinal property access while explicitly exposing each current record identity.<br/>
        /// </summary>
        public class ValueDataReaderWithIds : IDisposable
        {
            private readonly ReaderCore core;
            private readonly string[] paths;
            private readonly PropPathCheck[] pathPlans;

            /// <summary>Creates one identity-aware dynamic property reader.<br/></summary>
            /// <param name="core">Reader engine that owns record lifetime.<br/></param>
            /// <param name="paths">Validated property paths exposed by ordinal.<br/></param>
            internal ValueDataReaderWithIds(ReaderCore core, string[] paths)
            {
                this.core = core;
                this.paths = paths;
                try
                {
                    pathPlans = PrepareValuePathPlans(paths);
                    core.BindResultShape(BuildDynamicValuesBookmarkShape(withIds: true, paths));
                }
                catch
                {
                    core.Dispose();
                    throw;
                }
            }

            /// <summary>Gets the number of selected property paths.<br/></summary>
            public int FieldCount => paths.Length;

            /// <summary>Gets the zero-based ordinal of the current row.<br/></summary>
            public long Ordinal => core.Ordinal;

            /// <summary>Gets an opaque continuation bookmark positioned at the next unread row.<br/></summary>
            public RecordBookmark Bookmark => core.Bookmark;

            /// <summary>Gets the identity of the current row.<br/></summary>
            public ulong ID => core.ID;

            /// <summary>Advances to the next row; its payload is opened only if a selected value is not covered by the current index key.<br/></summary>
            /// <returns><see langword="true"/> when identity and property getters may be used.<br/></returns>
            public bool Read() => core.Read(requireBinary: false);

            /// <summary>Advances to the next row without resolving its payload until an uncovered value is requested.<br/></summary>
            /// <returns><see langword="true"/> when identity and property getters may be used.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>
            /// Selects the current value at one zero-based ordinal for explicit typed interpretation through its <c>As...</c> members.<br/>
            /// The returned stack-only accessor does not allocate or decode the value until a typed member is requested.<br/>
            /// </summary>
            /// <param name="ordinal">Zero-based selected-value ordinal.<br/></param>
            /// <returns>An allocation-free current-row value accessor.<br/></returns>
            public ValueAccessor Get(int ordinal)
                => new(core, GetPathPlan(ordinal));

            /// <summary>
            /// Selects one configured current value by canonical property, field, collection-position, or collection-key path.<br/>
            /// The configured path list is scanned directly to avoid allocating a lookup dictionary for small reader projections.<br/>
            /// </summary>
            /// <param name="propPath">Configured Inheto value path; its leading dot and admitted parameterless-method parentheses are optional.<br/></param>
            /// <returns>An allocation-free current-row value accessor.<br/></returns>
            public ValueAccessor Get(string propPath)
            {
                PropPathCheck requested = InhetoBinary.CheckPropPath(typeof(T), propPath);
                if (!requested.IsValid)
                    throw new ArgumentException($"Value path '{propPath}' is not valid for '{typeof(T).FullName}'.", nameof(propPath));
                for (var i = 0; i < pathPlans.Length; i++)
                {
                    if (string.Equals(pathPlans[i].CanonicalPath, requested.CanonicalPath, StringComparison.Ordinal))
                        return new ValueAccessor(core, pathPlans[i]);
                }
                throw new KeyNotFoundException($"Value path '{requested.CanonicalPath}' was not selected when this reader was opened.");
            }

            /// <summary>
            /// Reads one selected value as its exact CLR type without exposing an object-valued result.<br/>
            /// Direct parts of the selected index retain the covered-key shortcut; other paths use the ordinary lazy payload read.<br/>
            /// The non-generic <see cref="Get(string)"/> remains available for runtime-selected paths and named <c>As...</c> accessors.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact declared type of the selected value path.<br/></typeparam>
            /// <param name="propPath">A value path selected when this reader was opened.<br/></param>
            /// <returns>The typed value, or null/default when the member is null or missing.<br/></returns>
            public TValue? Get<TValue>(string propPath) => Get(propPath).As<TValue>();

            /// <summary>
            /// Borrows the selected stored string's natural UTF-8 payload without decoding or copying it.<br/>
            /// The returned span is valid only for the current row and must not be used after the next <see cref="Read"/>, <see cref="Skip"/>, or <see cref="Dispose"/> call.<br/>
            /// A present empty string returns an empty span; use <see cref="TryGetUtf8Span"/> when null or missing values are expected.<br/>
            /// UTF-8 byte offsets equal character offsets only when the caller's data contract guarantees compatible single-byte text, such as seven-bit ASCII.<br/>
            /// </summary>
            /// <param name="ordinal">Zero-based selected-property ordinal.<br/></param>
            /// <returns>The borrowed UTF-8 bytes for the current stored string.<br/></returns>
            /// <exception cref="InvalidOperationException">The reader is not positioned on a prepared row, or the selected property is null or missing.<br/></exception>
            /// <exception cref="InvalidCastException">The selected property is present but is not stored as a string.<br/></exception>
            public ReadOnlySpan<byte> GetUtf8Span(int ordinal)
            {
                string path = GetPath(ordinal);
                if (core.Binary.TryReadStringUtf8Payload(path, core.Options, out ReadOnlyMemory<byte> payload))
                    return payload.Span;

                throw new InvalidOperationException(
                    $"Property path '{path}' is null or missing in the current row.");
            }

            /// <summary>
            /// Attempts to borrow the selected stored string's natural UTF-8 payload without decoding or copying it.<br/>
            /// The returned span is valid only for the current row and must not be used after the next <see cref="Read"/>, <see cref="Skip"/>, or <see cref="Dispose"/> call.<br/>
            /// Null and missing values return <see langword="false"/>; a present empty string returns <see langword="true"/> with an empty span.<br/>
            /// UTF-8 byte offsets equal character offsets only when the caller's data contract guarantees compatible single-byte text, such as seven-bit ASCII.<br/>
            /// </summary>
            /// <param name="ordinal">Zero-based selected-property ordinal.<br/></param>
            /// <param name="value">Receives the borrowed UTF-8 bytes when the property contains a stored string.<br/></param>
            /// <returns><see langword="true"/> for a present string, including an empty string; otherwise <see langword="false"/>.<br/></returns>
            /// <exception cref="InvalidOperationException">The reader is not positioned on a prepared row.<br/></exception>
            /// <exception cref="InvalidCastException">The selected property is present but is not stored as a string.<br/></exception>
            public bool TryGetUtf8Span(int ordinal, out ReadOnlySpan<byte> value)
            {
                if (core.Binary.TryReadStringUtf8Payload(GetPath(ordinal), core.Options, out ReadOnlyMemory<byte> payload))
                {
                    value = payload.Span;
                    return true;
                }

                value = default;
                return false;
            }

            /// <summary>Reads the selected value using Inheto's runtime value shape.<br/></summary>
            /// <param name="ordinal">Zero-based selected-value ordinal.<br/></param>
            /// <returns>The runtime value, or <see langword="null"/>.<br/></returns>
            public object? GetObject(int ordinal)
                => core.Binary.ReadProp(GetPath(ordinal), core.Options);

            /// <summary>Gets the normalized property path assigned to an ordinal.<br/></summary>
            /// <param name="ordinal">Zero-based selected-property ordinal.<br/></param>
            /// <returns>The configured property path.<br/></returns>
            public string GetPath(int ordinal)
                => (uint)ordinal < (uint)paths.Length ? paths[ordinal] : throw new ArgumentOutOfRangeException(nameof(ordinal));

            /// <summary>Gets the cached type-level path plan assigned to an ordinal.<br/></summary>
            private PropPathCheck GetPathPlan(int ordinal)
                => (uint)ordinal < (uint)pathPlans.Length ? pathPlans[ordinal] : throw new ArgumentOutOfRangeException(nameof(ordinal));

            /// <summary>Skips rows without decoding their Inheto payloads.<br/></summary>
            /// <param name="count">Maximum rows to skip.<br/></param>
            /// <returns>The number of rows skipped.<br/></returns>
            public int Skip(int count) => core.Skip(count);

            /// <summary>Releases the current record and reader resources.<br/></summary>
            public void Dispose() => core.Dispose();
        }

        /// <summary>
        /// Materializes one reader row while preserving Abraxas materialization lifecycle events.<br/>
        /// </summary>
        private TOut MaterializeReaderObject<TOut>(
            InhetoBinary binary,
            ulong recordID,
            string operationName,
            LinkReadPolicy? suppressedLinks = null)
        {
            OnRecordMaterializing(recordID, typeof(TOut), operationName);
            Inheto.DeserializationOptions options = suppressedLinks is null
                ? NativeDeserializationOptions
                : suppressedLinks.Options(NativeDeserializationOptions);
            binary.SetMaterializationContext(recordID);
            TOut entity = binary.ToObject<TOut>(options)!;
            OverlayVirtualProperties(entity, binary, recordID);
            OnRecordMaterialized(recordID, typeof(TOut), entity, operationName);
            return entity;
        }

        /// <summary>
        /// Creates the direct Fractal group reader used for an unfiltered store walk.<br/>
        /// </summary>
        private ReaderCore CreateAllReaderCore(BookmarkReadContext? bookmarkContext = null)
        {
            BookmarkReadContext context = bookmarkContext ?? CreateAllBookmarkContext();
            return OpenReaderCore(new ReaderOpenRequest(ReaderSourceKind.All, context));
        }

        /// <summary>
        /// Creates a reader that resolves a prepared identity stream against authoritative Fractal payloads.<br/>
        /// </summary>
        private ReaderCore CreateIdentityReaderCore(
            IEnumerable<ulong> identities,
            BookmarkReadContext? bookmarkContext = null)
        {
            BookmarkReadContext context = bookmarkContext ?? CreateBookmarkContext(
                    "UnspecifiedIdentityStream",
                    sort: null,
                    bookmark: null,
                    BookmarkConsistency.GenerationBound,
                    unsupportedReason: "This internal identity source did not provide a durable selection identity.");
            return OpenReaderCore(new ReaderOpenRequest(ReaderSourceKind.Identities, context, identities));
        }

        /// <summary>
        /// Creates an unsorted condition reader that reuses a payload already read for residual evaluation while preserving zero-copy fetches for complete direct-LibraDex plans.<br/>
        /// Sorted conditions retain the established identity-ordering pipeline because ordering must complete before row payloads can be exposed.<br/>
        /// </summary>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="sort">Optional sort descriptors.<br/></param>
        /// <param name="operationName">Public operation name used by condition lifecycle diagnostics.<br/></param>
        /// <param name="bookmarkContext">Optional prevalidated semantic bookmark context; omission creates one from the condition and sort descriptors.<br/></param>
        /// <returns>A reader core positioned before the first selected row.<br/></returns>
        private ReaderCore CreateConditionReaderCore(
            Condition<T>.EndCondition where,
            SortBy[] sort,
            string operationName,
            BookmarkReadContext? bookmarkContext = null)
        {
            ArgumentNullException.ThrowIfNull(where);
            bookmarkContext ??= CreateConditionBookmarkContext(where, sort);
            return OpenReaderCore(new ReaderOpenRequest(
                ReaderSourceKind.Condition,
                bookmarkContext,
                condition: where,
                sort: sort,
                operationName: operationName));
        }

        /// <summary>
        /// Validates and clones dynamic property paths once per reader so caller mutation cannot change an active read shape.<br/>
        /// </summary>
        private static string[] ValidatePropPaths(string[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(propPaths);
            if (propPaths.Length == 0)
                throw new ArgumentException("At least one property path is required.", nameof(propPaths));
            string[] copy = (string[])propPaths.Clone();
            for (int i = 0; i < copy.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(copy[i]))
                    throw new ArgumentException($"Property path at ordinal {i} is empty.", nameof(propPaths));
            }
            return copy;
        }

        /// <summary>
        /// Resolves and canonicalizes every selected value path once when a reader is opened.<br/>
        /// Invalid members, blocked method calls, and unresolved expression remainders fail before the first row is consumed.<br/>
        /// The supplied array is already an internal clone, so replacing its entries cannot mutate caller-owned input.<br/>
        /// </summary>
        /// <param name="paths">Validated internal path array to canonicalize in place.<br/></param>
        /// <returns>Cached Inheto path plans in ordinal order.<br/></returns>
        private static PropPathCheck[] PrepareValuePathPlans(string[] paths)
        {
            var plans = new PropPathCheck[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                PropPathCheck plan = InhetoBinary.CheckPropPath(typeof(T), paths[i]);
                if (!plan.IsValid)
                {
                    string segment = string.IsNullOrEmpty(plan.FailedSegment)
                        ? string.Empty
                        : $" at segment '{plan.FailedSegment}'";
                    throw new ArgumentException(
                        $"Value path '{paths[i]}' is not resolvable for '{typeof(T).FullName}' ({plan.FailureKind}{segment}).",
                        nameof(paths));
                }

                paths[i] = plan.CanonicalPath;
                plans[i] = plan;
            }
            return plans;
        }

        /// <summary>
        /// Adapts one reader to familiar lazy enumeration while keeping the reader as the sole execution primitive.<br/>
        /// The iterator owns and disposes the reader when enumeration completes, fails, or is abandoned early.<br/>
        /// </summary>
        /// <typeparam name="TResult">Materialized row shape.<br/></typeparam>
        /// <param name="reader">Prepared reader to consume.<br/></param>
        /// <returns>A lazy enumeration of reader values.<br/></returns>
        private static IEnumerable<TResult> IterateReader<TResult>(DataReader<TResult> reader)
        {
            using (reader)
            {
                while (reader.Read())
                    yield return reader.Current;
            }
        }

        /// <summary>
        /// Owns one complete store-operation lease and either a direct Fractal group reader or an identity enumerator plus current Fractal record.<br/>
        /// </summary>
        internal sealed class ReaderCore : IDisposable
        {
            private readonly RecordStore<T> store;
            private readonly IDisposable operation;
            private readonly FractalStore.GroupRecordReader? groupReader;
            private readonly FractalStore.GroupBulkRecordReader? unorderedReader;
            private readonly IDisposable? unorderedSnapshot;
            private readonly BufferStream? unorderedBinaryStream;
            private readonly InhetoBinary? reusableUnorderedBinary;
            private readonly IIdentitySourceCursor? identityCursor;
            private readonly bool trustIdentityExistence;
            private readonly IEnumerator<ulong>? identities;
            private readonly IEnumerator<PlannedConditionMatch>? plannedMatches;
            private readonly BookmarkReadContext bookmarkContext;
            private Record currentRecord;
            private InhetoBinary? binary;
            private ulong id;
            private ulong? lastIdentity;
            private long ordinal;
            private bool ownsCurrentRecord;
            private bool positioned;
            private bool disposed;

            /// <summary>Creates a direct all-record core over Fractal's borrowed group reader.<br/></summary>
            internal ReaderCore(RecordStore<T> store, byte groupKey, BookmarkReadContext bookmarkContext)
            {
                this.store = store;
                this.bookmarkContext = bookmarkContext ?? throw new ArgumentNullException(nameof(bookmarkContext));
                ordinal = bookmarkContext.InitialResultsConsumed - 1;
                lastIdentity = bookmarkContext.InitialLastIdentity;
                operation = store.EnterStoreOperationScope();
                try
                {
                    groupReader = store.rbase.data.OpenGroupRecordReader(groupKey);
                    long remaining = bookmarkContext.InitialResultsConsumed;
                    while (remaining > 0)
                    {
                        int requested = (int)Math.Min(remaining, int.MaxValue);
                        int skipped = groupReader.Skip(requested);
                        remaining -= skipped;
                        if (skipped != requested)
                            break;
                    }
                }
                catch
                {
                    operation.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Creates a complete physical-order core for explicit order-insensitive payload consumption.<br/>
            /// The constructor acquires a non-thread-affine store snapshot before Fractal captures compact record locations, then prepares one reusable Inheto wrapper whose borrowed payload is rebound for each row.<br/>
            /// Bookmark continuation advances the physical cursor without hydrating skipped values, while live continuation remains unavailable because physical order is deliberately nondeterministic.<br/>
            /// </summary>
            /// <param name="store">Owning Abraxas record store.<br/></param>
            /// <param name="groupKey">Fractal identity-group byte assigned to the store.<br/></param>
            /// <param name="bookmarkContext">Validated complete-store unordered continuation contract.<br/></param>
            /// <param name="unorderedPhysical">Discriminator required to prevent accidental construction through an ordered source path.<br/></param>
            internal ReaderCore(
                RecordStore<T> store,
                byte groupKey,
                BookmarkReadContext bookmarkContext,
                bool unorderedPhysical)
            {
                if (!unorderedPhysical)
                    throw new ArgumentOutOfRangeException(nameof(unorderedPhysical));

                this.store = store;
                this.bookmarkContext = bookmarkContext ?? throw new ArgumentNullException(nameof(bookmarkContext));
                ordinal = bookmarkContext.InitialResultsConsumed - 1;
                lastIdentity = bookmarkContext.InitialLastIdentity;
                operation = store.EnterStoreOperationScope();
                IDisposable? snapshot = null;
                FractalStore.GroupBulkRecordReader? reader = null;
                BufferStream? stream = null;
                try
                {
                    snapshot = store.storeRuntime.EnterReadSnapshotScope();
                    reader = store.rbase.data.OpenGroupBulkRecordReader(groupKey);
                    stream = new BufferStream(ReadOnlyMemory<byte>.Empty);
                    InhetoBinary reusable = InhetoBinary.FromStream(stream, store.NativeDeserializationOptions);

                    long remaining = bookmarkContext.InitialResultsConsumed;
                    while (remaining > 0)
                    {
                        if (!reader.Read())
                        {
                            throw new RecordBookmarkException(
                                RecordBookmarkFailureKind.AnchorUnavailable,
                                "The generation-bound unordered bookmark extends beyond the current result stream.");
                        }
                        remaining--;
                    }

                    unorderedSnapshot = snapshot;
                    unorderedReader = reader;
                    unorderedBinaryStream = stream;
                    reusableUnorderedBinary = reusable;
                }
                catch
                {
                    stream?.Dispose();
                    reader?.Dispose();
                    snapshot?.Dispose();
                    operation.Dispose();
                    throw;
                }
            }

            /// <summary>Creates an identity-driven core for conditions, ordering, State, and explicit identity selections.<br/></summary>
            internal ReaderCore(
                RecordStore<T> store,
                IEnumerable<ulong> identities,
                BookmarkReadContext bookmarkContext)
            {
                this.store = store;
                this.bookmarkContext = bookmarkContext ?? throw new ArgumentNullException(nameof(bookmarkContext));
                ordinal = bookmarkContext.InitialResultsConsumed - 1;
                lastIdentity = bookmarkContext.InitialLastIdentity;
                operation = store.EnterStoreOperationScope();
                try
                {
                    this.identities = ResumeBookmarkStream(
                        identities,
                        static identity => identity,
                        bookmarkContext).GetEnumerator();
                }
                catch
                {
                    operation.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Creates a row core over a source-specialized identity cursor.<br/>
            /// Identity movement and skipping remain delegated to the selected source, while payload resolution occurs only for identities the developer actually reads.<br/>
            /// </summary>
            /// <param name="store">Owning Abraxas record store.<br/></param>
            /// <param name="identityCursor">Specialized ordered identity source.<br/></param>
            /// <param name="bookmarkContext">Validated selection, ordering, and continuation contract.<br/></param>
            /// <param name="trustIdentityExistence">Whether the mutation-converged source contract already proves that every emitted identity belongs to a live authoritative row.<br/></param>
            internal ReaderCore(
                RecordStore<T> store,
                IIdentitySourceCursor identityCursor,
                BookmarkReadContext bookmarkContext,
                bool trustIdentityExistence = false)
            {
                this.store = store;
                this.bookmarkContext = bookmarkContext ?? throw new ArgumentNullException(nameof(bookmarkContext));
                this.trustIdentityExistence = trustIdentityExistence;
                ordinal = bookmarkContext.InitialResultsConsumed - 1;
                lastIdentity = bookmarkContext.InitialLastIdentity;
                operation = store.EnterStoreOperationScope();
                this.identityCursor = identityCursor ?? throw new ArgumentNullException(nameof(identityCursor));
                try
                {
                    ResumeIdentityCursorFromBookmark();
                }
                catch
                {
                    this.identityCursor.Dispose();
                    operation.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Creates a condition-plan core that can consume retained residual-evaluation payloads without a second Fractal read.<br/>
            /// </summary>
            internal ReaderCore(
                RecordStore<T> store,
                IEnumerable<PlannedConditionMatch> matches,
                BookmarkReadContext bookmarkContext)
            {
                this.store = store;
                this.bookmarkContext = bookmarkContext ?? throw new ArgumentNullException(nameof(bookmarkContext));
                ordinal = bookmarkContext.InitialResultsConsumed - 1;
                lastIdentity = bookmarkContext.InitialLastIdentity;
                operation = store.EnterStoreOperationScope();
                try
                {
                    plannedMatches = matches.GetEnumerator();
                }
                catch
                {
                    operation.Dispose();
                    throw;
                }
            }

            /// <summary>Gets the current row ordinal.<br/></summary>
            internal long Ordinal => ordinal;

            /// <summary>Gets a detached continuation positioned at the next unread row.<br/></summary>
            internal RecordBookmark Bookmark
                => bookmarkContext.Capture(
                    ordinal + 1,
                    lastIdentity,
                    store.rbase.data.GetUSN(store.storeID));

            /// <summary>Binds and validates the public reader's object or property result shape.<br/></summary>
            internal void BindResultShape(string fingerprint)
                => bookmarkContext.BindResultShape(fingerprint);

            /// <summary>Gets the active store deserialization options used by current-row property access.<br/></summary>
            internal Inheto.DeserializationOptions Options => store.NativeDeserializationOptions;

            /// <summary>Gets the current record identity.<br/></summary>
            internal ulong ID => positioned ? id : throw new InvalidOperationException("The reader is not positioned on a row.");

            /// <summary>
            /// Gets the current Inheto payload wrapper, creating it only when authoritative value bytes are actually requested.<br/>
            /// Exact ordered LibraDex-key access can therefore remain payload-free while object projection and fallback value access preserve ordinary Fractal/Inheto semantics.<br/>
            /// </summary>
            internal InhetoBinary Binary => EnsureBinary();

            /// <summary>
            /// Gets the current authoritative Fractal application payload without decrypting, decompressing, or parsing it.<br/>
            /// The returned memory is borrowed and remains valid only until the next reader movement or disposal; callers that retain it must copy it first.<br/>
            /// </summary>
            internal ReadOnlyMemory<byte> StoredPayload
            {
                get
                {
                    if (!positioned)
                        throw new InvalidOperationException("The reader is not positioned on a row.");

                    if (groupReader is not null)
                        return groupReader.Data;
                    if (unorderedReader is not null)
                        return unorderedReader.Data;
                    if (ownsCurrentRecord)
                        return currentRecord.Data;
                    if (!TryAcquireCurrentRecord(id, createBinary: false))
                        throw new InvalidOperationException($"Record identity '{id}' was no longer available while its stored payload was being resolved.");

                    return currentRecord.Data;
                }
            }

            /// <summary>
            /// Attempts to satisfy a current-row value request directly from an exact reversible source key.<br/>
            /// Only the source cursor can approve this shortcut; all non-index, transformed, nullable-ambiguous, and lossy representations return <see langword="false"/>.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact declared CLR value type.<br/></typeparam>
            /// <param name="canonicalPath">Canonical Inheto value path.<br/></param>
            /// <param name="value">Receives the current exact source value when available.<br/></param>
            /// <returns><see langword="true"/> when no Fractal payload read is required.<br/></returns>
            internal bool TryProjectCurrentIndexValue<TValue>(string canonicalPath, out TValue? value)
            {
                if (!positioned)
                    throw new InvalidOperationException("The reader is not positioned on a row.");
                // One active physical index cannot prove complete coverage for a logical alias group whose records
                // may carry different stored member names. Preserve payload-first alias semantics for that request.
                if (store.NativeDeserializationOptions.Aliases.Contains(canonicalPath))
                {
                    value = default;
                    return false;
                }
                if (identityCursor is not null)
                    return identityCursor.TryGetCurrentValue(canonicalPath, out value);
                value = default;
                return false;
            }

            /// <summary>
            /// Advances the selected source and optionally creates an Inheto wrapper over the borrowed payload.<br/>
            /// </summary>
            internal bool Read(bool requireBinary)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ReleaseCurrent();

                if (groupReader is not null)
                {
                    if (!groupReader.Read())
                        return false;
                    id = groupReader.ID;
                    if (requireBinary)
                        binary = store.CreateRecordBinary(id, groupReader.Data);
                }
                else if (unorderedReader is not null)
                {
                    if (!unorderedReader.Read())
                        return false;
                    id = unorderedReader.ID;
                    if (requireBinary)
                        binary = BindUnorderedBinary();
                }
                else if (plannedMatches is not null)
                {
                    bool found = false;
                    while (plannedMatches.MoveNext())
                    {
                        PlannedConditionMatch match = plannedMatches.Current;
                        id = match.RecordId;
                        if (match.Payload is not null)
                        {
                            binary = match.Payload;
                            found = true;
                            break;
                        }

                        if (requireBinary && !TryAcquireCurrentRecord(id))
                            continue;
                        found = true;
                        break;
                    }

                    if (!found)
                        return false;
                }
                else if (identityCursor is not null)
                {
                    bool found = false;
                    while (identityCursor.Read(out ulong candidate))
                    {
                        store.EnsureRecordBelongsToCurrentStore(candidate, nameof(identityCursor));
                        if ((requireBinary || !trustIdentityExistence) && !TryAcquireCurrentRecord(candidate, requireBinary))
                            continue;
                        id = candidate;
                        found = true;
                        break;
                    }

                    if (!found)
                        return false;
                }
                else
                {
                    bool found = false;
                    while (identities!.MoveNext())
                    {
                        ulong candidate = identities.Current;
                        store.EnsureRecordBelongsToCurrentStore(candidate, nameof(identities));
                        if (!TryAcquireCurrentRecord(candidate, requireBinary))
                            continue;
                        id = candidate;
                        found = true;
                        break;
                    }

                    if (!found)
                        return false;
                }

                positioned = true;
                ordinal++;
                lastIdentity = id;
                return true;
            }

            /// <summary>
            /// Resolves the current authoritative payload on first actual value access.<br/>
            /// Direct Fractal group readers borrow their already-current bytes; identity-driven readers acquire and retain one pooled record until the reader advances.<br/>
            /// </summary>
            /// <returns>The current Inheto wrapper.<br/></returns>
            private InhetoBinary EnsureBinary()
            {
                if (!positioned)
                    throw new InvalidOperationException("The reader is not positioned on a row.");
                if (binary is not null)
                    return binary;

                if (groupReader is not null)
                {
                    binary = store.CreateRecordBinary(id, groupReader.Data);
                    return binary;
                }

                if (unorderedReader is not null)
                    return BindUnorderedBinary();

                if (ownsCurrentRecord)
                {
                    binary = store.CreateRecordBinary(id, currentRecord.Data);
                    return binary;
                }

                if (!TryAcquireCurrentRecord(id))
                    throw new InvalidOperationException($"Record identity '{id}' was no longer available while its current value was being resolved.");

                return binary!;
            }

            /// <summary>
            /// Rebinds the unordered reader's reusable Inheto wrapper to the current borrowed Fractal payload.<br/>
            /// The wrapper and its backing stream are allocated once per reader; callers must finish all current-row access before advancing, matching the existing borrowed-row contract.<br/>
            /// </summary>
            /// <returns>The reusable wrapper bound to the current physical row.<br/></returns>
            private InhetoBinary BindUnorderedBinary()
            {
                InhetoBinary reusable = reusableUnorderedBinary
                    ?? throw new InvalidOperationException("The unordered reader has no reusable Inheto wrapper.");
                FractalStore.GroupBulkRecordReader reader = unorderedReader
                    ?? throw new InvalidOperationException("The unordered Fractal reader is unavailable.");
                store.RebindRecordBinary(reusable, reader.ID, reader.Data);
                return reusable;
            }

            /// <summary>
            /// Locates and acquires one authoritative Fractal record in a single lookup, then retains its borrowed bytes for every later access to the current row.<br/>
            /// This is the row-materializing counterpart to an existence-only probe: callers that need the payload must not first call <c>RecordExists</c>, because <c>TryGetOne</c> already performs the same location traversal before reading the record.<br/>
            /// A failed lookup leaves the reader unpositioned for that candidate, while construction failures release the borrowed record immediately.<br/>
            /// </summary>
            /// <param name="recordId">Store-owned record identity to locate and retain.<br/></param>
            /// <param name="createBinary">True to create the Inheto wrapper immediately; false to retain only the pooled record and defer parsing until first value access.<br/></param>
            /// <returns><see langword="true"/> when the current record was acquired and any requested wrapper created; otherwise <see langword="false"/>.<br/></returns>
            private bool TryAcquireCurrentRecord(ulong recordId, bool createBinary = true)
            {
                if (!store.rbase.data.TryGetOne(recordId, out Record acquired))
                    return false;

                try
                {
                    currentRecord = acquired;
                    ownsCurrentRecord = true;
                    if (createBinary)
                        binary = store.CreateRecordBinary(recordId, currentRecord.Data);
                    return true;
                }
                catch
                {
                    acquired.Dispose();
                    currentRecord = default;
                    ownsCurrentRecord = false;
                    binary = null;
                    throw;
                }
            }

            /// <summary>Skips rows without creating Inheto wrappers.<br/></summary>
            internal int Skip(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                ObjectDisposedException.ThrowIf(disposed, this);
                ReleaseCurrent();
                if (groupReader is not null)
                {
                    int skippedNative = groupReader.Skip(count);
                    if (skippedNative > 0)
                    {
                        ordinal += skippedNative;
                        lastIdentity = groupReader.LastAdvancedIdentity;
                    }
                    return skippedNative;
                }
                if (unorderedReader is not null)
                {
                    int skippedUnordered = 0;
                    ulong lastSkippedIdentity = 0;
                    while (skippedUnordered < count && unorderedReader.Read())
                    {
                        lastSkippedIdentity = unorderedReader.ID;
                        skippedUnordered++;
                    }
                    if (skippedUnordered > 0)
                    {
                        ordinal += skippedUnordered;
                        lastIdentity = lastSkippedIdentity;
                    }
                    return skippedUnordered;
                }
                if (identityCursor is not null)
                {
                    int skippedSource = identityCursor.Skip(count, out ulong lastSkippedIdentity);
                    if (skippedSource > 0)
                    {
                        ordinal += skippedSource;
                        lastIdentity = lastSkippedIdentity;
                    }
                    return skippedSource;
                }

                int skipped = 0;
                while (skipped < count && Read(requireBinary: false))
                    skipped++;
                return skipped;
            }

            /// <summary>
            /// Repositions a specialized identity source according to the validated bookmark before public row access begins.<br/>
            /// Generation-bound bookmarks use bulk source skip; live continuation seeks the logical identity anchor.<br/>
            /// </summary>
            private void ResumeIdentityCursorFromBookmark()
            {
                RecordBookmark? bookmark = bookmarkContext.SourceBookmark;
                if (bookmark is null || bookmark.ResultsConsumed == 0)
                    return;

                if (bookmarkContext.Consistency == BookmarkConsistency.LiveContinuation)
                {
                    if (!bookmark.LastIdentity.HasValue)
                    {
                        throw new RecordBookmarkException(
                            RecordBookmarkFailureKind.AnchorUnavailable,
                            "A consumed live-continuation bookmark has no logical identity anchor.");
                    }
                    while (identityCursor!.Read(out ulong identity))
                    {
                        if (identity == bookmark.LastIdentity.Value)
                            return;
                    }
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.AnchorUnavailable,
                        "The live-continuation anchor is no longer present in the current result stream.");
                }

                long remaining = bookmark.ResultsConsumed;
                while (remaining > 0)
                {
                    int requested = (int)Math.Min(remaining, int.MaxValue);
                    int skipped = identityCursor!.Skip(requested, out _);
                    remaining -= skipped;
                    if (skipped != requested)
                    {
                        throw new RecordBookmarkException(
                            RecordBookmarkFailureKind.AnchorUnavailable,
                            "The generation-bound bookmark extends beyond the current result stream.");
                    }
                }
            }

            /// <summary>Releases every reader-owned lifetime in dependency order.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                ReleaseCurrent();
                groupReader?.Dispose();
                unorderedReader?.Dispose();
                unorderedBinaryStream?.Dispose();
                unorderedSnapshot?.Dispose();
                identityCursor?.Dispose();
                identities?.Dispose();
                plannedMatches?.Dispose();
                operation.Dispose();
            }

            /// <summary>Returns an identity-driven pooled record and invalidates the current Inheto wrapper.<br/></summary>
            private void ReleaseCurrent()
            {
                binary = null;
                positioned = false;
                if (!ownsCurrentRecord)
                    return;
                currentRecord.Dispose();
                currentRecord = default;
                ownsCurrentRecord = false;
            }
        }

        /// <summary>
        /// Provides forward-only access to authoritative Fractal application payloads without applying Abraxas payload transforms.<br/>
        /// The current payload memory is borrowed and is valid only until the next <see cref="Read"/> call or disposal; use <see cref="CopyPayload"/> when retention is required.<br/>
        /// </summary>
        public sealed class StoredPayloadReader : IDisposable
        {
            private readonly ReaderCore core;

            /// <summary>
            /// Creates one payload reader over an already-open shared reader core.<br/>
            /// </summary>
            /// <param name="core">Core that owns store admission, source traversal, and Fractal payload lifetimes.<br/></param>
            internal StoredPayloadReader(ReaderCore core)
            {
                this.core = core ?? throw new ArgumentNullException(nameof(core));
                try
                {
                    core.BindResultShape("stored-payload:v1");
                }
                catch
                {
                    core.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Gets the current Abraxas record identity after a successful <see cref="Read"/>.<br/>
            /// </summary>
            public ulong ID => core.ID;

            /// <summary>
            /// Gets the zero-based ordinal of the current result, or -1 before the first successful read.<br/>
            /// </summary>
            public long Ordinal => core.Ordinal;

            /// <summary>
            /// Gets a continuation bookmark positioned at the next unread result.<br/>
            /// </summary>
            public RecordBookmark Bookmark => core.Bookmark;

            /// <summary>
            /// Gets the exact current Fractal application payload without applying encryption or compression decoding.<br/>
            /// The memory is borrowed and remains valid only until the next <see cref="Read"/> call or disposal.<br/>
            /// </summary>
            public ReadOnlyMemory<byte> Payload => core.StoredPayload;

            /// <summary>
            /// Advances to the next selected record without parsing its stored payload.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when <see cref="ID"/> and <see cref="Payload"/> are available; otherwise <see langword="false"/>.<br/></returns>
            public bool Read() => core.Read(requireBinary: false);

            /// <summary>
            /// Advances to the next selected record without parsing its stored payload.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when a current payload is available; otherwise <see langword="false"/>.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>
            /// Gets the exact current payload as a borrowed span.<br/>
            /// The span is valid only until the next <see cref="Read"/> call or disposal.<br/>
            /// </summary>
            /// <returns>Borrowed current payload bytes.<br/></returns>
            public ReadOnlySpan<byte> GetRawSpan() => Payload.Span;

            /// <summary>
            /// Gets the exact current payload as borrowed memory.<br/>
            /// The memory is valid only until the next <see cref="Read"/> call or disposal.<br/>
            /// </summary>
            /// <returns>Borrowed current payload memory.<br/></returns>
            public ReadOnlyMemory<byte> GetRawMem() => Payload;

            /// <summary>
            /// Copies the exact current payload into a caller-owned array.<br/>
            /// This is the detached counterpart to <see cref="Payload"/> and remains valid after reader movement or disposal.<br/>
            /// </summary>
            /// <returns>Caller-owned current payload bytes.<br/></returns>
            public byte[] CopyPayload() => Payload.ToArray();

            /// <summary>
            /// Disposes the Fractal reader and releases the active store operation lease.<br/>
            /// </summary>
            public void Dispose() => core.Dispose();
        }

        /// <summary>
        /// Provides forward-only access to decoded, decompressed INHETO record binaries without CLR-object materialization.<br/>
        /// The current binary is reader-owned and must be consumed before the next <see cref="Read"/> call or disposal.<br/>
        /// </summary>
        public sealed class RecordBinaryReader : IDisposable
        {
            private readonly ReaderCore core;

            /// <summary>
            /// Creates one INHETO-binary reader over an already-open shared reader core.<br/>
            /// </summary>
            /// <param name="core">Core that owns store admission, source traversal, transforms, and payload lifetimes.<br/></param>
            internal RecordBinaryReader(ReaderCore core)
            {
                this.core = core ?? throw new ArgumentNullException(nameof(core));
                try
                {
                    core.BindResultShape("inheto-binary:v1");
                }
                catch
                {
                    core.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Gets the current Abraxas record identity after a successful <see cref="Read"/>.<br/>
            /// </summary>
            public ulong ID => core.ID;

            /// <summary>
            /// Gets the zero-based ordinal of the current result, or -1 before the first successful read.<br/>
            /// </summary>
            public long Ordinal => core.Ordinal;

            /// <summary>
            /// Gets a continuation bookmark positioned at the next unread result.<br/>
            /// </summary>
            public RecordBookmark Bookmark => core.Bookmark;

            /// <summary>
            /// Gets the decoded and decompressed INHETO binary for the current row without materializing a CLR object.<br/>
            /// The binary is reader-owned and must be consumed before reader movement or disposal.<br/>
            /// </summary>
            public RecordBinary Binary => new(core.Binary, isBorrowed: true);

            /// <summary>
            /// Advances to the next selected record and prepares its decoded INHETO binary.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when <see cref="ID"/> and <see cref="Binary"/> are available; otherwise <see langword="false"/>.<br/></returns>
            public bool Read() => core.Read(requireBinary: true);

            /// <summary>
            /// Advances to the next selected record and prepares its decoded INHETO binary.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when a current binary is available; otherwise <see langword="false"/>.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>
            /// Gets the decoded and decompressed INHETO binary for the current row.<br/>
            /// </summary>
            /// <returns>Reader-owned current INHETO binary.<br/></returns>
            public RecordBinary Get() => Binary;

            /// <summary>
            /// Copies the current decoded payload into an independently backed binary before reader movement or disposal.<br/>
            /// The live read policy and process-local materialization context are retained; callback/store dependencies are not cloned.<br/>
            /// </summary>
            /// <returns>Nonborrowed binary with independent bytes and cursor/caches.<br/></returns>
            public RecordBinary CopyCurrent() => Binary.Detach();

            /// <summary>
            /// Advances past up to count rows through the existing source's skip path without preparing their binary wrappers.<br/>
            /// This invalidates the current row even when count is zero. Negative counts are rejected.<br/>
            /// </summary>
            /// <param name="count">Nonnegative number of unread rows to skip.<br/></param>
            /// <returns>Number of rows skipped, which can be smaller at end of source.<br/></returns>
            public int Skip(int count) => core.Skip(count);

            /// <summary>
            /// Disposes the Fractal reader and releases the active store operation lease.<br/>
            /// </summary>
            public void Dispose() => core.Dispose();
        }
    }
}
