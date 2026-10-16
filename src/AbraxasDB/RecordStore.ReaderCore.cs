using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FractalKVS;
using Inheto;
using LibraDex;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Identifies a logical reader seek target that is independent of any physical Fractal or LibraDex cursor representation.<br/>
        /// </summary>
        public enum ReaderPosition
        {
            /// <summary>Reopens the reader's original selection at its logical beginning.<br/></summary>
            Beginning = 0
        }

        private enum ReaderSourceKind
        {
            All,
            UnorderedAll,
            Identities,
            Condition
        }

        private readonly struct ReaderOpenRequest
        {
            internal ReaderOpenRequest(
                ReaderSourceKind sourceKind,
                BookmarkReadContext bookmarkContext,
                IEnumerable<ulong>? identities = null,
                IIdentitySourceCursor? identityCursor = null,
                Condition<T>.EndCondition? condition = null,
                SortBy[]? sort = null,
                string? operationName = null,
                bool trustIdentityExistence = false)
            {
                SourceKind = sourceKind;
                BookmarkContext = bookmarkContext;
                Identities = identities;
                IdentityCursor = identityCursor;
                Condition = condition;
                Sort = sort ?? Array.Empty<SortBy>();
                OperationName = operationName;
                TrustIdentityExistence = trustIdentityExistence;
            }

            internal ReaderSourceKind SourceKind { get; }
            internal BookmarkReadContext BookmarkContext { get; }
            internal IEnumerable<ulong>? Identities { get; }
            internal IIdentitySourceCursor? IdentityCursor { get; }
            internal Condition<T>.EndCondition? Condition { get; }
            internal SortBy[] Sort { get; }
            internal string? OperationName { get; }
            internal bool TrustIdentityExistence { get; }
        }

        /// <summary>
        /// Retains one immutable logical reader selection and its centralized open mechanism.<br/>
        /// Every fork receives a new physical cursor while reusable condition, index, bookmark, and source-specific semantics remain represented once.<br/>
        /// </summary>
        internal sealed class ReaderSourcePlan
        {
            private readonly Func<RecordBookmark?, ReaderCore> open;
            private readonly Func<RecordBookmark?, IdentityReaderCore>? openIdentity;

            /// <summary>Creates one replayable logical reader source plan.<br/></summary>
            /// <param name="open">Factory that opens the source at its origin or a compatible bookmark.<br/></param>
            /// <param name="origin">Optional continuation bookmark that defines this reader's logical beginning.<br/></param>
            /// <param name="canReplay">Whether the source can safely open independent cursors.<br/></param>
            /// <param name="isCompleteStorePopulation">Whether the source selects the store's complete record population.<br/></param>
            /// <param name="openIdentity">Optional factory for an independent payload-free identity cursor at the origin or a compatible bookmark.<br/></param>
            internal ReaderSourcePlan(
                Func<RecordBookmark?, ReaderCore> open,
                RecordBookmark? origin = null,
                bool canReplay = true,
                bool isCompleteStorePopulation = false,
                Func<RecordBookmark?, IdentityReaderCore>? openIdentity = null)
            {
                this.open = open ?? throw new ArgumentNullException(nameof(open));
                this.openIdentity = openIdentity;
                Origin = origin;
                CanReplay = canReplay;
                IsCompleteStorePopulation = isCompleteStorePopulation;
            }

            /// <summary>Gets the continuation that defines this plan's beginning, or null for the beginning of its complete selection.<br/></summary>
            internal RecordBookmark? Origin { get; }

            /// <summary>Gets whether independent cursors may safely be opened from this source.<br/></summary>
            internal bool CanReplay { get; }

            /// <summary>Gets whether the selection is the complete current store population.<br/></summary>
            internal bool IsCompleteStorePopulation { get; }

            /// <summary>Opens the first cursor even when this source explicitly declares that it cannot be replayed.<br/></summary>
            internal ReaderCore OpenInitial() => open(Origin);

            /// <summary>Opens the first payload-free identity cursor when this plan supplies one.<br/></summary>
            internal IdentityReaderCore OpenIdentityInitial()
                => (openIdentity ?? throw new NotSupportedException("This reader plan does not expose an identity-only cursor."))(Origin);

            /// <summary>Opens an independent cursor at the plan's original logical beginning.<br/></summary>
            internal ReaderCore OpenBeginning()
            {
                if (!CanReplay)
                    throw new NotSupportedException("This reader source is one-shot and cannot be reset, forked, or sorted independently.");
                return open(Origin);
            }

            /// <summary>Opens an independent cursor at one compatible bookmark.<br/></summary>
            /// <param name="bookmark">Bookmark captured from the same logical selection.<br/></param>
            internal ReaderCore OpenBookmark(RecordBookmark bookmark)
            {
                if (!CanReplay)
                    throw new NotSupportedException("This reader source is one-shot and cannot seek to a bookmark.");
                ArgumentNullException.ThrowIfNull(bookmark);
                return open(bookmark);
            }

            /// <summary>Opens an independent payload-free identity cursor at the plan beginning.<br/></summary>
            internal IdentityReaderCore OpenIdentityBeginning()
            {
                if (!CanReplay)
                    throw new NotSupportedException("This reader source is one-shot and cannot be reset, forked, or sorted independently.");
                return (openIdentity ?? throw new NotSupportedException("This reader plan does not expose an identity-only cursor."))(Origin);
            }

            /// <summary>Opens an independent payload-free identity cursor at a compatible bookmark.<br/></summary>
            internal IdentityReaderCore OpenIdentityBookmark(RecordBookmark bookmark)
            {
                if (!CanReplay)
                    throw new NotSupportedException("This reader source is one-shot and cannot seek to a bookmark.");
                ArgumentNullException.ThrowIfNull(bookmark);
                return (openIdentity ?? throw new NotSupportedException("This reader plan does not expose an identity-only cursor."))(bookmark);
            }
        }

        /// <summary>
        /// Opens the specialized row engine selected from one semantic request.<br/>
        /// Selection, ordering, bookmark, and residual-payload reuse decisions are made once here so the per-row path does not switch among semantic modes.<br/>
        /// </summary>
        /// <param name="request">Complete immutable open-time request.<br/></param>
        /// <returns>A row engine positioned before its first logical result.<br/></returns>
        private ReaderCore OpenReaderCore(in ReaderOpenRequest request)
        {
            switch (request.SourceKind)
            {
                case ReaderSourceKind.All:
                    return new ReaderCore(this, storeID, request.BookmarkContext);

                case ReaderSourceKind.UnorderedAll:
                    return new ReaderCore(this, storeID, request.BookmarkContext, unorderedPhysical: true);

                case ReaderSourceKind.Identities:
                    if (request.IdentityCursor is not null)
                    {
                        return new ReaderCore(
                            this,
                            request.IdentityCursor,
                            request.BookmarkContext,
                            request.TrustIdentityExistence);
                    }
                    return new ReaderCore(
                        this,
                        request.Identities ?? throw new InvalidOperationException("The identity reader request has no identity source."),
                        request.BookmarkContext);

                case ReaderSourceKind.Condition:
                    Condition<T>.EndCondition where = request.Condition ??
                        throw new InvalidOperationException("The condition reader request has no completed condition.");
                    if (request.Sort.Length > 0)
                    {
                        return new ReaderCore(
                            this,
                            new BufferedIdentitySourceCursor(
                                PrepareTransientIdentityOrder(
                                    IteratePlannedIdentityMatches(
                                        where,
                                        request.OperationName ?? nameof(OpenReaderCore)),
                                    request.Sort)),
                            request.BookmarkContext);
                    }

                    return new ReaderCore(
                        this,
                        ResumeBookmarkStream(
                            IteratePlannedConditionMatches(
                                where,
                                request.OperationName ?? nameof(OpenReaderCore),
                                includePayload: true,
                                retainOnlyWhenEvaluated: true),
                            static match => match.RecordId,
                            request.BookmarkContext),
                        request.BookmarkContext);

                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }

        /// <summary>
        /// Opens the identity-only engine selected from one semantic request.<br/>
        /// The resulting hot loop consumes identities directly and never resolves a selected identity back to a Fractal payload.<br/>
        /// Residual condition plans may still inspect authoritative payload bytes while deciding whether an identity matches.<br/>
        /// </summary>
        /// <param name="request">Complete immutable open-time request.<br/></param>
        /// <returns>An identity engine positioned before its first result.<br/></returns>
        private IdentityReaderCore OpenIdentityReaderCore(in ReaderOpenRequest request)
        {
            IIdentitySourceCursor cursor = request.IdentityCursor ?? request.SourceKind switch
            {
                ReaderSourceKind.All => new FractalIdentitySourceCursor(rbase.data.OpenGroupRecordReader(storeID)),
                ReaderSourceKind.UnorderedAll => throw new NotSupportedException("The unordered payload reader does not expose an identity-only cursor."),
                ReaderSourceKind.Identities => new EnumerableIdentitySourceCursor(
                    request.Identities ?? throw new InvalidOperationException("The identity reader request has no identity source.")),
                ReaderSourceKind.Condition when request.Sort.Length > 0 => new BufferedIdentitySourceCursor(
                    PrepareTransientIdentityOrder(
                        IteratePlannedIdentityMatches(
                            request.Condition ?? throw new InvalidOperationException("The condition reader request has no completed condition."),
                            request.OperationName ?? nameof(OpenIdentityReaderCore)),
                        request.Sort)),
                ReaderSourceKind.Condition => new EnumerableIdentitySourceCursor(
                    IteratePlannedIdentityMatches(
                        request.Condition ?? throw new InvalidOperationException("The condition reader request has no completed condition."),
                        request.OperationName ?? nameof(OpenIdentityReaderCore))),
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            };

            try
            {
                return new IdentityReaderCore(this, cursor, request.BookmarkContext);
            }
            catch
            {
                cursor.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Projects the shared planned-condition stream to identities without retaining payloads for result materialization.<br/>
        /// Complete LibraDex plans stay identity-only; residual and scan plans borrow payloads only for synchronous condition evaluation.<br/>
        /// </summary>
        /// <param name="where">Completed condition to evaluate.<br/></param>
        /// <param name="operationName">Operation name used by condition lifecycle diagnostics.<br/></param>
        /// <returns>A lazy stream of matching record identities.<br/></returns>
        private IEnumerable<ulong> IteratePlannedIdentityMatches(
            Condition<T>.EndCondition where,
            string operationName)
        {
            foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                where,
                operationName,
                includePayload: false,
                retainOnlyWhenEvaluated: true))
            {
                yield return match.RecordId;
            }
        }

        /// <summary>
        /// Provides flexible current-row access over the same specialized engine used by semantic object and property readers.<br/>
        /// Property reads and materialization are demand-driven; advancing without accessing values creates no Inheto wrapper beyond the one required to expose current-row access.<br/>
        /// Borrowed UTF-8 spans expire when this reader advances, skips, or is disposed.<br/>
        /// </summary>
        public sealed class RowReader : IDisposable
        {
            private readonly RecordStore<T> store;
            private readonly ReaderSourcePlan? sourcePlan;
            private ReaderCore core;
            private readonly string operationName;
            private LinkReadPolicy? currentLinkPolicy;

            internal RowReader(RecordStore<T> store, ReaderCore core, string operationName)
            {
                this.store = store;
                this.core = core;
                this.operationName = operationName;
                sourcePlan = null;
                try
                {
                    core.BindResultShape(FingerprintBookmarkText("Row:Flexible"));
                }
                catch
                {
                    core.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Opens one public row reader over a reusable logical source plan.<br/>
            /// The first physical cursor and every later fork or reset pass through the same centralized open mechanism.<br/>
            /// </summary>
            /// <param name="store">Owning record store.<br/></param>
            /// <param name="sourcePlan">Reusable logical source plan.<br/></param>
            /// <param name="operationName">Developer-facing operation name used by diagnostics.<br/></param>
            internal RowReader(RecordStore<T> store, ReaderSourcePlan sourcePlan, string operationName)
                : this(store, sourcePlan.OpenInitial(), operationName)
            {
                this.sourcePlan = sourcePlan;
            }

            /// <summary>Gets whether this reader's source can reopen an independent cursor without consuming this reader.<br/></summary>
            public bool CanFork => sourcePlan?.CanReplay == true;

            /// <summary>Gets whether this reader can seek back to its original logical beginning.<br/></summary>
            public bool CanReset => CanFork;

            /// <summary>Gets the current record identity.<br/></summary>
            public ulong ID => core.ID;

            /// <summary>Gets the zero-based current result ordinal, or -1 before the first successful read.<br/></summary>
            public long Ordinal => core.Ordinal;

            /// <summary>Gets an opaque continuation positioned at the next unread logical result.<br/></summary>
            public RecordBookmark Bookmark => core.Bookmark;

            /// <summary>Advances to the next row and prepares demand-driven property or object access.<br/></summary>
            /// <returns><see langword="true"/> when current-row members may be used.<br/></returns>
            public bool Read()
            {
                currentLinkPolicy = null;
                return core.Read(requireBinary: false);
            }

            /// <summary>
            /// Advances to the next row while suppressing every registered link during later object materialization of that row.<br/>
            /// Scalar property access remains available and no link deserializer or application materializer runs unless a later explicit <see cref="Get()"/> requests ordinary behavior.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when current-row members may be used.<br/></returns>
            public bool ReadNoLinks()
            {
                currentLinkPolicy = null;
                if (!core.Read(requireBinary: false))
                    return false;
                currentLinkPolicy = store.Links.SuppressAll();
                return true;
            }

            /// <summary>
            /// Advances to the next row while suppressing only the named registered links during later object materialization of that row.<br/>
            /// Unlisted links retain ordinary Inheto and application materializer behavior.<br/>
            /// </summary>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns><see langword="true"/> when current-row members may be used.<br/></returns>
            public bool ReadNoLinks(string link, params string[] additionalLinks)
            {
                LinkReadPolicy policy = store.Links.Suppress(link, additionalLinks);
                currentLinkPolicy = null;
                if (!core.Read(requireBinary: false))
                    return false;
                currentLinkPolicy = policy;
                return true;
            }

            /// <summary>Advances to the next row.<br/></summary>
            /// <returns><see langword="true"/> when current-row members may be used.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>
            /// Reads one current-row property as the requested type.<br/>
            /// A registered property link is authoritative and invokes only that target's binary reader before adapting its semantic result to <typeparamref name="TValue"/>.<br/>
            /// A link suppressed for the current row returns the requested type's default value; unrelated links are not invoked.<br/>
            /// An exact reversible value already carried by the active index source is returned directly; every other path falls back lazily to the authoritative Inheto payload.<br/>
            /// </summary>
            /// <typeparam name="TValue">Requested property type.<br/></typeparam>
            /// <param name="propPath">Inheto property path.<br/></param>
            /// <returns>The converted value, including <see langword="null"/> where permitted.<br/></returns>
            public TValue? Get<TValue>(string propPath)
            {
                string path = ValidatePath(propPath);
                bool linked = store.Links.TryResolveTarget(path, out string normalizedPath);
                if (linked && currentLinkPolicy?.ExcludedMembers.Contains(normalizedPath) == true)
                    return default;
                if (linked && store.Links.TryReadProperty(
                    core.Binary,
                    core.ID,
                    normalizedPath,
                    typeof(TValue),
                    currentLinkPolicy,
                    core.Options,
                    out object? linkedValue))
                {
                    return linkedValue is null ? default : (TValue)linkedValue;
                }
                if (core.TryProjectCurrentIndexValue(normalizedPath, out TValue? value))
                    return value;
                return core.Binary.ReadPropAs<TValue>(path, core.Options);
            }

            /// <summary>
            /// Reads one current-row property using its runtime value shape.<br/>
            /// A registered property link is authoritative and invokes only that target's binary reader; a suppressed target returns <see langword="null"/>.<br/>
            /// Unlinked properties retain direct Inheto access and do not invoke unrelated links.<br/>
            /// </summary>
            /// <param name="propPath">Inheto property path.<br/></param>
            /// <returns>The runtime value, or <see langword="null"/> for null or missing data.<br/></returns>
            public object? Get(string propPath)
            {
                string path = ValidatePath(propPath);
                bool linked = store.Links.TryResolveTarget(path, out string targetPath);
                if (linked && currentLinkPolicy?.ExcludedMembers.Contains(targetPath) == true)
                    return null;
                if (linked &&
                    store.Links.TryReadProperty(
                    core.Binary,
                    core.ID,
                    targetPath,
                    typeof(object),
                    currentLinkPolicy,
                    core.Options,
                    out object? linkedValue))
                {
                    return linkedValue;
                }
                return core.Binary.ReadProp(path, core.Options);
            }

            /// <summary>
            /// Borrows one current stored string's natural UTF-8 bytes without decoding or copying.<br/>
            /// The span expires on the next read, skip, or dispose.<br/>
            /// </summary>
            /// <param name="propPath">Inheto property path.<br/></param>
            /// <returns>The borrowed UTF-8 bytes, including an empty span for a present empty string.<br/></returns>
            public ReadOnlySpan<byte> GetUtf8Span(string propPath)
            {
                string path = ValidatePath(propPath);
                if (core.Binary.TryReadStringUtf8Payload(path, core.Options, out ReadOnlyMemory<byte> payload))
                    return payload.Span;
                throw new InvalidOperationException($"Property path '{path}' is null or missing in the current row.");
            }

            /// <summary>Attempts to borrow one current stored string's natural UTF-8 bytes.<br/></summary>
            /// <param name="propPath">Inheto property path.<br/></param>
            /// <param name="value">Receives current-row borrowed UTF-8 bytes when present.<br/></param>
            /// <returns><see langword="true"/> for a present string, including an empty string; otherwise <see langword="false"/>.<br/></returns>
            public bool TryGetUtf8Span(string propPath, out ReadOnlySpan<byte> value)
            {
                if (core.Binary.TryReadStringUtf8Payload(ValidatePath(propPath), core.Options, out ReadOnlyMemory<byte> payload))
                {
                    value = payload.Span;
                    return true;
                }
                value = default;
                return false;
            }

            /// <summary>
            /// Gets the current payload as this store's declared record shape.<br/>
            /// Materialization remains demand-driven; advancing or skipping the reader does not construct the record.<br/>
            /// </summary>
            /// <returns>The current row as <typeparamref name="T"/>.<br/></returns>
            public T Get()
                => store.MaterializeReaderObject<T>(core.Binary, core.ID, operationName, currentLinkPolicy);

            /// <summary>
            /// Gets the current payload as an alternate object or projection shape.<br/>
            /// Materialization remains demand-driven and occurs only when this method is called.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <returns>The current row as <typeparamref name="TOut"/>.<br/></returns>
            public TOut GetAs<TOut>()
                => store.MaterializeReaderObject<TOut>(core.Binary, core.ID, operationName, currentLinkPolicy);

            /// <summary>
            /// Materializes the current row while suppressing every registered link, independent of how the row was advanced.<br/>
            /// The current cursor is not moved.<br/>
            /// </summary>
            /// <returns>The current row as <typeparamref name="T"/> with registered links left at their default values.<br/></returns>
            public T GetNoLinks()
                => store.MaterializeReaderObject<T>(core.Binary, core.ID, operationName, store.Links.SuppressAll());

            /// <summary>
            /// Materializes the current row while suppressing only the named registered links.<br/>
            /// Unlisted links retain ordinary behavior and the current cursor is not moved.<br/>
            /// </summary>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns>The current row as <typeparamref name="T"/> with the selected links left at their default values.<br/></returns>
            public T GetNoLinks(string link, params string[] additionalLinks)
                => store.MaterializeReaderObject<T>(core.Binary, core.ID, operationName, store.Links.Suppress(link, additionalLinks));

            /// <summary>
            /// Reprojects the current row while suppressing every registered link.<br/>
            /// The current cursor is not moved.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <returns>The current row as <typeparamref name="TOut"/> with registered links left at their default values.<br/></returns>
            public TOut GetNoLinks<TOut>()
                => store.MaterializeReaderObject<TOut>(core.Binary, core.ID, operationName, store.Links.SuppressAll());

            /// <summary>
            /// Reprojects the current row while suppressing only the named registered links.<br/>
            /// Unlisted links retain ordinary behavior and the current cursor is not moved.<br/>
            /// </summary>
            /// <typeparam name="TOut">Object or projection shape to retrieve.<br/></typeparam>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns>The current row as <typeparamref name="TOut"/> with the selected links left at their default values.<br/></returns>
            public TOut GetNoLinks<TOut>(string link, params string[] additionalLinks)
                => store.MaterializeReaderObject<TOut>(core.Binary, core.ID, operationName, store.Links.Suppress(link, additionalLinks));

            /// <summary>
            /// Loads subsequent rows into caller-owned storage using this store's declared record shape.<br/>
            /// The reader advances once per populated element and performs no collection allocation.<br/>
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
            /// The reader advances once per populated element and performs no collection allocation.<br/>
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

            /// <summary>Advances past at most the requested number of rows without materializing result values.<br/></summary>
            /// <param name="count">Maximum rows to skip; must not be negative.<br/></param>
            /// <returns>The number of rows actually skipped.<br/></returns>
            public int Skip(int count)
            {
                currentLinkPolicy = null;
                return core.Skip(count);
            }

            /// <summary>
            /// Reopens this reader's original logical selection at its beginning while retaining its reusable source plan and open-time optimizations.<br/>
            /// This is convenience vocabulary for <c>Seek(ReaderPosition.Beginning)</c>.<br/>
            /// </summary>
            public void Reset() => Seek(ReaderPosition.Beginning);

            /// <summary>
            /// Repositions this reader to a logical source position by opening a fresh optimized physical cursor.<br/>
            /// Borrowed current-row spans and values expire when the old cursor is released.<br/>
            /// </summary>
            /// <param name="position">Logical position to seek.<br/></param>
            public void Seek(ReaderPosition position)
            {
                if (position != ReaderPosition.Beginning)
                    throw new ArgumentOutOfRangeException(nameof(position));
                ReplaceCore(RequireReplayablePlan().OpenBeginning());
            }

            /// <summary>
            /// Repositions this reader through one compatible opaque bookmark without reconstructing its public reader object.<br/>
            /// Bookmark validation remains centralized in the source-specific open path.<br/>
            /// </summary>
            /// <param name="bookmark">Bookmark captured from the same logical selection and result shape.<br/></param>
            public void Seek(RecordBookmark bookmark)
                => ReplaceCore(RequireReplayablePlan().OpenBookmark(bookmark));

            /// <summary>
            /// Opens an independent reader at this reader's original logical beginning.<br/>
            /// The current cursor is not consumed or repositioned, allowing several independent transformations of the same selection.<br/>
            /// </summary>
            /// <returns>An independently disposable reader over the same logical selection.<br/></returns>
            public RowReader Fork()
                => new(store, RequireReplayablePlan(), operationName);

            /// <summary>
            /// Starts one independent unprepared sorted view over this reader's entire original selection.<br/>
            /// An exact compatible existing index is opened directly and reports complete immediately; otherwise a private memory-backed order is built without consuming this reader.<br/>
            /// </summary>
            /// <param name="sort">One or more exact property-path order descriptors.<br/></param>
            /// <returns>An independently disposable sorted reader.<br/></returns>
            public SortedReader Sort(params SortBy[] sort)
            {
                ArgumentNullException.ThrowIfNull(sort);
                return store.OpenSortedReader(RequireReplayablePlan(), sort);
            }

            /// <summary>
            /// Starts one independent sorted view from a direct or calculated typed selector without converting that selector into an index-definition builder.<br/>
            /// The method returns after bounded intent capture; <see cref="SortedReader.Prepare()"/> or the first consuming operation synchronously establishes the complete stable order.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact scalar ordering-key type inferred from the selector.<br/></typeparam>
            /// <param name="order">Direct or calculated durable selector defining the key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <returns>An independently disposable sorted reader.<br/></returns>
            public SortedReader Sort<TValue>(
                Selector<T>.IValue<TValue> order,
                bool descending = false)
                => store.OpenSortedReader(RequireReplayablePlan(), order, descending);

            /// <summary>
            /// Starts one independent unprepared sorted view using durable direct or calculated selector semantics.<br/>
            /// </summary>
            /// <param name="order">Primary direct or calculated selector including direction.<br/></param>
            /// <param name="thenBy">Optional lower-priority selector keys.<br/></param>
            /// <returns>An independently disposable sorted reader.<br/></returns>
            public SortedReader Sort(IndexSelectorBuilder order, params IndexSelectorBuilder[] thenBy)
            {
                ArgumentNullException.ThrowIfNull(order);
                ArgumentNullException.ThrowIfNull(thenBy);
                var combined = new IndexSelectorBuilder[thenBy.Length + 1];
                combined[0] = order;
                Array.Copy(thenBy, 0, combined, 1, thenBy.Length);
                return store.OpenSortedReader(RequireReplayablePlan(), combined);
            }

            /// <summary>
            /// Starts one independent sorted view over this row reader's original population using exact attached Abraxas indexes.<br/>
            /// The returned reader streams the primary index order directly for a complete-store source and intersects that order with this reader's replayable population for a candidate or condition source.<br/>
            /// No payload-key reconstruction or substitute index selection occurs, preserving the mutation-converged contract of the supplied handles.<br/>
            /// </summary>
            /// <param name="order">Primary exact attached index and requested traversal direction.<br/></param>
            /// <param name="thenBy">Optional lower-priority exact attached indexes.<br/></param>
            /// <returns>An independently disposable exact-index sorted reader.<br/></returns>
            public SortedReader Sort(IndexSort order, params IndexSort[] thenBy)
            {
                ArgumentNullException.ThrowIfNull(thenBy);
                var combined = new IndexSort[thenBy.Length + 1];
                combined[0] = order;
                Array.Copy(thenBy,0,combined,1,thenBy.Length);
                return store.OpenSortedReader(RequireReplayablePlan(), combined);
            }

            /// <summary>Releases the current record, selected source, and store-operation lease.<br/></summary>
            public void Dispose() => core.Dispose();

            /// <summary>Returns the reusable plan or fails explicitly for a one-shot/legacy cursor.<br/></summary>
            private ReaderSourcePlan RequireReplayablePlan()
                => sourcePlan is { CanReplay: true }
                    ? sourcePlan
                    : throw new NotSupportedException("This reader source cannot be reset, forked, or independently sorted.");

            /// <summary>Validates a replacement cursor before releasing the prior cursor so a failed seek leaves this reader usable.<br/></summary>
            /// <param name="replacement">Fresh source-specialized cursor.<br/></param>
            private void ReplaceCore(ReaderCore replacement)
            {
                ArgumentNullException.ThrowIfNull(replacement);
                try
                {
                    replacement.BindResultShape(FingerprintBookmarkText("Row:Flexible"));
                }
                catch
                {
                    replacement.Dispose();
                    throw;
                }

                ReaderCore previous = core;
                core = replacement;
                currentLinkPolicy = null;
                previous.Dispose();
            }

            private static string ValidatePath(string propPath)
                => string.IsNullOrWhiteSpace(propPath)
                    ? throw new ArgumentException("The property path cannot be empty.", nameof(propPath))
                    : propPath;
        }

        /// <summary>
        /// Provides a forward-only, low-allocation identity reader.<br/>
        /// Selected identities are not resolved back to record payloads, and caller-provided spans can be filled without allocating a collection.<br/>
        /// </summary>
        public sealed class IdentityReader : IDisposable
        {
            private readonly RecordStore<T>? store;
            private readonly ReaderSourcePlan? sourcePlan;
            private IdentityReaderCore core;

            internal IdentityReader(IdentityReaderCore core)
            {
                this.core = core;
                store = null;
                sourcePlan = null;
                try
                {
                    core.BindResultShape(FingerprintBookmarkText("Identity:UInt64"));
                }
                catch
                {
                    core.Dispose();
                    throw;
                }
            }

            /// <summary>Opens one payload-free identity reader over a reusable logical source plan.<br/></summary>
            /// <param name="sourcePlan">Plan supplying independently optimized identity cursors.<br/></param>
            /// <param name="store">Owning record store used to retain the reader's source and lifecycle context.<br/></param>
            internal IdentityReader(RecordStore<T> store, ReaderSourcePlan sourcePlan)
                : this(sourcePlan.OpenIdentityInitial())
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                this.sourcePlan = sourcePlan;
            }

            /// <summary>Gets whether this reader can open an independent identity cursor.<br/></summary>
            public bool CanFork => sourcePlan?.CanReplay == true;

            /// <summary>Gets whether this reader can seek back to its original logical beginning.<br/></summary>
            public bool CanReset => CanFork;

            /// <summary>
            /// Gets the identity selected by the most recent successful <see cref="Read"/> call.<br/>
            /// The identity is returned directly from the payload-free cursor without resolving a Fractal record.<br/>
            /// </summary>
            /// <returns>The current store-owned record identity.<br/></returns>
            /// <exception cref="InvalidOperationException">The reader is not positioned on an identity.<br/></exception>
            public ulong Get() => core.Current;

            /// <summary>Gets the zero-based current result ordinal, or -1 before the first successful read.<br/></summary>
            public long Ordinal => core.Ordinal;

            /// <summary>Gets an opaque continuation positioned at the next unread identity.<br/></summary>
            public RecordBookmark Bookmark => core.Bookmark;

            /// <summary>Advances to the next identity.<br/></summary>
            /// <returns><see langword="true"/> when <see cref="Get"/> is available.<br/></returns>
            public bool Read() => core.Read();

            /// <summary>Advances to the next identity.<br/></summary>
            /// <returns><see langword="true"/> when <see cref="Get"/> is available.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>Advances past at most the requested number of identities.<br/></summary>
            /// <param name="count">Maximum identities to skip; must not be negative.<br/></param>
            /// <returns>The number of identities actually skipped.<br/></returns>
            public int Skip(int count) => core.Skip(count);

            /// <summary>Loads subsequent identities into caller-owned storage without allocating a collection.<br/></summary>
            /// <param name="destination">Span receiving identities.<br/></param>
            /// <returns>The number of elements populated.<br/></returns>
            public int Load(Span<ulong> destination) => core.Load(destination);

            /// <summary>
            /// Pulls at most <paramref name="count"/> subsequent identities into a newly allocated exact-length array.<br/>
            /// A short final result is trimmed to its populated length; zero identities return a shared empty array.<br/>
            /// </summary>
            /// <param name="count">Maximum number of identities to retrieve; must not be negative.<br/></param>
            /// <returns>An exact-length array containing the retrieved identities.<br/></returns>
            public ulong[] Pull(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0)
                    return Array.Empty<ulong>();

                var result = new ulong[count];
                int loaded = Load(result);
                if (loaded == count)
                    return result;
                if (loaded == 0)
                    return Array.Empty<ulong>();
                Array.Resize(ref result, loaded);
                return result;
            }

            /// <summary>Seeks to the beginning of this reader's original logical identity selection.<br/></summary>
            public void Reset() => Seek(ReaderPosition.Beginning);

            /// <summary>Seeks this payload-free reader to a logical position through a newly optimized source cursor.<br/></summary>
            /// <param name="position">Logical position to seek.<br/></param>
            public void Seek(ReaderPosition position)
            {
                if (position != ReaderPosition.Beginning)
                    throw new ArgumentOutOfRangeException(nameof(position));
                ReplaceCore(RequireReplayablePlan().OpenIdentityBeginning());
            }

            /// <summary>Seeks this payload-free reader through one compatible opaque bookmark.<br/></summary>
            /// <param name="bookmark">Bookmark captured from the same identity selection.<br/></param>
            public void Seek(RecordBookmark bookmark)
                => ReplaceCore(RequireReplayablePlan().OpenIdentityBookmark(bookmark));

            /// <summary>Opens an independent payload-free reader over the same original identity selection.<br/></summary>
            public IdentityReader Fork() => new(RequireStore(), RequireReplayablePlan());

            /// <summary>Starts an independently sorted row view without consuming this identity reader.<br/></summary>
            /// <param name="sort">One or more exact property-path order descriptors.<br/></param>
            public SortedReader Sort(params SortBy[] sort)
            {
                ArgumentNullException.ThrowIfNull(sort);
                return RequireStore().OpenSortedReader(RequireReplayablePlan(), sort);
            }

            /// <summary>
            /// Starts an independently sorted row view from a direct or calculated typed selector without indexing-specific authoring ceremony.<br/>
            /// Opening returns after bounded intent capture and preserves the identity reader's cursor; preparation or first use establishes the complete stable order.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact scalar ordering-key type inferred from the selector.<br/></typeparam>
            /// <param name="order">Direct or calculated durable selector defining the key.<br/></param>
            /// <param name="descending">Whether to order from greatest to least.<br/></param>
            /// <returns>An independently disposable sorted row reader.<br/></returns>
            public SortedReader Sort<TValue>(
                Selector<T>.IValue<TValue> order,
                bool descending = false)
                => RequireStore().OpenSortedReader(RequireReplayablePlan(), order, descending);

            /// <summary>Starts an independently sorted row view using durable calculated selector semantics.<br/></summary>
            /// <param name="order">Primary calculated ordering selector.<br/></param>
            /// <param name="thenBy">Optional lower-priority selector keys.<br/></param>
            public SortedReader Sort(IndexSelectorBuilder order, params IndexSelectorBuilder[] thenBy)
            {
                ArgumentNullException.ThrowIfNull(order);
                ArgumentNullException.ThrowIfNull(thenBy);
                var combined = new IndexSelectorBuilder[thenBy.Length + 1];
                combined[0] = order;
                Array.Copy(thenBy, 0, combined, 1, thenBy.Length);
                return RequireStore().OpenSortedReader(RequireReplayablePlan(), combined);
            }

            /// <summary>Releases the identity source and store-operation lease.<br/></summary>
            public void Dispose() => core.Dispose();

            /// <summary>Returns the reusable identity source plan or fails explicitly for a legacy/one-shot cursor.<br/></summary>
            private ReaderSourcePlan RequireReplayablePlan()
                => sourcePlan is { CanReplay: true }
                    ? sourcePlan
                    : throw new NotSupportedException("This identity reader source cannot be reset, forked, or independently sorted.");

            /// <summary>Returns the owning store required by forked transformation operations.<br/></summary>
            private RecordStore<T> RequireStore()
                => store ?? throw new NotSupportedException("This legacy identity reader does not retain a reusable source plan.");

            /// <summary>Validates a replacement identity cursor before releasing the prior one.<br/></summary>
            private void ReplaceCore(IdentityReaderCore replacement)
            {
                ArgumentNullException.ThrowIfNull(replacement);
                try
                {
                    replacement.BindResultShape(FingerprintBookmarkText("Identity:UInt64"));
                }
                catch
                {
                    replacement.Dispose();
                    throw;
                }
                IdentityReaderCore previous = core;
                core = replacement;
                previous.Dispose();
            }
        }

        internal sealed class IdentityReaderCore : IDisposable
        {
            private readonly RecordStore<T> store;
            private readonly IDisposable operation;
            private readonly IIdentitySourceCursor identities;
            private readonly BookmarkReadContext bookmarkContext;
            private ulong current;
            private ulong? lastIdentity;
            private long ordinal;
            private bool positioned;
            private bool disposed;

            internal IdentityReaderCore(
                RecordStore<T> store,
                IIdentitySourceCursor identities,
                BookmarkReadContext bookmarkContext)
            {
                this.store = store;
                this.bookmarkContext = bookmarkContext;
                ordinal = bookmarkContext.InitialResultsConsumed - 1;
                lastIdentity = bookmarkContext.InitialLastIdentity;
                operation = store.EnterStoreOperationScope();
                try
                {
                    this.identities = identities;
                    ResumeFromBookmark();
                }
                catch
                {
                    operation.Dispose();
                    throw;
                }
            }

            internal ulong Current
                => positioned ? current : throw new InvalidOperationException("The identity reader is not positioned on a result.");

            internal long Ordinal => ordinal;

            internal RecordBookmark Bookmark
                => bookmarkContext.Capture(
                    ordinal + 1,
                    lastIdentity,
                    store.rbase.data.GetUSN(store.storeID));

            internal void BindResultShape(string fingerprint)
                => bookmarkContext.BindResultShape(fingerprint);

            internal bool Read()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                positioned = false;
                if (!identities.Read(out ulong identity))
                    return false;
                current = identity;
                store.EnsureRecordBelongsToCurrentStore(current, nameof(identities));
                positioned = true;
                ordinal++;
                lastIdentity = current;
                return true;
            }

            internal int Skip(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                ObjectDisposedException.ThrowIf(disposed, this);
                positioned = false;
                int skipped = identities.Skip(count, out ulong lastSkippedIdentity);
                if (skipped > 0)
                {
                    ordinal += skipped;
                    lastIdentity = lastSkippedIdentity;
                }
                return skipped;
            }

            internal int Load(Span<ulong> destination)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                positioned = false;
                int loaded = identities.Load(destination, out ulong lastLoadedIdentity);
                if (loaded > 0)
                {
                    current = lastLoadedIdentity;
                    lastIdentity = lastLoadedIdentity;
                    ordinal += loaded;
                    positioned = true;
                }
                return loaded;
            }

            /// <summary>
            /// Repositions the selected source to the next unread identity described by the validated bookmark.<br/>
            /// Generation-bound bookmarks use source-aware skip; live continuations seek the durable logical anchor because intervening mutations may change ordinals.<br/>
            /// </summary>
            private void ResumeFromBookmark()
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

                    while (identities.Read(out ulong identity))
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
                    int skipped = identities.Skip(requested, out _);
                    remaining -= skipped;
                    if (skipped != requested)
                    {
                        throw new RecordBookmarkException(
                            RecordBookmarkFailureKind.AnchorUnavailable,
                            "The generation-bound bookmark extends beyond the current result stream.");
                    }
                }
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                positioned = false;
                identities.Dispose();
                operation.Dispose();
            }
        }

        /// <summary>
        /// Defines the source-specialized movement contract consumed by the centralized identity reader core.<br/>
        /// Every source reports the final consumed identity from bulk movement so bookmarks remain equivalent to repeated single reads.<br/>
        /// </summary>
        internal interface IIdentitySourceCursor : IDisposable
        {
            /// <summary>Consumes one identity when available.<br/></summary>
            /// <param name="identity">Receives the consumed identity.<br/></param>
            /// <returns><see langword="true"/> when an identity was consumed.<br/></returns>
            bool Read(out ulong identity);

            /// <summary>Consumes at most the requested number of identities using the source's best movement primitive.<br/></summary>
            /// <param name="count">Maximum number of identities to consume.<br/></param>
            /// <param name="lastIdentity">Receives the final consumed identity when the return value is positive.<br/></param>
            /// <returns>The number of identities consumed.<br/></returns>
            int Skip(int count, out ulong lastIdentity);

            /// <summary>Loads subsequent identities into caller-owned storage.<br/></summary>
            /// <param name="destination">Storage to populate.<br/></param>
            /// <param name="lastIdentity">Receives the final copied identity when the return value is positive.<br/></param>
            /// <returns>The number of identities copied.<br/></returns>
            int Load(Span<ulong> destination, out ulong lastIdentity);

            /// <summary>
            /// Attempts to expose the exact current source value when the cursor retains a reversible value representation for the requested path.<br/>
            /// Identity-only, buffered, transformed, and canonicalized sources return <see langword="false"/> so the caller resolves the authoritative Fractal/Inheto value instead.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact CLR value type requested by the Abraxas accessor.<br/></typeparam>
            /// <param name="canonicalPath">Canonical Inheto path whose value is requested.<br/></param>
            /// <param name="value">Receives the exact current source value when available.<br/></param>
            /// <returns><see langword="true"/> only when returning the source value preserves authoritative value semantics.<br/></returns>
            bool TryGetCurrentValue<TValue>(string canonicalPath, out TValue? value);
        }

        /// <summary>Adapts an arbitrary identity sequence when no lower-level cursor contract is available.<br/></summary>
        private sealed class EnumerableIdentitySourceCursor : IIdentitySourceCursor
        {
            private readonly IEnumerator<ulong> enumerator;

            internal EnumerableIdentitySourceCursor(IEnumerable<ulong> source)
                => enumerator = (source ?? throw new ArgumentNullException(nameof(source))).GetEnumerator();

            public bool Read(out ulong identity)
            {
                if (enumerator.MoveNext())
                {
                    identity = enumerator.Current;
                    return true;
                }
                identity = default;
                return false;
            }

            public int Skip(int count, out ulong lastIdentity)
            {
                int skipped = 0;
                lastIdentity = default;
                while (skipped < count && Read(out ulong identity))
                {
                    lastIdentity = identity;
                    skipped++;
                }
                return skipped;
            }

            public int Load(Span<ulong> destination, out ulong lastIdentity)
            {
                int pulled = 0;
                lastIdentity = default;
                while (pulled < destination.Length && Read(out ulong identity))
                {
                    destination[pulled++] = identity;
                    lastIdentity = identity;
                }
                return pulled;
            }

            public bool TryGetCurrentValue<TValue>(string canonicalPath, out TValue? value)
            {
                value = default;
                return false;
            }

            public void Dispose() => enumerator.Dispose();
        }

        /// <summary>Reads a compact prepared identity order with constant-time skip and contiguous span copies.<br/></summary>
        private sealed class BufferedIdentitySourceCursor : IIdentitySourceCursor
        {
            private readonly ulong[] identities;
            private int next;

            internal BufferedIdentitySourceCursor(ulong[] identities)
                => this.identities = identities ?? throw new ArgumentNullException(nameof(identities));

            public bool Read(out ulong identity)
            {
                if ((uint)next < (uint)identities.Length)
                {
                    identity = identities[next++];
                    return true;
                }
                identity = default;
                return false;
            }

            public int Skip(int count, out ulong lastIdentity)
            {
                int skipped = Math.Min(count, identities.Length - next);
                next += skipped;
                lastIdentity = skipped == 0 ? default : identities[next - 1];
                return skipped;
            }

            public int Load(Span<ulong> destination, out ulong lastIdentity)
            {
                int pulled = Math.Min(destination.Length, identities.Length - next);
                identities.AsSpan(next, pulled).CopyTo(destination);
                next += pulled;
                lastIdentity = pulled == 0 ? default : identities[next - 1];
                return pulled;
            }

            public bool TryGetCurrentValue<TValue>(string canonicalPath, out TValue? value)
            {
                value = default;
                return false;
            }

            public void Dispose() { }
        }

        /// <summary>Adapts Fractal's group reader so native identity reads avoid payload-body access and use header-only skipping.<br/></summary>
        private sealed class FractalIdentitySourceCursor : IIdentitySourceCursor
        {
            private readonly FractalStore.GroupRecordReader reader;

            internal FractalIdentitySourceCursor(FractalStore.GroupRecordReader reader)
                => this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

            public bool Read(out ulong identity)
            {
                if (reader.Skip(1) == 1)
                {
                    identity = reader.LastAdvancedIdentity;
                    return true;
                }
                identity = default;
                return false;
            }

            public int Skip(int count, out ulong lastIdentity)
            {
                int skipped = reader.Skip(count);
                lastIdentity = skipped == 0 ? default : reader.LastAdvancedIdentity;
                return skipped;
            }

            public int Load(Span<ulong> destination, out ulong lastIdentity)
            {
                int pulled = 0;
                lastIdentity = default;
                while (pulled < destination.Length && Read(out ulong identity))
                {
                    destination[pulled++] = identity;
                    lastIdentity = identity;
                }
                return pulled;
            }

            public bool TryGetCurrentValue<TValue>(string canonicalPath, out TValue? value)
            {
                value = default;
                return false;
            }

            public void Dispose() => reader.Dispose();
        }

        /// <summary>Adapts one exact typed LibraDex index reader while retaining its shelf-aware skip implementation.<br/></summary>
        private sealed class LibraDexIdentitySourceCursor<TKey> : IIdentitySourceCursor
        {
            private readonly LibraDexRangeReader<TKey, ulong> reader;
            private readonly string canonicalKeyPath;
            private readonly bool exposesExactKey;

            internal LibraDexIdentitySourceCursor(
                LibraDexRangeReader<TKey, ulong> reader,
                string canonicalKeyPath,
                bool exposesExactKey)
            {
                this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
                this.canonicalKeyPath = canonicalKeyPath ?? string.Empty;
                this.exposesExactKey = exposesExactKey;
            }

            public bool Read(out ulong identity) => reader.TryReadNextIdentity(out identity);

            public int Skip(int count, out ulong lastIdentity)
            {
                int skipped = reader.Skip(count);
                lastIdentity = skipped == 0 ? default : reader.CurrentIdentity;
                return skipped;
            }

            public int Load(Span<ulong> destination, out ulong lastIdentity)
            {
                int pulled = 0;
                lastIdentity = default;
                while (pulled < destination.Length && reader.TryReadNextIdentity(out ulong identity))
                {
                    destination[pulled++] = identity;
                    lastIdentity = identity;
                }
                return pulled;
            }

            /// <summary>
            /// Returns the already-decoded current LibraDex key only for an exact reversible path/type contract.<br/>
            /// A null reference key falls back to Fractal/Inheto because the generic LibraDex range facade does not expose null-route provenance.<br/>
            /// </summary>
            public bool TryGetCurrentValue<TValue>(string canonicalPath, out TValue? value)
            {
                if (!exposesExactKey ||
                    typeof(TValue) != typeof(TKey) ||
                    !string.Equals(canonicalKeyPath, canonicalPath, StringComparison.Ordinal))
                {
                    value = default;
                    return false;
                }

                TKey key = reader.CurrentKey;
                if (key is null)
                {
                    value = default;
                    return false;
                }

                value = Unsafe.As<TKey, TValue>(ref key);
                return true;
            }

            public void Dispose() => reader.Dispose();
        }

        /// <summary>Filters one stable ordered source through an already-computed identity membership set.<br/></summary>
        private sealed class FilteringIdentitySourceCursor : IIdentitySourceCursor
        {
            private readonly IIdentitySourceCursor source;
            private readonly HashSet<ulong> accepted;

            internal FilteringIdentitySourceCursor(IIdentitySourceCursor source, HashSet<ulong> accepted)
            {
                this.source = source ?? throw new ArgumentNullException(nameof(source));
                this.accepted = accepted ?? throw new ArgumentNullException(nameof(accepted));
            }

            public bool Read(out ulong identity)
            {
                while (source.Read(out identity))
                {
                    if (accepted.Contains(identity))
                        return true;
                }
                identity = default;
                return false;
            }

            public int Skip(int count, out ulong lastIdentity)
            {
                int skipped = 0;
                lastIdentity = default;
                while (skipped < count && Read(out ulong identity))
                {
                    lastIdentity = identity;
                    skipped++;
                }
                return skipped;
            }

            public int Load(Span<ulong> destination, out ulong lastIdentity)
            {
                int pulled = 0;
                lastIdentity = default;
                while (pulled < destination.Length && Read(out ulong identity))
                {
                    destination[pulled++] = identity;
                    lastIdentity = identity;
                }
                return pulled;
            }

            public bool TryGetCurrentValue<TValue>(string canonicalPath, out TValue? value)
                => source.TryGetCurrentValue(canonicalPath, out value);

            public void Dispose() => source.Dispose();
        }

        /// <summary>
        /// Retains a named composite index's current key beside its identity, covering exact direct-member reads without opening the record payload.<br/>
        /// Other value paths retain the ordinary lazy Fractal/Inheto fallback.<br/>
        /// </summary>
        private sealed class CompositeIndexIdentitySourceCursor : IIdentitySourceCursor
        {
            private readonly IEnumerator<LibraDexRuntimeTuple> tuples;
            private readonly string?[] canonicalPaths;
            private readonly Type[] valueTypes;
            private LibraDexCompositeKey? currentKey;

            /// <summary>Opens a forward-only tuple stream with reversible member-path and type contracts.<br/></summary>
            /// <param name="source">Native composite tuple stream.<br/></param>
            /// <param name="canonicalPaths">Direct path of each coverable part, or null otherwise.<br/></param>
            /// <param name="valueTypes">Declared CLR type of each ordered part.<br/></param>
            internal CompositeIndexIdentitySourceCursor(IEnumerable<LibraDexRuntimeTuple> source, string?[] canonicalPaths, Type[] valueTypes)
            {
                tuples = (source ?? throw new ArgumentNullException(nameof(source))).GetEnumerator();
                this.canonicalPaths = canonicalPaths ?? throw new ArgumentNullException(nameof(canonicalPaths));
                this.valueTypes = valueTypes ?? throw new ArgumentNullException(nameof(valueTypes));
                if (canonicalPaths.Length != valueTypes.Length)
                    throw new ArgumentException("Composite key path and type counts differ.", nameof(canonicalPaths));
            }

            /// <summary>Advances to the next key/identity tuple without resolving its source record.<br/></summary>
            /// <param name="identity">Current RecordID when a tuple remains.<br/></param>
            /// <returns>True while the index has another tuple.<br/></returns>
            public bool Read(out ulong identity)
            {
                currentKey = null;
                if (!tuples.MoveNext())
                {
                    identity = default;
                    return false;
                }
                LibraDexRuntimeTuple tuple = tuples.Current;
                currentKey = tuple.Key as LibraDexCompositeKey
                    ?? throw new InvalidDataException("A composite index returned a non-composite key.");
                identity = tuple.Identity is ulong recordId
                    ? recordId
                    : throw new InvalidDataException("A composite index returned a non-RecordID identity.");
                return true;
            }

            /// <summary>Skips tuples while retaining the last identity for reader continuation.<br/></summary>
            /// <param name="count">Maximum tuples to skip.<br/></param>
            /// <param name="lastIdentity">Last skipped RecordID, if any.<br/></param>
            /// <returns>Number skipped.<br/></returns>
            public int Skip(int count, out ulong lastIdentity)
            {
                int skipped = 0;
                lastIdentity = default;
                while (skipped < count && Read(out ulong identity))
                {
                    lastIdentity = identity;
                    skipped++;
                }
                return skipped;
            }

            /// <summary>Copies subsequent RecordIDs without reading their payloads.<br/></summary>
            /// <param name="destination">Caller-owned destination.<br/></param>
            /// <param name="lastIdentity">Last copied RecordID, if any.<br/></param>
            /// <returns>Number copied.<br/></returns>
            public int Load(Span<ulong> destination, out ulong lastIdentity)
            {
                int pulled = 0;
                lastIdentity = default;
                while (pulled < destination.Length && Read(out ulong identity))
                {
                    destination[pulled++] = identity;
                    lastIdentity = identity;
                }
                return pulled;
            }

            /// <summary>Gets an exact key part for the corresponding direct member path and requested CLR type.<br/></summary>
            /// <typeparam name="TValue">Requested value type.<br/></typeparam>
            /// <param name="canonicalPath">Canonical member path.<br/></param>
            /// <param name="value">Current covered value, including null, when matched.<br/></param>
            /// <returns>True only when the key can supply this value exactly.<br/></returns>
            public bool TryGetCurrentValue<TValue>(string canonicalPath, out TValue? value)
            {
                if (currentKey is not null)
                {
                    for (int ordinal = 0; ordinal < canonicalPaths.Length; ordinal++)
                    {
                        if (valueTypes[ordinal] != typeof(TValue) ||
                            !string.Equals(canonicalPaths[ordinal], canonicalPath, StringComparison.Ordinal))
                            continue;
                        object? part = currentKey[ordinal];
                        if (part is null)
                        {
                            value = default;
                            return true;
                        }
                        if (part is TValue typed)
                        {
                            value = typed;
                            return true;
                        }
                    }
                }
                value = default;
                return false;
            }

            /// <summary>Closes the native tuple iterator.<br/></summary>
            public void Dispose() => tuples.Dispose();
        }
    }
}
