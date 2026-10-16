using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using LibraDex;
using Inheto;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Gets phase measurements from the most recently completed virtual-property index ensure on this store facade.<br/>
        /// Callers should capture the value immediately because a later virtual-property ensure replaces it.<br/>
        /// </summary>
        public VirtualPropertyIndexEnsureDiagnostics? LastVirtualPropertyIndexEnsureDiagnostics { get; private set; }

        /// <summary>
        /// Selects how the initial value set for an index-backed virtual property is obtained.<br/>
        /// Later ordinary mutations continue to evaluate the durable selector unless the mutation supplies an explicit virtual value.<br/>
        /// </summary>
        public enum VirtualPropertyPopulationMode
        {
            /// <summary>Evaluates the durable selector for initial population; a non-empty supplied value sequence is rejected.<br/></summary>
            Evaluated = 0,
            /// <summary>Requires an explicit supplied value or missing-value declaration for every existing record identity.<br/></summary>
            Supplied = 1,
            /// <summary>Uses supplied values for declared identities and evaluates the durable selector for the remaining existing records.<br/></summary>
            Hybrid = 2
        }

        /// <summary>
        /// Defines ordinary calculated-index behavior plus the initial population source for one index-backed virtual property.<br/>
        /// Persistence remains a creation-method choice so value semantics do not change when physical lifetime changes.<br/>
        /// </summary>
        public sealed class VirtualPropertyIndexOptions : CalculatedIndexOptions
        {
            /// <summary>Gets or initializes how existing record identities receive their initial virtual values.<br/></summary>
            public VirtualPropertyPopulationMode PopulationMode { get; init; } = VirtualPropertyPopulationMode.Evaluated;
        }

        /// <summary>
        /// Carries one caller-supplied virtual value for an existing Abraxas record identity.<br/>
        /// <see cref="HasValue"/> distinguishes an explicit missing/null projection from a present value, including a nullable CLR default.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact virtual-property result type declared by the durable selector.<br/></typeparam>
        public readonly record struct VirtualPropertyValue<TValue>
        {
            /// <summary>Creates one present virtual-property value for an existing record identity.<br/></summary>
            /// <param name="recordID">Existing Abraxas record identity.<br/></param>
            /// <param name="value">Caller-owned projected value.<br/></param>
            public VirtualPropertyValue(ulong recordID, TValue? value)
            {
                RecordID = recordID;
                HasValue = true;
                Value = value;
            }

            private VirtualPropertyValue(ulong recordID)
            {
                RecordID = recordID;
                HasValue = false;
                Value = default;
            }

            /// <summary>Gets the existing Abraxas identity whose virtual value is supplied.<br/></summary>
            public ulong RecordID { get; }

            /// <summary>Gets whether this item carries a present value instead of an explicit missing/null projection.<br/></summary>
            public bool HasValue { get; }

            /// <summary>Gets the supplied value when <see cref="HasValue"/> is true.<br/></summary>
            public TValue? Value { get; }

            /// <summary>Creates an explicit missing/null virtual value for one existing identity.<br/></summary>
            /// <param name="recordID">Existing Abraxas record identity.<br/></param>
            /// <returns>A supplied item that participates through the configured null policy.<br/></returns>
            public static VirtualPropertyValue<TValue> Missing(ulong recordID) => new(recordID);
        }

        /// <summary>
        /// Ensures one persistent expression index whose logical name is also a virtual property path.<br/>
        /// The index participates in ordinary selection, mutation, completeness, recovery, and reprojection while the base Fractal object remains unchanged.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact virtual-property result type.<br/></typeparam>
        /// <param name="virtualPropertyPath">Developer-owned dotted path, including paths not present on <typeparamref name="T"/>.<br/></param>
        /// <param name="selector">Durable backend-neutral expression used for evaluated values and later ordinary mutations.<br/></param>
        /// <param name="suppliedValues">Optional caller-provided initial values used by supplied or hybrid population.<br/></param>
        /// <param name="options">Optional population, uniqueness, null, empty-string, and binary-shape contract.<br/></param>
        /// <returns>The ordinary Abraxas index handle whose name and key path equal the normalized virtual property path.<br/></returns>
        public RecordIndex EnsureVirtualExpressionIndex<TValue>(
            string virtualPropertyPath,
            IndexSelectorBuilder selector,
            IEnumerable<VirtualPropertyValue<TValue>>? suppliedValues = null,
            VirtualPropertyIndexOptions? options = null)
            => EnsureVirtualExpressionIndexCore(virtualPropertyPath, selector, suppliedValues, options, useMemoryIndex: false);

        /// <summary>
        /// Ensures one volatile expression index whose logical name is also a virtual property path.<br/>
        /// Volatility affects only catalog lifetime; selection, mutation participation, completeness, and reprojection semantics match the persistent form.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact virtual-property result type.<br/></typeparam>
        /// <param name="virtualPropertyPath">Developer-owned dotted path, including paths not present on <typeparamref name="T"/>.<br/></param>
        /// <param name="selector">Durable backend-neutral expression used for evaluated values and later ordinary mutations.<br/></param>
        /// <param name="suppliedValues">Optional caller-provided initial values used by supplied or hybrid population.<br/></param>
        /// <param name="options">Optional population, uniqueness, null, empty-string, and binary-shape contract.<br/></param>
        /// <returns>The ordinary Abraxas index handle whose name and key path equal the normalized virtual property path.<br/></returns>
        public RecordIndex EnsureMemoryVirtualExpressionIndex<TValue>(
            string virtualPropertyPath,
            IndexSelectorBuilder selector,
            IEnumerable<VirtualPropertyValue<TValue>>? suppliedValues = null,
            VirtualPropertyIndexOptions? options = null)
            => EnsureVirtualExpressionIndexCore(virtualPropertyPath, selector, suppliedValues, options, useMemoryIndex: true);

        /// <summary>
        /// Validates, prepares, and atomically publishes one index-backed virtual property through the existing calculated-index lifecycle.<br/>
        /// Supplied identities are validated against the authoritative store before physical mutation; hybrid population evaluates only identities the caller did not supply.<br/>
        /// </summary>
        private RecordIndex EnsureVirtualExpressionIndexCore<TValue>(
            string virtualPropertyPath,
            IndexSelectorBuilder selector,
            IEnumerable<VirtualPropertyValue<TValue>>? suppliedValues,
            VirtualPropertyIndexOptions? options,
            bool useMemoryIndex)
        {
            long totalStarted = Stopwatch.GetTimestamp();
            LastVirtualPropertyIndexEnsureDiagnostics = null;
            var timing = new VirtualPropertyIndexEnsureTiming();
            ArgumentNullException.ThrowIfNull(selector);
            options ??= new VirtualPropertyIndexOptions();
            if (!Enum.IsDefined(options.PopulationMode))
                throw new ArgumentOutOfRangeException(nameof(options), options.PopulationMode, "Unknown virtual-property population mode.");
            if (!Enum.IsDefined(options.NullValues) || !Enum.IsDefined(options.EmptyStrings) || !Enum.IsDefined(options.BinaryShape))
                throw new ArgumentOutOfRangeException(nameof(options), "The virtual-property index contains an unsupported calculated-index option.");

            string logicalName = NormalizeVirtualPropertyPath(virtualPropertyPath);
            SelectorDescriptor<T> descriptor = SelectorDescriptor<T>.FromSelector(selector.CalculatedSelector);
            Type resultType = NormalizeValueType(descriptor.ResultType);
            if (NormalizeValueType(typeof(TValue)) != resultType)
                throw new InvalidOperationException($"Virtual property '{logicalName}' declares '{typeof(TValue).FullName}' but its durable selector returns '{resultType.FullName}'.");
            if (selector.DeclaredValueType is not null && NormalizeValueType(selector.DeclaredValueType) != resultType)
                throw new InvalidOperationException($"Virtual property '{logicalName}' selector metadata contradicts its durable result type.");
            if (selector.IsTextResult != (resultType == typeof(string)))
                throw new InvalidOperationException($"Virtual property '{logicalName}' selector text metadata contradicts its durable result type.");
            if (resultType != typeof(byte[]) && options.BinaryShape != CalculatedIndexBinaryShape.VariableLength)
                throw new InvalidOperationException("VirtualPropertyIndexOptions.BinaryShape applies only to byte[] virtual properties.");

            TextIndexProfile profile = resultType == typeof(string)
                ? ToTextIndexProfile(selector.CaseType, ignoreCase: false)
                : TextIndexProfile.Exact;
            IndexDirection direction = selector.Sort == IndexSortDirection.Descending
                ? IndexDirection.Descending
                : IndexDirection.Ascending;
            string physicalName = CreateVirtualPropertyPhysicalName(logicalName, descriptor.SemanticFingerprint, options.PopulationMode);
            timing.ContractTicks = Stopwatch.GetTimestamp() - totalStarted;
            long populationPreparationStarted = Stopwatch.GetTimestamp();
            List<(ulong RecordID, object? Key)>? prepared = PrepareVirtualPropertyPopulation(
                logicalName,
                descriptor,
                suppliedValues,
                options);
            timing.PopulationPreparationTicks = Stopwatch.GetTimestamp() - populationPreparationStarted;
            bool registered = TryRegisterLibraDexCalculatedIndex(
                logicalName,
                physicalName,
                descriptor,
                profile,
                direction,
                options.Unique,
                options.NullValues == CalculatedIndexNullValues.Include,
                options.EmptyStrings == CalculatedIndexEmptyStrings.Include,
                ToCalculatedBinaryKeyStorage(resultType, options.BinaryShape),
                GetCalculatedBinaryKeyBytes(resultType, options.BinaryShape),
                useMemoryIndex ? LibraDexVolatileIndexes : libraDexIdentityIndexes
                    ?? throw new InvalidOperationException("A persistent LibraDex identity group is not bound to this store."),
                useMemoryIndex ? libraDexVolatileCatalog : null,
                populateExisting: prepared is not null,
                preparedPopulation: prepared,
                isVirtualProperty: true,
                virtualValuesAreAuthoritative: prepared is not null,
                virtualPropertyTiming: timing);
            if (!registered)
                throw new NotSupportedException($"Virtual property result type '{resultType.FullName}' does not have a supported ordered representation.");

            long handleStarted = Stopwatch.GetTimestamp();
            var index = new RecordIndex(logicalName, logicalName, options.Unique, this);
            timing.HandleCreationTicks = Stopwatch.GetTimestamp() - handleStarted;
            timing.TotalTicks = Stopwatch.GetTimestamp() - totalStarted;
            LastVirtualPropertyIndexEnsureDiagnostics = timing.CreateDiagnostics();
            return index;
        }

        /// <summary>
        /// Produces the complete validated initial tuple set for supplied or hybrid virtual-property population.<br/>
        /// Pure evaluated population returns null so the established optimized calculated-index backfill path remains authoritative.<br/>
        /// </summary>
        private List<(ulong RecordID, object? Key)>? PrepareVirtualPropertyPopulation<TValue>(
            string virtualPropertyPath,
            SelectorDescriptor<T> descriptor,
            IEnumerable<VirtualPropertyValue<TValue>>? suppliedValues,
            VirtualPropertyIndexOptions options)
        {
            if (options.PopulationMode == VirtualPropertyPopulationMode.Evaluated)
            {
                if (suppliedValues is not null && suppliedValues.Any())
                    throw new ArgumentException("Evaluated virtual-property population does not accept supplied values.", nameof(suppliedValues));
                return null;
            }

            var supplied = new Dictionary<ulong, VirtualPropertyValue<TValue>>();
            if (suppliedValues is not null)
            {
                foreach (VirtualPropertyValue<TValue> item in suppliedValues)
                {
                    if (item.RecordID == 0)
                        throw new ArgumentException("Virtual-property supplied values cannot target reserved identity 0.", nameof(suppliedValues));
                    if (!supplied.TryAdd(item.RecordID, item))
                        throw new ArgumentException($"Virtual property '{virtualPropertyPath}' received identity '{item.RecordID}' more than once.", nameof(suppliedValues));
                }
            }

            if (!TryCreateStoreSelectorReader(descriptor, out Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader))
                throw new NotSupportedException($"Virtual property '{virtualPropertyPath}' does not expose executable backend-neutral selector semantics.");
            var prepared = new List<(ulong RecordID, object? Key)>();
            HashSet<object?>? unique = options.Unique ? new HashSet<object?>(LibraDexRuntimeKeyComparer.Instance) : null;
            foreach (ulong recordID in IterateRecordIDs())
            {
                bool hasValue;
                object? value;
                if (supplied.Remove(recordID, out VirtualPropertyValue<TValue> item))
                {
                    hasValue = item.HasValue && item.Value is not null;
                    value = item.Value;
                }
                else if (options.PopulationMode == VirtualPropertyPopulationMode.Supplied)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Configuration,
                        $"Supplied virtual property '{virtualPropertyPath}' has no value declaration for existing record '{recordID}'.",
                        virtualPropertyPath,
                        virtualPropertyPath,
                        recordID);
                }
                else
                {
                    var evaluated = reader(GetInhetoBinary(recordID, nameof(PrepareVirtualPropertyPopulation)));
                    hasValue = evaluated.HasValue && evaluated.Value is not null;
                    value = evaluated.Value;
                }

                if (!hasValue)
                {
                    if (options.NullValues == CalculatedIndexNullValues.Include)
                        prepared.Add((recordID, null));
                    continue;
                }
                if (value is string { Length: 0 } && options.EmptyStrings == CalculatedIndexEmptyStrings.Exclude)
                    continue;
                if (value is not TValue)
                    throw new IndexContractException(IndexContractFailureKind.Configuration, $"Virtual property '{virtualPropertyPath}' received a supplied value whose CLR type is not '{typeof(TValue).FullName}'.", virtualPropertyPath, virtualPropertyPath, recordID);
                if (unique is not null && !unique.Add(value))
                    throw new IndexContractException(IndexContractFailureKind.UniqueConstraint, $"Unique virtual property '{virtualPropertyPath}' received the same non-null value for multiple identities.", virtualPropertyPath, virtualPropertyPath, recordID);
                prepared.Add((recordID, value));
            }
            if (supplied.Count != 0)
                throw new ArgumentException($"Virtual property '{virtualPropertyPath}' received {supplied.Count:N0} supplied identity value(s) that do not exist in the store.", nameof(suppliedValues));
            return prepared;
        }

        /// <summary>
        /// Normalizes an unrestricted dotted virtual path while rejecting ambiguous empty segments and outer whitespace.<br/>
        /// The path need not exist on the current record type; future reprojection types may introduce the same nested shape.<br/>
        /// </summary>
        private static string NormalizeVirtualPropertyPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!string.Equals(path, path.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("A virtual property path cannot contain outer whitespace.", nameof(path));
            string normalized = path[0] == '.' ? path : "." + path;
            if (normalized.Length == 1 || normalized[^1] == '.' || normalized.Contains("..", StringComparison.Ordinal))
                throw new ArgumentException("A virtual property path must contain non-empty dotted segments.", nameof(path));
            return normalized;
        }

        /// <summary>
        /// Creates a deterministic physical catalog name distinct from the developer-owned virtual property path.<br/>
        /// Both the path and expression fingerprint participate so a changed semantic contract cannot silently reuse prior tuples.<br/>
        /// </summary>
        /// <param name="virtualPropertyPath">Normalized developer-owned virtual property path.<br/></param>
        /// <param name="selectorFingerprint">Stable semantic fingerprint of the durable value expression.<br/></param>
        /// <param name="populationMode">Initial population source whose semantics must remain distinguishable across reopen.<br/></param>
        /// <returns>A deterministic private LibraDex physical name for the ordinary Abraxas index.<br/></returns>
        private string CreateVirtualPropertyPhysicalName(
            string virtualPropertyPath,
            string selectorFingerprint,
            VirtualPropertyPopulationMode populationMode)
        {
            string canonical = virtualPropertyPath + "\n" + selectorFingerprint + "\n" + populationMode;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            string prefix = populationMode switch
            {
                VirtualPropertyPopulationMode.Evaluated => "vpxe",
                VirtualPropertyPopulationMode.Supplied => "vpxs",
                VirtualPropertyPopulationMode.Hybrid => "vpxh",
                _ => throw new ArgumentOutOfRangeException(nameof(populationMode), populationMode, "Unknown virtual-property population mode.")
            };
            return $"{prefix}_{name}_{SanitizeTokenForIndex(virtualPropertyPath, 28)}_{hash[..16]}";
        }

        /// <summary>
        /// Recognizes the private physical-name namespace reserved for index-backed virtual properties.<br/>
        /// Durable intent retains the distinct developer logical path, so reopen can restore virtual semantics without a second catalog concept.<br/>
        /// </summary>
        private static bool IsVirtualPropertyPhysicalName(string physicalName)
            => physicalName.StartsWith("vpxe_", StringComparison.Ordinal) ||
                physicalName.StartsWith("vpxs_", StringComparison.Ordinal) ||
                physicalName.StartsWith("vpxh_", StringComparison.Ordinal);

        /// <summary>
        /// Determines whether persisted virtual-property tuples, rather than reevaluation, remain the authoritative reprojection value source.<br/>
        /// Supplied and hybrid definitions preserve caller-provided initial values; evaluated definitions can read their durable selector directly from the already loaded Inheto payload.<br/>
        /// </summary>
        private static bool VirtualPropertyUsesAuthoritativeIndexValues(string physicalName)
            => physicalName.StartsWith("vpxs_", StringComparison.Ordinal) ||
                physicalName.StartsWith("vpxh_", StringComparison.Ordinal);

        private volatile VirtualPropertyRuntime[] virtualPropertyRuntimes = Array.Empty<VirtualPropertyRuntime>();
        private readonly object virtualPropertyRuntimeLock = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Type, string Path), VirtualPropertyOverlayPlan> VirtualPropertyOverlayPlans = new();

        /// <summary>
        /// Publishes one attached virtual-property runtime after its physical tuples and completeness intent are usable.<br/>
        /// Evaluated properties retain only their compiled selector; supplied and hybrid properties retain or lazily reconstruct the index-authoritative identity map used by reprojection.<br/>
        /// </summary>
        private void RegisterVirtualPropertyRuntime(
            LibraDexSimpleIndex entry,
            List<(ulong RecordID, object? Key)>? preparedPopulation,
            bool valuesAreAuthoritative)
        {
            Dictionary<ulong, object?>? values = null;
            if (valuesAreAuthoritative && preparedPopulation is not null)
            {
                values = new Dictionary<ulong, object?>(preparedPopulation.Count);
                for (var i = 0; i < preparedPopulation.Count; i++)
                    values[preparedPopulation[i].RecordID] = preparedPopulation[i].Key;
            }

            var runtime = new VirtualPropertyRuntime(
                entry.Path,
                entry.KeyType,
                entry.Index,
                entry.CalculatedReader ?? throw new InvalidOperationException($"Virtual property '{entry.Path}' has no durable selector reader."),
                valuesAreAuthoritative,
                values);
            lock (virtualPropertyRuntimeLock)
            {
                VirtualPropertyRuntime[] current = virtualPropertyRuntimes;
                for (var i = 0; i < current.Length; i++)
                {
                    if (!string.Equals(current[i].Path, entry.Path, StringComparison.Ordinal))
                        continue;
                    var next = (VirtualPropertyRuntime[])current.Clone();
                    next[i] = runtime;
                    virtualPropertyRuntimes = next;
                    return;
                }

                var appended = new VirtualPropertyRuntime[current.Length + 1];
                Array.Copy(current, appended, current.Length);
                appended[current.Length] = runtime;
                virtualPropertyRuntimes = appended;
            }
        }

        /// <summary>
        /// Detaches the reprojection runtime owned by one physical virtual-property index.<br/>
        /// Removal uses physical index identity rather than logical path so persistent and volatile ownership cannot accidentally detach each other.<br/>
        /// </summary>
        /// <param name="index">Exact LibraDex index instance being unregistered from the owning store.<br/></param>
        private void UnregisterVirtualPropertyRuntime(IIndex index)
        {
            ArgumentNullException.ThrowIfNull(index);
            lock (virtualPropertyRuntimeLock)
            {
                VirtualPropertyRuntime[] current = virtualPropertyRuntimes;
                var retainedCount = 0;
                for (var i = 0; i < current.Length; i++)
                {
                    if (!current[i].MatchesIndex(index))
                        retainedCount++;
                }
                if (retainedCount == current.Length)
                    return;
                if (retainedCount == 0)
                {
                    virtualPropertyRuntimes = Array.Empty<VirtualPropertyRuntime>();
                    return;
                }

                var retained = new VirtualPropertyRuntime[retainedCount];
                var target = 0;
                for (var i = 0; i < current.Length; i++)
                {
                    if (!current[i].MatchesIndex(index))
                        retained[target++] = current[i];
                }
                virtualPropertyRuntimes = retained;
            }
        }

        /// <summary>
        /// Overlays every applicable index-backed virtual value after ordinary Inheto materialization and before the public materialized event.<br/>
        /// Paths absent from the requested projection type are ignored; partially matching or type-incompatible shapes fail explicitly instead of silently returning a misleading default.<br/>
        /// </summary>
        private void OverlayVirtualProperties<TOut>(TOut entity, InhetoBinary binary, ulong recordID)
        {
            if (entity is null)
                return;
            VirtualPropertyRuntime[] runtimes = virtualPropertyRuntimes;
            if (runtimes.Length == 0)
                return;

            object root = entity;
            for (var i = 0; i < runtimes.Length; i++)
            {
                VirtualPropertyRuntime runtime = runtimes[i];
                VirtualPropertyOverlayPlan plan = VirtualPropertyOverlayPlans.GetOrAdd(
                    (typeof(TOut), runtime.Path),
                    static key => VirtualPropertyOverlayPlan.Create(key.Type, key.Path));
                if (!plan.Applies)
                    continue;
                if (!runtime.TryGetValue(recordID, binary, out object? value))
                    continue;
                plan.Apply(root, value, runtime.ValueType);
            }
        }

        /// <summary>
        /// Updates already-materialized supplied/hybrid reprojection maps after ordinary index mutation succeeds.<br/>
        /// Evaluated properties need no identity map because their value is read from the same authoritative payload already used for materialization.<br/>
        /// </summary>
        private void UpdateVirtualPropertyRuntimeValues(ulong recordID, InhetoBinary binary)
        {
            VirtualPropertyRuntime[] runtimes = virtualPropertyRuntimes;
            for (var i = 0; i < runtimes.Length; i++)
                runtimes[i].UpdateAfterMutation(recordID, binary);
        }

        /// <summary>
        /// Resolves the exact prior key owned by one supplied or hybrid virtual-property index before an overwrite or delete.<br/>
        /// The matched runtime is reported separately from key presence so an intentionally unindexed null/missing value never falls back to an unrelated record-derived expression key.<br/>
        /// </summary>
        /// <param name="index">Exact physical virtual-property index being maintained.<br/></param>
        /// <param name="recordID">Existing Abraxas identity whose prior tuple is being removed.<br/></param>
        /// <param name="hasValue">Whether the authoritative index contains a tuple for the identity.<br/></param>
        /// <param name="value">Exact prior key, including a present null key when null participation is enabled.<br/></param>
        /// <returns><see langword="true"/> when a supplied/hybrid authoritative runtime owns the index; otherwise <see langword="false"/>.<br/></returns>
        private bool TryGetAuthoritativeVirtualPropertyMutationValue(
            IIndex index,
            ulong recordID,
            out bool hasValue,
            out object? value)
        {
            VirtualPropertyRuntime[] runtimes = virtualPropertyRuntimes;
            for (var i = 0; i < runtimes.Length; i++)
            {
                if (runtimes[i].MatchesIndex(index) &&
                    runtimes[i].TryGetAuthoritativeValue(recordID, out hasValue, out value))
                {
                    return true;
                }
            }

            hasValue = false;
            value = null;
            return false;
        }

        /// <summary>
        /// Removes deleted identities from any supplied/hybrid reprojection map that has already been materialized.<br/>
        /// A not-yet-built map requires no work because its later forward index walk observes the post-delete physical state.<br/>
        /// </summary>
        private void RemoveVirtualPropertyRuntimeValues(ulong recordID)
        {
            VirtualPropertyRuntime[] runtimes = virtualPropertyRuntimes;
            for (var i = 0; i < runtimes.Length; i++)
                runtimes[i].Remove(recordID);
        }

        /// <summary>
        /// Holds the executable and optional identity-authoritative state for one attached virtual property.<br/>
        /// The forward index is scanned only when a supplied/hybrid property is actually requested by a projection after reopen.<br/>
        /// </summary>
        private sealed class VirtualPropertyRuntime
        {
            private readonly object gate = new();
            private readonly IIndex index;
            private readonly Func<InhetoBinary, (bool HasValue, object? Value)> reader;
            private readonly bool valuesAreAuthoritative;
            private Dictionary<ulong, object?>? values;

            internal VirtualPropertyRuntime(
                string path,
                Type valueType,
                IIndex index,
                Func<InhetoBinary, (bool HasValue, object? Value)> reader,
                bool valuesAreAuthoritative,
                Dictionary<ulong, object?>? values)
            {
                Path = path;
                ValueType = valueType;
                this.index = index;
                this.reader = reader;
                this.valuesAreAuthoritative = valuesAreAuthoritative;
                this.values = values;
            }

            internal string Path { get; }
            internal Type ValueType { get; }

            /// <summary>Returns whether this runtime is owned by the exact physical LibraDex index being detached.<br/></summary>
            internal bool MatchesIndex(IIndex candidate) => ReferenceEquals(index, candidate);

            /// <summary>
            /// Reads one exact prior supplied/hybrid key for mutation maintenance.<br/>
            /// Returning an authoritative match with <paramref name="hasValue"/> false prevents fallback evaluation from inventing a tuple that was never present.<br/>
            /// </summary>
            /// <param name="recordID">Existing identity whose current tuple is needed.<br/></param>
            /// <param name="hasValue">Whether the identity currently owns an indexed virtual value.<br/></param>
            /// <param name="value">Exact indexed key when present.<br/></param>
            /// <returns><see langword="true"/> only for a supplied/hybrid index-authoritative runtime.<br/></returns>
            internal bool TryGetAuthoritativeValue(ulong recordID, out bool hasValue, out object? value)
            {
                if (!valuesAreAuthoritative)
                {
                    hasValue = false;
                    value = null;
                    return false;
                }

                Dictionary<ulong, object?> map = EnsureValues();
                lock (gate)
                    hasValue = map.TryGetValue(recordID, out value);
                return true;
            }

            /// <summary>Gets one projection value from the authoritative index map or durable evaluated selector.<br/></summary>
            internal bool TryGetValue(ulong recordID, InhetoBinary binary, out object? value)
            {
                if (!valuesAreAuthoritative)
                {
                    var evaluated = reader(binary);
                    value = evaluated.Value;
                    return evaluated.HasValue;
                }

                Dictionary<ulong, object?> map = EnsureValues();
                lock (gate)
                    return map.TryGetValue(recordID, out value);
            }

            /// <summary>Maintains an already-built authoritative map without imposing work on unopened reprojection paths.<br/></summary>
            internal void UpdateAfterMutation(ulong recordID, InhetoBinary binary)
            {
                if (!valuesAreAuthoritative || values is null)
                    return;
                var evaluated = reader(binary);
                lock (gate)
                {
                    if (evaluated.HasValue)
                        values[recordID] = evaluated.Value;
                    else
                        values.Remove(recordID);
                }
            }

            /// <summary>Removes one deleted identity from an already-built authoritative map.<br/></summary>
            internal void Remove(ulong recordID)
            {
                if (values is null)
                    return;
                lock (gate)
                    values.Remove(recordID);
            }

            /// <summary>Lazily reconstructs supplied/hybrid values from the complete ordinary index after reopen.<br/></summary>
            private Dictionary<ulong, object?> EnsureValues()
            {
                if (values is not null)
                    return values;
                lock (gate)
                {
                    if (values is not null)
                        return values;
                    var built = new Dictionary<ulong, object?>();
                    CatalogIdentityGroupIndexes group = index.Catalog[index.Group];
                    foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(index))
                    {
                        ulong identity = tuple.Identity switch
                        {
                            ulong typed => typed,
                            byte[] encoded => LibraDexCanonical.Numeric.Read<ulong>(encoded),
                            _ => Convert.ToUInt64(tuple.Identity, System.Globalization.CultureInfo.InvariantCulture)
                        };
                        built[identity] = tuple.Key;
                    }
                    values = built;
                    return built;
                }
            }
        }

        /// <summary>
        /// Caches one projection-type member chain and its intermediate constructors for a virtual property path.<br/>
        /// Reflection discovery occurs once per target type/path pair; steady-state rows reuse the resolved members.<br/>
        /// </summary>
        private sealed class VirtualPropertyOverlayPlan
        {
            private readonly MemberInfo[] members;
            private readonly Func<object>[] factories;

            private VirtualPropertyOverlayPlan(bool applies, MemberInfo[] members, Func<object>[] factories)
            {
                Applies = applies;
                this.members = members;
                this.factories = factories;
            }

            internal bool Applies { get; }

            /// <summary>Builds one strict nested member plan, treating an absent first segment as a projection that does not request the virtual value.<br/></summary>
            internal static VirtualPropertyOverlayPlan Create(Type rootType, string path)
            {
                if (rootType.IsValueType)
                    return new VirtualPropertyOverlayPlan(false, Array.Empty<MemberInfo>(), Array.Empty<Func<object>>());
                string[] segments = path.TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
                var members = new MemberInfo[segments.Length];
                var factories = new Func<object>[Math.Max(0, segments.Length - 1)];
                Type current = rootType;
                for (var i = 0; i < segments.Length; i++)
                {
                    MemberInfo? member = current.GetProperty(segments[i], BindingFlags.Instance | BindingFlags.Public)
                        ?? (MemberInfo?)current.GetField(segments[i], BindingFlags.Instance | BindingFlags.Public);
                    if (member is null)
                    {
                        if (i == 0)
                            return new VirtualPropertyOverlayPlan(false, Array.Empty<MemberInfo>(), Array.Empty<Func<object>>());
                        throw new InvalidOperationException($"Projection type '{rootType.FullName}' partially matches virtual path '{path}' but is missing segment '{segments[i]}'.");
                    }
                    if (!CanWrite(member))
                        throw new InvalidOperationException($"Projection member '{current.FullName}.{segments[i]}' requested by virtual path '{path}' is not writable.");
                    members[i] = member;
                    current = GetMemberType(member);
                    if (i < segments.Length - 1)
                    {
                        if (current.IsValueType || current.IsAbstract || current.IsInterface || current.GetConstructor(Type.EmptyTypes) is null)
                            throw new InvalidOperationException($"Projection member '{segments[i]}' in virtual path '{path}' cannot be created when null.");
                        Type captured = current;
                        factories[i] = () => Activator.CreateInstance(captured)!;
                    }
                }
                return new VirtualPropertyOverlayPlan(true, members, factories);
            }

            /// <summary>Assigns one exact virtual value while creating null intermediate objects along the cached member chain.<br/></summary>
            internal void Apply(object root, object? value, Type valueType)
            {
                Type targetType = GetMemberType(members[^1]);
                Type normalizedTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (normalizedTarget != valueType)
                    throw new InvalidOperationException($"Virtual property '{members[^1].Name}' returns '{valueType.FullName}' but projection member type is '{targetType.FullName}'.");
                object current = root;
                for (var i = 0; i < members.Length - 1; i++)
                {
                    object? child = GetValue(members[i], current);
                    if (child is null)
                    {
                        child = factories[i]();
                        SetValue(members[i], current, child);
                    }
                    current = child;
                }
                SetValue(members[^1], current, value);
            }

            private static Type GetMemberType(MemberInfo member)
                => member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

            private static bool CanWrite(MemberInfo member)
                => member is PropertyInfo property ? property.SetMethod is not null : !((FieldInfo)member).IsInitOnly;

            private static object? GetValue(MemberInfo member, object instance)
                => member is PropertyInfo property ? property.GetValue(instance) : ((FieldInfo)member).GetValue(instance);

            private static void SetValue(MemberInfo member, object instance, object? value)
            {
                if (member is PropertyInfo property)
                    property.SetValue(instance, value);
                else
                    ((FieldInfo)member).SetValue(instance, value);
            }
        }

        /// <summary>
        /// Accumulates non-overlapping timestamp deltas across the virtual-property ensure and calculated-index registration seams.<br/>
        /// The mutable collector remains private to one serialized ensure operation; callers receive only its immutable public diagnostic snapshot.<br/>
        /// </summary>
        private sealed class VirtualPropertyIndexEnsureTiming
        {
            internal long TotalTicks;
            internal long ContractTicks;
            internal long PopulationPreparationTicks;
            internal long ReaderPreparationTicks;
            internal long MutationGateTicks;
            internal long RegistrationPreparationTicks;
            internal long PhysicalDefinitionTicks;
            internal long RuntimeAttachmentTicks;
            internal long PopulationTicks;
            internal long CompletionTicks;
            internal long HandleCreationTicks;

            /// <summary>
            /// Freezes the collected timestamp deltas as stable public durations after a successful ensure.<br/>
            /// Timestamp conversion occurs once per phase rather than inside record-processing loops.<br/>
            /// </summary>
            /// <returns>Immutable phase diagnostics for the completed ensure operation.<br/></returns>
            internal VirtualPropertyIndexEnsureDiagnostics CreateDiagnostics()
                => new(
                    BackfillElapsed(TotalTicks),
                    BackfillElapsed(ContractTicks),
                    BackfillElapsed(PopulationPreparationTicks),
                    BackfillElapsed(ReaderPreparationTicks),
                    BackfillElapsed(MutationGateTicks),
                    BackfillElapsed(RegistrationPreparationTicks),
                    BackfillElapsed(PhysicalDefinitionTicks),
                    BackfillElapsed(RuntimeAttachmentTicks),
                    BackfillElapsed(PopulationTicks),
                    BackfillElapsed(CompletionTicks),
                    BackfillElapsed(HandleCreationTicks));
        }
    }

    /// <summary>
    /// Reports non-overlapping setup, population, and publication costs for one completed index-backed virtual-property ensure.<br/>
    /// The existing <see cref="IndexBackfillDiagnostics"/> remains authoritative for record extraction and physical tuple construction inside <see cref="PopulationTime"/>.<br/>
    /// </summary>
    /// <param name="TotalTime">Complete ensure duration measured inside Abraxas.<br/></param>
    /// <param name="ContractTime">Argument, durable-selector, type-contract, profile, direction, and physical-name preparation time.<br/></param>
    /// <param name="PopulationPreparationTime">Caller-supplied or hybrid initial-value preparation time; evaluated population normally reports near zero.<br/></param>
    /// <param name="ReaderPreparationTime">Time spent compiling or resolving the reusable Inheto selector reader.<br/></param>
    /// <param name="MutationGateTime">Time spent acquiring the store and index mutation gates.<br/></param>
    /// <param name="RegistrationPreparationTime">Time spent checking conflicts, resolving catalog existence, and preparing durable intent before physical definition access.<br/></param>
    /// <param name="PhysicalDefinitionTime">Time spent creating or opening the empty physical LibraDex definition.<br/></param>
    /// <param name="RuntimeAttachmentTime">Time spent constructing and attaching the Abraxas runtime index descriptor before population.<br/></param>
    /// <param name="PopulationTime">Complete population call, including the extraction and physical-build phases reported by <see cref="IndexBackfillDiagnostics"/>.<br/></param>
    /// <param name="CompletionTime">Time spent publishing ordinary-index readiness and registering the virtual-property runtime after population.<br/></param>
    /// <param name="HandleCreationTime">Time spent issuing the already-validated lightweight <see cref="RecordIndex"/> handle.<br/></param>
    public sealed record VirtualPropertyIndexEnsureDiagnostics(
        TimeSpan TotalTime,
        TimeSpan ContractTime,
        TimeSpan PopulationPreparationTime,
        TimeSpan ReaderPreparationTime,
        TimeSpan MutationGateTime,
        TimeSpan RegistrationPreparationTime,
        TimeSpan PhysicalDefinitionTime,
        TimeSpan RuntimeAttachmentTime,
        TimeSpan PopulationTime,
        TimeSpan CompletionTime,
        TimeSpan HandleCreationTime)
    {
        /// <summary>Gets the sum of every explicitly timed non-overlapping phase.<br/></summary>
        public TimeSpan AccountedTime =>
            ContractTime +
            PopulationPreparationTime +
            ReaderPreparationTime +
            MutationGateTime +
            RegistrationPreparationTime +
            PhysicalDefinitionTime +
            RuntimeAttachmentTime +
            PopulationTime +
            CompletionTime +
            HandleCreationTime;

        /// <summary>Gets scheduler, call-boundary, and measurement remainder not attributed to an explicit phase.<br/></summary>
        public TimeSpan OtherTime => TotalTime > AccountedTime ? TotalTime - AccountedTime : TimeSpan.Zero;
    }
}
