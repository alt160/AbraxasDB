using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Gets the allocation-free reader capability view for this store.<br/>
        /// The returned readonly value carries only the store reference; opening a reader selects its optimized execution path once.<br/>
        /// </summary>
        public ReaderOperations Readers => new(this);

        /// <summary>
        /// Gets the allocation-free writer capability view for this store.<br/>
        /// Existing direct write methods remain source-compatible and this view adds no buffering or dispatch allocation.<br/>
        /// </summary>
        public WriterOperations Writers => new(this);

        /// <summary>
        /// Gets the allocation-free store-maintenance capability view.<br/>
        /// RecordBase-wide backup and compaction remain on <see cref="RecordBase"/> so their scope is not disguised as store-local work.<br/>
        /// </summary>
        public MaintenanceOperations Maintenance => new(this);

        /// <summary>
        /// Groups forward-only reader entry points without repeating the word Reader in every member name.<br/>
        /// This readonly value is a discoverability facade; every member forwards directly into the established semantic reader or centralized open-time planner.<br/>
        /// </summary>
        public readonly partial struct ReaderOperations
        {
            private readonly RecordStore<T> store;

            internal ReaderOperations(RecordStore<T> store) => this.store = store;

            /// <summary>
            /// Gets the allocation-free recursive-reader capability view for explicit, ad hoc hierarchy traversal.<br/>
            /// Recursive preparation resolves identity topology through ordinary Abraxas conditions, allowing LibraDex index adoption or authoritative fallback without turning recursion into a persistent property link.<br/>
            /// </summary>
            public RecursiveReaderOperations Recursive => new(store);

            /// <summary>Opens the flexible borrowed-current-row reader in natural store order.<br/></summary>
            /// <returns>A reader positioned before the first row.<br/></returns>
            public RowReader Open()
                => new(store, store.CreateAllRowPlan(), nameof(Open));

            /// <summary>
            /// Opens the flexible borrowed-current-row reader for complete, order-insensitive bulk consumption.<br/>
            /// Opening materializes a compact physical-location snapshot before returning; iteration then reuses contiguous Fractal read windows and one rebound Inheto wrapper instead of performing one natural-route lookup per record.<br/>
            /// Result order is deliberately unspecified and may differ from <see cref="Open()"/>; use the natural reader or an explicit sorted/index reader whenever order is observable.<br/>
            /// The reader holds a shared read snapshot that blocks mutations to this store until disposal, so dispose it promptly after the bulk operation.<br/>
            /// This explicit route does not create, require, or silently adopt an index, and it does not change the behavior of <see cref="Open()"/>.<br/>
            /// </summary>
            /// <returns>A replayable reader positioned before the first unordered row.<br/></returns>
            public RowReader OpenUnordered()
                => new(store, store.CreateUnorderedRowPlan(), nameof(OpenUnordered));

            /// <summary>
            /// Resumes complete order-insensitive bulk consumption from a generation-bound unordered-reader bookmark.<br/>
            /// The store generation and unordered ordering contract are validated before the replacement physical snapshot is exposed.<br/>
            /// </summary>
            /// <param name="bookmark">Continuation captured from an unordered reader over this exact store generation.<br/></param>
            /// <returns>A replayable reader positioned immediately before the bookmark's next unread row.<br/></returns>
            public RowReader OpenUnordered(RecordBookmark bookmark)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                return new RowReader(store, store.CreateUnorderedRowPlan(bookmark), nameof(OpenUnordered));
            }

            /// <summary>
            /// Opens the flexible borrowed-current-row reader in the order and population of one exact existing index.<br/>
            /// Identity movement and skip stay on the selected index cursor; record payloads are fetched only for rows the developer reads.<br/>
            /// </summary>
            /// <param name="index">Store-owned index that defines ordered population and traversal order.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <returns>A flexible row reader positioned before the first indexed row.<br/></returns>
            public RowReader Open(
                RecordIndex index,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                return new RowReader(store, store.CreateIndexRowPlan(index, direction), nameof(Open));
            }

            /// <summary>Opens the flexible borrowed-current-row reader for a condition in its selected Fractal or LibraDex source order.<br/></summary>
            /// <param name="where">Completed condition to execute.<br/></param>
            /// <returns>A reader positioned before the first matching row.<br/></returns>
            public RowReader Open(Condition<T>.EndCondition where)
            {
                ArgumentNullException.ThrowIfNull(where);
                return new RowReader(store, store.CreateConditionRowPlan(where), nameof(Open));
            }

            /// <summary>
            /// Opens one quickly returned sorted reader over the complete store from one or more explicit property-order components.<br/>
            /// Bare property paths are ascending; strongly typed tuples apply per-key direction without caller-created descriptor objects.<br/>
            /// <see cref="SortedReader.Prepare()"/> or the first consuming operation reuses an exact index or synchronously establishes one private complete order.<br/>
            /// </summary>
            /// <param name="first">Required primary property path or strongly typed path-and-direction tuple.<br/></param>
            /// <param name="additional">Optional additional components in decreasing lexicographic priority.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted(SortBy first, params SortBy[] additional)
            {
                ArgumentNullException.ThrowIfNull(additional);
                SortBy[] order = new SortBy[additional.Length + 1];
                order[0] = first;
                additional.CopyTo(order, 1);
                RecordStore<T> capturedStore = store;
                return capturedStore.OpenSortedReader(
                    capturedStore.CreateAllRowPlan(),
                    order,
                    () => capturedStore.CreateFlexibleRowBookmarkTemplate(capturedStore.CreateAllBookmarkContext(order)));
            }

            /// <summary>
            /// Opens one quickly returned sorted reader over a condition-selected population from one or more explicit property-order components.<br/>
            /// The condition chooses population only; ordering remains an explicit topology-independent caller requirement.<br/>
            /// </summary>
            /// <param name="where">Completed condition selecting the logical population.<br/></param>
            /// <param name="first">Required primary property path or strongly typed path-and-direction tuple.<br/></param>
            /// <param name="additional">Optional additional components in decreasing lexicographic priority.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted(Condition<T>.EndCondition where, SortBy first, params SortBy[] additional)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(additional);
                SortBy[] order = new SortBy[additional.Length + 1];
                order[0] = first;
                additional.CopyTo(order, 1);
                RecordStore<T> capturedStore = store;
                return capturedStore.OpenSortedReader(
                    capturedStore.CreateConditionRowPlan(where),
                    order,
                    () => capturedStore.CreateFlexibleRowBookmarkTemplate(capturedStore.CreateConditionBookmarkContext(where, order)));
            }

            /// <summary>
            /// Opens one quickly returned sorted reader whose population and lexicographic order come from exact existing indexes.<br/>
            /// Bare handles are ascending; strongly typed tuples apply per-index direction. No substitute property index or payload-key sort is selected.<br/>
            /// </summary>
            /// <param name="first">Required primary exact-index handle or strongly typed handle-and-direction tuple.<br/></param>
            /// <param name="additional">Optional additional exact-index components in decreasing lexicographic priority.<br/></param>
            /// <returns>An independently disposable index-backed sorted reader.<br/></returns>
            public SortedReader OpenSorted(IndexSort first, params IndexSort[] additional)
            {
                ArgumentNullException.ThrowIfNull(additional);
                IndexSort[] order = new IndexSort[additional.Length + 1];
                order[0] = first;
                additional.CopyTo(order, 1);
                return store.OpenSortedReader(store.CreateAllRowPlan(), order);
            }

            /// <summary>
            /// Opens one quickly returned condition-selected reader in the lexicographic order of exact existing indexes.<br/>
            /// The condition supplies the logical population; the handles supply ordering and no property-order fallback is substituted.<br/>
            /// </summary>
            /// <param name="where">Completed condition selecting the logical population.<br/></param>
            /// <param name="first">Required primary exact-index handle or strongly typed handle-and-direction tuple.<br/></param>
            /// <param name="additional">Optional additional exact-index components in decreasing lexicographic priority.<br/></param>
            /// <returns>An independently disposable index-backed sorted reader.<br/></returns>
            public SortedReader OpenSorted(Condition<T>.EndCondition where, IndexSort first, params IndexSort[] additional)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(additional);
                IndexSort[] order = new IndexSort[additional.Length + 1];
                order[0] = first;
                additional.CopyTo(order, 1);
                return store.OpenSortedReader(store.CreateConditionRowPlan(where), order);
            }

            /// <summary>
            /// Opens one quickly returned unprepared sorted reader over the complete store from a direct or calculated typed selector.<br/>
            /// <see cref="SortedReader.Prepare()"/> or the first consuming operation synchronously establishes the complete stable order.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact scalar ordering-key type inferred from the selector.<br/></typeparam>
            /// <param name="order">Direct or calculated durable selector defining the primary key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least; the default is ascending.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted<TValue>(
                Selector<T>.IValue<TValue> order,
                bool descending = false)
                => store.OpenSortedReader(store.CreateAllRowPlan(), order, descending);

            /// <summary>
            /// Opens one quickly returned unprepared sorted reader over a completed condition from a direct or calculated typed selector.<br/>
            /// The condition remains the population contract, while explicit preparation or first use synchronously establishes the complete stable order.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact scalar ordering-key type inferred from the selector.<br/></typeparam>
            /// <param name="where">Completed condition selecting the logical population.<br/></param>
            /// <param name="order">Direct or calculated durable selector defining the primary key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least; the default is ascending.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted<TValue>(
                Condition<T>.EndCondition where,
                Selector<T>.IValue<TValue> order,
                bool descending = false)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenSortedReader(
                    store.CreateConditionRowPlan(where),
                    order,
                    descending);
            }

            /// <summary>
            /// Opens one quickly returned unprepared sorted reader over the complete store from an Inheto property path.<br/>
            /// Culture and comparison options are represented as a durable sort-key selector; when omitted, the direct path remains eligible for an ordinary exact index.<br/>
            /// </summary>
            /// <param name="propPath">Inheto property path supplying the order key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <param name="cultureName">Optional culture for text ordering; null or empty means invariant culture.<br/></param>
            /// <param name="compareOptions">Text comparison options; non-default options require a string-valued path.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted(
                string propPath,
                bool descending = false,
                string? cultureName = null,
                CompareOptions compareOptions = CompareOptions.None)
                => store.OpenSortedReader(store.CreateAllRowPlan(), propPath, descending, cultureName, compareOptions);

            /// <summary>
            /// Opens one quickly returned unprepared sorted reader over a condition-selected population from an Inheto property path.<br/>
            /// Explicit preparation or the first consuming operation synchronously establishes the complete stable order.<br/>
            /// </summary>
            /// <param name="where">Completed condition selecting the logical population.<br/></param>
            /// <param name="propPath">Inheto property path supplying the order key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <param name="cultureName">Optional culture for text ordering; null or empty means invariant culture.<br/></param>
            /// <param name="compareOptions">Text comparison options; non-default options require a string-valued path.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted(
                Condition<T>.EndCondition where,
                string propPath,
                bool descending = false,
                string? cultureName = null,
                CompareOptions compareOptions = CompareOptions.None)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenSortedReader(
                    store.CreateConditionRowPlan(where),
                    propPath,
                    descending,
                    cultureName,
                    compareOptions);
            }

            /// <summary>
            /// Opens one quickly returned unprepared sorted reader over the complete store from a strongly typed property expression.<br/>
            /// The expression is reduced to its canonical Inheto path immediately; arbitrary calculated expressions remain available through the reusable selector overload.<br/>
            /// </summary>
            /// <typeparam name="TValue">Declared property value type inferred from the expression.<br/></typeparam>
            /// <param name="property">Property, field, or admitted parameterless-member expression supplying the order key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted<TValue>(
                Expression<Func<T, TValue>> property,
                bool descending = false)
            {
                ArgumentNullException.ThrowIfNull(property);
                return store.OpenSortedReader(
                    store.CreateAllRowPlan(),
                    property.GetPropertyPath(),
                    descending,
                    cultureName: null,
                    CompareOptions.None);
            }

            /// <summary>
            /// Opens one quickly returned unprepared sorted reader over a condition-selected population from a strongly typed property expression.<br/>
            /// The condition supplies population only; explicit preparation or first use establishes the complete stable order.<br/>
            /// </summary>
            /// <typeparam name="TValue">Declared property value type inferred from the expression.<br/></typeparam>
            /// <param name="where">Completed condition selecting the logical population.<br/></param>
            /// <param name="property">Property, field, or admitted parameterless-member expression supplying the order key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted<TValue>(
                Condition<T>.EndCondition where,
                Expression<Func<T, TValue>> property,
                bool descending = false)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(property);
                return store.OpenSortedReader(
                    store.CreateConditionRowPlan(where),
                    property.GetPropertyPath(),
                    descending,
                    cultureName: null,
                    CompareOptions.None);
            }

            /// <summary>
            /// Opens one quickly returned unprepared culture-aware text-sorted reader over the complete store from a strongly typed string property expression.<br/>
            /// Culture and comparison options become a durable binary sort-key selector and may therefore reuse a matching explicit calculated index.<br/>
            /// </summary>
            /// <param name="property">String property expression supplying the order key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <param name="cultureName">Optional culture for text ordering; null or empty means invariant culture.<br/></param>
            /// <param name="compareOptions">Text comparison options.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted(
                Expression<Func<T, string>> property,
                bool descending = false,
                string? cultureName = null,
                CompareOptions compareOptions = CompareOptions.None)
            {
                ArgumentNullException.ThrowIfNull(property);
                return store.OpenSortedReader(
                    store.CreateAllRowPlan(),
                    property.GetPropertyPath(),
                    descending,
                    cultureName,
                    compareOptions);
            }

            /// <summary>
            /// Opens one quickly returned unprepared culture-aware text-sorted reader over a condition-selected population from a strongly typed string property expression.<br/>
            /// Culture and comparison options become a durable binary sort-key selector and may therefore reuse a matching explicit calculated index.<br/>
            /// </summary>
            /// <param name="where">Completed condition selecting the logical population.<br/></param>
            /// <param name="property">String property expression supplying the order key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <param name="cultureName">Optional culture for text ordering; null or empty means invariant culture.<br/></param>
            /// <param name="compareOptions">Text comparison options.<br/></param>
            /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
            public SortedReader OpenSorted(
                Condition<T>.EndCondition where,
                Expression<Func<T, string>> property,
                bool descending = false,
                string? cultureName = null,
                CompareOptions compareOptions = CompareOptions.None)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(property);
                return store.OpenSortedReader(
                    store.CreateConditionRowPlan(where),
                    property.GetPropertyPath(),
                    descending,
                    cultureName,
                    compareOptions);
            }

            /// <summary>
            /// Opens condition-selected flexible rows in the order and population of one exact existing index.<br/>
            /// The condition remains the selection contract and the supplied index alone defines order; no alternative order support is created.<br/>
            /// </summary>
            /// <param name="where">Completed condition used to select rows.<br/></param>
            /// <param name="index">Store-owned index that defines ordered population and traversal order.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <returns>A flexible row reader positioned before the first matching indexed row.<br/></returns>
            public RowReader Open(
                Condition<T>.EndCondition where,
                RecordIndex index,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentNullException.ThrowIfNull(where);
                return new RowReader(store, store.CreateIndexRowPlan(index, direction, where), nameof(Open));
            }

            /// <summary>Opens the flexible borrowed-current-row reader for persistent State and an optional mask.<br/></summary>
            /// <param name="state">State value to match.<br/></param>
            /// <param name="mask">Optional mask applying <c>(State &amp; mask) == state</c>.<br/></param>
            /// <returns>A reader positioned before the first matching row.<br/></returns>
            public RowReader Open(long state, long? mask = null)
                => new(store, store.CreateStateRowPlan(state, mask), nameof(Open));

            /// <summary>
            /// Opens flexible rows over a developer-supplied identity source without silently buffering it.<br/>
            /// Arrays and collection-backed sources support reset, fork, and sorting; a one-shot sequence remains readable once and reports those replay capabilities as unavailable.<br/>
            /// </summary>
            /// <param name="identities">Store-owned identities to read in their supplied order.<br/></param>
            /// <returns>A flexible row reader whose replay capabilities reflect the supplied source.<br/></returns>
            public RowReader Open(IEnumerable<ulong> identities)
            {
                ArgumentNullException.ThrowIfNull(identities);
                return new RowReader(store, store.CreateIdentityRowPlan(identities), nameof(Open));
            }

            /// <summary>Resumes a natural-order flexible row reader from an opaque bookmark.<br/></summary>
            /// <param name="bookmark">Bookmark captured by a compatible flexible row reader.<br/></param>
            /// <returns>A reader positioned before the next unread row.<br/></returns>
            public RowReader Open(RecordBookmark bookmark)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                return new RowReader(store, store.CreateAllRowPlan(origin: bookmark), nameof(Open));
            }

            /// <summary>
            /// Resumes a natural-order flexible row reader under an explicit bookmark-consistency policy.<br/>
            /// Live continuation requires an explicit deterministic order and is therefore available through <see cref="OpenSorted(RecordBookmark, BookmarkConsistency, SortBy, SortBy[])"/> rather than this natural-order overload.<br/>
            /// </summary>
            /// <param name="bookmark">Bookmark captured by a compatible natural-order flexible row reader.<br/></param>
            /// <param name="consistency">Generation-bound or live-continuation policy validated against the natural-order contract.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread row.<br/></returns>
            public RowReader Open(RecordBookmark bookmark, BookmarkConsistency consistency)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                BookmarkReadContext context = store.CreateAllBookmarkContext(Array.Empty<SortBy>(), bookmark, consistency);
                return new RowReader(store, store.CreateAllReaderCore(context), nameof(Open));
            }

            /// <summary>
            /// Resumes an exact-index flexible row reader from a compatible opaque bookmark.<br/>
            /// Source-aware skip repositions generation-bound readers without fetching payloads for already-consumed rows.<br/>
            /// </summary>
            /// <param name="index">The same exact store-owned index used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible indexed row reader.<br/></param>
            /// <param name="direction">The same traversal direction used to capture the bookmark.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread indexed row.<br/></returns>
            public RowReader Open(
                RecordIndex index,
                RecordBookmark bookmark,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                return new RowReader(store, store.CreateIndexRowPlan(index, direction, origin: bookmark), nameof(Open));
            }

            /// <summary>Resumes a natural plan-order conditional flexible row reader from an opaque bookmark.<br/></summary>
            /// <param name="where">The same completed condition used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible flexible row reader.<br/></param>
            /// <returns>A reader positioned before the next unread row.<br/></returns>
            public RowReader Open(Condition<T>.EndCondition where, RecordBookmark bookmark)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(bookmark);
                return new RowReader(store, store.CreateConditionRowPlan(where, origin: bookmark), nameof(Open));
            }

            /// <summary>
            /// Resumes a natural plan-order conditional row reader under an explicit bookmark-consistency policy.<br/>
            /// A live continuation must declare deterministic ordering and therefore uses the matching sorted overload.<br/>
            /// </summary>
            /// <param name="where">The same completed condition used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible conditional flexible row reader.<br/></param>
            /// <param name="consistency">Generation-bound or live-continuation policy validated against the condition and natural-order contract.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread matching row.<br/></returns>
            public RowReader Open(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(bookmark);
                SortBy[] order = Array.Empty<SortBy>();
                BookmarkReadContext context = store.CreateConditionBookmarkContext(where, order, bookmark, consistency);
                return new RowReader(
                    store,
                    store.CreateConditionReaderCore(where, order, nameof(Open), context),
                    nameof(Open));
            }

            /// <summary>
            /// Resumes a State-selected flexible row reader under the supplied continuation policy.<br/>
            /// State selection has no independent sort contract, so live continuation is rejected by the centralized bookmark validator.<br/>
            /// </summary>
            /// <param name="state">The same persistent State value used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible State-selected reader.<br/></param>
            /// <param name="mask">The same optional State mask used to capture the bookmark.<br/></param>
            /// <param name="consistency">Continuation policy; State readers support generation-bound continuation.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread State-selected row.<br/></returns>
            public RowReader Open(
                long state,
                RecordBookmark bookmark,
                long? mask = null,
                BookmarkConsistency consistency = BookmarkConsistency.GenerationBound)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                BookmarkReadContext context = store.CreateStateBookmarkContext(state, mask, bookmark, consistency);
                return new RowReader(
                    store,
                    store.CreateIdentityReaderCore(store.IterateRecordIDs(state, mask), context),
                    nameof(Open));
            }

            /// <summary>
            /// Resumes an explicitly property-sorted complete-store row stream from a compatible bookmark.<br/>
            /// The deterministic order is reconstructed immediately so validation and positioning occur at open time.<br/>
            /// </summary>
            /// <param name="bookmark">Bookmark captured from the same ordered population and row shape.<br/></param>
            /// <param name="first">Required primary property sort descriptor.<br/></param>
            /// <param name="additional">Optional remaining descriptors in decreasing lexicographic priority.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread sorted row.<br/></returns>
            public RowReader OpenSorted(RecordBookmark bookmark, SortBy first, params SortBy[] additional)
                => OpenSorted(bookmark, BookmarkConsistency.GenerationBound, first, additional);

            /// <summary>
            /// Resumes an explicitly property-sorted complete-store row stream under a requested consistency policy.<br/>
            /// The same stable ordering, including identity tie-breaking, participates in compatibility and live-continuation validation.<br/>
            /// </summary>
            /// <param name="bookmark">Bookmark captured from the same ordered population and row shape.<br/></param>
            /// <param name="consistency">Generation-bound or deterministic live-continuation policy.<br/></param>
            /// <param name="first">Required primary property sort descriptor.<br/></param>
            /// <param name="additional">Optional remaining descriptors in decreasing lexicographic priority.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread sorted row.<br/></returns>
            public RowReader OpenSorted(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy first, params SortBy[] additional)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                SortBy[] order = CombineSort(first, additional);
                BookmarkReadContext context = store.CreateAllBookmarkContext(order, bookmark, consistency);
                return new RowReader(
                    store,
                    store.CreateIdentityReaderCore(store.IterateRecordIDs(order), context),
                    nameof(OpenSorted));
            }

            /// <summary>
            /// Resumes an explicitly property-sorted conditional row stream from a compatible bookmark.<br/>
            /// Selection and ordering remain independent parts of the continuation contract.<br/>
            /// </summary>
            /// <param name="where">The same completed condition used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured from the same conditional ordered population.<br/></param>
            /// <param name="first">Required primary property sort descriptor.<br/></param>
            /// <param name="additional">Optional remaining descriptors in decreasing lexicographic priority.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread matching sorted row.<br/></returns>
            public RowReader OpenSorted(Condition<T>.EndCondition where, RecordBookmark bookmark, SortBy first, params SortBy[] additional)
                => OpenSorted(where, bookmark, BookmarkConsistency.GenerationBound, first, additional);

            /// <summary>
            /// Resumes an explicitly property-sorted conditional row stream under a requested consistency policy.<br/>
            /// Condition, stable ordering, row shape, store, and revision policy are validated before a row is exposed.<br/>
            /// </summary>
            /// <param name="where">The same completed condition used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured from the same conditional ordered population.<br/></param>
            /// <param name="consistency">Generation-bound or deterministic live-continuation policy.<br/></param>
            /// <param name="first">Required primary property sort descriptor.<br/></param>
            /// <param name="additional">Optional remaining descriptors in decreasing lexicographic priority.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread matching sorted row.<br/></returns>
            public RowReader OpenSorted(
                Condition<T>.EndCondition where,
                RecordBookmark bookmark,
                BookmarkConsistency consistency,
                SortBy first,
                params SortBy[] additional)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(bookmark);
                SortBy[] order = CombineSort(first, additional);
                BookmarkReadContext context = store.CreateConditionBookmarkContext(where, order, bookmark, consistency);
                return new RowReader(
                    store,
                    store.CreateConditionReaderCore(where, order, nameof(OpenSorted), context),
                    nameof(OpenSorted));
            }

            /// <summary>Combines one required sort descriptor with optional additional descriptors without exposing a builder object.<br/></summary>
            /// <param name="first">Required primary descriptor.<br/></param>
            /// <param name="additional">Optional remaining descriptors.<br/></param>
            /// <returns>A detached descriptor array preserving caller order.<br/></returns>
            private static SortBy[] CombineSort(SortBy first, SortBy[] additional)
            {
                ArgumentNullException.ThrowIfNull(additional);
                SortBy[] order = new SortBy[additional.Length + 1];
                order[0] = first;
                additional.CopyTo(order, 1);
                return order;
            }

            /// <summary>
            /// Resumes condition-selected flexible rows in one exact existing index order from a compatible opaque bookmark.<br/>
            /// The same condition, index, and traversal direction are required because each participates in the continuation contract.<br/>
            /// </summary>
            /// <param name="where">The same completed condition used to capture the bookmark.<br/></param>
            /// <param name="index">The same exact store-owned index used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible reader.<br/></param>
            /// <param name="direction">The same traversal direction used to capture the bookmark.<br/></param>
            /// <returns>A flexible row reader positioned before the next unread matching indexed row.<br/></returns>
            public RowReader Open(
                Condition<T>.EndCondition where,
                RecordIndex index,
                RecordBookmark bookmark,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(bookmark);
                return new RowReader(store, store.CreateIndexRowPlan(index, direction, where, bookmark), nameof(Open));
            }

            /// <summary>Opens an identity-only reader in natural store order.<br/></summary>
            /// <returns>A payload-free identity reader positioned before the first identity.<br/></returns>
            public IdentityReader OpenIdentities()
                => new(store, store.CreateAllRowPlan());

            /// <summary>
            /// Opens an identity-only reader in the stable order and population of one exact existing index.<br/>
            /// The selected index must enforce one key per identity; Abraxas does not create or substitute an index for this request.<br/>
            /// </summary>
            /// <param name="index">Store-owned index that defines both ordered population and traversal order.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <returns>A payload-free identity reader backed by the selected index's source-aware cursor.<br/></returns>
            public IdentityReader OpenIdentities(
                RecordIndex index,
                RetrievalDirection direction = RetrievalDirection.Ascending)
                => new(store, store.CreateIndexRowPlan(index, direction));

            /// <summary>
            /// Opens an identity-only reader through an exact existing index resolved by logical name, physical name, or property path.<br/>
            /// Resolution is explicit and fail-fast; no index is invented when the identifier is absent.<br/>
            /// </summary>
            /// <param name="indexIdentifier">Existing index identifier accepted by <see cref="RecordStore{T}.GetIndex(string, bool)"/>.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <returns>A payload-free identity reader in the selected index order.<br/></returns>
            public IdentityReader OpenIdentities(
                string indexIdentifier,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(indexIdentifier);
                return OpenIdentities(store.GetIndex(indexIdentifier), direction);
            }

            /// <summary>
            /// Resumes an exact-index identity reader from a compatible opaque bookmark.<br/>
            /// Generation-bound continuation uses the selected index's source-aware skip rather than replaying individual identities.<br/>
            /// </summary>
            /// <param name="index">The same exact store-owned index used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible indexed identity reader.<br/></param>
            /// <param name="direction">The same traversal direction used to capture the bookmark.<br/></param>
            /// <returns>A reader positioned before the next unread indexed identity.<br/></returns>
            public IdentityReader OpenIdentities(
                RecordIndex index,
                RecordBookmark bookmark,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                return new IdentityReader(store, store.CreateIndexRowPlan(index, direction, origin: bookmark));
            }

            /// <summary>Opens an identity-only reader for a condition in its selected Fractal or LibraDex source order.<br/></summary>
            /// <param name="where">Completed condition to execute.<br/></param>
            /// <returns>An identity reader positioned before the first match.<br/></returns>
            public IdentityReader OpenIdentities(Condition<T>.EndCondition where)
            {
                ArgumentNullException.ThrowIfNull(where);
                return new IdentityReader(store, store.CreateConditionRowPlan(where));
            }

            /// <summary>
            /// Opens identities selected by a condition in the order and population of one exact existing index.<br/>
            /// Selection and ordering remain independent: the condition chooses matches and the supplied index alone chooses their order.<br/>
            /// </summary>
            /// <param name="where">Completed condition used to select identities.<br/></param>
            /// <param name="index">Store-owned index that defines ordered population and traversal order.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <returns>An ordered payload-free identity reader over matches present in the selected index.<br/></returns>
            public IdentityReader OpenIdentities(
                Condition<T>.EndCondition where,
                RecordIndex index,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentNullException.ThrowIfNull(where);
                return new IdentityReader(store, store.CreateIndexRowPlan(index, direction, where));
            }

            /// <summary>
            /// Opens condition-selected identities through an exact existing index resolved by identifier.<br/>
            /// The identifier must already exist and enforce one key per identity; no ordering support is created implicitly.<br/>
            /// </summary>
            /// <param name="where">Completed condition used to select identities.<br/></param>
            /// <param name="indexIdentifier">Existing index identifier accepted by <see cref="RecordStore{T}.GetIndex(string, bool)"/>.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <returns>An ordered payload-free identity reader over matching indexed identities.<br/></returns>
            public IdentityReader OpenIdentities(
                Condition<T>.EndCondition where,
                string indexIdentifier,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentException.ThrowIfNullOrWhiteSpace(indexIdentifier);
                return OpenIdentities(where, store.GetIndex(indexIdentifier), direction);
            }

            /// <summary>
            /// Resumes condition-selected identities in one exact existing index order from a compatible opaque bookmark.<br/>
            /// The same condition, index, and traversal direction are required because each participates in the continuation contract.<br/>
            /// </summary>
            /// <param name="where">The same completed condition used to capture the bookmark.<br/></param>
            /// <param name="index">The same exact store-owned index used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible reader.<br/></param>
            /// <param name="direction">The same traversal direction used to capture the bookmark.<br/></param>
            /// <returns>A reader positioned before the next unread matching indexed identity.<br/></returns>
            public IdentityReader OpenIdentities(
                Condition<T>.EndCondition where,
                RecordIndex index,
                RecordBookmark bookmark,
                RetrievalDirection direction = RetrievalDirection.Ascending)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(bookmark);
                return new IdentityReader(store, store.CreateIndexRowPlan(index, direction, where, bookmark));
            }

            /// <summary>Opens an identity-only reader for persistent State and an optional mask.<br/></summary>
            /// <param name="state">State value to match.<br/></param>
            /// <param name="mask">Optional State mask.<br/></param>
            /// <returns>An identity reader positioned before the first match.<br/></returns>
            public IdentityReader OpenIdentities(long state, long? mask = null)
                => new(store, store.CreateStateRowPlan(state, mask));

            /// <summary>Opens a payload-free reader over developer-supplied identities without hidden replay buffering.<br/></summary>
            /// <param name="identities">Store-owned identities in requested source order.<br/></param>
            /// <returns>An identity reader whose reset/fork capabilities reflect the supplied source.<br/></returns>
            public IdentityReader OpenIdentities(IEnumerable<ulong> identities)
            {
                ArgumentNullException.ThrowIfNull(identities);
                return new IdentityReader(store, store.CreateIdentityRowPlan(identities));
            }

            /// <summary>Resumes a natural-order identity-only reader from an opaque bookmark.<br/></summary>
            /// <param name="bookmark">Bookmark captured by a compatible identity reader.<br/></param>
            /// <returns>An identity reader positioned before the next unread identity.<br/></returns>
            public IdentityReader OpenIdentities(RecordBookmark bookmark)
            {
                ArgumentNullException.ThrowIfNull(bookmark);
                return new IdentityReader(store, store.CreateAllRowPlan(origin: bookmark));
            }

            /// <summary>Resumes a natural plan-order conditional identity-only reader from an opaque bookmark.<br/></summary>
            /// <param name="where">The same completed condition used to capture the bookmark.<br/></param>
            /// <param name="bookmark">Bookmark captured by a compatible identity reader.<br/></param>
            /// <returns>An identity reader positioned before the next unread identity.<br/></returns>
            public IdentityReader OpenIdentities(Condition<T>.EndCondition where, RecordBookmark bookmark)
            {
                ArgumentNullException.ThrowIfNull(where);
                ArgumentNullException.ThrowIfNull(bookmark);
                return new IdentityReader(store, store.CreateConditionRowPlan(where, origin: bookmark));
            }

            /// <summary>
            /// Opens the canonical allocation-conscious value reader in natural order.<br/>
            /// Each selected value is interpreted explicitly through <c>reader.Get(...).As...</c>, avoiding an unrestricted generic projection surface.<br/>
            /// </summary>
            /// <param name="valuePaths">Property, field, collection, or admitted expression paths exposed by ordinal and canonical path.<br/></param>
            /// <returns>A value reader positioned before the first result.<br/></returns>
            public ValueDataReader OpenValues(params string[] valuePaths)
                => store.OpenValuesReader(valuePaths);

            /// <summary>Opens the canonical allocation-conscious value reader for a completed condition.<br/></summary>
            /// <param name="where">Completed condition to execute.<br/></param>
            /// <param name="valuePaths">Selected value paths.<br/></param>
            /// <returns>A value reader positioned before the first match.<br/></returns>
            public ValueDataReader OpenValues(Condition<T>.EndCondition where, params string[] valuePaths)
                => store.OpenValuesReader(where, valuePaths);

            /// <summary>Opens the canonical value reader in ascending order of one exact developer-selected index.<br/></summary>
            /// <param name="index">Exact store-owned index defining population and order.<br/></param>
            /// <param name="valuePaths">Selected value paths.<br/></param>
            /// <returns>A value reader positioned before the first indexed result.<br/></returns>
            public ValueDataReader OpenValues(RecordIndex index, params string[] valuePaths)
                => store.OpenValuesReader(index, valuePaths);

            /// <summary>Opens the canonical value reader in the requested direction of one exact developer-selected index.<br/></summary>
            /// <param name="index">Exact store-owned index defining population and order.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <param name="valuePaths">Selected value paths.<br/></param>
            /// <returns>A value reader positioned before the first indexed result.<br/></returns>
            public ValueDataReader OpenValues(RecordIndex index, RetrievalDirection direction, params string[] valuePaths)
                => store.OpenValuesReader(index, direction, valuePaths);

            /// <summary>Opens the canonical identity-aware allocation-conscious value reader in natural order.<br/></summary>
            /// <param name="valuePaths">Selected value paths.<br/></param>
            /// <returns>An identity-aware value reader positioned before the first result.<br/></returns>
            public ValueDataReaderWithIds OpenValuesWithIds(params string[] valuePaths)
                => store.OpenValuesReaderWithIds(valuePaths);

            /// <summary>Opens the canonical identity-aware allocation-conscious value reader for a completed condition.<br/></summary>
            /// <param name="where">Completed condition to execute.<br/></param>
            /// <param name="valuePaths">Selected value paths.<br/></param>
            /// <returns>An identity-aware value reader positioned before the first match.<br/></returns>
            public ValueDataReaderWithIds OpenValuesWithIds(Condition<T>.EndCondition where, params string[] valuePaths)
                => store.OpenValuesReaderWithIds(where, valuePaths);

            /// <summary>Opens the canonical identity-aware value reader in ascending order of one exact developer-selected index.<br/></summary>
            /// <param name="index">Exact store-owned index defining population and order.<br/></param>
            /// <param name="valuePaths">Selected value paths.<br/></param>
            /// <returns>An identity-aware value reader positioned before the first indexed result.<br/></returns>
            public ValueDataReaderWithIds OpenValuesWithIds(RecordIndex index, params string[] valuePaths)
                => store.OpenValuesReaderWithIds(index, valuePaths);

            /// <summary>Opens the canonical identity-aware value reader in the requested direction of one exact developer-selected index.<br/></summary>
            /// <param name="index">Exact store-owned index defining population and order.<br/></param>
            /// <param name="direction">Whole-index traversal direction.<br/></param>
            /// <param name="valuePaths">Selected value paths.<br/></param>
            /// <returns>An identity-aware value reader positioned before the first indexed result.<br/></returns>
            public ValueDataReaderWithIds OpenValuesWithIds(RecordIndex index, RetrievalDirection direction, params string[] valuePaths)
                => store.OpenValuesReaderWithIds(index, direction, valuePaths);

            /// <summary>Opens a typed selector-aware value reader in natural order without hydrating records.<br/></summary>
            /// <typeparam name="TValue">Exact scalar result type inferred from the selector.<br/></typeparam>
            /// <param name="selector">Direct or computed selector to evaluate.<br/></param>
            /// <returns>A typed selector-value reader positioned before the first result.<br/></returns>
            public DataReader<TValue?> OpenValues<TValue>(Selector<T>.IValue<TValue> selector)
                => store.OpenValuesReader(selector);

            /// <summary>Opens a typed selector-aware value reader for a completed condition in its selected source order.<br/></summary>
            /// <typeparam name="TValue">Exact scalar result type inferred from the selector.<br/></typeparam>
            /// <param name="where">Completed condition selecting records.<br/></param>
            /// <param name="selector">Direct or computed selector to evaluate.<br/></param>
            /// <returns>A typed selector-value reader positioned before the first match.<br/></returns>
            public DataReader<TValue?> OpenValues<TValue>(Condition<T>.EndCondition where, Selector<T>.IValue<TValue> selector)
                => store.OpenValuesReader(where, selector);

            /// <summary>Opens two independently typed selector projections during one borrowed payload visit.<br/></summary>
            /// <typeparam name="TValue1">Exact first selector result type.<br/></typeparam>
            /// <typeparam name="TValue2">Exact second selector result type.<br/></typeparam>
            /// <param name="selector1">First direct or computed selector.<br/></param>
            /// <param name="selector2">Second direct or computed selector.<br/></param>
            /// <returns>A typed pair reader positioned before the first result.<br/></returns>
            public DataReader<(TValue1? Value1, TValue2? Value2)> OpenValues<TValue1, TValue2>(Selector<T>.IValue<TValue1> selector1, Selector<T>.IValue<TValue2> selector2)
                => store.OpenValuesReader(selector1, selector2);

            /// <summary>Opens an identity-aware typed selector projection in natural order.<br/></summary>
            /// <typeparam name="TValue">Exact scalar result type inferred from the selector.<br/></typeparam>
            /// <param name="selector">Direct or computed selector to evaluate.<br/></param>
            /// <returns>An identity-and-selector-value reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, TValue? Value)> OpenValuesWithIds<TValue>(Selector<T>.IValue<TValue> selector)
                => store.OpenValuesReaderWithIds(selector);

            /// <summary>Opens an identity-aware typed selector projection for a completed condition in its selected source order.<br/></summary>
            /// <typeparam name="TValue">Exact scalar result type inferred from the selector.<br/></typeparam>
            /// <param name="where">Completed condition selecting records.<br/></param>
            /// <param name="selector">Direct or computed selector to evaluate.<br/></param>
            /// <returns>An identity-and-selector-value reader positioned before the first match.<br/></returns>
            public DataReader<(ulong ID, TValue? Value)> OpenValuesWithIds<TValue>(Condition<T>.EndCondition where, Selector<T>.IValue<TValue> selector)
                => store.OpenValuesReaderWithIds(where, selector);

            /// <summary>Opens a typed single-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">Requested property type.<br/></typeparam>
            /// <param name="propPath1">Property path to read.<br/></param>
            /// <returns>A typed value reader positioned before the first result.<br/></returns>
            public DataReader<T1?> OpenValues<T1>(string propPath1)
                => store.OpenValuesReader<T1>(propPath1);

            /// <summary>Opens a typed two-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <returns>A typed tuple reader positioned before the first result.<br/></returns>
            public DataReader<(T1? Value1, T2? Value2)> OpenValues<T1, T2>(string propPath1, string propPath2)
                => store.OpenValuesReader<T1, T2>(propPath1, propPath2);

            /// <summary>Opens an identity-aware typed single-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">Requested property type.<br/></typeparam>
            /// <param name="propPath1">Property path to read.<br/></param>
            /// <returns>An identity-and-value reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, T1? Value1)> OpenValuesWithIds<T1>(string propPath1)
                => store.OpenValuesReaderWithIds<T1>(propPath1);

            /// <summary>Opens an identity-aware typed two-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <returns>An identity-and-values reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesWithIds<T1, T2>(string propPath1, string propPath2)
                => store.OpenValuesReaderWithIds<T1, T2>(propPath1, propPath2);

            /// <summary>Opens a typed three-property reader in natural order without a per-row container allocation beyond the value tuple itself.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <returns>A typed tuple reader positioned before the first result.<br/></returns>
            public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValues<T1, T2, T3>(string propPath1, string propPath2, string propPath3)
                => store.OpenValuesReader<T1, T2, T3>(propPath1, propPath2, propPath3);

            /// <summary>Opens a typed four-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <returns>A typed tuple reader positioned before the first result.<br/></returns>
            public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValues<T1, T2, T3, T4>(string propPath1, string propPath2, string propPath3, string propPath4)
                => store.OpenValuesReader<T1, T2, T3, T4>(propPath1, propPath2, propPath3, propPath4);

            /// <summary>Opens a typed five-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <typeparam name="T5">Fifth requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <param name="propPath5">Fifth property path.<br/></param>
            /// <returns>A typed tuple reader positioned before the first result.<br/></returns>
            public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValues<T1, T2, T3, T4, T5>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
                => store.OpenValuesReader<T1, T2, T3, T4, T5>(propPath1, propPath2, propPath3, propPath4, propPath5);

            /// <summary>Opens a typed six-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <typeparam name="T5">Fifth requested property type.<br/></typeparam>
            /// <typeparam name="T6">Sixth requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <param name="propPath5">Fifth property path.<br/></param>
            /// <param name="propPath6">Sixth property path.<br/></param>
            /// <returns>A typed tuple reader positioned before the first result.<br/></returns>
            public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValues<T1, T2, T3, T4, T5, T6>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
                => store.OpenValuesReader<T1, T2, T3, T4, T5, T6>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6);

            /// <summary>Opens a typed seven-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <typeparam name="T5">Fifth requested property type.<br/></typeparam>
            /// <typeparam name="T6">Sixth requested property type.<br/></typeparam>
            /// <typeparam name="T7">Seventh requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <param name="propPath5">Fifth property path.<br/></param>
            /// <param name="propPath6">Sixth property path.<br/></param>
            /// <param name="propPath7">Seventh property path.<br/></param>
            /// <returns>A typed tuple reader positioned before the first result.<br/></returns>
            public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValues<T1, T2, T3, T4, T5, T6, T7>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
                => store.OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7);

            /// <summary>Opens an identity-aware typed three-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <returns>An identity-and-values reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesWithIds<T1, T2, T3>(string propPath1, string propPath2, string propPath3)
                => store.OpenValuesReaderWithIds<T1, T2, T3>(propPath1, propPath2, propPath3);

            /// <summary>Opens an identity-aware typed four-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <returns>An identity-and-values reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesWithIds<T1, T2, T3, T4>(string propPath1, string propPath2, string propPath3, string propPath4)
                => store.OpenValuesReaderWithIds<T1, T2, T3, T4>(propPath1, propPath2, propPath3, propPath4);

            /// <summary>Opens an identity-aware typed five-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <typeparam name="T5">Fifth requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <param name="propPath5">Fifth property path.<br/></param>
            /// <returns>An identity-and-values reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesWithIds<T1, T2, T3, T4, T5>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
                => store.OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(propPath1, propPath2, propPath3, propPath4, propPath5);

            /// <summary>Opens an identity-aware typed six-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <typeparam name="T5">Fifth requested property type.<br/></typeparam>
            /// <typeparam name="T6">Sixth requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <param name="propPath5">Fifth property path.<br/></param>
            /// <param name="propPath6">Sixth property path.<br/></param>
            /// <returns>An identity-and-values reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesWithIds<T1, T2, T3, T4, T5, T6>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
                => store.OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6);

            /// <summary>Opens an identity-aware typed seven-property reader in natural order.<br/></summary>
            /// <typeparam name="T1">First requested property type.<br/></typeparam>
            /// <typeparam name="T2">Second requested property type.<br/></typeparam>
            /// <typeparam name="T3">Third requested property type.<br/></typeparam>
            /// <typeparam name="T4">Fourth requested property type.<br/></typeparam>
            /// <typeparam name="T5">Fifth requested property type.<br/></typeparam>
            /// <typeparam name="T6">Sixth requested property type.<br/></typeparam>
            /// <typeparam name="T7">Seventh requested property type.<br/></typeparam>
            /// <param name="propPath1">First property path.<br/></param>
            /// <param name="propPath2">Second property path.<br/></param>
            /// <param name="propPath3">Third property path.<br/></param>
            /// <param name="propPath4">Fourth property path.<br/></param>
            /// <param name="propPath5">Fifth property path.<br/></param>
            /// <param name="propPath6">Sixth property path.<br/></param>
            /// <param name="propPath7">Seventh property path.<br/></param>
            /// <returns>An identity-and-values reader positioned before the first result.<br/></returns>
            public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesWithIds<T1, T2, T3, T4, T5, T6, T7>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
                => store.OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7);

            /// <summary>
            /// Opens a typed reader over unique values from one direct property path.<br/>
            /// Exact ordered indexes suppress adjacent key runs with constant retained state; topology-independent fallback stores only typed unique outputs.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact projected property type.<br/></typeparam>
            /// <param name="propPath">Direct Inheto property path to project.<br/></param>
            /// <returns>A forward-only distinct-value reader positioned before the first value.<br/></returns>
            public DistinctReader<TValue?> OpenDistinct<TValue>(string propPath)
                => store.OpenDistinctReader<TValue>(propPath);

            /// <summary>
            /// Opens a typed distinct-value reader over records selected by a completed condition.<br/>
            /// The condition controls participation while the direct path controls the projected value shape.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact projected property type.<br/></typeparam>
            /// <param name="where">Completed condition selecting contributing records.<br/></param>
            /// <param name="propPath">Direct Inheto property path to project.<br/></param>
            /// <returns>A forward-only distinct-value reader positioned before the first value.<br/></returns>
            public DistinctReader<TValue?> OpenDistinct<TValue>(Condition<T>.EndCondition where, string propPath)
                => store.OpenDistinctReader<TValue>(where, propPath);

            /// <summary>
            /// Opens a typed distinct-value reader from one direct property expression.<br/>
            /// The expression is normalized once and uses the same execution paths as textual property input.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact projected property type inferred from the expression.<br/></typeparam>
            /// <param name="property">Direct property, field, or admitted parameterless-member expression.<br/></param>
            /// <returns>A forward-only distinct-value reader positioned before the first value.<br/></returns>
            public DistinctReader<TValue?> OpenDistinct<TValue>(Expression<Func<T, TValue>> property)
                => store.OpenDistinctReader(property);

            /// <summary>
            /// Opens a typed distinct-value reader from one reusable direct or calculated selector.<br/>
            /// The selector evaluator is compiled once and fallback deduplication remains typed.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact selector result type.<br/></typeparam>
            /// <param name="selector">Direct or calculated selector defining each projected value.<br/></param>
            /// <returns>A forward-only distinct-value reader positioned before the first value.<br/></returns>
            public DistinctReader<TValue?> OpenDistinct<TValue>(Selector<T>.IValue<TValue> selector)
                => store.OpenDistinctReader(selector);

            /// <summary>
            /// Opens a typed distinct-value reader from one reusable selector over a completed condition.<br/>
            /// The public call remains identical whether LibraDex supplies candidates or Fractal/Inheto supplies the complete scan.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact selector result type.<br/></typeparam>
            /// <param name="where">Completed condition selecting contributing records.<br/></param>
            /// <param name="selector">Direct or calculated selector defining each projected value.<br/></param>
            /// <returns>A forward-only distinct-value reader positioned before the first value.<br/></returns>
            public DistinctReader<TValue?> OpenDistinct<TValue>(Condition<T>.EndCondition where, Selector<T>.IValue<TValue> selector)
                => store.OpenDistinctReader(where, selector);

            /// <summary>
            /// Opens a typed reader over unique structural pairs from two direct property paths.<br/>
            /// Numeric components remain unboxed and binary components retain structural equality through the typed tuple comparer.<br/>
            /// </summary>
            /// <typeparam name="TValue1">Exact first projected property type.<br/></typeparam>
            /// <typeparam name="TValue2">Exact second projected property type.<br/></typeparam>
            /// <param name="propPath1">First direct property path.<br/></param>
            /// <param name="propPath2">Second direct property path.<br/></param>
            /// <returns>A forward-only distinct structural-pair reader positioned before the first pair.<br/></returns>
            public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinct<TValue1, TValue2>(string propPath1, string propPath2)
                => store.OpenDistinctReader<TValue1, TValue2>(propPath1, propPath2);

            /// <summary>
            /// Opens a typed distinct structural-pair reader over records selected by a completed condition.<br/>
            /// Both properties are projected during one payload visit and only unique typed pairs are retained.<br/>
            /// </summary>
            /// <typeparam name="TValue1">Exact first projected property type.<br/></typeparam>
            /// <typeparam name="TValue2">Exact second projected property type.<br/></typeparam>
            /// <param name="where">Completed condition selecting contributing records.<br/></param>
            /// <param name="propPath1">First direct property path.<br/></param>
            /// <param name="propPath2">Second direct property path.<br/></param>
            /// <returns>A forward-only distinct structural-pair reader positioned before the first pair.<br/></returns>
            public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinct<TValue1, TValue2>(Condition<T>.EndCondition where, string propPath1, string propPath2)
                => store.OpenDistinctReader<TValue1, TValue2>(where, propPath1, propPath2);

            /// <summary>
            /// Opens a typed distinct structural-pair reader from two direct property expressions.<br/>
            /// The expressions are normalized once and avoid caller-created tuple or set ceremony.<br/>
            /// </summary>
            public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinct<TValue1, TValue2>(Expression<Func<T, TValue1>> property1, Expression<Func<T, TValue2>> property2)
                => store.OpenDistinctReader(property1, property2);

            /// <summary>
            /// Opens a typed distinct structural-pair reader from two reusable direct or calculated selectors.<br/>
            /// Both evaluators compile once and the hot loop retains typed tuple keys rather than object arrays.<br/>
            /// </summary>
            public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinct<TValue1, TValue2>(Selector<T>.IValue<TValue1> selector1, Selector<T>.IValue<TValue2> selector2)
                => store.OpenDistinctReader(selector1, selector2);

            /// <summary>
            /// Opens a sorted typed reader over unique values from one direct property.<br/>
            /// Exact ordered indexes stream distinct key runs; fallback preparation sorts only the unique value set.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact projected property type.<br/></typeparam>
            /// <param name="value">Required property path and optional per-key direction/case descriptor.<br/></param>
            /// <returns>A distinct-value reader whose output order is explicit and stable for the same values.<br/></returns>
            public DistinctReader<TValue?> OpenDistinctSorted<TValue>(SortBy value)
                => store.OpenDistinctSortedReader<TValue>(value);

            /// <summary>
            /// Opens a sorted typed distinct-value reader over a completed condition.<br/>
            /// Preparation is deferred until explicitly requested or required by the first read.<br/>
            /// </summary>
            public DistinctReader<TValue?> OpenDistinctSorted<TValue>(Condition<T>.EndCondition where, SortBy value)
                => store.OpenDistinctSortedReader<TValue>(where, value);

            /// <summary>
            /// Opens a sorted typed distinct-value reader from one reusable direct or calculated selector.<br/>
            /// Fallback preparation retains only unique selector outputs before sorting.<br/>
            /// </summary>
            public DistinctReader<TValue?> OpenDistinctSorted<TValue>(Selector<T>.IValue<TValue> selector, bool descending = false)
                => store.OpenDistinctSortedReader(selector, descending);

            /// <summary>
            /// Opens a lexicographically sorted typed reader over unique structural pairs.<br/>
            /// Each component accepts the same path-and-direction descriptors used by <see cref="OpenSorted(SortBy, SortBy[])"/>.<br/>
            /// </summary>
            public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctSorted<TValue1, TValue2>(SortBy value1, SortBy value2)
                => store.OpenDistinctSortedReader<TValue1, TValue2>(value1, value2);

            /// <summary>
            /// Opens a lexicographically sorted typed distinct structural-pair reader over a completed condition.<br/>
            /// The fallback reader stores one value tuple per unique output rather than one ordering row per contributing record.<br/>
            /// </summary>
            public DistinctReader<(TValue1? Value1, TValue2? Value2)> OpenDistinctSorted<TValue1, TValue2>(Condition<T>.EndCondition where, SortBy value1, SortBy value2)
                => store.OpenDistinctSortedReader<TValue1, TValue2>(where, value1, value2);

        }

        /// <summary>
        /// Groups store mutation entry points in a compact, allocation-free capability view.<br/>
        /// Methods retain the exact write, convergence, State, and zero-write semantics of their direct compatibility counterparts.<br/>
        /// </summary>
        public readonly struct WriterOperations
        {
            private readonly RecordStore<T> store;

            internal WriterOperations(RecordStore<T> store) => this.store = store;

            /// <summary>Writes one record using the store identity generator.<br/></summary>
            /// <param name="record">Record to serialize and persist.<br/></param>
            /// <param name="state">Optional persistent State value.<br/></param>
            /// <returns>The persisted record identity.<br/></returns>
            public ulong Put(T record, long? state = null)
                => store.Put(record, state);

            /// <summary>Writes one record at an explicit identity.<br/></summary>
            /// <param name="recordID">Store-owned record identity.<br/></param>
            /// <param name="record">Record to serialize and persist.<br/></param>
            /// <param name="state">Optional persistent State value.<br/></param>
            /// <returns>The persisted record identity.<br/></returns>
            public ulong Put(ulong recordID, T record, long? state = null)
                => store.Put(recordID, record, state);

            /// <summary>Writes a sequence of records with one optional State value.<br/></summary>
            /// <param name="records">Records to serialize and persist.<br/></param>
            /// <param name="state">Optional persistent State applied to every record.<br/></param>
            /// <returns>The persisted identities in input order.<br/></returns>
            public IReadOnlyList<ulong> PutMany(IEnumerable<T> records, long? state = null)
                => store.PutMany(records, state);

            /// <summary>
            /// Begins a progressive write batch that accepts records incrementally and automatically publishes bounded checkpoints.<br/>
            /// Calling <c>Commit()</c> publishes the current partial checkpoint without closing the batch; <c>Complete()</c> publishes the tail and closes it.<br/>
            /// </summary>
            /// <param name="options">Optional checkpoint, worker, identity-retention, and observation settings; omission uses the low-ceremony 2,048-record default.<br/></param>
            /// <param name="state">Optional persistent State value applied to every record accepted by this batch.<br/></param>
            /// <returns>A sequential caller-owned batch facade that must be completed, aborted, or disposed.<br/></returns>
            public WriteBatch BeginBatch(RecordWriteBatchOptions? options = null, long? state = null)
                => new(store, options, state);

            /// <summary>
            /// Begins a progressive write batch with the specified automatic checkpoint record bound.<br/>
            /// This overload keeps the common streaming case concise while preserving the full options overload for worker and observation controls.<br/>
            /// </summary>
            /// <param name="checkpointRecordCount">Positive maximum records retained before an automatic continuing commit.<br/></param>
            /// <param name="state">Optional persistent State value applied to every record accepted by this batch.<br/></param>
            /// <returns>A sequential caller-owned batch facade that must be completed, aborted, or disposed.<br/></returns>
            public WriteBatch BeginBatch(int checkpointRecordCount, long? state = null)
                => new(
                    store,
                    new RecordWriteBatchOptions { CheckpointRecordCount = checkpointRecordCount },
                    state);

            /// <summary>Deletes one record and its derived index reality.<br/></summary>
            /// <param name="recordID">Store-owned identity to delete.<br/></param>
            /// <param name="zeroWrite">Whether Fractal should overwrite the deleted payload capacity before tombstoning.<br/></param>
            /// <returns><see langword="true"/> when a record was deleted.<br/></returns>
            public bool Delete(ulong recordID, bool zeroWrite = false)
                => store.Delete(recordID, zeroWrite);

            /// <summary>Deletes multiple records and optionally compacts after deletion.<br/></summary>
            /// <param name="recordIDs">Store-owned identities to delete.<br/></param>
            /// <param name="zeroWrite">Whether deleted payload capacity should be overwritten.<br/></param>
            /// <param name="compactAfter">Whether to run store compaction after deletion.<br/></param>
            /// <param name="compactTimeout">Optional compaction timeout.<br/></param>
            /// <returns>The identities actually deleted.<br/></returns>
            public IReadOnlyList<ulong> DeleteMany(
                IEnumerable<ulong> recordIDs,
                bool zeroWrite = false,
                bool compactAfter = false,
                TimeSpan? compactTimeout = null)
                => store.DeleteMany(recordIDs, zeroWrite, compactAfter, compactTimeout);

            /// <summary>Sets persistent State for one existing record.<br/></summary>
            /// <param name="recordID">Store-owned record identity.<br/></param>
            /// <param name="state">State value to persist.<br/></param>
            /// <returns><see langword="true"/> when the record exists and State was set.<br/></returns>
            public bool SetState(ulong recordID, long state) => store.SetState(recordID, state);

            /// <summary>Clears persistent State for one existing record.<br/></summary>
            /// <param name="recordID">Store-owned record identity.<br/></param>
            /// <returns><see langword="true"/> when State was cleared.<br/></returns>
            public bool ClearState(ulong recordID) => store.ClearState(recordID);
        }

        /// <summary>
        /// Groups store-scoped index repair and cache housekeeping operations.<br/>
        /// Operations whose true scope is the complete RecordBase intentionally remain on RecordBase.<br/>
        /// </summary>
        public readonly struct MaintenanceOperations
        {
            private readonly RecordStore<T> store;

            internal MaintenanceOperations(RecordStore<T> store) => this.store = store;

            /// <summary>Rebuilds one ordinary index from authoritative Fractal/Inheto records.<br/></summary>
            /// <param name="index">Store-owned index handle to rebuild.<br/></param>
            /// <returns><see langword="true"/> when the rebuild completed.<br/></returns>
            public bool RebuildIndex(RecordIndex index) => store.RebuildIndex(index);

            /// <summary>Rebuilds one ordinary index resolved by identifier.<br/></summary>
            /// <param name="indexIdentifier">Logical index identifier.<br/></param>
            /// <returns><see langword="true"/> when the rebuild completed.<br/></returns>
            public bool RebuildIndex(string indexIdentifier) => store.RebuildIndex(indexIdentifier);

            /// <summary>Rebuilds one ordinary index resolved by property path.<br/></summary>
            /// <param name="key">Indexed property path.<br/></param>
            /// <returns><see langword="true"/> when the rebuild completed.<br/></returns>
            public bool RebuildIndex(PropPath key) => store.RebuildIndex(key);

            /// <summary>Trims eligible volatile auto-indexes under the configured eviction policy.<br/></summary>
            /// <returns>The number of auto-indexes removed.<br/></returns>
            public int TrimAutoIndexes() => store.TrimAutoIndexes();

            /// <summary>Removes one ordinary index while retaining authoritative records and reserved system indexes.<br/></summary>
            /// <param name="index">Store-owned ordinary index handle.<br/></param>
            /// <returns><see langword="true"/> when an index was removed.<br/></returns>
            public bool RemoveIndex(RecordIndex index) => store.RemoveIndex(index);

            /// <summary>Removes one ordinary index by identifier.<br/></summary>
            /// <param name="indexIdentifier">Logical index identifier.<br/></param>
            /// <returns><see langword="true"/> when an index was removed.<br/></returns>
            public bool RemoveIndex(string indexIdentifier) => store.RemoveIndex(indexIdentifier);
        }
    }
}
