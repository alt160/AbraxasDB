using System;
using System.Collections.Generic;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Lazily enumerates 1 directly decoded property from every record.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<T1?> IterateProps<T1>(string propPath1)
            => IterateReader(OpenValuesReader<T1>(propPath1));

        /// <summary>
        /// Lazily enumerates 1 directly decoded property from records matching a completed condition and optional sort.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<T1?> IterateProps<T1>(Condition<T>.EndCondition where, string propPath1, params SortBy[] sort)
            => IterateReader(OpenValuesReader<T1>(where, propPath1, sort));

        /// <summary>
        /// Lazily enumerates 1 directly decoded property from records matching persistent State and an optional mask.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<T1?> IterateProps<T1>(long state, string propPath1, long? mask = null)
            => IterateReader(OpenValuesReader<T1>(state, propPath1, mask));

        /// <summary>
        /// Lazily enumerates 1 directly decoded property from every record with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1)> IteratePropsWithIds<T1>(string propPath1)
            => IterateReader(OpenValuesReaderWithIds<T1>(propPath1));

        /// <summary>
        /// Lazily enumerates 1 directly decoded property from records matching a completed condition and optional sort with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1)> IteratePropsWithIds<T1>(Condition<T>.EndCondition where, string propPath1, params SortBy[] sort)
            => IterateReader(OpenValuesReaderWithIds<T1>(where, propPath1, sort));

        /// <summary>
        /// Lazily enumerates 1 directly decoded property from records matching persistent State and an optional mask with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1)> IteratePropsWithIds<T1>(long state, string propPath1, long? mask = null)
            => IterateReader(OpenValuesReaderWithIds<T1>(state, propPath1, mask));

        /// <summary>
        /// Lazily enumerates 2 directly decoded properties from every record.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2)> IterateProps<T1, T2>(string propPath1, string propPath2)
            => IterateReader(OpenValuesReader<T1, T2>(propPath1, propPath2));

        /// <summary>
        /// Lazily enumerates 2 directly decoded properties from records matching a completed condition and optional sort.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2)> IterateProps<T1, T2>(Condition<T>.EndCondition where, string propPath1, string propPath2, params SortBy[] sort)
            => IterateReader(OpenValuesReader<T1, T2>(where, propPath1, propPath2, sort));

        /// <summary>
        /// Lazily enumerates 2 directly decoded properties from records matching persistent State and an optional mask.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2)> IterateProps<T1, T2>(long state, string propPath1, string propPath2, long? mask = null)
            => IterateReader(OpenValuesReader<T1, T2>(state, propPath1, propPath2, mask));

        /// <summary>
        /// Lazily enumerates 2 directly decoded properties from every record with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2)> IteratePropsWithIds<T1, T2>(string propPath1, string propPath2)
            => IterateReader(OpenValuesReaderWithIds<T1, T2>(propPath1, propPath2));

        /// <summary>
        /// Lazily enumerates 2 directly decoded properties from records matching a completed condition and optional sort with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2)> IteratePropsWithIds<T1, T2>(Condition<T>.EndCondition where, string propPath1, string propPath2, params SortBy[] sort)
            => IterateReader(OpenValuesReaderWithIds<T1, T2>(where, propPath1, propPath2, sort));

        /// <summary>
        /// Lazily enumerates 2 directly decoded properties from records matching persistent State and an optional mask with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2)> IteratePropsWithIds<T1, T2>(long state, string propPath1, string propPath2, long? mask = null)
            => IterateReader(OpenValuesReaderWithIds<T1, T2>(state, propPath1, propPath2, mask));

        /// <summary>
        /// Lazily enumerates 3 directly decoded properties from every record.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3)> IterateProps<T1, T2, T3>(string propPath1, string propPath2, string propPath3)
            => IterateReader(OpenValuesReader<T1, T2, T3>(propPath1, propPath2, propPath3));

        /// <summary>
        /// Lazily enumerates 3 directly decoded properties from records matching a completed condition and optional sort.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3)> IterateProps<T1, T2, T3>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
            => IterateReader(OpenValuesReader<T1, T2, T3>(where, propPath1, propPath2, propPath3, sort));

        /// <summary>
        /// Lazily enumerates 3 directly decoded properties from records matching persistent State and an optional mask.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3)> IterateProps<T1, T2, T3>(long state, string propPath1, string propPath2, string propPath3, long? mask = null)
            => IterateReader(OpenValuesReader<T1, T2, T3>(state, propPath1, propPath2, propPath3, mask));

        /// <summary>
        /// Lazily enumerates 3 directly decoded properties from every record with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> IteratePropsWithIds<T1, T2, T3>(string propPath1, string propPath2, string propPath3)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3>(propPath1, propPath2, propPath3));

        /// <summary>
        /// Lazily enumerates 3 directly decoded properties from records matching a completed condition and optional sort with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="where">Completed condition to execute.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="sort">Optional property-path sort descriptors.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> IteratePropsWithIds<T1, T2, T3>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, params SortBy[] sort)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3>(where, propPath1, propPath2, propPath3, sort));

        /// <summary>
        /// Lazily enumerates 3 directly decoded properties from records matching persistent State and an optional mask with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3)> IteratePropsWithIds<T1, T2, T3>(long state, string propPath1, string propPath2, string propPath3, long? mask = null)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3>(state, propPath1, propPath2, propPath3, mask));

        /// <summary>
        /// Lazily enumerates 4 directly decoded properties from every record.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> IterateProps<T1, T2, T3, T4>(string propPath1, string propPath2, string propPath3, string propPath4)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4>(propPath1, propPath2, propPath3, propPath4));

        /// <summary>
        /// Lazily enumerates 4 directly decoded properties from records matching a completed condition and optional sort.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> IterateProps<T1, T2, T3, T4>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4>(where, propPath1, propPath2, propPath3, propPath4, sort));

        /// <summary>
        /// Lazily enumerates 4 directly decoded properties from records matching persistent State and an optional mask.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4)> IterateProps<T1, T2, T3, T4>(long state, string propPath1, string propPath2, string propPath3, string propPath4, long? mask = null)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4>(state, propPath1, propPath2, propPath3, propPath4, mask));

        /// <summary>
        /// Lazily enumerates 4 directly decoded properties from every record with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
        /// </summary>
        /// <typeparam name="T1">Requested type for property value 1.<br/></typeparam>
        /// <typeparam name="T2">Requested type for property value 2.<br/></typeparam>
        /// <typeparam name="T3">Requested type for property value 3.<br/></typeparam>
        /// <typeparam name="T4">Requested type for property value 4.<br/></typeparam>
        /// <param name="propPath1">Property path for value 1.<br/></param>
        /// <param name="propPath2">Property path for value 2.<br/></param>
        /// <param name="propPath3">Property path for value 3.<br/></param>
        /// <param name="propPath4">Property path for value 4.<br/></param>
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> IteratePropsWithIds<T1, T2, T3, T4>(string propPath1, string propPath2, string propPath3, string propPath4)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4>(propPath1, propPath2, propPath3, propPath4));

        /// <summary>
        /// Lazily enumerates 4 directly decoded properties from records matching a completed condition and optional sort with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> IteratePropsWithIds<T1, T2, T3, T4>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, params SortBy[] sort)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4>(where, propPath1, propPath2, propPath3, propPath4, sort));

        /// <summary>
        /// Lazily enumerates 4 directly decoded properties from records matching persistent State and an optional mask with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4)> IteratePropsWithIds<T1, T2, T3, T4>(long state, string propPath1, string propPath2, string propPath3, string propPath4, long? mask = null)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4>(state, propPath1, propPath2, propPath3, propPath4, mask));

        /// <summary>
        /// Lazily enumerates 5 directly decoded properties from every record.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> IterateProps<T1, T2, T3, T4, T5>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5>(propPath1, propPath2, propPath3, propPath4, propPath5));

        /// <summary>
        /// Lazily enumerates 5 directly decoded properties from records matching a completed condition and optional sort.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> IterateProps<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5>(where, propPath1, propPath2, propPath3, propPath4, propPath5, sort));

        /// <summary>
        /// Lazily enumerates 5 directly decoded properties from records matching persistent State and an optional mask.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> IterateProps<T1, T2, T3, T4, T5>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, long? mask = null)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5>(state, propPath1, propPath2, propPath3, propPath4, propPath5, mask));

        /// <summary>
        /// Lazily enumerates 5 directly decoded properties from every record with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> IteratePropsWithIds<T1, T2, T3, T4, T5>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(propPath1, propPath2, propPath3, propPath4, propPath5));

        /// <summary>
        /// Lazily enumerates 5 directly decoded properties from records matching a completed condition and optional sort with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> IteratePropsWithIds<T1, T2, T3, T4, T5>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, params SortBy[] sort)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(where, propPath1, propPath2, propPath3, propPath4, propPath5, sort));

        /// <summary>
        /// Lazily enumerates 5 directly decoded properties from records matching persistent State and an optional mask with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5)> IteratePropsWithIds<T1, T2, T3, T4, T5>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, long? mask = null)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5>(state, propPath1, propPath2, propPath3, propPath4, propPath5, mask));

        /// <summary>
        /// Lazily enumerates 6 directly decoded properties from every record.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> IterateProps<T1, T2, T3, T4, T5, T6>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5, T6>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6));

        /// <summary>
        /// Lazily enumerates 6 directly decoded properties from records matching a completed condition and optional sort.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> IterateProps<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5, T6>(where, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, sort));

        /// <summary>
        /// Lazily enumerates 6 directly decoded properties from records matching persistent State and an optional mask.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> IterateProps<T1, T2, T3, T4, T5, T6>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, long? mask = null)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5, T6>(state, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, mask));

        /// <summary>
        /// Lazily enumerates 6 directly decoded properties from every record with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> IteratePropsWithIds<T1, T2, T3, T4, T5, T6>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6));

        /// <summary>
        /// Lazily enumerates 6 directly decoded properties from records matching a completed condition and optional sort with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> IteratePropsWithIds<T1, T2, T3, T4, T5, T6>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, params SortBy[] sort)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(where, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, sort));

        /// <summary>
        /// Lazily enumerates 6 directly decoded properties from records matching persistent State and an optional mask with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6)> IteratePropsWithIds<T1, T2, T3, T4, T5, T6>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, long? mask = null)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6>(state, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, mask));

        /// <summary>
        /// Lazily enumerates 7 directly decoded properties from every record.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> IterateProps<T1, T2, T3, T4, T5, T6, T7>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7));

        /// <summary>
        /// Lazily enumerates 7 directly decoded properties from records matching a completed condition and optional sort.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> IterateProps<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(where, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7, sort));

        /// <summary>
        /// Lazily enumerates 7 directly decoded properties from records matching persistent State and an optional mask.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> IterateProps<T1, T2, T3, T4, T5, T6, T7>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, long? mask = null)
            => IterateReader(OpenValuesReader<T1, T2, T3, T4, T5, T6, T7>(state, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7, mask));

        /// <summary>
        /// Lazily enumerates 7 directly decoded properties from every record with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> IteratePropsWithIds<T1, T2, T3, T4, T5, T6, T7>(string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7));

        /// <summary>
        /// Lazily enumerates 7 directly decoded properties from records matching a completed condition and optional sort with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> IteratePropsWithIds<T1, T2, T3, T4, T5, T6, T7>(Condition<T>.EndCondition where, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, params SortBy[] sort)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(where, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7, sort));

        /// <summary>
        /// Lazily enumerates 7 directly decoded properties from records matching persistent State and an optional mask with each identity included.<br/>
        /// This is the convenience iterator over the corresponding reader; use <c>OpenValuesReader</c> when avoiding iterator state or pulling into caller-owned spans matters.<br/>
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
        /// <returns>A lazy sequence of typed value rows.<br/></returns>
        public IEnumerable<(ulong ID, T1? Value1, T2? Value2, T3? Value3, T4? Value4, T5? Value5, T6? Value6, T7? Value7)> IteratePropsWithIds<T1, T2, T3, T4, T5, T6, T7>(long state, string propPath1, string propPath2, string propPath3, string propPath4, string propPath5, string propPath6, string propPath7, long? mask = null)
            => IterateReader(OpenValuesReaderWithIds<T1, T2, T3, T4, T5, T6, T7>(state, propPath1, propPath2, propPath3, propPath4, propPath5, propPath6, propPath7, mask));

    }
}
