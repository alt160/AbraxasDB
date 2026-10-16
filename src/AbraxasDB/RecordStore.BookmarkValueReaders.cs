namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Resumes an unfiltered typed 1-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<T1?> OpenValuesReader<T1>(RecordBookmark bookmark, string propPath1)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<T1?>(CreateAllReaderCore(context), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 1-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<T1?> OpenValuesReader<T1>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<T1?>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 1-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<T1?> OpenValuesReader<T1>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<T1?>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 1-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<T1?> OpenValuesReader<T1>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<T1?>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a State-selected typed 1-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<T1?> OpenValuesReader<T1>(long state, RecordBookmark bookmark, string propPath1, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<T1?>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 1-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(RecordBookmark bookmark, string propPath1)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(ulong ID, T1? Value1)>(CreateAllReaderCore(context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 1-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 1-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(ulong ID, T1? Value1)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 1-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a State-selected identity-aware typed 1-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(long state, RecordBookmark bookmark, string propPath1, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(ulong ID, T1? Value1)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 2-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(RecordBookmark bookmark, string propPath1, string propPath2)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(T1? Value1, T2? Value2)>(CreateAllReaderCore(context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 2-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 2-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(T1? Value1, T2? Value2)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 2-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a State-selected typed 2-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(long state, RecordBookmark bookmark, string propPath1, string propPath2, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(T1? Value1, T2? Value2)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 2-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(RecordBookmark bookmark, string propPath1, string propPath2)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateAllReaderCore(context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 2-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 2-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 2-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a State-selected identity-aware typed 2-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(long state, RecordBookmark bookmark, string propPath1, string propPath2, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 3-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateAllReaderCore(context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 3-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 3-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 3-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a State-selected typed 3-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 3-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateAllReaderCore(context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 3-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 3-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 3-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a State-selected identity-aware typed 3-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 4-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateAllReaderCore(context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 4-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 4-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 4-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a State-selected typed 4-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 4-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateAllReaderCore(context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 4-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 4-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 4-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a State-selected identity-aware typed 4-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 5-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateAllReaderCore(context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 5-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 5-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 5-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a State-selected typed 5-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 5-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateAllReaderCore(context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 5-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 5-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 5-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a State-selected identity-aware typed 5-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 6-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateAllReaderCore(context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 6-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 6-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 6-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a State-selected typed 6-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 6-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateAllReaderCore(context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 6-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 6-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 6-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a State-selected identity-aware typed 6-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 7-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateAllReaderCore(context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered typed 7-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 7-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a condition-selected typed 7-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes a State-selected typed 7-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 7-property reader with generation-bound continuation.<br/>
        /// Every property path and generic result type participates in bookmark shape validation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort: null, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateAllReaderCore(context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes an unfiltered identity-aware typed 7-property reader with an explicit ordering and continuation policy.<br/>
        /// Live continuation requires the supplied deterministic sort contract.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(RecordBookmark bookmark, BookmarkConsistency consistency, SortBy[] sort, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateAllBookmarkContext(sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateIdentityReaderCore(IterateRecordIDs(sort), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 7-property reader with generation-bound continuation.<br/>
        /// The same semantic condition, property paths, result types, and ordering must be supplied again.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, BookmarkConsistency.GenerationBound);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a condition-selected identity-aware typed 7-property reader under the requested continuation policy.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, RecordBookmark bookmark, BookmarkConsistency consistency, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(sort);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateConditionBookmarkContext(where, sort, bookmark, consistency);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReaderWithIds), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

        /// <summary>
        /// Resumes a State-selected identity-aware typed 7-property reader with generation-bound continuation.<br/>
        /// Natural-order State readers deliberately reject live continuation.<br/>
        /// </summary>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(long state, RecordBookmark bookmark, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, long? mask = null)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            BookmarkReadContext context = CreateStateBookmarkContext(state, mask, bookmark);
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), context), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

    }
}
