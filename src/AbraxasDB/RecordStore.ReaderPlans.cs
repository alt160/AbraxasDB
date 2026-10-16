using System;
using System.Collections.Generic;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>Creates a replayable complete-store reader plan with optional legacy property ordering.<br/></summary>
        /// <param name="sort">Optional property ordering applied independently on every fork.<br/></param>
        /// <param name="origin">Optional continuation that defines this plan's beginning.<br/></param>
        private ReaderSourcePlan CreateAllRowPlan(SortBy[]? sort = null, RecordBookmark? origin = null)
        {
            SortBy[] detachedSort = sort is null ? Array.Empty<SortBy>() : (SortBy[])sort.Clone();
            return new ReaderSourcePlan(
                bookmark =>
                {
                    BookmarkReadContext context = CreateAllBookmarkContext(detachedSort, bookmark);
                    ReaderOpenRequest request = detachedSort.Length == 0
                        ? new ReaderOpenRequest(ReaderSourceKind.All, context)
                        : new ReaderOpenRequest(
                            ReaderSourceKind.Identities,
                            context,
                            identityCursor: new BufferedIdentitySourceCursor(
                                PrepareTransientIdentityOrder(IterateRecordIDs(), detachedSort)));
                    return OpenReaderCore(request);
                },
                origin,
                canReplay: true,
                isCompleteStorePopulation: origin is null,
                openIdentity: bookmark =>
                {
                    BookmarkReadContext context = CreateAllBookmarkContext(detachedSort, bookmark);
                    ReaderOpenRequest request = detachedSort.Length == 0
                        ? new ReaderOpenRequest(ReaderSourceKind.All, context)
                        : new ReaderOpenRequest(
                            ReaderSourceKind.Identities,
                            context,
                            identityCursor: new BufferedIdentitySourceCursor(
                                PrepareTransientIdentityOrder(IterateRecordIDs(), detachedSort)));
                    return OpenIdentityReaderCore(request);
                });
        }

        /// <summary>
        /// Creates a replayable complete-store plan whose physical order is intentionally outside the public semantic contract.<br/>
        /// Each open establishes an independent mutation-stable Fractal location snapshot; no identity-only source is exposed because the route is specifically optimized for complete payload consumption.<br/>
        /// </summary>
        /// <param name="origin">Optional generation-bound continuation that defines this plan's beginning.<br/></param>
        /// <returns>A complete-store source plan backed by the unordered bulk reader.<br/></returns>
        private ReaderSourcePlan CreateUnorderedRowPlan(RecordBookmark? origin = null)
            => new(
                bookmark =>
                {
                    BookmarkReadContext context = CreateUnorderedBookmarkContext(bookmark);
                    return OpenReaderCore(new ReaderOpenRequest(ReaderSourceKind.UnorderedAll, context));
                },
                origin,
                canReplay: true,
                isCompleteStorePopulation: origin is null);

        /// <summary>Creates a replayable condition-selected reader plan.<br/></summary>
        /// <param name="where">Completed condition retained as the selection contract.<br/></param>
        /// <param name="sort">Optional legacy property ordering.<br/></param>
        /// <param name="origin">Optional continuation that defines this plan's beginning.<br/></param>
        private ReaderSourcePlan CreateConditionRowPlan(
            Condition<T>.EndCondition where,
            SortBy[]? sort = null,
            RecordBookmark? origin = null)
        {
            ArgumentNullException.ThrowIfNull(where);
            SortBy[] detachedSort = sort is null ? Array.Empty<SortBy>() : (SortBy[])sort.Clone();
            return new ReaderSourcePlan(
                bookmark =>
                {
                    BookmarkReadContext context = CreateConditionBookmarkContext(where, detachedSort, bookmark);
                    return OpenReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Condition,
                        context,
                        condition: where,
                        sort: detachedSort,
                        operationName: nameof(ReaderOperations.Open)));
                },
                origin,
                openIdentity: bookmark =>
                {
                    BookmarkReadContext context = CreateConditionBookmarkContext(where, detachedSort, bookmark);
                    return OpenIdentityReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Condition,
                        context,
                        condition: where,
                        sort: detachedSort,
                        operationName: nameof(ReaderOperations.OpenIdentities)));
                });
        }

        /// <summary>Creates a replayable exact-index reader plan with optional independent condition filtering.<br/></summary>
        /// <param name="index">Exact store-owned index defining population and order.<br/></param>
        /// <param name="direction">Whole-index traversal direction.<br/></param>
        /// <param name="where">Optional completed condition filtering the selected index population.<br/></param>
        /// <param name="origin">Optional continuation that defines this plan's beginning.<br/></param>
        private ReaderSourcePlan CreateIndexRowPlan(
            RecordIndex index,
            RetrievalDirection direction,
            Condition<T>.EndCondition? where = null,
            RecordBookmark? origin = null)
        {
            return new ReaderSourcePlan(
                bookmark =>
                {
                    BookmarkReadContext context = CreateIndexBookmarkContext(index, direction, where, bookmark);
                    IIdentitySourceCursor cursor = where is null
                        ? OpenIndexedIdentitySourceCursor(index, direction)
                        : OpenConditionedIndexedIdentitySourceCursor(index, where, direction, nameof(ReaderOperations.Open));
                    return OpenReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        identityCursor: cursor,
                        trustIdentityExistence: true));
                },
                origin,
                openIdentity: bookmark =>
                {
                    BookmarkReadContext context = CreateIndexBookmarkContext(index, direction, where, bookmark);
                    IIdentitySourceCursor cursor = where is null
                        ? OpenIndexedIdentitySourceCursor(index, direction)
                        : OpenConditionedIndexedIdentitySourceCursor(index, where, direction, nameof(ReaderOperations.OpenIdentities));
                    return OpenIdentityReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        identityCursor: cursor));
                });
        }

        /// <summary>Creates a replayable persistent-State reader plan.<br/></summary>
        /// <param name="state">State value to match.<br/></param>
        /// <param name="mask">Optional state mask.<br/></param>
        private ReaderSourcePlan CreateStateRowPlan(long state, long? mask)
            => new(
                bookmark =>
                {
                    BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
                    return OpenReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        IterateRecordIDs(state, mask)));
                },
                openIdentity: bookmark =>
                {
                    BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
                    return OpenIdentityReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        IterateRecordIDs(state, mask)));
                });

        /// <summary>Creates a reader plan over developer-supplied identities without hidden replay buffering.<br/></summary>
        /// <param name="identities">Identity source whose collection shape determines whether independent enumeration is supported.<br/></param>
        private ReaderSourcePlan CreateIdentityRowPlan(IEnumerable<ulong> identities)
        {
            ArgumentNullException.ThrowIfNull(identities);
            bool canReplay = identities is IReadOnlyCollection<ulong> || identities is ICollection<ulong>;
            return new ReaderSourcePlan(
                bookmark =>
                {
                    BookmarkReadContext context = CreateAllBookmarkContext(bookmark: bookmark);
                    return OpenReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        identities));
                },
                canReplay: canReplay,
                openIdentity: bookmark =>
                {
                    BookmarkReadContext context = CreateAllBookmarkContext(bookmark: bookmark);
                    return OpenIdentityReaderCore(new ReaderOpenRequest(
                        ReaderSourceKind.Identities,
                        context,
                        identities));
                });
        }
    }
}
