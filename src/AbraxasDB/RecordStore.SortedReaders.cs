using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using LibraDex;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Provides a resettable ordinary row reader whose complete stable ordering is prepared explicitly or by the first consuming operation.<br/>
        /// Construction captures only the source and ordering intent; preparation reuses an exact index or synchronously builds one private complete order.<br/>
        /// </summary>
        public sealed class SortedReader : IDisposable
        {
            private readonly RecordStore<T> store;
            private readonly ReaderSourcePlan? directPlan;
            private readonly Action<CancellationToken>? directPrepare;
            private readonly Func<PreparedSortState>? prepareFactory;
            private readonly Func<RecordBookmark>? bookmarkTemplateFactory;
            private readonly SortBy[]? bookmarkSort;
            private ulong[]? preparedIdentities;
            private RecordBookmark? bookmarkTemplate;
            private RowReader? current;
            private bool prepared;
            private bool exhausted;
            private bool disposed;

            /// <summary>Creates an immediately complete sorted reader served entirely by an existing exact index.<br/></summary>
            /// <param name="store">Owning record store.<br/></param>
            /// <param name="directPlan">Exact indexed source plan.<br/></param>
            /// <param name="directPrepare">Optional bounded backend initialization that must complete before the direct cursor is opened.<br/></param>
            internal SortedReader(
                RecordStore<T> store,
                ReaderSourcePlan directPlan,
                Action<CancellationToken>? directPrepare = null)
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                this.directPlan = directPlan ?? throw new ArgumentNullException(nameof(directPlan));
                this.directPrepare = directPrepare;
            }

            /// <summary>Creates an unprepared sorted reader over one independent reader-source fork.<br/></summary>
            /// <param name="store">Owning record store.<br/></param>
            /// <param name="prepareFactory">Deferred private complete-order factory invoked only by preparation.<br/></param>
            /// <param name="bookmarkTemplateFactory">Factory for the source-selection bookmark template used by resumable property sorting.<br/></param>
            /// <param name="bookmarkSort">Property-sort descriptors that define the stable semantic ordering in continuation bookmarks.<br/></param>
            internal SortedReader(
                RecordStore<T> store,
                Func<PreparedSortState> prepareFactory,
                Func<RecordBookmark> bookmarkTemplateFactory,
                SortBy[] bookmarkSort)
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                this.prepareFactory = prepareFactory ?? throw new ArgumentNullException(nameof(prepareFactory));
                this.bookmarkTemplateFactory = bookmarkTemplateFactory ?? throw new ArgumentNullException(nameof(bookmarkTemplateFactory));
                this.bookmarkSort = bookmarkSort ?? throw new ArgumentNullException(nameof(bookmarkSort));
            }

            /// <summary>
            /// Creates an unprepared sorted reader for a calculated order that has no durable property-sort identity.<br/>
            /// The reader remains fully usable, but its prepared detached cursor cannot advertise a resumable semantic ordering contract.<br/>
            /// </summary>
            /// <param name="store">Owning record store.<br/></param>
            /// <param name="prepareFactory">Deferred private complete-order factory invoked only by preparation.<br/></param>
            internal SortedReader(RecordStore<T> store, Func<PreparedSortState> prepareFactory)
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                this.prepareFactory = prepareFactory ?? throw new ArgumentNullException(nameof(prepareFactory));
            }

            /// <summary>Gets whether the complete stable ordering has been established and ordinary reading may proceed without preparation work.<br/></summary>
            public bool IsPrepared => prepared;

            /// <summary>Gets whether the currently pinned revision has no unread rows.<br/></summary>
            public bool IsExhausted => exhausted;

            /// <summary>Gets the current record identity after a successful read.<br/></summary>
            public ulong ID => RequireCurrent().ID;

            /// <summary>Gets the zero-based ordinal within the currently pinned revision.<br/></summary>
            public long Ordinal => RequireCurrent().Ordinal;

            /// <summary>
            /// Gets an opaque continuation positioned after the current row in this stable sorted population.<br/>
            /// Exact-index readers retain their native index continuation; privately prepared property sorts combine the original selection contract with the explicit stable ordering contract.<br/>
            /// </summary>
            public RecordBookmark Bookmark
            {
                get
                {
                    RecordBookmark currentBookmark = RequireCurrent().Bookmark;
                    if (prepareFactory is not null && bookmarkTemplateFactory is null)
                    {
                        throw new RecordBookmarkException(
                            RecordBookmarkFailureKind.UnsupportedQuery,
                            "This calculated sorted reader has no durable ordering identity and cannot produce a resumable bookmark.");
                    }
                    if (bookmarkTemplate is null)
                        return currentBookmark;

                    return new RecordBookmark(
                        bookmarkTemplate.StoreInstanceId,
                        bookmarkTemplate.StoreId,
                        bookmarkTemplate.RecordTypeIdentity,
                        bookmarkTemplate.SelectionFingerprint,
                        currentBookmark.ResultShapeFingerprint,
                        BuildOrderingFingerprint(bookmarkSort),
                        bookmarkTemplate.SourceUsn,
                        currentBookmark.ResultsConsumed,
                        currentBookmark.LastIdentity,
                        deterministicOrdering: true,
                        currentBookmark.Consistency,
                        DateTimeOffset.UtcNow);
                }
            }

            /// <summary>
            /// Establishes the complete stable ordering without advancing the reader.<br/>
            /// An exact compatible index makes this bounded setup; otherwise the call may synchronously walk and sort the complete selected population.<br/>
            /// </summary>
            public void Prepare() => Prepare(CancellationToken.None);

            /// <summary>
            /// Establishes the complete stable ordering without advancing the reader and cooperatively observes cancellation during a private scan and sort.<br/>
            /// A canceled preparation publishes no partial result and a later call may retry from the original source beginning.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed while preparing a non-indexed ordering.<br/></param>
            public void Prepare(CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (prepared)
                    return;

                cancellationToken.ThrowIfCancellationRequested();
                if (directPlan is not null)
                {
                    directPrepare?.Invoke(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    current = new RowReader(store, directPlan, nameof(SortedReader));
                    prepared = true;
                    return;
                }

                using PreparedSortState state = prepareFactory!();
                state.Prepare(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                preparedIdentities = state.SnapshotIdentities();
                if (bookmarkTemplateFactory is not null)
                    bookmarkTemplate = bookmarkTemplateFactory();
                current = store.OpenDetachedIdentityRows(preparedIdentities, nameof(SortedReader));
                prepared = true;
            }

            /// <summary>
            /// Advances within the complete stable order, implicitly preparing it on the first call.<br/>
            /// The first call may synchronously walk and sort the complete selected population when no exact compatible index exists.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when current-row members are available.<br/></returns>
            public bool Read() => Read(CancellationToken.None);

            /// <summary>
            /// Advances within the complete stable order and supplies cancellation to implicit first-use preparation.<br/>
            /// Cancellation is ignored after preparation because ordinary cursor advancement is already bounded.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation observed only when this call must prepare the ordering.<br/></param>
            /// <returns><see langword="true"/> when current-row members are available.<br/></returns>
            public bool Read(CancellationToken cancellationToken)
            {
                Prepare(cancellationToken);
                if (!current!.Read())
                {
                    exhausted = true;
                    return false;
                }
                exhausted = false;
                return true;
            }

            /// <summary>
            /// Advances within the complete stable order while suppressing every registered link during later materialization of the current row.<br/>
            /// The first call retains ordinary implicit preparation semantics.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when current-row members are available.<br/></returns>
            public bool ReadNoLinks()
            {
                Prepare();
                if (!current!.ReadNoLinks())
                {
                    exhausted = true;
                    return false;
                }
                exhausted = false;
                return true;
            }

            /// <summary>
            /// Advances within the complete stable order while suppressing only the named registered links during later materialization.<br/>
            /// The first call retains ordinary implicit preparation semantics and unlisted links remain normal.<br/>
            /// </summary>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns><see langword="true"/> when current-row members are available.<br/></returns>
            public bool ReadNoLinks(string link, params string[] additionalLinks)
            {
                Prepare();
                if (!current!.ReadNoLinks(link, additionalLinks))
                {
                    exhausted = true;
                    return false;
                }
                exhausted = false;
                return true;
            }

            /// <summary>Aliases <see cref="Read()"/> for ordinary enumerator vocabulary and retains the same implicit preparation behavior.<br/></summary>
            /// <returns><see langword="true"/> when current-row members are available.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>Reads one current-row property through the ordinary demand-driven row materializer.<br/></summary>
            /// <typeparam name="TValue">Requested property type.<br/></typeparam>
            /// <param name="propPath">Inheto property path.<br/></param>
            /// <returns>The current property value.<br/></returns>
            public TValue? Get<TValue>(string propPath) => RequireCurrent().Get<TValue>(propPath);

            /// <summary>Reads one current-row property using its runtime value shape.<br/></summary>
            /// <param name="propPath">Inheto property path.<br/></param>
            /// <returns>The current property value.<br/></returns>
            public object? Get(string propPath) => RequireCurrent().Get(propPath);

            /// <summary>
            /// Gets the current payload as this store's declared record shape.<br/>
            /// Materialization remains demand-driven; preparing, advancing, or skipping does not construct the record.<br/>
            /// </summary>
            /// <returns>The current row as <typeparamref name="T"/>.<br/></returns>
            public T Get() => RequireCurrent().Get();

            /// <summary>
            /// Gets the current payload as an alternate object or projection shape.<br/>
            /// Materialization remains demand-driven and occurs only when this method is called.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <returns>The current row as <typeparamref name="TOut"/>.<br/></returns>
            public TOut GetAs<TOut>() => RequireCurrent().GetAs<TOut>();

            /// <summary>
            /// Materializes the current sorted row while suppressing every registered link.<br/>
            /// The reader remains positioned on the same row.<br/>
            /// </summary>
            /// <returns>The current row as <typeparamref name="T"/> with registered links left at their default values.<br/></returns>
            public T GetNoLinks() => RequireCurrent().GetNoLinks();

            /// <summary>
            /// Materializes the current sorted row while suppressing only the named registered links.<br/>
            /// Unlisted links retain ordinary behavior and the reader remains positioned on the same row.<br/>
            /// </summary>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns>The current row as <typeparamref name="T"/> with selected links left at their default values.<br/></returns>
            public T GetNoLinks(string link, params string[] additionalLinks)
                => RequireCurrent().GetNoLinks(link, additionalLinks);

            /// <summary>
            /// Reprojects the current sorted row while suppressing every registered link.<br/>
            /// The reader remains positioned on the same row.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <returns>The current row as <typeparamref name="TOut"/> with registered links left at their default values.<br/></returns>
            public TOut GetNoLinks<TOut>() => RequireCurrent().GetNoLinks<TOut>();

            /// <summary>
            /// Reprojects the current sorted row while suppressing only the named registered links.<br/>
            /// Unlisted links retain ordinary behavior and the reader remains positioned on the same row.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns>The current row as <typeparamref name="TOut"/> with selected links left at their default values.<br/></returns>
            public TOut GetNoLinks<TOut>(string link, params string[] additionalLinks)
                => RequireCurrent().GetNoLinks<TOut>(link, additionalLinks);

            /// <summary>
            /// Loads subsequent rows into caller-owned storage using this store's declared record shape.<br/>
            /// The first load may synchronously prepare the complete order; no result collection is allocated.<br/>
            /// </summary>
            /// <param name="destination">Span receiving materialized records.<br/></param>
            /// <returns>The number of destination elements populated before the span filled or the reader was exhausted.<br/></returns>
            public int Load(Span<T> destination)
            {
                int count = 0;
                while (count < destination.Length && Read())
                    destination[count++] = Get();
                return count;
            }

            /// <summary>
            /// Loads subsequent rows into caller-owned storage using an alternate object or projection shape.<br/>
            /// The first load may synchronously prepare the complete order; no result collection is allocated.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <param name="destination">Span receiving reprojected records.<br/></param>
            /// <returns>The number of destination elements populated before the span filled or the reader was exhausted.<br/></returns>
            public int LoadAs<TOut>(Span<TOut> destination)
            {
                int count = 0;
                while (count < destination.Length && Read())
                    destination[count++] = GetAs<TOut>();
                return count;
            }

            /// <summary>
            /// Pulls at most <paramref name="count"/> subsequent rows into a newly allocated exact-length array using this store's declared record shape.<br/>
            /// A short final result is trimmed to its populated length; zero rows return a shared empty array.<br/>
            /// </summary>
            /// <param name="count">Maximum number of rows to retrieve; must not be negative.<br/></param>
            /// <returns>An exact-length array containing the retrieved rows.<br/></returns>
            public T[] Pull(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0)
                    return Array.Empty<T>();

                var result = new T[count];
                int loaded = Load(result);
                if (loaded == count)
                    return result;
                if (loaded == 0)
                    return Array.Empty<T>();
                Array.Resize(ref result, loaded);
                return result;
            }

            /// <summary>
            /// Pulls at most <paramref name="count"/> subsequent rows into a newly allocated exact-length array using an alternate object or projection shape.<br/>
            /// A short final result is trimmed to its populated length; zero rows return a shared empty array.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <param name="count">Maximum number of rows to retrieve; must not be negative.<br/></param>
            /// <returns>An exact-length array containing the reprojected rows.<br/></returns>
            public TOut[] PullAs<TOut>(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0)
                    return Array.Empty<TOut>();

                var result = new TOut[count];
                int loaded = LoadAs<TOut>(result);
                if (loaded == count)
                    return result;
                if (loaded == 0)
                    return Array.Empty<TOut>();
                Array.Resize(ref result, loaded);
                return result;
            }

            /// <summary>Skips within the complete stable order and implicitly prepares it when necessary.<br/></summary>
            /// <param name="count">Maximum rows to skip.<br/></param>
            /// <returns>The number of rows skipped.<br/></returns>
            public int Skip(int count)
            {
                Prepare();
                return current!.Skip(count);
            }

            /// <summary>
            /// Repositions a prepared reader to the beginning of its immutable stable order without repeating preparation.<br/>
            /// Calling this before preparation is a bounded no-op and leaves the first read responsible for preparation.<br/>
            /// </summary>
            public void Reset()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (current is not null && preparedIdentities is not null)
                {
                    current.Dispose();
                    current = store.OpenDetachedIdentityRows(preparedIdentities, nameof(SortedReader));
                }
                else
                {
                    current?.Reset();
                }
                exhausted = false;
            }

            /// <summary>Seeks to the logical beginning of the complete stable order.<br/></summary>
            /// <param name="position">Only <see cref="ReaderPosition.Beginning"/> is valid for this reader.<br/></param>
            public void Seek(ReaderPosition position)
            {
                if (position != ReaderPosition.Beginning)
                    throw new ArgumentOutOfRangeException(nameof(position));
                Reset();
            }

            /// <summary>Releases the prepared ordinary row cursor; an unprepared reader owns no private ordering backend.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                current?.Dispose();
                current = null;
                preparedIdentities = null;
            }

            /// <summary>Returns the prepared row reader or throws when no consuming operation has established it.<br/></summary>
            private RowReader RequireCurrent()
                => current ?? throw new InvalidOperationException("The sorted reader is not prepared or positioned on a current row.");
        }

        /// <summary>Creates a sorted reader over one reusable source plan and property ordering request.<br/></summary>
        private SortedReader OpenSortedReader(
            ReaderSourcePlan sourcePlan,
            SortBy[] sort,
            Func<RecordBookmark>? bookmarkTemplateFactory = null)
        {
            ArgumentNullException.ThrowIfNull(sourcePlan);
            ArgumentNullException.ThrowIfNull(sort);
            if (sort.Length == 0)
                throw new ArgumentException("A sorted reader requires at least one order descriptor.", nameof(sort));

            if (TryCreateExactIndexSortPlan(sourcePlan, sort, out ReaderSourcePlan? direct))
            {
                OnOrderingExecutionPlanned(
                    nameof(ReaderOperations.OpenSorted),
                    OrderingExecutionPlanKind.IndexDirectCompleteStore,
                    sort,
                    sourcePlan.IsCompleteStorePopulation,
                    "A mutation-converged complete LibraDex index supplies the sorted-reader order directly.");
                return new SortedReader(this, direct!);
            }

            OnOrderingExecutionPlanned(
                nameof(ReaderOperations.OpenSorted),
                OrderingExecutionPlanKind.DefensiveCandidateOrdering,
                sort,
                sourcePlan.IsCompleteStorePopulation,
                "No exact complete index preserves the requested sorted-reader semantics; preparation will read authoritative Fractal/Inheto values into a private complete order.");
            SortBy[] captured = (SortBy[])sort.Clone();
            if (bookmarkTemplateFactory is null)
                return new SortedReader(this, () => new PreparedSortState(this, sourcePlan, captured));
            return new SortedReader(
                this,
                () => new PreparedSortState(this, sourcePlan, captured),
                bookmarkTemplateFactory,
                captured);
        }

        /// <summary>
        /// Creates a sorted reader whose population and lexicographic order are supplied by exact existing Abraxas indexes.<br/>
        /// Explicit preparation or the first consuming operation may establish reusable LibraDex inverse support for secondary keys, but never substitutes payload-key sorting or another index.<br/>
        /// </summary>
        /// <param name="sourcePlan">Replayable logical population intersected with the primary index population.<br/></param>
        /// <param name="order">Exact attached index handles and per-key directions.<br/></param>
        /// <returns>An independently disposable index-backed sorted reader.<br/></returns>
        private SortedReader OpenSortedReader(ReaderSourcePlan sourcePlan, IndexSort[] order)
        {
            ArgumentNullException.ThrowIfNull(sourcePlan);
            ArgumentNullException.ThrowIfNull(order);
            if (order.Length == 0)
                throw new ArgumentException("A sorted reader requires at least one exact index.", nameof(order));

            IndexSort[] captured = (IndexSort[])order.Clone();
            for (int i = 0; i < captured.Length; i++)
            {
                if (!captured[i].Index.IsOwnedBy(this))
                    throw new ArgumentException($"Sort index {i} was not issued by this RecordStore.", nameof(order));
            }

            OnExactIndexOrderingExecutionPlanned(captured, sourcePlan.IsCompleteStorePopulation);
            if (captured.Length == 1 && sourcePlan.IsCompleteStorePopulation)
            {
                RetrievalDirection direction = captured[0].Direction == SortDirection.Descending
                    ? RetrievalDirection.Descending
                    : RetrievalDirection.Ascending;
                return new SortedReader(this, CreateIndexRowPlan(captured[0].Index, direction));
            }
            return CreateExactIndexSortedReader(sourcePlan, captured);
        }

        /// <summary>
        /// Creates a replayable sorted reader that initializes exact LibraDex support during <see cref="SortedReader.Prepare()"/> and then streams that index order.<br/>
        /// Complete-store preparation remains bounded to index resolution and support setup; condition requests also collect matching identities before the ordered cursor is exposed.<br/>
        /// Preparation publishes its selected population and exact ordered source only after both are complete, so cancellation cannot expose a partial reader state.<br/>
        /// </summary>
        /// <param name="sourcePlan">Replayable logical population intersected with the primary exact-index population.<br/></param>
        /// <param name="order">Validated exact attached index handles and per-key directions.<br/></param>
        /// <returns>An independently disposable exact-index sorted reader whose explicit preparation owns all backend initialization latency.<br/></returns>
        private SortedReader CreateExactIndexSortedReader(ReaderSourcePlan sourcePlan, IndexSort[] order)
        {
            HashSet<ulong>? selected = null;
            IEnumerable<ulong>? exactOrder = null;
            bool initialized = false;

            void PrepareExact(CancellationToken cancellationToken)
            {
                if (initialized)
                    return;

                HashSet<ulong>? preparedSelection = null;
                if (!sourcePlan.IsCompleteStorePopulation)
                {
                    preparedSelection = new HashSet<ulong>();
                    using ReaderCore source = sourcePlan.OpenBeginning();
                    while (source.Read(requireBinary: false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        preparedSelection.Add(source.ID);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!TryIterateRecordIDsByLibraDexIndexes(
                    order,
                    allowIndexSupportCreation: true,
                    out IEnumerable<ulong>? preparedOrder))
                {
                    throw new InvalidOperationException("Every exact-index OpenSorted component must resolve to a current ordered LibraDex index.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                selected = preparedSelection;
                exactOrder = preparedOrder;
                initialized = true;
            }

            IEnumerable<ulong> OpenIdentities()
            {
                if (!initialized || exactOrder is null)
                    throw new InvalidOperationException("The exact-index sorted reader was opened before preparation completed.");

                foreach (ulong identity in exactOrder)
                {
                    if (selected is null || selected.Contains(identity))
                        yield return identity;
                }
            }

            var plan = new ReaderSourcePlan(
                _ =>
                {
                    BookmarkReadContext context = CreateAllBookmarkContext();
                    return OpenReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        identityCursor: new EnumerableIdentitySourceCursor(OpenIdentities())));
                },
                isCompleteStorePopulation: sourcePlan.IsCompleteStorePopulation);
            return new SortedReader(this, plan, PrepareExact);
        }

        /// <summary>Creates a sorted reader over one reusable source plan and calculated ordering request.<br/></summary>
        private SortedReader OpenSortedReader(ReaderSourcePlan sourcePlan, IndexSelectorBuilder[] order)
        {
            ArgumentNullException.ThrowIfNull(sourcePlan);
            ArgumentNullException.ThrowIfNull(order);
            if (order.Length == 0)
                throw new ArgumentException("A sorted reader requires at least one order descriptor.", nameof(order));

            if (TryCreateExactIndexSortPlan(sourcePlan, order, out ReaderSourcePlan? direct))
            {
                OnCalculatedOrderingExecutionPlanned(
                    nameof(ReaderOperations.OpenSorted),
                    OrderingExecutionPlanKind.IndexDirectCompleteStore,
                    order,
                    sourcePlan.IsCompleteStorePopulation,
                    "A mutation-converged complete LibraDex calculated index supplies the sorted-reader order directly.");
                return new SortedReader(this, direct!);
            }

            OnCalculatedOrderingExecutionPlanned(
                nameof(ReaderOperations.OpenSorted),
                OrderingExecutionPlanKind.DefensiveCandidateOrdering,
                order,
                sourcePlan.IsCompleteStorePopulation,
                "No exact complete calculated index preserves the requested sorted-reader semantics; preparation will evaluate authoritative Fractal/Inheto values into a private complete order.");
            IndexSelectorBuilder[] captured = (IndexSelectorBuilder[])order.Clone();
            return new SortedReader(this, () => new PreparedSortState(this, sourcePlan, captured));
        }

        /// <summary>
        /// Creates a quickly returned unprepared sorted-reader context from one directly supplied typed selector.<br/>
        /// A direct property selector reuses the ordinary property-order planner; a calculated selector receives an internal ordering descriptor without exposing indexing vocabulary or requiring a token source.<br/>
        /// Exact compatible indexes remain bounded at preparation; every other request defers all private ordering work until explicit preparation or the first consuming operation.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar key type inferred from the selector.<br/></typeparam>
        /// <param name="sourcePlan">Replayable logical population to order independently.<br/></param>
        /// <param name="order">Direct or calculated durable selector producing the primary key.<br/></param>
        /// <param name="descending">Whether the key should be traversed from greatest to least.<br/></param>
        /// <returns>An independently disposable unprepared sorted reader whose construction never walks the selected population.<br/></returns>
        private SortedReader OpenSortedReader<TValue>(
            ReaderSourcePlan sourcePlan,
            Selector<T>.IValue<TValue> order,
            bool descending)
        {
            ArgumentNullException.ThrowIfNull(sourcePlan);
            ArgumentNullException.ThrowIfNull(order);

            if (order is ISelectorStructure { SimplePropertyPath: string propertyPath })
            {
                return OpenSortedReader(
                    sourcePlan,
                    new[] { new SortBy(new PropPath(propertyPath), descending: descending) });
            }

            IndexSelectorBuilder builder = IndexSelectorBuilder.FromSelector(
                order,
                isText: typeof(TValue) == typeof(string),
                tokenSource: null,
                valueType: typeof(TValue));
            if (descending)
                builder = builder.Descending;
            return OpenSortedReader(sourcePlan, new[] { builder });
        }

        /// <summary>
        /// Creates one quickly returned unprepared sorted-reader context from a textual property path and optional exact text-comparison semantics.<br/>
        /// Plain ordering retains the direct property path so an ordinary compatible index can be reused; culture or comparison options become a durable binary sort-key selector that can match an explicit calculated index.<br/>
        /// </summary>
        /// <param name="sourcePlan">Replayable logical population to order independently.<br/></param>
        /// <param name="propPath">Inheto property path supplying the order key.<br/></param>
        /// <param name="descending">Whether the key should be traversed from greatest to least.<br/></param>
        /// <param name="cultureName">Optional culture name for text ordering; null or empty means invariant culture.<br/></param>
        /// <param name="compareOptions">Text comparison options applied through an exact binary sort-key selector.<br/></param>
        /// <returns>An independently disposable unprepared sorted reader.<br/></returns>
        private SortedReader OpenSortedReader(
            ReaderSourcePlan sourcePlan,
            string propPath,
            bool descending,
            string? cultureName,
            CompareOptions compareOptions)
        {
            ArgumentNullException.ThrowIfNull(sourcePlan);
            ArgumentException.ThrowIfNullOrWhiteSpace(propPath);
            string path = new PropPath(propPath).Path;
            bool hasTextOptions = !string.IsNullOrEmpty(cultureName) || compareOptions != CompareOptions.None;
            if (!hasTextOptions)
            {
                return OpenSortedReader(
                    sourcePlan,
                    new[] { new SortBy(new PropPath(path), descending: descending) });
            }

            if (!TryResolveSimplePropertyType(path, out Type declaredType) || NormalizeValueType(declaredType) != typeof(string))
                throw new InvalidOperationException("Culture and CompareOptions sorting parameters apply only to a property path whose resolved value type is string.");

            Selector<T>.BinarySelector order = new Selector<T>()
                .PropPath(path)
                .AsString
                .SortKey(cultureName ?? string.Empty, compareOptions);
            return OpenSortedReader(sourcePlan, order, descending);
        }

        /// <summary>Opens one detached immutable identity revision as an ordinary flexible row reader.<br/></summary>
        private RowReader OpenDetachedIdentityRows(ulong[] identities, string operationName)
        {
            ArgumentNullException.ThrowIfNull(identities);
            BookmarkReadContext context = CreateAllBookmarkContext();
            return new RowReader(
                this,
                OpenReaderCore(new ReaderOpenRequest(
                    ReaderSourceKind.Identities,
                    context,
                    identityCursor: new BufferedIdentitySourceCursor(identities))),
                operationName);
        }

        /// <summary>Uses an exact complete-population simple index only when it fully preserves the requested property semantics.<br/></summary>
        private bool TryCreateExactIndexSortPlan(
            ReaderSourcePlan sourcePlan,
            SortBy[] sort,
            out ReaderSourcePlan? plan)
        {
            plan = null;
            if (!sourcePlan.IsCompleteStorePopulation || sort.Length != 1 || sort[0].IgnoreCase)
                return false;

            EnsureLibraDexConvergence(nameof(TryCreateExactIndexSortPlan));

            string path = sort[0].Key.Path;
            if (NativeDeserializationOptions.Aliases.Contains(path))
                return false;
            LibraDexSimpleIndex[] candidates = libraDexSimpleIndexes;
            for (int i = 0; i < candidates.Length; i++)
            {
                LibraDexSimpleIndex candidate = candidates[i];
                if (!string.Equals(candidate.Path, path, StringComparison.Ordinal) ||
                    candidate.CalculatedSelector is not null ||
                    !candidate.IncludeNullValues ||
                    !candidate.IncludeEmptyStrings ||
                    (candidate.Capabilities & IndexBackendCapabilities.OrderedRead) == 0 ||
                    (candidate.KeyType != typeof(string) &&
                        candidate.Index.IdentityKeyMultiplicity != LibraDex.IdentityKeyMultiplicity.SingleKeyPerIdentity))
                {
                    continue;
                }

                bool physicalDescending = candidate.Index.LogicalShape?.SortOrder == LibraDex.LibraDexIndexSortOrder.Descending;
                RetrievalDirection direction = physicalDescending == sort[0].Descending
                    ? RetrievalDirection.Ascending
                    : RetrievalDirection.Descending;
                var handle = new RecordIndex(candidate.Path, candidate.Path, candidate.Unique, this);
                plan = CreateIndexRowPlan(handle, direction);
                return true;
            }
            return false;
        }

        /// <summary>Uses an exact calculated index only when its canonical selector, participation, direction, and complete-population contract all match.<br/></summary>
        private bool TryCreateExactIndexSortPlan(
            ReaderSourcePlan sourcePlan,
            IndexSelectorBuilder[] order,
            out ReaderSourcePlan? plan)
        {
            plan = null;
            if (!sourcePlan.IsCompleteStorePopulation || order.Length != 1 || order[0].CaseType != IndexCaseType.None)
                return false;

            EnsureLibraDexConvergence(nameof(TryCreateExactIndexSortPlan));

            if (order[0].CalculatedSelector is ISelectorStructure
                {
                    SemanticDescriptor: IRuntimeSelectorDescriptor<T>
                })
            {
                return false;
            }

            if (TryCreateMaintainedStringSortPlan(order[0], out plan))
                return true;

            SelectorDescriptor<T> descriptor = SelectorDescriptor<T>.FromSelector(order[0].CalculatedSelector);
            LibraDexSimpleIndex[] candidates = libraDexSimpleIndexes;
            for (int i = 0; i < candidates.Length; i++)
            {
                LibraDexSimpleIndex candidate = candidates[i];
                if (!string.Equals(candidate.CalculatedFingerprint, descriptor.SemanticFingerprint, StringComparison.Ordinal) ||
                    !candidate.IncludeNullValues ||
                    !candidate.IncludeEmptyStrings ||
                    (candidate.Capabilities & IndexBackendCapabilities.OrderedRead) == 0 ||
                    candidate.Index.IdentityKeyMultiplicity != LibraDex.IdentityKeyMultiplicity.SingleKeyPerIdentity)
                {
                    continue;
                }

                bool requestedDescending = order[0].Sort == IndexSortDirection.Descending;
                bool physicalDescending = candidate.Index.LogicalShape?.SortOrder == LibraDex.LibraDexIndexSortOrder.Descending;
                RetrievalDirection direction = physicalDescending == requestedDescending
                    ? RetrievalDirection.Ascending
                    : RetrievalDirection.Descending;
                var handle = new RecordIndex(candidate.Path, candidate.Path, candidate.Unique, this);
                plan = CreateIndexRowPlan(handle, direction);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempts to adopt one exact LibraDex-owned culture sort-key projection for a calculated `OpenSorted` request.<br/>
        /// The selector must be a direct string property followed only by `SortKey(culture, options)`; the ordinary Abraxas index must cover null and empty values, and LibraDex must report the exact semantic profile.<br/>
        /// No projection is created or rebuilt here. Absence, mismatch, partial population, or a more complex selector returns <see langword="false"/> so the existing prepared-order fallback remains authoritative.<br/>
        /// </summary>
        /// <param name="order">The single calculated ordering descriptor.<br/></param>
        /// <param name="plan">Receives a replayable direct projection-reader plan when adoption succeeds.<br/></param>
        /// <returns><see langword="true"/> when an existing owned LibraDex profile can stream the complete requested order.<br/></returns>
        private bool TryCreateMaintainedStringSortPlan(
            IndexSelectorBuilder order,
            out ReaderSourcePlan? plan)
        {
            plan = null;
            if (order.CalculatedSelector is not ISelectorStructure structure ||
                structure.SemanticDescriptor is not Selector<T>.StringOperationSelectorDescriptor operation ||
                operation.Operation != StringOperationKind.SortKey ||
                operation.IntegerOperand is not long rawOptions ||
                !operation.TryGetSourcePropertyPath(out string propertyPath))
            {
                return false;
            }

            CompareOptions compareOptions = (CompareOptions)rawOptions;
            LibraDexSimpleIndex[] candidates = libraDexSimpleIndexes;
            for (int i = 0; i < candidates.Length; i++)
            {
                LibraDexSimpleIndex candidate = candidates[i];
                if (!string.Equals(candidate.Path, propertyPath, StringComparison.Ordinal) ||
                    candidate.CalculatedSelector is not null ||
                    candidate.KeyType != typeof(string) ||
                    !candidate.IncludeNullValues ||
                    !candidate.IncludeEmptyStrings ||
                    (candidate.Capabilities & IndexBackendCapabilities.OrderedRead) == 0 ||
                    candidate.Index is not LibraDexStringScalar8Index stringIndex)
                {
                    continue;
                }

                QueryDirection queryDirection = order.Sort == IndexSortDirection.Descending
                    ? QueryDirection.Descending
                    : QueryDirection.Ascending;
                if (!stringIndex.TryIterateSortKeyIdentities(
                    operation.CultureName,
                    compareOptions,
                    queryDirection,
                    out _))
                {
                    continue;
                }

                RetrievalDirection direction = queryDirection == QueryDirection.Descending
                    ? RetrievalDirection.Descending
                    : RetrievalDirection.Ascending;
                var handle = new RecordIndex(candidate.Path, candidate.Path, candidate.Unique, this);
                plan = CreateMaintainedStringSortRowPlan(
                    handle,
                    stringIndex,
                    operation.CultureName,
                    compareOptions,
                    queryDirection,
                    direction);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a replayable Abraxas row plan over one existing LibraDex-owned culture sort-key profile.<br/>
        /// Every open asks LibraDex for a fresh lazy identity sequence, preserving independent reader/reset/bookmark behavior without buffering calculated values or exposing projection keys as authoritative record values.<br/>
        /// </summary>
        /// <param name="handle">The ordinary property index handle used for bookmark identity and diagnostics.<br/></param>
        /// <param name="index">The logical LibraDex string index that owns the selected profile.<br/></param>
        /// <param name="cultureName">The exact requested culture name.<br/></param>
        /// <param name="compareOptions">The exact requested comparison options.<br/></param>
        /// <param name="queryDirection">The requested LibraDex projection traversal direction.<br/></param>
        /// <param name="direction">The equivalent Abraxas retrieval direction for bookmark metadata.<br/></param>
        /// <returns>A complete-population reader source plan that performs no preparation scan.<br/></returns>
        private ReaderSourcePlan CreateMaintainedStringSortRowPlan(
            RecordIndex handle,
            LibraDexStringScalar8Index index,
            string? cultureName,
            CompareOptions compareOptions,
            QueryDirection queryDirection,
            RetrievalDirection direction)
        {
            IIdentitySourceCursor OpenCursor()
            {
                if (!index.TryIterateSortKeyIdentities(
                    cultureName,
                    compareOptions,
                    queryDirection,
                    out IEnumerable<ulong>? identities))
                {
                    throw new InvalidOperationException("The selected LibraDex culture sort-key profile is no longer available.");
                }

                return new EnumerableIdentitySourceCursor(identities);
            }

            return new ReaderSourcePlan(
                bookmark =>
                {
                    BookmarkReadContext context = CreateIndexBookmarkContext(handle, direction, where: null, bookmark: bookmark);
                    return OpenReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        identityCursor: OpenCursor()));
                },
                isCompleteStorePopulation: true,
                openIdentity: bookmark =>
                {
                    BookmarkReadContext context = CreateIndexBookmarkContext(handle, direction, where: null, bookmark: bookmark);
                    return OpenIdentityReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        identityCursor: OpenCursor()));
                });
        }

        /// <summary>
        /// Raises the unified Abraxas ordering diagnostic for a caller-selected exact attached index route.<br/>
        /// The subscriber check precedes key-label allocation, so exact-index readers incur no diagnostic allocation when no observer is attached.<br/>
        /// A complete-store flag reports whether index order can stream directly or must be intersected with a replayable candidate population.<br/>
        /// </summary>
        /// <param name="order">Validated exact attached index handles in lexicographic priority order.<br/></param>
        /// <param name="completeStorePopulation">Whether the logical source represents the complete store population.<br/></param>
        private void OnExactIndexOrderingExecutionPlanned(IndexSort[] order, bool completeStorePopulation)
        {
            EventHandler<OrderingExecutionPlanEventArgs>? handler = OrderingExecutionPlanned;
            if (handler is null)
                return;

            string[] orderingKeys = new string[order.Length];
            for (int i = 0; i < order.Length; i++)
                orderingKeys[i] = order[i].Index.KeyPath;

            handler(this, new OrderingExecutionPlanEventArgs(
                nameof(ReaderOperations.OpenSorted),
                OrderingExecutionPlanKind.IndexDirectCompleteStore,
                orderingKeys,
                completeStorePopulation,
                completeStorePopulation
                    ? "A complete mutation-converged Abraxas index supplies the sorted-reader order directly."
                    : "A mutation-converged Abraxas index supplies the order and is intersected with the replayable source population."));
        }
    }
}
