using System;
using System.Collections.Generic;
using System.Threading;
using Inheto;
using LibraDex;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Owns one synchronously prepared, memory-backed complete order without exposing its LibraDex implementation.<br/>
        /// Construction creates only the private backend; <see cref="Prepare"/> performs the complete source walk on the caller's thread and never publishes partial identities.<br/>
        /// The owner snapshots the final identities and disposes this temporary backend before ordinary reader iteration begins.<br/>
        /// </summary>
        internal sealed class PreparedSortState : IDisposable
        {
            private const string ResultGroupName = "prepared-sort";
            private const string ResultIndexName = "order";
            private readonly RecordStore<T> store;
            private readonly ReaderSourcePlan sourcePlan;
            private readonly PreparedOrderKey[] keys;
            private readonly Catalog catalog;
            private readonly CatalogIdentityGroupIndexes group;
            private readonly LibraDexRoutedCompositeIndex? compositeIndex;
            private readonly LibraDexStringScalar8Index? stringIndex;
            private readonly HashSet<ulong>? acceptedIdentities;
            private readonly List<(LibraDexCompositeKey Key, object Identity)> bufferedCompositeEntries = new();
            private readonly List<(string? Key, ulong Identity)> bufferedStringEntries = new();
            private long matchedCount;
            private bool prepared;
            private bool disposed;

            /// <summary>
            /// Creates one private property-order backend over an independent fork of a reader's original selection without consuming that selection.<br/>
            /// </summary>
            /// <param name="store">Owning Abraxas store.<br/></param>
            /// <param name="sourcePlan">Replayable reader selection to consume independently.<br/></param>
            /// <param name="sort">One or more exact property-path order descriptors.<br/></param>
            internal PreparedSortState(RecordStore<T> store, ReaderSourcePlan sourcePlan, SortBy[] sort)
                : this(store, CreatePropertyKeys(store, sort), sourcePlan)
            {
            }

            /// <summary>
            /// Creates one private calculated-order backend over an independent fork of a reader's original selection without consuming that selection.<br/>
            /// </summary>
            /// <param name="store">Owning Abraxas store.<br/></param>
            /// <param name="sourcePlan">Replayable reader selection to consume independently.<br/></param>
            /// <param name="order">One or more durable selector ordering descriptors.<br/></param>
            internal PreparedSortState(RecordStore<T> store, ReaderSourcePlan sourcePlan, IndexSelectorBuilder[] order)
                : this(store, CreateCalculatedKeys(store, order), sourcePlan)
            {
            }

            /// <summary>
            /// Initializes the common prepared result engine from already validated exact key readers.<br/>
            /// </summary>
            /// <param name="store">Owning Abraxas store.<br/></param>
            /// <param name="keys">Validated property or calculated key readers.<br/></param>
            /// <param name="sourcePlan">The logical source selection to order; its complete-population flag determines whether candidate-identity filtering is needed.<br/></param>
            private PreparedSortState(
                RecordStore<T> store,
                PreparedOrderKey[] keys,
                ReaderSourcePlan sourcePlan)
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                this.sourcePlan = sourcePlan ?? throw new ArgumentNullException(nameof(sourcePlan));
                this.keys = keys ?? throw new ArgumentNullException(nameof(keys));
                acceptedIdentities = sourcePlan.IsCompleteStorePopulation ? null : new HashSet<ulong>();
                catalog = Catalog.CreateMemory(CatalogOptions.UInt64Identities);
                try
                {
                    group = catalog.Indexes.IndexSet(ResultGroupName);
                    if (keys.Length == 1 && keys[0].KeyType == typeof(string))
                    {
                        compositeIndex = null;
                        stringIndex = group.Define(ResultIndexName).String.Create(
                            stringKeys: StringKeys.Exact,
                            directions: LibraDexProjectionDirectionSet.Forward,
                            sortOrder: LibraDexIndexSortOrder.Ascending,
                            identityLookupMode: IdentityLookupMode.Disabled);
                    }
                    else
                    {
                        stringIndex = null;
                        LibraDexCompositeKeyPartSpec[] parts = CreateParts(keys);
                        compositeIndex = (LibraDexRoutedCompositeIndex)group
                            .Composite<ulong>(ResultIndexName, parts)
                            .Create(
                                IndexKeys.NonUnique,
                                new IndexOptions
                                {
                                    Keys = IndexKeys.NonUnique,
                                    IdentityKeyMultiplicity = IdentityKeyMultiplicity.SingleKeyPerIdentity
                                });
                    }
                }
                catch
                {
                    catalog.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Releases the temporary memory catalog and every accumulated sort key.<br/>
            /// Disposal before or after preparation never publishes the private partial construction state.<br/>
            /// </summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                catalog.Dispose();
            }

            /// <summary>
            /// Synchronously evaluates the complete selected identity source, reads each surviving payload once, and buffers its complete sort tuple for one detached index build.<br/>
            /// The completed tuple set is published to the private memory index once, avoiding cumulative per-record routed-root rewrites while preserving one authoritative LibraDex ordering implementation.<br/>
            /// Cancellation discards the private state through the owner's retry boundary; this method never exposes a partial ordering.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation checked between source rows, after each payload evaluation, throughout detached index construction, and immediately before publication.<br/></param>
            internal void Prepare(CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                if (prepared)
                    return;

                using ReaderCore forkedSource = sourcePlan.OpenBeginning();
                while (forkedSource.Read(requireBinary: false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AcceptIdentity(forkedSource.ID, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                int selectedCount;
                if (stringIndex is not null)
                {
                    selectedCount = bufferedStringEntries.Count;
                    matchedCount = PopulateStringIndex(cancellationToken);
                }
                else
                {
                    selectedCount = bufferedCompositeEntries.Count;
                    matchedCount = compositeIndex!.ReplaceFromUnordered(bufferedCompositeEntries, cancellationToken);
                }

                if (matchedCount != selectedCount)
                {
                    throw new InvalidDataException(
                        $"Prepared ordering retained {matchedCount:N0} distinct tuples from {selectedCount:N0} selected records.");
                }
                bufferedCompositeEntries.Clear();
                bufferedStringEntries.Clear();
                prepared = true;
            }

            /// <summary>Reads one distinct authoritative payload and buffers its complete ordered tuple for the later detached index build.<br/></summary>
            /// <param name="identity">Store-owned identity selected by the independent reader fork.<br/></param>
            /// <param name="cancellationToken">Cancellation checked after payload evaluation and before retaining the tuple.<br/></param>
            private void AcceptIdentity(ulong identity, CancellationToken cancellationToken)
            {
                if (acceptedIdentities is not null && !acceptedIdentities.Add(identity))
                    return;
                if (!store.rbase.data.TryGetOne(identity, out var record))
                    return;

                object?[] values;
                using (record)
                {
                    InhetoBinary payload = store.CreateRecordBinary(identity, record.Data);
                    values = ReadSortValues(payload);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                if (stringIndex is not null)
                {
                    if (values[0] is not null && values[0] is not string)
                    {
                        throw new InvalidDataException(
                            $"Prepared exact-string ordering received a runtime value of type '{values[0]!.GetType().FullName}'.");
                    }
                    bufferedStringEntries.Add(((string?)values[0], identity));
                }
                else
                {
                    bufferedCompositeEntries.Add((LibraDexCompositeKey.Of(values), identity));
                }
            }

            /// <summary>
            /// Populates the specialized one-key exact-string backend through LibraDex's native empty-root `VS8` builder.<br/>
            /// Repeated display strings are encoded once per distinct ordinal value, sorted as scalar identity tuples, and packed into ordinary or exhausted-key terminal routes without per-record mutation publication.<br/>
            /// Cancellation or build failure discards this private prepared-sort catalog at the caller boundary; no partially prepared identity order is exposed to reader iteration.<br/>
            /// </summary>
            /// <param name="cancellationToken">Cancellation checked throughout encoding, validation, sorting, native topology construction, and key-state population.<br/></param>
            /// <returns>The number of distinct key/identity tuples retained by the private VS8 index.<br/></returns>
            private long PopulateStringIndex(CancellationToken cancellationToken)
            {
                LibraDexStringScalar8Index target = stringIndex
                    ?? throw new InvalidOperationException("The prepared exact-string backend was not initialized.");
                return target.ReplaceExactFromUnordered(bufferedStringEntries, cancellationToken);
            }

            /// <summary>Captures every identity from the completed private ordering as one detached immutable array.<br/></summary>
            /// <returns>Final ordered identities suitable for ordinary reader iteration.<br/></returns>
            internal ulong[] SnapshotIdentities()
            {
                ThrowIfDisposed();
                if (!prepared)
                    throw new InvalidOperationException("The stable order must be prepared before identities can be captured.");
                var values = new List<ulong>(checked((int)Math.Min(matchedCount, int.MaxValue)));
                IIndex target = stringIndex is not null ? stringIndex : compositeIndex!;
                QueryDirection direction = stringIndex is not null && keys[0].SortOrder == LibraDexIndexSortOrder.Descending
                    ? QueryDirection.Descending
                    : QueryDirection.Ascending;
                foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(target, direction))
                    values.Add(ToRecordId(tuple.Identity));
                return values.ToArray();
            }

            /// <summary>Reads and normalizes every declared property key from one already-open authoritative payload.<br/></summary>
            /// <param name="payload">Current Fractal/Inheto payload.<br/></param>
            /// <returns>Composite key values in descriptor priority order.<br/></returns>
            private object?[] ReadSortValues(InhetoBinary payload)
            {
                var values = new object?[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    (bool hasValue, object? value) = keys[i].Read(payload);
                    values[i] = NormalizeKeyValue(hasValue ? value : null, keys[i].KeyType);
                }
                return values;
            }

            /// <summary>Validates property ordering and creates one exact payload reader for each descriptor.<br/></summary>
            /// <param name="store">Owning store supplying direct-path value semantics.<br/></param>
            /// <param name="sort">Developer-supplied order descriptors.<br/></param>
            /// <returns>Validated exact key readers detached from caller array mutation.<br/></returns>
            private static PreparedOrderKey[] CreatePropertyKeys(RecordStore<T> store, SortBy[] sort)
            {
                ArgumentNullException.ThrowIfNull(store);
                ArgumentNullException.ThrowIfNull(sort);
                if (sort.Length == 0)
                    throw new ArgumentException("A sorted reader requires at least one sort descriptor.", nameof(sort));

                SortBy[] copy = (SortBy[])sort.Clone();
                var keys = new PreparedOrderKey[copy.Length];
                for (int i = 0; i < copy.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(copy[i].Key.Path))
                        throw new ArgumentException($"Sort descriptor {i} has no property path.", nameof(sort));
                    if (copy[i].IgnoreCase)
                    {
                        throw new NotSupportedException(
                            "Prepared property ordering does not accept an ambiguous case-fold flag; use the culture and CompareOptions OpenSorted overload so Abraxas builds an exact sort-key selector.");
                    }

                    string path = copy[i].Key.Path;
                    if (!TryResolveSimplePropertyType(path, out Type declaredType))
                        throw new NotSupportedException($"Prepared ordering cannot resolve property path '{path}' on '{typeof(T).FullName}'.");
                    Type keyType = NormalizeValueType(declaredType);
                    ValidateKeyType(path, declaredType, keyType);
                    keys[i] = new PreparedOrderKey(
                        keyType,
                        copy[i].Descending ? LibraDexIndexSortOrder.Descending : LibraDexIndexSortOrder.Ascending,
                        payload =>
                        {
                            bool hasValue = store.TryReadLegacyAggregateValue(payload, path, out object? value);
                            return (hasValue, value);
                        });
                }
                return keys;
            }

            /// <summary>
            /// Validates calculated selector ordering and compiles each executable semantic reader once.<br/>
            /// Durable selectors and runtime-only <c>Selector.External</c> projections share preparation, while only durable selectors remain eligible for persistent index matching.<br/>
            /// </summary>
            /// <param name="store">Owning store supplying deserialization registrations.<br/></param>
            /// <param name="order">Developer-supplied selector ordering descriptors.<br/></param>
            /// <returns>Validated exact key readers detached from caller array mutation.<br/></returns>
            private static PreparedOrderKey[] CreateCalculatedKeys(RecordStore<T> store, IndexSelectorBuilder[] order)
            {
                ArgumentNullException.ThrowIfNull(store);
                ArgumentNullException.ThrowIfNull(order);
                if (order.Length == 0)
                    throw new ArgumentException("A sorted reader requires at least one selector descriptor.", nameof(order));

                IndexSelectorBuilder[] copy = (IndexSelectorBuilder[])order.Clone();
                var keys = new PreparedOrderKey[copy.Length];
                for (int i = 0; i < copy.Length; i++)
                {
                    IndexSelectorBuilder builder = copy[i] ?? throw new ArgumentException($"Selector descriptor {i} is null.", nameof(order));
                    if (builder.CaseType != IndexCaseType.None)
                    {
                        throw new NotSupportedException(
                            "Prepared calculated ordering requires text comparison semantics to be represented by an explicit selector transformation such as SortKey.");
                    }

                    if (builder.CalculatedSelector is not ISelectorStructure { ResultType: Type resultType })
                        throw new NotSupportedException($"Calculated ordering selector '{builder.CalculatedSelector}' does not expose an exact scalar result type.");

                    Type keyType = NormalizeValueType(resultType);
                    string diagnosticName = builder.CalculatedSelector.ToString() ?? "<calculated selector>";
                    ValidateKeyType(diagnosticName, resultType, keyType);
                    if (!store.TryCreateStoreSelectorReader(builder.CalculatedSelector, out var read))
                        throw new NotSupportedException($"Calculated ordering selector '{diagnosticName}' cannot be evaluated from authoritative Inheto payloads.");

                    keys[i] = new PreparedOrderKey(
                        keyType,
                        builder.Sort == IndexSortDirection.Descending ? LibraDexIndexSortOrder.Descending : LibraDexIndexSortOrder.Ascending,
                        read);
                }
                return keys;
            }

            /// <summary>Creates the exact typed LibraDex composite shape used by one prepared sort request.<br/></summary>
            /// <param name="keys">Validated property or calculated key contracts.<br/></param>
            /// <returns>Composite part descriptors with per-part ordering direction.<br/></returns>
            private static LibraDexCompositeKeyPartSpec[] CreateParts(PreparedOrderKey[] keys)
            {
                var parts = new LibraDexCompositeKeyPartSpec[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    parts[i] = new LibraDexCompositeKeyPartSpec(
                        $"key{i}",
                        keys[i].KeyType,
                        GetKeyFamily(keys[i].KeyType),
                        StringKeys.Exact,
                        GuidKeys.Exact,
                        DateKeys.Exact,
                        DateTimeKeyEncoding.PrecisionSdt,
                        keys[i].SortOrder);
                }
                return parts;
            }

            /// <summary>Rejects any result type whose exact ordered representation is not shared by Abraxas and LibraDex.<br/></summary>
            /// <param name="name">Developer-facing property or selector identity used by the exception.<br/></param>
            /// <param name="declaredType">Original nullable or enum-aware result type.<br/></param>
            /// <param name="keyType">Normalized physical key type.<br/></param>
            private static void ValidateKeyType(string name, Type declaredType, Type keyType)
            {
                if (IsSupportedLibraDexScalarKeyType(keyType) || keyType == typeof(byte[]))
                    return;
                throw new NotSupportedException(
                    $"Prepared ordering cannot represent '{name}' of type '{declaredType.FullName}' with an exact LibraDex key contract.");
            }

            /// <summary>Maps an Abraxas runtime value type to its exact LibraDex composite key family.<br/></summary>
            /// <param name="keyType">Normalized non-nullable key type.<br/></param>
            /// <returns>The corresponding LibraDex key family.<br/></returns>
            private static CatalogIndexKeyFamily GetKeyFamily(Type keyType)
            {
                if (keyType == typeof(string))
                    return CatalogIndexKeyFamily.String;
                if (keyType == typeof(byte[]))
                    return CatalogIndexKeyFamily.Blob;
                if (keyType == typeof(Guid))
                    return CatalogIndexKeyFamily.Guid;
                if (keyType == typeof(DateTime) ||
                    keyType == typeof(DateTimeOffset) ||
                    keyType == typeof(DateOnly) ||
                    keyType == typeof(TimeOnly) ||
                    keyType == typeof(TimeSpan))
                {
                    return CatalogIndexKeyFamily.Date;
                }
                return CatalogIndexKeyFamily.Scalar;
            }

            /// <summary>Converts enum values to their declared LibraDex scalar representation while preserving null state.<br/></summary>
            /// <param name="value">Current payload value or null.<br/></param>
            /// <param name="keyType">Normalized target key type.<br/></param>
            /// <returns>A value accepted by the exact composite part contract.<br/></returns>
            private static object? NormalizeKeyValue(object? value, Type keyType)
            {
                if (value is null || keyType.IsInstanceOfType(value))
                    return value;
                if (value.GetType().IsEnum)
                    return Convert.ChangeType(value, keyType, System.Globalization.CultureInfo.InvariantCulture);
                return value;
            }

            /// <summary>Throws after result disposal so callers cannot access a released memory catalog.<br/></summary>
            private void ThrowIfDisposed()
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(PreparedSortState));
            }

            /// <summary>Captures one exact key type, physical direction, and allocation-stable payload reader.<br/></summary>
            /// <param name="KeyType">Normalized LibraDex key type.<br/></param>
            /// <param name="SortOrder">Per-part physical comparison direction.<br/></param>
            /// <param name="Read">Compiled current-payload key reader.<br/></param>
            private readonly record struct PreparedOrderKey(
                Type KeyType,
                LibraDexIndexSortOrder SortOrder,
                Func<InhetoBinary, (bool HasValue, object? Value)> Read);
        }
    }
}
