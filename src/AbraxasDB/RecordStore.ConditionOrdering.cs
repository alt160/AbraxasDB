using LibraDex;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Applies an explicit property order to one completed condition while preserving a direct same-index primitive whenever that primitive already supplies the requested order.<br/>
        /// The optimized route is selected only after LibraDex proves physical-handle identity between the condition leaf and ordering target, so logical-name similarity cannot accidentally reuse a folded, reversed, calculated, or other projection with different ordering semantics.<br/>
        /// Unsupported shapes retain the established arbitrary-candidate validation, deduplication, existence checks, complete-index intersection, and authoritative payload-sort fallback.<br/>
        /// </summary>
        /// <param name="where">The completed Abraxas condition selecting the result population.<br/></param>
        /// <param name="ignoreCase">Whether the requested property order applies ordinal case folding.<br/></param>
        /// <param name="sort">The non-empty property-order descriptors.<br/></param>
        /// <param name="operationName">Public operation name used by condition and ordering diagnostics.<br/></param>
        /// <returns>The selected identities in the requested order.<br/></returns>
        private IEnumerable<ulong> IterateConditionRecordIDsInRequestedOrder(
            Condition<T>.EndCondition where,
            bool ignoreCase,
            SortBy[] sort,
            string operationName)
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(sort);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
            if (sort.Length == 0)
                return IterateRecordIDsByLibraDexConditionOrThrow(where, operationName);

            if (TryIterateAlignedLibraDexConditionOrder(where, ignoreCase, sort, out IEnumerable<ulong>? direct))
            {
                OnOrderingExecutionPlanned(
                    operationName,
                    OrderingExecutionPlanKind.IndexDirectConditionOrder,
                    sort,
                    completeStorePopulation: false,
                    "The complete mutation-converged condition is one primitive over the exact index and direction requested for ordering, so its cursor supplies selection and order together.");
                return direct;
            }

            IEnumerable<ulong> candidates = IterateRecordIDsByLibraDexConditionOrThrow(where, operationName);
            OnOrderingExecutionPlanned(
                operationName,
                OrderingExecutionPlanKind.DefensiveCandidateOrdering,
                sort,
                completeStorePopulation: false,
                "The condition cursor does not prove the requested ordering over the same physical index, so arbitrary-candidate ordering remains defensive.");
            return OrderRecordIDsNatively(candidates, ignoreCase, sort);
        }

        /// <summary>
        /// Attempts to coalesce one direct condition primitive and one same-index sort into a single LibraDex cursor.<br/>
        /// Eligibility is deliberately narrow: one case-sensitive property order, a store-owned ordered index, one identity per stored key route, no deserialization alias, and physical equality between the translated condition leaf and ordering index.<br/>
        /// Ascending and descending requests both remain streaming because LibraDex receives the requested primitive direction before traversal begins.<br/>
        /// </summary>
        /// <param name="where">The completed condition to translate against the ordering index's owning catalog.<br/></param>
        /// <param name="ignoreCase">Whether the outer ordering request applies case folding.<br/></param>
        /// <param name="sort">Requested property-order descriptors.<br/></param>
        /// <param name="ids">The direct ordered identity stream when the proof succeeds.<br/></param>
        /// <returns><see langword="true"/> when one physical LibraDex primitive supplies both selection and order; otherwise <see langword="false"/>.<br/></returns>
        private bool TryIterateAlignedLibraDexConditionOrder(
            Condition<T>.EndCondition where,
            bool ignoreCase,
            SortBy[] sort,
            out IEnumerable<ulong> ids)
        {
            ids = default!;
            if (sort.Length != 1 || ignoreCase || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            SortBy descriptor = sort[0];
            string path = descriptor.Key.Path;
            if (string.IsNullOrWhiteSpace(path) ||
                descriptor.IgnoreCase ||
                NativeDeserializationOptions.Aliases.Contains(path) ||
                !TryGetOrCreateAutoLibraDexIndex(path, AutoIndexUse.Ordering, out RecordIndex recordIndex) ||
                !TryGetLibraDexIndexGroup(recordIndex, out CatalogIdentityGroupIndexes group, out IIndex targetIndex) ||
                !TryGetSingleIdentityLibraDexSimpleIndex(path, targetIndex, out LibraDexSimpleIndex simpleIndex))
            {
                return false;
            }

            if (!where.builder.TryBuildLibraDexConditionTree(
                    group,
                    indexName => CanUseLibraDexIndexForSemanticPath(indexName, simpleIndex.OwnerCatalog),
                    (Condition<T>.Builder builder, IBackendNeutralSelectorDescriptor selector, out string indexName) =>
                        TryResolveLibraDexDerivedSelector(builder, selector, simpleIndex.OwnerCatalog, out indexName),
                    (string logicalIndexName, out string physicalIndexName) =>
                        TryResolveReservedLibraDexIndexAlias(logicalIndexName, simpleIndex.OwnerCatalog, out physicalIndexName),
                    out LibraDexConditionEndCondition condition))
            {
                return false;
            }

            QueryDirection direction = descriptor.Descending
                ? QueryDirection.Descending
                : QueryDirection.Ascending;
            if (!group.TryOpenDirectConditionIdentityCursor(
                    targetIndex,
                    condition,
                    direction,
                    out LibraDexIdentityCursor<ulong>? cursor))
            {
                return false;
            }

            ids = IterateCursor(cursor!);
            return true;
        }

        /// <summary>
        /// Resolves the runtime simple-index descriptor that owns an exact physical ordering handle and proves one logical record identity can occur under at most one key.<br/>
        /// Abraxas string indexes are admitted because their maintained exact-key facade stores one source string per record identity even though the underlying catalog's general string metadata permits broader tuple uses.<br/>
        /// Other key families require LibraDex's explicit single-key-per-identity declaration.<br/>
        /// </summary>
        /// <param name="path">The requested Abraxas property path.<br/></param>
        /// <param name="targetIndex">The exact physical index resolved from the public ordering handle.<br/></param>
        /// <param name="simpleIndex">The matching maintained simple-index descriptor.<br/></param>
        /// <returns><see langword="true"/> when the exact index is safe for identity-only ordered condition streaming.<br/></returns>
        private bool TryGetSingleIdentityLibraDexSimpleIndex(
            string path,
            IIndex targetIndex,
            out LibraDexSimpleIndex simpleIndex)
        {
            LibraDexSimpleIndex[] registered = libraDexSimpleIndexes;
            for (int i = 0; i < registered.Length; i++)
            {
                LibraDexSimpleIndex candidate = registered[i];
                if (string.Equals(candidate.Path, path, StringComparison.Ordinal) &&
                    ReferenceEquals(candidate.Index, targetIndex) &&
                    (candidate.Capabilities & IndexBackendCapabilities.OrderedRead) != 0 &&
                    targetIndex.IdentityType == typeof(ulong) &&
                    (targetIndex is LibraDexStringScalar8Index ||
                     targetIndex.IdentityKeyMultiplicity == IdentityKeyMultiplicity.SingleKeyPerIdentity))
                {
                    simpleIndex = candidate;
                    return true;
                }
            }

            simpleIndex = default;
            return false;
        }
    }
}
