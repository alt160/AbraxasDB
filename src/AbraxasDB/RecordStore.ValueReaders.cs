using System;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Opens a forward-only typed-value reader over all records.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<T1?> OpenValuesReader<T1>(string propPath1)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            return new DataReader<T1?>(CreateAllReaderCore(), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<T1?> OpenValuesReader<T1>(Condition<T>.EndCondition where, string propPath1, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            return new DataReader<T1?>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<T1?> OpenValuesReader<T1>(long state, string propPath1, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            return new DataReader<T1?>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), BuildTypedValuesBookmarkShape(typeof(T1?), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(string propPath1)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            return new DataReader<(ulong ID, T1? Value1)>(CreateAllReaderCore(), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(Condition<T>.EndCondition where, string propPath1, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            return new DataReader<(ulong ID, T1? Value1)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1)> OpenValuesReaderWithIds<T1>(long state, string propPath1, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1 });
            return new DataReader<(ulong ID, T1? Value1)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(string propPath1, string propPath2)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            return new DataReader<(T1? Value1, T2? Value2)>(CreateAllReaderCore(), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(Condition<T>.EndCondition where, string propPath1, string propPath2, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            return new DataReader<(T1? Value1, T2? Value2)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2)> OpenValuesReader<T1, T2>(long state, string propPath1, string propPath2, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            return new DataReader<(T1? Value1, T2? Value2)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(string propPath1, string propPath2)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateAllReaderCore(), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(Condition<T>.EndCondition where, string propPath1, string propPath2, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2)> OpenValuesReaderWithIds<T1, T2>(long state, string propPath1, string propPath2, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(string propPath1, string propPath2, string propPath3)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateAllReaderCore(), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3)> OpenValuesReader<T1, T2, T3>(long state, string propPath1, string propPath2, string propPath3, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(string propPath1, string propPath2, string propPath3)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateAllReaderCore(), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> OpenValuesReaderWithIds<T1, T2, T3>(long state, string propPath1, string propPath2, string propPath3, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(string propPath1, string propPath2, string propPath3, string propPath4)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateAllReaderCore(), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReader<T1, T2, T3, T4>(long state, string propPath1, string propPath2, string propPath3, string propPath4, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(string propPath1, string propPath2, string propPath3, string propPath4)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateAllReaderCore(), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> OpenValuesReaderWithIds<T1, T2, T3, T4>(long state, string propPath1, string propPath2, string propPath3, string propPath4, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateAllReaderCore(), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReader<T1, T2, T3, T4, T5>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateAllReaderCore(), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateAllReaderCore(), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReader<T1, T2, T3, T4, T5, T6>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateAllReaderCore(), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <typeparam name="T7">Requested type for property value 7.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="propPath7">Property path for value 7.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateAllReaderCore(), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <typeparam name="T7">Requested type for property value 7.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="propPath7">Property path for value 7.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <typeparam name="T7">Requested type for property value 7.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="propPath7">Property path for value 7.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            return new DataReader<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: false, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over all records with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <typeparam name="T7">Requested type for property value 7.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="propPath7">Property path for value 7.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateAllReaderCore(), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching a completed condition and optional sort with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <typeparam name="T7">Requested type for property value 7.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="propPath7">Property path for value 7.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateConditionReaderCore(where, sort, nameof(OpenValuesReader)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

        /// <summary>
        /// Opens a forward-only typed-value reader over records matching persistent State and an optional mask with each record identity explicitly included.<br/>
        /// Only the requested Inheto property values are decoded; the containing object is not hydrated.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <typeparam name="T5">Requested type for property value 5.<br/></typeparam>
        /// <typeparam name="T6">Requested type for property value 6.<br/></typeparam>
        /// <typeparam name="T7">Requested type for property value 7.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <param name="propPath5">Property path for value 5.<br/></param>
        /// <param name="propPath6">Property path for value 6.<br/></param>
        /// <param name="propPath7">Property path for value 7.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A reader positioned before the first row.<br/></returns>
        public DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, long? mask = null)
        {
            string[] paths = ValidatePropPaths(new[] { propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7 });
            return new DataReader<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)>(CreateIdentityReaderCore(IterateRecordIDs(state, mask), CreateStateBookmarkContext(state, mask)), (binary, id) => (id, binary.ReadPropAs<T1>(paths[0], NativeDeserializationOptions), binary.ReadPropAs<T2>(paths[1], NativeDeserializationOptions), binary.ReadPropAs<T3>(paths[2], NativeDeserializationOptions), binary.ReadPropAs<T4>(paths[3], NativeDeserializationOptions), binary.ReadPropAs<T5>(paths[4], NativeDeserializationOptions), binary.ReadPropAs<T6>(paths[5], NativeDeserializationOptions), binary.ReadPropAs<T7>(paths[6], NativeDeserializationOptions)), BuildTypedValuesBookmarkShape(typeof((ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)), withIds: true, paths));
        }

    }
}
