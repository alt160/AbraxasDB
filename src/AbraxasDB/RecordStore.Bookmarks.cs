using System.Security.Cryptography;
using System.Text;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Creates immutable semantic continuation metadata for an unfiltered store read.<br/>
        /// </summary>
        private BookmarkReadContext CreateAllBookmarkContext(
            SortBy[]? sort = null,
            RecordBookmark? bookmark = null,
            BookmarkConsistency consistency = BookmarkConsistency.GenerationBound)
            => CreateBookmarkContext("All", sort, bookmark, consistency, unsupportedReason: null);

        /// <summary>
        /// Creates immutable continuation metadata for a complete unordered store read.<br/>
        /// The selection matches the natural complete-store population, while a distinct nondeterministic ordering identity prevents bookmarks from crossing between natural and physical readers.<br/>
        /// </summary>
        /// <param name="bookmark">Optional generation-bound continuation captured from the same unordered source.<br/></param>
        /// <param name="consistency">Requested continuation policy; live continuation is rejected because physical order is not a stable semantic order.<br/></param>
        /// <returns>Validated bookmark metadata for one unordered complete-store cursor.<br/></returns>
        private BookmarkReadContext CreateUnorderedBookmarkContext(
            RecordBookmark? bookmark = null,
            BookmarkConsistency consistency = BookmarkConsistency.GenerationBound)
            => CreateBookmarkContext(
                "All",
                sort: null,
                bookmark,
                consistency,
                unsupportedReason: null,
                trackTopology: false,
                orderingIdentity: "UnorderedComplete",
                deterministicOrdering: false);

        /// <summary>
        /// Creates immutable semantic continuation metadata for one completed condition.<br/>
        /// Runtime-only condition nodes remain executable, but their reader reports that no durable bookmark can be captured.<br/>
        /// </summary>
        private BookmarkReadContext CreateConditionBookmarkContext(
            Condition<T>.EndCondition where,
            SortBy[]? sort = null,
            RecordBookmark? bookmark = null,
            BookmarkConsistency consistency = BookmarkConsistency.GenerationBound)
        {
            ArgumentNullException.ThrowIfNull(where);
            string selection;
            string? unsupported = null;
            try
            {
                ConditionDescriptor<T> descriptor = where.ToDescriptor();
                selection = "Condition:" + descriptor.SemanticFingerprint;
                if (!descriptor.IsSystemPersistable)
                {
                    unsupported =
                        "This condition contains deferred or runtime application code without durable semantic identity. " +
                        "The reader remains usable, but its bookmark cannot be serialized or resumed.";
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                selection = "Condition:RuntimeOnly";
                unsupported =
                    "This condition contains runtime application code without durable semantic identity. " +
                    "The reader remains usable, but its bookmark cannot be serialized or resumed.";
            }

            bool trackTopology = sort is not { Length: > 0 };
            return CreateBookmarkContext(selection, sort, bookmark, consistency, unsupported, trackTopology);
        }

        /// <summary>
        /// Creates immutable semantic continuation metadata for a State or State-mask selection.<br/>
        /// </summary>
        private BookmarkReadContext CreateStateBookmarkContext(
            long state,
            long? mask,
            RecordBookmark? bookmark = null,
            BookmarkConsistency consistency = BookmarkConsistency.GenerationBound)
            => CreateBookmarkContext(
                $"State:{state.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{(mask.HasValue ? mask.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}",
                sort: null,
                bookmark,
                consistency,
                unsupportedReason: null,
                trackTopology: true);

        /// <summary>
        /// Creates immutable continuation metadata for an exact developer-selected index order and optional independent condition.<br/>
        /// The logical index name, key path, and direction participate in the ordering contract; no physical engine vocabulary is exposed or persisted in the bookmark shape.<br/>
        /// </summary>
        /// <param name="index">Exact store index selected to define order and indexed population.<br/></param>
        /// <param name="direction">Whole-index traversal direction.<br/></param>
        /// <param name="where">Optional independent condition applied within the selected index population.<br/></param>
        /// <param name="bookmark">Optional bookmark to validate and resume.<br/></param>
        /// <param name="consistency">Mutation-consistency policy for continuation.<br/></param>
        /// <returns>Validated bookmark state for the indexed identity reader.<br/></returns>
        private BookmarkReadContext CreateIndexBookmarkContext(
            RecordIndex index,
            RetrievalDirection direction,
            Condition<T>.EndCondition? where = null,
            RecordBookmark? bookmark = null,
            BookmarkConsistency consistency = BookmarkConsistency.GenerationBound)
        {
            string selection = $"IndexPopulation:{index.Name.Length}:{index.Name}";
            string? unsupported = null;
            if (where is not null)
            {
                try
                {
                    ConditionDescriptor<T> descriptor = where.ToDescriptor();
                    selection += "|Condition:" + descriptor.SemanticFingerprint;
                    if (!descriptor.IsSystemPersistable)
                    {
                        unsupported =
                            "This condition contains deferred or runtime application code without durable semantic identity. " +
                            "The reader remains usable, but its bookmark cannot be serialized or resumed.";
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
                {
                    selection += "|Condition:RuntimeOnly";
                    unsupported =
                        "This condition contains runtime application code without durable semantic identity. " +
                        "The reader remains usable, but its bookmark cannot be serialized or resumed.";
                }
            }

            string orderingIdentity =
                $"Index:{index.Name.Length}:{index.Name}:{index.KeyPath.Length}:{index.KeyPath}:{(direction == RetrievalDirection.Descending ? 'D' : 'A')}|Identity:A";
            return CreateBookmarkContext(
                selection,
                sort: null,
                bookmark,
                consistency,
                unsupported,
                trackTopology: false,
                orderingIdentity,
                deterministicOrdering: true);
        }

        /// <summary>
        /// Creates and validates the store-, selection-, and ordering-bound state shared by one reader core.<br/>
        /// Result-shape validation is deferred until the typed or dynamic public reader is constructed.<br/>
        /// </summary>
        private BookmarkReadContext CreateBookmarkContext(
            string selectionIdentity,
            SortBy[]? sort,
            RecordBookmark? bookmark,
            BookmarkConsistency consistency,
            string? unsupportedReason,
            bool trackTopology = false,
            string? orderingIdentity = null,
            bool? deterministicOrdering = null)
        {
            if (consistency is not (BookmarkConsistency.GenerationBound or BookmarkConsistency.LiveContinuation))
                throw new ArgumentOutOfRangeException(nameof(consistency));
            if (bookmark is not null && unsupportedReason is not null)
            {
                throw new RecordBookmarkException(
                    RecordBookmarkFailureKind.UnsupportedQuery,
                    unsupportedReason);
            }

            string recordTypeIdentity = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name;
            string? topologyFingerprint = trackTopology ? BuildBookmarkIndexTopologyFingerprint() : null;
            string selectionFingerprint = FingerprintBookmarkText(
                topologyFingerprint is null
                    ? selectionIdentity
                    : selectionIdentity + "|IndexTopology:" + topologyFingerprint);
            string orderingFingerprint = orderingIdentity is null
                ? BuildOrderingFingerprint(sort)
                : FingerprintBookmarkText(orderingIdentity);
            bool hasDeterministicOrdering = deterministicOrdering ?? sort is { Length: > 0 };
            ulong sourceUsn = rbase.data.GetUSN(storeID);
            var context = new BookmarkReadContext(
                storeRuntime.InstanceID,
                storeID,
                recordTypeIdentity,
                selectionFingerprint,
                orderingFingerprint,
                sourceUsn,
                hasDeterministicOrdering,
                consistency,
                bookmark,
                unsupportedReason,
                topologyFingerprint,
                trackTopology ? BuildBookmarkIndexTopologyFingerprint : null);
            context.ValidateStoreSelectionAndOrdering();
            return context;
        }

        /// <summary>
        /// Captures an unpositioned flexible-row bookmark template from already-resolved selection and ordering metadata.<br/>
        /// Sorted readers use this after preparation so their detached identity cursor can retain the original semantic population without reopening or re-sorting that population.<br/>
        /// </summary>
        /// <param name="context">Selection and ordering context created for the sorted request.<br/></param>
        /// <returns>An opaque zero-consumed bookmark whose row shape and source generation are bound to the request.<br/></returns>
        private RecordBookmark CreateFlexibleRowBookmarkTemplate(BookmarkReadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.BindResultShape(FingerprintBookmarkText("Row:Flexible"));
            return context.Capture(0, lastIdentity: null, rbase.data.GetUSN(storeID));
        }

        /// <summary>
        /// Produces a stable result-shape fingerprint for one typed property tuple reader.<br/>
        /// </summary>
        private static string BuildTypedValuesBookmarkShape(Type resultType, bool withIds, IReadOnlyList<string> paths)
        {
            ArgumentNullException.ThrowIfNull(resultType);
            return BuildPropertyBookmarkShape(
                "Typed:" + (resultType.AssemblyQualifiedName ?? resultType.FullName ?? resultType.Name),
                withIds,
                paths);
        }

        /// <summary>
        /// Produces a stable result-shape fingerprint for one dynamic ordinal property reader.<br/>
        /// </summary>
        private static string BuildDynamicValuesBookmarkShape(bool withIds, IReadOnlyList<string> paths)
            => BuildPropertyBookmarkShape("Dynamic", withIds, paths);

        private static string BuildPropertyBookmarkShape(string family, bool withIds, IReadOnlyList<string> paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            var builder = new StringBuilder(family).Append(':').Append(withIds ? "WithId" : "ValueOnly");
            for (int i = 0; i < paths.Count; i++)
                builder.Append('|').Append(paths[i].Length).Append(':').Append(paths[i]);
            return FingerprintBookmarkText(builder.ToString());
        }

        private static string BuildOrderingFingerprint(SortBy[]? sort)
        {
            if (sort is not { Length: > 0 })
                return FingerprintBookmarkText("Natural");
            var builder = new StringBuilder("Explicit");
            for (int i = 0; i < sort.Length; i++)
            {
                builder.Append('|')
                    .Append(sort[i].Key.Path.Length)
                    .Append(':')
                    .Append(sort[i].Key.Path)
                    .Append(':')
                    .Append(sort[i].IgnoreCase ? 'I' : 'S')
                    .Append(':')
                    .Append(sort[i].Descending ? 'D' : 'A');
            }
            builder.Append("|Identity:A");
            return FingerprintBookmarkText(builder.ToString());
        }

        private static string FingerprintBookmarkText(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        /// <summary>
        /// Captures the active Abraxas index topology that can influence a natural-order condition plan.<br/>
        /// Mutable usage timestamps and physical backend vocabulary are excluded; only stable logical planning attributes participate.<br/>
        /// </summary>
        private string BuildBookmarkIndexTopologyFingerprint()
        {
            IndexInfo[] indexes = GetIndexes()
                .OrderBy(static index => index.LogicalPrefix, StringComparer.Ordinal)
                .ThenBy(static index => index.Backend.KeyPath, StringComparer.Ordinal)
                .ToArray();
            var builder = new StringBuilder();
            for (int i = 0; i < indexes.Length; i++)
            {
                IndexInfo index = indexes[i];
                builder.Append(index.LogicalPrefix.Length).Append(':').Append(index.LogicalPrefix)
                    .Append(':').Append(index.IsUnique ? 'U' : 'N')
                    .Append(':').Append((int)index.Backend.Status)
                    .Append(':').Append((int)index.Backend.Capabilities)
                    .Append(':').Append(index.Backend.KeyPath.Length).Append(':').Append(index.Backend.KeyPath)
                    .Append(':').Append(index.Backend.KeyType?.AssemblyQualifiedName ?? "null")
                    .Append('|');
            }
            return FingerprintBookmarkText(builder.ToString());
        }

        /// <summary>
        /// Applies ordinal or logical-anchor continuation without materializing the complete selected identity stream.<br/>
        /// </summary>
        private static IEnumerable<TItem> ResumeBookmarkStream<TItem>(
            IEnumerable<TItem> source,
            Func<TItem, ulong> identitySelector,
            BookmarkReadContext context)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(identitySelector);
            if (context.SourceBookmark is null || context.SourceBookmark.ResultsConsumed == 0)
                return source;

            return context.Consistency == BookmarkConsistency.LiveContinuation
                ? ContinueAfterAnchor(source, identitySelector, context.SourceBookmark)
                : SkipConsumed(source, context.SourceBookmark.ResultsConsumed);

            static IEnumerable<TItem> SkipConsumed(IEnumerable<TItem> values, long count)
            {
                using IEnumerator<TItem> enumerator = values.GetEnumerator();
                long skipped = 0;
                while (skipped < count && enumerator.MoveNext())
                    skipped++;
                while (enumerator.MoveNext())
                    yield return enumerator.Current;
            }

            static IEnumerable<TItem> ContinueAfterAnchor(
                IEnumerable<TItem> values,
                Func<TItem, ulong> getIdentity,
                RecordBookmark bookmark)
            {
                if (!bookmark.LastIdentity.HasValue)
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.AnchorUnavailable,
                        "A consumed live-continuation bookmark has no logical identity anchor.");

                using IEnumerator<TItem> enumerator = values.GetEnumerator();
                bool found = false;
                while (enumerator.MoveNext())
                {
                    if (getIdentity(enumerator.Current) == bookmark.LastIdentity.Value)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.AnchorUnavailable,
                        "The live-continuation anchor is no longer present in the current result stream.");
                }
                while (enumerator.MoveNext())
                    yield return enumerator.Current;
            }
        }

        /// <summary>
        /// Holds immutable selection metadata and the mutable result-shape binding for one public reader.<br/>
        /// </summary>
        internal sealed class BookmarkReadContext
        {
            private string? resultShapeFingerprint;

            internal BookmarkReadContext(
                Guid storeInstanceId,
                byte storeId,
                string recordTypeIdentity,
                string selectionFingerprint,
                string orderingFingerprint,
                ulong sourceUsn,
                bool deterministicOrdering,
                BookmarkConsistency consistency,
                RecordBookmark? sourceBookmark,
                string? unsupportedReason,
                string? topologyFingerprint,
                Func<string>? topologyProvider)
            {
                StoreInstanceId = storeInstanceId;
                StoreId = storeId;
                RecordTypeIdentity = recordTypeIdentity;
                SelectionFingerprint = selectionFingerprint;
                OrderingFingerprint = orderingFingerprint;
                SourceUsn = sourceUsn;
                DeterministicOrdering = deterministicOrdering;
                Consistency = consistency;
                SourceBookmark = sourceBookmark;
                UnsupportedReason = unsupportedReason;
                TopologyFingerprint = topologyFingerprint;
                TopologyProvider = topologyProvider;
            }

            internal Guid StoreInstanceId { get; }
            internal byte StoreId { get; }
            internal string RecordTypeIdentity { get; }
            internal string SelectionFingerprint { get; }
            internal string OrderingFingerprint { get; }
            internal ulong SourceUsn { get; }
            internal bool DeterministicOrdering { get; }
            internal BookmarkConsistency Consistency { get; }
            internal RecordBookmark? SourceBookmark { get; }
            internal string? UnsupportedReason { get; }
            internal string? TopologyFingerprint { get; }
            internal Func<string>? TopologyProvider { get; }
            internal long InitialResultsConsumed => SourceBookmark?.ResultsConsumed ?? 0;
            internal ulong? InitialLastIdentity => SourceBookmark?.LastIdentity;

            internal void ValidateStoreSelectionAndOrdering()
            {
                if (SourceBookmark is null)
                    return;
                if (SourceBookmark.StoreInstanceId != StoreInstanceId ||
                    SourceBookmark.StoreId != StoreId ||
                    !string.Equals(SourceBookmark.RecordTypeIdentity, RecordTypeIdentity, StringComparison.Ordinal))
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.StoreMismatch,
                        "The bookmark belongs to a different Abraxas store generation or record type.");
                }
                if (!string.Equals(SourceBookmark.OrderingFingerprint, OrderingFingerprint, StringComparison.Ordinal))
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.OrderingMismatch,
                        "The bookmark does not apply to the supplied ordering contract.");
                }
                if (!string.Equals(SourceBookmark.SelectionFingerprint, SelectionFingerprint, StringComparison.Ordinal))
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.SelectionMismatch,
                        "The bookmark does not apply to the supplied condition, State selection, or natural-plan index topology.");
                }
                if (Consistency == BookmarkConsistency.GenerationBound && SourceBookmark.SourceUsn != SourceUsn)
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.GenerationChanged,
                        "The authoritative Fractal store changed after this generation-bound bookmark was captured.");
                }
                if (Consistency == BookmarkConsistency.LiveContinuation && !DeterministicOrdering)
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.UnstableContinuation,
                        "Live continuation requires explicit deterministic ordering. Generation-bound continuation remains available for this natural-order reader.");
                }
            }

            internal void BindResultShape(string fingerprint)
            {
                if (resultShapeFingerprint is not null && !string.Equals(resultShapeFingerprint, fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("Reader result shape was bound more than once with different identities.");
                resultShapeFingerprint = fingerprint;
                if (SourceBookmark is not null &&
                    !string.Equals(SourceBookmark.ResultShapeFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.ResultShapeMismatch,
                        "The bookmark does not apply to the requested object projection or property shape.");
                }
            }

            internal RecordBookmark Capture(long resultsConsumed, ulong? lastIdentity, ulong currentSourceUsn)
            {
                if (UnsupportedReason is not null)
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.UnsupportedQuery,
                        UnsupportedReason);
                }
                if (resultShapeFingerprint is null)
                    throw new InvalidOperationException("Reader result shape has not been bound.");
                if (Consistency == BookmarkConsistency.GenerationBound && currentSourceUsn != SourceUsn)
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.GenerationChanged,
                        "The store changed while this generation-bound reader was active, so a stable bookmark cannot be captured.");
                }
                if (Consistency == BookmarkConsistency.GenerationBound &&
                    TopologyProvider is not null &&
                    !string.Equals(TopologyProvider(), TopologyFingerprint, StringComparison.Ordinal))
                {
                    throw new RecordBookmarkException(
                        RecordBookmarkFailureKind.GenerationChanged,
                        "The active Abraxas index topology changed while this natural-order reader was active, so a stable bookmark cannot be captured.");
                }

                return new RecordBookmark(
                    StoreInstanceId,
                    StoreId,
                    RecordTypeIdentity,
                    SelectionFingerprint,
                    resultShapeFingerprint,
                    OrderingFingerprint,
                    currentSourceUsn,
                    resultsConsumed,
                    lastIdentity,
                    DeterministicOrdering,
                    Consistency,
                    DateTimeOffset.UtcNow);
            }
        }
    }
}
