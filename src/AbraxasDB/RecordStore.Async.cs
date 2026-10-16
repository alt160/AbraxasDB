using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private static Task<TResult> RunAsync<TResult>(Func<TResult> func, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(func);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return func();
            }, cancellationToken);
        }

        /// <summary>
        /// Schedules hydration of one store-owned record on a worker thread.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="recordID">Identity that must belong to this store; replacement writes additionally require the record to exist.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the materialized matching record or requested projection.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<T> GetAsync(ulong recordID, CancellationToken cancellationToken = default)
            => RunAsync(() => Get(recordID), cancellationToken);

        /// <summary>
        /// Schedules a scalar property lookup and hydrates the first matching record, optionally requiring a persistent State value.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Property path or member expression for a supported scalar value expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null scalar value matched by the property lookup.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; the synchronous lookup planner selects available maintained indexes independently of it.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the materialized matching record or requested projection.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<T> GetAsync(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, long? state = null, bool useMemoryIndex = false, CancellationToken cancellationToken = default)
            => RunAsync(() => Get(uniquePropPath, uniquePropValue, state, useMemoryIndex), cancellationToken);

        /// <summary>
        /// Schedules a scalar property lookup and hydrates the first matching record, optionally requiring a persistent State value.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="uniquePropPath">Property path or member expression for a supported scalar value expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null scalar value matched by the property lookup.<br/></param>
        /// <param name="state">Optional persistent State value that must also match.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; the synchronous lookup planner selects available maintained indexes independently of it.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the materialized matching record or requested projection.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<T> GetAsync(string uniquePropPath, object uniquePropValue, long? state = null, bool useMemoryIndex = false, CancellationToken cancellationToken = default)
            => RunAsync(() => Get(uniquePropPath, uniquePropValue, state, useMemoryIndex), cancellationToken);

        /// <summary>
        /// Schedules hydration of one store-owned record into the requested CLR result shape.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection result type.<br/></typeparam>
        /// <param name="recordID">Identity that must belong to this store; replacement writes additionally require the record to exist.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the materialized matching record or requested projection.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<TOut> GetAsAsync<TOut>(ulong recordID, CancellationToken cancellationToken = default)
            => RunAsync(() => GetAs<TOut>(recordID), cancellationToken);

        /// <summary>
        /// Schedules a scalar property lookup and hydrates the first matching record into the requested CLR result shape.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection result type.<br/></typeparam>
        /// <param name="uniquePropPath">Property path or member expression for a supported scalar value expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null scalar value matched by the property lookup.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; the synchronous lookup planner selects available maintained indexes independently of it.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the materialized matching record or requested projection.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<TOut> GetAsAsync<TOut>(Expression<Func<T, object>> uniquePropPath, object uniquePropValue, bool useMemoryIndex = false, CancellationToken cancellationToken = default)
            => RunAsync(() => GetAs<TOut>(uniquePropPath, uniquePropValue, useMemoryIndex), cancellationToken);

        /// <summary>
        /// Schedules a scalar property lookup and hydrates the first matching record into the requested CLR result shape.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection result type.<br/></typeparam>
        /// <param name="uniquePropPath">Property path or member expression for a supported scalar value expected to identify one record.<br/></param>
        /// <param name="uniquePropValue">Non-null scalar value matched by the property lookup.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; the synchronous lookup planner selects available maintained indexes independently of it.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the materialized matching record or requested projection.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<TOut> GetAsAsync<TOut>(string uniquePropPath, object uniquePropValue, bool useMemoryIndex = false, CancellationToken cancellationToken = default)
            => RunAsync(() => GetAs<TOut>(uniquePropPath, uniquePropValue, useMemoryIndex), cancellationToken);

        /// <summary>
        /// Schedules hydration of the supplied record identities into a read-only list, preserving caller order and duplicates.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="recordIDs">Caller-supplied store-owned identities; the synchronous operation validates each identity.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing a read-only materialized result list in supplied identity order.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// A missing same-store record fails the batch; use a missing-record-policy overload when missing positions should be skipped or preserved.<br/>
        /// </remarks>
        public Task<IReadOnlyList<T>> GetManyAsync(IEnumerable<ulong> recordIDs, CancellationToken cancellationToken = default)
            => RunAsync(() => GetMany(recordIDs), cancellationToken);

        /// <summary>
        /// Schedules hydration of the supplied identities into a read-only list of the requested CLR shape, preserving caller order and duplicates.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested CLR hydration or projection result type.<br/></typeparam>
        /// <param name="recordIDs">Caller-supplied store-owned identities; the synchronous operation validates each identity.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing a read-only materialized result list in supplied identity order.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// A missing same-store record fails the batch; use a missing-record-policy overload when missing positions should be skipped or preserved.<br/>
        /// </remarks>
        public Task<IReadOnlyList<TOut>> GetManyAsAsync<TOut>(IEnumerable<ulong> recordIDs, CancellationToken cancellationToken = default)
            => RunAsync(() => GetManyAs<TOut>(recordIDs), cancellationToken);

        /// <summary>
        /// Asynchronously materializes the requested identities using the selected missing-record behavior.<br/>
        /// The synchronous batch contract remains authoritative; this member schedules that work and preserves caller order and duplicates.<br/>
        /// </summary>
        /// <param name="recordIDs">Ordered identities to retrieve.<br/></param>
        /// <param name="missingRecordBehavior">Behavior applied to same-store identities that no longer exist.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before execution begins.<br/></param>
        /// <returns>A task producing the policy-shaped read-only result list.<br/></returns>
        public Task<IReadOnlyList<T?>> GetManyAsync(
            IEnumerable<ulong> recordIDs,
            MissingRecordBehavior missingRecordBehavior,
            CancellationToken cancellationToken = default)
            => RunAsync(() => GetMany(recordIDs, missingRecordBehavior), cancellationToken);

        /// <summary>
        /// Asynchronously reprojects the requested identities using the selected missing-record behavior.<br/>
        /// Missing positions may be skipped or represented by <see langword="default"/> without changing cross-store identity validation.<br/>
        /// </summary>
        /// <typeparam name="TOut">Requested Inheto materialization or reprojection shape.<br/></typeparam>
        /// <param name="recordIDs">Ordered identities to retrieve.<br/></param>
        /// <param name="missingRecordBehavior">Behavior applied to same-store identities that no longer exist.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before execution begins.<br/></param>
        /// <returns>A task producing the policy-shaped projected result list.<br/></returns>
        public Task<IReadOnlyList<TOut?>> GetManyAsAsync<TOut>(
            IEnumerable<ulong> recordIDs,
            MissingRecordBehavior missingRecordBehavior,
            CancellationToken cancellationToken = default)
            => RunAsync(() => GetManyAs<TOut>(recordIDs, missingRecordBehavior), cancellationToken);

        /// <summary>
        /// Schedules deletion of one store-owned record and its maintained index entries.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="recordID">Identity that must belong to this store; replacement writes additionally require the record to exist.<br/></param>
        /// <param name="zeroWrite">Whether to overwrite discoverable physical payload copies with zeros; not a guarantee of device-level secure erasure.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing true when the record existed and was deleted, otherwise false.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<bool> DeleteAsync(ulong recordID, bool zeroWrite = false, CancellationToken cancellationToken = default)
            => RunAsync(() => Delete(recordID, zeroWrite), cancellationToken);

        /// <summary>
        /// Schedules deletion of the supplied store-owned identities, with optional payload zero-writing and post-delete defragmentation.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="recordIDs">Caller-supplied store-owned identities; the synchronous operation validates each identity.<br/></param>
        /// <param name="zeroWrite">Whether to overwrite discoverable physical payload copies with zeros; not a guarantee of device-level secure erasure.<br/></param>
        /// <param name="compactAfter">Whether to run optional online defragmentation after deletion.<br/></param>
        /// <param name="compactTimeout">Maximum duration for optional post-delete defragmentation; null uses the synchronous API default.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the identities successfully deleted by the synchronous operation.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<IReadOnlyList<ulong>> DeleteManyAsync(IEnumerable<ulong> recordIDs, bool zeroWrite = false, bool compactAfter = false, TimeSpan? compactTimeout = null, CancellationToken cancellationToken = default)
            => RunAsync(() => DeleteMany(recordIDs, zeroWrite, compactAfter, compactTimeout), cancellationToken);

        /// <summary>
        /// Schedules a new-record write with a freshly allocated identity and maintained-index convergence.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="record">Non-null source object to persist.<br/></param>
        /// <param name="state">Optional developer-owned persistent State value supplied with the write.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the new or replaced record identity.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<ulong> PutAsync(T record, long? state = null, CancellationToken cancellationToken = default)
            => RunAsync(() => Put(record, state), cancellationToken);

        /// <summary>
        /// Schedules replacement of an existing store-owned record and maintained-index convergence.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="recordID">Identity that must belong to this store; replacement writes additionally require the record to exist.<br/></param>
        /// <param name="record">Non-null source object to persist.<br/></param>
        /// <param name="state">Optional developer-owned persistent State value supplied with the write.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the new or replaced record identity.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<ulong> PutAsync(ulong recordID, T record, long? state = null, CancellationToken cancellationToken = default)
            => RunAsync(() => Put(recordID, record, state), cancellationToken);

        /// <summary>
        /// Schedules a new-record write with one handling mode applied to every registered link.<br/>
        /// The synchronous link-write contract remains authoritative and cancellation is observed before scheduled execution begins.<br/>
        /// </summary>
        /// <param name="record">Record instance to persist.<br/></param>
        /// <param name="linkHandling">Handling applied to every registered link.<br/></param>
        /// <param name="state">Optional developer State value.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and execution.<br/></param>
        /// <returns>A task producing the new record identity.<br/></returns>
        public Task<ulong> PutAsync(
            T record,
            LinkHandling linkHandling,
            long? state = null,
            CancellationToken cancellationToken = default)
            => RunAsync(() => Put(record, linkHandling, state), cancellationToken);

        /// <summary>
        /// Schedules an existing-record replacement with one handling mode applied to every registered link.<br/>
        /// The synchronous link-write contract remains authoritative and validates ownership before mutation.<br/>
        /// </summary>
        /// <param name="recordID">Existing identity owned by this store.<br/></param>
        /// <param name="record">Replacement record instance.<br/></param>
        /// <param name="linkHandling">Handling applied to every registered link.<br/></param>
        /// <param name="state">Optional developer State value.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and execution.<br/></param>
        /// <returns>A task producing the replaced record identity.<br/></returns>
        public Task<ulong> PutAsync(
            ulong recordID,
            T record,
            LinkHandling linkHandling,
            long? state = null,
            CancellationToken cancellationToken = default)
            => RunAsync(() => Put(recordID, record, linkHandling, state), cancellationToken);

        /// <summary>
        /// Schedules a new-record write that stores every registered link absent without invoking link serializers.<br/>
        /// </summary>
        /// <param name="record">Record instance to persist.<br/></param>
        /// <param name="state">Optional developer State value.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and execution.<br/></param>
        /// <returns>A task producing the new record identity.<br/></returns>
        public Task<ulong> PutNoLinksAsync(
            T record,
            long? state = null,
            CancellationToken cancellationToken = default)
            => RunAsync(() => PutNoLinks(record, state), cancellationToken);

        /// <summary>
        /// Schedules an existing-record replacement that preserves every registered stored link payload without invoking link serializers.<br/>
        /// </summary>
        /// <param name="recordID">Existing identity owned by this store.<br/></param>
        /// <param name="record">Replacement record instance.<br/></param>
        /// <param name="state">Optional developer State value.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and execution.<br/></param>
        /// <returns>A task producing the replaced record identity.<br/></returns>
        public Task<ulong> PutNoLinksAsync(
            ulong recordID,
            T record,
            long? state = null,
            CancellationToken cancellationToken = default)
            => RunAsync(() => PutNoLinks(recordID, record, state), cancellationToken);

        /// <summary>
        /// Schedules an existing-record replacement with independently selected handling for registered links.<br/>
        /// Value tuples supplied by the caller retain their implicit conversion to <see cref="LinkWriteRule"/> before scheduling.<br/>
        /// </summary>
        /// <param name="recordID">Existing identity owned by this store.<br/></param>
        /// <param name="record">Replacement record instance.<br/></param>
        /// <param name="link">First registered link write rule.<br/></param>
        /// <param name="additionalLinks">Optional additional registered link write rules.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and execution.<br/></param>
        /// <returns>A task producing the replaced record identity.<br/></returns>
        public Task<ulong> PutAsync(
            ulong recordID,
            T record,
            LinkWriteRule link,
            LinkWriteRule[] additionalLinks,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(additionalLinks);
            return RunAsync(() => Put(recordID, record, link, additionalLinks), cancellationToken);
        }

        /// <summary>
        /// Schedules an existing-record replacement that clears every registered link from storage without modifying the caller object.<br/>
        /// </summary>
        /// <param name="recordID">Existing identity owned by this store.<br/></param>
        /// <param name="record">Replacement record instance.<br/></param>
        /// <param name="state">Optional developer State value.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and execution.<br/></param>
        /// <returns>A task producing the replaced record identity.<br/></returns>
        public Task<ulong> PutClearLinksAsync(
            ulong recordID,
            T record,
            long? state = null,
            CancellationToken cancellationToken = default)
            => RunAsync(() => PutClearLinks(recordID, record, state), cancellationToken);

        /// <summary>
        /// Schedules new-record writes for the supplied sequence and returns generated identities in write order.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="records">Sequence of non-null source records to persist.<br/></param>
        /// <param name="state">Optional developer-owned persistent State value supplied with the write.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing generated record identities in write order.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<IReadOnlyList<ulong>> PutManyAsync(IEnumerable<T> records, long? state = null, CancellationToken cancellationToken = default)
            => RunAsync(() => PutMany(records, state), cancellationToken);

        /// <summary>
        /// Schedules the synchronous bulk-write API and returns its generated identities and caller-owned indexing handle.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="records">Sequence of non-null source records to persist.<br/></param>
        /// <param name="state">Optional developer-owned persistent State value supplied with the write.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing generated identities and the caller-owned batch handle.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// After successful completion, dispose the returned indexing handle when observation is finished; its snapshot may be retained independently.<br/>
        /// </remarks>
        public Task<PutManyFastResult> PutManyFastAsync(IEnumerable<T> records, long? state = null, CancellationToken cancellationToken = default)
            => RunAsync(() => PutManyFast(records, state), cancellationToken);

        /// <summary>
        /// Schedules retrieval of the store record count.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the authoritative matching record count.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<ulong> CountAsync(CancellationToken cancellationToken = default)
            => RunAsync(() => Count, cancellationToken);

        /// <summary>
        /// Schedules an authoritative count of records matching the supplied condition without hydrating full record objects.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="where">Completed condition used by the synchronous count planner.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing the authoritative matching record count.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<ulong> CountAsync(Condition<T>.EndCondition where, CancellationToken cancellationToken = default)
            => RunAsync(() => CountWhere(where), cancellationToken);

        /// <summary>
        /// Schedules an authoritative existence check for one store-owned identity without hydrating a record object.<br/>
        /// This is scheduled synchronous work, not a cancellable asynchronous storage-I/O implementation.<br/>
        /// </summary>
        /// <param name="recordID">Identity that must belong to this store; replacement writes additionally require the record to exist.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and before the synchronous operation starts, not during an already-running operation.<br/></param>
        /// <returns>A task producing true when the record currently exists, otherwise false.<br/></returns>
        /// <remarks>
        /// Cancellation is checked before scheduling and again before execution starts; once the synchronous operation begins, this token does not interrupt it.<br/>
        /// </remarks>
        public Task<bool> ExistsAsync(ulong recordID, CancellationToken cancellationToken = default)
            => RunAsync(() => Exists(recordID), cancellationToken);

        /// <summary>
        /// Asynchronously determines whether at least one authoritative record satisfies the specified condition.<br/>
        /// The scheduled operation uses condition planning and stops after the first matching identity without materializing a record object.<br/>
        /// </summary>
        /// <param name="where">Condition built by the Abraxas condition builder.<br/></param>
        /// <param name="cancellationToken">Token checked before scheduling and execution.<br/></param>
        /// <returns>A task producing <see langword="true"/> when the condition has at least one authoritative match; otherwise <see langword="false"/>.<br/></returns>
        public Task<bool> ExistsAsync(
            Condition<T>.EndCondition where,
            CancellationToken cancellationToken = default)
            => RunAsync(() => Exists(where), cancellationToken);

        /// <summary>
        /// Asynchronously tests authoritative record existence and persistent State membership.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="cancellationToken">Token observed before and during scheduled execution.<br/></param>
        /// <returns>A task producing true when the record exists and its assigned State matches.<br/></returns>
        public Task<bool> ExistsAsync(ulong recordID, long state, long? mask = null, CancellationToken cancellationToken = default)
            => RunAsync(() => Exists(recordID, state, mask), cancellationToken);

        /// <summary>
        /// Retains the historical asynchronous State-existence signature while State remains persistent-only.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="mask">Optional State bitmask.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <param name="cancellationToken">Token observed before and during scheduled execution.<br/></param>
        /// <returns>A task producing true when the record exists and its assigned State matches.<br/></returns>
        [Obsolete("State is always persistent. Use ExistsAsync(recordID, state, mask, cancellationToken) instead.")]
        public Task<bool> ExistsAsync(ulong recordID, long state, long? mask, bool useMemoryIndex, CancellationToken cancellationToken = default)
        {
            _ = useMemoryIndex;
            return ExistsAsync(recordID, state, mask, cancellationToken);
        }

        /// <summary>
        /// Retains historical named <c>useMemoryIndex</c> asynchronous State calls that omitted a mask.<br/>
        /// </summary>
        /// <param name="recordID">Identifier that must belong to this store.<br/></param>
        /// <param name="state">State value to compare.<br/></param>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <param name="cancellationToken">Token observed before and during scheduled execution.<br/></param>
        /// <returns>A task producing true when the record exists and its assigned State matches.<br/></returns>
        [Obsolete("State is always persistent. Use ExistsAsync(recordID, state, cancellationToken: cancellationToken) instead.")]
        public Task<bool> ExistsAsync(ulong recordID, long state, bool useMemoryIndex, CancellationToken cancellationToken = default)
        {
            _ = useMemoryIndex;
            return ExistsAsync(recordID, state, mask: null, cancellationToken: cancellationToken);
        }
    }
}
