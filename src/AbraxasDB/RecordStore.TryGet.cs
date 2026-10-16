using System.Linq.Expressions;
using System.Numerics;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Inheto;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Attempts to retrieve and materialize one record directly from its owning-store identity.<br/>
        /// Ordinary absence returns <see langword="false"/> without using an exception, while malformed or cross-store identities and materialization failures remain explicit errors.<br/>
        /// The authoritative Fractal payload is fetched once and remains borrowed only for the synchronous Inheto materialization call.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="record">Receives the materialized record when found, or <see langword="default"/> when absent.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGet(ulong recordID, [NotNullWhen(true)] out T? record)
            => TryMaterializeRecordCore(recordID, nameof(TryGet), out record);

        /// <summary>
        /// Retrieves one record directly by identity or returns <see langword="default"/> when it is absent.<br/>
        /// Choose this compact form when the caller does not need to distinguish absence from a materialized default value; use <see cref="TryGet(ulong, out T)"/> when presence matters.<br/>
        /// Invalid or cross-store identities and materialization failures continue to throw.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <returns>The materialized record when found; otherwise <see langword="default"/>.<br/></returns>
        public T? GetOrDefault(ulong recordID)
            => TryGet(recordID, out T? record) ? record : default;

        /// <summary>
        /// Attempts to retrieve one record by identity and materialize it as a caller-selected projection or record shape.<br/>
        /// The operation performs one authoritative Fractal fetch and returns <see langword="false"/> only for ordinary record absence.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="record">Receives the projected record when found, or <see langword="default"/> when absent.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetAs<TOut>(ulong recordID, [NotNullWhen(true)] out TOut? record)
            => TryMaterializeRecordCore(recordID, nameof(TryGetAs), out record);

        /// <summary>
        /// Retrieves one identity as a caller-selected projection or returns <see langword="default"/> when absent.<br/>
        /// This is the compact counterpart to <see cref="TryGetAs{TOut}(ulong, out TOut)"/> and intentionally collapses absence into the requested type's default value.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <returns>The projected record when found; otherwise <see langword="default"/>.<br/></returns>
        public TOut? GetAsOrDefault<TOut>(ulong recordID)
            => TryGetAs(recordID, out TOut? record) ? record : default;

        /// <summary>
        /// Attempts to retrieve one record only when its persistent developer-owned State satisfies the requested exact or masked comparison.<br/>
        /// State is tested through LibraDex before the single Fractal payload fetch, so a mismatch performs no record materialization work.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="state">State value to match.<br/></param>
        /// <param name="record">Receives the materialized record when both identity and State match.<br/></param>
        /// <param name="mask">Optional mask applying <c>(State &amp; mask) == state</c>.<br/></param>
        /// <returns><see langword="true"/> when State matches and the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGet(ulong recordID, long state, [NotNullWhen(true)] out T? record, long? mask = null)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!TryRecordMatchesStateCore(recordID, state, mask))
            {
                record = default;
                return false;
            }

            return TryMaterializeValidatedRecordCore(recordID, nameof(TryGet), out record);
        }

        /// <summary>
        /// Retrieves one State-qualified record or returns <see langword="default"/> when the record is absent or its State does not match.<br/>
        /// Use the corresponding <see cref="TryGet(ulong, long, out T, long?)"/> overload when absence and State mismatch must remain observable.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="state">State value to match.<br/></param>
        /// <param name="mask">Optional mask applying <c>(State &amp; mask) == state</c>.<br/></param>
        /// <returns>The matching record, or <see langword="default"/>.<br/></returns>
        public T? GetOrDefault(ulong recordID, long state, long? mask = null)
            => TryGet(recordID, state, out T? record, mask) ? record : default;

        /// <summary>
        /// Attempts to retrieve one State-qualified identity as a caller-selected projection shape.<br/>
        /// State mismatch and ordinary record absence return <see langword="false"/> without materializing a payload.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="state">State value to match.<br/></param>
        /// <param name="record">Receives the projection when found and matched.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns><see langword="true"/> when State matches and the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetAs<TOut>(ulong recordID, long state, [NotNullWhen(true)] out TOut? record, long? mask = null)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!TryRecordMatchesStateCore(recordID, state, mask))
            {
                record = default;
                return false;
            }

            return TryMaterializeValidatedRecordCore(recordID, nameof(TryGetAs), out record);
        }

        /// <summary>
        /// Retrieves one State-qualified identity as a caller-selected shape or returns <see langword="default"/>.<br/>
        /// This compact form intentionally combines record absence and State mismatch.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="state">State value to match.<br/></param>
        /// <param name="mask">Optional State mask.<br/></param>
        /// <returns>The matching projection, or <see langword="default"/>.<br/></returns>
        public TOut? GetAsOrDefault<TOut>(ulong recordID, long state, long? mask = null)
            => TryGetAs(recordID, state, out TOut? record, mask) ? record : default;

        /// <summary>
        /// Attempts to retrieve one record through a property path whose value is expected to identify at most one record.<br/>
        /// A compatible unique LibraDex index is used directly when available; otherwise the ordinary condition planner preserves authoritative fallback semantics.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Property path containing the unique lookup value.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="record">Receives the materialized record when found.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns><see langword="true"/> when one matching live record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGet(string uniquePropPath, object uniquePropValue, [NotNullWhen(true)] out T? record, long? state = null)
        {
            if (!TryResolveUniqueRecordID(uniquePropPath, uniquePropValue, state, out ulong recordID))
            {
                record = default;
                return false;
            }

            return TryMaterializeRecordCore(recordID, nameof(TryGet), out record);
        }

        /// <summary>
        /// Attempts a unique-property record lookup using an expression-derived Inheto property path.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Expression identifying the unique property.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="record">Receives the materialized record when found.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns><see langword="true"/> when one matching live record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGet(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, [NotNullWhen(true)] out T? record, long? state = null)
        {
            ArgumentNullException.ThrowIfNull(uniquePropPath);
            return TryGet(uniquePropPath.GetPropertyPath(), uniquePropValue, out record, state);
        }

        /// <summary>
        /// Retrieves one unique-property record or returns <see langword="default"/> when no match exists.<br/>
        /// This compact form intentionally combines a missing key, State mismatch, and absent record into the default result.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Property path containing the unique lookup value.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns>The matching record, or <see langword="default"/>.<br/></returns>
        public T? GetOrDefault(string uniquePropPath, object uniquePropValue, long? state = null)
            => TryGet(uniquePropPath, uniquePropValue, out T? record, state) ? record : default;

        /// <summary>
        /// Retrieves one expression-selected unique-property record or returns <see langword="default"/> when no match exists.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Expression identifying the unique property.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns>The matching record, or <see langword="default"/>.<br/></returns>
        public T? GetOrDefault(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, long? state = null)
        {
            ArgumentNullException.ThrowIfNull(uniquePropPath);
            return GetOrDefault(uniquePropPath.GetPropertyPath(), uniquePropValue, state);
        }

        /// <summary>
        /// Attempts a unique-property lookup and materializes the matching identity as a caller-selected shape.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="uniquePropPath">Property path containing the unique lookup value.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="record">Receives the projected record when found.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns><see langword="true"/> when one matching live record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetAs<TOut>(string uniquePropPath, object uniquePropValue, [NotNullWhen(true)] out TOut? record, long? state = null)
        {
            if (!TryResolveUniqueRecordID(uniquePropPath, uniquePropValue, state, out ulong recordID))
            {
                record = default;
                return false;
            }

            return TryMaterializeRecordCore(recordID, nameof(TryGetAs), out record);
        }

        /// <summary>
        /// Attempts an expression-selected unique-property lookup and materializes the matching identity as a caller-selected shape.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="uniquePropPath">Expression identifying the unique property.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="record">Receives the projected record when found.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns><see langword="true"/> when one matching live record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetAs<TOut>(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, [NotNullWhen(true)] out TOut? record, long? state = null)
        {
            ArgumentNullException.ThrowIfNull(uniquePropPath);
            return TryGetAs(uniquePropPath.GetPropertyPath(), uniquePropValue, out record, state);
        }

        /// <summary>
        /// Retrieves one unique-property match as a caller-selected shape or returns <see langword="default"/>.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="uniquePropPath">Property path containing the unique lookup value.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns>The matching projection, or <see langword="default"/>.<br/></returns>
        public TOut? GetAsOrDefault<TOut>(string uniquePropPath, object uniquePropValue, long? state = null)
            => TryGetAs(uniquePropPath, uniquePropValue, out TOut? record, state) ? record : default;

        /// <summary>
        /// Retrieves one expression-selected unique-property match as a caller-selected shape or returns <see langword="default"/>.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization shape.<br/></typeparam>
        /// <param name="uniquePropPath">Expression identifying the unique property.<br/></param>
        /// <param name="uniquePropValue">Exact scalar value to locate.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <returns>The matching projection, or <see langword="default"/>.<br/></returns>
        public TOut? GetAsOrDefault<TOut>(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, long? state = null)
        {
            ArgumentNullException.ThrowIfNull(uniquePropPath);
            return GetAsOrDefault<TOut>(uniquePropPath.GetPropertyPath(), uniquePropValue, state);
        }

        /// <summary>
        /// Attempts to read one property directly from one record identity without materializing the complete record.<br/>
        /// A <see langword="true"/> result means the record exists; the property value itself may legitimately be null, missing, empty, or the requested type's default value.<br/>
        /// </summary>
        /// <typeparam name="TValue">Requested runtime property type.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="propPath">Inheto property path to read.<br/></param>
        /// <param name="value">Receives the converted property value when the record exists.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetValue<TValue>(ulong recordID, string propPath, out TValue? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(propPath);
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.TryGetOne(recordID, out var record))
            {
                value = default;
                return false;
            }

            using (record)
            {
                InhetoBinary binary = CreateRecordBinary(recordID, record.Data);
                value = binary.ReadPropAs<TValue>(propPath, NativeDeserializationOptions);
                return true;
            }
        }

        /// <summary>
        /// Attempts to read one expression-selected property directly from one record identity.<br/>
        /// </summary>
        /// <typeparam name="TValue">Requested runtime property type.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="propPath">Expression identifying the property to read.<br/></param>
        /// <param name="value">Receives the converted property value when the record exists.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetValue<TValue>(ulong recordID, Expression<Func<T, object>> propPath, out TValue? value)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return TryGetValue(recordID, propPath.GetPropertyPath(), out value);
        }

        /// <summary>
        /// Reads one property directly from one identity or returns <see langword="default"/> when the record is absent.<br/>
        /// Record absence and a present property whose value is null, missing, or default are intentionally indistinguishable; use <see cref="TryGetValue{TValue}(ulong, string, out TValue)"/> when that distinction matters.<br/>
        /// </summary>
        /// <typeparam name="TValue">Requested runtime property type.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="propPath">Inheto property path to read.<br/></param>
        /// <returns>The converted property value, or <see langword="default"/>.<br/></returns>
        public TValue? GetValueOrDefault<TValue>(ulong recordID, string propPath)
            => TryGetValue(recordID, propPath, out TValue? value) ? value : default;

        /// <summary>
        /// Reads one expression-selected property directly from one identity or returns <see langword="default"/> when the record is absent.<br/>
        /// </summary>
        /// <typeparam name="TValue">Requested runtime property type.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="propPath">Expression identifying the property to read.<br/></param>
        /// <returns>The converted property value, or <see langword="default"/>.<br/></returns>
        public TValue? GetValueOrDefault<TValue>(ulong recordID, Expression<Func<T, object>> propPath)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return GetValueOrDefault<TValue>(recordID, propPath.GetPropertyPath());
        }

        /// <summary>
        /// Attempts to read multiple runtime-shaped properties from one identity without materializing the complete record.<br/>
        /// The result list is allocated only after the authoritative record has been found; a missing record returns <see langword="false"/> with a null result.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="values">Receives property values in the supplied ordinal order when the record exists.<br/></param>
        /// <param name="propPaths">One or more Inheto property paths to read.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetValues(ulong recordID, out List<object?>? values, params string[] propPaths)
        {
            ValidateRetrievalPropPaths(propPaths);
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.TryGetOne(recordID, out var record))
            {
                values = default;
                return false;
            }

            using (record)
            {
                InhetoBinary binary = CreateRecordBinary(recordID, record.Data);
                var result = new List<object?>(propPaths.Length);
                for (int index = 0; index < propPaths.Length; index++)
                    result.Add(binary.ReadPropAs<object?>(propPaths[index], NativeDeserializationOptions));
                values = result;
                return true;
            }
        }

        /// <summary>
        /// Reads multiple runtime-shaped properties from one identity or returns null when the record is absent.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="propPaths">One or more Inheto property paths to read.<br/></param>
        /// <returns>Property values in supplied order, or null when the record is absent.<br/></returns>
        public List<object?>? GetValuesOrDefault(ulong recordID, params string[] propPaths)
            => TryGetValues(recordID, out List<object?>? values, propPaths) ? values : default;

        /// <summary>
        /// Attempts to read multiple expression-selected runtime-shaped properties from one identity.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="values">Receives property values in supplied order when the record exists.<br/></param>
        /// <param name="propPaths">Expressions identifying the properties to read.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetValues(ulong recordID, out List<object?>? values, params Expression<Func<T, object>>[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(propPaths);
            string[] paths = new string[propPaths.Length];
            for (int index = 0; index < propPaths.Length; index++)
                paths[index] = (propPaths[index] ?? throw new ArgumentException("Property expressions cannot contain null entries.", nameof(propPaths))).GetPropertyPath();
            return TryGetValues(recordID, out values, paths);
        }

        /// <summary>
        /// Reads multiple expression-selected runtime-shaped properties from one identity or returns null when the record is absent.<br/>
        /// </summary>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="propPaths">Expressions identifying the properties to read.<br/></param>
        /// <returns>Property values in supplied order, or null when the record is absent.<br/></returns>
        public List<object?>? GetValuesOrDefault(ulong recordID, params Expression<Func<T, object>>[] propPaths)
            => TryGetValues(recordID, out List<object?>? values, propPaths) ? values : default;

        /// <summary>
        /// Attempts to read two typed properties from one record identity as a value tuple.<br/>
        /// The record payload is fetched once and no per-row container object is allocated.<br/>
        /// </summary>
        /// <typeparam name="T1">First requested property type.<br/></typeparam>
        /// <typeparam name="T2">Second requested property type.<br/></typeparam>
        /// <param name="recordID">Identity whose encoded store key must match this store.<br/></param>
        /// <param name="values">Receives the typed property tuple when the record exists.<br/></param>
        /// <param name="propPaths">Exactly two Inheto property paths in tuple order.<br/></param>
        /// <returns><see langword="true"/> when the record exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetValues<T1, T2>(ulong recordID, out (T1?, T2?) values, params string[] propPaths)
            => TryGetTypedValuesCore(
                recordID,
                propPaths,
                2,
                static (binary, paths, options) => (
                    binary.ReadPropAs<T1>(paths[0], options),
                    binary.ReadPropAs<T2>(paths[1], options)),
                out values);

        /// <summary>
        /// Reads two typed properties from one identity or returns the tuple's default value when the record is absent.<br/>
        /// Use <c>TryGetValues&lt;T1, T2&gt;</c> when record presence must remain distinct.<br/>
        /// </summary>
        public (T1?, T2?) GetValuesOrDefault<T1, T2>(ulong recordID, params string[] propPaths)
            => TryGetValues(recordID, out (T1?, T2?) values, propPaths) ? values : default;

        /// <summary>
        /// Attempts to read three typed properties from one record identity as a value tuple.<br/>
        /// This fills the historical three-value direct-retrieval arity while retaining one authoritative payload fetch.<br/>
        /// </summary>
        public bool TryGetValues<T1, T2, T3>(ulong recordID, out (T1?, T2?, T3?) values, params string[] propPaths)
            => TryGetTypedValuesCore(
                recordID,
                propPaths,
                3,
                static (binary, paths, options) => (
                    binary.ReadPropAs<T1>(paths[0], options),
                    binary.ReadPropAs<T2>(paths[1], options),
                    binary.ReadPropAs<T3>(paths[2], options)),
                out values);

        /// <summary>
        /// Reads three typed properties from one identity or returns the tuple's default value when the record is absent.<br/>
        /// </summary>
        public (T1?, T2?, T3?) GetValuesOrDefault<T1, T2, T3>(ulong recordID, params string[] propPaths)
            => TryGetValues(recordID, out (T1?, T2?, T3?) values, propPaths) ? values : default;

        /// <summary>
        /// Attempts to read four typed properties from one record identity as a value tuple.<br/>
        /// </summary>
        public bool TryGetValues<T1, T2, T3, T4>(ulong recordID, out (T1?, T2?, T3?, T4?) values, params string[] propPaths)
            => TryGetTypedValuesCore(
                recordID,
                propPaths,
                4,
                static (binary, paths, options) => (
                    binary.ReadPropAs<T1>(paths[0], options),
                    binary.ReadPropAs<T2>(paths[1], options),
                    binary.ReadPropAs<T3>(paths[2], options),
                    binary.ReadPropAs<T4>(paths[3], options)),
                out values);

        /// <summary>
        /// Reads four typed properties from one identity or returns the tuple's default value when the record is absent.<br/>
        /// </summary>
        public (T1?, T2?, T3?, T4?) GetValuesOrDefault<T1, T2, T3, T4>(ulong recordID, params string[] propPaths)
            => TryGetValues(recordID, out (T1?, T2?, T3?, T4?) values, propPaths) ? values : default;

        /// <summary>
        /// Attempts to read five typed properties from one record identity as a value tuple.<br/>
        /// </summary>
        public bool TryGetValues<T1, T2, T3, T4, T5>(ulong recordID, out (T1?, T2?, T3?, T4?, T5?) values, params string[] propPaths)
            => TryGetTypedValuesCore(
                recordID,
                propPaths,
                5,
                static (binary, paths, options) => (
                    binary.ReadPropAs<T1>(paths[0], options),
                    binary.ReadPropAs<T2>(paths[1], options),
                    binary.ReadPropAs<T3>(paths[2], options),
                    binary.ReadPropAs<T4>(paths[3], options),
                    binary.ReadPropAs<T5>(paths[4], options)),
                out values);

        /// <summary>
        /// Reads five typed properties from one identity or returns the tuple's default value when the record is absent.<br/>
        /// </summary>
        public (T1?, T2?, T3?, T4?, T5?) GetValuesOrDefault<T1, T2, T3, T4, T5>(ulong recordID, params string[] propPaths)
            => TryGetValues(recordID, out (T1?, T2?, T3?, T4?, T5?) values, propPaths) ? values : default;

        /// <summary>
        /// Attempts to read six typed properties from one record identity as a value tuple.<br/>
        /// </summary>
        public bool TryGetValues<T1, T2, T3, T4, T5, T6>(ulong recordID, out (T1?, T2?, T3?, T4?, T5?, T6?) values, params string[] propPaths)
            => TryGetTypedValuesCore(
                recordID,
                propPaths,
                6,
                static (binary, paths, options) => (
                    binary.ReadPropAs<T1>(paths[0], options),
                    binary.ReadPropAs<T2>(paths[1], options),
                    binary.ReadPropAs<T3>(paths[2], options),
                    binary.ReadPropAs<T4>(paths[3], options),
                    binary.ReadPropAs<T5>(paths[4], options),
                    binary.ReadPropAs<T6>(paths[5], options)),
                out values);

        /// <summary>
        /// Reads six typed properties from one identity or returns the tuple's default value when the record is absent.<br/>
        /// </summary>
        public (T1?, T2?, T3?, T4?, T5?, T6?) GetValuesOrDefault<T1, T2, T3, T4, T5, T6>(ulong recordID, params string[] propPaths)
            => TryGetValues(recordID, out (T1?, T2?, T3?, T4?, T5?, T6?) values, propPaths) ? values : default;

        /// <summary>
        /// Attempts to read seven typed properties from one record identity as a value tuple.<br/>
        /// </summary>
        public bool TryGetValues<T1, T2, T3, T4, T5, T6, T7>(ulong recordID, out (T1?, T2?, T3?, T4?, T5?, T6?, T7?) values, params string[] propPaths)
            => TryGetTypedValuesCore(
                recordID,
                propPaths,
                7,
                static (binary, paths, options) => (
                    binary.ReadPropAs<T1>(paths[0], options),
                    binary.ReadPropAs<T2>(paths[1], options),
                    binary.ReadPropAs<T3>(paths[2], options),
                    binary.ReadPropAs<T4>(paths[3], options),
                    binary.ReadPropAs<T5>(paths[4], options),
                    binary.ReadPropAs<T6>(paths[5], options),
                    binary.ReadPropAs<T7>(paths[6], options)),
                out values);

        /// <summary>
        /// Reads seven typed properties from one identity or returns the tuple's default value when the record is absent.<br/>
        /// </summary>
        public (T1?, T2?, T3?, T4?, T5?, T6?, T7?) GetValuesOrDefault<T1, T2, T3, T4, T5, T6, T7>(ulong recordID, params string[] propPaths)
            => TryGetValues(recordID, out (T1?, T2?, T3?, T4?, T5?, T6?, T7?) values, propPaths) ? values : default;

        /// <summary>
        /// Performs the shared single-probe borrowed-payload materialization used by identity, State, and unique-key conveniences.<br/>
        /// </summary>
        private bool TryMaterializeRecordCore<TOut>(ulong recordID, string operationName, [NotNullWhen(true)] out TOut? record)
        {
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            return TryMaterializeValidatedRecordCore(recordID, operationName, out record);
        }

        /// <summary>
        /// Materializes one already store-validated identity while an enclosing operation scope remains active.<br/>
        /// The method performs the authoritative Fractal probe, borrows a present pooled payload for synchronous Inheto decoding, and returns that lease before completing.<br/>
        /// </summary>
        private bool TryMaterializeValidatedRecordCore<TOut>(ulong recordID, string operationName, [NotNullWhen(true)] out TOut? record)
        {
            if (!rbase.data.TryGetOne(recordID, out var fractalRecord))
            {
                record = default;
                return false;
            }

            using (fractalRecord)
            {
                record = MaterializeFractalRecordFast<TOut>(recordID, fractalRecord, operationName)!;
                return true;
            }
        }

        /// <summary>
        /// Executes one typed multi-property projector while the authoritative Fractal record and its borrowed Inheto payload remain alive.<br/>
        /// Static projector lambdas are cached by the runtime for each closed generic call shape and introduce no per-call closure allocation.<br/>
        /// </summary>
        private bool TryGetTypedValuesCore<TResult>(
            ulong recordID,
            string[] propPaths,
            int expectedPathCount,
            Func<InhetoBinary, string[], Inheto.DeserializationOptions, TResult> projector,
            out TResult values)
        {
            ArgumentNullException.ThrowIfNull(projector);
            EnsurePropPathCount(propPaths, expectedPathCount);
            ValidateRetrievalPropPaths(propPaths);
            using var operation = EnterStoreOperationScope();
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.TryGetOne(recordID, out var record))
            {
                values = default!;
                return false;
            }

            using (record)
            {
                InhetoBinary binary = CreateRecordBinary(recordID, record.Data);
                values = projector(binary, propPaths, NativeDeserializationOptions);
                return true;
            }
        }

        /// <summary>
        /// Tests one identity against the persistent State index without fetching its Fractal payload.<br/>
        /// </summary>
        private bool TryRecordMatchesStateCore(ulong recordID, long state, long? mask)
        {
            if (TryExistsByLibraDexState(recordID, state, mask, out bool matches))
                return matches;
            throw new InvalidOperationException(
                $"Operation '{nameof(TryGet)}' requires this store's reserved persistent LibraDex State index.");
        }

        /// <summary>
        /// Resolves one unique scalar value into a live record identity through the direct unique index or the authoritative condition planner.<br/>
        /// </summary>
        private bool TryResolveUniqueRecordID(string uniquePropPath, object uniquePropValue, long? state, out ulong recordID)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uniquePropPath);
            ArgumentNullException.ThrowIfNull(uniquePropValue);

            if (!state.HasValue &&
                !NativeDeserializationOptions.Aliases.Contains(uniquePropPath) &&
                TryGetUniqueLibraDexRecordID(uniquePropPath, uniquePropValue, out recordID))
                return true;

            Condition<T>.EndCondition equality = BuildUniqueEqualityCondition(uniquePropPath, uniquePropValue);
            Condition<T>.EndCondition where = state.HasValue
                ? Where.State.EqualTo(state.Value).And(equality).EndCondition
                : equality;
            using var identities = Readers.OpenIdentities(where);
            if (!identities.Read())
            {
                recordID = default;
                return false;
            }

            recordID = identities.Get();
            return true;
        }

        /// <summary>
        /// Builds one strongly typed equality condition from a supported runtime scalar without weakening backend-neutral planning.<br/>
        /// </summary>
        private Condition<T>.EndCondition BuildUniqueEqualityCondition(string propPath, object value)
        {
            var property = Where.PropPath(propPath);
            return value switch
            {
                byte typed => property.AsByte.EqualTo(typed).EndCondition,
                sbyte typed => property.AsSByte.EqualTo(typed).EndCondition,
                short typed => property.AsInt16.EqualTo(typed).EndCondition,
                ushort typed => property.AsUInt16.EqualTo(typed).EndCondition,
                int typed => property.AsInt32.EqualTo(typed).EndCondition,
                uint typed => property.AsUInt32.EqualTo(typed).EndCondition,
                long typed => property.AsInt64.EqualTo(typed).EndCondition,
                ulong typed => property.AsUInt64.EqualTo(typed).EndCondition,
                Int128 typed => property.AsInt128.EqualTo(typed).EndCondition,
                UInt128 typed => property.AsUInt128.EqualTo(typed).EndCondition,
                Half typed => property.AsHalf.EqualTo(typed).EndCondition,
                float typed => property.AsSingle.EqualTo(typed).EndCondition,
                double typed => property.AsDouble.EqualTo(typed).EndCondition,
                decimal typed => property.AsDecimal.EqualTo(typed).EndCondition,
                BigInteger typed => property.AsBigInteger.EqualTo(typed).EndCondition,
                bool typed => property.AsBoolean.EqualTo(typed).EndCondition,
                string typed => property.AsString.EqualTo(typed).EndCondition,
                char typed => property.AsString.EqualTo(typed.ToString()).EndCondition,
                Rune typed => property.AsString.EqualTo(typed.ToString()).EndCondition,
                Guid typed => property.AsGuid.EqualTo(typed).EndCondition,
                DateTime typed => property.AsDateTime.EqualTo(typed).EndCondition,
                DateOnly typed => property.AsDateOnly.EqualTo(typed).EndCondition,
                TimeOnly typed => property.AsTimeOnly.EqualTo(typed).EndCondition,
                TimeSpan typed => property.AsTimeSpan.EqualTo(typed).EndCondition,
                byte[] typed => property.AsBinary.EqualTo(typed).EndCondition,
                _ => throw new InvalidCastException(
                    "uniquePropValue must be a supported scalar: Boolean, signed/unsigned integral, floating point, Decimal, BigInteger, Guid, String, Char, Rune, DateTime, DateOnly, TimeOnly, TimeSpan, or Byte[]."),
            };
        }

        /// <summary>
        /// Validates a dynamic property-retrieval request once before opening authoritative storage.<br/>
        /// </summary>
        private static void ValidateRetrievalPropPaths(string[] propPaths)
        {
            ArgumentNullException.ThrowIfNull(propPaths);
            if (propPaths.Length == 0)
                throw new ArgumentException("At least one property path is required.", nameof(propPaths));
            for (int index = 0; index < propPaths.Length; index++)
                ArgumentException.ThrowIfNullOrWhiteSpace(propPaths[index]);
        }
    }
}
