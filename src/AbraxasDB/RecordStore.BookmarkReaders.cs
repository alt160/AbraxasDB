namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Resumes an unfiltered canonical value reader with generation-bound continuation.<br/>
        /// </summary>
        /// <param name="bookmark">Detached bookmark captured from an equivalent unfiltered value-reader shape.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>A value reader positioned before the next unread row.<br/></returns>
        public ValueDataReader OpenValuesReader(RecordBookmark bookmark, params string[] valuePaths)
            => OpenValuesReader(bookmark, BookmarkConsistency.GenerationBound, valuePaths);

        /// <summary>
        /// Resumes an unfiltered canonical value reader under the requested continuation policy.<br/>
        /// </summary>
        /// <param name="bookmark">Detached bookmark captured from an equivalent unfiltered value-reader shape.<br/></param>
        /// <param name="consistency">Continuation policy governing generation changes since capture.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>A value reader positioned before the next unread row.<br/></returns>
        public ValueDataReader OpenValuesReader(
            RecordBookmark bookmark,
            BookmarkConsistency consistency,
            params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(valuePaths);
            return new ValueDataReader(
                CreateAllReaderCore(CreateAllBookmarkContext(sort: null, bookmark, consistency)),
                paths);
        }

        /// <summary>
        /// Resumes a condition-selected canonical value reader without explicit sorting.<br/>
        /// </summary>
        /// <param name="where">The same semantic condition used when the bookmark was captured.<br/></param>
        /// <param name="bookmark">Detached bookmark to resume under generation-bound continuation.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>A value reader positioned before the next unread matching row.<br/></returns>
        public ValueDataReader OpenValuesReader(
            Condition<T>.EndCondition where,
            RecordBookmark bookmark,
            params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(valuePaths);
            BookmarkReadContext context = CreateConditionBookmarkContext(where, null, bookmark, BookmarkConsistency.GenerationBound);
            return new ValueDataReader(
                CreateConditionReaderCore(where, Array.Empty<SortBy>(), nameof(OpenValuesReader), context),
                paths);
        }

        /// <summary>
        /// Resumes a condition-selected canonical value reader with an explicit ordering contract.<br/>
        /// </summary>
        /// <param name="where">The same semantic condition used when the bookmark was captured.<br/></param>
        /// <param name="bookmark">Detached bookmark to resume.<br/></param>
        /// <param name="consistency">Continuation policy governing generation changes since capture.<br/></param>
        /// <param name="sort">Sort descriptors in the same semantic order used when the bookmark was captured.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>A value reader positioned before the next unread matching row.<br/></returns>
        public ValueDataReader OpenValuesReader(
            Condition<T>.EndCondition where,
            RecordBookmark bookmark,
            BookmarkConsistency consistency,
            SortBy[] sort,
            params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(valuePaths);
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new ValueDataReader(
                CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context),
                paths);
        }

        /// <summary>
        /// Resumes a State-selected canonical value reader.<br/>
        /// </summary>
        /// <param name="state">Persistent State value used when the bookmark was captured.<br/></param>
        /// <param name="bookmark">Detached bookmark to resume under generation-bound continuation.<br/></param>
        /// <param name="mask">Optional State mask used when the bookmark was captured.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>A value reader positioned before the next unread State-selected row.<br/></returns>
        public ValueDataReader OpenValuesReader(
            long state,
            RecordBookmark bookmark,
            long? mask,
            params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(valuePaths);
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new ValueDataReader(
                CreateIdentityReaderCore(IterateRecordIDs(state, mask), context),
                paths);
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware canonical value reader.<br/>
        /// </summary>
        /// <param name="bookmark">Detached bookmark captured from an equivalent unfiltered identity-aware value-reader shape.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>An identity-aware value reader positioned before the next unread row.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(RecordBookmark bookmark, params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(valuePaths);
            return new ValueDataReaderWithIds(
                CreateAllReaderCore(CreateAllBookmarkContext(sort: null, bookmark)),
                paths);
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware canonical value reader without explicit sorting.<br/>
        /// The reader uses generation-bound continuation and validates the condition plus requested value paths before reading.<br/>
        /// </summary>
        /// <param name="where">The same semantic condition used to capture the bookmark.<br/></param>
        /// <param name="bookmark">Detached bookmark to resume.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used by the original reader.<br/></param>
        /// <returns>A reader positioned before the next unread matching value row.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(
            Condition<T>.EndCondition where,
            RecordBookmark bookmark,
            params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(valuePaths);
            BookmarkReadContext context = CreateConditionBookmarkContext(where, null, bookmark, BookmarkConsistency.GenerationBound);
            return new ValueDataReaderWithIds(
                CreateConditionReaderCore(where, Array.Empty<SortBy>(), nameof(OpenValuesReaderWithIds), context),
                paths);
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware canonical value reader with optional deterministic ordering.<br/>
        /// </summary>
        /// <param name="where">The same semantic condition used when the bookmark was captured.<br/></param>
        /// <param name="bookmark">Detached bookmark to resume.<br/></param>
        /// <param name="consistency">Continuation policy governing generation changes since capture.<br/></param>
        /// <param name="sort">Sort descriptors in the same semantic order used when the bookmark was captured.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>An identity-aware value reader positioned before the next unread matching row.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(
            Condition<T>.EndCondition where,
            RecordBookmark bookmark,
            BookmarkConsistency consistency,
            SortBy[] sort,
            params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(valuePaths);
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new ValueDataReaderWithIds(
                CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context),
                paths);
        }

        /// <summary>
        /// Resumes a State-selected identity-aware canonical value reader.<br/>
        /// </summary>
        /// <param name="state">Persistent State value used when the bookmark was captured.<br/></param>
        /// <param name="bookmark">Detached bookmark to resume under generation-bound continuation.<br/></param>
        /// <param name="mask">Optional State mask used when the bookmark was captured.<br/></param>
        /// <param name="valuePaths">Value paths in the same ordinal order used when the bookmark was captured.<br/></param>
        /// <returns>An identity-aware value reader positioned before the next unread State-selected row.<br/></returns>
        public ValueDataReaderWithIds OpenValuesReaderWithIds(
            long state,
            RecordBookmark bookmark,
            long? mask,
            params string[] valuePaths)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(valuePaths);
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new ValueDataReaderWithIds(
                CreateIdentityReaderCore(IterateRecordIDs(state, mask), context),
                paths);
        }
    }
}
