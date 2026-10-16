using Inheto;
using LibraDex;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private LibraDexCompositeIndex[] libraDexCompositeIndexes = Array.Empty<LibraDexCompositeIndex>();

        /// <summary>
        /// Defines the full-key uniqueness contract for one persistent composite index.<br/>
        /// Part result types, text representations, and directions remain attached to their existing <see cref="IndexSelectorBuilder"/> values so developers do not repeat selector metadata.<br/>
        /// </summary>
        public sealed class CompositeIndexOptions
        {
            /// <summary>
            /// Gets or initializes whether a complete composite key whose parts are all non-null must be unique across record identities.<br/>
            /// A key containing any null or missing part uses LibraDex's explicit null route and does not participate in Abraxas uniqueness rejection.<br/>
            /// </summary>
            public bool Unique { get; init; }
        }

        /// <summary>
        /// Creates a persistent ordered composite index from two or more direct or computed `.ForIndexing()` selectors.<br/>
        /// Abraxas persists the ordered selector intent, populates the native LibraDex routed-composite shape from authoritative Fractal/Inheto records, and maintains it during later store mutations.<br/>
        /// </summary>
        /// <param name="name">Stable developer-facing semantic name used for management and explicit index selection.<br/></param>
        /// <param name="parts">Two through sixteen ordered selector parts; their order defines composite prefix and ordering semantics.<br/></param>
        /// <returns><see langword="true"/> after the composite index is registered, validated, and populated.<br/></returns>
        public bool CreateCompositeIndex(string name, params IndexSelectorBuilder[] parts)
            => CreateCompositeIndex(name, new CompositeIndexOptions(), parts);

        /// <summary>
        /// Creates a persistent ordered composite index from two or more direct or computed `.ForIndexing()` selectors with an explicit full-key contract.<br/>
        /// The physical LibraDex index remains an implementation detail; subsequent Abraxas writes, deletes, convergence repair, reopen, and removal maintain the same selector tuple automatically.<br/>
        /// </summary>
        /// <param name="name">Stable developer-facing semantic name used for management and explicit index selection.<br/></param>
        /// <param name="options">Full-composite uniqueness options.<br/></param>
        /// <param name="parts">Two through sixteen ordered selector parts; their order defines composite prefix and ordering semantics.<br/></param>
        /// <returns><see langword="true"/> after the composite index is registered, validated, and populated.<br/></returns>
        public bool CreateCompositeIndex(
            string name,
            CompositeIndexOptions options,
            params IndexSelectorBuilder[] parts)
            => CreateCompositeIndexCore(name, options, ownerCatalog: null, parts);

        /// <summary>
        /// Creates a memory-only ordered composite index from two through sixteen typed selector parts.<br/>
        /// The complete tuple is populated from authoritative records and maintained by later store mutations, while its physical keys and definition disappear when this store instance closes.<br/>
        /// </summary>
        /// <param name="name">Stable developer-facing name for discovery, explicit selection, and typed composite-value readers during this store instance.<br/></param>
        /// <param name="parts">Two through sixteen ordered selectors whose types and directions define the composite route.<br/></param>
        /// <returns><see langword="true"/> once the volatile composite is registered and populated.<br/></returns>
        public bool CreateMemoryCompositeIndex(string name, params IndexSelectorBuilder[] parts)
            => CreateMemoryCompositeIndex(name, new CompositeIndexOptions(), parts);

        /// <summary>
        /// Creates the memory-only counterpart of a file-backed composite index with the same full-key uniqueness semantics.<br/>
        /// No selector intent or tuple pages are written to the persistent catalog; callers recreate this index after reopening the store.<br/>
        /// </summary>
        /// <param name="name">Stable developer-facing name for discovery, explicit selection, and typed composite-value readers during this store instance.<br/></param>
        /// <param name="options">Complete non-null tuple uniqueness contract.<br/></param>
        /// <param name="parts">Two through sixteen ordered selectors whose types and directions define the composite route.<br/></param>
        /// <returns><see langword="true"/> once the volatile composite is registered and populated.<br/></returns>
        public bool CreateMemoryCompositeIndex(string name, CompositeIndexOptions options, params IndexSelectorBuilder[] parts)
            => CreateCompositeIndexCore(name, options, libraDexVolatileCatalog, parts);

        /// <summary>
        /// Validates one composite selector contract and routes registration to its requested file or memory catalog.<br/>
        /// </summary>
        /// <param name="name">Developer-facing logical index name.<br/></param>
        /// <param name="options">Full-key uniqueness contract.<br/></param>
        /// <param name="ownerCatalog">Volatile owner, or <see langword="null"/> for the persistent catalog.<br/></param>
        /// <param name="parts">Ordered selector parts to compile and register.<br/></param>
        /// <returns><see langword="true"/> after the physical index is ready.<br/></returns>
        private bool CreateCompositeIndexCore(string name, CompositeIndexOptions options, Catalog? ownerCatalog, IndexSelectorBuilder[] parts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(parts);
            if (parts.Length is < 2 or > 16)
                throw new ArgumentOutOfRangeException(nameof(parts), parts.Length, "Composite indexes require between two and sixteen ordered selector parts.");
            if (parts.Any(static part => part is null))
                throw new ArgumentException("Composite index selector parts cannot contain null entries.", nameof(parts));

            var intents = new OrdinaryCompositeIndexPartIntent[parts.Length];
            var descriptors = new SelectorDescriptor<T>[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                IndexSelectorBuilder part = parts[i];
                SelectorDescriptor<T> descriptor = SelectorDescriptor<T>.FromSelector(part.CalculatedSelector);
                Type resultType = NormalizeValueType(descriptor.ResultType);
                if (part.DeclaredValueType is not null && NormalizeValueType(part.DeclaredValueType) != resultType)
                    throw new InvalidOperationException($"Composite selector part {i + 1} declares '{part.DeclaredValueType.FullName}' but its durable result is '{resultType.FullName}'.");
                if (!IsSupportedCompositePartType(resultType))
                    throw new NotSupportedException($"Composite selector part {i + 1} produces unsupported ordered type '{resultType.FullName}'.");
                if (part.IsTextResult != (resultType == typeof(string)))
                    throw new InvalidOperationException($"Composite selector part {i + 1} has text metadata that contradicts its durable result type.");

                TextIndexProfile profile = resultType == typeof(string)
                    ? ToTextIndexProfile(part.CaseType, ignoreCase: false)
                    : TextIndexProfile.Exact;
                IndexDirection direction = part.Sort == IndexSortDirection.Descending
                    ? IndexDirection.Descending
                    : IndexDirection.Ascending;
                byte[] serialized = descriptor.Serialize();
                descriptors[i] = descriptor;
                intents[i] = new OrdinaryCompositeIndexPartIntent
                {
                    SelectorDescriptor = serialized,
                    ResultTypeCode = ConditionWireTypes.GetCode(resultType),
                    TextProfile = profile,
                    Direction = direction
                };
            }

            string physicalName = BuildCompositePhysicalName(name, intents);
            return RegisterCompositeIndex(
                name,
                physicalName,
                options.Unique,
                intents,
                descriptors,
                populateExisting: true,
                manageIntent: true,
                ownerCatalog: ownerCatalog);
        }

        /// <summary>
        /// Registers or restores one file- or memory-backed native LibraDex composite shape and its compiled Inheto selector readers.<br/>
        /// Persistent registration publishes preparing intent before physical creation and ready intent only after authoritative population succeeds; volatile registration publishes no durable intent.<br/>
        /// </summary>
        private bool RegisterCompositeIndex(
            string logicalName,
            string physicalName,
            bool unique,
            OrdinaryCompositeIndexPartIntent[] partIntents,
            SelectorDescriptor<T>[] descriptors,
            bool populateExisting,
            bool manageIntent,
            Catalog? ownerCatalog = null)
        {
            CatalogIdentityGroupIndexes indexes = ownerCatalog is null
                ? libraDexIdentityIndexes ?? throw new InvalidOperationException("A persistent LibraDex identity group is not bound to this store.")
                : ownerCatalog.Indexes.IndexSet(indexGroupName);
            if (partIntents.Length != descriptors.Length)
                throw new ArgumentException("Composite selector intent and descriptor counts must match.", nameof(descriptors));

            var readers = new Func<InhetoBinary, (bool HasValue, object? Value)>[descriptors.Length];
            var specs = new LibraDexCompositeKeyPartSpec[descriptors.Length];
            for (var i = 0; i < descriptors.Length; i++)
            {
                if (!TryCreateStoreSelectorReader(descriptors[i], out readers[i]))
                    throw new NotSupportedException($"Composite selector part {i + 1} cannot be evaluated from authoritative Inheto bytes.");
                Type resultType = ConditionWireTypes.Resolve(partIntents[i].ResultTypeCode);
                specs[i] = CreateCompositePartSpec("part" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), resultType, partIntents[i].TextProfile, partIntents[i].Direction);
            }

            ThrowIfLibraDexLogicalNameBackingConflicts(logicalName, ownerCatalog);

            using var indexMutation = EnterPartialIndexMutationScope();
            OrdinaryIndexIntentEntry? intent = ownerCatalog is null && manageIntent
                ? PrepareCompositeIndexIntent(logicalName, physicalName, unique, partIntents)
                : null;
            bool existed = indexes.HasIndex(physicalName);
            IIndex raw = indexes.Define(physicalName)
                .Composite<ulong>(specs)
                .CreateOrOpen(IndexKeys.NonUnique, new IndexOptions
                {
                    Keys = IndexKeys.NonUnique,
                    IdentityKeyMultiplicity = IdentityKeyMultiplicity.SingleKeyPerIdentity
                });
            if (raw is not LibraDexRoutedCompositeIndex routed)
                throw new InvalidOperationException($"LibraDex index '{physicalName}' did not open as a routed composite index.");

            var entry = new LibraDexCompositeIndex(logicalName, physicalName, unique, routed, specs, descriptors, readers, ownerCatalog);
            lock (libraDexIndexLock)
            {
                LibraDexCompositeIndex[] current = libraDexCompositeIndexes;
                int existing = Array.FindIndex(current, candidate => string.Equals(candidate.LogicalName, logicalName, StringComparison.Ordinal));
                if (existing >= 0)
                {
                    if (!current[existing].ContractEquals(entry))
                        throw new IndexContractException(IndexContractFailureKind.Configuration, $"Composite index '{logicalName}' conflicts with its active runtime definition.", physicalName, logicalName);
                    return true;
                }

                var next = new LibraDexCompositeIndex[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = entry;
                libraDexCompositeIndexes = next;
            }

            try
            {
                if (populateExisting || !existed)
                    PopulateLibraDexCompositeIndex(entry);
                CompleteOrdinaryIndexRegistration(ownerCatalog, intent);
                return true;
            }
            catch
            {
                lock (libraDexIndexLock)
                    libraDexCompositeIndexes = libraDexCompositeIndexes.Where(candidate => !ReferenceEquals(candidate, entry)).ToArray();
                throw;
            }
        }

        /// <summary>
        /// Creates or verifies the preparing durable definition for one composite index.<br/>
        /// </summary>
        private OrdinaryIndexIntentEntry PrepareCompositeIndexIntent(
            string logicalName,
            string physicalName,
            bool unique,
            OrdinaryCompositeIndexPartIntent[] parts)
        {
            var expected = new OrdinaryIndexIntentEntry
            {
                StoreID = storeID,
                Kind = OrdinaryIndexIntentKind.Composite,
                LogicalName = logicalName,
                PhysicalName = physicalName,
                Unique = unique,
                TextProfile = TextIndexProfile.Exact,
                Direction = IndexDirection.Ascending,
                CompositeParts = parts.Select(static part => part.Clone()).ToArray(),
                Ready = false
            };
            expected.Validate();
            if (rbase.TryGetOrdinaryIndexIntent(storeID, logicalName, out OrdinaryIndexIntentEntry existing))
            {
                if (!existing.ContractEquals(expected, includeReady: false))
                    throw new IndexContractException(IndexContractFailureKind.Configuration, $"Composite index '{logicalName}' conflicts with its persisted Abraxas definition.", physicalName, logicalName);
                return existing;
            }

            rbase.UpsertOrdinaryIndexIntent(expected);
            return expected;
        }

        /// <summary>
        /// Restores one persisted composite intent by rebuilding exact selector descriptors and reopening its native LibraDex routed shape.<br/>
        /// </summary>
        private bool RestoreCompositeIndexIntent(OrdinaryIndexIntentEntry intent)
        {
            var descriptors = new SelectorDescriptor<T>[intent.CompositeParts.Length];
            for (var i = 0; i < descriptors.Length; i++)
                descriptors[i] = SelectorDescriptor<T>.Deserialize(intent.CompositeParts[i].SelectorDescriptor);
            return RegisterCompositeIndex(intent.LogicalName, intent.PhysicalName, intent.Unique, intent.CompositeParts, descriptors, populateExisting: false, manageIntent: false);
        }

        /// <summary>
        /// Prepares and validates every authoritative tuple before mutating one composite index's physical contents.<br/>
        /// </summary>
        private List<(ulong RecordID, LibraDexCompositeKey Key)> PrepareLibraDexCompositeIndexPopulation(LibraDexCompositeIndex entry)
        {
            var prepared = new List<(ulong RecordID, LibraDexCompositeKey Key)>();
            var uniqueKeys = entry.Unique ? new HashSet<CompositeRuntimeKey>(CompositeRuntimeKeyComparer.Instance) : null;
            foreach (ulong recordID in IterateRecordIDs())
            {
                InhetoBinary binary = GetInhetoBinary(recordID, nameof(PopulateLibraDexCompositeIndex));
                CompositeRuntimeKey runtime = ReadCompositeRuntimeKey(entry, binary);
                if (uniqueKeys is not null && !runtime.ContainsNull && !uniqueKeys.Add(runtime))
                    throw new IndexContractException(IndexContractFailureKind.UniqueConstraint, $"Unique composite index '{entry.LogicalName}' found the same complete non-null key on more than one existing record.", entry.PhysicalName, entry.LogicalName, recordID);
                LibraDexCompositeKey key = runtime.ToLibraDexKey();
                if (entry.Unique && !runtime.ContainsNull)
                    ValidateLibraDexCompositeUniqueKey(entry, key, recordID);
                prepared.Add((recordID, key));
            }
            return prepared;
        }

        /// <summary>
        /// Populates one composite index only after its complete authoritative key set has passed extraction and uniqueness validation.<br/>
        /// </summary>
        private void PopulateLibraDexCompositeIndex(LibraDexCompositeIndex entry)
        {
            List<(ulong RecordID, LibraDexCompositeKey Key)> prepared = PrepareLibraDexCompositeIndexPopulation(entry);
            ApplyLibraDexCompositeIndexPopulation(entry, prepared);
        }

        /// <summary>
        /// Inserts one previously validated authoritative composite population.<br/>
        /// </summary>
        private static void ApplyLibraDexCompositeIndexPopulation(LibraDexCompositeIndex entry, List<(ulong RecordID, LibraDexCompositeKey Key)> prepared)
        {
            for (var i = 0; i < prepared.Count; i++)
                EnsureLibraDexCompositeInsertAccepted(entry, prepared[i].Key, prepared[i].RecordID, entry.Index.Insert(prepared[i].Key, prepared[i].RecordID));
        }

        /// <summary>
        /// Validates every unique composite key affected by one pending record write before Fractal mutation.<br/>
        /// </summary>
        private void ValidateLibraDexCompositeIndexUpsert(ulong recordID, InhetoBinary binary)
        {
            LibraDexCompositeIndex[] indexes = libraDexCompositeIndexes;
            for (var i = 0; i < indexes.Length; i++)
            {
                LibraDexCompositeIndex entry = indexes[i];
                if (!entry.Unique)
                    continue;
                CompositeRuntimeKey runtime = ReadCompositeRuntimeKey(entry, binary);
                if (!runtime.ContainsNull)
                    ValidateLibraDexCompositeUniqueKey(entry, runtime.ToLibraDexKey(), recordID);
            }
        }

        /// <summary>
        /// Inserts one record into every maintained composite index using the already serialized authoritative payload.<br/>
        /// </summary>
        private void TryApplyLibraDexCompositeUpsert(ulong recordID, InhetoBinary binary)
        {
            LibraDexCompositeIndex[] indexes = libraDexCompositeIndexes;
            for (var i = 0; i < indexes.Length; i++)
            {
                LibraDexCompositeIndex entry = indexes[i];
                LibraDexCompositeKey key = ReadCompositeRuntimeKey(entry, binary).ToLibraDexKey();
                EnsureLibraDexCompositeInsertAccepted(entry, key, recordID, entry.Index.Insert(key, recordID));
            }
        }

        /// <summary>
        /// Deletes one record's prior exact tuple from every maintained composite index.<br/>
        /// </summary>
        private void TryApplyLibraDexCompositeDelete(ulong recordID, InhetoBinary binary)
        {
            LibraDexCompositeIndex[] indexes = libraDexCompositeIndexes;
            for (var i = 0; i < indexes.Length; i++)
                indexes[i].Index.Delete(ReadCompositeRuntimeKey(indexes[i], binary).ToLibraDexKey(), recordID);
        }

        /// <summary>
        /// Reads all selector-part values once from one Inheto payload and preserves missing values as explicit null-route parts.<br/>
        /// </summary>
        private static CompositeRuntimeKey ReadCompositeRuntimeKey(LibraDexCompositeIndex entry, InhetoBinary binary)
        {
            var values = new object?[entry.Readers.Length];
            bool containsNull = false;
            for (var i = 0; i < entry.Readers.Length; i++)
            {
                var result = entry.Readers[i](binary);
                object? value = result.HasValue ? result.Value : null;
                values[i] = value;
                containsNull |= value is null;
            }
            return new CompositeRuntimeKey(values, containsNull);
        }

        /// <summary>
        /// Enforces Abraxas's complete-non-null uniqueness policy through LibraDex's exact routed-composite lookup.<br/>
        /// </summary>
        private static void ValidateLibraDexCompositeUniqueKey(LibraDexCompositeIndex entry, LibraDexCompositeKey key, ulong recordID)
        {
            IReadOnlyList<byte[]> matches = entry.Index.Identities.GetByKey(key);
            for (var i = 0; i < matches.Count; i++)
            {
                ulong existing = LibraDexCanonical.Numeric.Read<ulong>(matches[i]);
                if (existing != recordID)
                    throw new IndexContractException(IndexContractFailureKind.UniqueConstraint, $"Unique composite index '{entry.LogicalName}' already contains the supplied complete non-null key for another record.", entry.PhysicalName, entry.LogicalName, recordID);
            }
        }

        /// <summary>
        /// Converts a rejected composite insertion into a stable Abraxas failure unless the exact tuple already exists idempotently.<br/>
        /// </summary>
        private static void EnsureLibraDexCompositeInsertAccepted(LibraDexCompositeIndex entry, LibraDexCompositeKey key, ulong recordID, LibraDexGenericInsertResult result)
        {
            if (result.Inserted)
                return;
            IReadOnlyList<byte[]> matches = entry.Index.Identities.GetByKey(key);
            for (var i = 0; i < matches.Count; i++)
            {
                if (LibraDexCanonical.Numeric.Read<ulong>(matches[i]) == recordID)
                    return;
            }
            throw new IndexContractException(IndexContractFailureKind.BackendMutation, $"Composite index '{entry.LogicalName}' rejected the validated record tuple.", entry.PhysicalName, entry.LogicalName, recordID);
        }

        /// <summary>
        /// Opens a routed-composite candidate cursor for a flat Abraxas conjunction whose static leaves can be expressed as named LibraDex key-part predicates.<br/>
        /// The cursor deliberately remains a candidate source: the ordinary RecordStore pipeline retains authoritative Fractal/Inheto evaluation for every leaf until a later exactness proof promotes a fully covered composite tree to direct execution.<br/>
        /// This is distinct from the legacy full-key lookup because prefix, range, membership, and mixed component predicates retain LibraDex's routed traversal instead of requiring a synthetic concatenated key or copied identity array.<br/>
        /// </summary>
        /// <param name="where">The completed public Abraxas condition being planned.<br/></param>
        /// <param name="cursor">The native LibraDex identity cursor when a compatible routed-composite candidate plan is available.<br/></param>
        /// <param name="condition">The materialized LibraDex composite condition used by <paramref name="cursor"/>.<br/></param>
        /// <param name="branchCount">The number of routed disjunct branches represented by the opened candidate condition.<br/></param>
        /// <returns><see langword="true"/> when a compatible composite candidate cursor was opened; otherwise <see langword="false"/>.<br/></returns>
        private bool TryOpenLibraDexCompositeCandidateCursor(
            Condition<T>.EndCondition where,
            out LibraDexIdentityCursor<ulong>? cursor,
            out LibraDexConditionEndCondition? condition,
            out int branchCount)
        {
            cursor = null;
            condition = null;
            branchCount = 0;
            if (where is null || libraDexCompositeIndexes.Length == 0)
            {
                return false;
            }

            EnsureLibraDexConvergence(nameof(TryOpenLibraDexCompositeCandidateCursor));
            Catalog? selectedOwner = null;
            LibraDexConditionEndCondition? planned = null;
            if (libraDexBackendMode != LibraDexIndexBackendMode.FractalOnly && libraDexIdentityIndexes is not null)
                _ = TryBuildLibraDexCompositeCandidateCondition(where, ownerCatalog: null, out planned, out branchCount);
            if (planned is null)
            {
                selectedOwner = libraDexVolatileCatalog;
                if (!TryBuildLibraDexCompositeCandidateCondition(where, selectedOwner, out planned, out branchCount))
                    return false;
            }

            CatalogIdentityGroupIndexes group = selectedOwner is null
                ? libraDexIdentityIndexes!
                : selectedOwner.Indexes.IndexSet(indexGroupName);
            cursor = group.AbraxasIdentityQuery<ulong>().OpenCursor(
                planned!,
                deduplication: IdentityDeduplication.Distinct);
            condition = planned;
            return true;
        }

        /// <summary>
        /// Compiles the compatible portion of one public condition tree into one or more LibraDex composite-match branches.<br/>
        /// Deferred operands are materialized through the original builder before capture, and AND-before-OR reduction preserves the same branch semantics as the authoritative evaluator.<br/>
        /// Every OR branch must produce a safe routed candidate source; unsupported branches fail closed so a union can never omit a record that could satisfy the full condition.<br/>
        /// </summary>
        /// <param name="where">The completed public condition whose semantic wire graph is inspected.<br/></param>
        /// <param name="condition">The completed native composite-match condition or grouped branch union when compilation succeeds.<br/></param>
        /// <param name="branchCount">The number of routed disjunct branches represented by <paramref name="condition"/>.<br/></param>
        /// <param name="ownerCatalog">The volatile catalog whose composite indexes may participate, or null for persistent store-owned indexes.<br/></param>
        /// <returns><see langword="true"/> when every logical disjunct has at least one routed composite candidate source.<br/></returns>
        private bool TryBuildLibraDexCompositeCandidateCondition(
            Condition<T>.EndCondition where,
            Catalog? ownerCatalog,
            out LibraDexConditionEndCondition? condition,
            out int branchCount)
        {
            condition = null;
            branchCount = 0;
            ConditionWireSequence sequence;
            try
            {
                sequence = ConditionDescriptor<T>.CaptureMaterializedSemanticSequence(where);
            }
            catch (NotSupportedException)
            {
                return false;
            }

            if (!TryExpandCompositeCandidateBranches(sequence, out List<ConditionWireLeaf[]> branches))
                return false;

            var branchConditions = new LibraDexConditionEndCondition[branches.Count];
            for (var branchOrdinal = 0; branchOrdinal < branches.Count; branchOrdinal++)
            {
                if (!TryBuildLibraDexCompositeCandidateCondition(
                        branches[branchOrdinal],
                        ownerCatalog,
                        out _,
                        out LibraDexConditionEndCondition? branchCondition))
                {
                    return false;
                }

                branchConditions[branchOrdinal] = branchCondition!;
            }

            if (!TryComposeLibraDexCompositeCandidateBranches(branchConditions, out LibraDexConditionEndCondition? planned))
                return false;

            condition = planned;
            branchCount = branchConditions.Length;
            return true;
        }

        /// <summary>
        /// Compiles one conjunction of already materialized public leaves into its strongest available routed composite predicate.<br/>
        /// A criterion is selected by the persisted selector fingerprint rather than a property-path spelling, which keeps calculated and nested selector parts structurally safe and preserves the composite index's declared component order.<br/>
        /// Unsupported leaves and later repeated-component predicates remain authoritative residual semantics; the first compatible predicate per component still supplies a safe candidate reduction without requiring lossy predicate merging.<br/>
        /// </summary>
        /// <param name="leaves">One AND-only materialized conjunction from the public condition tree.<br/></param>
        /// <param name="selected">The best compatible maintained composite index when compilation succeeds.<br/></param>
        /// <param name="condition">The completed native composite-match condition when compilation succeeds.<br/></param>
        /// <param name="ownerCatalog">The volatile catalog whose composite indexes may participate, or null for persistent store-owned indexes.<br/></param>
        /// <returns><see langword="true"/> when at least one routed composite component can safely reduce this conjunction's candidate population.<br/></returns>
        private bool TryBuildLibraDexCompositeCandidateCondition(
            ConditionWireLeaf[] leaves,
            Catalog? ownerCatalog,
            out LibraDexCompositeIndex? selected,
            out LibraDexConditionEndCondition? condition)
        {
            selected = null;
            condition = null;
            if (leaves.Length == 0)
                return false;

            LibraDexCompositePartCriterion[]? selectedCriteria = null;
            var selectedScore = -1;
            LibraDexCompositeIndex[] indexes = libraDexCompositeIndexes;
            for (var indexOrdinal = 0; indexOrdinal < indexes.Length; indexOrdinal++)
            {
                LibraDexCompositeIndex candidate = indexes[indexOrdinal];
                if (!ReferenceEquals(candidate.OwnerCatalog, ownerCatalog))
                    continue;
                var criteria = new List<LibraDexCompositePartCriterion>(candidate.Specs.Length);
                var usedParts = new bool[candidate.Specs.Length];
                for (var leafOrdinal = 0; leafOrdinal < leaves.Length; leafOrdinal++)
                {
                    if (!TryCreateCompositeCriterion(candidate, leaves[leafOrdinal], out int[] partOrdinals, out LibraDexCompositePartCriterion? criterion))
                        continue;

                    if (partOrdinals.Any(partOrdinal => usedParts[partOrdinal]))
                        continue;

                    for (var partOrdinalIndex = 0; partOrdinalIndex < partOrdinals.Length; partOrdinalIndex++)
                        usedParts[partOrdinals[partOrdinalIndex]] = true;
                    criteria.Add(criterion!);
                }

                if (criteria.Count == 0 || criteria.Count <= selectedScore)
                    continue;

                selected = candidate;
                selectedCriteria = criteria.ToArray();
                selectedScore = selectedCriteria.Length;
            }

            if (selected is null || selectedCriteria is null)
                return false;

            condition = selected.RoutedIndex.Where.Where(selectedCriteria).EndCondition;
            return true;
        }

        /// <summary>
        /// Reduces a grouped public condition to bounded AND conjunctions using the same AND-before-OR precedence as its record evaluator.<br/>
        /// Each output conjunction represents one logical disjunct and must independently yield a composite candidate before the caller may union the branches.<br/>
        /// </summary>
        /// <param name="sequence">The execution-materialized public semantic sequence.<br/></param>
        /// <param name="branches">The bounded disjunctive-normal branch list when reduction succeeds.<br/></param>
        /// <returns><see langword="true"/> when the sequence is structurally valid and remains within the explicit branch budget.<br/></returns>
        private static bool TryExpandCompositeCandidateBranches(
            ConditionWireSequence sequence,
            out List<ConditionWireLeaf[]> branches)
        {
            const int maximumBranchCount = 32;
            branches = new List<ConditionWireLeaf[]>();
            if (sequence?.Items is not { Length: > 0 } items)
                return false;

            var operands = new List<List<ConditionWireLeaf[]>>();
            var connectors = new List<bool>();
            bool? pendingUseOr = null;
            for (var itemOrdinal = 0; itemOrdinal < items.Length; itemOrdinal++)
            {
                ConditionWireItem item = items[itemOrdinal];
                if (item.Kind is "And" or "Or")
                {
                    if (operands.Count == 0 || pendingUseOr.HasValue)
                        return false;
                    pendingUseOr = item.Kind == "Or";
                    continue;
                }

                List<ConditionWireLeaf[]> next;
                if (item.Kind == "Leaf" && item.Leaf is not null)
                {
                    next = new List<ConditionWireLeaf[]>(1) { new[] { item.Leaf } };
                }
                else if (item.Kind == "Group" && item.Group is not null &&
                    TryExpandCompositeCandidateBranches(item.Group, out next))
                {
                }
                else
                {
                    return false;
                }

                if (operands.Count > 0)
                {
                    if (!pendingUseOr.HasValue)
                        return false;
                    connectors.Add(pendingUseOr.Value);
                }

                operands.Add(next);
                pendingUseOr = null;
            }

            if (operands.Count == 0 || pendingUseOr.HasValue || connectors.Count != operands.Count - 1)
                return false;

            for (var connectorOrdinal = 0; connectorOrdinal < connectors.Count;)
            {
                if (connectors[connectorOrdinal])
                {
                    connectorOrdinal++;
                    continue;
                }

                if (!TryCombineCompositeCandidateConjunctions(
                        operands[connectorOrdinal],
                        operands[connectorOrdinal + 1],
                        maximumBranchCount,
                        out List<ConditionWireLeaf[]> combined))
                {
                    return false;
                }

                operands[connectorOrdinal] = combined;
                operands.RemoveAt(connectorOrdinal + 1);
                connectors.RemoveAt(connectorOrdinal);
            }

            var result = new List<ConditionWireLeaf[]>();
            for (var operandOrdinal = 0; operandOrdinal < operands.Count; operandOrdinal++)
            {
                if (result.Count > maximumBranchCount - operands[operandOrdinal].Count)
                    return false;
                result.AddRange(operands[operandOrdinal]);
            }

            if (result.Count == 0)
                return false;

            branches = result;
            return true;
        }

        /// <summary>
        /// Produces the conjunction cross-product required when two grouped branch sets are joined by AND.<br/>
        /// A finite budget prevents an adversarial condition shape from converting planning into unbounded disjunctive-normal expansion; exceeding it simply preserves the established fallback path.<br/>
        /// </summary>
        /// <param name="left">The left branch set.<br/></param>
        /// <param name="right">The right branch set.<br/></param>
        /// <param name="maximumBranchCount">The maximum permitted product size.<br/></param>
        /// <param name="combined">The cross-product conjunctions when bounded.<br/></param>
        /// <returns><see langword="true"/> when the product is within the budget.<br/></returns>
        private static bool TryCombineCompositeCandidateConjunctions(
            List<ConditionWireLeaf[]> left,
            List<ConditionWireLeaf[]> right,
            int maximumBranchCount,
            out List<ConditionWireLeaf[]> combined)
        {
            combined = new List<ConditionWireLeaf[]>();
            if (left.Count == 0 || right.Count == 0 || left.Count > maximumBranchCount / right.Count)
                return false;

            combined.Capacity = left.Count * right.Count;
            for (var leftOrdinal = 0; leftOrdinal < left.Count; leftOrdinal++)
            {
                for (var rightOrdinal = 0; rightOrdinal < right.Count; rightOrdinal++)
                {
                    var conjunction = new ConditionWireLeaf[left[leftOrdinal].Length + right[rightOrdinal].Length];
                    Array.Copy(left[leftOrdinal], conjunction, left[leftOrdinal].Length);
                    Array.Copy(right[rightOrdinal], 0, conjunction, left[leftOrdinal].Length, right[rightOrdinal].Length);
                    combined.Add(conjunction);
                }
            }

            return true;
        }

        /// <summary>
        /// Joins independently safe routed composite branches into one native LibraDex OR condition.<br/>
        /// All maintained indexes belong to the same RecordStore identity group, and LibraDex remains responsible for distinct identity traversal at the terminal cursor.<br/>
        /// </summary>
        /// <param name="branches">The completed per-disjunct routed conditions.<br/></param>
        /// <param name="condition">The native union condition when composition succeeds.<br/></param>
        /// <returns><see langword="true"/> when one or more same-group branches were composed.<br/></returns>
        private static bool TryComposeLibraDexCompositeCandidateBranches(
            LibraDexConditionEndCondition[] branches,
            out LibraDexConditionEndCondition? condition)
        {
            condition = null;
            if (branches.Length == 0)
                return false;

            LibraDexConditionContinueOrEnd continuation = LibraDexCondition
                .ForGroup(branches[0].Group)
                .Group(branches[0]);
            for (var branchOrdinal = 1; branchOrdinal < branches.Length; branchOrdinal++)
            {
                if (!string.Equals(branches[0].Group, branches[branchOrdinal].Group, StringComparison.Ordinal))
                    return false;
                continuation = continuation.OR.Group(branches[branchOrdinal]);
            }

            condition = continuation.EndCondition;
            return true;
        }

        /// <summary>
        /// Attempts to translate one static public condition leaf into the matching named part of one maintained composite index.<br/>
        /// The method declines when the selector is not one declared part, when the value type differs, or when the operator cannot preserve Abraxas semantics through LibraDex's corresponding part predicate.<br/>
        /// </summary>
        /// <param name="index">The candidate composite index and its persisted ordered part contracts.<br/></param>
        /// <param name="leaf">The public condition leaf to inspect.<br/></param>
        /// <param name="partOrdinals">The composite component ordinals consumed by the translated criterion when translation succeeds.<br/></param>
        /// <param name="criterion">The native named-part criterion when translation succeeds.<br/></param>
        /// <returns><see langword="true"/> when the leaf is one compatible composite component predicate.<br/></returns>
        private static bool TryCreateCompositeCriterion(
            LibraDexCompositeIndex index,
            ConditionWireLeaf leaf,
            out int[] partOrdinals,
            out LibraDexCompositePartCriterion? criterion)
        {
            partOrdinals = Array.Empty<int>();
            criterion = null;
            if (leaf.Family == "PropertyComparison")
                return TryCreateCompositePropertyComparisonCriterion(index, leaf, out partOrdinals, out criterion);

            if (!TryReadStaticCompositeOperands(leaf, out object?[] values))
            {
                return false;
            }

            for (var indexOrdinal = 0; indexOrdinal < index.Descriptors.Length; indexOrdinal++)
            {
                LibraDexCompositeKeyPartSpec part = index.Specs[indexOrdinal];
                Type valueType = GetCompositeSelectorValueType(leaf, part);
                if (!TryReadCompositeSelectorFingerprint(leaf, valueType, out string fingerprint) ||
                    !string.Equals(index.Descriptors[indexOrdinal].SemanticFingerprint, fingerprint, StringComparison.Ordinal) ||
                    part.KeyType != valueType)
                {
                    continue;
                }

                LibraDexCompositePartCriterion? translated = TryCreateCompositeCriterion(
                    part.Name,
                    part,
                    leaf,
                    values);
                if (translated is null)
                    return false;

                partOrdinals = new[] { indexOrdinal };
                criterion = translated;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves one leaf selector to the same backend-neutral fingerprint persisted by composite index registration.<br/>
        /// </summary>
        private static bool TryReadCompositeSelectorFingerprint(
            ConditionWireLeaf leaf,
            Type valueType,
            out string fingerprint)
            => TryReadCompositeSelectorFingerprint(leaf.Selector, valueType, out fingerprint);

        /// <summary>
        /// Resolves one durable selector node to the same backend-neutral fingerprint persisted by composite index registration.<br/>
        /// Property-to-property comparisons use this overload twice so both sides must be maintained key parts of the same selected routed index.<br/>
        /// </summary>
        /// <param name="selector">The semantic selector node to resolve.<br/></param>
        /// <param name="valueType">The exact normalized CLR component type expected by the persisted part.<br/></param>
        /// <param name="fingerprint">The stable selector fingerprint when resolution succeeds.<br/></param>
        /// <returns><see langword="true"/> when the selector has a complete backend-neutral descriptor.<br/></returns>
        private static bool TryReadCompositeSelectorFingerprint(
            ConditionWireSelector selector,
            Type valueType,
            out string fingerprint)
        {
            fingerprint = string.Empty;
            ValueOrExpression<string> replay = ConditionWireReplayer<T>.CreateReplaySelector(selector);
            if (replay.SelectorDescriptor is not IBackendNeutralSelectorDescriptor selectorDescriptor)
                return false;

            fingerprint = SelectorDescriptor<T>.FromSemanticDescriptor(selectorDescriptor, valueType).SemanticFingerprint;
            return true;
        }

        /// <summary>
        /// Maps one same-type Abraxas property-to-property comparison onto LibraDex's terminal routed-composite component comparison.<br/>
        /// Both selectors must match persisted parts of the same composite index by semantic fingerprint; mixed-type coercions deliberately retain the authoritative residual until LibraDex gains a lossless cross-domain comparison contract.<br/>
        /// </summary>
        /// <param name="index">The candidate routed composite index.<br/></param>
        /// <param name="leaf">The materialized two-selector public comparison leaf.<br/></param>
        /// <param name="partOrdinals">The participating left and right component ordinals when translation succeeds.<br/></param>
        /// <param name="criterion">The native terminal tuple comparison criterion when translation succeeds.<br/></param>
        /// <returns><see langword="true"/> when the comparison is exactly representable by two maintained components.<br/></returns>
        private static bool TryCreateCompositePropertyComparisonCriterion(
            LibraDexCompositeIndex index,
            ConditionWireLeaf leaf,
            out int[] partOrdinals,
            out LibraDexCompositePartCriterion? criterion)
        {
            partOrdinals = Array.Empty<int>();
            criterion = null;
            if (leaf.OperandSelector is null ||
                leaf.TextOptions.Length != 3 ||
                leaf.BooleanOptions.Length != 1 ||
                leaf.Operands.Length != 0)
            {
                return false;
            }

            Type comparisonType;
            Type leftType;
            Type rightType;
            try
            {
                comparisonType = NormalizeValueType(ConditionWireTypes.Resolve(leaf.ValueTypeCode));
                leftType = NormalizeValueType(ConditionWireTypes.Resolve(leaf.TextOptions[0]));
                rightType = NormalizeValueType(ConditionWireTypes.Resolve(leaf.TextOptions[1]));
            }
            catch (InvalidDataException)
            {
                return false;
            }

            if (leftType != rightType || leftType != comparisonType ||
                !TryReadCompositeSelectorFingerprint(leaf.Selector, leftType, out string leftFingerprint) ||
                !TryReadCompositeSelectorFingerprint(leaf.OperandSelector, rightType, out string rightFingerprint))
            {
                return false;
            }

            if ((leftType == typeof(Guid) || leftType == typeof(byte[])) &&
                leaf.Operator is not ("Equal" or "NotEqual"))
            {
                return false;
            }

            for (var leftOrdinal = 0; leftOrdinal < index.Descriptors.Length; leftOrdinal++)
            {
                LibraDexCompositeKeyPartSpec leftPart = index.Specs[leftOrdinal];
                if (leftPart.KeyType != leftType ||
                    !string.Equals(index.Descriptors[leftOrdinal].SemanticFingerprint, leftFingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                for (var rightOrdinal = 0; rightOrdinal < index.Descriptors.Length; rightOrdinal++)
                {
                    LibraDexCompositeKeyPartSpec rightPart = index.Specs[rightOrdinal];
                    if (rightPart.KeyType != rightType ||
                        !string.Equals(index.Descriptors[rightOrdinal].SemanticFingerprint, rightFingerprint, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string? culture = string.IsNullOrEmpty(leaf.TextOptions[2]) ? null : leaf.TextOptions[2];
                    LibraDexCompositePartComparisonCondition comparison = LibraDexCompositePart.Compare(leftPart.Name, rightPart.Name);
                    criterion = leaf.Operator switch
                    {
                        "Equal" => comparison.EqualTo(leaf.BooleanOptions[0], culture),
                        "NotEqual" => comparison.NotEqualTo(leaf.BooleanOptions[0], culture),
                        "Greater" => comparison.GreaterThan(leaf.BooleanOptions[0], culture),
                        "GreaterOrEqual" => comparison.GreaterOrEqual(leaf.BooleanOptions[0], culture),
                        "Less" => comparison.LessThan(leaf.BooleanOptions[0], culture),
                        "LessOrEqual" => comparison.LessOrEqual(leaf.BooleanOptions[0], culture),
                        _ => null
                    };
                    if (criterion is null)
                        return false;

                    partOrdinals = leftOrdinal == rightOrdinal
                        ? new[] { leftOrdinal }
                        : new[] { leftOrdinal, rightOrdinal };
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves the CLR type selected by one leaf for composite-fingerprint matching.<br/>
        /// Null-state, empty-state, and textual Guid-pattern leaves deliberately store an operand-oriented wire type, so their selector must instead be compared using the persisted composite part type.<br/>
        /// </summary>
        /// <param name="leaf">The materialized public condition leaf.<br/></param>
        /// <param name="part">The persisted candidate composite key part.<br/></param>
        /// <returns>The normalized CLR type represented by the leaf selector.<br/></returns>
        private static Type GetCompositeSelectorValueType(ConditionWireLeaf leaf, LibraDexCompositeKeyPartSpec part)
        {
            return leaf.Family is "Null" or "Empty" or "Guid"
                ? NormalizeValueType(part.KeyType)
                : NormalizeValueType(ConditionWireTypes.Resolve(leaf.ValueTypeCode));
        }

        /// <summary>
        /// Reads execution-materialized operands from the transient semantic capture, including literal and deferred membership collections.<br/>
        /// Operand-free state leaves intentionally return an empty array so null and empty predicates can target the explicit LibraDex key-state routes.<br/>
        /// </summary>
        private static bool TryReadStaticCompositeOperands(ConditionWireLeaf leaf, out object?[] values)
        {
            values = Array.Empty<object?>();
            if (leaf.Operands.Any(static operand => operand.IsParameter))
            return false;

            var result = new object?[leaf.Operands.Length];
            for (var index = 0; index < result.Length; index++)
            {
                ConditionWireValue operand = leaf.Operands[index];
                if (operand.IsNull)
                    return false;
                result[index] = InhetoBinary.FromStream(new BufferStream(operand.Payload)).ToObject(ConditionWireTypes.Resolve(operand.TypeCode));
            }

            values = result;
            return true;
        }

        /// <summary>
        /// Selects the native named-part factory matching the persisted composite key family.<br/>
        /// String comparison options flow unchanged to LibraDex's named-part predicate so folded and culture-aware candidates retain the same engine-owned treatment as ordinary indexed text conditions.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? TryCreateCompositeCriterion(
            string partName,
            LibraDexCompositeKeyPartSpec part,
            ConditionWireLeaf leaf,
            object?[] values)
        {
            if (leaf.Family is "Null" or "Empty")
                return TryCreateCompositeStateCriterion(partName, part, leaf);
            if (part.KeyType == typeof(string) && leaf.Family == "String")
                return TryCreateCompositeStringCriterion(partName, leaf, values);
            if (part.KeyType == typeof(Guid) && leaf.Family == "Guid")
                return TryCreateCompositeGuidCriterion(partName, leaf, values);
            if (part.KeyType == typeof(byte[]) && leaf.Family == "Binary")
                return TryCreateCompositeBinaryCriterion(partName, leaf, values);
            if (part.KeyType == typeof(DateTime) && leaf.Family == "DateTime")
                return TryCreateCompositeDateTimeCriterion(partName, leaf, values);
            if (leaf.Family == "TemporalWhole" && part.KeyType == NormalizeValueType(ConditionWireTypes.Resolve(leaf.ValueTypeCode)))
                return TryCreateCompositeTemporalWholeCriterion(partName, part.KeyType, leaf.Operator, values);
            if (leaf.Family == "TemporalStructured" && IsCompositeDateType(part.KeyType))
                return TryCreateCompositeStructuredTemporalCriterion(partName, part.KeyType, leaf.Operator, values);
            if (part.KeyFamily == CatalogIndexKeyFamily.Scalar &&
                leaf.Family is "Numeric" or "Boolean")
            {
                return TryCreateCompositeScalarCriterion(partName, part.KeyType, leaf.Operator, values);
            }

            return null;
        }

        /// <summary>
        /// Maps whole-value temporal equality, ordering, range, and membership leaves through the persisted CLR component type.<br/>
        /// The typed scalar criterion preserves DateOnly, TimeOnly, TimeSpan, DateTimeOffset, and DateTime comparison semantics without forcing a DateTime-only public bridge.<br/>
        /// </summary>
        /// <param name="partName">The named composite part.<br/></param>
        /// <param name="valueType">The exact persisted temporal CLR type.<br/></param>
        /// <param name="operation">The materialized public whole-value operation.<br/></param>
        /// <param name="values">The materialized comparison operands.<br/></param>
        /// <returns>The typed native criterion when the operation is supported.<br/></returns>
        private static LibraDexCompositePartCriterion? TryCreateCompositeTemporalWholeCriterion(
            string partName,
            Type valueType,
            string operation,
            object?[] values)
        {
            if (valueType == typeof(DateTime) ||
                valueType == typeof(DateTimeOffset) ||
                valueType == typeof(DateOnly) ||
                valueType == typeof(TimeOnly))
            {
                return TryCreateCompositeStructuredDateCriterion(partName, operation, values);
            }

            return TryCreateCompositeScalarCriterion(partName, valueType, operation, values);
        }

        /// <summary>
        /// Maps a whole-value temporal leaf to LibraDex's structured-date component domain.<br/>
        /// Literal membership is normalized into the single set operand required by the native criterion, while equality and ordered operations preserve their original operands.<br/>
        /// </summary>
        /// <param name="partName">The named date-like composite component.<br/></param>
        /// <param name="operation">The materialized public whole-value operation.<br/></param>
        /// <param name="values">The materialized literal operands.<br/></param>
        /// <returns>A same-semantic structured-date criterion, or <see langword="null"/> for an unrecognized operation.<br/></returns>
        private static LibraDexCompositePartCriterion? TryCreateCompositeStructuredDateCriterion(
            string partName,
            string operation,
            object?[] values)
        {
            LibraDexConditionOperatorKind? nativeOperation = operation switch
            {
                "Equal" or "EqualTo" => LibraDexConditionOperatorKind.EqualTo,
                "NotEqual" or "NotEqualTo" => LibraDexConditionOperatorKind.NotEqualTo,
                "Greater" => LibraDexConditionOperatorKind.GreaterThan,
                "GreaterOrEqual" => LibraDexConditionOperatorKind.GreaterOrEqual,
                "Less" => LibraDexConditionOperatorKind.LessThan,
                "LessOrEqual" => LibraDexConditionOperatorKind.LessOrEqual,
                "Between" => LibraDexConditionOperatorKind.Between,
                "NotBetween" => LibraDexConditionOperatorKind.NotBetween,
                "In" or "InSet" => LibraDexConditionOperatorKind.InSet,
                "NotIn" or "NotInSet" => LibraDexConditionOperatorKind.NotInSet,
                _ => null
            };
            if (nativeOperation is null)
                return null;

            object?[] nativeValues = nativeOperation is LibraDexConditionOperatorKind.InSet or LibraDexConditionOperatorKind.NotInSet
                ? new object?[] { values }
                : values;
            return new LibraDexCompositePartCriterion(
                partName,
                LibraDexConditionValueKind.DateTime,
                nativeOperation.Value,
                Array.AsReadOnly(nativeValues),
                ignoreCase: false,
                culture: null);
        }

        /// <summary>
        /// Maps deterministic calendar-component leaves to the corresponding named LibraDex composite date predicate.<br/>
        /// Clock-relative leaves are intentionally excluded here because candidate construction must use the exact execution clock captured by the owning Abraxas builder.<br/>
        /// </summary>
        /// <param name="partName">The named date-like composite component.<br/></param>
        /// <param name="operation">The materialized public structured temporal operation.<br/></param>
        /// <param name="values">The materialized component operands.<br/></param>
        /// <param name="valueType">The exact declared temporal CLR type used to preserve date-family and relative-time semantics.<br/></param>
        /// <returns>The matching deterministic native date criterion, or <see langword="null"/> when the operation needs a separate clock-bound adapter.<br/></returns>
        private static LibraDexCompositePartCriterion? TryCreateCompositeStructuredTemporalCriterion(
            string partName,
            Type valueType,
            string operation,
            object?[] values)
        {
            if (TryCreateCompositeRelativeTemporalCriterion(partName, valueType, operation, values, out LibraDexCompositePartCriterion? relative))
                return relative;

            LibraDexConditionOperatorKind? nativeOperation = operation switch
            {
                "YearEqual" => LibraDexConditionOperatorKind.YearEqualTo,
                "YearNotEqual" => LibraDexConditionOperatorKind.YearNotEqualTo,
                "YearIn" => LibraDexConditionOperatorKind.YearIn,
                "YearNotIn" => LibraDexConditionOperatorKind.YearNotIn,
                "YearBetween" => LibraDexConditionOperatorKind.YearRange,
                "YearNotBetween" => LibraDexConditionOperatorKind.YearNotRange,
                "YearOnOrAfter" => LibraDexConditionOperatorKind.YearOnOrAfter,
                "YearOnOrBefore" => LibraDexConditionOperatorKind.YearOnOrBefore,
                "YearMonth" => LibraDexConditionOperatorKind.YearMonth,
                "YearMonthIn" => LibraDexConditionOperatorKind.YearMonthIn,
                "YearMonthDay" => LibraDexConditionOperatorKind.YearMonthDay,
                "YearMonthDayIn" => LibraDexConditionOperatorKind.YearMonthDayIn,
                "YearMonths" => LibraDexConditionOperatorKind.YearInMonths,
                "MonthEqual" => LibraDexConditionOperatorKind.MonthEqualTo,
                "MonthNotEqual" => LibraDexConditionOperatorKind.MonthNotEqualTo,
                "MonthIn" => LibraDexConditionOperatorKind.MonthIn,
                "MonthNotIn" => LibraDexConditionOperatorKind.MonthNotIn,
                "MonthBetween" => LibraDexConditionOperatorKind.MonthRange,
                "MonthNotBetween" => LibraDexConditionOperatorKind.MonthNotRange,
                "DayEqual" => LibraDexConditionOperatorKind.DayEqualTo,
                "DayNotEqual" => LibraDexConditionOperatorKind.DayNotEqualTo,
                "DayIn" => LibraDexConditionOperatorKind.DayIn,
                "DayNotIn" => LibraDexConditionOperatorKind.DayNotIn,
                "DayBetween" => LibraDexConditionOperatorKind.DayRange,
                "DayNotBetween" => LibraDexConditionOperatorKind.DayNotRange,
                "MonthDay" => LibraDexConditionOperatorKind.MonthDay,
                "InQuarter" => LibraDexConditionOperatorKind.InQuarter,
                "InQuarterRange" => LibraDexConditionOperatorKind.InQuarterRange,
                "YearQuarter" => LibraDexConditionOperatorKind.YearQuarter,
                "AtQuarterStart" => LibraDexConditionOperatorKind.IsQuarterStart,
                "AtQuarterEnd" => LibraDexConditionOperatorKind.IsQuarterEnd,
                "AtHalfStart" => LibraDexConditionOperatorKind.IsHalfYearStart,
                "AtHalfEnd" => LibraDexConditionOperatorKind.IsHalfYearEnd,
                "AtFirstOfMonth" => LibraDexConditionOperatorKind.IsFirstOfMonth,
                "AtLastOfMonth" => LibraDexConditionOperatorKind.IsLastOfMonth,
                "InWeekend" => LibraDexConditionOperatorKind.IsWeekend,
                "InWeekday" => LibraDexConditionOperatorKind.IsWeekday,
                "HourEqual" => LibraDexConditionOperatorKind.HourEqualTo,
                "HourIn" => LibraDexConditionOperatorKind.HourIn,
                "HourNotIn" => LibraDexConditionOperatorKind.HourNotIn,
                "HourBetween" => LibraDexConditionOperatorKind.HourRange,
                "HourNotBetween" => LibraDexConditionOperatorKind.HourNotRange,
                "InMorning" => LibraDexConditionOperatorKind.IsMorning,
                "InAfternoon" => LibraDexConditionOperatorKind.IsAfternoon,
                "InEvening" => LibraDexConditionOperatorKind.IsEvening,
                "InNight" => LibraDexConditionOperatorKind.IsNight,
                _ => null
            };
            if (nativeOperation is null || values.Any(static value => value is not int and not ValueTuple<int, int> and not ValueTuple<int, int, int>))
                return null;

            object?[] nativeValues = nativeOperation.Value switch
            {
                LibraDexConditionOperatorKind.YearIn or
                LibraDexConditionOperatorKind.YearNotIn or
                LibraDexConditionOperatorKind.MonthIn or
                LibraDexConditionOperatorKind.MonthNotIn or
                LibraDexConditionOperatorKind.DayIn or
                LibraDexConditionOperatorKind.DayNotIn or
                LibraDexConditionOperatorKind.HourIn or
                LibraDexConditionOperatorKind.HourNotIn => new object?[] { values.Cast<int>().ToArray() },
                LibraDexConditionOperatorKind.YearMonthIn => new object?[] { values.Cast<(int year, int month)>().ToArray() },
                LibraDexConditionOperatorKind.YearMonthDayIn => new object?[] { values.Cast<(int year, int month, int day)>().ToArray() },
                LibraDexConditionOperatorKind.YearInMonths when values.Length >= 2 && values[0] is int year =>
                    new object?[] { year, values.Skip(1).Cast<int>().ToArray() },
                _ => values
            };

            if (nativeOperation == LibraDexConditionOperatorKind.YearInMonths && nativeValues.Length == 0)
            {
                return null;
            }

            return new LibraDexCompositePartCriterion(
                partName,
                LibraDexConditionValueKind.DateTime,
                nativeOperation.Value,
                Array.AsReadOnly(nativeValues),
                ignoreCase: false,
                culture: null);
        }

        /// <summary>
        /// Rewrites clock-relative public temporal leaves into exact whole-value bounds using the single UTC instant frozen by the owning condition builder.<br/>
        /// The resulting native component criterion never reads an ambient clock, so candidate construction and residual evaluation retain the same execution-time boundary.<br/>
        /// </summary>
        /// <param name="partName">The named temporal composite component.<br/></param>
        /// <param name="valueType">The exact persisted temporal CLR type.<br/></param>
        /// <param name="operation">The materialized relative temporal operation.<br/></param>
        /// <param name="values">Materialized integer operands followed by the frozen UTC instant.<br/></param>
        /// <param name="criterion">The exact rewritten component criterion when applicable.<br/></param>
        /// <returns><see langword="true"/> when <paramref name="operation"/> was recognized, even when its CLR type does not support that operation.<br/></returns>
        private static bool TryCreateCompositeRelativeTemporalCriterion(
            string partName,
            Type valueType,
            string operation,
            object?[] values,
            out LibraDexCompositePartCriterion? criterion)
        {
            criterion = null;
            if (operation is not ("IsToday" or "IsYesterday" or "InLastDays" or "InLastHours" or "InLastMinutes"))
                return false;
            if (values.Length == 0 || values[^1] is not DateTime utcNow)
                return true;

            if (valueType == typeof(DateOnly))
            {
                DateOnly through = DateOnly.FromDateTime(utcNow);
                if (operation == "IsToday")
                    criterion = TryCreateCompositeStructuredDateCriterion(partName, "EqualTo", new object?[] { through });
                else if (operation == "IsYesterday")
                    criterion = TryCreateCompositeStructuredDateCriterion(partName, "EqualTo", new object?[] { through.AddDays(-1) });
                else if (operation == "InLastDays" && values.Length == 2 && values[0] is int days)
                    criterion = TryCreateCompositeStructuredDateCriterion(partName, "Between", new object?[] { through.AddDays(-days), through });
                return true;
            }

            if (valueType != typeof(DateTime))
                return true;

            DateTime lower;
            DateTime upper;
            switch (operation)
            {
                case "IsToday":
                    lower = utcNow.Date;
                    upper = lower.AddDays(1).AddTicks(-1);
                    break;
                case "IsYesterday":
                    upper = utcNow.Date.AddTicks(-1);
                    lower = utcNow.Date.AddDays(-1);
                    break;
                case "InLastDays" when values.Length == 2 && values[0] is int days:
                    lower = utcNow.AddDays(-days);
                    upper = utcNow;
                    break;
                case "InLastHours" when values.Length == 2 && values[0] is int hours:
                    lower = utcNow.AddHours(-hours);
                    upper = utcNow;
                    break;
                case "InLastMinutes" when values.Length == 2 && values[0] is int minutes:
                    lower = utcNow.AddMinutes(-minutes);
                    upper = utcNow;
                    break;
                default:
                    return true;
            }

            criterion = TryCreateCompositeStructuredDateCriterion(partName, "Between", new object?[] { lower, upper });
            return true;
        }

        /// <summary>
        /// Maps Abraxas null and empty leaves to LibraDex's explicit composite key-state routes.<br/>
        /// String and binary parts distinguish null from empty through <see cref="NullKey"/>, while all other optional composite domains use <see cref="ScalarNull"/>.<br/>
        /// </summary>
        /// <param name="partName">The persisted named composite component.<br/></param>
        /// <param name="part">The persisted component contract.<br/></param>
        /// <param name="leaf">The operand-free public state leaf.<br/></param>
        /// <returns>The matching native state criterion, or <see langword="null"/> when the state is not meaningful for this component type.<br/></returns>
        private static LibraDexCompositePartCriterion? TryCreateCompositeStateCriterion(
            string partName,
            LibraDexCompositeKeyPartSpec part,
            ConditionWireLeaf leaf)
        {
            bool isNull = leaf.Family == "Null";
            bool isEmpty = leaf.Family == "Empty";
            bool positive = leaf.Operator is "IsNull" or "IsEmpty";
            if ((!isNull && !isEmpty) || !positive && leaf.Operator is not ("IsNotNull" or "IsNotEmpty"))
                return null;

            if (part.KeyType == typeof(string))
            {
                LibraDexCompositeStringPartCondition text = LibraDexCompositePart.String(partName);
                if (isEmpty)
                {
                    return positive ? text.EqualTo(NullKey.Empty) : text.NotEqualTo(NullKey.Empty);
                }

                return positive ? text.EqualTo(NullKey.Null) : text.NotEqualTo(NullKey.Null);
            }

            if (part.KeyType == typeof(byte[]))
            {
                LibraDexCompositeBinaryPartCondition binary = LibraDexCompositePart.Binary(partName);
                if (isEmpty)
                {
                    return positive ? binary.EqualTo(NullKey.Empty) : binary.NotEqualTo(NullKey.Empty);
                }

                return positive ? binary.EqualTo(NullKey.Null) : binary.NotEqualTo(NullKey.Null);
            }

            if (isEmpty)
                return null;

            if (part.KeyType == typeof(Guid))
            {
                LibraDexCompositeGuidPartCondition guid = LibraDexCompositePart.Guid(partName);
                return positive ? guid.EqualTo(ScalarNull.Null) : guid.EqualTo(ScalarNull.NonNull);
            }

            if (part.KeyType is not null &&
                (part.KeyType == typeof(DateTime) ||
                 part.KeyType == typeof(DateTimeOffset) ||
                 part.KeyType == typeof(DateOnly) ||
                 part.KeyType == typeof(TimeOnly)))
            {
                LibraDexCompositeDatePartCondition date = LibraDexCompositePart.Date(partName);
                return positive ? date.EqualTo(ScalarNull.Null) : date.EqualTo(ScalarNull.NonNull);
            }

            return TryCreateCompositeScalarStateCriterion(partName, part.KeyType!, positive ? ScalarNull.Null : ScalarNull.NonNull);
        }

        /// <summary>
        /// Closes the scalar null-state factory over one persisted optional component type.<br/>
        /// Reflection is planning-only and preserves LibraDex's typed scalar-null route without widening nullable values into an object comparison.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? TryCreateCompositeScalarStateCriterion(string partName, Type valueType, ScalarNull state)
        {
            try
            {
                MethodInfo factory = typeof(RecordStore<T>)
                    .GetMethod(nameof(CreateCompositeScalarStateCriterion), BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(valueType);
                return (LibraDexCompositePartCriterion?)factory.Invoke(null, new object?[] { partName, state });
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates one typed composite scalar null-state predicate.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion CreateCompositeScalarStateCriterion<TValue>(string partName, ScalarNull state)
            => LibraDexCompositePart.Scalar<TValue>(partName).EqualTo(state);

        /// <summary>
        /// Maps one static public string leaf to its same-semantic LibraDex composite component equivalent.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? TryCreateCompositeStringCriterion(string partName, ConditionWireLeaf leaf, object?[] values)
        {
            var part = LibraDexCompositePart.String(partName);
            bool ignoreCase = leaf.BooleanOptions.FirstOrDefault();
            string? culture = leaf.TextOptions.FirstOrDefault();
            if (string.IsNullOrEmpty(culture))
                culture = null;
            if (values.Length == 1 &&
                values[0] is string pattern &&
                leaf.Operator is "Matches" or "NotMatches")
            {
                RegexOptions options = RegexOptions.Compiled;
                if (ignoreCase)
                    options |= RegexOptions.IgnoreCase;
                if (culture is null)
                    options |= RegexOptions.CultureInvariant;

                return new LibraDexCompositePartCriterion(
                    partName,
                    LibraDexConditionValueKind.String,
                    leaf.Operator == "Matches"
                        ? LibraDexConditionOperatorKind.MatchesPattern
                        : LibraDexConditionOperatorKind.NotMatchesPattern,
                    Array.AsReadOnly<object?>(new object?[] { new Regex(pattern, options) }),
                    ignoreCase: false,
                    culture: null);
            }

            IEnumerable<string>? membership = TryReadCompositeMembership<string>(values);
            return leaf.Operator switch
            {
                "Equal" or "EqualTo" when values.Length == 1 && values[0] is string value => part.EqualTo(value, ignoreCase, culture),
                "NotEqual" or "NotEqualTo" when values.Length == 1 && values[0] is string value => part.NotEqualTo(value, ignoreCase, culture),
                "Greater" when values.Length == 1 && values[0] is string value => part.GreaterThan(value, ignoreCase, culture),
                "GreaterOrEqual" when values.Length == 1 && values[0] is string value => part.GreaterOrEqual(value, ignoreCase, culture),
                "Less" when values.Length == 1 && values[0] is string value => part.LessThan(value, ignoreCase, culture),
                "LessOrEqual" when values.Length == 1 && values[0] is string value => part.LessOrEqual(value, ignoreCase, culture),
                "Between" when values.Length == 2 && values[0] is string lower && values[1] is string upper => part.Between(lower, upper, ignoreCase, culture),
                "NotBetween" when values.Length == 2 && values[0] is string lower && values[1] is string upper => part.NotBetween(lower, upper, ignoreCase, culture),
                "StartsWith" when values.Length == 1 && values[0] is string value => part.StartsWith(value, ignoreCase, culture),
                "EndsWith" when values.Length == 1 && values[0] is string value => part.EndsWith(value, ignoreCase, culture),
                "Contains" when values.Length == 1 && values[0] is string value => part.Contains(value, ignoreCase, culture),
                "NotContains" when values.Length == 1 && values[0] is string value => part.NotContains(value, ignoreCase, culture),
                "Like" when values.Length == 1 && values[0] is string value => part.Like(value, ignoreCase, culture),
                "NotLike" when values.Length == 1 && values[0] is string value => part.NotLike(value, ignoreCase, culture),
                "In" or "InSet" when membership is not null => part.InSet(membership, ignoreCase, culture),
                "NotIn" or "NotInSet" when membership is not null => part.NotInSet(membership, ignoreCase, culture),
                _ => null
            };
        }

        /// <summary>
        /// Maps one exact Guid component predicate without inventing string-guid normalization rules.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? TryCreateCompositeGuidCriterion(string partName, ConditionWireLeaf leaf, object?[] values)
        {
            var part = LibraDexCompositePart.Guid(partName);
            if (values.Length == 1 &&
                values[0] is string text &&
                TryNormalizeCompositeGuidPattern(text, out string normalized))
            {
                return leaf.Operator switch
                {
                    "StartsWith" => part.StartsWith(normalized),
                    "EndsWith" => part.EndsWith(normalized),
                    "Contains" => part.Contains(normalized),
                    "MatchesPattern" => part.MatchesPattern(normalized),
                    _ => null
                };
            }

            IEnumerable<Guid>? membership = TryReadCompositeMembership<Guid>(values);
            return leaf.Operator switch
            {
                "Equal" or "EqualTo" when values.Length == 1 && values[0] is Guid value => part.EqualTo(value),
                "NotEqual" or "NotEqualTo" when values.Length == 1 && values[0] is Guid value => part.NotEqualTo(value),
                "In" or "InSet" when membership is not null => part.InSet(membership),
                "NotIn" or "NotInSet" when membership is not null => part.NotInSet(membership),
                _ => null
            };
        }

        /// <summary>
        /// Normalizes a public Abraxas Guid pattern with the same permissive hexadecimal and <c>x</c> wildcard treatment used by its semantic evaluator.<br/>
        /// LibraDex's native Guid predicate intentionally rejects unrelated text, so normalization occurs once while planning rather than changing either backend's public contract.<br/>
        /// </summary>
        /// <param name="value">The materialized public Guid text or partial pattern.<br/></param>
        /// <param name="normalized">The canonical hexadecimal and wildcard core accepted by LibraDex.<br/></param>
        /// <returns><see langword="true"/> when at least one retained nibble or wildcard remains.<br/></returns>
        private static bool TryNormalizeCompositeGuidPattern(string value, out string normalized)
        {
            Span<char> buffer = stackalloc char[32];
            var count = 0;
            foreach (char character in value)
            {
                if ((Uri.IsHexDigit(character) || character is 'x' or 'X') && count < buffer.Length)
                    buffer[count++] = char.ToLowerInvariant(character);
            }

            normalized = count == 0 ? string.Empty : new string(buffer[..count]);
            return count != 0;
        }

        /// <summary>
        /// Maps one binary component equality or prefix predicate while retaining LibraDex's byte-domain comparison semantics.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? TryCreateCompositeBinaryCriterion(string partName, ConditionWireLeaf leaf, object?[] values)
        {
            var part = LibraDexCompositePart.Binary(partName);
            return leaf.Operator switch
            {
                "Equal" or "EqualTo" when values.Length == 1 && values[0] is byte[] value => part.EqualTo(value),
                "NotEqual" or "NotEqualTo" when values.Length == 1 && values[0] is byte[] value => part.NotEqualTo(value),
                "StartsWith" when values.Length == 1 && values[0] is byte[] value => part.StartsWith(value),
                "EndsWith" when values.Length == 1 && values[0] is byte[] value => part.EndsWith(value),
                "Contains" when values.Length == 1 && values[0] is byte[] value => part.Contains(value),
                _ => null
            };
        }

        /// <summary>
        /// Maps full DateTime equality and ordered bounds to a structured composite date tier.<br/>
        /// Other temporal component semantics retain the established residual path until their Abraxas operator wire contracts are explicitly paired with LibraDex's structured-date names.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? TryCreateCompositeDateTimeCriterion(string partName, ConditionWireLeaf leaf, object?[] values)
        {
            if (values.Any(static value => value is not DateTime))
                return null;

            var part = LibraDexCompositePart.Date(partName);
            return leaf.Operator switch
            {
                "Equal" or "EqualTo" when values.Length == 1 => part.EqualTo((DateTime)values[0]!),
                "NotEqual" or "NotEqualTo" when values.Length == 1 => part.NotEqualTo((DateTime)values[0]!),
                "Greater" when values.Length == 1 => part.GreaterThan((DateTime)values[0]!),
                "GreaterOrEqual" when values.Length == 1 => part.GreaterOrEqual((DateTime)values[0]!),
                "Less" when values.Length == 1 => part.LessThan((DateTime)values[0]!),
                "LessOrEqual" when values.Length == 1 => part.LessOrEqual((DateTime)values[0]!),
                "Between" when values.Length == 2 => part.Between((DateTime)values[0]!, (DateTime)values[1]!),
                "NotBetween" when values.Length == 2 => part.NotBetween((DateTime)values[0]!, (DateTime)values[1]!),
                _ => null
            };
        }

        /// <summary>
        /// Closes the generic scalar component factory over the persisted CLR type at planning time.<br/>
        /// Reflection occurs once while a condition is planned; identity traversal remains entirely inside LibraDex's native cursor.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? TryCreateCompositeScalarCriterion(string partName, Type valueType, string operation, object?[] values)
        {
            try
            {
                MethodInfo factory = typeof(RecordStore<T>)
                    .GetMethod(nameof(CreateCompositeScalarCriterion), BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(valueType);
                return (LibraDexCompositePartCriterion?)factory.Invoke(null, new object?[] { partName, operation, values });
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Maps compatible scalar comparisons, ranges, membership, and bitwise-AND equality for one exact persisted scalar component type.<br/>
        /// </summary>
        private static LibraDexCompositePartCriterion? CreateCompositeScalarCriterion<TValue>(string partName, string operation, object?[] values)
        {
            var part = LibraDexCompositePart.Scalar<TValue>(partName);
            IEnumerable<TValue>? membership = TryReadCompositeMembership<TValue>(values);
            if (operation == "BitNotAnd" &&
                values.Length == 2 &&
                values[0] is TValue excludedMask &&
                values[1] is TValue expectedValue &&
                TryComplementCompositeIntegral(excludedMask, out TValue complemented))
            {
                return part.BitAnd(complemented, expectedValue);
            }

            if (operation == "BitXOr" &&
                values.Length == 2 &&
                values[0] is TValue mask &&
                values[1] is TValue expectedXor &&
                TryXorCompositeIntegral(mask, expectedXor, out TValue requiredValue))
            {
                return part.EqualTo(requiredValue);
            }

            if (operation == "BitNot" &&
                values.Length == 1 &&
                values[0] is TValue expectedNot &&
                TryComplementCompositeIntegral(expectedNot, out TValue requiredComplement))
            {
                return part.EqualTo(requiredComplement);
            }

            if (operation == "BitOr" &&
                values.Length == 2 &&
                values[0] is TValue maskOr &&
                values[1] is TValue expectedOr &&
                TryReduceCompositeBitOr(maskOr, expectedOr, out TValue preservedMask, out TValue preservedExpected))
            {
                return part.BitAnd(preservedMask, preservedExpected);
            }

            return operation switch
            {
                "Equal" or "EqualTo" when values.Length == 1 && values[0] is TValue value => part.EqualTo(value),
                "NotEqual" or "NotEqualTo" when values.Length == 1 && values[0] is TValue value => part.NotEqualTo(value),
                "Greater" when values.Length == 1 && values[0] is TValue value => part.GreaterThan(value),
                "GreaterOrEqual" when values.Length == 1 && values[0] is TValue value => part.GreaterOrEqual(value),
                "Less" when values.Length == 1 && values[0] is TValue value => part.LessThan(value),
                "LessOrEqual" when values.Length == 1 && values[0] is TValue value => part.LessOrEqual(value),
                "Between" when values.Length == 2 && values[0] is TValue lower && values[1] is TValue upper => part.Between(lower, upper),
                "NotBetween" when values.Length == 2 && values[0] is TValue lower && values[1] is TValue upper => part.NotBetween(lower, upper),
                "In" or "InSet" when membership is not null => part.InSet(membership),
                "NotIn" or "NotInSet" when membership is not null => part.NotInSet(membership),
                "BitAnd" when values.Length == 2 && values[0] is TValue bitMask && values[1] is TValue expected => part.BitAnd(bitMask, expected),
                _ => null
            };
        }

        /// <summary>
        /// Complements one fixed-width integral operand without widening its storage domain.<br/>
        /// This is the exact reduction required for Abraxas <c>BitNotAnd(mask, expected)</c>, whose semantic form is <c>(value &amp; ~mask) == expected</c>.<br/>
        /// </summary>
        /// <typeparam name="TValue">The exact persisted scalar component type.<br/></typeparam>
        /// <param name="value">The materialized integral mask to complement.<br/></param>
        /// <param name="complemented">The same-width complemented mask when supported.<br/></param>
        /// <returns><see langword="true"/> for one fixed-width integral type; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryComplementCompositeIntegral<TValue>(TValue value, out TValue complemented)
        {
            object boxed = value!;
            object? result = typeof(TValue) == typeof(byte) ? unchecked((byte)~(byte)boxed) :
                typeof(TValue) == typeof(sbyte) ? unchecked((sbyte)~(sbyte)boxed) :
                typeof(TValue) == typeof(short) ? unchecked((short)~(short)boxed) :
                typeof(TValue) == typeof(ushort) ? unchecked((ushort)~(ushort)boxed) :
                typeof(TValue) == typeof(int) ? ~(int)boxed :
                typeof(TValue) == typeof(uint) ? ~(uint)boxed :
                typeof(TValue) == typeof(long) ? ~(long)boxed :
                typeof(TValue) == typeof(ulong) ? ~(ulong)boxed :
                typeof(TValue) == typeof(Int128) ? ~(Int128)boxed :
                typeof(TValue) == typeof(UInt128) ? ~(UInt128)boxed : null;
            if (result is TValue typed)
            {
                complemented = typed;
                return true;
            }

            complemented = default!;
            return false;
        }

        /// <summary>
        /// Reduces <c>value ^ mask == expected</c> to an exact equality over the original fixed-width integral value.<br/>
        /// </summary>
        private static bool TryXorCompositeIntegral<TValue>(TValue mask, TValue expected, out TValue required)
        {
            object left = mask!;
            object right = expected!;
            object? result = typeof(TValue) == typeof(byte) ? (byte)((byte)left ^ (byte)right) :
                typeof(TValue) == typeof(sbyte) ? (sbyte)((sbyte)left ^ (sbyte)right) :
                typeof(TValue) == typeof(short) ? (short)((short)left ^ (short)right) :
                typeof(TValue) == typeof(ushort) ? (ushort)((ushort)left ^ (ushort)right) :
                typeof(TValue) == typeof(int) ? (int)left ^ (int)right :
                typeof(TValue) == typeof(uint) ? (uint)left ^ (uint)right :
                typeof(TValue) == typeof(long) ? (long)left ^ (long)right :
                typeof(TValue) == typeof(ulong) ? (ulong)left ^ (ulong)right :
                typeof(TValue) == typeof(Int128) ? (Int128)left ^ (Int128)right :
                typeof(TValue) == typeof(UInt128) ? (UInt128)left ^ (UInt128)right : null;
            if (result is TValue typed)
            {
                required = typed;
                return true;
            }

            required = default!;
            return false;
        }

        /// <summary>
        /// Reduces <c>value | mask == expected</c> to its equivalent masked equality when the requested result contains every forced mask bit.<br/>
        /// </summary>
        private static bool TryReduceCompositeBitOr<TValue>(TValue mask, TValue expected, out TValue preservedMask, out TValue preservedExpected)
        {
            object left = mask!;
            object right = expected!;
            bool satisfiable = typeof(TValue) == typeof(byte) ? (((byte)right & (byte)left) == (byte)left) :
                typeof(TValue) == typeof(sbyte) ? (((sbyte)right & (sbyte)left) == (sbyte)left) :
                typeof(TValue) == typeof(short) ? (((short)right & (short)left) == (short)left) :
                typeof(TValue) == typeof(ushort) ? (((ushort)right & (ushort)left) == (ushort)left) :
                typeof(TValue) == typeof(int) ? (((int)right & (int)left) == (int)left) :
                typeof(TValue) == typeof(uint) ? (((uint)right & (uint)left) == (uint)left) :
                typeof(TValue) == typeof(long) ? (((long)right & (long)left) == (long)left) :
                typeof(TValue) == typeof(ulong) ? (((ulong)right & (ulong)left) == (ulong)left) :
                typeof(TValue) == typeof(Int128) ? (((Int128)right & (Int128)left) == (Int128)left) :
                typeof(TValue) == typeof(UInt128) ? (((UInt128)right & (UInt128)left) == (UInt128)left) : false;
            if (!satisfiable ||
                !TryComplementCompositeIntegral(mask, out preservedMask) ||
                !TryAndCompositeIntegral(expected, preservedMask, out preservedExpected))
            {
                preservedMask = default!;
                preservedExpected = default!;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies a fixed-width integral AND without widening the persisted scalar type.<br/>
        /// </summary>
        private static bool TryAndCompositeIntegral<TValue>(TValue left, TValue right, out TValue result)
        {
            object first = left!;
            object second = right!;
            object? combined = typeof(TValue) == typeof(byte) ? (byte)((byte)first & (byte)second) :
                typeof(TValue) == typeof(sbyte) ? (sbyte)((sbyte)first & (sbyte)second) :
                typeof(TValue) == typeof(short) ? (short)((short)first & (short)second) :
                typeof(TValue) == typeof(ushort) ? (ushort)((ushort)first & (ushort)second) :
                typeof(TValue) == typeof(int) ? (int)first & (int)second :
                typeof(TValue) == typeof(uint) ? (uint)first & (uint)second :
                typeof(TValue) == typeof(long) ? (long)first & (long)second :
                typeof(TValue) == typeof(ulong) ? (ulong)first & (ulong)second :
                typeof(TValue) == typeof(Int128) ? (Int128)first & (Int128)second :
                typeof(TValue) == typeof(UInt128) ? (UInt128)first & (UInt128)second : null;
            if (combined is TValue typed)
            {
                result = typed;
                return true;
            }

            result = default!;
            return false;
        }

        /// <summary>
        /// Converts one literal semantic collection operand into the exact typed membership sequence expected by LibraDex.<br/>
        /// Collections are accepted only as a sole static operand, preventing a malformed wire payload from being mistaken for several scalar comparison operands.<br/>
        /// </summary>
        /// <typeparam name="TValue">The persisted composite component CLR type.<br/></typeparam>
        /// <param name="values">Materialized semantic operands for one public leaf.<br/></param>
        /// <returns>The typed membership sequence when every value matches the persisted component type; otherwise <see langword="null"/>.<br/></returns>
        private static IEnumerable<TValue>? TryReadCompositeMembership<TValue>(object?[] values)
        {
            System.Collections.IEnumerable? source = values.Length == 1 && values[0] is not string
                ? values[0] as System.Collections.IEnumerable
                : null;

            var result = new List<TValue>();
            if (source is not null)
            {
                foreach (object? value in source)
                {
                    if (value is not TValue typed)
                        return null;
                    result.Add(typed);
                }

                return result;
            }

            if (values.Length == 0)
                return null;
            foreach (object? value in values)
            {
                if (value is not TValue typed)
                    return null;
                result.Add(typed);
            }

            return result;
        }

        /// <summary>
        /// Selects the most specific maintained composite index whose every ordered selector part has a static equality leaf in one top-level AND condition.<br/>
        /// Additional AND leaves remain residual semantics, while groups, OR connectors, deferred operands, and unsupported operators conservatively leave planning to existing direct, single-index candidate, or Fractal paths.<br/>
        /// </summary>
        /// <param name="where">Completed condition being planned.<br/></param>
        /// <param name="identities">Exact composite-key identities when a compatible index is found.<br/></param>
        /// <param name="logicalName">Selected composite index name for planning diagnostics.<br/></param>
        /// <returns><see langword="true"/> when a full composite equality key was resolved; otherwise <see langword="false"/>.<br/></returns>
        private bool TryGetLibraDexCompositeCandidateIdentities(
            Condition<T>.EndCondition where,
            out IEnumerable<ulong> identities,
            out string logicalName)
        {
            identities = Array.Empty<ulong>();
            logicalName = string.Empty;
            LibraDexCompositeIndex[] indexes = libraDexCompositeIndexes;
            if (indexes.Length == 0)
                return false;

            ConditionDescriptor<T> condition;
            try
            {
                condition = where.ToDescriptor();
            }
            catch (NotSupportedException)
            {
                return false;
            }
            if (!condition.IsComplete)
                return false;

            var equalities = new Dictionary<string, object?>(StringComparer.Ordinal);
            ConditionWireItem[] items = condition.SemanticSequence.Items;
            bool expectOperand = true;
            for (var i = 0; i < items.Length; i++)
            {
                ConditionWireItem item = items[i];
                if (expectOperand)
                {
                    if (item.Kind != "Leaf" || item.Leaf is null || !TryReadCompositeEquality(item.Leaf, out string fingerprint, out object? value))
                        return false;
                    if (equalities.TryGetValue(fingerprint, out object? prior) && !LibraDexRuntimeKeyComparer.Instance.Equals(prior, value))
                        return false;
                    equalities[fingerprint] = value;
                }
                else if (item.Kind != "And")
                {
                    return false;
                }
                expectOperand = !expectOperand;
            }
            if (expectOperand)
                return false;

            LibraDexCompositeIndex? selected = null;
            object?[]? selectedValues = null;
            for (var indexOrdinal = 0; indexOrdinal < indexes.Length; indexOrdinal++)
            {
                LibraDexCompositeIndex candidate = indexes[indexOrdinal];
                var values = new object?[candidate.Descriptors.Length];
                bool complete = true;
                for (var partOrdinal = 0; partOrdinal < candidate.Descriptors.Length; partOrdinal++)
                {
                    if (!equalities.TryGetValue(candidate.Descriptors[partOrdinal].SemanticFingerprint, out values[partOrdinal]))
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete && (selected is null || candidate.Descriptors.Length > selected.Descriptors.Length))
                {
                    selected = candidate;
                    selectedValues = values;
                }
            }
            if (selected is null || selectedValues is null)
                return false;

            IReadOnlyList<byte[]> encodedIdentities = selected.Index.Identities.GetByKey(LibraDexCompositeKey.Of(selectedValues));
            var decoded = new ulong[encodedIdentities.Count];
            for (var i = 0; i < decoded.Length; i++)
                decoded[i] = LibraDexCanonical.Numeric.Read<ulong>(encodedIdentities[i]);
            identities = decoded;
            logicalName = selected.LogicalName;
            return true;
        }

        /// <summary>
        /// Reads one static whole-value equality leaf into the exact selector fingerprint and CLR operand used by composite planning.<br/>
        /// </summary>
        private static bool TryReadCompositeEquality(ConditionWireLeaf leaf, out string fingerprint, out object? value)
        {
            fingerprint = string.Empty;
            value = null;
            if (leaf.Operator is not ("Equal" or "EqualTo") ||
                leaf.Operands.Length != 1 ||
                leaf.Operands[0].IsParameter ||
                leaf.Operands[0].IsCollection)
            {
                return false;
            }
            if (leaf.Family == "String" &&
                ((leaf.BooleanOptions.Length != 0 && leaf.BooleanOptions[0]) ||
                 (leaf.IntegerOptions.Length != 0 && leaf.IntegerOptions[0] != (int)IndexCaseType.None) ||
                 (leaf.TextOptions.Length != 0 && !string.IsNullOrEmpty(leaf.TextOptions[0]))))
            {
                return false;
            }

            ConditionWireValue operand = leaf.Operands[0];
            Type valueType = NormalizeValueType(ConditionWireTypes.Resolve(leaf.ValueTypeCode));
            ValueOrExpression<string> replay = ConditionWireReplayer<T>.CreateReplaySelector(leaf.Selector);
            if (replay.SelectorDescriptor is not IBackendNeutralSelectorDescriptor selectorDescriptor)
                return false;
            SelectorDescriptor<T> descriptor = SelectorDescriptor<T>.FromSemanticDescriptor(selectorDescriptor, valueType);
            fingerprint = descriptor.SemanticFingerprint;
            if (operand.IsNull)
                return true;
            value = InhetoBinary.FromStream(new BufferStream(operand.Payload)).ToObject(ConditionWireTypes.Resolve(operand.TypeCode));
            return true;
        }

        /// <summary>
        /// Builds a native LibraDex composite-part descriptor from one exact Abraxas selector result contract.<br/>
        /// </summary>
        private static LibraDexCompositeKeyPartSpec CreateCompositePartSpec(string partName, Type resultType, TextIndexProfile profile, IndexDirection direction)
        {
            LibraDexIndexSortOrder sort = direction == IndexDirection.Descending ? LibraDexIndexSortOrder.Descending : LibraDexIndexSortOrder.Ascending;
            CatalogIndexKeyFamily family = resultType == typeof(string) ? CatalogIndexKeyFamily.String
                : resultType == typeof(byte[]) ? CatalogIndexKeyFamily.Blob
                : resultType == typeof(Guid) ? CatalogIndexKeyFamily.Guid
                : IsCompositeDateType(resultType) ? CatalogIndexKeyFamily.Date
                : CatalogIndexKeyFamily.Scalar;
            return new LibraDexCompositeKeyPartSpec(
                partName,
                resultType,
                family,
                resultType == typeof(string) ? ToLibraDexStringKeys(profile) : StringKeys.Exact,
                GuidKeys.Exact,
                DateKeys.Exact,
                DateTimeKeyEncoding.CalendarSdt,
                sort);
        }

        /// <summary>
        /// Tests whether one selector result can use a native routed-composite key-part family without an application codec.<br/>
        /// </summary>
        private static bool IsSupportedCompositePartType(Type type)
            => type == typeof(byte[]) || IsSupportedLibraDexScalarKeyType(type);

        /// <summary>
        /// Tests whether one exact selector result belongs to LibraDex's structured date/time composite family.<br/>
        /// </summary>
        private static bool IsCompositeDateType(Type type)
            => type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);

        /// <summary>
        /// Produces a bounded deterministic physical catalog name from the developer name and exact ordered part contracts.<br/>
        /// </summary>
        private static string BuildCompositePhysicalName(string logicalName, OrdinaryCompositeIndexPartIntent[] parts)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(logicalName));
            for (var i = 0; i < parts.Length; i++)
            {
                hash.AppendData(parts[i].SelectorDescriptor);
                hash.AppendData(Encoding.UTF8.GetBytes(parts[i].ResultTypeCode));
                hash.AppendData(BitConverter.GetBytes((int)parts[i].TextProfile));
                hash.AppendData(BitConverter.GetBytes((int)parts[i].Direction));
            }
            string fingerprint = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return "cmp_" + SanitizeTokenForIndex(logicalName, 32) + "_" + fingerprint[..12];
        }

        internal sealed class LibraDexCompositeIndex
        {
            internal LibraDexCompositeIndex(string logicalName, string physicalName, bool unique, LibraDexRoutedCompositeIndex index, LibraDexCompositeKeyPartSpec[] specs, SelectorDescriptor<T>[] descriptors, Func<InhetoBinary, (bool HasValue, object? Value)>[] readers, Catalog? ownerCatalog)
            {
                LogicalName = logicalName;
                PhysicalName = physicalName;
                Unique = unique;
                Index = index;
                RoutedIndex = index;
                Specs = specs;
                Descriptors = descriptors;
                Readers = readers;
                OwnerCatalog = ownerCatalog;
            }

            internal string LogicalName { get; }
            internal string PhysicalName { get; }
            internal bool Unique { get; }
            internal IIndex Index { get; }
            internal LibraDexRoutedCompositeIndex RoutedIndex { get; }
            internal LibraDexCompositeKeyPartSpec[] Specs { get; }
            internal SelectorDescriptor<T>[] Descriptors { get; }
            internal Func<InhetoBinary, (bool HasValue, object? Value)>[] Readers { get; }
            internal Catalog? OwnerCatalog { get; }

            internal bool ContractEquals(LibraDexCompositeIndex other)
                => string.Equals(LogicalName, other.LogicalName, StringComparison.Ordinal) &&
                    string.Equals(PhysicalName, other.PhysicalName, StringComparison.Ordinal) &&
                    ReferenceEquals(OwnerCatalog, other.OwnerCatalog) &&
                    Unique == other.Unique &&
                    Specs.AsSpan().SequenceEqual(other.Specs);
        }

        private readonly record struct CompositeRuntimeKey(object?[] Values, bool ContainsNull)
        {
            internal LibraDexCompositeKey ToLibraDexKey() => LibraDexCompositeKey.Of(Values);
        }

        private sealed class CompositeRuntimeKeyComparer : IEqualityComparer<CompositeRuntimeKey>
        {
            internal static CompositeRuntimeKeyComparer Instance { get; } = new();

            public bool Equals(CompositeRuntimeKey x, CompositeRuntimeKey y)
            {
                if (x.Values.Length != y.Values.Length)
                    return false;
                for (var i = 0; i < x.Values.Length; i++)
                {
                    object? left = x.Values[i];
                    object? right = y.Values[i];
                    if (left is byte[] leftBytes && right is byte[] rightBytes)
                    {
                        if (!leftBytes.AsSpan().SequenceEqual(rightBytes))
                            return false;
                    }
                    else if (!Equals(left, right))
                    {
                        return false;
                    }
                }
                return true;
            }

            public int GetHashCode(CompositeRuntimeKey value)
            {
                var hash = new HashCode();
                for (var i = 0; i < value.Values.Length; i++)
                {
                    if (value.Values[i] is byte[] bytes)
                    {
                        for (var j = 0; j < bytes.Length; j++)
                            hash.Add(bytes[j]);
                    }
                    else
                    {
                        hash.Add(value.Values[i]);
                    }
                }
                return hash.ToHashCode();
            }
        }
    }
}
