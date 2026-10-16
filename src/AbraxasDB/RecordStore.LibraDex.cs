using LibraDex;
using Inheto;
using System.Linq.Expressions;
using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Buffers;

namespace AbraxasDB
{
    public partial class RecordStore<T> : ILibraDexRebindableRecordStore
    {
        private const int CurrentTextPopulationRevision = 1;
        private readonly object libraDexIndexLock = new();
        private readonly Catalog libraDexVolatileCatalog = Catalog.CreateMemory(CatalogOptions.UInt64Identities);
        private CatalogIdentityGroupIndexes? libraDexIdentityIndexes;
        private LibraDexIndexBackendMode libraDexBackendMode;
        private LibraDexSimpleIndex[] libraDexSimpleIndexes = Array.Empty<LibraDexSimpleIndex>();
        private LibraDexRegexProjectionIndex[] libraDexRegexProjectionIndexes = Array.Empty<LibraDexRegexProjectionIndex>();
        private LibraDexIndex<long, ulong>? libraDexStateIndex;
        private LibraDexIndex<ulong, ulong>? libraDexAppliedSourceUsnIndex;
        private LibraDexIndex<byte[], ulong>? libraDexPendingStateIntentIndex;
        private LibraDexIndexBackendMode detachedLibraDexBackendMode = LibraDexIndexBackendMode.Preferred;
        private const string LibraDexStateIndexName = "__abx_state";
        private const string LibraDexAppliedSourceUsnIndexName = "__abx_applied_source_usn";
        private const string LibraDexPendingStateIntentIndexName = "__abx_pending_state_intent";

        /// <summary>
        /// Gets the store-local policy used when a newly created ordinary LibraDex index is populated from existing Fractal records.<br/>
        /// The default optimized mode keeps payload and tuple memory bounded, while legacy mode preserves the former whole-population preparation path for diagnostics and A/B comparisons.<br/>
        /// Existing-index repair continues to use the fully prepared replacement path regardless of this setting so a key-read failure cannot partially replace a previously usable tuple set.<br/>
        /// </summary>
        public IndexBackfillOptions IndexBackfill { get; } = new();

        /// <summary>
        /// Gets measurements from the most recently completed ordinary simple-index population on this store facade.<br/>
        /// Callers should capture the value immediately after index creation because a later index operation replaces it.<br/>
        /// A null value means this facade has not completed an ordinary simple-index population in the current process.<br/>
        /// </summary>
        public IndexBackfillDiagnostics? LastIndexBackfillDiagnostics { get; private set; }

        private CatalogIdentityGroupIndexes LibraDexVolatileIndexes => libraDexVolatileCatalog.Indexes.IndexSet(indexGroupName);

        /// <summary>
        /// Registers one application-defined CLR type as an Inheto custom type and as a fixed-width LibraDex binary index key.<br/>
        /// The same encoder is used for persisted Inheto bytes, indexed keys, and condition operands so equality remains deterministic across storage and query execution.<br/>
        /// Valid fixed key widths are 8, 16, and 32 bytes; encoded values with any other length fail before index mutation.<br/>
        /// </summary>
        /// <typeparam name="TValue">The application CLR type being registered.<br/></typeparam>
        /// <param name="fixedKeyWidth">The exact LibraDex binary key width: 8, 16, or 32 bytes.<br/></param>
        /// <param name="toBytes">Converts one CLR value to its stable deterministic byte representation.<br/></param>
        /// <param name="fromBytes">Restores one CLR value from the bytes written by <paramref name="toBytes"/>.<br/></param>
        /// <param name="orderPreserving">Set to <c>true</c> only when lexicographic byte order intentionally matches the CLR value order.<br/></param>
        public void RegisterBinaryCodec<TValue>(
            int fixedKeyWidth,
            Func<TValue, byte[]> toBytes,
            BinaryDecoder<TValue> fromBytes,
            bool orderPreserving = false)
            => RegisterBinaryCodecCore(
                fixedKeyWidth,
                formatId: null,
                formatVersion: 0,
                toBytes,
                fromBytes,
                orderPreserving);

        /// <summary>
        /// Registers one application-defined CLR type with Inheto and assigns the stable Abraxas format identity required by persistent indexes.<br/>
        /// The format ID is ordinal, case-sensitive, and limited to 128 UTF-8 bytes; increment <paramref name="formatVersion"/> whenever the emitted binary meaning changes.<br/>
        /// The executable delegates remain process-local while Abraxas persists only format and per-index binding metadata in the owning RecordBase.<br/>
        /// </summary>
        /// <typeparam name="TValue">Application CLR value represented by the binary format.<br/></typeparam>
        /// <param name="fixedKeyWidth">Exact LibraDex binary key width: 8, 16, or 32 bytes.<br/></param>
        /// <param name="formatId">Stable application-owned binary format identity.<br/></param>
        /// <param name="formatVersion">Positive application-owned binary format version.<br/></param>
        /// <param name="toBytes">Converts one CLR value to its stable deterministic byte representation.<br/></param>
        /// <param name="fromBytes">Restores one CLR value from the bytes written by <paramref name="toBytes"/>.<br/></param>
        /// <param name="orderPreserving">Whether lexicographic bytes preserve the intended CLR value order.<br/></param>
        public void RegisterBinaryCodec<TValue>(
            int fixedKeyWidth,
            string formatId,
            int formatVersion,
            Func<TValue, byte[]> toBytes,
            BinaryDecoder<TValue> fromBytes,
            bool orderPreserving = false)
            => RegisterBinaryCodecCore(
                fixedKeyWidth,
                formatId,
                formatVersion,
                toBytes,
                fromBytes,
                orderPreserving);

        /// <summary>
        /// Installs one combined Inheto handler pair and publishes its optional Abraxas persistent-index format contract.<br/>
        /// </summary>
        private void RegisterBinaryCodecCore<TValue>(
            int fixedKeyWidth,
            string? formatId,
            int formatVersion,
            Func<TValue, byte[]> toBytes,
            BinaryDecoder<TValue> fromBytes,
            bool orderPreserving)
        {
            using var operation = EnterStoreOperationScope();
            ArgumentNullException.ThrowIfNull(toBytes);
            ArgumentNullException.ThrowIfNull(fromBytes);
            if (fixedKeyWidth is not (8 or 16 or 32))
                throw new ArgumentOutOfRangeException(nameof(fixedKeyWidth), fixedKeyWidth, "Custom LibraDex binary keys must be exactly 8, 16, or 32 bytes.");

            byte[] EncodeChecked(TValue value)
            {
                byte[]? bytes;
                try
                {
                    bytes = toBytes(value);
                }
                catch (IndexContractException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Codec,
                        $"The registered binary codec for '{typeof(TValue).FullName}' failed while encoding a value.",
                        indexName: typeof(TValue).FullName,
                        innerException: exception);
                }

                if (bytes is null)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Codec,
                        $"The registered binary codec for '{typeof(TValue).FullName}' returned null.",
                        indexName: typeof(TValue).FullName);
                }
                if (bytes.Length != fixedKeyWidth)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Codec,
                        $"The registered binary codec for '{typeof(TValue).FullName}' returned {bytes.Length} bytes; its fixed key width is {fixedKeyWidth}.",
                        indexName: typeof(TValue).FullName);
                }
                return bytes;
            }

            // Bind once, before registration mutation; decoding keeps the original method/target and call depth.
            var nativeDecoder = BinaryDecoderBinding.ToInheto(fromBytes);
            NativeSerializationOptions.AddTypeSerializer<TValue>(EncodeChecked);
            NativeDeserializationOptions.AddTypeDeserializer<TValue>(nativeDecoder);
            RegisterBinaryIndexEncodingCore(
                typeof(TValue),
                registrationPath: null,
                fixedKeyWidth,
                orderPreserving,
                formatId,
                formatVersion);
        }

        /// <summary>
        /// Adopts the type-wide binary serializer already registered in this store's Inheto options as a fixed-width ordered-index representation.<br/>
        /// The developer's existing Inheto handler remains the single runtime-object conversion source; Abraxas reads its emitted payload directly from serialized records and does not deserialize then reserialize custom values for LibraDex.<br/>
        /// Valid fixed key widths are 8, 16, and 32 bytes; <paramref name="orderPreserving"/> should be enabled only when lexicographic byte order matches the custom value's intended order.<br/>
        /// </summary>
        /// <typeparam name="TValue">The application value type whose type-wide Inheto binary handler should be adopted.<br/></typeparam>
        /// <param name="fixedKeyWidth">The exact fixed binary key width: 8, 16, or 32 bytes.<br/></param>
        /// <param name="orderPreserving">Whether lexicographic encoded-byte order is a valid ordering for the application value.<br/></param>
        public void RegisterBinaryIndexEncoding<TValue>(int fixedKeyWidth, bool orderPreserving = false)
            => RegisterBinaryIndexEncodingCore(
                typeof(TValue),
                registrationPath: null,
                fixedKeyWidth,
                orderPreserving,
                formatId: null,
                formatVersion: 0);

        /// <summary>
        /// Adopts a type-wide Inheto binary serializer and assigns the stable Abraxas format contract required by persistent indexes.<br/>
        /// Multiple indexes using the same ordinal ID and positive version share one deduplicated persisted format-table entry.<br/>
        /// </summary>
        /// <typeparam name="TValue">Application value type handled by the existing Inheto serializer.<br/></typeparam>
        /// <param name="fixedKeyWidth">Exact fixed binary key width: 8, 16, or 32 bytes.<br/></param>
        /// <param name="formatId">Stable application-owned binary format identity, limited to 128 UTF-8 bytes.<br/></param>
        /// <param name="formatVersion">Positive binary format version.<br/></param>
        /// <param name="orderPreserving">Whether lexicographic encoded-byte order preserves application value order.<br/></param>
        public void RegisterBinaryIndexEncoding<TValue>(
            int fixedKeyWidth,
            string formatId,
            int formatVersion,
            bool orderPreserving = false)
            => RegisterBinaryIndexEncodingCore(
                typeof(TValue),
                registrationPath: null,
                fixedKeyWidth,
                orderPreserving,
                formatId,
                formatVersion);

        /// <summary>
        /// Adopts the property-specific binary serializer already registered in this store's Inheto options as one fixed-width ordered-index representation.<br/>
        /// Property-path registration takes precedence over a type-wide index encoding for the same CLR type, matching Inheto's normal member-before-type serializer resolution.<br/>
        /// This overload supports applications that intentionally encode the same CLR type differently at different record paths without registering duplicate Abraxas conversion delegates.<br/>
        /// </summary>
        /// <typeparam name="TValue">The application value type resolved at the selected property path.<br/></typeparam>
        /// <param name="key">The exact record property path whose Inheto binary handler should be adopted.<br/></param>
        /// <param name="fixedKeyWidth">The exact fixed binary key width: 8, 16, or 32 bytes.<br/></param>
        /// <param name="orderPreserving">Whether lexicographic encoded-byte order is a valid ordering for the application value.<br/></param>
        public void RegisterBinaryIndexEncoding<TValue>(PropPath key, int fixedKeyWidth, bool orderPreserving = false)
        {
            var path = PropPath.NormalizePath(key.Path);
            if (!TryResolveSimplePropertyType(path, out var propertyType) ||
                NormalizeValueType(propertyType) != NormalizeValueType(typeof(TValue)))
            {
                throw new ArgumentException(
                    $"Property path '{path}' does not resolve to '{typeof(TValue).FullName}' on '{typeof(T).FullName}'.",
                    nameof(key));
            }

            RegisterBinaryIndexEncodingCore(
                typeof(TValue),
                path,
                fixedKeyWidth,
                orderPreserving,
                formatId: null,
                formatVersion: 0);
        }

        /// <summary>
        /// Adopts a property-specific Inheto serializer and assigns its stable Abraxas persistent-index format contract.<br/>
        /// Member-specific registration retains precedence over type-wide registration while its format identity is validated independently.<br/>
        /// </summary>
        /// <typeparam name="TValue">Application value type resolved at the selected property path.<br/></typeparam>
        /// <param name="key">Exact record property path owning the member-specific Inheto handler.<br/></param>
        /// <param name="fixedKeyWidth">Exact fixed binary key width: 8, 16, or 32 bytes.<br/></param>
        /// <param name="formatId">Stable application-owned binary format identity, limited to 128 UTF-8 bytes.<br/></param>
        /// <param name="formatVersion">Positive binary format version.<br/></param>
        /// <param name="orderPreserving">Whether lexicographic encoded-byte order preserves application value order.<br/></param>
        public void RegisterBinaryIndexEncoding<TValue>(
            PropPath key,
            int fixedKeyWidth,
            string formatId,
            int formatVersion,
            bool orderPreserving = false)
        {
            var path = PropPath.NormalizePath(key.Path);
            if (!TryResolveSimplePropertyType(path, out var propertyType) ||
                NormalizeValueType(propertyType) != NormalizeValueType(typeof(TValue)))
            {
                throw new ArgumentException(
                    $"Property path '{path}' does not resolve to '{typeof(TValue).FullName}' on '{typeof(T).FullName}'.",
                    nameof(key));
            }

            RegisterBinaryIndexEncodingCore(
                typeof(TValue),
                path,
                fixedKeyWidth,
                orderPreserving,
                formatId,
                formatVersion);
        }

        /// <summary>
        /// Validates and publishes one type-wide or property-specific Inheto-to-index binary adaptation.<br/>
        /// The hot serialized-record path borrows the stored fixed-width payload and passes it directly to LibraDex's synchronous span contract; it does not invoke either registered handler or create a standalone key array.<br/>
        /// Runtime condition operands invoke the same effective Inheto serializer once, preserving property-before-type precedence and deterministic equality.<br/>
        /// </summary>
        /// <param name="valueType">The normalized application value type represented by the binary key.<br/></param>
        /// <param name="registrationPath">The optional exact property path for a member-specific handler, or <c>null</c> for a type-wide handler.<br/></param>
        /// <param name="fixedKeyWidth">The required 8-, 16-, or 32-byte key width.<br/></param>
        /// <param name="orderPreserving">Whether lexicographic byte order preserves application value order.<br/></param>
        /// <param name="formatId">Optional stable application-codec format identity used to validate persistent index compatibility.<br/></param>
        /// <param name="formatVersion">The application-codec format version paired with the format identity.<br/></param>
        private void RegisterBinaryIndexEncodingCore(
            Type valueType,
            string? registrationPath,
            int fixedKeyWidth,
            bool orderPreserving,
            string? formatId,
            int formatVersion)
        {
            if (fixedKeyWidth is not (8 or 16 or 32))
                throw new ArgumentOutOfRangeException(nameof(fixedKeyWidth), fixedKeyWidth, "Custom binary index keys must be exactly 8, 16, or 32 bytes.");
            ValidateApplicationCodecFormat(formatId, formatVersion);
            if (!NativeSerializationOptions.HasBinarySerializer(valueType, registrationPath))
            {
                throw new InvalidOperationException(
                    registrationPath is null
                        ? $"Inheto has no type-wide binary serializer registered for '{valueType.FullName}'."
                        : $"Inheto has no effective binary serializer registered for '{registrationPath}' as '{valueType.FullName}'.");
            }

            byte[] EncodeChecked(object value, string keyPath)
            {
                byte[]? bytes;
                try
                {
                    if (!NativeSerializationOptions.TrySerializeBinary(value, valueType, keyPath, out bytes))
                    {
                        throw new InvalidOperationException(
                            $"Inheto has no effective binary serializer for '{keyPath}' as '{valueType.FullName}'.");
                    }
                }
                catch (IndexContractException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Codec,
                        $"The registered Inheto binary serializer for '{valueType.FullName}' failed while encoding an index value.",
                        indexName: valueType.FullName,
                        keyPath: keyPath,
                        innerException: exception);
                }

                ValidateCustomBinaryWidth(bytes, valueType, keyPath, fixedKeyWidth);
                return bytes;
            }

            bool TryReadPayload(InhetoBinary binary, string keyPath, out ReadOnlyMemory<byte> payload)
            {
                if (!binary.TryReadCustomBinaryPayload(keyPath, out payload))
                {
                    payload = default;
                    return false;
                }

                if (payload.Length != fixedKeyWidth)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Codec,
                        $"The stored Inheto binary payload for '{valueType.FullName}' contains {payload.Length} bytes; its fixed index width is {fixedKeyWidth}.",
                        indexName: valueType.FullName,
                        keyPath: keyPath);
                }

                return true;
            }

            bool TryReadKey(InhetoBinary binary, string keyPath, out object key)
            {
                if (!TryReadPayload(binary, keyPath, out ReadOnlyMemory<byte> payload))
                {
                    key = default!;
                    return false;
                }

                key = payload.ToArray();
                return true;
            }

            var codec = new CustomBinaryCodec(
                valueType,
                registrationPath,
                fixedKeyWidth,
                orderPreserving,
                formatId,
                formatVersion,
                EncodeChecked,
                TryReadKey,
                TryReadPayload);

            var replaced = false;
            lock (libraDexIndexLock)
            {
                var current = customBinaryCodecs;
                for (var i = 0; i < current.Length; i++)
                {
                    if (current[i].ValueType != valueType ||
                        !string.Equals(current[i].RegistrationPath, registrationPath, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!current[i].HasSamePersistentContract(codec) &&
                        HasAttachedPersistentIndexForCodec(valueType, registrationPath))
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.CodecIdentity,
                            registrationPath is null
                                ? $"The binary index encoding for '{valueType.FullName}' cannot change while a persistent index using it is attached."
                                : $"The binary index encoding for '{registrationPath}' cannot change while its persistent index is attached.",
                            registrationPath ?? valueType.FullName,
                            registrationPath);
                    }

                    var next = (CustomBinaryCodec[])current.Clone();
                    next[i] = codec;
                    customBinaryCodecs = next;
                    replaced = true;
                    break;
                }

                if (!replaced)
                {
                    var appended = new CustomBinaryCodec[current.Length + 1];
                    Array.Copy(current, appended, current.Length);
                    appended[current.Length] = codec;
                    customBinaryCodecs = appended;
                }
            }

            AttachPersistedOrdinaryIndexesForRegisteredCodec(codec);
        }

        /// <summary>
        /// Validates the optional application-owned format identity attached to one runtime codec registration.<br/>
        /// Unversioned registrations remain valid for volatile indexes; persistent creation later rejects them explicitly.<br/>
        /// </summary>
        /// <param name="formatId">Ordinal stable format identity, or <c>null</c> for a runtime-only registration.<br/></param>
        /// <param name="formatVersion">Positive version when an identity is supplied; otherwise zero.<br/></param>
        private static void ValidateApplicationCodecFormat(string? formatId, int formatVersion)
        {
            if (formatId is null)
            {
                if (formatVersion != 0)
                    throw new ArgumentOutOfRangeException(nameof(formatVersion), formatVersion, "An unversioned runtime codec must use format version zero.");
                return;
            }

            if (string.IsNullOrWhiteSpace(formatId))
                throw new ArgumentException("Application codec format ID cannot be empty.", nameof(formatId));
            if (!string.Equals(formatId, formatId.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("Application codec format ID cannot contain leading or trailing whitespace.", nameof(formatId));
            if (Encoding.UTF8.GetByteCount(formatId) > ApplicationCodecIndexBindingEntry.MaximumFormatIdUtf8Bytes)
            {
                throw new ArgumentException(
                    $"Application codec format ID cannot exceed {ApplicationCodecIndexBindingEntry.MaximumFormatIdUtf8Bytes} UTF-8 bytes.",
                    nameof(formatId));
            }
            if (formatVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(formatVersion), formatVersion, "Application codec format version must be a positive integer.");
        }

        /// <summary>
        /// Reports whether any currently published persistent ordinary or partial index depends on the selected runtime codec slot.<br/>
        /// The conservative setup-time check prevents hot condition encoding from changing while maintained tuples still use the prior contract.<br/>
        /// </summary>
        private bool HasAttachedPersistentIndexForCodec(Type valueType, string? registrationPath)
        {
            var simple = libraDexSimpleIndexes;
            for (var i = 0; i < simple.Length; i++)
            {
                if (simple[i].OwnerCatalog is null &&
                    simple[i].KeyType == valueType &&
                    (registrationPath is null ||
                     string.Equals(simple[i].Path, registrationPath, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            var partial = libraDexPartialIndexes;
            for (var i = 0; i < partial.Length; i++)
            {
                if (partial[i].OwnerCatalog is null && partial[i].KeyType == valueType &&
                    (registrationPath is null ||
                     string.Equals(partial[i].KeyPath, registrationPath, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Validates the exact fixed-width result produced by an adopted Inheto binary serializer.<br/>
        /// Null is rejected by the caller before this helper; zero length remains an explicit empty payload and therefore fails every supported fixed-width custom index contract rather than being mistaken for a missing value.<br/>
        /// </summary>
        /// <param name="bytes">The handler-owned binary result to validate.<br/></param>
        /// <param name="valueType">The custom CLR type represented by the bytes.<br/></param>
        /// <param name="keyPath">The property path whose effective handler produced the bytes.<br/></param>
        /// <param name="fixedKeyWidth">The exact configured fixed width.<br/></param>
        private static void ValidateCustomBinaryWidth(byte[] bytes, Type valueType, string keyPath, int fixedKeyWidth)
        {
            if (bytes.Length == fixedKeyWidth)
                return;

            throw new IndexContractException(
                IndexContractFailureKind.Codec,
                $"The registered Inheto binary serializer for '{valueType.FullName}' returned {bytes.Length} bytes; its fixed index width is {fixedKeyWidth}.",
                indexName: valueType.FullName,
                keyPath: keyPath);
        }

        /// <summary>
        /// Creates the complete durable Abraxas binding expected for one custom-codec ordinary or partial index.<br/>
        /// Runtime-only codec registrations fail here only when a persistent index actually attempts to use them, preserving ephemeral-index flexibility.<br/>
        /// </summary>
        private ApplicationCodecIndexBindingEntry CreateApplicationCodecBinding(
            CustomBinaryCodec codec,
            ApplicationCodecIndexKind kind,
            string logicalName,
            string physicalName,
            string keyPath,
            Type keyType,
            bool ready)
        {
            if (!codec.IsPersistent)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.CodecIdentity,
                    $"Persistent index '{logicalName}' uses application type '{keyType.FullName}', but its binary index encoding has no stable format ID and version.",
                    logicalName,
                    keyPath);
            }

            return new ApplicationCodecIndexBindingEntry
            {
                StoreID = storeID,
                Kind = kind,
                LogicalName = logicalName,
                PhysicalName = physicalName,
                KeyPath = keyPath,
                KeyTypeCode = ConditionWireTypes.GetCode(keyType),
                FormatId = codec.FormatId!,
                FormatVersion = codec.FormatVersion,
                FixedKeyWidth = codec.FixedKeyWidth,
                OrderPreserving = codec.OrderPreserving,
                Ready = ready
            };
        }

        /// <summary>
        /// Requires one existing durable codec binding to match the runtime format, type, path, width, ordering, and physical identity exactly.<br/>
        /// Missing legacy metadata and every mismatch fail before LibraDex attachment; preparing state is admitted only by the explicit authoritative-recovery path.<br/>
        /// </summary>
        /// <param name="expected">The complete runtime codec contract expected by the index.<br/></param>
        /// <param name="allowPreparing">Whether a matching preparing binding may attach solely for immediate authoritative rebuild.<br/></param>
        private void RequireReadyApplicationCodecBinding(
            ApplicationCodecIndexBindingEntry expected,
            bool allowPreparing = false)
        {
            if (!rbase.TryGetApplicationCodecBinding(
                storeID,
                expected.Kind,
                expected.LogicalName,
                out var persisted))
            {
                throw new IndexContractException(
                    IndexContractFailureKind.CodecIdentity,
                    $"Persistent index '{expected.LogicalName}' has no application-codec identity binding. Remove and recreate or explicitly adopt this legacy index through Abraxas before use.",
                    expected.LogicalName,
                    expected.KeyPath);
            }
            if (!persisted.ContractEquals(expected, includeReady: false))
            {
                throw new IndexContractException(
                    IndexContractFailureKind.CodecIdentity,
                    $"Persistent index '{expected.LogicalName}' was created with application codec '{persisted.FormatId}' version {persisted.FormatVersion}, but runtime setup registered '{expected.FormatId}' version {expected.FormatVersion}.",
                    expected.LogicalName,
                    expected.KeyPath);
            }
            if (!persisted.Ready && !allowPreparing)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.CodecIdentity,
                    $"Persistent index '{expected.LogicalName}' has an incomplete application-codec binding and cannot be attached.",
                    expected.LogicalName,
                    expected.KeyPath);
            }
        }

        /// <summary>
        /// Persists preparing intent for a new physical index without overwriting a contradictory orphaned or interrupted contract.<br/>
        /// A matching prior entry may be resumed, but every format, type, path, width, ordering, and physical-name mismatch fails closed.<br/>
        /// </summary>
        private void PersistPreparingApplicationCodecBinding(
            ApplicationCodecIndexBindingEntry expected)
        {
            if (rbase.TryGetApplicationCodecBinding(
                    storeID,
                    expected.Kind,
                    expected.LogicalName,
                    out var persisted) &&
                !persisted.ContractEquals(expected, includeReady: false))
            {
                throw new IndexContractException(
                    IndexContractFailureKind.CodecIdentity,
                    $"Persistent index '{expected.LogicalName}' has an existing application-codec binding that conflicts with runtime setup.",
                    expected.LogicalName,
                    expected.KeyPath);
            }

            expected.Ready = false;
            rbase.UpsertApplicationCodecBinding(expected);
        }

        /// <summary>
        /// Writes preparing or ready application-codec intent for one index after validating the complete runtime contract.<br/>
        /// </summary>
        private void PersistApplicationCodecBinding(
            CustomBinaryCodec codec,
            ApplicationCodecIndexKind kind,
            string logicalName,
            string physicalName,
            string keyPath,
            Type keyType,
            bool ready)
            => rbase.UpsertApplicationCodecBinding(
                CreateApplicationCodecBinding(
                    codec,
                    kind,
                    logicalName,
                    physicalName,
                    keyPath,
                    keyType,
                    ready));

        /// <summary>
        /// Attaches persisted ordinary indexes whose reflected property type exactly matches a newly registered custom codec.<br/>
        /// This is the post-open convenience path; persisted partial indexes use the earlier RecordBase store-setup callback because their condition and key contracts must rehydrate before the store is published.<br/>
        /// Catalog enumeration and descriptor publication occur only during registration and never add work or allocation to steady-state record mutation.<br/>
        /// </summary>
        /// <param name="codec">The immutable custom codec that has just been published to lock-free readers.<br/></param>
        private void AttachPersistedOrdinaryIndexesForRegisteredCodec(CustomBinaryCodec codec)
        {
            var indexes = libraDexIdentityIndexes;
            if (indexes is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return;

            CatalogIndexInfo[] catalogIndexes;
            try
            {
                catalogIndexes = indexes.List();
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Persisted indexes could not be inspected after registering the binary codec for '{codec.ValueType.FullName}'.",
                    indexName: codec.ValueType.FullName,
                    innerException: exception);
            }

            for (var i = 0; i < catalogIndexes.Length; i++)
            {
                var info = catalogIndexes[i];
                if (info.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                    rbase.IsOrdinaryIndexRemoved(storeID, info.Name) ||
                    (codec.RegistrationPath is not null &&
                        !string.Equals(codec.RegistrationPath, info.Name, StringComparison.Ordinal)) ||
                    !TryResolveSimplePropertyType(info.Name, out var keyType) ||
                    NormalizeValueType(keyType) != codec.ValueType)
                {
                    continue;
                }

                if (rbase.TryGetOrdinaryIndexIntent(storeID, info.Name, out var intent))
                {
                    _ = TryRestoreOrdinaryIndexIntent(intent, indexes);
                }
                else
                {
                    TryRegisterLibraDexSimplePathIndex(
                        info.Name,
                        info.KeyContract == IndexKeys.Unique,
                        indexes,
                        ownerCatalog: null,
                        populateExisting: false,
                        autoCreated: false,
                        AutoIndexUse.None,
                        manageIntent: false);
                }
            }
        }

        /// <summary>
        /// Attempts to encode a custom condition operand with the codec registered on this store.<br/>
        /// </summary>
        /// <typeparam name="TValue">The custom CLR operand type.<br/></typeparam>
        /// <param name="value">The materialized condition operand.<br/></param>
        /// <param name="propPath">The selected property path used for member-before-type Inheto handler resolution.<br/></param>
        /// <param name="key">The fixed-width LibraDex key when a codec is registered.<br/></param>
        /// <returns><c>true</c> when the value was encoded; otherwise <c>false</c>.<br/></returns>
        internal bool TryEncodeCustomIndexKey<TValue>(TValue value, string propPath, out byte[] key)
        {
            if (TryGetCustomBinaryCodec(typeof(TValue), propPath, out var codec))
            {
                key = codec.Encode(value!, propPath);
                return true;
            }

            key = default!;
            return false;
        }

        /// <summary>
        /// Resolves a registered custom binary codec by exact CLR type without reflection or delegate construction on record write/query paths.<br/>
        /// </summary>
        /// <param name="valueType">The exact custom CLR value type.<br/></param>
        /// <param name="propPath">The optional property path used to prefer a member-specific registration before the type-wide fallback.<br/></param>
        /// <param name="codec">The immutable codec descriptor when registered.<br/></param>
        /// <returns><c>true</c> when a codec was found; otherwise <c>false</c>.<br/></returns>
        private bool TryGetCustomBinaryCodec(Type valueType, string? propPath, out CustomBinaryCodec codec)
        {
            var codecs = customBinaryCodecs;
            if (propPath is not null)
            {
                for (var i = 0; i < codecs.Length; i++)
                {
                    if (codecs[i].ValueType == valueType &&
                        string.Equals(codecs[i].RegistrationPath, propPath, StringComparison.Ordinal))
                    {
                        codec = codecs[i];
                        return true;
                    }
                }
            }

            for (var i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].ValueType == valueType && codecs[i].RegistrationPath is null)
                {
                    codec = codecs[i];
                    return true;
                }
            }

            codec = default;
            return false;
        }
        /// <summary>
        /// Binds this store to a LibraDex identity group for supported internal query execution attempts.<br/>
        /// Passing <c>null</c> clears the binding and leaves query execution on the Fractal/Inheto scan path when no usable LibraDex index exists.<br/>
        /// </summary>
        /// <param name="indexes">The LibraDex identity group indexes that mirror this store's record identifiers, or <c>null</c> to clear the binding.<br/></param>
        /// <param name="mode">The backend selection mode used by public query calls after binding.<br/></param>
        internal void BindLibraDexIdentityIndexes(CatalogIdentityGroupIndexes? indexes, LibraDexIndexBackendMode mode)
        {
            if (!ReferenceEquals(libraDexIdentityIndexes, indexes))
            {
                libraDexStateIndex = null;
                libraDexAppliedSourceUsnIndex = null;
                libraDexPendingStateIntentIndex = null;
            }
            libraDexIdentityIndexes = indexes;
            libraDexBackendMode = indexes is null ? LibraDexIndexBackendMode.FractalOnly : mode;
            if (indexes is not null)
            {
                EnsureLibraDexStateIndex(indexes);
                EnsureLibraDexConvergenceIndexes(indexes);
                RegisterExistingLibraDexSimplePathIndexes(indexes);
                RegisterExistingLibraDexPartialIndexes();
                EnsureLibraDexConvergence(nameof(BindLibraDexIdentityIndexes));
            }
        }

        /// <summary>
        /// Gets the persistent LibraDex identity-group name used by this store.<br/>
        /// </summary>
        string ILibraDexRebindableRecordStore.LibraDexStoreName => name;

        /// <summary>
        /// Gets the durable store-generation identity captured by this facade.<br/>
        /// </summary>
        Guid ILibraDexRebindableRecordStore.StoreInstanceID => storeRuntime.InstanceID;

        /// <summary>
        /// Invalidates this facade after coordinated store removal and releases its process-local volatile index catalog.<br/>
        /// Persistent handles remain unusable because every later facade operation fails its shared generation check.<br/>
        /// </summary>
        /// <param name="state">The unavailable lifecycle state that invalidated the facade.<br/></param>
        void ILibraDexRebindableRecordStore.InvalidateStore(StoreLifecycleState state)
        {
            storeRuntime.Invalidate(state);
            Interlocked.Exchange(ref autoIndexTrimTimer, null)?.Dispose();
            libraDexVolatileCatalog.Dispose();
        }

        /// <summary>
        /// Detaches file-backed LibraDex handles before the owning RecordBase closes or replaces its catalog.<br/>
        /// Volatile simple and regex indexes retain their original memory-catalog handles; persistent regex intent is captured because its runtime projection settings are richer than the physical catalog name.<br/>
        /// </summary>
        void ILibraDexRebindableRecordStore.DetachPersistentLibraDexBinding()
        {
            lock (partialIndexLifecycleLock)
            lock (libraDexIndexLock)
            {
                detachedLibraDexBackendMode = libraDexBackendMode;
            }
        }

        /// <summary>
        /// Rebinds file-backed index handles after the owning RecordBase opens a replacement LibraDex catalog.<br/>
        /// Ordinary and partial indexes rehydrate through their existing catalog/Fractal intent paths; captured regex projection definitions reopen their exact physical string shapes without repopulating duplicate tuples.<br/>
        /// </summary>
        /// <param name="indexes">The reopened persistent identity group for this store.<br/></param>
        void ILibraDexRebindableRecordStore.RebindPersistentLibraDexBinding(CatalogIdentityGroupIndexes indexes)
        {
            ArgumentNullException.ThrowIfNull(indexes);
            var simpleIndexes = (LibraDexSimpleIndex[])libraDexSimpleIndexes.Clone();
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                if (simpleIndexes[i].OwnerCatalog is null)
                    simpleIndexes[i] = simpleIndexes[i].WithIndex(indexes.Index(simpleIndexes[i].Index.Name));
            }

            var regexIndexes = (LibraDexRegexProjectionIndex[])libraDexRegexProjectionIndexes.Clone();
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                if (regexIndexes[i].OwnerCatalog is null)
                    regexIndexes[i] = regexIndexes[i].WithIndex(indexes.Index(regexIndexes[i].Index.Name));
            }

            var partialIndexes = (LibraDexPartialIndex[])libraDexPartialIndexes.Clone();
            for (var i = 0; i < partialIndexes.Length; i++)
            {
                if (partialIndexes[i].OwnerCatalog is null)
                    partialIndexes[i] = partialIndexes[i].WithIndex(indexes.Index(partialIndexes[i].PhysicalName));
            }

            var stateIndex = OpenValidatedLibraDexStateIndex(indexes);
            var appliedSourceUsnIndex = OpenValidatedLibraDexAppliedSourceUsnIndex(indexes);
            var pendingStateIntentIndex = OpenValidatedLibraDexPendingStateIntentIndex(indexes);

            lock (partialIndexLifecycleLock)
            lock (libraDexIndexLock)
            {
                libraDexIdentityIndexes = indexes;
                libraDexBackendMode = detachedLibraDexBackendMode;
                libraDexStateIndex = stateIndex;
                libraDexAppliedSourceUsnIndex = appliedSourceUsnIndex;
                libraDexPendingStateIntentIndex = pendingStateIntentIndex;
                libraDexSimpleIndexes = simpleIndexes;
                libraDexRegexProjectionIndexes = regexIndexes;
                libraDexPartialIndexes = partialIndexes;
            }

            EnsureLibraDexConvergence(nameof(ILibraDexRebindableRecordStore.RebindPersistentLibraDexBinding));
            AdvanceMemoryPartialWatermarksAfterPersistentRebind();
        }

        /// <summary>
        /// Attempts to open a LibraDex identity cursor for a supported Abraxas condition.<br/>
        /// This method is deliberately allocation-light in the success path after descriptor translation: it returns LibraDex's native cursor directly instead of wrapping per-row delegates.<br/>
        /// </summary>
        /// <param name="where">The Abraxas condition supplied to the public query method.<br/></param>
        /// <param name="cursor">The opened cursor when this method returns <c>true</c>.<br/></param>
        /// <param name="plannedCondition">The translated condition used to open the cursor for optional plan diagnostics.<br/></param>
        /// <returns><c>true</c> when a bound LibraDex group and supported condition are available; otherwise <c>false</c>.</returns>
        private bool TryOpenLibraDexIdentityCursor(
            Condition<T>.EndCondition where,
            out LibraDexIdentityCursor<ulong>? cursor,
            out LibraDexConditionEndCondition? plannedCondition)
        {
            cursor = null;
            plannedCondition = null;
            if (where is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            EnsureLibraDexConvergence(nameof(TryOpenLibraDexIdentityCursor));
            var persistentIndexes = libraDexIdentityIndexes;
            if (persistentIndexes is not null &&
                TryOpenLibraDexIdentityCursor(
                    where,
                    persistentIndexes,
                    ownerCatalog: null,
                    out cursor,
                    out plannedCondition))
            {
                return true;
            }

            return HasVolatileLibraDexIndexes() &&
                TryOpenLibraDexIdentityCursor(
                    where,
                    LibraDexVolatileIndexes,
                    libraDexVolatileCatalog,
                    out cursor,
                    out plannedCondition);
        }

        /// <summary>
        /// Attempts to translate and open one complete condition tree against a single persistent or volatile LibraDex identity group.<br/>
        /// A complete tree never crosses catalog ownership boundaries; mixed-owner conditions continue through candidate and residual planning.<br/>
        /// </summary>
        /// <param name="where">The complete Abraxas condition supplied by the developer.<br/></param>
        /// <param name="indexes">The one LibraDex identity group against which every condition leaf must resolve.<br/></param>
        /// <param name="ownerCatalog">The volatile owner catalog, or <c>null</c> when <paramref name="indexes"/> belongs to the persistent catalog.<br/></param>
        /// <param name="cursor">The opened identity cursor when the complete tree resolves against this group.<br/></param>
        /// <param name="plannedCondition">The translated LibraDex condition used to open <paramref name="cursor"/>.<br/></param>
        /// <returns><c>true</c> when every condition leaf resolves within the supplied group; otherwise <c>false</c>.<br/></returns>
        private bool TryOpenLibraDexIdentityCursor(
            Condition<T>.EndCondition where,
            CatalogIdentityGroupIndexes indexes,
            Catalog? ownerCatalog,
            out LibraDexIdentityCursor<ulong>? cursor,
            out LibraDexConditionEndCondition? plannedCondition)
        {
            cursor = null;
            plannedCondition = null;
            if (!where.builder.TryBuildLibraDexConditionTree(
                    indexes,
                    indexName => CanUseLibraDexIndexForSemanticPath(indexName, ownerCatalog),
                    (Condition<T>.Builder builder, IBackendNeutralSelectorDescriptor descriptor, out string indexName) =>
                        TryResolveLibraDexDerivedSelector(builder, descriptor, ownerCatalog, out indexName),
                    (string logicalIndexName, out string physicalIndexName) =>
                        TryResolveReservedLibraDexIndexAlias(logicalIndexName, ownerCatalog, out physicalIndexName),
                    out var condition))
            {
                return false;
            }

            bool oneStringKeyPerIdentity = TryProveSingleStringKeyPerIdentity(condition, ownerCatalog);
            if (indexes.TryOpenMultiplicityProvenConditionIdentityCursor(
                    condition,
                    oneStringKeyPerIdentity,
                    out cursor))
            {
                plannedCondition = condition;
                return true;
            }

            cursor = indexes.AbraxasIdentityQuery<ulong>().OpenCursor(
                condition,
                deduplication: IdentityDeduplication.Distinct);
            plannedCondition = condition;
            return true;
        }

        /// <summary>
        /// Attempts to create an exact-worker LibraDex partition set for one complete Abraxas condition.<br/>
        /// Persistent and volatile catalogs are resolved through the same semantic-path, derived-selector, reserved-alias, convergence, and one-key-per-identity proofs as the ordinary direct cursor.<br/>
        /// </summary>
        /// <param name="where">The completed Abraxas condition supplied to FastFind.<br/></param>
        /// <param name="workerCount">The exact number of independent physical LibraDex workers required.<br/></param>
        /// <param name="partitions">The exact partition set when the selected physical shape supports this primitive and topology; otherwise <see langword="null"/>.<br/></param>
        /// <param name="unsupportedReason">The reason the complete condition or physical index shape could not provide the requested worker partitions, when unavailable.<br/></param>
        /// <returns><see langword="true"/> only when a complete condition resolved and created exactly <paramref name="workerCount"/> disjoint partitions.<br/></returns>
        private bool TryCreateLibraDexIdentityPartitions(
            Condition<T>.EndCondition where,
            int workerCount,
            out LibraDexIdentityPrimitivePartitionSet<ulong>? partitions,
            out string? unsupportedReason)
        {
            partitions = null;
            unsupportedReason = null;
            if (where is null || workerCount < 2 || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
            {
                unsupportedReason = "The store has no eligible LibraDex exact-worker binding or fewer than two workers were requested.";
                return false;
            }

            EnsureLibraDexConvergence(nameof(TryCreateLibraDexIdentityPartitions));
            CatalogIdentityGroupIndexes? persistentIndexes = libraDexIdentityIndexes;
            if (persistentIndexes is not null &&
                TryCreateLibraDexIdentityPartitions(where, persistentIndexes, ownerCatalog: null, workerCount, out partitions, out unsupportedReason))
            {
                return true;
            }

            return HasVolatileLibraDexIndexes() &&
                TryCreateLibraDexIdentityPartitions(where, LibraDexVolatileIndexes, libraDexVolatileCatalog, workerCount, out partitions, out unsupportedReason);
        }

        /// <summary>
        /// Translates one complete Abraxas condition against one catalog owner and requests the shape-native exact-worker primitive partition contract.<br/>
        /// </summary>
        private bool TryCreateLibraDexIdentityPartitions(
            Condition<T>.EndCondition where,
            CatalogIdentityGroupIndexes indexes,
            Catalog? ownerCatalog,
            int workerCount,
            out LibraDexIdentityPrimitivePartitionSet<ulong>? partitions,
            out string? unsupportedReason)
        {
            partitions = null;
            unsupportedReason = null;
            if (!where.builder.TryBuildLibraDexConditionTree(
                    indexes,
                    indexName => CanUseLibraDexIndexForSemanticPath(indexName, ownerCatalog),
                    (Condition<T>.Builder builder, IBackendNeutralSelectorDescriptor descriptor, out string indexName) =>
                        TryResolveLibraDexDerivedSelector(builder, descriptor, ownerCatalog, out indexName),
                    (string logicalIndexName, out string physicalIndexName) =>
                        TryResolveReservedLibraDexIndexAlias(logicalIndexName, ownerCatalog, out physicalIndexName),
                    out LibraDexConditionEndCondition? condition))
            {
                unsupportedReason = "The complete Abraxas condition did not translate against this LibraDex catalog owner.";
                return false;
            }

            bool oneStringKeyPerIdentity = TryProveSingleStringKeyPerIdentity(condition, ownerCatalog);
            return indexes.TryCreateMultiplicityProvenConditionIdentityPartitions(
                condition,
                oneStringKeyPerIdentity,
                workerCount,
                out partitions,
                out unsupportedReason);
        }

        /// <summary>
        /// Proves that one completed condition names exactly one Abraxas-maintained string property index whose logical contract permits at most one key for each record identity.<br/>
        /// The reusable LibraDex string facade conservatively advertises multiple-key multiplicity because direct LibraDex callers may use it more broadly; Abraxas narrows that contract only for its own one-property-value maintenance path.<br/>
        /// This method intentionally proves only catalog ownership and exact registered path or physical-handle name; LibraDex separately materializes the condition and requires its root to be one primitive leaf before omitting deduplication.<br/>
        /// </summary>
        /// <param name="condition">The completed LibraDex condition translated from the Abraxas condition tree.<br/></param>
        /// <param name="ownerCatalog">The volatile owner catalog, or <c>null</c> for a persistent maintained index.<br/></param>
        /// <returns><see langword="true"/> when the condition's sole named index is an exact Abraxas-maintained string property index under the supplied owner.<br/></returns>
        private bool TryProveSingleStringKeyPerIdentity(
            LibraDexConditionEndCondition condition,
            Catalog? ownerCatalog)
        {
            if (condition.Leaves.Count != 1)
                return false;

            string conditionIndexName = condition.Leaves[0].IndexName;
            LibraDexSimpleIndex[] registered = libraDexSimpleIndexes;
            for (int i = 0; i < registered.Length; i++)
            {
                LibraDexSimpleIndex candidate = registered[i];
                if (ReferenceEquals(candidate.OwnerCatalog, ownerCatalog) &&
                    candidate.Index is LibraDexStringScalar8Index &&
                    (string.Equals(candidate.Path, conditionIndexName, StringComparison.Ordinal) ||
                     string.Equals(candidate.Index.Name, conditionIndexName, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to open a LibraDex cursor from one safe indexed leaf of a top-level AND condition for residual Fractal/Inheto evaluation.<br/>
        /// The returned identities are candidates only; callers must evaluate the complete semantic condition before returning a row.<br/>
        /// </summary>
        /// <param name="where">The complete Abraxas condition being planned.<br/></param>
        /// <param name="cursor">The candidate cursor when one available AND leaf can be translated.<br/></param>
        /// <param name="plannedCondition">The candidate condition used to open the cursor for optional plan diagnostics.<br/></param>
        /// <returns><c>true</c> when a candidate-reducing cursor was opened; otherwise <c>false</c>.<br/></returns>
        private bool TryOpenLibraDexAndCandidateCursor(
            Condition<T>.EndCondition where,
            out LibraDexIdentityCursor<ulong>? cursor,
            out LibraDexConditionEndCondition? plannedCondition)
        {
            cursor = null;
            plannedCondition = null;
            if (where is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            EnsureLibraDexConvergence(nameof(TryOpenLibraDexAndCandidateCursor));
            ConditionDescriptor<T>? queryDescriptor = null;
            LibraDexPartialIndex? selectedPartial = null;
            bool ResolvePartialAlias(Catalog? ownerCatalog, string logicalIndexName, out string physicalIndexName)
            {
                if (TryResolveReservedLibraDexIndexAlias(
                        logicalIndexName,
                        ownerCatalog,
                        out physicalIndexName))
                {
                    return true;
                }

                physicalIndexName = string.Empty;
                var partialEntries = libraDexPartialIndexes;
                var hasPathCandidate = false;
                for (var i = 0; i < partialEntries.Length; i++)
                {
                    var candidate = partialEntries[i];
                    if (ReferenceEquals(candidate.OwnerCatalog, ownerCatalog) &&
                        candidate.KeyType == typeof(string) &&
                        string.Equals(candidate.KeyPath, logicalIndexName, StringComparison.Ordinal) &&
                        IsPartialIndexUsable(candidate))
                    {
                        hasPathCandidate = true;
                        break;
                    }
                }
                if (!hasPathCandidate)
                    return false;

                try
                {
                    queryDescriptor ??= where.ToDescriptor();
                    if (!queryDescriptor.IsSystemPersistable ||
                        !TryResolvePartialIndexAlias(queryDescriptor, logicalIndexName, ownerCatalog, out var resolved) ||
                        resolved is null)
                    {
                        return false;
                    }

                    selectedPartial = resolved;
                    physicalIndexName = resolved.PhysicalName;
                    return true;
                }
                catch (Exception exception)
                    when (exception is InvalidDataException or
                          InvalidOperationException or
                          NotSupportedException or
                          ArgumentException)
                {
                    return false;
                }
            }

            bool ResolvePersistentAlias(string logicalIndexName, out string physicalIndexName)
                => ResolvePartialAlias(ownerCatalog: null, logicalIndexName, out physicalIndexName);

            bool ResolveVolatileAlias(string logicalIndexName, out string physicalIndexName)
                => ResolvePartialAlias(libraDexVolatileCatalog, logicalIndexName, out physicalIndexName);

            try
            {
                var persistentIndexes = libraDexIdentityIndexes;
                if (persistentIndexes is not null &&
                    TryOpenLibraDexAndCandidateCursor(
                        where,
                        persistentIndexes,
                        ownerCatalog: null,
                        ResolvePersistentAlias,
                        out cursor,
                        out plannedCondition))
                {
                    if (selectedPartial is not null && !IsPartialIndexUsable(selectedPartial))
                    {
                        cursor!.Dispose();
                        cursor = null;
                        plannedCondition = null;
                    }
                    else
                    {
                        return true;
                    }
                }

                selectedPartial = null;
                if (!HasVolatileLibraDexIndexes() ||
                    !TryOpenLibraDexAndCandidateCursor(
                        where,
                        LibraDexVolatileIndexes,
                        libraDexVolatileCatalog,
                        ResolveVolatileAlias,
                        out cursor,
                        out plannedCondition))
                {
                    return false;
                }
                if (selectedPartial is not null && !IsPartialIndexUsable(selectedPartial))
                {
                    cursor!.Dispose();
                    cursor = null;
                    plannedCondition = null;
                    return false;
                }
                return true;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to open one safe top-level-AND candidate against a single persistent or volatile LibraDex identity group.<br/>
        /// The authoritative Fractal/Inheto residual evaluator remains responsible for every condition fragment not represented by the returned cursor.<br/>
        /// </summary>
        /// <param name="where">The complete Abraxas condition being planned.<br/></param>
        /// <param name="indexes">The single catalog identity group allowed to supply the candidate leaf.<br/></param>
        /// <param name="ownerCatalog">The volatile owner catalog, or <c>null</c> for persistent candidates.<br/></param>
        /// <param name="aliasResolver">Optional owner-scoped reserved or partial-index alias resolver.<br/></param>
        /// <param name="cursor">The candidate-producing identity cursor when one safe leaf is available.<br/></param>
        /// <param name="plannedCondition">The LibraDex leaf condition used to open <paramref name="cursor"/>.<br/></param>
        /// <returns><c>true</c> when one safe candidate leaf was opened from the supplied group; otherwise <c>false</c>.<br/></returns>
        private bool TryOpenLibraDexAndCandidateCursor(
            Condition<T>.EndCondition where,
            CatalogIdentityGroupIndexes indexes,
            Catalog? ownerCatalog,
            Condition<T>.LibraDexIndexAliasResolver? aliasResolver,
            out LibraDexIdentityCursor<ulong>? cursor,
            out LibraDexConditionEndCondition? plannedCondition)
        {
            cursor = null;
            plannedCondition = null;
            if (!where.builder.TryBuildLibraDexAndCandidate(
                    indexes,
                    indexName => CanUseLibraDexIndexForSemanticPath(indexName, ownerCatalog),
                    (Condition<T>.Builder builder, IBackendNeutralSelectorDescriptor descriptor, out string indexName) =>
                        TryResolveLibraDexDerivedSelector(builder, descriptor, ownerCatalog, out indexName),
                    aliasResolver,
                    out var candidateCondition))
            {
                return false;
            }

            cursor = indexes.AbraxasIdentityQuery<ulong>().OpenCursor(
                candidateCondition,
                deduplication: IdentityDeduplication.Distinct);
            plannedCondition = candidateCondition;
            return true;
        }

        /// <summary>
        /// Attempts to use a maintained structured DateTime index as the candidate source for an executable <see cref="DateTime.TimeOfDay"/> PropPath condition.<br/>
        /// An explicit index for the complete derived path is deliberately left to the ordinary direct/candidate planner, which runs first and provides a true ordered TimeSpan range lookup.<br/>
        /// The base-index path scans compact DateTime index tuples without hydrating records; the caller retains Inheto residual evaluation as the exact semantic authority.<br/>
        /// </summary>
        /// <param name="where">The completed condition containing one safe top-level-AND TimeOfDay range leaf.<br/></param>
        /// <param name="identities">Candidate identities from the maintained DateTime source index.<br/></param>
        /// <param name="indexName">The base DateTime property-path index used by the accelerator.<br/></param>
        /// <returns><see langword="true"/> when an eligible persistent or volatile base DateTime index is available; otherwise <see langword="false"/>.<br/></returns>
        private bool TryGetTimeOfDayCandidateIdentities(
            Condition<T>.EndCondition where,
            out IEnumerable<ulong> identities,
            out string indexName)
        {
            identities = Array.Empty<ulong>();
            indexName = string.Empty;
            if (where is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            EnsureLibraDexConvergence(nameof(TryGetTimeOfDayCandidateIdentities));
            if (TryGetTimeOfDayCandidateIdentities(where, ownerCatalog: null, out identities, out indexName))
                return true;

            return HasVolatileLibraDexIndexes() &&
                TryGetTimeOfDayCandidateIdentities(
                    where,
                    libraDexVolatileCatalog,
                    out identities,
                    out indexName);
        }

        /// <summary>
        /// Resolves one Abraxas-recognized TimeOfDay candidate against a single LibraDex owner without teaching LibraDex about CLR PropPath semantics.<br/>
        /// Calendar SDT truncates sub-microsecond ticks, so the lower candidate boundary is widened by nine ticks and the authoritative residual removes any resulting false positives.<br/>
        /// </summary>
        /// <param name="where">The completed Abraxas condition.<br/></param>
        /// <param name="ownerCatalog">The required volatile owner, or <see langword="null"/> for the persistent group.<br/></param>
        /// <param name="identities">Candidate identities from the exact base DateTime index.<br/></param>
        /// <param name="indexName">The selected base DateTime property path.<br/></param>
        /// <returns><see langword="true"/> when one exact base DateTime index can supply candidates.<br/></returns>
        private bool TryGetTimeOfDayCandidateIdentities(
            Condition<T>.EndCondition where,
            Catalog? ownerCatalog,
            out IEnumerable<ulong> identities,
            out string indexName)
        {
            identities = Array.Empty<ulong>();
            indexName = string.Empty;
            if (!where.builder.TryBuildTimeOfDayCandidate(out var candidate) ||
                HasLibraDexIndexInGroup(candidate.Path, ownerCatalog) ||
                NativeDeserializationOptions.Aliases.Contains(candidate.SourcePath))
            {
                return false;
            }

            LibraDexSimpleIndex[] entries = libraDexSimpleIndexes;
            for (var i = 0; i < entries.Length; i++)
            {
                LibraDexSimpleIndex entry = entries[i];
                if (!ReferenceEquals(entry.OwnerCatalog, ownerCatalog) ||
                    !string.Equals(entry.Path, candidate.SourcePath, StringComparison.Ordinal) ||
                    entry.KeyType != typeof(DateTime) ||
                    entry.CalculatedSelector is not null ||
                    entry.Index is not LibraDexIndex<DateTime, ulong> index ||
                    index.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
                {
                    continue;
                }

                identities = IterateTimeOfDayCandidates(index, candidate);
                indexName = candidate.SourcePath;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Scans one maintained DateTime index and yields a conservative identity superset for an inclusive TimeOfDay range.<br/>
        /// The typed reader avoids object-key boxing and record hydration; the lower boundary is widened for the index encoding's public DateTime projection, and the authoritative Inheto residual removes every admitted false positive.<br/>
        /// </summary>
        /// <param name="index">The exact maintained DateTime/UInt64 index owned by this store.<br/></param>
        /// <param name="candidate">The materialized inclusive TimeOfDay bounds.<br/></param>
        /// <returns>A lazy candidate identity stream consumed by the existing residual pipeline.<br/></returns>
        private static IEnumerable<ulong> IterateTimeOfDayCandidates(
            LibraDexIndex<DateTime, ulong> index,
            Condition<T>.TimeOfDayCandidate candidate)
        {
            const long maximumTimeOfDayTicks = TimeSpan.TicksPerDay - 1;
            // LibraDexRangeReader currently projects Precision SDT low bits through its Calendar SDT key decoder.
            // Across one millisecond that projection can round an exact key downward by at most 3,759 ticks.
            const long maximumPrecisionProjectionDrift = 3759;
            long start = candidate.Start.Ticks;
            long end = candidate.End.Ticks;
            if (start > end || end < 0 || start > maximumTimeOfDayTicks)
                yield break;

            long widening = index.DateTimeKeyEncoding == DateTimeKeyEncoding.CalendarSdt
                ? 9
                : maximumPrecisionProjectionDrift;
            long lower = start <= 0 ? 0 : start - widening;
            long upper = end >= maximumTimeOfDayTicks ? maximumTimeOfDayTicks : end;
            using LibraDexRangeReader<DateTime, ulong> reader = index.OpenReader(QueryDirection.Ascending);
            while (reader.TryReadNext(out DateTime dateTime, out ulong identity))
            {
                long ticks = dateTime.Ticks % TimeSpan.TicksPerDay;
                if (ticks >= lower && ticks <= upper)
                    yield return identity;
            }
        }

        /// <summary>
        /// Tests whether a translated condition leaf has a registered ordered index in the requested persistent or volatile LibraDex identity group.<br/>
        /// Translation alone is insufficient because LibraDex condition descriptors are intentionally lazy and do not open their named index until cursor materialization.<br/>
        /// </summary>
        /// <param name="indexName">The logical property path or resolved physical index name emitted by the typed condition translator.<br/></param>
        /// <param name="ownerCatalog">The required volatile owner, or <c>null</c> when checking the persistent group.<br/></param>
        /// <returns><c>true</c> when the requested group owns a live registered index for the name; otherwise <c>false</c>.<br/></returns>
        private bool HasLibraDexIndexInGroup(string indexName, Catalog? ownerCatalog)
        {
            if (string.IsNullOrWhiteSpace(indexName))
                return false;

            var registered = libraDexSimpleIndexes;
            for (var i = 0; i < registered.Length; i++)
            {
                var entry = registered[i];
                if (ReferenceEquals(entry.OwnerCatalog, ownerCatalog) &&
                    (string.Equals(entry.Path, indexName, StringComparison.Ordinal) ||
                     string.Equals(entry.Index.Name, indexName, StringComparison.Ordinal)))
                    return true;
            }

            var regexRegistered = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexRegistered.Length; i++)
            {
                var entry = regexRegistered[i];
                if (ReferenceEquals(entry.OwnerCatalog, ownerCatalog) &&
                    string.Equals(BuildLibraDexRegexProjectionIndexName(entry.LogicalPrefix, entry.SourcePath, entry.Regex, entry.Settings), indexName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasLibraDexIndexInBoundGroup(string indexName)
        {
            return CanUseLibraDexIndexForSemanticPath(indexName, ownerCatalog: null);
        }

        /// <summary>
        /// Tests whether one physical LibraDex index can safely represent a semantic property-path request.<br/>
        /// An alias equivalence group may span several physical member names across stored generations, so one ordinary member index cannot prove that its identity population is complete.<br/>
        /// Explicit named-index selectors remain outside this check because they request that physical index intentionally rather than asking for property-path equivalence semantics.<br/>
        /// </summary>
        /// <param name="indexName">Semantic property path whose ordinary maintained index would execute the request.<br/></param>
        /// <param name="ownerCatalog">Required volatile owner, or <see langword="null"/> for the persistent index group.<br/></param>
        /// <returns><see langword="true"/> only when the physical index exists and no registered alias can broaden the requested path.<br/></returns>
        private bool CanUseLibraDexIndexForSemanticPath(string indexName, Catalog? ownerCatalog)
        {
            return !NativeDeserializationOptions.Aliases.Contains(indexName) &&
                HasLibraDexIndexInGroup(indexName, ownerCatalog);
        }

        /// <summary>
        /// Resolves one public logical metadata selector to its Abraxas-owned hidden physical LibraDex index.<br/>
        /// Reserved State is persistent-only; volatile catalogs deliberately cannot shadow or override that authoritative metadata reality.<br/>
        /// </summary>
        /// <param name="logicalIndexName">Public condition selector name.<br/></param>
        /// <param name="ownerCatalog">Requested volatile owner, or <c>null</c> for the persistent group.<br/></param>
        /// <param name="physicalIndexName">Hidden physical index name when resolution succeeds.<br/></param>
        /// <returns><see langword="true"/> only for the bound persistent State alias.<br/></returns>
        private bool TryResolveReservedLibraDexIndexAlias(
            string logicalIndexName,
            Catalog? ownerCatalog,
            out string physicalIndexName)
        {
            physicalIndexName = string.Empty;
            if (ownerCatalog is not null ||
                libraDexStateIndex is null ||
                !logicalIndexName.AsSpan().TrimStart('.').Equals("State", StringComparison.Ordinal))
            {
                return false;
            }

            physicalIndexName = LibraDexStateIndexName;
            return true;
        }

        /// <summary>
        /// Tests whether any attached persistent or volatile LibraDex group owns the requested maintained index.<br/>
        /// This owner-neutral view is used only for developer diagnostics; execution planning still binds every native condition to exactly one catalog group.<br/>
        /// </summary>
        /// <param name="indexName">The property-path or projection index identity reported by a condition leaf.<br/></param>
        /// <returns><c>true</c> when either attached backing owns the index; otherwise <c>false</c>.<br/></returns>
        private bool HasAnyLibraDexIndex(string indexName)
        {
            return HasLibraDexIndexInGroup(indexName, ownerCatalog: null) ||
                HasLibraDexIndexInGroup(indexName, libraDexVolatileCatalog);
        }

        /// <summary>
        /// Resolves an execution-facing physical LibraDex definition name back to its public Abraxas logical index identity for diagnostics.<br/>
        /// Ordinary indexes already use the same name on both sides, while virtual properties and other separately named calculated indexes retain their developer-owned semantic path.<br/>
        /// </summary>
        /// <param name="indexName">Logical or physical index identity produced by condition planning.<br/></param>
        /// <returns>The public logical Abraxas identity when a registered simple index owns the physical name; otherwise the supplied identity unchanged.<br/></returns>
        private string GetLibraDexDiagnosticIndexName(string indexName)
        {
            if (string.IsNullOrWhiteSpace(indexName))
                return indexName;

            LibraDexSimpleIndex[] registered = libraDexSimpleIndexes;
            for (int i = 0; i < registered.Length; i++)
            {
                LibraDexSimpleIndex entry = registered[i];
                if (string.Equals(entry.Index.Name, indexName, StringComparison.Ordinal))
                    return entry.Path;
            }

            LibraDexCompositeIndex[] composites = libraDexCompositeIndexes;
            for (int i = 0; i < composites.Length; i++)
            {
                LibraDexCompositeIndex entry = composites[i];
                if (string.Equals(entry.PhysicalName, indexName, StringComparison.Ordinal))
                    return entry.LogicalName;
            }

            return indexName;
        }

        /// <summary>
        /// Resolves a backend-neutral derived selector against the persistent LibraDex indexes maintained by this store.<br/>
        /// Regex selectors require an exactly matching maintained projection; binary slices reuse their exact source blob index because LibraDex owns native slice execution and no separate projection is required.<br/>
        /// Volatile indexes are excluded because they do not belong to the bound persistent identity group.<br/>
        /// </summary>
        /// <param name="builder">The condition builder that owns execution-scoped parameter materialization.<br/></param>
        /// <param name="descriptor">The backend-neutral selector descriptor supplied by the condition leaf.<br/></param>
        /// <param name="indexName">The exact maintained LibraDex projection index name when resolution succeeds.<br/></param>
        /// <returns><c>true</c> when one persistent maintained index can execute the selector exactly; otherwise <c>false</c>.<br/></returns>
        private bool TryResolveLibraDexDerivedSelector(
            Condition<T>.Builder builder,
            IBackendNeutralSelectorDescriptor descriptor,
            out string indexName)
        {
            return TryResolveLibraDexDerivedSelector(builder, descriptor, ownerCatalog: null, out indexName);
        }

        /// <summary>
        /// Resolves a derived selector against either attached backing for owner-neutral condition-plan diagnostics.<br/>
        /// Execution planning uses the owner-specific overload so a native LibraDex condition never crosses catalog groups.<br/>
        /// </summary>
        /// <param name="builder">The condition builder that owns execution-scoped parameter materialization.<br/></param>
        /// <param name="descriptor">The backend-neutral derived-selector descriptor.<br/></param>
        /// <param name="indexName">The matching maintained projection identity when found.<br/></param>
        /// <returns><c>true</c> when a persistent or volatile projection matches; otherwise <c>false</c>.<br/></returns>
        private bool TryResolveAnyLibraDexDerivedSelector(
            Condition<T>.Builder builder,
            IBackendNeutralSelectorDescriptor descriptor,
            out string indexName)
        {
            return TryResolveLibraDexDerivedSelector(builder, descriptor, ownerCatalog: null, out indexName) ||
                TryResolveLibraDexDerivedSelector(builder, descriptor, libraDexVolatileCatalog, out indexName);
        }

        /// <summary>
        /// Resolves a backend-neutral derived selector against indexes owned by one requested persistent or volatile catalog.<br/>
        /// Restricting resolution to one owner ensures the resulting LibraDex condition is opened only against the identity group containing its projection or exact source blob.<br/>
        /// </summary>
        /// <param name="builder">The condition builder that owns execution-scoped parameter materialization.<br/></param>
        /// <param name="descriptor">The backend-neutral derived-selector descriptor supplied by the condition leaf.<br/></param>
        /// <param name="ownerCatalog">The required volatile owner, or <c>null</c> for the persistent catalog.<br/></param>
        /// <param name="indexName">The exact maintained projection name when resolution succeeds.<br/></param>
        /// <returns><c>true</c> when one projection with the required owner exactly matches the descriptor; otherwise <c>false</c>.<br/></returns>
        private bool TryResolveLibraDexDerivedSelector(
            Condition<T>.Builder builder,
            IBackendNeutralSelectorDescriptor descriptor,
            Catalog? ownerCatalog,
            out string indexName)
        {
            indexName = string.Empty;
            if (builder is null || descriptor is null)
            {
                return false;
            }

            if (descriptor is NamedIndexSelectorDescriptor namedIndex)
            {
                LibraDexSimpleIndex[] namedCandidates = libraDexSimpleIndexes;
                for (int i = 0; i < namedCandidates.Length; i++)
                {
                    LibraDexSimpleIndex candidate = namedCandidates[i];
                    if (!ReferenceEquals(candidate.OwnerCatalog, ownerCatalog) ||
                        (!string.Equals(candidate.Path, namedIndex.SemanticName, StringComparison.Ordinal) &&
                         !string.Equals(candidate.Index.Name, namedIndex.SemanticName, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    indexName = candidate.Index.Name;
                    return true;
                }
                if (HasLibraDexIndexInGroup(namedIndex.SemanticName, ownerCatalog))
                {
                    indexName = namedIndex.SemanticName;
                    return true;
                }
                FunctionIndexProjection[] projections = functionIndexProjections;
                for (int i = 0; i < projections.Length; i++)
                {
                    if (!string.Equals(projections[i].FunctionId, namedIndex.SemanticName, StringComparison.Ordinal))
                        continue;
                    string logicalName = GetFunctionIndexLogicalName(namedIndex.SemanticName);
                    if (HasLibraDexIndexInGroup(logicalName, ownerCatalog))
                    {
                        indexName = logicalName;
                        return true;
                    }
                    return false;
                }
                return false;
            }

            if (descriptor is Condition<T>.BinarySliceSelectorDescriptor sliceDescriptor &&
                Condition<T>.TryExtractPropPath(sliceDescriptor.SourceSelector, out var binarySourcePath) &&
                CanUseLibraDexIndexForSemanticPath(binarySourcePath, ownerCatalog))
            {
                indexName = binarySourcePath;
                return true;
            }

            Condition<T>.StringOperator.MatchValueSelectorDescriptor? runtimeRegexDescriptor =
                descriptor as Condition<T>.StringOperator.MatchValueSelectorDescriptor;
            Regex? configuredRegex = null;
            IBackendNeutralSelectorDescriptor calculatedDescriptor = descriptor;
            if (runtimeRegexDescriptor is not null)
            {
                IBackendNeutralSelectorDescriptor? sourceDescriptor =
                    runtimeRegexDescriptor.SourceSelector.SelectorDescriptor;
                if (sourceDescriptor is null &&
                    Condition<T>.TryExtractPropPath(runtimeRegexDescriptor.SourceSelector, out var directRegexSource))
                {
                    sourceDescriptor = new DirectPropertySelectorDescriptor(directRegexSource);
                }

                if (sourceDescriptor is not null)
                {
                    configuredRegex = builder.MaterializeParameter(
                        runtimeRegexDescriptor.PatternExpression,
                        Condition<T>.TryExtractPropPath(runtimeRegexDescriptor.SourceSelector, out var materializedRegexSource)
                            ? materializedRegexSource
                            : string.Empty,
                        owner: runtimeRegexDescriptor);
                    calculatedDescriptor = Selector<T>.StringOperationSelectorDescriptor.CreateMatchValue(
                        sourceDescriptor,
                        configuredRegex,
                        runtimeRegexDescriptor.GroupNumber,
                        runtimeRegexDescriptor.OccurrenceNumber ?? 0);
                }
            }

            LibraDexSimpleIndex[] calculatedCandidates = libraDexSimpleIndexes;
            for (int i = 0; i < calculatedCandidates.Length; i++)
            {
                LibraDexSimpleIndex candidate = calculatedCandidates[i];
                if (!ReferenceEquals(candidate.OwnerCatalog, ownerCatalog) ||
                    candidate.CalculatedSelector is null ||
                    !candidate.IncludeNullValues ||
                    (candidate.KeyType == typeof(string) && !candidate.IncludeEmptyStrings))
                    continue;
                try
                {
                    SelectorDescriptor<T> requested = SelectorDescriptor<T>.FromSemanticDescriptor(calculatedDescriptor, candidate.KeyType);
                    if (string.Equals(
                        requested.SemanticFingerprint,
                        candidate.CalculatedFingerprint,
                        StringComparison.Ordinal))
                    {
                        indexName = candidate.Index.Name;
                        return true;
                    }
                }
                catch (NotSupportedException)
                {
                }
                catch (InvalidDataException)
                {
                }
            }

            if (descriptor is Selector<T>.StringOperationSelectorDescriptor
                {
                    Operation: StringOperationKind.Normalize,
                    Source: DirectPropertySelectorDescriptor directSource
                } &&
                CanUseLibraDexIndexForSemanticPath(directSource.PropertyPath, ownerCatalog))
            {
                indexName = directSource.PropertyPath;
                return true;
            }

            if (runtimeRegexDescriptor is not { } regexDescriptor ||
                !Condition<T>.TryExtractPropPath(regexDescriptor.SourceSelector, out var sourcePath))
            {
                return false;
            }

            LibraDexRegexProjectionIndex[] candidates = libraDexRegexProjectionIndexes;
            var hasCandidate = false;
            for (var i = 0; i < candidates.Length; i++)
            {
                var entry = candidates[i];
                if (ReferenceEquals(entry.OwnerCatalog, ownerCatalog) &&
                    string.Equals(entry.SourcePath, sourcePath, StringComparison.Ordinal) &&
                    RegexProjectionSelectsGroup(entry, regexDescriptor.GroupNumber) &&
                    (entry.Settings?.Occurrence ?? 0) == (regexDescriptor.OccurrenceNumber ?? 0))
                {
                    hasCandidate = true;
                    break;
                }
            }

            if (!hasCandidate)
                return false;

            var configured = configuredRegex ?? builder.MaterializeParameter(
                regexDescriptor.PatternExpression,
                sourcePath,
                owner: regexDescriptor);
            var configuredOptions = configured.Options & ~RegexOptions.Compiled;
            for (var i = 0; i < candidates.Length; i++)
            {
                var entry = candidates[i];
                if (!ReferenceEquals(entry.OwnerCatalog, ownerCatalog) ||
                    !string.Equals(entry.SourcePath, sourcePath, StringComparison.Ordinal) ||
                    !RegexProjectionSelectsGroup(entry, regexDescriptor.GroupNumber) ||
                    (entry.Settings?.Occurrence ?? 0) != (regexDescriptor.OccurrenceNumber ?? 0) ||
                    !string.Equals(entry.Regex.ToString(), configured.ToString(), StringComparison.Ordinal) ||
                    (entry.Regex.Options & ~RegexOptions.Compiled) != configuredOptions ||
                    entry.Regex.MatchTimeout != configured.MatchTimeout)
                {
                    continue;
                }

                indexName = BuildLibraDexRegexProjectionIndexName(
                    entry.LogicalPrefix,
                    entry.SourcePath,
                    entry.Regex,
                    entry.Settings);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tests whether a maintained regex projection selects the same numeric capture as a regex-value descriptor.<br/>
        /// Named registry captures are mapped through the registered regex so semantically identical numeric condition selection remains resolvable.<br/>
        /// </summary>
        /// <param name="entry">The maintained regex projection descriptor.<br/></param>
        /// <param name="groupNumber">The numeric capture requested by the condition selector.<br/></param>
        /// <returns><c>true</c> when both descriptors select the same capture; otherwise <c>false</c>.<br/></returns>
        private static bool RegexProjectionSelectsGroup(LibraDexRegexProjectionIndex entry, int groupNumber)
        {
            if (entry.Settings?.SelectedGroupNumber is int selectedNumber)
                return selectedNumber == groupNumber;

            if (!string.IsNullOrEmpty(entry.Settings?.SelectedGroupName))
                return entry.Regex.GroupNumberFromName(entry.Settings.SelectedGroupName!) == groupNumber;

            return groupNumber == 0;
        }
        /// <summary>
        /// Registers a simple property-path LibraDex index when a supported Abraxas index is created.<br/>
        /// The method performs reflection and catalog shape selection only at setup time; write/query hot paths use the recorded runtime index handle directly.<br/>
        /// </summary>
        /// <param name="propPath">The Abraxas property path that also names the LibraDex index inside the bound identity group.<br/></param>
        /// <param name="unique">When true, creates or opens a unique-key LibraDex index; otherwise creates or opens a non-unique secondary index.<br/></param>
        /// <param name="direction">The physical key ordering direction requested for the maintained index.<br/></param>
        /// <returns><c>true</c> when a LibraDex index was registered for the path; otherwise <c>false</c>.</returns>
        private bool TryRegisterLibraDexSimplePathIndex(string propPath, bool unique, IndexDirection direction = IndexDirection.Ascending)
        {
            var indexes = libraDexIdentityIndexes;
            if (indexes is null || string.IsNullOrWhiteSpace(propPath))
                return false;

            return TryRegisterLibraDexSimplePathIndex(propPath, unique, indexes, ownerCatalog: null, populateExisting: true, autoCreated: false, AutoIndexUse.None, direction: direction);
        }

        /// <summary>
        /// Registers a simple text property-path LibraDex index when a supported Abraxas text index is created.<br/>
        /// Text profile selection is Abraxas-facing; the companion index profile is selected here so callers do not need storage-engine vocabulary.<br/>
        /// </summary>
        /// <param name="propPath">The Abraxas property path that provides text keys.<br/></param>
        /// <param name="profile">The text representations to maintain for lookup and ordering.<br/></param>
        /// <param name="unique">When true, the primary Abraxas index enforces unique keys where supported.<br/></param>
        /// <param name="direction">The physical key ordering direction requested for the maintained index.<br/></param>
        /// <param name="sortKeyProfiles">Optional culture and comparison-options profiles whose sort-key representations are maintained alongside the text keys.<br/></param>
        /// <returns><c>true</c> when a text ordered backend was registered; otherwise <c>false</c>.</returns>
        private bool TryRegisterLibraDexTextPathIndex(string propPath, TextIndexProfile profile, IndexDirection direction, bool unique, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
        {
            var indexes = libraDexIdentityIndexes;
            if (indexes is null || string.IsNullOrWhiteSpace(propPath))
                return false;

            return TryRegisterLibraDexTextPathIndex(propPath, profile, direction, unique, indexes, ownerCatalog: null, populateExisting: true, sortKeyProfiles: sortKeyProfiles);
        }

        /// <summary>
        /// Registers a simple property-path ordered index in a memory-backed catalog owned by this store.<br/>
        /// The memory catalog is an implementation detail; public callers see only an Abraxas volatile index whose key material is not persisted.<br/>
        /// </summary>
        /// <param name="propPath">The Abraxas property path to mirror into the volatile ordered index.<br/></param>
        /// <param name="unique">When true, creates a unique-key volatile ordered index; otherwise creates a non-unique volatile ordered index.<br/></param>
        /// <param name="direction">The physical key ordering direction requested for the maintained index.<br/></param>
        /// <returns><c>true</c> when the volatile ordered index was registered; otherwise <c>false</c>.</returns>
        private bool TryRegisterLibraDexMemorySimplePathIndex(string propPath, bool unique, IndexDirection direction = IndexDirection.Ascending)
        {
            return TryRegisterLibraDexMemorySimplePathIndex(propPath, unique, autoCreated: false, AutoIndexUse.None, direction);
        }

        private bool TryRegisterLibraDexMemorySimplePathIndex(string propPath, bool unique, bool autoCreated, AutoIndexUse autoUse, IndexDirection direction = IndexDirection.Ascending)
        {
            if (string.IsNullOrWhiteSpace(propPath))
                return false;

            return TryRegisterLibraDexSimplePathIndex(propPath, unique, LibraDexVolatileIndexes, libraDexVolatileCatalog, populateExisting: true, autoCreated, autoUse, direction: direction);
        }

        /// <summary>
        /// Registers a simple text property-path ordered index in a memory-backed catalog owned by this store.<br/>
        /// The text key material is volatile and is lost when the application closes or the store instance closes.<br/>
        /// </summary>
        /// <param name="propPath">The Abraxas property path that provides volatile text keys.<br/></param>
        /// <param name="profile">The text representations to maintain for lookup and ordering.<br/></param>
        /// <param name="unique">When true, the primary Abraxas index enforces unique keys where supported.<br/></param>
        /// <param name="direction">The physical key ordering direction requested for the maintained index.<br/></param>
        /// <param name="sortKeyProfiles">Optional culture and comparison-options profiles whose sort-key representations are maintained alongside the text keys.<br/></param>
        /// <returns><c>true</c> when the volatile text ordered backend was registered; otherwise <c>false</c>.</returns>
        private bool TryRegisterLibraDexMemoryTextPathIndex(string propPath, TextIndexProfile profile, IndexDirection direction, bool unique, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
        {
            if (string.IsNullOrWhiteSpace(propPath))
                return false;

            return TryRegisterLibraDexTextPathIndex(propPath, profile, direction, unique, LibraDexVolatileIndexes, libraDexVolatileCatalog, populateExisting: true, sortKeyProfiles: sortKeyProfiles);
        }

        /// <summary>
        /// Creates or reattaches one persistent or volatile calculated selector index using an exact durable selector descriptor.<br/>
        /// The selector reader is compiled once from backend-neutral semantics, then reused for initial Fractal/Inheto population and every later store mutation without hydrating the record object.<br/>
        /// </summary>
        /// <param name="logicalName">Stable Abraxas management identity for the calculated index.<br/></param>
        /// <param name="physicalName">LibraDex catalog definition name owned by the calculated index.<br/></param>
        /// <param name="selector">Durable exact-result selector descriptor.<br/></param>
        /// <param name="profile">Maintained string representations when the result is text.<br/></param>
        /// <param name="direction">Requested ordered traversal direction.<br/></param>
        /// <param name="unique">Whether non-null calculated values must be unique.<br/></param>
        /// <param name="includeNullValues">Whether missing, null, or invalid calculated values use the null-key route.<br/></param>
        /// <param name="includeEmptyStrings">Whether empty string results remain indexed.<br/></param>
        /// <param name="binaryStorage">The bounded variable or exact fixed storage shape for byte-array results; otherwise none.<br/></param>
        /// <param name="binaryKeyBytes">The variable payload ceiling or exact fixed width for byte-array results; otherwise zero.<br/></param>
        /// <param name="indexes">Target persistent or volatile LibraDex identity group.<br/></param>
        /// <param name="ownerCatalog">Volatile owner catalog, or null for persistent store-owned reality.<br/></param>
        /// <param name="populateExisting">Whether an existing physical definition must be rebuilt from authoritative records.<br/></param>
        /// <param name="manageIntent">Whether this call owns preparing/ready persistent intent publication.<br/></param>
        /// <param name="functionResultType">Exact function projection result type when <paramref name="selector"/> is absent.<br/></param>
        /// <param name="functionBinaryReader">Function projection reader used by backfill, repair, and prior-key removal.<br/></param>
        /// <param name="functionRuntimeReader">Function projection reader used against the CLR object already supplied by an ordinary Put.<br/></param>
        /// <param name="functionId">Stable function projection identity, or null for a durable built-in selector.<br/></param>
        /// <param name="functionVersion">Positive function projection semantic version, or zero for a durable built-in selector.<br/></param>
        /// <param name="preparedPopulation">Optional precomputed record identities and keys used to rebuild the index without rereading or recomputing the authoritative record population.<br/></param>
        /// <param name="isVirtualProperty">Whether the calculated index also supplies a developer-declared virtual property.<br/></param>
        /// <param name="virtualValuesAreAuthoritative">Whether the virtual property's indexed values are authoritative rather than recomputable projections of stored records.<br/></param>
        /// <param name="virtualPropertyTiming">Optional accumulator for virtual-property registration phase timings; null disables this bookkeeping.<br/></param>
        /// <returns><see langword="true"/> when the exact result type has a supported ordered representation; otherwise <see langword="false"/>.<br/></returns>
        private bool TryRegisterLibraDexCalculatedIndex(
            string logicalName,
            string physicalName,
            SelectorDescriptor<T> selector,
            TextIndexProfile profile,
            IndexDirection direction,
            bool unique,
            bool includeNullValues,
            bool includeEmptyStrings,
            CalculatedBinaryKeyStorage binaryStorage,
            int binaryKeyBytes,
            CatalogIdentityGroupIndexes indexes,
            Catalog? ownerCatalog,
            bool populateExisting,
            bool manageIntent = true,
            Type? functionResultType = null,
            Func<InhetoBinary, (bool HasValue, object? Value)>? functionBinaryReader = null,
            Func<T, (bool HasValue, object? Value)>? functionRuntimeReader = null,
            string? functionId = null,
            int functionVersion = 0,
            List<(ulong RecordID, object? Key)>? preparedPopulation = null,
            bool isVirtualProperty = false,
            bool virtualValuesAreAuthoritative = false,
            VirtualPropertyIndexEnsureTiming? virtualPropertyTiming = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
            ArgumentException.ThrowIfNullOrWhiteSpace(physicalName);
            long readerPreparationStarted = Stopwatch.GetTimestamp();
            Func<InhetoBinary, (bool HasValue, object? Value)> reader;
            Type keyType;
            OrdinaryIndexIntentKind intentKind;
            if (functionId is null)
            {
                ArgumentNullException.ThrowIfNull(selector);
                if (!TryCreateStoreSelectorReader(selector, out reader))
                    return false;
                keyType = NormalizeValueType(selector.ResultType);
                intentKind = OrdinaryIndexIntentKind.Calculated;
            }
            else
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
                if (functionVersion <= 0)
                    throw new ArgumentOutOfRangeException(nameof(functionVersion), functionVersion, "Function projection versions must be positive integers.");
                ArgumentNullException.ThrowIfNull(functionResultType);
                ArgumentNullException.ThrowIfNull(functionBinaryReader);
                keyType = NormalizeValueType(functionResultType);
                reader = functionBinaryReader;
                intentKind = OrdinaryIndexIntentKind.FunctionCalculated;
            }
            if (keyType != typeof(byte[]) && !IsSupportedLibraDexScalarKeyType(keyType))
                return false;
            if (keyType == typeof(byte[]))
            {
                if (binaryStorage == CalculatedBinaryKeyStorage.VariableLength && binaryKeyBytes != 1023)
                    throw new ArgumentOutOfRangeException(nameof(binaryKeyBytes), binaryKeyBytes, "Calculated variable binary indexes use the standard 1,023-byte payload ceiling.");
                if (binaryStorage == CalculatedBinaryKeyStorage.FixedLength && binaryKeyBytes is not (8 or 16 or 32))
                    throw new ArgumentOutOfRangeException(nameof(binaryKeyBytes), binaryKeyBytes, "Calculated fixed binary indexes require an 8-, 16-, or 32-byte key width.");
                if (binaryStorage == CalculatedBinaryKeyStorage.None)
                    throw new ArgumentException("Calculated byte-array indexes require a variable or fixed binary storage shape.", nameof(binaryStorage));
            }
            else if (binaryStorage != CalculatedBinaryKeyStorage.None || binaryKeyBytes != 0)
            {
                throw new ArgumentException("Only calculated byte-array results can define a binary storage shape.", nameof(binaryStorage));
            }
            if (virtualPropertyTiming is not null)
                virtualPropertyTiming.ReaderPreparationTicks += Stopwatch.GetTimestamp() - readerPreparationStarted;

            long mutationGateStarted = Stopwatch.GetTimestamp();
            using var indexMutation = EnterPartialIndexMutationScope();
            if (virtualPropertyTiming is not null)
                virtualPropertyTiming.MutationGateTicks += Stopwatch.GetTimestamp() - mutationGateStarted;
            long registrationPreparationStarted = Stopwatch.GetTimestamp();
            ThrowIfLibraDexSimpleIndexBackingConflicts(logicalName, ownerCatalog);
            bool indexExisted = indexes.HasIndex(physicalName);
            var keyContract = IndexKeys.NonUnique;
            var scalarOptions = new IndexOptions
            {
                Keys = keyContract,
                IdentityKeyMultiplicity = keyType == typeof(byte[]) && binaryStorage == CalculatedBinaryKeyStorage.VariableLength
                    ? IdentityKeyMultiplicity.MultipleKeysPerIdentity
                    : IdentityKeyMultiplicity.SingleKeyPerIdentity,
                DateKeys = keyType == typeof(DateTime) ||
                    keyType == typeof(DateTimeOffset) ||
                    keyType == typeof(DateOnly) ||
                    keyType == typeof(TimeOnly)
                        ? DateKeys.ExactAndStructured
                        : DateKeys.Exact,
                DateTimeKeyEncoding = keyType == typeof(DateTime) ||
                    keyType == typeof(DateTimeOffset) ||
                    keyType == typeof(TimeOnly)
                        ? DateTimeKeyEncoding.PrecisionSdt
                        : DateTimeKeyEncoding.CalendarSdt,
                SortOrder = direction == IndexDirection.Descending
                    ? LibraDexIndexSortOrder.Descending
                    : LibraDexIndexSortOrder.Ascending
            };
            OrdinaryIndexIntentEntry? intent = ownerCatalog is null && manageIntent
                ? PrepareOrdinaryIndexIntent(
                    intentKind,
                    logicalName,
                    physicalName,
                    unique,
                    profile,
                    direction,
                    selectorDescriptor: selector?.Serialize(),
                    resultType: keyType,
                    includeNullValues: includeNullValues,
                    includeEmptyStrings: includeEmptyStrings,
                    calculatedBinaryStorage: binaryStorage,
                    maximumKeyBytes: binaryKeyBytes,
                    functionId: functionId,
                    functionVersion: functionVersion)
                : null;
            if (virtualPropertyTiming is not null)
                virtualPropertyTiming.RegistrationPreparationTicks += Stopwatch.GetTimestamp() - registrationPreparationStarted;

            if (indexExisted && indexes.TryGetInfo(physicalName, out var persistedCalculated) && persistedCalculated.SortOrder != scalarOptions.SortOrder)
                throw new IndexContractException(IndexContractFailureKind.Configuration,
                    $"Calculated index '{logicalName}' requests sort direction '{direction}', but its persisted physical sort order is '{persistedCalculated.SortOrder}'.",
                    logicalName, physicalName);

            IIndex index;
            long physicalDefinitionStarted = Stopwatch.GetTimestamp();
            try
            {
                if (keyType == typeof(string))
                {
                    CatalogNamedStringKeyBuilder stringBuilder = indexes.Define(physicalName).StringKeys();
                    index = indexExisted
                        ? stringBuilder.Open()
                        : stringBuilder.Create(
                            ToLibraDexStringKeys(profile),
                            ToLibraDexProjectionDirections(direction),
                            scalarOptions.SortOrder);
                }
                else if (keyType == typeof(byte[]) && binaryStorage == CalculatedBinaryKeyStorage.VariableLength)
                {
                    index = indexes.Define(physicalName).Blob.Variable<ulong>(binaryKeyBytes).CreateOrOpen(keyContract, scalarOptions);
                }
                else if (keyType == typeof(byte[]) && binaryStorage == CalculatedBinaryKeyStorage.FixedLength)
                {
                    LibraDexScalarWidth width = binaryKeyBytes switch
                    {
                        8 => LibraDexScalarWidth.Bytes8,
                        16 => LibraDexScalarWidth.Bytes16,
                        32 => LibraDexScalarWidth.Bytes32,
                        _ => throw new ArgumentOutOfRangeException(nameof(binaryKeyBytes), binaryKeyBytes, "Unsupported calculated fixed binary width.")
                    };
                    index = indexes.Define(physicalName).Blob.Scalar<ulong>(width, ToLibraDexProjectionDirections(direction)).CreateOrOpen(keyContract, scalarOptions);
                }
                else if (keyType == typeof(int)) index = indexes.Define(physicalName).Int32Keys<ulong>().CreateOrOpen(keyContract, scalarOptions);
                else if (keyType == typeof(long)) index = indexes.Define(physicalName).Int64Keys<ulong>().CreateOrOpen(keyContract, scalarOptions);
                else if (keyType == typeof(uint)) index = indexes.CreateOrOpen<uint, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(ulong)) index = indexes.CreateOrOpen<ulong, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(short)) index = indexes.CreateOrOpen<short, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(ushort)) index = indexes.CreateOrOpen<ushort, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(byte)) index = indexes.CreateOrOpen<byte, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(sbyte)) index = indexes.CreateOrOpen<sbyte, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(char)) index = indexes.CreateOrOpen<char, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(Int128)) index = indexes.CreateOrOpen<Int128, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(UInt128)) index = indexes.CreateOrOpen<UInt128, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(float)) index = indexes.Define(physicalName).SingleKeys<ulong>().CreateOrOpen(keyContract, scalarOptions);
                else if (keyType == typeof(double)) index = indexes.Define(physicalName).DoubleKeys<ulong>().CreateOrOpen(keyContract, scalarOptions);
                else if (keyType == typeof(decimal)) index = indexes.Define(physicalName).DecimalKeys<ulong>().CreateOrOpen(keyContract, scalarOptions);
                else if (keyType == typeof(bool)) index = indexes.CreateOrOpen<bool, ulong>(physicalName, keyContract, scalarOptions);
                else if (keyType == typeof(Guid)) index = indexes.Define(physicalName).GuidKeys<ulong>().CreateOrOpen(keyContract, scalarOptions);
                else if (keyType == typeof(DateTime)) index = indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<DateTime, ulong>(DateKeys.ExactAndStructured, DateTimeKeyEncoding.PrecisionSdt, keyContract, sortOrder: scalarOptions.SortOrder), scalarOptions);
                else if (keyType == typeof(DateTimeOffset)) index = indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<DateTimeOffset, ulong>(DateKeys.ExactAndStructured, DateTimeKeyEncoding.PrecisionSdt, keyContract, sortOrder: scalarOptions.SortOrder), scalarOptions);
                else if (keyType == typeof(DateOnly)) index = indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<DateOnly, ulong>(DateKeys.ExactAndStructured, keys: keyContract, sortOrder: scalarOptions.SortOrder), scalarOptions);
                else if (keyType == typeof(TimeOnly)) index = indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<TimeOnly, ulong>(DateKeys.ExactAndStructured, DateTimeKeyEncoding.PrecisionSdt, keyContract, sortOrder: scalarOptions.SortOrder), scalarOptions);
                else if (keyType == typeof(TimeSpan)) index = indexes.CreateOrOpen(indexes.Define(physicalName).Shape.Date<TimeSpan, ulong>(DateKeys.Exact, keys: keyContract, sortOrder: scalarOptions.SortOrder), scalarOptions);
                else return false;
            }
            catch (Exception exception) when (exception is not IndexContractException)
            {
                throw new IndexContractException(
                    indexExisted ? IndexContractFailureKind.Configuration : IndexContractFailureKind.BackendMutation,
                    indexExisted
                        ? $"Calculated index '{logicalName}' conflicts with its persisted ordered-backend definition."
                        : $"Calculated index '{logicalName}' could not create its ordered-backend definition.",
                    logicalName,
                    logicalName,
                    innerException: exception);
            }
            if (virtualPropertyTiming is not null)
                virtualPropertyTiming.PhysicalDefinitionTicks += Stopwatch.GetTimestamp() - physicalDefinitionStarted;

            long runtimeAttachmentStarted = Stopwatch.GetTimestamp();
            if (ownerCatalog is null)
                EnsureLibraDexStateIndex(indexes);
            IndexBackendCapabilities capabilities = IndexBackendCapabilities.Equality | IndexBackendCapabilities.Range | IndexBackendCapabilities.OrderedRead;
            if (unique)
                capabilities |= IndexBackendCapabilities.Unique;
            if (ownerCatalog is not null)
                capabilities |= IndexBackendCapabilities.Volatile;
            var entry = new LibraDexSimpleIndex(
                logicalName,
                keyType,
                unique,
                capabilities,
                index,
                ownerCatalog,
                autoCreated: false,
                AutoIndexUse.None,
                DateTime.UtcNow,
                calculatedSelector: selector,
                calculatedReader: reader,
                functionRuntimeReader: functionRuntimeReader,
                includeNullValues: includeNullValues,
                includeEmptyStrings: includeEmptyStrings,
                maximumKeyBytes: keyType == typeof(byte[]) ? binaryKeyBytes : null,
                fixedBinaryKeyBytes: binaryStorage == CalculatedBinaryKeyStorage.FixedLength ? binaryKeyBytes : null,
                isFunctionIndex: functionId is not null,
                isVirtualProperty: isVirtualProperty);

            if (preparedPopulation is not null)
            {
                for (var preparedIndex = 0; preparedIndex < preparedPopulation.Count; preparedIndex++)
                {
                    var (preparedRecordID, preparedKey) = preparedPopulation[preparedIndex];
                    ValidateLibraDexBinaryKeyLength(entry, preparedKey, preparedRecordID);
                }
            }

            lock (libraDexIndexLock)
            {
                LibraDexSimpleIndex[] current = libraDexSimpleIndexes;
                for (int i = 0; i < current.Length; i++)
                {
                    if (!string.Equals(current[i].Path, logicalName, StringComparison.Ordinal))
                        continue;
                    LibraDexSimpleIndex[] next = (LibraDexSimpleIndex[])current.Clone();
                    Catalog? priorCatalog = next[i].OwnerCatalog;
                    next[i] = entry;
                    libraDexSimpleIndexes = next;
                    if (!ReferenceEquals(priorCatalog, ownerCatalog) && ShouldDisposeLibraDexCatalog(priorCatalog))
                        priorCatalog?.Dispose();
                    if (virtualPropertyTiming is not null)
                        virtualPropertyTiming.RuntimeAttachmentTicks += Stopwatch.GetTimestamp() - runtimeAttachmentStarted;

                    long replacementPopulationStarted = Stopwatch.GetTimestamp();
                    if (preparedPopulation is not null)
                        RebuildPreparedLibraDexSimpleIndex(entry, preparedPopulation);
                    else if (populateExisting || !indexExisted)
                        PopulateLibraDexSimpleIndex(entry, physicalIndexIsNew: !indexExisted);
                    if (virtualPropertyTiming is not null)
                        virtualPropertyTiming.PopulationTicks += Stopwatch.GetTimestamp() - replacementPopulationStarted;

                    long replacementCompletionStarted = Stopwatch.GetTimestamp();
                    CompleteOrdinaryIndexRegistration(ownerCatalog, intent);
                    if (isVirtualProperty)
                        RegisterVirtualPropertyRuntime(entry, preparedPopulation, virtualValuesAreAuthoritative);
                    if (virtualPropertyTiming is not null)
                        virtualPropertyTiming.CompletionTicks += Stopwatch.GetTimestamp() - replacementCompletionStarted;
                    return true;
                }

                LibraDexSimpleIndex[] appended = new LibraDexSimpleIndex[current.Length + 1];
                Array.Copy(current, appended, current.Length);
                appended[current.Length] = entry;
                libraDexSimpleIndexes = appended;
            }
            if (virtualPropertyTiming is not null)
                virtualPropertyTiming.RuntimeAttachmentTicks += Stopwatch.GetTimestamp() - runtimeAttachmentStarted;

            long appendedPopulationStarted = Stopwatch.GetTimestamp();
            if (preparedPopulation is not null)
                RebuildPreparedLibraDexSimpleIndex(entry, preparedPopulation);
            else if (populateExisting || !indexExisted)
                PopulateLibraDexSimpleIndex(entry, physicalIndexIsNew: !indexExisted);
            if (virtualPropertyTiming is not null)
                virtualPropertyTiming.PopulationTicks += Stopwatch.GetTimestamp() - appendedPopulationStarted;

            long appendedCompletionStarted = Stopwatch.GetTimestamp();
            CompleteOrdinaryIndexRegistration(ownerCatalog, intent);
            if (isVirtualProperty)
                RegisterVirtualPropertyRuntime(entry, preparedPopulation, virtualValuesAreAuthoritative);
            if (virtualPropertyTiming is not null)
                virtualPropertyTiming.CompletionTicks += Stopwatch.GetTimestamp() - appendedCompletionStarted;
            return true;
        }

        private bool TryRegisterLibraDexSimplePathIndex(string propPath, bool unique, CatalogIdentityGroupIndexes indexes, Catalog? ownerCatalog, bool populateExisting, bool autoCreated, AutoIndexUse autoUse, bool manageIntent = true, bool recoverPreparingIntent = false, IndexDirection direction = IndexDirection.Ascending)
        {
            if (!TryResolveSimplePropertyType(propPath, out var keyType, out Inheto.PropPathCheck pathCheck))
                return false;
            propPath = pathCheck.CanonicalPath;

            using var indexMutation = EnterPartialIndexMutationScope();
            ThrowIfLibraDexSimpleIndexBackingConflicts(propPath, ownerCatalog);
            keyType = Nullable.GetUnderlyingType(keyType) ?? keyType;
            // Abraxas uniqueness intentionally excludes null keys. The physical index therefore remains
            // non-unique while Abraxas preflights every non-null unique key under the store mutation gate.
            var keyContract = IndexKeys.NonUnique;
            var sortOrder = direction == IndexDirection.Descending
                ? LibraDexIndexSortOrder.Descending
                : LibraDexIndexSortOrder.Ascending;
            var singleValueOptions = new IndexOptions
            {
                Keys = keyContract,
                SortOrder = sortOrder,
                IdentityKeyMultiplicity = IdentityKeyMultiplicity.SingleKeyPerIdentity,
                DateKeys = keyType == typeof(DateTime) ||
                    keyType == typeof(DateTimeOffset) ||
                    keyType == typeof(DateOnly) ||
                    keyType == typeof(TimeOnly)
                        ? DateKeys.ExactAndStructured
                        : DateKeys.Exact,
                DateTimeKeyEncoding = keyType == typeof(DateTime) ||
                    keyType == typeof(DateTimeOffset) ||
                    keyType == typeof(TimeOnly)
                        ? DateTimeKeyEncoding.PrecisionSdt
                        : DateTimeKeyEncoding.CalendarSdt
            };
            var indexExisted = indexes.HasIndex(propPath);
            if (indexExisted && indexes.TryGetInfo(propPath, out var persistedIndex) && persistedIndex.SortOrder != sortOrder)
                throw new IndexContractException(IndexContractFailureKind.Configuration,
                    $"Index '{propPath}' requests sort direction '{direction}', but its persisted physical sort order is '{persistedIndex.SortOrder}'.",
                    propPath, propPath);
            IIndex index;
            var isCustomCodec = false;
            var runtimeCodecAvailable = TryGetCustomBinaryCodec(keyType, propPath, out var registeredCodec);
            if (activeLibraDexGroupedBackfill is not null && runtimeCodecAvailable)
                throw new NotSupportedException("Grouped index creation does not yet support application-defined scalar codecs.");
            var preparedCodecBinding = false;
            if (!IsSupportedLibraDexScalarKeyType(keyType) && !runtimeCodecAvailable)
                return false;
            var ordinaryIntent = ownerCatalog is null && manageIntent
                ? PrepareOrdinaryIndexIntent(
                    OrdinaryIndexIntentKind.Scalar,
                    propPath,
                    propPath,
                    unique,
                    direction: direction)
                : null;

            try
            {
                if (keyType == typeof(int))
                    index = indexes.Define(propPath).Int32Keys<ulong>().CreateOrOpen(keyContract, singleValueOptions);
                else if (keyType == typeof(long))
                    index = indexes.Define(propPath).Int64Keys<ulong>().CreateOrOpen(keyContract, singleValueOptions);
                else if (keyType == typeof(uint))
                    index = indexes.CreateOrOpen<uint, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(ulong))
                    index = indexes.CreateOrOpen<ulong, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(short))
                    index = indexes.CreateOrOpen<short, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(ushort))
                    index = indexes.CreateOrOpen<ushort, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(byte))
                    index = indexes.CreateOrOpen<byte, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(sbyte))
                    index = indexes.CreateOrOpen<sbyte, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(char))
                    index = indexes.CreateOrOpen<char, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(Int128))
                    index = indexes.CreateOrOpen<Int128, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(UInt128))
                    index = indexes.CreateOrOpen<UInt128, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(float))
                    index = indexes.Define(propPath).SingleKeys<ulong>().CreateOrOpen(keyContract, singleValueOptions);
                else if (keyType == typeof(double))
                    index = indexes.Define(propPath).DoubleKeys<ulong>().CreateOrOpen(keyContract, singleValueOptions);
                else if (keyType == typeof(decimal))
                    index = indexes.Define(propPath).DecimalKeys<ulong>().CreateOrOpen(keyContract, singleValueOptions);
                else if (keyType == typeof(bool))
                    index = indexes.CreateOrOpen<bool, ulong>(propPath, keyContract, singleValueOptions);
                else if (keyType == typeof(Guid))
                    index = indexes.Define(propPath).GuidKeys<ulong>().CreateOrOpen(keyContract, singleValueOptions);
                else if (keyType == typeof(string))
                    index = indexes.Define(propPath).StringKeys().CreateOrOpen(StringKeys.Exact, sortOrder: sortOrder);
                else if (keyType == typeof(DateTime))
                {
                    var shape = indexes.Define(propPath).Shape.Date<DateTime, ulong>(
                        DateKeys.ExactAndStructured,
                        DateTimeKeyEncoding.PrecisionSdt,
                        keyContract,
                        sortOrder: sortOrder);
                    index = indexes.CreateOrOpen(shape, singleValueOptions);
                }
                else if (keyType == typeof(DateTimeOffset))
                {
                    var shape = indexes.Define(propPath).Shape.Date<DateTimeOffset, ulong>(
                        DateKeys.ExactAndStructured,
                        DateTimeKeyEncoding.PrecisionSdt,
                        keyContract,
                        sortOrder: sortOrder);
                    index = indexes.CreateOrOpen(shape, singleValueOptions);
                }
                else if (keyType == typeof(DateOnly))
                {
                    var shape = indexes.Define(propPath).Shape.Date<DateOnly, ulong>(
                        DateKeys.ExactAndStructured,
                        keys: keyContract,
                        sortOrder: sortOrder);
                    index = indexes.CreateOrOpen(shape, singleValueOptions);
                }
                else if (keyType == typeof(TimeOnly))
                {
                    var shape = indexes.Define(propPath).Shape.Date<TimeOnly, ulong>(
                        DateKeys.ExactAndStructured,
                        DateTimeKeyEncoding.PrecisionSdt,
                        keyContract,
                        sortOrder: sortOrder);
                    index = indexes.CreateOrOpen(shape, singleValueOptions);
                }
                else if (keyType == typeof(TimeSpan))
                {
                    var shape = indexes.Define(propPath).Shape.Date<TimeSpan, ulong>(
                        DateKeys.Exact,
                        keys: keyContract,
                        sortOrder: sortOrder);
                    index = indexes.CreateOrOpen(shape, singleValueOptions);
                }
                else if (runtimeCodecAvailable)
                {
                    isCustomCodec = true;
                    if (ownerCatalog is null)
                    {
                        var expected = CreateApplicationCodecBinding(
                            registeredCodec,
                            ApplicationCodecIndexKind.Ordinary,
                            propPath,
                            propPath,
                            propPath,
                            keyType,
                            ready: true);
                        if (indexExisted)
                        {
                            RequireReadyApplicationCodecBinding(expected, allowPreparing: recoverPreparingIntent);
                        }
                        else
                        {
                            PersistPreparingApplicationCodecBinding(expected);
                            preparedCodecBinding = true;
                        }
                    }

                    index = indexes.Define(propPath).Blob.Scalar<ulong>(registeredCodec.Width).CreateOrOpen(keyContract, singleValueOptions);
                }
                else
                    return false;
            }
            catch (IndexContractException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (preparedCodecBinding)
                {
                    try
                    {
                        rbase.RemoveApplicationCodecBinding(
                            storeID,
                            ApplicationCodecIndexKind.Ordinary,
                            propPath);
                    }
                    catch
                    {
                    }
                }

                throw new IndexContractException(
                    indexExisted
                        ? IndexContractFailureKind.Configuration
                        : IndexContractFailureKind.BackendMutation,
                    indexExisted
                        ? $"Index '{propPath}' conflicts with its persisted ordered-backend definition."
                        : $"Index '{propPath}' could not create its ordered-backend definition.",
                    propPath,
                    propPath,
                    innerException: exception);
            }

            if (ownerCatalog is null)
                EnsureLibraDexStateIndex(indexes);

            var capabilities = IndexBackendCapabilities.Equality;
            if (!isCustomCodec || registeredCodec.OrderPreserving)
                capabilities |= IndexBackendCapabilities.Range | IndexBackendCapabilities.OrderedRead;
            if (unique)
                capabilities |= IndexBackendCapabilities.Unique;
            if (ownerCatalog is not null)
                capabilities |= IndexBackendCapabilities.Volatile;
            if (autoCreated)
                capabilities |= IndexBackendCapabilities.AutoCreated;

            var customReader = isCustomCodec ? registeredCodec.ReadKey : null;
            var customPayloadReader = isCustomCodec ? registeredCodec.ReadPayload : null;
            var entry = new LibraDexSimpleIndex(
                propPath,
                keyType,
                unique,
                capabilities,
                index,
                ownerCatalog,
                autoCreated,
                autoUse,
                DateTime.UtcNow,
                customReader,
                customPayloadReader,
                calculatedReader: CreateClrContinuationReader(propPath, keyType));

            if (TryStageLibraDexGroupedBackfill(entry, indexes, ownerCatalog, ordinaryIntent, indexExisted))
                return true;

            if (!indexExisted)
            {
                try
                {
                    PopulateLibraDexSimpleIndex(entry, physicalIndexIsNew: true);
                    if (preparedCodecBinding)
                    {
                        PersistApplicationCodecBinding(
                            registeredCodec,
                            ApplicationCodecIndexKind.Ordinary,
                            propPath,
                            propPath,
                            propPath,
                            keyType,
                            ready: true);
                    }
                }
                catch
                {
                    RollBackNewLibraDexIndexAfterPopulationFailure(indexes, propPath);
                    if (preparedCodecBinding)
                    {
                        try
                        {
                            rbase.RemoveApplicationCodecBinding(
                                storeID,
                                ApplicationCodecIndexKind.Ordinary,
                                propPath);
                        }
                        catch
                        {
                        }
                    }
                    throw;
                }
            }

            lock (libraDexIndexLock)
            {
                var current = libraDexSimpleIndexes;
                for (var i = 0; i < current.Length; i++)
                {
                    if (string.Equals(current[i].Path, propPath, StringComparison.Ordinal))
                    {
                        var next = (LibraDexSimpleIndex[])current.Clone();
                        var priorCatalog = next[i].OwnerCatalog;
                        next[i] = entry;
                        libraDexSimpleIndexes = next;
                        if (!ReferenceEquals(priorCatalog, ownerCatalog) && ShouldDisposeLibraDexCatalog(priorCatalog))
                            priorCatalog?.Dispose();

                        CompleteOrdinaryIndexRegistration(ownerCatalog, ordinaryIntent);
                        return true;
                    }
                }

                var appended = new LibraDexSimpleIndex[current.Length + 1];
                Array.Copy(current, appended, current.Length);
                appended[current.Length] = entry;
                libraDexSimpleIndexes = appended;
            }

            CompleteOrdinaryIndexRegistration(ownerCatalog, ordinaryIntent);
            return true;
        }

        private bool TryRegisterLibraDexTextPathIndex(string propPath, TextIndexProfile profile, IndexDirection direction, bool unique, CatalogIdentityGroupIndexes indexes, Catalog? ownerCatalog, bool populateExisting, bool manageIntent = true, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
        {
            if (!TryResolveSimplePropertyType(propPath, out var keyType, out Inheto.PropPathCheck pathCheck))
                return false;
            propPath = pathCheck.CanonicalPath;

            using var indexMutation = EnterPartialIndexMutationScope();
            ThrowIfLibraDexSimpleIndexBackingConflicts(propPath, ownerCatalog);
            keyType = NormalizeValueType(keyType);
            if (keyType != typeof(string))
                return false;
            lock (libraDexIndexLock)
            {
                foreach (var attached in libraDexSimpleIndexes)
                {
                    if (string.Equals(attached.Path, propPath, StringComparison.Ordinal) &&
                        ReferenceEquals(attached.OwnerCatalog, ownerCatalog) && attached.Unique != unique)
                        throw new IndexContractException(IndexContractFailureKind.Configuration,
                            $"Text index '{propPath}' requests uniqueness that conflicts with its attached definition.", propPath, propPath);
                }
            }
            var stringKeys = ToLibraDexStringKeys(profile);
            if (sortKeyProfiles is { Count: > 0 })
                stringKeys |= StringKeys.SortKey;
            var directions = ToLibraDexProjectionDirections(direction);
            var sortOrder = direction == IndexDirection.Descending
                ? LibraDexIndexSortOrder.Descending
                : LibraDexIndexSortOrder.Ascending;
            var ordinaryIntent = ownerCatalog is null && manageIntent
                ? PrepareOrdinaryIndexIntent(
                    OrdinaryIndexIntentKind.Text,
                    propPath,
                    propPath,
                    unique,
                    profile,
                    direction,
                    textSortProfiles: sortKeyProfiles)
                : null;

            var indexExisted = indexes.HasIndex(propPath);
            if (indexExisted && indexes.TryGetInfo(propPath, out var persisted))
            {
                if (persisted.SortOrder != sortOrder)
                    throw new IndexContractException(IndexContractFailureKind.Configuration,
                        $"Text index '{propPath}' requests sort direction '{direction}', but its persisted physical sort order is '{persisted.SortOrder}'.",
                        propPath, propPath);
                if (!manageIntent && ownerCatalog is null &&
                    TryAddMissingTextSortProfiles(indexes, propPath, stringKeys, directions, sortKeyProfiles, persisted))
                    _ = indexes.TryGetInfo(propPath, out persisted);

                if (persisted.StringKeys != stringKeys ||
                    persisted.Directions != directions ||
                    persisted.SortOrder != sortOrder ||
                    !TextSortProfilesMatch(sortKeyProfiles, persisted.SortKeyProfiles))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Configuration,
                        $"Text index '{propPath}' requests profile '{profile}' and direction '{direction}', but its persisted definition uses '{persisted.StringKeys}' and '{persisted.Directions}'.",
                        propPath,
                        propPath);
                }
            }

            IIndex index;
            try
            {
                index = indexes.Define(propPath).StringKeys().CreateOrOpen(
                    stringKeys,
                    directions,
                    sortOrder: sortOrder,
                    sortKeyProfiles: ToLibraDexSortKeyProfiles(sortKeyProfiles));
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    indexExisted
                        ? IndexContractFailureKind.Configuration
                        : IndexContractFailureKind.BackendMutation,
                    indexExisted
                        ? $"Text index '{propPath}' conflicts with its persisted ordered-backend definition."
                        : $"Text index '{propPath}' could not create its ordered-backend definition.",
                    propPath,
                    propPath,
                    innerException: exception);
            }
            if (ownerCatalog is null)
                EnsureLibraDexStateIndex(indexes);

            var capabilities = IndexBackendCapabilities.Equality | IndexBackendCapabilities.Range | IndexBackendCapabilities.OrderedRead;
            if (unique)
                capabilities |= IndexBackendCapabilities.Unique;
            if (ownerCatalog is not null)
                capabilities |= IndexBackendCapabilities.Volatile;

            var entry = new LibraDexSimpleIndex(
                propPath,
                keyType,
                unique,
                capabilities,
                index,
                ownerCatalog,
                autoCreated: false,
                AutoIndexUse.None,
                DateTime.UtcNow,
                calculatedReader: CreateClrContinuationReader(propPath, keyType));
            if (TryStageLibraDexGroupedBackfill(entry, indexes, ownerCatalog, ordinaryIntent, indexExisted))
                return true;
            if (!indexExisted)
            {
                try
                {
                    PopulateLibraDexSimpleIndex(entry, physicalIndexIsNew: true);
                }
                catch
                {
                    RollBackNewLibraDexIndexAfterPopulationFailure(indexes, propPath);
                    throw;
                }
            }

            lock (libraDexIndexLock)
            {
                var current = libraDexSimpleIndexes;
                for (var i = 0; i < current.Length; i++)
                {
                    if (string.Equals(current[i].Path, propPath, StringComparison.Ordinal))
                    {
                        var next = (LibraDexSimpleIndex[])current.Clone();
                        var priorCatalog = next[i].OwnerCatalog;
                        next[i] = entry;
                        libraDexSimpleIndexes = next;
                        if (!ReferenceEquals(priorCatalog, ownerCatalog) && ShouldDisposeLibraDexCatalog(priorCatalog))
                            priorCatalog?.Dispose();

                        CompleteOrdinaryIndexRegistration(ownerCatalog, ordinaryIntent);
                        return true;
                    }
                }

                var appended = new LibraDexSimpleIndex[current.Length + 1];
                Array.Copy(current, appended, current.Length);
                appended[current.Length] = entry;
                libraDexSimpleIndexes = appended;
            }

            CompleteOrdinaryIndexRegistration(ownerCatalog, ordinaryIntent);
            return true;
        }

        /// <summary>
        /// Removes a newly created physical LibraDex definition when authoritative population fails before the runtime descriptor is published.<br/>
        /// Rollback is best-effort so the original population exception remains authoritative; a later catalog recovery can still diagnose a storage failure that also prevented the drop.<br/>
        /// </summary>
        /// <param name="indexes">The persistent or volatile identity group that owns the failed definition.<br/></param>
        /// <param name="propPath">The canonical logical index name created for the property path.<br/></param>
        private static void RollBackNewLibraDexIndexAfterPopulationFailure(
            CatalogIdentityGroupIndexes indexes,
            string propPath)
        {
            try
            {
                _ = indexes.Drop(propPath);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Rejects an ordinary-index registration that would replace an attached descriptor owned by the other backing kind.<br/>
        /// Persistent and volatile catalogs are maintained independently, so silently replacing the sole runtime descriptor could leave the displaced physical index stale after later record mutations.<br/>
        /// Repeating a registration against the same owner remains idempotent and may continue through the normal physical-shape validation path.<br/>
        /// </summary>
        /// <param name="propPath">The normalized ordinary property path that identifies the runtime descriptor.<br/></param>
        /// <param name="requestedOwnerCatalog">The requested volatile owner, or <c>null</c> for persistent registration.<br/></param>
        private void ThrowIfLibraDexSimpleIndexBackingConflicts(string propPath, Catalog? requestedOwnerCatalog)
        {
            string logicalName = requestedOwnerCatalog is null
                ? "lidx_" + name + "_" + SanitizeTokenForIndex(propPath, 40) + "_libradex"
                : "memidx_" + name + "_" + SanitizeTokenForIndex(propPath, 40);
            ThrowIfLibraDexLogicalNameBackingConflicts(logicalName, requestedOwnerCatalog);
            lock (libraDexIndexLock)
            {
                var current = libraDexSimpleIndexes;
                for (var i = 0; i < current.Length; i++)
                {
                    var existing = current[i];
                    if (!string.Equals(existing.Path, propPath, StringComparison.Ordinal) ||
                        ReferenceEquals(existing.OwnerCatalog, requestedOwnerCatalog))
                    {
                        continue;
                    }

                    var requestedBacking = requestedOwnerCatalog is null ? "persistent" : "volatile";
                    var existingBacking = existing.OwnerCatalog is null ? "persistent" : "volatile";
                    throw new IndexContractException(
                        IndexContractFailureKind.Configuration,
                        $"Index '{propPath}' cannot be created as {requestedBacking} because an attached {existingBacking} index already owns that property path.",
                        propPath,
                        propPath);
                }
            }
        }

        /// <summary>
        /// Rejects a public logical name already attached to the other backing kind, even when the two shapes belong to different index families.<br/>
        /// Public index handles carry a logical name rather than a catalog identity, so allowing this collision would make explicit selection and removal ambiguous.<br/>
        /// </summary>
        /// <param name="logicalName">Proposed public index name.<br/></param>
        /// <param name="requestedOwnerCatalog">Volatile owner, or <see langword="null"/> for a persistent index.<br/></param>
        private void ThrowIfLibraDexLogicalNameBackingConflicts(string logicalName, Catalog? requestedOwnerCatalog)
        {
            lock (libraDexIndexLock)
            {
                foreach (LibraDexCompositeIndex entry in libraDexCompositeIndexes)
                {
                    if (string.Equals(entry.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase) &&
                        !ReferenceEquals(entry.OwnerCatalog, requestedOwnerCatalog))
                        throw new IndexContractException(IndexContractFailureKind.Configuration, $"Index name '{logicalName}' is already attached with the other backing kind.", entry.PhysicalName, logicalName);
                }
                foreach (LibraDexPartialIndex entry in libraDexPartialIndexes)
                {
                    if (string.Equals(entry.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase) &&
                        !ReferenceEquals(entry.OwnerCatalog, requestedOwnerCatalog))
                        throw new IndexContractException(IndexContractFailureKind.Configuration, $"Index name '{logicalName}' is already attached with the other backing kind.", entry.PhysicalName, logicalName);
                }
                foreach (LibraDexRegexProjectionIndex entry in libraDexRegexProjectionIndexes)
                {
                    if (string.Equals(entry.LogicalPrefix, logicalName, StringComparison.OrdinalIgnoreCase) &&
                        !ReferenceEquals(entry.OwnerCatalog, requestedOwnerCatalog))
                        throw new IndexContractException(IndexContractFailureKind.Configuration, $"Index name '{logicalName}' is already attached with the other backing kind.", entry.Index.Name, logicalName);
                }
                foreach (LibraDexSimpleIndex entry in libraDexSimpleIndexes)
                {
                    string attachedName = entry.OwnerCatalog is null
                        ? "lidx_" + name + "_" + SanitizeTokenForIndex(entry.Path, 40) + "_libradex"
                        : "memidx_" + name + "_" + SanitizeTokenForIndex(entry.Path, 40);
                    if (string.Equals(attachedName, logicalName, StringComparison.OrdinalIgnoreCase) &&
                        !ReferenceEquals(entry.OwnerCatalog, requestedOwnerCatalog))
                        throw new IndexContractException(IndexContractFailureKind.Configuration, $"Index name '{logicalName}' is already attached with the other backing kind.", entry.Index.Name, logicalName);
                }
            }
        }

        /// <summary>
        /// Persists one preparing ordinary-index definition before its physical LibraDex creation or recovery begins.<br/>
        /// An existing definition must match exactly so repeated setup remains idempotent without silently changing developer-owned semantics.<br/>
        /// </summary>
        /// <param name="kind">The scalar, text, or bounded-binary recreation shape.<br/></param>
        /// <param name="logicalName">The stable Abraxas logical index identity.<br/></param>
        /// <param name="physicalName">The LibraDex catalog definition name.<br/></param>
        /// <param name="unique">Whether Abraxas enforces uniqueness for non-null keys.<br/></param>
        /// <param name="textProfile">The text representation profile when <paramref name="kind"/> is text.<br/></param>
        /// <param name="direction">The maintained text ordering direction when <paramref name="kind"/> is text.<br/></param>
        /// <param name="maximumKeyBytes">The developer payload bound when <paramref name="kind"/> is binary.<br/></param>
        /// <param name="textSortProfiles">Optional culture sort-key profiles when <paramref name="kind"/> is text.<br/></param>
        /// <param name="selectorDescriptor">Serialized backend-neutral calculated selector semantics, or null when this index family does not use a stored selector.<br/></param>
        /// <param name="resultType">The exact calculated result CLR type, or null for a direct property index.<br/></param>
        /// <param name="includeNullValues">Whether calculated missing, null, or invalid values participate in the null-key route.<br/></param>
        /// <param name="includeEmptyStrings">Whether calculated empty-string values remain indexed.<br/></param>
        /// <param name="calculatedBinaryStorage">The variable or fixed physical storage shape for calculated binary keys, or none for non-binary keys.<br/></param>
        /// <param name="functionId">Stable application function identity for a function-calculated index, or null for other index families.<br/></param>
        /// <param name="functionVersion">Positive semantic version of the application function, or zero when the index is not function-calculated.<br/></param>
        /// <returns>The existing matching definition or newly persisted preparing definition.<br/></returns>
        private OrdinaryIndexIntentEntry PrepareOrdinaryIndexIntent(
            OrdinaryIndexIntentKind kind,
            string logicalName,
            string physicalName,
            bool unique,
            TextIndexProfile textProfile = TextIndexProfile.Exact,
            IndexDirection direction = IndexDirection.Ascending,
            int maximumKeyBytes = 0,
            byte[]? selectorDescriptor = null,
            Type? resultType = null,
            bool includeNullValues = false,
            bool includeEmptyStrings = false,
            CalculatedBinaryKeyStorage calculatedBinaryStorage = CalculatedBinaryKeyStorage.None,
            string? functionId = null,
            int functionVersion = 0,
            IReadOnlyList<TextSortProfile>? textSortProfiles = null)
        {
            var expected = new OrdinaryIndexIntentEntry
            {
                StoreID = storeID,
                Kind = kind,
                LogicalName = logicalName,
                PhysicalName = physicalName,
                Unique = unique,
                TextProfile = textProfile,
                Direction = direction,
                MaximumKeyBytes = maximumKeyBytes,
                CalculatedBinaryStorage = calculatedBinaryStorage,
                SelectorDescriptor = selectorDescriptor ?? Array.Empty<byte>(),
                ResultTypeCode = resultType is null ? string.Empty : ConditionWireTypes.GetCode(resultType),
                FunctionId = functionId ?? string.Empty,
                FunctionVersion = functionVersion,
                IncludeNullValues = includeNullValues,
                IncludeEmptyStrings = includeEmptyStrings,
                TextSortProfiles = textSortProfiles is null ? Array.Empty<TextSortProfile>() : textSortProfiles.ToArray(),
                PopulationRevision = RequiresCurrentTextPopulationRevision(kind, resultType)
                    ? CurrentTextPopulationRevision
                    : 0,
                Ready = false
            };
            expected.Validate();

            if (rbase.TryGetOrdinaryIndexIntent(storeID, logicalName, out var existing))
            {
                if (!existing.ContractEquals(expected, includeReady: false))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Configuration,
                        $"Index '{logicalName}' requests an ordinary-index contract that conflicts with its persisted Abraxas definition.",
                        logicalName,
                        logicalName);
                }

                return existing;
            }

            rbase.UpsertOrdinaryIndexIntent(expected);
            return expected;
        }

        /// <summary>
        /// Publishes one successfully populated persistent ordinary-index definition before clearing any prior removal tombstone.<br/>
        /// Volatile and catalog-recovery registrations pass no active intent and therefore perform no durable mutation here.<br/>
        /// </summary>
        /// <param name="ownerCatalog">The volatile owner catalog, or <c>null</c> for persistent registration.<br/></param>
        /// <param name="intent">The preparing persistent definition, or <c>null</c> when intent is managed by the recovery caller.<br/></param>
        private void CompleteOrdinaryIndexRegistration(
            Catalog? ownerCatalog,
            OrdinaryIndexIntentEntry? intent)
        {
            if (ownerCatalog is not null || intent is null)
                return;

            intent.PopulationRevision = GetRequiredPopulationRevision(intent);
            intent.Ready = true;
            rbase.UpsertOrdinaryIndexIntent(intent);
            rbase.RemoveOrdinaryIndexRemoval(storeID, intent.LogicalName);
        }

        /// <summary>
        /// Determines whether one newly declared ordinary index uses the exact-string physical population contract.<br/>
        /// Direct text definitions and calculated string results share the corrected VS8 route-batch publication semantics.<br/>
        /// </summary>
        /// <param name="kind">Persisted ordinary-index intent family.<br/></param>
        /// <param name="resultType">Calculated result type, or null for direct property indexes.<br/></param>
        /// <returns><see langword="true"/> when the definition requires the current text population revision; otherwise <see langword="false"/>.<br/></returns>
        private static bool RequiresCurrentTextPopulationRevision(
            OrdinaryIndexIntentKind kind,
            Type? resultType)
            => kind == OrdinaryIndexIntentKind.Text ||
                (kind is OrdinaryIndexIntentKind.Calculated or OrdinaryIndexIntentKind.FunctionCalculated &&
                 NormalizeValueType(resultType ?? typeof(object)) == typeof(string));

        /// <summary>
        /// Resolves the engine-owned population revision required by one persisted ordinary-index intent.<br/>
        /// Revision zero remains valid for physical families unaffected by the historical exact-string route-batch publication defect.<br/>
        /// </summary>
        /// <param name="intent">Validated durable index intent.<br/></param>
        /// <returns>The minimum accepted population revision for the intent's physical family.<br/></returns>
        private static int GetRequiredPopulationRevision(OrdinaryIndexIntentEntry intent)
        {
            bool calculatedString = intent.Kind is OrdinaryIndexIntentKind.Calculated or OrdinaryIndexIntentKind.FunctionCalculated &&
                string.Equals(intent.ResultTypeCode, ConditionWireTypes.GetCode(typeof(string)), StringComparison.Ordinal);
            return intent.Kind == OrdinaryIndexIntentKind.Text || calculatedString
                ? CurrentTextPopulationRevision
                : 0;
        }

        /// <summary>
        /// Routes an attached <see cref="RecordIndex"/> culture-subindex request through the owning store's text-index lifecycle.<br/>
        /// </summary>
        /// <param name="index">The attached ordinary text-index handle.<br/></param>
        /// <param name="compareOptions">The exact .NET sort-key comparison options.<br/></param>
        /// <param name="culture">The resolved .NET culture.<br/></param>
        /// <returns><c>true</c> when a new physical companion was added; <c>false</c> when the exact profile already existed.<br/></returns>
        bool IRecordIndexOwner.AddTextSubIndex(RecordIndex index, CompareOptions compareOptions, CultureInfo culture)
            => AddTextSubIndex(index, compareOptions, culture);

        /// <summary>
        /// Persists append-only text-profile intent, backfills the LibraDex companion, publishes logical ownership, and replaces the runtime facade under the store mutation gate.<br/>
        /// Persistent preparing intent makes an interrupted operation recoverable on reopen; volatile indexes perform the same physical work without durable Abraxas intent.<br/>
        /// </summary>
        /// <param name="index">The attached ordinary text-index handle.<br/></param>
        /// <param name="compareOptions">The exact .NET sort-key comparison options.<br/></param>
        /// <param name="culture">The resolved .NET culture.<br/></param>
        /// <returns><c>true</c> when a new physical companion was added; <c>false</c> when the exact profile already existed.<br/></returns>
        private bool AddTextSubIndex(RecordIndex index, CompareOptions compareOptions, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            if (!index.IsOwnedBy(this))
                throw new InvalidOperationException("The supplied RecordIndex belongs to a different RecordStore.");

            _ = culture.CompareInfo.GetSortKey(string.Empty, compareOptions);
            var requested = new TextSortProfile(
                culture.Equals(CultureInfo.InvariantCulture) ? string.Empty : culture.Name,
                compareOptions);

            using var indexMutation = EnterPartialIndexMutationScope();
            LibraDexSimpleIndex entry = default;
            var entryOffset = -1;
            lock (libraDexIndexLock)
            {
                LibraDexSimpleIndex[] current = libraDexSimpleIndexes;
                for (var i = 0; i < current.Length; i++)
                {
                    if ((string.Equals(current[i].Path, index.KeyPath, StringComparison.Ordinal) ||
                        string.Equals(current[i].Path, index.Name, StringComparison.Ordinal)) &&
                        current[i].KeyType == typeof(string))
                    {
                        entry = current[i];
                        entryOffset = i;
                        break;
                    }
                }
            }
            if (entryOffset < 0)
                throw new InvalidOperationException("AddSubIndex is available only for an attached ordinary text index.");

            CatalogIdentityGroupIndexes? indexes = entry.OwnerCatalog is null
                ? libraDexIdentityIndexes
                : LibraDexVolatileIndexes;
            if (indexes is null || !indexes.TryGetInfo(entry.Path, out CatalogIndexInfo persisted))
                throw new InvalidOperationException($"Text index '{entry.Path}' is not attached to an active LibraDex definition.");

            TextSortProfile[] physicalProfiles = ResolveAbraxasPersistedSortProfiles(persisted);
            var physicalExists = false;
            for (var i = 0; i < physicalProfiles.Length; i++)
            {
                if (string.Equals(physicalProfiles[i].CultureName, requested.CultureName, StringComparison.Ordinal) &&
                    physicalProfiles[i].CompareOptions == requested.CompareOptions)
                {
                    physicalExists = true;
                    break;
                }
            }

            OrdinaryIndexIntentEntry? intent = null;
            if (entry.OwnerCatalog is null)
            {
                if (rbase.TryGetOrdinaryIndexIntent(storeID, entry.Path, out OrdinaryIndexIntentEntry existingIntent))
                {
                    if (existingIntent.Kind != OrdinaryIndexIntentKind.Text)
                        throw new InvalidOperationException($"Index '{entry.Path}' does not have a durable text-index contract.");
                    intent = existingIntent.Clone();
                }
                else
                {
                    TextIndexProfile textProfile = TextIndexProfile.Exact;
                    if ((persisted.StringKeys & StringKeys.Folded) != 0)
                        textProfile |= TextIndexProfile.Folded;
                    if ((persisted.StringKeys & StringKeys.Normalized) != 0)
                        textProfile |= TextIndexProfile.Normalized;
                    IndexDirection direction = persisted.Directions == LibraDexProjectionDirectionSet.ForwardAndReversed
                        ? IndexDirection.Bidirectional
                        : persisted.SortOrder == LibraDexIndexSortOrder.Descending
                            ? IndexDirection.Descending
                            : IndexDirection.Ascending;
                    intent = new OrdinaryIndexIntentEntry
                    {
                        StoreID = storeID,
                        Kind = OrdinaryIndexIntentKind.Text,
                        LogicalName = entry.Path,
                        PhysicalName = entry.Path,
                        Unique = entry.Unique,
                        TextProfile = textProfile,
                        Direction = direction,
                        TextSortProfiles = (TextSortProfile[])physicalProfiles.Clone()
                    };
                }

                if (!intent.TextSortProfiles.Contains(requested))
                    intent.TextSortProfiles = [.. intent.TextSortProfiles, requested];
                intent.Ready = false;
                intent.Validate();
                rbase.UpsertOrdinaryIndexIntent(intent);
            }

            if (!physicalExists)
            {
                using LibraDexStringScalar8Index expanded = indexes.Define(entry.Path).StringKeys().AddSortKeyProfile(
                    new LibraDexStringSortKeyProfile(requested.CultureName, requested.CompareOptions));
            }

            LibraDexStringScalar8Index reopened = indexes.Define(entry.Path).StringKeys().Open();
            lock (libraDexIndexLock)
            {
                LibraDexSimpleIndex[] current = libraDexSimpleIndexes;
                if ((uint)entryOffset >= (uint)current.Length || !string.Equals(current[entryOffset].Path, entry.Path, StringComparison.Ordinal))
                {
                    reopened.Dispose();
                    throw new InvalidOperationException($"Text index '{entry.Path}' changed while its subindex was being added.");
                }

                LibraDexSimpleIndex[] next = (LibraDexSimpleIndex[])current.Clone();
                next[entryOffset] = current[entryOffset].WithIndex(reopened);
                libraDexSimpleIndexes = next;
            }

            CompleteOrdinaryIndexRegistration(entry.OwnerCatalog, intent);
            return !physicalExists;
        }

        /// <summary>
        /// Tests whether a reflected scalar type has a built-in LibraDex physical shape in the ordinary-index bridge.<br/>
        /// Custom registered binary codecs are evaluated separately by the caller.<br/>
        /// </summary>
        /// <param name="keyType">The normalized non-nullable property type.<br/></param>
        /// <returns><c>true</c> when the scalar registration switch can create the physical index.<br/></returns>
        private static bool IsSupportedLibraDexScalarKeyType(Type keyType)
            => keyType == typeof(int) ||
                keyType == typeof(long) ||
                keyType == typeof(uint) ||
                keyType == typeof(ulong) ||
                keyType == typeof(short) ||
                keyType == typeof(ushort) ||
                keyType == typeof(byte) ||
                keyType == typeof(sbyte) ||
                keyType == typeof(char) ||
                keyType == typeof(Int128) ||
                keyType == typeof(UInt128) ||
                keyType == typeof(float) ||
                keyType == typeof(double) ||
                keyType == typeof(decimal) ||
                keyType == typeof(bool) ||
                keyType == typeof(Guid) ||
                keyType == typeof(string) ||
                keyType == typeof(DateTime) ||
                keyType == typeof(DateTimeOffset) ||
                keyType == typeof(DateOnly) ||
                keyType == typeof(TimeOnly) ||
                keyType == typeof(TimeSpan);

        private void PopulateLibraDexSimpleIndex(LibraDexSimpleIndex entry, bool physicalIndexIsNew)
        {
            if (physicalIndexIsNew &&
                IndexBackfill.Mode == IndexBackfillMode.Optimized &&
                TryPopulateLibraDexSimpleIndexOptimized(entry))
            {
                return;
            }

            var prepareTimer = Stopwatch.StartNew();
            var prepared = PrepareLibraDexSimpleIndexPopulation(entry);
            prepareTimer.Stop();
            var insertTimer = Stopwatch.StartNew();
            ApplyLibraDexSimpleIndexPopulation(entry, prepared);
            insertTimer.Stop();
            LastIndexBackfillDiagnostics = new IndexBackfillDiagnostics(
                IndexBackfill.Mode,
                IndexBackfillMode.Legacy,
                prepared.Count,
                prepared.Count,
                prepareTimer.Elapsed,
                TimeSpan.Zero,
                insertTimer.Elapsed,
                TimeSpan.Zero,
                prepared.Count,
                prepared.Count == 0 ? 0 : 1);
        }

        /// <summary>
        /// Reads and validates every key for one ordinary index from authoritative Fractal/Inheto records without mutating the physical index.<br/>
        /// Preparing the complete population first ensures key-read, codec, binary-bound, and uniqueness failures preserve the previously usable tuple set.<br/>
        /// </summary>
        /// <param name="entry">The immutable runtime index descriptor whose key contract is being rebuilt.<br/></param>
        /// <returns>A complete caller-owned tuple population ready for physical insertion.<br/></returns>
        private List<(ulong RecordID, object? Key)> PrepareLibraDexSimpleIndexPopulation(LibraDexSimpleIndex entry)
        {
            var prepared = new List<(ulong RecordID, object? Key)>();
            HashSet<object?>? uniqueKeys = entry.Unique
                ? new HashSet<object?>(LibraDexRuntimeKeyComparer.Instance)
                : null;
            foreach (var recordID in IterateRecordIDs())
            {
                InhetoBinary binary;
                try
                {
                    binary = GetInhetoBinary(recordID, nameof(PopulateLibraDexSimpleIndex));
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Index '{entry.Path}' could not read authoritative record '{recordID}' during population.",
                        entry.Index.Name,
                        entry.Path,
                        recordID,
                        exception);
                }

                if (!TryReadLibraDexKey(binary, entry, out var key, strict: true))
                    continue;

                ValidateLibraDexBinaryKeyLength(entry, key, recordID);
                if (uniqueKeys is not null &&
                    key is not null &&
                    !uniqueKeys.Add(key))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.UniqueConstraint,
                        $"Unique index '{entry.Path}' found the same non-null key on more than one existing record.",
                        entry.Index.Name,
                        entry.Path,
                        recordID);
                }

                if (entry.Unique && key is not null)
                    ValidateLibraDexUniqueKey(entry.Index, entry.Path, key, recordID);

                prepared.Add((recordID, key));
            }

            return prepared;
        }

        /// <summary>
        /// Inserts one completely validated ordinary-index population into its current LibraDex physical index.<br/>
        /// Callers control whether the destination is newly created, already populated, or deliberately cleared for repair.<br/>
        /// </summary>
        /// <param name="entry">The immutable runtime index descriptor receiving tuples.<br/></param>
        /// <param name="prepared">The authoritative tuple population produced before physical mutation begins.<br/></param>
        private void ApplyLibraDexSimpleIndexPopulation(
            LibraDexSimpleIndex entry,
            List<(ulong RecordID, object? Key)> prepared)
        {
            for (var i = 0; i < prepared.Count; i++)
            {
                var (recordID, key) = prepared[i];
                var result = entry.Index.Insert(key, recordID);
                EnsureLibraDexInsertAccepted(entry, key, recordID, result);
            }
        }

        /// <summary>
        /// Replaces one ordinary simple-index population through LibraDex's native stable-root generation path when the physical shape supports it.<br/>
        /// The fallback retains the existing clear-and-reinsert behavior for unsupported shapes and null-key populations.<br/>
        /// </summary>
        /// <param name="entry">Runtime simple-index descriptor receiving the authoritative population.<br/></param>
        /// <param name="prepared">Completely validated authoritative tuples.<br/></param>
        private void RebuildPreparedLibraDexSimpleIndex(
            LibraDexSimpleIndex entry,
            List<(ulong RecordID, object? Key)> prepared)
        {
            if (prepared.All(static tuple => tuple.Key is not null) &&
                entry.Index is ILibraDexNativeSortedBuild native &&
                native.TryReplaceFromUnorderedObjects(
                    prepared.Select(static tuple => new LibraDexObjectTuple(tuple.Key!, tuple.RecordID)),
                    CancellationToken.None,
                    out long replacedCount))
            {
                if (replacedCount != prepared.Count)
                    throw new InvalidDataException($"Index '{entry.Path}' replaced {replacedCount:N0} tuples instead of the authoritative {prepared.Count:N0} tuples.");
                return;
            }

            RebuildPreparedLibraDexIndex(
                entry.Index,
                entry.OwnerCatalog,
                () => ApplyLibraDexSimpleIndexPopulation(entry, prepared),
                entry.Index.Name,
                entry.Path);
        }

        private void PopulateLibraDexRegexProjectionIndex(LibraDexRegexProjectionIndex entry)
        {
            var prepared = PrepareLibraDexRegexProjectionIndexPopulation(entry);
            ApplyLibraDexRegexProjectionIndexPopulation(entry, prepared);
        }

        /// <summary>
        /// Reads and validates every projected key for one regex index from authoritative Fractal/Inheto records without mutating the physical index.<br/>
        /// The complete preparation step preserves the prior tuple set when payload, projection, or uniqueness validation fails.<br/>
        /// </summary>
        /// <param name="entry">The immutable maintained-projection descriptor whose tuple set is being rebuilt.<br/></param>
        /// <returns>A complete caller-owned projected tuple population ready for physical insertion.<br/></returns>
        private List<(ulong RecordID, string Key)> PrepareLibraDexRegexProjectionIndexPopulation(
            LibraDexRegexProjectionIndex entry)
        {
            var prepared = new List<(ulong RecordID, string Key)>();
            HashSet<string>? uniqueKeys = entry.Unique
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;
            foreach (var recordID in IterateRecordIDs())
            {
                InhetoBinary binary;
                try
                {
                    binary = GetInhetoBinary(recordID, nameof(PopulateLibraDexRegexProjectionIndex));
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Regex index '{entry.LogicalPrefix}' could not read authoritative record '{recordID}' during population.",
                        entry.LogicalPrefix,
                        entry.SourcePath,
                        recordID,
                        exception);
                }

                if (!TryReadLibraDexRegexProjectionKey(binary, entry, out var key, strict: true))
                    continue;

                if (uniqueKeys is not null && !uniqueKeys.Add(key))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.UniqueConstraint,
                        $"Unique regex index '{entry.LogicalPrefix}' produced the same key for more than one existing record.",
                        entry.LogicalPrefix,
                        entry.SourcePath,
                        recordID);
                }

                if (entry.Unique)
                    ValidateLibraDexUniqueKey(entry.Index, entry.SourcePath, key, recordID);

                prepared.Add((recordID, key));
            }

            return prepared;
        }

        /// <summary>
        /// Inserts one completely validated regex-projection population into its current LibraDex physical index.<br/>
        /// Callers control whether the destination is newly created, already populated, or deliberately cleared for repair.<br/>
        /// </summary>
        /// <param name="entry">The immutable maintained-projection descriptor receiving tuples.<br/></param>
        /// <param name="prepared">The authoritative projected tuple population produced before physical mutation begins.<br/></param>
        private void ApplyLibraDexRegexProjectionIndexPopulation(
            LibraDexRegexProjectionIndex entry,
            List<(ulong RecordID, string Key)> prepared)
        {
            for (var i = 0; i < prepared.Count; i++)
            {
                var (recordID, key) = prepared[i];
                var result = entry.Index.Insert(key, recordID);
                EnsureLibraDexRegexInsertAccepted(entry, key, recordID, result);
            }
        }

        /// <summary>
        /// Rebuilds one registered ordinary index solely from authoritative Fractal/Inheto records while preserving the reserved State index.<br/>
        /// All source keys are prepared and validated before the target is cleared; if physical insertion then fails, the target is cleared again so callers observe a deterministic repair-required state rather than a partial tuple set.<br/>
        /// Partial indexes are intentionally excluded because their durable condition and readiness lifecycle owns a separate stale-USN rebuild contract.<br/>
        /// </summary>
        /// <param name="index">The Abraxas index handle selecting one simple, binary, custom-codec, text, or regex-projection index.<br/></param>
        /// <returns><c>true</c> when a matching ordinary index was rebuilt; otherwise <c>false</c>.<br/></returns>
        private bool TryRebuildLibraDexIndex(RecordIndex index)
        {
            using var indexMutation = EnterPartialIndexMutationScope();

            var compositeIndexes = libraDexCompositeIndexes;
            for (var i = 0; i < compositeIndexes.Length; i++)
            {
                var entry = compositeIndexes[i];
                if (!string.Equals(entry.LogicalName, index.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.PhysicalName, index.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                var prepared = PrepareLibraDexCompositeIndexPopulation(entry);
                RebuildPreparedLibraDexIndex(
                    entry.Index,
                    entry.OwnerCatalog,
                    () => ApplyLibraDexCompositeIndexPopulation(entry, prepared),
                    entry.PhysicalName,
                    entry.LogicalName);
                return true;
            }

            var regexIndexes = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                if (!string.Equals(entry.LogicalPrefix, index.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var prepared = PrepareLibraDexRegexProjectionIndexPopulation(entry);
                RebuildPreparedLibraDexIndex(
                    entry.Index,
                    entry.OwnerCatalog,
                    () => ApplyLibraDexRegexProjectionIndexPopulation(entry, prepared),
                    entry.LogicalPrefix,
                    entry.SourcePath);
                return true;
            }

            var simpleIndexes = libraDexSimpleIndexes;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                if (!string.Equals(entry.Path, index.KeyPath, StringComparison.Ordinal) &&
                    !string.Equals(entry.Path, index.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var prepared = PrepareLibraDexSimpleIndexPopulation(entry);
                RebuildPreparedLibraDexSimpleIndex(entry, prepared);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears and replaces one ordinary LibraDex physical tuple set after its authoritative population has been fully prepared.<br/>
        /// The reserved State index is unreachable through this helper because callers pass only descriptors from the ordinary simple and regex registries.<br/>
        /// </summary>
        /// <param name="index">The ordinary physical index being replaced.<br/></param>
        /// <param name="ownerCatalog">The volatile owner catalog, or <c>null</c> for the bound persistent catalog.<br/></param>
        /// <param name="applyPreparedPopulation">Applies the already validated authoritative tuple population.<br/></param>
        /// <param name="indexName">Stable Abraxas or physical index name used by the exception contract.<br/></param>
        /// <param name="keyPath">Developer-facing source property path used by the exception contract.<br/></param>
        private void RebuildPreparedLibraDexIndex(
            IIndex index,
            Catalog? ownerCatalog,
            Action applyPreparedPopulation,
            string indexName,
            string keyPath)
        {
            if (!TryClearLibraDexIndex(index, ownerCatalog))
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{indexName}' could not clear its existing tuple set before rebuild.",
                    indexName,
                    keyPath);
            }

            try
            {
                applyPreparedPopulation();
            }
            catch (Exception exception)
            {
                _ = TryClearLibraDexIndex(index, ownerCatalog);
                if (exception is IndexContractException)
                    throw;

                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{indexName}' could not apply its authoritative rebuilt tuple set.",
                    indexName,
                    keyPath,
                    innerException: exception);
            }
        }

        /// <summary>
        /// </summary>
        /// <returns>Abraxas index metadata for each registered persistent LibraDex index.<br/></returns>
        private IEnumerable<IndexInfo> GetPersistentLibraDexIndexInfos()
        {
            using var lifecycle = EnterStoreOperationScope();
            var compositeIndexes = libraDexCompositeIndexes;
            for (var i = 0; i < compositeIndexes.Length; i++)
            {
                var entry = compositeIndexes[i];
                if (entry.OwnerCatalog is not null)
                    continue;
                var info = new IndexInfo(
                    entry.LogicalName,
                    entry.Unique,
                    "Persistent composite index with " + entry.Specs.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ordered selector parts. Physical identity: " + entry.PhysicalName + ".")
                {
                    Backend = new IndexBackendInfo(IndexBackendStatus.OrderedReady, IndexBackendCapabilities.Equality | IndexBackendCapabilities.Range | IndexBackendCapabilities.OrderedRead | (entry.Unique ? IndexBackendCapabilities.Unique : IndexBackendCapabilities.None), entry.LogicalName, typeof(LibraDexCompositeKey))
                };
                yield return info;
            }

            var simpleIndexes = libraDexSimpleIndexes;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                if (entry.OwnerCatalog is not null)
                    continue;

                var logicalPrefix = "lidx_" + name + "_" + SanitizeTokenForIndex(entry.Path, 40) + "_libradex";
                var info = new IndexInfo(
                    logicalPrefix,
                    entry.Unique,
                    "Persistent ordered index for " + entry.Path + ".")
                {
                    Backend = new IndexBackendInfo(IndexBackendStatus.OrderedReady, entry.Capabilities, entry.Path, entry.KeyType)
                };

                yield return info;
            }

            var regexIndexes = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                if (entry.OwnerCatalog is not null)
                    continue;

                var info = new IndexInfo(
                    entry.LogicalPrefix,
                    entry.Unique,
                    "Persistent regular-expression projection index for " + entry.SourcePath + ". Abraxas maintains the projected key.")
                {
                    Backend = new IndexBackendInfo(IndexBackendStatus.OrderedReady, entry.Capabilities, entry.SourcePath, typeof(string))
                };

                yield return info;
            }

            var partialIndexes = libraDexPartialIndexes;
            for (var i = 0; i < partialIndexes.Length; i++)
            {
                var entry = partialIndexes[i];
                if (entry.OwnerCatalog is not null)
                    continue;
                var usable = IsPartialIndexUsable(entry);
                var info = new IndexInfo(
                    entry.LogicalName,
                    entry.Unique,
                    "Persistent partial LibraDex index for " + entry.KeyPath +
                    ". Membership: " + entry.Descriptor.ToDisplayString() +
                    ". Physical identity: " + entry.PhysicalName + ".")
                {
                    Backend = usable
                        ? new IndexBackendInfo(IndexBackendStatus.OrderedReady, entry.Capabilities, entry.KeyPath, entry.KeyType)
                        : new IndexBackendInfo(IndexBackendStatus.PrimaryOnly, IndexBackendCapabilities.None, entry.KeyPath, entry.KeyType)
                };

                yield return info;
            }
        }

        private IEnumerable<IndexInfo> GetVolatileLibraDexIndexInfos()
        {
            var simpleIndexes = libraDexSimpleIndexes;
            var regexIndexes = libraDexRegexProjectionIndexes;
            var compositeIndexes = libraDexCompositeIndexes;
            var partialIndexes = libraDexPartialIndexes;
            if (simpleIndexes.Length == 0 && regexIndexes.Length == 0 && compositeIndexes.Length == 0 && partialIndexes.Length == 0)
                yield break;

            List<IndexInfo>? infos = null;
            if (simpleIndexes.Length != 0)
            {
                for (var i = 0; i < simpleIndexes.Length; i++)
                {
                    var entry = simpleIndexes[i];
                    if (entry.OwnerCatalog is null)
                        continue;

                    var logicalPrefix = "memidx_" + name + "_" + SanitizeTokenForIndex(entry.Path, 40);
                    var infoText = entry.AutoCreated
                        ? "Auto-created volatile ordered index for " + entry.Path + ". Contents are held in memory and may be evicted by the store auto-indexing policy."
                        : "Volatile ordered index for " + entry.Path + ". Contents are held in memory and are lost when the app or store instance closes.";
                    var info = new IndexInfo(logicalPrefix, entry.Unique, infoText)
                    {
                        Backend = new IndexBackendInfo(IndexBackendStatus.OrderedReady, entry.Capabilities, entry.Path, entry.KeyType)
                    };

                    infos ??= new List<IndexInfo>(2);
                    infos.Add(info);
                }
            }

            if (regexIndexes.Length != 0)
            {
                for (var i = 0; i < regexIndexes.Length; i++)
                {
                    var entry = regexIndexes[i];
                    if (entry.OwnerCatalog is null)
                        continue;

                    var infoText = "Volatile regex projection index for " + entry.SourcePath + ". Contents are held in memory and are lost when the app or store instance closes.";
                    var info = new IndexInfo(entry.LogicalPrefix, entry.Unique, infoText)
                    {
                        Backend = new IndexBackendInfo(IndexBackendStatus.OrderedReady, entry.Capabilities, entry.SourcePath, typeof(string))
                    };

                    infos ??= new List<IndexInfo>(2);
                    infos.Add(info);
                }
            }

            for (var i = 0; i < compositeIndexes.Length; i++)
            {
                var entry = compositeIndexes[i];
                if (entry.OwnerCatalog is null)
                    continue;
                infos ??= new List<IndexInfo>(2);
                infos.Add(new IndexInfo(entry.LogicalName, entry.Unique,
                    "Volatile composite index with " + entry.Specs.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ordered selector parts. Contents are lost when this store closes.")
                {
                    Backend = new IndexBackendInfo(IndexBackendStatus.OrderedReady,
                        IndexBackendCapabilities.Equality | IndexBackendCapabilities.Range | IndexBackendCapabilities.OrderedRead | (entry.Unique ? IndexBackendCapabilities.Unique : IndexBackendCapabilities.None),
                        entry.LogicalName, typeof(LibraDexCompositeKey))
                });
            }

            for (var i = 0; i < partialIndexes.Length; i++)
            {
                var entry = partialIndexes[i];
                if (entry.OwnerCatalog is null)
                    continue;
                var usable = IsPartialIndexUsable(entry);
                infos ??= new List<IndexInfo>(2);
                infos.Add(new IndexInfo(entry.LogicalName, entry.Unique,
                    "Volatile partial index for " + entry.KeyPath + ". Membership: " + entry.Descriptor.ToDisplayString() + ". Contents are lost when this store closes.")
                {
                    Backend = usable
                        ? new IndexBackendInfo(IndexBackendStatus.OrderedReady, entry.Capabilities, entry.KeyPath, entry.KeyType)
                        : new IndexBackendInfo(IndexBackendStatus.PrimaryOnly, IndexBackendCapabilities.None, entry.KeyPath, entry.KeyType)
                });
            }

            if (infos is null)
                yield break;

            for (var i = 0; i < infos.Count; i++)
                yield return infos[i];
        }

        /// <summary>
        /// Mirrors a persisted record into every registered simple LibraDex path index.<br/>
        /// Key extraction reads from the already materialized Inheto payload so the write path does not rehydrate the CLR object or compile selector delegates.<br/>
        /// </summary>
        /// <param name="recordID">The Abraxas record identifier used as the LibraDex identity.<br/></param>
        /// <param name="binary">The serialized record payload that contains indexed values.<br/></param>
        /// <param name="functionKeys">Pooled function projection keys evaluated once for this new record state.<br/></param>
        private void TryApplyLibraDexUpsert(ulong recordID, InhetoBinary binary, PreparedFunctionIndexKeys functionKeys = default)
        {
            var simpleIndexes = libraDexSimpleIndexes;
            var compositeIndexes = libraDexCompositeIndexes;
            var regexIndexes = libraDexRegexProjectionIndexes;
            var partialIndexes = libraDexPartialIndexes;
            if ((simpleIndexes.Length == 0 && compositeIndexes.Length == 0 && regexIndexes.Length == 0 && partialIndexes.Length == 0) || binary is null)
                return;

            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                try
                {
                    bool hasPreparedKey = functionKeys.TryGet(simpleIndexes,i,out bool preparedHasValue,out object? preparedValue);
                    if (!hasPreparedKey && TryInsertBorrowedLibraDexString(binary, entry, recordID, out LibraDexGenericInsertResult stringResult))
                    {
                        EnsureBorrowedLibraDexStringInsertAccepted(binary, entry, recordID, stringResult);
                        continue;
                    }

                    if (!hasPreparedKey && TryInsertTypedLibraDexScalar(binary, entry, recordID, out LibraDexGenericInsertResult typedResult))
                    {
                        if (!typedResult.Inserted)
                        {
                            if (!TryReadLibraDexKey(binary, entry, out object typedKey, strict: true))
                                throw new InvalidDataException($"Index '{entry.Path}' could not reread a rejected typed scalar key.");
                            EnsureLibraDexInsertAccepted(entry, typedKey, recordID, typedResult);
                        }
                        continue;
                    }

                    if (entry.CustomPayloadReader is not null)
                    {
                        if (!TryReadBorrowedLibraDexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                            continue;

                        LibraDexGenericInsertResult borrowedResult = InsertBorrowedLibraDexKey(entry, payload, recordID);
                        EnsureBorrowedLibraDexInsertAccepted(entry, payload, recordID, borrowedResult);
                        continue;
                    }

                    if (!TryReadLibraDexKey(
                        binary,
                        entry,
                        out var key,
                        strict: true,
                        hasPreparedFunctionKey: hasPreparedKey,
                        preparedHasValue: preparedHasValue,
                        preparedValue: preparedValue))
                        continue;

                    var result = entry.Index.Insert(key, recordID);
                    EnsureLibraDexInsertAccepted(entry, key, recordID, result);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"LibraDex upsert failed for simple index '{entry.Index.Name}', path '{entry.Path}', key type '{entry.KeyType.FullName}', and record '{recordID}'.",
                        exception);
                }
            }

            UpdateVirtualPropertyRuntimeValues(recordID, binary);

            if (compositeIndexes.Length != 0)
                TryApplyLibraDexCompositeUpsert(recordID, binary);

            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                if (!TryReadLibraDexRegexProjectionKey(binary, entry, out var key, strict: true))
                    continue;

                var result = entry.Index.Insert(key, recordID);
                EnsureLibraDexRegexInsertAccepted(entry, key, recordID, result);
            }

            if (partialIndexes.Length != 0)
                TryApplyLibraDexPartialUpsert(recordID, binary);
        }

        /// <summary>
        /// Applies an explicitly supplied Abraxas State value to its authoritative internal persistent ordered index.<br/>
        /// A missing <paramref name="state"/> preserves the prior state, matching the existing public overwrite semantics.<br/>
        /// </summary>
        /// <param name="recordID">The Abraxas record identifier used as the LibraDex identity.<br/></param>
        /// <param name="state">The developer-supplied state value, or <c>null</c> when the write should leave state unchanged.<br/></param>
        private void TryApplyLibraDexStateUpsert(ulong recordID, long? state)
        {
            if (!state.HasValue)
                return;

            var index = RequireLibraDexStateIndex(nameof(TryApplyLibraDexStateUpsert));

            lock (libraDexIndexLock)
            {
                if (index.TryGetSingleKey(recordID, out long current))
                {
                    if (current != state.Value)
                        index.Rekey(recordID, current, state.Value);
                    return;
                }

                LibraDexGenericInsertResult inserted = index.Insert(state.Value, recordID);
                if (!inserted.Inserted && !index.TryGetSingleKey(recordID, out _))
                    throw new InvalidOperationException($"Could not assign State '{state.Value}' to record '{recordID}'.");
            }
        }

        /// <summary>
        /// Removes one record's prior tuples using binary bytes captured before authoritative Fractal mutation.<br/>
        /// This overload lets overwrite and delete retain the old tuples until Fractal succeeds without attempting to reread bytes that no longer represent the prior record.<br/>
        /// </summary>
        /// <param name="recordID">Abraxas record identity used by every maintained tuple.<br/></param>
        /// <param name="binary">Prior authoritative Inheto payload captured before Fractal mutation.<br/></param>
        private void TryApplyLibraDexDelete(ulong recordID, InhetoBinary binary)
        {
            var simpleIndexes = libraDexSimpleIndexes;
            var compositeIndexes = libraDexCompositeIndexes;
            var regexIndexes = libraDexRegexProjectionIndexes;
            var partialIndexes = libraDexPartialIndexes;
            T? runtimeRecord = default;
            bool hasRuntimeRecord = false;
            for (int i = 0; i < simpleIndexes.Length; i++)
            {
                if (simpleIndexes[i].FunctionRuntimeReader is null)
                    continue;
                runtimeRecord = binary.ToObject<T>(NativeDeserializationOptions);
                hasRuntimeRecord = runtimeRecord is not null;
                break;
            }
            if (partialIndexes.Length != 0)
                TryApplyLibraDexPartialDelete(recordID, binary);

            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                bool isFunctionIndex = entry.IsFunctionIndex;
                if (entry.IsVirtualProperty &&
                    TryGetAuthoritativeVirtualPropertyMutationValue(
                        entry.Index,
                        recordID,
                        out bool hasAuthoritativeValue,
                        out object? authoritativeValue))
                {
                    if (hasAuthoritativeValue)
                        entry.Index.Delete(authoritativeValue!, recordID);
                    continue;
                }
                if (!isFunctionIndex && TryDeleteBorrowedLibraDexString(binary, entry, recordID))
                    continue;

                if (!isFunctionIndex && TryDeleteTypedLibraDexScalar(binary, entry, recordID))
                    continue;

                if (entry.CustomPayloadReader is not null)
                {
                    if (TryReadBorrowedLibraDexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                        _ = DeleteBorrowedLibraDexKey(entry, payload, recordID);
                    continue;
                }

                if (!TryReadLibraDexKey(binary, entry, out var key, runtimeRecord: runtimeRecord, hasRuntimeRecord: hasRuntimeRecord))
                    continue;

                entry.Index.Delete(key, recordID);
            }

            RemoveVirtualPropertyRuntimeValues(recordID);

            if (compositeIndexes.Length != 0)
                TryApplyLibraDexCompositeDelete(recordID, binary);

            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                if (!TryReadLibraDexRegexProjectionKey(binary, entry, out var key))
                    continue;

                entry.Index.Delete(key, recordID);
            }
        }

        /// <summary>
        /// Removes a record's tuple from the internal persistent ordered State index.<br/>
        /// State is external metadata rather than object payload, so deletion resolves the one maintained identity-to-key association without reading the serialized object.<br/>
        /// </summary>
        /// <param name="recordID">The Abraxas record identifier used as the LibraDex identity.<br/></param>
        private void TryApplyLibraDexStateDelete(ulong recordID)
        {
            var index = libraDexStateIndex;
            if (index is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return;

            lock (libraDexIndexLock)
            {
                if (index.TryGetSingleKey(recordID, out long state))
                    index.Delete(state, recordID);
            }
        }

        /// <summary>
        /// Removes every current store record from registered LibraDex path indexes before a bulk storage delete runs.<br/>
        /// The method snapshots ordered-backend descriptors once and returns the same record-id snapshot so callers that still need FractalKVS deletion IDs do not enumerate twice.<br/>
        /// </summary>
        /// <param name="recordIDs">Record identifiers observed before storage deletion; <c>null</c> when no ordered backend was registered.<br/></param>
        /// <returns><c>true</c> when ordered-backend delete work was attempted; otherwise <c>false</c>.<br/></returns>
        private bool TryApplyLibraDexDeleteAll(out ulong[]? recordIDs)
        {
            recordIDs = null;
            var simpleIndexes = libraDexSimpleIndexes;
            var compositeIndexes = libraDexCompositeIndexes;
            var regexIndexes = libraDexRegexProjectionIndexes;
            var partialIndexes = libraDexPartialIndexes;
            var stateIndex = libraDexStateIndex;
            if (simpleIndexes.Length == 0 && compositeIndexes.Length == 0 && regexIndexes.Length == 0 && partialIndexes.Length == 0 && stateIndex is null)
                return false;

            var ids = rbase.data.IterateRecordIDsByGroupKey(storeID).ToArray();
            var clearedAll = true;
            for (var i = 0; i < simpleIndexes.Length; i++)
                clearedAll &= TryClearLibraDexIndex(simpleIndexes[i].Index, simpleIndexes[i].OwnerCatalog);

            for (var i = 0; i < compositeIndexes.Length; i++)
                clearedAll &= TryClearLibraDexIndex(compositeIndexes[i].Index, compositeIndexes[i].OwnerCatalog);

            for (var i = 0; i < regexIndexes.Length; i++)
                clearedAll &= TryClearLibraDexIndex(regexIndexes[i].Index, regexIndexes[i].OwnerCatalog);

            for (var i = 0; i < partialIndexes.Length; i++)
            {
                if (partialIndexes[i].IsReady)
                    clearedAll &= TryClearLibraDexIndex(partialIndexes[i].Index, partialIndexes[i].OwnerCatalog);
            }

            if (stateIndex is not null)
                clearedAll &= TryClearLibraDexIndex(stateIndex, ownerCatalog: null);

            if (clearedAll)
            {
                recordIDs = ids;
                return true;
            }

            for (var idIndex = 0; idIndex < ids.Length; idIndex++)
            {
                var recordID = ids[idIndex];
                InhetoBinary binary;
                try
                {
                    binary = GetInhetoBinary(recordID, nameof(TryApplyLibraDexDeleteAll));
                }
                catch
                {
                    continue;
                }

                for (var i = 0; i < simpleIndexes.Length; i++)
                {
                    var entry = simpleIndexes[i];
                    if (TryDeleteBorrowedLibraDexString(binary, entry, recordID))
                        continue;

                    if (TryDeleteTypedLibraDexScalar(binary, entry, recordID))
                        continue;

                    if (entry.CustomPayloadReader is not null)
                    {
                        if (TryReadBorrowedLibraDexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                            _ = DeleteBorrowedLibraDexKey(entry, payload, recordID);
                        continue;
                    }

                    if (!TryReadLibraDexKey(binary, entry, out var key))
                        continue;

                    entry.Index.Delete(key, recordID);
                }

                for (var i = 0; i < regexIndexes.Length; i++)
                {
                    var entry = regexIndexes[i];
                    if (!TryReadLibraDexRegexProjectionKey(binary, entry, out var key))
                        continue;

                    entry.Index.Delete(key, recordID);
                }
            }

            if (stateIndex is not null)
            {
                for (var idIndex = 0; idIndex < ids.Length; idIndex++)
                    TryApplyLibraDexStateDelete(ids[idIndex]);
            }

            recordIDs = ids;
            return true;
        }

        /// <summary>
        /// Creates or opens the internal persistent ordered State index whenever the store binds its persistent LibraDex index set.<br/>
        /// State is developer-owned index metadata, and the single-key-per-identity contract guarantees at most one State value per record identity.<br/>
        /// </summary>
        /// <param name="indexes">The persistent ordered index set bound to this store.<br/></param>
        private void EnsureLibraDexStateIndex(CatalogIdentityGroupIndexes indexes)
        {
            if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly || indexes is null)
                return;

            if (libraDexStateIndex is not null)
                return;

            lock (libraDexIndexLock)
            {
                libraDexStateIndex ??= OpenValidatedLibraDexStateIndex(indexes);
            }
        }

        /// <summary>
        /// Opens the reserved persistent State index only after validating its complete Abraxas-owned persisted contract, or creates that exact contract when the definition is absent.<br/>
        /// Validation occurs from disconnected catalog metadata before a typed physical handle is opened, preventing an incompatible reserved definition from being silently reinterpreted.<br/>
        /// </summary>
        /// <param name="indexes">The persistent LibraDex identity group owned by this store.<br/></param>
        /// <returns>The validated Int64-key/UInt64-identity State index with non-unique keys and one key per identity.<br/></returns>
        /// <exception cref="InvalidDataException">Thrown when an existing reserved definition lacks rich metadata or differs from the required key type, identity type, families, key contract, or identity multiplicity.<br/></exception>
        private static LibraDexIndex<long, ulong> OpenValidatedLibraDexStateIndex(
            CatalogIdentityGroupIndexes indexes)
        {
            var options = new IndexOptions
            {
                Keys = IndexKeys.NonUnique,
                IdentityKeyMultiplicity = IdentityKeyMultiplicity.SingleKeyPerIdentity
            };
            if (!indexes.TryGetInfo(LibraDexStateIndexName, out CatalogIndexInfo info))
            {
                return indexes.Define(LibraDexStateIndexName)
                    .Int64Keys<ulong>()
                    .Create(options: options);
            }

            if (!info.TryCreateShape(out LibraDexIndexShapeSpec? shape) ||
                shape.KeyType != typeof(long) ||
                shape.IdentityType != typeof(ulong) ||
                info.KeyFamily != CatalogIndexKeyFamily.Scalar ||
                info.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                info.KeyContract != IndexKeys.NonUnique ||
                info.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
            {
                throw new InvalidDataException(
                    $"Reserved LibraDex State index '{indexes.Name}/{LibraDexStateIndexName}' has an incompatible persisted contract. " +
                    $"Expected key={typeof(long).FullName}, identity={typeof(ulong).FullName}, key-family={CatalogIndexKeyFamily.Scalar}, " +
                    $"identity-family={CatalogIndexIdentityFamily.Scalar}, keys={IndexKeys.NonUnique}, " +
                    $"identity-multiplicity={IdentityKeyMultiplicity.SingleKeyPerIdentity}; " +
                    $"actual key={shape?.KeyType.FullName ?? info.KeyTypeName}, identity={shape?.IdentityType.FullName ?? info.IdentityTypeName}, " +
                    $"key-family={info.KeyFamily}, identity-family={info.IdentityFamily}, keys={info.KeyContract}, " +
                    $"identity-multiplicity={info.IdentityKeyMultiplicity}.");
            }

            LibraDexIndex<long, ulong> opened = indexes.Define(LibraDexStateIndexName)
                .Int64Keys<ulong>()
                .Open(options: options);
            if (opened.KeyType != typeof(long) ||
                opened.IdentityType != typeof(ulong) ||
                opened.KeyFamily != CatalogIndexKeyFamily.Scalar ||
                opened.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                opened.KeyContract != IndexKeys.NonUnique ||
                opened.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
            {
                throw new InvalidDataException(
                    $"Reserved LibraDex State index '{indexes.Name}/{LibraDexStateIndexName}' did not retain its validated runtime contract.");
            }

            return opened;
        }

        /// <summary>
        /// Deletes every tuple from one registered LibraDex index using LibraDex's own all-condition mutation path.<br/>
        /// This is preferred for store-wide deletes because it avoids reconstructing keys from every serialized payload and lets string/projection indexes clear their own physical rows.<br/>
        /// </summary>
        /// <param name="index">Runtime ordered index to clear.<br/></param>
        /// <param name="ownerCatalog">Memory catalog that owns the index, or <c>null</c> for the bound persistent group.<br/></param>
        /// <returns><c>true</c> when the index accepted the clear operation; otherwise <c>false</c> so callers can fall back to exact per-record tuple deletion.<br/></returns>
        private bool TryClearLibraDexIndex(IIndex index, Catalog? ownerCatalog)
        {
            if (index is LibraDexRoutedCompositeIndex composite)
            {
                _ = composite.DeleteAll();
                return true;
            }

            CatalogIdentityGroupIndexes? group = ownerCatalog is null
                ? libraDexIdentityIndexes
                : ownerCatalog.Indexes.IndexSet(indexGroupName);
            if (group is null)
                return false;

            try
            {
                _ = group.Define(index.Name).DeleteAll();
                return true;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Validates every ordinary and derived LibraDex key affected by one authoritative record write before Fractal payload mutation begins.<br/>
        /// The store-local mutation gate keeps unique-key preflight and the later tuple insert serialized; null and missing keys remain valid null-route values and do not participate in uniqueness rejection.<br/>
        /// </summary>
        /// <param name="recordID">The Abraxas record identity being inserted or overwritten.<br/></param>
        /// <param name="binary">The already serialized authoritative Inheto payload.<br/></param>
        /// <param name="functionKeys">Pooled function projection keys shared with the later insertion phase.<br/></param>
        private void ValidateLibraDexIndexUpsert(ulong recordID, InhetoBinary binary, PreparedFunctionIndexKeys functionKeys = default)
        {
            var simpleIndexes = libraDexSimpleIndexes;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                bool hasPreparedKey = functionKeys.TryGet(simpleIndexes,i,out bool preparedHasValue,out object? preparedValue);
                if (!hasPreparedKey && TryValidateBorrowedLibraDexString(binary, entry, recordID))
                    continue;

                if (entry.CustomPayloadReader is not null)
                {
                    if (!TryReadBorrowedLibraDexKey(binary, entry, out ReadOnlyMemory<byte> payload))
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.KeyRead,
                            $"Index '{entry.Path}' could not read its configured fixed-binary key path '{entry.Path}'.",
                            entry.Index.Name,
                            entry.Path,
                            recordID);
                    }

                    if (entry.Unique)
                        ValidateBorrowedLibraDexUniqueKey(entry, payload, recordID);
                    continue;
                }

                if (!hasPreparedKey && TryValidateTypedLibraDexScalar(binary, entry, recordID))
                    continue;

                if (!TryReadLibraDexKey(
                    binary,
                    entry,
                    out var key,
                    strict: true,
                    hasPreparedFunctionKey: hasPreparedKey,
                    preparedHasValue: preparedHasValue,
                    preparedValue: preparedValue))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Index '{entry.Path}' could not read its configured key path '{entry.Path}'.",
                        entry.Index.Name,
                        entry.Path,
                        recordID);
                }

                ValidateLibraDexBinaryKeyLength(entry, key, recordID);
                if (entry.Unique && key is not null)
                    ValidateLibraDexUniqueKey(entry.Index, entry.Path, key, recordID);
            }

            var regexIndexes = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                if (!TryReadLibraDexRegexProjectionKey(binary, entry, out var key, strict: true) ||
                    !entry.Unique)
                {
                    continue;
                }

                ValidateLibraDexUniqueKey(entry.Index, entry.SourcePath, key, recordID);
            }
        }

        /// <summary>
        /// Enforces the durable developer-facing payload cap for one bounded binary index before any primary or index mutation occurs.<br/>
        /// Null keys use the dedicated null route and therefore have no payload length to validate.<br/>
        /// </summary>
        /// <param name="entry">The registered simple-index descriptor carrying the optional binary cap.<br/></param>
        /// <param name="key">The extracted runtime key, or null for a null/missing route.<br/></param>
        /// <param name="recordID">The affected Abraxas record identity.<br/></param>
        private static void ValidateLibraDexBinaryKeyLength(LibraDexSimpleIndex entry, object? key, ulong recordID)
        {
            if (!entry.MaximumKeyBytes.HasValue || key is not byte[] bytes)
            {
                return;
            }

            if (entry.FixedBinaryKeyBytes.HasValue && bytes.Length != entry.FixedBinaryKeyBytes.Value)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Index '{entry.Path}' received a {bytes.Length}-byte key; its fixed key shape requires exactly {entry.FixedBinaryKeyBytes.Value} bytes.",
                    entry.Index.Name,
                    entry.Path,
                    recordID);
            }

            if (bytes.Length <= entry.MaximumKeyBytes.Value)
                return;

            throw new IndexContractException(
                IndexContractFailureKind.BinaryKeyTooLarge,
                $"Index '{entry.Path}' received a {bytes.Length}-byte key; its maximum payload length is {entry.MaximumKeyBytes.Value} bytes.",
                entry.Index.Name,
                entry.Path,
                recordID);
        }

        /// <summary>
        /// Rejects a non-null key when an exact lookup proves that another record already owns it in an Abraxas unique index.<br/>
        /// Runtime lookup returns canonical identity bytes; decoding uses LibraDex's public canonical UInt64 contract and never depends on an engine-specific exception or storage layout.<br/>
        /// </summary>
        /// <param name="index">The ordered index whose exact key route is inspected.<br/></param>
        /// <param name="keyPath">The Abraxas property path associated with the index.<br/></param>
        /// <param name="key">The non-null runtime key being validated.<br/></param>
        /// <param name="recordID">The record identity allowed to retain its own existing key during overwrite preflight.<br/></param>
        private static void ValidateLibraDexUniqueKey(IIndex index, string keyPath, object key, ulong recordID)
        {
            IReadOnlyList<LibraDexIndexEntry<byte[]?, byte[]>> entries;
            try
            {
                entries = index.Entries.GetByKey(key);
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{keyPath}' could not validate its unique-key route.",
                    index.Name,
                    keyPath,
                    recordID,
                    exception);
            }

            for (var i = 0; i < entries.Count; i++)
            {
                ulong existingRecordID;
                try
                {
                    existingRecordID = LibraDexCanonical.Numeric.Read<ulong>(entries[i].Identity);
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.BackendMutation,
                        $"Index '{keyPath}' returned an invalid record identity while validating uniqueness.",
                        index.Name,
                        keyPath,
                        recordID,
                        exception);
                }

                if (existingRecordID == recordID)
                    continue;

                throw new IndexContractException(
                    IndexContractFailureKind.UniqueConstraint,
                    $"Unique index '{keyPath}' already contains the supplied non-null key for another record.",
                    index.Name,
                    keyPath,
                    recordID);
            }
        }

        /// <summary>
        /// Verifies that one ordered-backend insert either created the tuple or proved that the exact tuple already exists.<br/>
        /// This converts a shape-specific false result into one stable Abraxas mutation failure without treating idempotent population as an error.<br/>
        /// </summary>
        /// <param name="entry">The simple index receiving the tuple.<br/></param>
        /// <param name="key">The runtime key supplied to the ordered backend.<br/></param>
        /// <param name="recordID">The Abraxas identity supplied as the tuple identity.<br/></param>
        /// <param name="result">The ordered backend's generic insert result.<br/></param>
        private static void EnsureLibraDexInsertAccepted(LibraDexSimpleIndex entry, object? key, ulong recordID, LibraDexGenericInsertResult result)
        {
            if (result.Inserted)
                return;

            try
            {
                if (LibraDexIndexContainsTuple(entry.Index, key, recordID))
                    return;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{entry.Path}' could not verify a rejected ordered-backend insertion.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }

            throw new IndexContractException(
                entry.Unique && key is not null
                    ? IndexContractFailureKind.UniqueConstraint
                    : IndexContractFailureKind.BackendMutation,
                entry.Unique && key is not null
                    ? $"Unique index '{entry.Path}' rejected a non-null key because another record already owns it."
                    : $"Index '{entry.Path}' rejected the validated record tuple.",
                entry.Index.Name,
                entry.Path,
                recordID);
        }

        /// <summary>
        /// Verifies that one regex-projection insert created the tuple or found the same exact tuple already present.<br/>
        /// Unique conflicts retain a distinct failure reason while all other false insert results become stable backend-mutation failures.<br/>
        /// </summary>
        /// <param name="entry">The registered regex projection receiving the tuple.<br/></param>
        /// <param name="key">The derived non-null text key.<br/></param>
        /// <param name="recordID">The Abraxas identity supplied as the tuple identity.<br/></param>
        /// <param name="result">The ordered backend's generic insert result.<br/></param>
        private static void EnsureLibraDexRegexInsertAccepted(LibraDexRegexProjectionIndex entry, string key, ulong recordID, LibraDexGenericInsertResult result)
        {
            if (result.Inserted)
                return;

            try
            {
                if (LibraDexIndexContainsTuple(entry.Index, key, recordID))
                    return;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Regex index '{entry.LogicalPrefix}' could not verify a rejected ordered-backend insertion.",
                    entry.LogicalPrefix,
                    entry.SourcePath,
                    recordID,
                    exception);
            }

            throw new IndexContractException(
                entry.Unique
                    ? IndexContractFailureKind.UniqueConstraint
                    : IndexContractFailureKind.BackendMutation,
                entry.Unique
                    ? $"Unique regex index '{entry.LogicalPrefix}' rejected a projected key because another record already owns it."
                    : $"Regex index '{entry.LogicalPrefix}' rejected the validated record tuple.",
                entry.LogicalPrefix,
                entry.SourcePath,
                recordID);
        }

        /// <summary>
        /// Determines whether an exact runtime key route already contains one Abraxas UInt64 identity.<br/>
        /// Null values use the index's family-specific null-state identity route instead of the binary-only null-key lookup surface.<br/>
        /// The helper is used only to distinguish idempotent population from a rejected backend mutation.<br/>
        /// </summary>
        /// <param name="index">The ordered index to inspect.<br/></param>
        /// <param name="key">The exact runtime key, including null-route values.<br/></param>
        /// <param name="recordID">The Abraxas identity expected on that route.<br/></param>
        /// <returns><c>true</c> when the exact tuple already exists; otherwise <c>false</c>.<br/></returns>
        private static bool LibraDexIndexContainsTuple(IIndex index, object? key, ulong recordID)
        {
            if (key is null)
                return index.Identities.Exists(recordID);

            var entries = index.Entries.GetByKey(key);
            for (var i = 0; i < entries.Count; i++)
            {
                if (LibraDexCanonical.Numeric.Read<ulong>(entries[i].Identity) == recordID)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a registered simple LibraDex index key from a serialized payload using the exact CLR key type selected at index setup.<br/>
        /// The switch is intentionally explicit so hot write/delete mirroring avoids reflection, compiled delegates, or generic method construction per record.<br/>
        /// </summary>
        /// <param name="binary">The serialized record payload that contains indexed values.<br/></param>
        /// <param name="entry">The registered LibraDex simple index descriptor.<br/></param>
        /// <param name="key">The boxed scalar key when this method returns <c>true</c>.<br/></param>
        /// <param name="strict">Whether key-reader failures must propagate instead of behaving as a missing key.<br/></param>
        /// <param name="runtimeRecord">Optional CLR record available without deserialization.<br/></param>
        /// <param name="hasRuntimeRecord">Whether <paramref name="runtimeRecord"/> may be used by an function projection reader.<br/></param>
        /// <param name="hasPreparedFunctionKey">Whether write-scoped projection preparation supplied this entry's result.<br/></param>
        /// <param name="preparedHasValue">Whether the prepared projection produced a non-null value.<br/></param>
        /// <param name="preparedValue">Prepared boxed projection value when present.<br/></param>
        /// <returns><c>true</c> when the key could be read for a supported scalar type; otherwise <c>false</c>.</returns>
        private bool TryReadLibraDexKey(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            out object key,
            bool strict = false,
            T? runtimeRecord = default,
            bool hasRuntimeRecord = false,
            bool hasPreparedFunctionKey = false,
            bool preparedHasValue = false,
            object? preparedValue = null)
        {
            key = default!;
            try
            {
                if (hasPreparedFunctionKey)
                {
                    if (!preparedHasValue)
                        return entry.IncludeNullValues;
                    if (!entry.IncludeEmptyStrings && preparedValue is string { Length: 0 })
                        return false;
                    key = preparedValue!;
                    return true;
                }

                if (hasRuntimeRecord && entry.FunctionRuntimeReader is not null)
                {
                    var calculated = entry.FunctionRuntimeReader(runtimeRecord!);
                    if (!calculated.HasValue)
                        return entry.IncludeNullValues;
                    if (!entry.IncludeEmptyStrings && calculated.Value is string { Length: 0 })
                        return false;
                    key = calculated.Value!;
                    return true;
                }

                if (entry.CalculatedReader is not null)
                {
                    var calculated = entry.CalculatedReader(binary);
                    if (!calculated.HasValue)
                        return entry.IncludeNullValues;
                    if (!entry.IncludeEmptyStrings && calculated.Value is string { Length: 0 })
                        return false;
                    key = calculated.Value!;
                    return true;
                }

                if (!binary.PropExists(entry.Path))
                    return true;

                var keyType = entry.KeyType;
                if (keyType == typeof(int))
                    key = binary.ReadInt32(entry.Path)!;
                else if (keyType == typeof(long))
                    key = binary.ReadInt64(entry.Path)!;
                else if (keyType == typeof(uint))
                    key = binary.ReadUInt32(entry.Path)!;
                else if (keyType == typeof(ulong))
                    key = binary.ReadUInt64(entry.Path)!;
                else if (keyType == typeof(short))
                    key = binary.ReadInt16(entry.Path)!;
                else if (keyType == typeof(ushort))
                    key = binary.ReadUInt16(entry.Path)!;
                else if (keyType == typeof(byte))
                    key = binary.ReadByte(entry.Path)!;
                else if (keyType == typeof(sbyte))
                    key = binary.ReadSByte(entry.Path)!;
                else if (keyType == typeof(char))
                    key = binary.ReadChar(entry.Path)!;
                else if (keyType == typeof(Int128))
                    key = binary.ReadInt128(entry.Path)!;
                else if (keyType == typeof(UInt128))
                    key = binary.ReadUInt128(entry.Path)!;
                else if (keyType == typeof(float))
                    key = binary.ReadSingle(entry.Path)!;
                else if (keyType == typeof(double))
                    key = binary.ReadDouble(entry.Path)!;
                else if (keyType == typeof(decimal))
                    key = binary.ReadDecimal(entry.Path)!;
                else if (keyType == typeof(bool))
                    key = binary.ReadBoolean(entry.Path)!;
                else if (keyType == typeof(Guid))
                    key = binary.ReadGuid(entry.Path)!;
                else if (keyType == typeof(string))
                    key = binary.ReadString(entry.Path)!;
                else if (keyType == typeof(DateTime))
                    key = binary.ReadDateTime(entry.Path)!;
                else if (keyType == typeof(DateTimeOffset))
                    key = binary.ReadDateTimeOffset(entry.Path)!;
                else if (keyType == typeof(DateOnly))
                    key = binary.ReadDateOnly(entry.Path)!;
                else if (keyType == typeof(TimeOnly))
                    key = binary.ReadTimeOnly(entry.Path)!;
                else if (keyType == typeof(TimeSpan))
                    key = binary.ReadTimeSpan(entry.Path)!;
                else if (keyType == typeof(byte[]))
                    key = binary.ReadByteArray(entry.Path)!;
                else if (entry.CustomKeyReader is not null)
                {
                    if (!entry.CustomKeyReader(binary, entry.Path, out key))
                        return true;
                }
                else
                {
                    if (strict)
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.KeyRead,
                            $"Index '{entry.Path}' has no registered reader for key type '{entry.KeyType.FullName}'.",
                            entry.Index.Name,
                            entry.Path);
                    }
                    return false;
                }

                return true;
            }
            catch (IndexContractException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (strict)
                {
                    throw new IndexContractException(
                        entry.CustomKeyReader is null
                            ? IndexContractFailureKind.KeyRead
                            : IndexContractFailureKind.Codec,
                        entry.CustomKeyReader is null
                            ? $"Index '{entry.Path}' could not read its configured key value."
                            : $"The registered binary codec for index '{entry.Path}' could not produce its key.",
                        entry.Index.Name,
                        entry.Path,
                        innerException: exception);
                }
                return false;
            }
        }

        /// <summary>
        /// Attempts to borrow one registered custom fixed-width key directly from an authoritative Inheto payload.<br/>
        /// The returned memory remains owned by <paramref name="binary"/> and is valid only for synchronous LibraDex operations completed before the payload is released.<br/>
        /// </summary>
        /// <param name="binary">The authoritative serialized record payload.<br/></param>
        /// <param name="entry">The registered custom fixed-width index descriptor.<br/></param>
        /// <param name="payload">Receives the borrowed fixed-width key memory when present.<br/></param>
        /// <returns><see langword="true"/> when a payload is present; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryReadBorrowedLibraDexKey(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            out ReadOnlyMemory<byte> payload)
        {
            CustomBinaryPayloadReader reader = entry.CustomPayloadReader
                ?? throw new InvalidOperationException($"Index '{entry.Path}' has no borrowed custom-binary reader.");
            return reader(binary, entry.Path, out payload);
        }

        /// <summary>
        /// Inserts one borrowed custom fixed-width key through LibraDex's synchronous binary-key contract.<br/>
        /// No standalone key array is created for the immediate scalar-identity path.<br/>
        /// </summary>
        private static LibraDexGenericInsertResult InsertBorrowedLibraDexKey(
            LibraDexSimpleIndex entry,
            ReadOnlyMemory<byte> payload,
            ulong recordID)
            => RequireFixedBinaryIndex(entry).Insert(payload.Span, recordID);

        /// <summary>
        /// Deletes one borrowed custom fixed-width key through LibraDex's synchronous binary-key contract.<br/>
        /// </summary>
        private static bool DeleteBorrowedLibraDexKey(
            LibraDexSimpleIndex entry,
            ReadOnlyMemory<byte> payload,
            ulong recordID)
            => RequireFixedBinaryIndex(entry).Delete(payload.Span, recordID);

        /// <summary>
        /// Rejects a borrowed custom fixed-width key when another record already owns its exact LibraDex route.<br/>
        /// </summary>
        private static void ValidateBorrowedLibraDexUniqueKey(
            LibraDexSimpleIndex entry,
            ReadOnlyMemory<byte> payload,
            ulong recordID)
        {
            try
            {
                if (!RequireFixedBinaryIndex(entry).ContainsOtherIdentity(payload.Span, recordID))
                    return;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{entry.Path}' could not validate its unique fixed-binary key route.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }

            throw new IndexContractException(
                IndexContractFailureKind.UniqueConstraint,
                $"Unique index '{entry.Path}' already contains the supplied non-null key for another record.",
                entry.Index.Name,
                entry.Path,
                recordID);
        }

        /// <summary>
        /// Verifies one borrowed fixed-width insertion without materializing the key when LibraDex reports an idempotent false result.<br/>
        /// </summary>
        private static void EnsureBorrowedLibraDexInsertAccepted(
            LibraDexSimpleIndex entry,
            ReadOnlyMemory<byte> payload,
            ulong recordID,
            LibraDexGenericInsertResult result)
        {
            if (result.Inserted)
                return;

            try
            {
                if (RequireFixedBinaryIndex(entry).ContainsTuple(payload.Span, recordID))
                    return;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{entry.Path}' could not verify a rejected fixed-binary insertion.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }

            throw new IndexContractException(
                entry.Unique
                    ? IndexContractFailureKind.UniqueConstraint
                    : IndexContractFailureKind.BackendMutation,
                entry.Unique
                    ? $"Unique index '{entry.Path}' rejected a non-null fixed-binary key because another record already owns it."
                    : $"Index '{entry.Path}' rejected the validated fixed-binary record tuple.",
                entry.Index.Name,
                entry.Path,
                recordID);
        }

        /// <summary>
        /// Requires the LibraDex fixed-width span capability promised by an Abraxas custom-binary index registration.<br/>
        /// </summary>
        private static IFixedBinaryKeyIndex<ulong> RequireFixedBinaryIndex(LibraDexSimpleIndex entry)
            => entry.Index as IFixedBinaryKeyIndex<ulong>
                ?? throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Index '{entry.Path}' does not expose LibraDex's fixed-binary mutation contract.",
                    entry.Index.Name,
                    entry.Path);

        /// <summary>
        /// Borrows one stored Inheto UTF-8 string payload and inserts it through LibraDex's logical string facade without materializing the exact CLR string.<br/>
        /// Missing and null properties use LibraDex's null-string route, while present empty strings remain distinct empty-route values.<br/>
        /// LibraDex alone derives folded, reversed, and culture sort-key companions when the logical index owns them.<br/>
        /// </summary>
        /// <param name="binary">The authoritative Inheto payload containing the indexed value.<br/></param>
        /// <param name="entry">The registered ordinary or partial index descriptor.<br/></param>
        /// <param name="recordID">The Abraxas record identity paired with the key.<br/></param>
        /// <param name="result">Receives the LibraDex insertion result when the descriptor is a string index.<br/></param>
        /// <returns><see langword="true"/> when the descriptor uses the string family; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryInsertBorrowedLibraDexString(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            ulong recordID,
            out LibraDexGenericInsertResult result)
        {
            if (entry.CalculatedReader is not null)
            {
                result = default;
                return false;
            }

            if (entry.KeyType != typeof(string))
            {
                result = default;
                return false;
            }

            try
            {
                LibraDexStringScalar8Index index = RequireLibraDexStringIndex(entry);
                result = binary.TryReadStringUtf8Payload(entry.Path, out ReadOnlyMemory<byte> utf8)
                    ? index.InsertUtf8(utf8.Span, recordID)
                    : index.Insert(null, recordID);
                return true;
            }
            catch (IndexContractException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.KeyRead,
                    $"Index '{entry.Path}' could not borrow or insert its stored UTF-8 string key.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }
        }

        /// <summary>
        /// Validates unique ownership for one direct-path borrowed Inheto UTF-8 string key without decoding it or materializing the key's identity list.<br/>
        /// Calculated string indexes return false so their projected keys are read and validated by the general calculated-key path.<br/>
        /// Missing and null strings intentionally do not participate in Abraxas uniqueness; present empty strings remain ordinary unique values.<br/>
        /// </summary>
        /// <param name="binary">The proposed authoritative Inheto payload.<br/></param>
        /// <param name="entry">The registered ordinary or partial string index descriptor.<br/></param>
        /// <param name="recordID">The identity permitted to retain its own existing key.<br/></param>
        /// <param name="partialIndexName">The developer-facing partial-index name when validating a partial index; otherwise <see langword="null"/> for the ordinary-index contract.<br/></param>
        /// <returns><see langword="true"/> when the descriptor uses the string family; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryValidateBorrowedLibraDexString(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            ulong recordID,
            string? partialIndexName = null)
        {
            if (entry.CalculatedReader is not null)
                return false;

            if (entry.KeyType != typeof(string))
                return false;

            try
            {
                if (!entry.Unique ||
                    !binary.TryReadStringUtf8Payload(entry.Path, out ReadOnlyMemory<byte> utf8))
                {
                    return true;
                }

                if (!RequireLibraDexStringIndex(entry).ContainsOtherIdentityUtf8(utf8.Span, recordID))
                    return true;
            }
            catch (IndexContractException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{entry.Path}' could not validate its borrowed UTF-8 unique-key route.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }

            throw new IndexContractException(
                IndexContractFailureKind.UniqueConstraint,
                partialIndexName is null
                    ? $"Unique index '{entry.Path}' already contains the supplied non-null string key for another record."
                    : $"Unique partial index '{partialIndexName}' already contains the supplied non-null key for another record.",
                partialIndexName ?? entry.Index.Name,
                entry.Path,
                recordID);
        }

        /// <summary>
        /// Deletes one stored Inheto UTF-8 string tuple through LibraDex's logical facade without materializing its exact CLR string.<br/>
        /// Missing and null properties use the null-string route; LibraDex removes every owned projection when a present string tuple is deleted.<br/>
        /// </summary>
        /// <param name="binary">The prior authoritative Inheto payload.<br/></param>
        /// <param name="entry">The registered ordinary or partial string index descriptor.<br/></param>
        /// <param name="recordID">The Abraxas record identity paired with the key.<br/></param>
        /// <returns><see langword="true"/> when the descriptor uses the string family; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryDeleteBorrowedLibraDexString(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            ulong recordID)
        {
            if (entry.CalculatedReader is not null)
                return false;

            if (entry.KeyType != typeof(string))
                return false;

            LibraDexStringScalar8Index index = RequireLibraDexStringIndex(entry);
            if (binary.TryReadStringUtf8Payload(entry.Path, out ReadOnlyMemory<byte> utf8))
                _ = index.DeleteUtf8(utf8.Span, recordID);
            else
                _ = index.Delete(null, recordID);
            return true;
        }

        /// <summary>
        /// Verifies a rejected borrowed-string insertion against the exact logical tuple route.<br/>
        /// The check remains allocation-conscious for present strings and uses LibraDex's dedicated null route for missing or null properties.<br/>
        /// </summary>
        /// <param name="binary">The authoritative Inheto payload that supplied the key.<br/></param>
        /// <param name="entry">The registered string index descriptor.<br/></param>
        /// <param name="recordID">The Abraxas identity expected on the route.<br/></param>
        /// <param name="result">The insertion result to verify.<br/></param>
        private static void EnsureBorrowedLibraDexStringInsertAccepted(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            ulong recordID,
            LibraDexGenericInsertResult result)
        {
            if (result.Inserted)
                return;

            try
            {
                LibraDexStringScalar8Index index = RequireLibraDexStringIndex(entry);
                bool exists = binary.TryReadStringUtf8Payload(entry.Path, out ReadOnlyMemory<byte> utf8)
                    ? index.ContainsTupleUtf8(utf8.Span, recordID)
                    : index.Entries.Exists(null!, recordID);
                if (exists)
                    return;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{entry.Path}' could not verify a rejected borrowed UTF-8 string insertion.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }

            throw new IndexContractException(
                entry.Unique
                    ? IndexContractFailureKind.UniqueConstraint
                    : IndexContractFailureKind.BackendMutation,
                entry.Unique
                    ? $"Unique index '{entry.Path}' rejected a string key because another record already owns it."
                    : $"Index '{entry.Path}' rejected the validated borrowed UTF-8 string tuple.",
                entry.Index.Name,
                entry.Path,
                recordID);
        }

        /// <summary>
        /// Requires the concrete logical LibraDex string facade selected during index registration.<br/>
        /// Abraxas uses this boundary so physical exact, folded, reversed, and sort-key subindexes remain invisible and cannot drift independently.<br/>
        /// </summary>
        /// <param name="entry">The ordinary or partial descriptor whose logical handle must be a string/scalar8 index.<br/></param>
        /// <returns>The concrete logical LibraDex string facade.<br/></returns>
        private static LibraDexStringScalar8Index RequireLibraDexStringIndex(LibraDexSimpleIndex entry)
            => entry.Index as LibraDexStringScalar8Index
                ?? throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Index '{entry.Path}' does not expose LibraDex's logical UTF-8 string mutation contract.",
                    entry.Index.Name,
                    entry.Path);

        /// <summary>
        /// Reads and inserts one supported non-null scalar through LibraDex's typed generic surface.<br/>
        /// Value-type keys and the UInt64 record identity cross the hot boundary without boxing; null or missing values deliberately use the existing untyped null-route contract.<br/>
        /// </summary>
        /// <param name="binary">The authoritative Inheto payload containing the indexed value.<br/></param>
        /// <param name="entry">The registered ordinary-index descriptor.<br/></param>
        /// <param name="recordID">The Abraxas record identity.<br/></param>
        /// <param name="result">Receives the ordinary LibraDex insert result when the key family is handled.<br/></param>
        /// <returns><see langword="true"/> when the key family is a supported typed scalar; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryInsertTypedLibraDexScalar(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            ulong recordID,
            out LibraDexGenericInsertResult result)
        {
            if (entry.CalculatedReader is not null)
            {
                var calculated = entry.CalculatedReader(binary);
                if (!calculated.HasValue)
                {
                    result = default;
                    return false;
                }

                object? value = calculated.Value;
                Type calculatedType = entry.KeyType;
                if (calculatedType == typeof(bool)) return InsertTypedScalar((bool?)value, entry, recordID, out result);
                if (calculatedType == typeof(byte)) return InsertTypedScalar((byte?)value, entry, recordID, out result);
                if (calculatedType == typeof(sbyte)) return InsertTypedScalar((sbyte?)value, entry, recordID, out result);
                if (calculatedType == typeof(short)) return InsertTypedScalar((short?)value, entry, recordID, out result);
                if (calculatedType == typeof(ushort)) return InsertTypedScalar((ushort?)value, entry, recordID, out result);
                if (calculatedType == typeof(char)) return InsertTypedScalar((char?)value, entry, recordID, out result);
                if (calculatedType == typeof(int)) return InsertTypedScalar((int?)value, entry, recordID, out result);
                if (calculatedType == typeof(uint)) return InsertTypedScalar((uint?)value, entry, recordID, out result);
                if (calculatedType == typeof(long)) return InsertTypedScalar((long?)value, entry, recordID, out result);
                if (calculatedType == typeof(ulong)) return InsertTypedScalar((ulong?)value, entry, recordID, out result);
                if (calculatedType == typeof(Int128)) return InsertTypedScalar((Int128?)value, entry, recordID, out result);
                if (calculatedType == typeof(UInt128)) return InsertTypedScalar((UInt128?)value, entry, recordID, out result);
                if (calculatedType == typeof(float)) return InsertTypedScalar((float?)value, entry, recordID, out result);
                if (calculatedType == typeof(double)) return InsertTypedScalar((double?)value, entry, recordID, out result);
                if (calculatedType == typeof(decimal)) return InsertTypedScalar((decimal?)value, entry, recordID, out result);
                if (calculatedType == typeof(Guid)) return InsertTypedScalar((Guid?)value, entry, recordID, out result);
                if (calculatedType == typeof(DateTime)) return InsertTypedScalar((DateTime?)value, entry, recordID, out result);
                if (calculatedType == typeof(DateTimeOffset)) return InsertTypedScalar((DateTimeOffset?)value, entry, recordID, out result);
                if (calculatedType == typeof(DateOnly)) return InsertTypedScalar((DateOnly?)value, entry, recordID, out result);
                if (calculatedType == typeof(TimeOnly)) return InsertTypedScalar((TimeOnly?)value, entry, recordID, out result);
                if (calculatedType == typeof(TimeSpan)) return InsertTypedScalar((TimeSpan?)value, entry, recordID, out result);

                result = default;
                return false;
            }

            Type type = entry.KeyType;
            if (type == typeof(bool)) return InsertTypedScalar(binary.ReadBoolean(entry.Path), entry, recordID, out result);
            if (type == typeof(byte)) return InsertTypedScalar(binary.ReadByte(entry.Path), entry, recordID, out result);
            if (type == typeof(sbyte)) return InsertTypedScalar(binary.ReadSByte(entry.Path), entry, recordID, out result);
            if (type == typeof(short)) return InsertTypedScalar(binary.ReadInt16(entry.Path), entry, recordID, out result);
            if (type == typeof(ushort)) return InsertTypedScalar(binary.ReadUInt16(entry.Path), entry, recordID, out result);
            if (type == typeof(char)) return InsertTypedScalar(binary.ReadChar(entry.Path), entry, recordID, out result);
            if (type == typeof(int)) return InsertTypedScalar(binary.ReadInt32(entry.Path), entry, recordID, out result);
            if (type == typeof(uint)) return InsertTypedScalar(binary.ReadUInt32(entry.Path), entry, recordID, out result);
            if (type == typeof(long)) return InsertTypedScalar(binary.ReadInt64(entry.Path), entry, recordID, out result);
            if (type == typeof(ulong)) return InsertTypedScalar(binary.ReadUInt64(entry.Path), entry, recordID, out result);
            if (type == typeof(Int128)) return InsertTypedScalar(binary.ReadInt128(entry.Path), entry, recordID, out result);
            if (type == typeof(UInt128)) return InsertTypedScalar(binary.ReadUInt128(entry.Path), entry, recordID, out result);
            if (type == typeof(float)) return InsertTypedScalar(binary.ReadSingle(entry.Path), entry, recordID, out result);
            if (type == typeof(double)) return InsertTypedScalar(binary.ReadDouble(entry.Path), entry, recordID, out result);
            if (type == typeof(decimal)) return InsertTypedScalar(binary.ReadDecimal(entry.Path), entry, recordID, out result);
            if (type == typeof(Guid)) return InsertTypedScalar(binary.ReadGuid(entry.Path), entry, recordID, out result);
            if (type == typeof(DateTime)) return InsertTypedScalar(binary.ReadDateTime(entry.Path), entry, recordID, out result);
            if (type == typeof(DateTimeOffset)) return InsertTypedScalar(binary.ReadDateTimeOffset(entry.Path), entry, recordID, out result);
            if (type == typeof(DateOnly)) return InsertTypedScalar(binary.ReadDateOnly(entry.Path), entry, recordID, out result);
            if (type == typeof(TimeOnly)) return InsertTypedScalar(binary.ReadTimeOnly(entry.Path), entry, recordID, out result);
            if (type == typeof(TimeSpan)) return InsertTypedScalar(binary.ReadTimeSpan(entry.Path), entry, recordID, out result);

            result = default;
            return false;
        }

        /// <summary>
        /// Inserts one nullable typed scalar while preserving Abraxas missing/null key routing.<br/>
        /// </summary>
        private static bool InsertTypedScalar<TKey>(
            TKey? key,
            LibraDexSimpleIndex entry,
            ulong recordID,
            out LibraDexGenericInsertResult result)
            where TKey : struct
        {
            if (!key.HasValue)
            {
                result = entry.Index.Insert(null, recordID);
                return true;
            }

            LibraDexIndex<TKey, ulong> index = RequireTypedScalarIndex<TKey>(entry);
            result = index.Insert(key.Value, recordID);
            return true;
        }

        /// <summary>
        /// Strictly reads one supported direct-path scalar before authoritative mutation and enforces its non-null unique-key ownership without boxing the key or record identity.<br/>
        /// Calculated scalar indexes return false so their projected keys are read and validated by the general calculated-key path.<br/>
        /// Read failures retain the ordinary Abraxas key-read contract, while backend lookup failures retain the backend-mutation contract.<br/>
        /// </summary>
        /// <param name="binary">The proposed authoritative Inheto payload.<br/></param>
        /// <param name="entry">The ordinary or partial index descriptor to validate.<br/></param>
        /// <param name="recordID">The record identity allowed to retain its own existing key.<br/></param>
        /// <returns><see langword="true"/> when the descriptor uses a supported typed scalar family; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryValidateTypedLibraDexScalar(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            ulong recordID)
        {
            if (entry.CalculatedReader is not null)
                return false;

            try
            {
                Type type = entry.KeyType;
                if (type == typeof(bool)) return ValidateTypedScalar(binary.ReadBoolean(entry.Path), entry, recordID);
                if (type == typeof(byte)) return ValidateTypedScalar(binary.ReadByte(entry.Path), entry, recordID);
                if (type == typeof(sbyte)) return ValidateTypedScalar(binary.ReadSByte(entry.Path), entry, recordID);
                if (type == typeof(short)) return ValidateTypedScalar(binary.ReadInt16(entry.Path), entry, recordID);
                if (type == typeof(ushort)) return ValidateTypedScalar(binary.ReadUInt16(entry.Path), entry, recordID);
                if (type == typeof(char)) return ValidateTypedScalar(binary.ReadChar(entry.Path), entry, recordID);
                if (type == typeof(int)) return ValidateTypedScalar(binary.ReadInt32(entry.Path), entry, recordID);
                if (type == typeof(uint)) return ValidateTypedScalar(binary.ReadUInt32(entry.Path), entry, recordID);
                if (type == typeof(long)) return ValidateTypedScalar(binary.ReadInt64(entry.Path), entry, recordID);
                if (type == typeof(ulong)) return ValidateTypedScalar(binary.ReadUInt64(entry.Path), entry, recordID);
                if (type == typeof(Int128)) return ValidateTypedScalar(binary.ReadInt128(entry.Path), entry, recordID);
                if (type == typeof(UInt128)) return ValidateTypedScalar(binary.ReadUInt128(entry.Path), entry, recordID);
                if (type == typeof(float)) return ValidateTypedScalar(binary.ReadSingle(entry.Path), entry, recordID);
                if (type == typeof(double)) return ValidateTypedScalar(binary.ReadDouble(entry.Path), entry, recordID);
                if (type == typeof(decimal)) return ValidateTypedScalar(binary.ReadDecimal(entry.Path), entry, recordID);
                if (type == typeof(Guid)) return ValidateTypedScalar(binary.ReadGuid(entry.Path), entry, recordID);
                if (type == typeof(DateTime)) return ValidateTypedScalar(binary.ReadDateTime(entry.Path), entry, recordID);
                if (type == typeof(DateTimeOffset)) return ValidateTypedScalar(binary.ReadDateTimeOffset(entry.Path), entry, recordID);
                if (type == typeof(DateOnly)) return ValidateTypedScalar(binary.ReadDateOnly(entry.Path), entry, recordID);
                if (type == typeof(TimeOnly)) return ValidateTypedScalar(binary.ReadTimeOnly(entry.Path), entry, recordID);
                if (type == typeof(TimeSpan)) return ValidateTypedScalar(binary.ReadTimeSpan(entry.Path), entry, recordID);
                return false;
            }
            catch (IndexContractException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.KeyRead,
                    $"Index '{entry.Path}' could not read its configured typed scalar key path '{entry.Path}'.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }
        }

        /// <summary>
        /// Validates one nullable typed scalar after Inheto extraction.<br/>
        /// Missing and null keys remain valid null-route values; non-unique keys require no lookup; unique non-null keys may be retained only by the same record identity.<br/>
        /// </summary>
        /// <typeparam name="TKey">Supported non-nullable scalar key type selected by index registration.<br/></typeparam>
        /// <param name="key">The proposed scalar value, or no value for a missing/null property path.<br/></param>
        /// <param name="entry">The ordinary or partial index descriptor whose typed handle is inspected.<br/></param>
        /// <param name="recordID">The record identity allowed to retain its own key.<br/></param>
        /// <returns>Always <see langword="true"/> after successful validation, allowing the caller to recognize the handled scalar family.<br/></returns>
        private static bool ValidateTypedScalar<TKey>(
            TKey? key,
            LibraDexSimpleIndex entry,
            ulong recordID)
            where TKey : struct
        {
            if (!key.HasValue || !entry.Unique)
                return true;

            try
            {
                if (!RequireTypedScalarIndex<TKey>(entry).Entries.ExistsOtherIdentity(key.Value, recordID))
                    return true;
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{entry.Path}' could not validate its typed unique-key route.",
                    entry.Index.Name,
                    entry.Path,
                    recordID,
                    exception);
            }

            throw new IndexContractException(
                IndexContractFailureKind.UniqueConstraint,
                $"Unique index '{entry.Path}' already contains the supplied non-null key for another record.",
                entry.Index.Name,
                entry.Path,
                recordID);
        }

        /// <summary>
        /// Reads and deletes one supported scalar through LibraDex's typed generic surface without boxing non-null values.<br/>
        /// </summary>
        /// <param name="binary">The authoritative Inheto payload containing the indexed value.<br/></param>
        /// <param name="entry">The registered ordinary or partial index descriptor.<br/></param>
        /// <param name="recordID">The Abraxas record identity paired with the key.<br/></param>
        /// <returns><see langword="true"/> when the key family is a supported typed scalar; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryDeleteTypedLibraDexScalar(
            InhetoBinary binary,
            LibraDexSimpleIndex entry,
            ulong recordID)
        {
            if (entry.CalculatedReader is not null)
            {
                var calculated = entry.CalculatedReader(binary);
                if (!calculated.HasValue)
                    return false;

                object? value = calculated.Value;
                Type calculatedType = entry.KeyType;
                if (calculatedType == typeof(bool)) return DeleteTypedScalar((bool?)value, entry, recordID);
                if (calculatedType == typeof(byte)) return DeleteTypedScalar((byte?)value, entry, recordID);
                if (calculatedType == typeof(sbyte)) return DeleteTypedScalar((sbyte?)value, entry, recordID);
                if (calculatedType == typeof(short)) return DeleteTypedScalar((short?)value, entry, recordID);
                if (calculatedType == typeof(ushort)) return DeleteTypedScalar((ushort?)value, entry, recordID);
                if (calculatedType == typeof(char)) return DeleteTypedScalar((char?)value, entry, recordID);
                if (calculatedType == typeof(int)) return DeleteTypedScalar((int?)value, entry, recordID);
                if (calculatedType == typeof(uint)) return DeleteTypedScalar((uint?)value, entry, recordID);
                if (calculatedType == typeof(long)) return DeleteTypedScalar((long?)value, entry, recordID);
                if (calculatedType == typeof(ulong)) return DeleteTypedScalar((ulong?)value, entry, recordID);
                if (calculatedType == typeof(Int128)) return DeleteTypedScalar((Int128?)value, entry, recordID);
                if (calculatedType == typeof(UInt128)) return DeleteTypedScalar((UInt128?)value, entry, recordID);
                if (calculatedType == typeof(float)) return DeleteTypedScalar((float?)value, entry, recordID);
                if (calculatedType == typeof(double)) return DeleteTypedScalar((double?)value, entry, recordID);
                if (calculatedType == typeof(decimal)) return DeleteTypedScalar((decimal?)value, entry, recordID);
                if (calculatedType == typeof(Guid)) return DeleteTypedScalar((Guid?)value, entry, recordID);
                if (calculatedType == typeof(DateTime)) return DeleteTypedScalar((DateTime?)value, entry, recordID);
                if (calculatedType == typeof(DateTimeOffset)) return DeleteTypedScalar((DateTimeOffset?)value, entry, recordID);
                if (calculatedType == typeof(DateOnly)) return DeleteTypedScalar((DateOnly?)value, entry, recordID);
                if (calculatedType == typeof(TimeOnly)) return DeleteTypedScalar((TimeOnly?)value, entry, recordID);
                if (calculatedType == typeof(TimeSpan)) return DeleteTypedScalar((TimeSpan?)value, entry, recordID);
                return false;
            }

            Type type = entry.KeyType;
            if (type == typeof(bool)) return DeleteTypedScalar(binary.ReadBoolean(entry.Path), entry, recordID);
            if (type == typeof(byte)) return DeleteTypedScalar(binary.ReadByte(entry.Path), entry, recordID);
            if (type == typeof(sbyte)) return DeleteTypedScalar(binary.ReadSByte(entry.Path), entry, recordID);
            if (type == typeof(short)) return DeleteTypedScalar(binary.ReadInt16(entry.Path), entry, recordID);
            if (type == typeof(ushort)) return DeleteTypedScalar(binary.ReadUInt16(entry.Path), entry, recordID);
            if (type == typeof(char)) return DeleteTypedScalar(binary.ReadChar(entry.Path), entry, recordID);
            if (type == typeof(int)) return DeleteTypedScalar(binary.ReadInt32(entry.Path), entry, recordID);
            if (type == typeof(uint)) return DeleteTypedScalar(binary.ReadUInt32(entry.Path), entry, recordID);
            if (type == typeof(long)) return DeleteTypedScalar(binary.ReadInt64(entry.Path), entry, recordID);
            if (type == typeof(ulong)) return DeleteTypedScalar(binary.ReadUInt64(entry.Path), entry, recordID);
            if (type == typeof(Int128)) return DeleteTypedScalar(binary.ReadInt128(entry.Path), entry, recordID);
            if (type == typeof(UInt128)) return DeleteTypedScalar(binary.ReadUInt128(entry.Path), entry, recordID);
            if (type == typeof(float)) return DeleteTypedScalar(binary.ReadSingle(entry.Path), entry, recordID);
            if (type == typeof(double)) return DeleteTypedScalar(binary.ReadDouble(entry.Path), entry, recordID);
            if (type == typeof(decimal)) return DeleteTypedScalar(binary.ReadDecimal(entry.Path), entry, recordID);
            if (type == typeof(Guid)) return DeleteTypedScalar(binary.ReadGuid(entry.Path), entry, recordID);
            if (type == typeof(DateTime)) return DeleteTypedScalar(binary.ReadDateTime(entry.Path), entry, recordID);
            if (type == typeof(DateTimeOffset)) return DeleteTypedScalar(binary.ReadDateTimeOffset(entry.Path), entry, recordID);
            if (type == typeof(DateOnly)) return DeleteTypedScalar(binary.ReadDateOnly(entry.Path), entry, recordID);
            if (type == typeof(TimeOnly)) return DeleteTypedScalar(binary.ReadTimeOnly(entry.Path), entry, recordID);
            if (type == typeof(TimeSpan)) return DeleteTypedScalar(binary.ReadTimeSpan(entry.Path), entry, recordID);
            return false;
        }

        /// <summary>
        /// Deletes one nullable typed scalar while preserving the existing null-route behavior.<br/>
        /// </summary>
        /// <typeparam name="TKey">Supported non-nullable scalar key type selected by index registration.<br/></typeparam>
        /// <param name="key">The scalar key, or no value for a missing/null property path.<br/></param>
        /// <param name="entry">The registered ordinary or partial index descriptor.<br/></param>
        /// <param name="recordID">The Abraxas record identity paired with the key.<br/></param>
        /// <returns>Always <see langword="true"/> after the typed family is handled.<br/></returns>
        private static bool DeleteTypedScalar<TKey>(
            TKey? key,
            LibraDexSimpleIndex entry,
            ulong recordID)
            where TKey : struct
        {
            if (!key.HasValue)
            {
                _ = entry.Index.Delete(null, recordID);
                return true;
            }

            RequireTypedScalarIndex<TKey>(entry).Delete(key.Value, recordID);
            return true;
        }

        /// <summary>
        /// Requires the concrete typed generic index selected during ordinary or partial index registration.<br/>
        /// </summary>
        /// <typeparam name="TKey">Expected non-nullable scalar key type.<br/></typeparam>
        /// <param name="entry">Descriptor whose physical handle must match <typeparamref name="TKey"/> and UInt64 identities.<br/></param>
        /// <returns>The concrete typed LibraDex index handle.<br/></returns>
        private static LibraDexIndex<TKey, ulong> RequireTypedScalarIndex<TKey>(LibraDexSimpleIndex entry)
            where TKey : struct
            => entry.Index as LibraDexIndex<TKey, ulong>
                ?? throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Index '{entry.Path}' does not expose the expected typed LibraDex scalar contract for '{typeof(TKey).FullName}'.",
                    entry.Index.Name,
                    entry.Path);

        /// <summary>
        /// Reads a regex-projected text key from a serialized payload.<br/>
        /// The source value is read as text, then the pre-registered regex and capture settings decide the key stored in LibraDex.<br/>
        /// </summary>
        /// <param name="binary">The serialized record payload that contains the source value.<br/></param>
        /// <param name="entry">The registered regex projection descriptor.<br/></param>
        /// <param name="key">The projected text key when this method returns <c>true</c>.<br/></param>
        /// <param name="strict">Whether extraction failures are reported as projection contract exceptions instead of treating the key as unavailable; existing index contract exceptions always propagate.<br/></param>
        /// <returns><c>true</c> when the source value matched and produced a key; otherwise <c>false</c>.</returns>
        private bool TryReadLibraDexRegexProjectionKey(InhetoBinary binary, LibraDexRegexProjectionIndex entry, out string key, bool strict = false)
        {
            key = string.Empty;
            try
            {
                var source = binary.ReadPropAs<string>(entry.SourcePath, NativeDeserializationOptions);
                if (source is null)
                    return false;

                var match = SelectRegexMatch(entry.Regex, source, entry.Settings?.Occurrence);
                if (!match.Success)
                    return false;

                if (entry.Settings?.SelectedGroupNumber.HasValue == true)
                {
                    var groupNumber = entry.Settings.SelectedGroupNumber.Value;
                    if (groupNumber >= match.Groups.Count)
                        return false;

                    var group = match.Groups[groupNumber];
                    if (!group.Success)
                        return false;

                    key = group.Value;
                    return true;
                }

                if (!string.IsNullOrEmpty(entry.Settings?.SelectedGroupName))
                {
                    var group = match.Groups[entry.Settings.SelectedGroupName!];
                    if (!group.Success)
                        return false;

                    key = group.Value;
                    return true;
                }

                key = match.Value;
                return true;
            }
            catch (IndexContractException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (strict)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Projection,
                        $"Regex index '{entry.LogicalPrefix}' could not produce a deterministic key from '{entry.SourcePath}'.",
                        entry.LogicalPrefix,
                        entry.SourcePath,
                        innerException: exception);
                }
                return false;
            }
        }

        private static Match SelectRegexMatch(Regex rx, string source, int? occurrence)
        {
            if (!occurrence.HasValue || occurrence.Value == 0)
                return rx.Match(source);

            var current = rx.Match(source);
            var index = 0;
            while (current.Success && index < occurrence.Value)
            {
                current = current.NextMatch();
                index++;
            }

            return current;
        }

        private static string BuildLibraDexRegexProjectionIndexName(string logicalPrefix, string propPath, Regex rx, MatchValueIndexOptions? settings)
        {
            var canonical = logicalPrefix + "|" + propPath + "|" + rx.ToString() + "|" + ((int)rx.Options).ToString() + "|" + (settings?.ComputeDeterministicId(16) ?? string.Empty);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            var hex = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
            return "rx_" + SanitizeTokenForIndex(propPath, 28) + "_" + hex;
        }

        /// <summary>
        /// Unregisters ordered backend state for a removed Abraxas index.<br/>
        /// Memory-backed catalogs owned by the backend descriptor are disposed here so volatile key material is released with the index.<br/>
        /// </summary>
        /// <param name="index">The Abraxas record-index handle being removed.<br/></param>
        /// <param name="clearPhysical">Whether to clear tuples before detaching the runtime descriptor; false is used after durable catalog drop.<br/></param>
        /// <param name="constrainOwner">Whether removal must match the exact persistent or volatile owner supplied by <paramref name="requiredOwnerCatalog"/>.<br/></param>
        /// <param name="requiredOwnerCatalog">The required volatile owner, or null when constrained removal targets only the persistent catalog.<br/></param>
        /// <returns><c>true</c> when a registered ordered backend was removed; otherwise <c>false</c>.</returns>
        private bool TryUnregisterLibraDexIndex(
            RecordIndex index,
            bool clearPhysical = true,
            bool constrainOwner = false,
            Catalog? requiredOwnerCatalog = null)
        {
            var removed = false;
            List<IIndex>? removedVirtualPropertyIndexes = null;
            lock (libraDexIndexLock)
            {
                var compositeIndexes = libraDexCompositeIndexes;
                if (compositeIndexes.Length != 0)
                {
                    LibraDexCompositeIndex[]? kept = null;
                    var keptCount = 0;
                    var removedComposite = false;
                    for (var i = 0; i < compositeIndexes.Length; i++)
                    {
                        var entry = compositeIndexes[i];
                        if (string.Equals(entry.LogicalName, index.Name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(entry.PhysicalName, index.Name, StringComparison.Ordinal))
                        {
                            if (constrainOwner && !ReferenceEquals(entry.OwnerCatalog, requiredOwnerCatalog))
                            {
                                kept ??= new LibraDexCompositeIndex[compositeIndexes.Length];
                                kept[keptCount++] = entry;
                                continue;
                            }
                            if (clearPhysical)
                                TryClearLibraDexIndex(entry.Index, entry.OwnerCatalog);
                            removed = true;
                            removedComposite = true;
                            continue;
                        }

                        kept ??= new LibraDexCompositeIndex[compositeIndexes.Length];
                        kept[keptCount++] = entry;
                    }

                    if (removedComposite)
                    {
                        if (kept is null || keptCount == 0)
                            libraDexCompositeIndexes = Array.Empty<LibraDexCompositeIndex>();
                        else if (keptCount == kept.Length)
                            libraDexCompositeIndexes = kept;
                        else
                        {
                            var trimmed = new LibraDexCompositeIndex[keptCount];
                            Array.Copy(kept, trimmed, keptCount);
                            libraDexCompositeIndexes = trimmed;
                        }
                    }
                }

                var regexIndexes = libraDexRegexProjectionIndexes;
                if (regexIndexes.Length != 0)
                {
                    LibraDexRegexProjectionIndex[]? kept = null;
                    var keptCount = 0;
                    for (var i = 0; i < regexIndexes.Length; i++)
                    {
                        var entry = regexIndexes[i];
                        if (string.Equals(entry.LogicalPrefix, index.Name, StringComparison.OrdinalIgnoreCase) &&
                            (!constrainOwner || ReferenceEquals(entry.OwnerCatalog, requiredOwnerCatalog)))
                        {
                            if (clearPhysical)
                                TryClearLibraDexIndex(entry.Index, entry.OwnerCatalog);
                            if (ShouldDisposeLibraDexCatalog(entry.OwnerCatalog))
                                entry.OwnerCatalog?.Dispose();
                            removed = true;
                            continue;
                        }

                        kept ??= new LibraDexRegexProjectionIndex[regexIndexes.Length];
                        kept[keptCount++] = entry;
                    }

                    if (removed)
                    {
                        if (kept is null || keptCount == 0)
                        {
                            libraDexRegexProjectionIndexes = Array.Empty<LibraDexRegexProjectionIndex>();
                        }
                        else if (keptCount == kept.Length)
                        {
                            libraDexRegexProjectionIndexes = kept;
                        }
                        else
                        {
                            var trimmed = new LibraDexRegexProjectionIndex[keptCount];
                            Array.Copy(kept, trimmed, keptCount);
                            libraDexRegexProjectionIndexes = trimmed;
                        }
                    }
                }

                var simpleIndexes = libraDexSimpleIndexes;
                if (simpleIndexes.Length != 0 && !string.IsNullOrWhiteSpace(index.KeyPath))
                {
                    LibraDexSimpleIndex[]? kept = null;
                    var keptCount = 0;
                    var removedSimple = false;
                    for (var i = 0; i < simpleIndexes.Length; i++)
                    {
                        var entry = simpleIndexes[i];
                        if (string.Equals(entry.Path, index.KeyPath, StringComparison.Ordinal) &&
                            (!constrainOwner || ReferenceEquals(entry.OwnerCatalog, requiredOwnerCatalog)))
                        {
                            if (entry.IsVirtualProperty)
                            {
                                removedVirtualPropertyIndexes ??= new List<IIndex>();
                                removedVirtualPropertyIndexes.Add(entry.Index);
                            }
                            if (clearPhysical)
                                TryClearLibraDexIndex(entry.Index, entry.OwnerCatalog);
                            if (ShouldDisposeLibraDexCatalog(entry.OwnerCatalog))
                                entry.OwnerCatalog?.Dispose();
                            removed = true;
                            removedSimple = true;
                            continue;
                        }

                        kept ??= new LibraDexSimpleIndex[simpleIndexes.Length];
                        kept[keptCount++] = entry;
                    }

                    if (removedSimple)
                    {
                        if (kept is null || keptCount == 0)
                        {
                            libraDexSimpleIndexes = Array.Empty<LibraDexSimpleIndex>();
                        }
                        else if (keptCount == kept.Length)
                        {
                            libraDexSimpleIndexes = kept;
                        }
                        else
                        {
                            var trimmed = new LibraDexSimpleIndex[keptCount];
                            Array.Copy(kept, trimmed, keptCount);
                            libraDexSimpleIndexes = trimmed;
                        }
                    }
                }
            }

            if (removedVirtualPropertyIndexes is not null)
            {
                for (var i = 0; i < removedVirtualPropertyIndexes.Count; i++)
                    UnregisterVirtualPropertyRuntime(removedVirtualPropertyIndexes[i]);
            }

            return removed;
        }

        /// <summary>
        /// Removes one ordinary index according to Abraxas developer intent and its physical catalog ownership.<br/>
        /// Persistent removal writes the Fractal/Inheto tombstone first, then drops the LibraDex definition and detaches the runtime descriptor; volatile removal clears and unregisters only the process-local catalog.<br/>
        /// </summary>
        /// <param name="index">The attached Abraxas index handle selected for removal.<br/></param>
        /// <returns><c>true</c> when a matching ordinary persistent or volatile index was removed; otherwise <c>false</c>.<br/></returns>
        private bool TryRemoveLibraDexIndex(RecordIndex index)
        {
            using var indexMutation = EnterPartialIndexMutationScope();
            if (TryRemoveLibraDexPartialIndex(index))
                return true;

            var compositeIndexes = libraDexCompositeIndexes;
            for (var i = 0; i < compositeIndexes.Length; i++)
            {
                var entry = compositeIndexes[i];
                if (!string.Equals(entry.LogicalName, index.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.PhysicalName, index.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.OwnerCatalog is not null)
                    return TryUnregisterLibraDexIndex(index, constrainOwner: true, requiredOwnerCatalog: entry.OwnerCatalog);
                return PersistDropAndUnregisterOrdinaryIndex(
                    index,
                    entry.LogicalName,
                    entry.PhysicalName,
                    entry.LogicalName);
            }

            var regexIndexes = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                if (!string.Equals(entry.LogicalPrefix, index.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.OwnerCatalog is not null)
                {
                    return TryUnregisterLibraDexIndex(
                        index,
                        constrainOwner: true,
                        requiredOwnerCatalog: entry.OwnerCatalog);
                }

                return PersistDropAndUnregisterOrdinaryIndex(
                    index,
                    entry.LogicalPrefix,
                    entry.Index.Name,
                    entry.SourcePath);
            }

            var simpleIndexes = libraDexSimpleIndexes;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                if (!string.Equals(entry.Path, index.KeyPath, StringComparison.Ordinal) &&
                    !string.Equals(entry.Path, index.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (entry.OwnerCatalog is not null)
                {
                    return TryUnregisterLibraDexIndex(
                        index,
                        constrainOwner: true,
                        requiredOwnerCatalog: entry.OwnerCatalog);
                }

                return PersistDropAndUnregisterOrdinaryIndex(
                    index,
                    entry.Path,
                    entry.Index.Name,
                    entry.Path);
            }

            return false;
        }

        /// <summary>
        /// Persists one ordinary-index tombstone, requests the physical LibraDex drop, and detaches the current runtime descriptor.<br/>
        /// A failed physical drop still detaches the descriptor and leaves the tombstone authoritative so reopen cannot revive the index accidentally.<br/>
        /// </summary>
        /// <param name="index">The attached Abraxas handle being detached.<br/></param>
        /// <param name="logicalName">The stable Abraxas removal identity.<br/></param>
        /// <param name="physicalName">The logical LibraDex catalog definition to deactivate.<br/></param>
        /// <param name="keyPath">Developer-facing key path used by the exception contract.<br/></param>
        /// <returns><c>true</c> after durable removal intent and runtime detachment complete.<br/></returns>
        private bool PersistDropAndUnregisterOrdinaryIndex(
            RecordIndex index,
            string logicalName,
            string physicalName,
            string keyPath)
        {
            rbase.AddOrdinaryIndexRemoval(new OrdinaryIndexRemovalEntry
            {
                StoreID = storeID,
                LogicalName = logicalName,
                PhysicalName = physicalName
            });
            rbase.RemoveOrdinaryIndexIntent(storeID, logicalName);
            rbase.RemoveApplicationCodecBinding(
                storeID,
                ApplicationCodecIndexKind.Ordinary,
                logicalName);

            try
            {
                _ = libraDexIdentityIndexes?.Drop(physicalName);
            }
            catch (Exception exception)
            {
                _ = TryUnregisterLibraDexIndex(
                    index,
                    clearPhysical: false,
                    constrainOwner: true,
                    requiredOwnerCatalog: null);
                RefreshUnavailableFunctionIndexProjection();
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{logicalName}' recorded durable removal intent but could not drop its physical ordered-backend definition.",
                    logicalName,
                    keyPath,
                    innerException: exception);
            }

            _ = TryUnregisterLibraDexIndex(
                index,
                clearPhysical: false,
                constrainOwner: true,
                requiredOwnerCatalog: null);
            RefreshUnavailableFunctionIndexProjection();
            return true;
        }

        /// <summary>
        /// Attempts to stream record identifiers in the lexicographic order defined by one or more registered LibraDex indexes.<br/>
        /// The first index supplies the primary ordered stream; secondary indexes are read through LibraDex's inverse key map only for equal-key runs.<br/>
        /// </summary>
        /// <param name="indexes">Abraxas index handles that identify the requested order.<br/></param>
        /// <param name="direction">Whole-order traversal direction requested by the caller.<br/></param>
        /// <param name="allowIndexSupportCreation">When true, Abraxas may create or extend LibraDex inverse support for the requested indexes.<br/></param>
        /// <param name="ids">The ordered identifier stream when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when LibraDex can execute the ordered stream; otherwise <c>false</c>.</returns>
        private bool TryIterateRecordIDsByLibraDexIndexes(RecordIndex[] indexes, RetrievalDirection direction, bool allowIndexSupportCreation, out IEnumerable<ulong> ids)
        {
            ids = default!;
            if (indexes is null || indexes.Length == 0)
                return false;

            EnsureLibraDexConvergence(nameof(TryIterateRecordIDsByLibraDexIndexes));

            if (indexes.Length == 1)
            {
                if (!TryGetLibraDexIndexGroup(indexes[0], out var singleGroup, out var singleIndex))
                    return false;

                ids = IterateLibraDexIndexOrder(singleGroup, new[] { singleIndex }, direction, allowIndexSupportCreation);
                return true;
            }

            if (!TryGetLibraDexIndexGroup(indexes[0], out CatalogIdentityGroupIndexes? group, out IIndex? primary))
                return false;

            var resolved = new IIndex[indexes.Length];
            resolved[0] = primary;
            for (var i = 1; i < indexes.Length; i++)
            {
                if (!TryGetLibraDexIndexGroup(indexes[i], out CatalogIdentityGroupIndexes? componentGroup, out IIndex? index) ||
                    !ReferenceEquals(group.Batch, componentGroup.Batch))
                {
                    return false;
                }

                resolved[i] = index;
            }

            ids = IterateLibraDexIndexOrder(group, resolved, direction, allowIndexSupportCreation);
            return true;
        }

        /// <summary>
        /// Attempts to stream one complete-store property order directly from mutation-converged LibraDex indexes.<br/>
        /// Eligibility proves that every requested path has a registered ordered index with complete null/empty participation and one-key-per-identity semantics where the key family requires that declaration.<br/>
        /// No Fractal identity enumeration, per-record existence check, candidate-set allocation, or result intersection is performed after this contract succeeds.<br/>
        /// </summary>
        /// <param name="sort">Property-path descriptors defining the complete store order.<br/></param>
        /// <param name="ids">Direct LibraDex identity stream when every descriptor is eligible.<br/></param>
        /// <returns><see langword="true"/> when the complete order can be trusted directly; otherwise <see langword="false"/> so the defensive compatibility route remains authoritative.<br/></returns>
        private bool TryIterateCompleteStoreRecordIDsByLibraDex(SortBy[] sort, out IEnumerable<ulong> ids)
        {
            ids = default!;
            if (sort is null || sort.Length == 0 || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            EnsureLibraDexConvergence(nameof(TryIterateCompleteStoreRecordIDsByLibraDex));

            bool descending = sort[0].Descending;
            var indexes = new RecordIndex[sort.Length];
            for (int sortIndex = 0; sortIndex < sort.Length; sortIndex++)
            {
                SortBy descriptor = sort[sortIndex];
                string path = descriptor.Key.Path;
                if (string.IsNullOrWhiteSpace(path) ||
                    descriptor.IgnoreCase ||
                    descriptor.Descending != descending ||
                    NativeDeserializationOptions.Aliases.Contains(path) ||
                    !TryGetOrCreateAutoLibraDexIndex(path, AutoIndexUse.Ordering, out RecordIndex index))
                {
                    return false;
                }

                LibraDexSimpleIndex? complete = null;
                LibraDexSimpleIndex[] registered = libraDexSimpleIndexes;
                for (int indexPosition = 0; indexPosition < registered.Length; indexPosition++)
                {
                    LibraDexSimpleIndex candidate = registered[indexPosition];
                    if (!string.Equals(candidate.Path, path, StringComparison.Ordinal) ||
                        candidate.CalculatedSelector is not null ||
                        !candidate.IncludeNullValues ||
                        !candidate.IncludeEmptyStrings ||
                        (candidate.Capabilities & IndexBackendCapabilities.OrderedRead) == 0 ||
                        (candidate.KeyType != typeof(string) &&
                         candidate.Index.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity))
                    {
                        continue;
                    }

                    complete = candidate;
                    break;
                }

                if (complete is null)
                    return false;
                indexes[sortIndex] = index;
            }

            RetrievalDirection direction = descending
                ? RetrievalDirection.Descending
                : RetrievalDirection.Ascending;
            return TryIterateRecordIDsByLibraDexIndexes(
                indexes,
                direction,
                allowIndexSupportCreation: true,
                out ids);
        }

        /// <summary>
        /// Attempts to stream record identities in the mixed per-key order of exact attached LibraDex indexes.<br/>
        /// The primary index supplies population and primary order; later indexes order equal-primary-key runs through reusable inverse support.<br/>
        /// </summary>
        private bool TryIterateRecordIDsByLibraDexIndexes(IndexSort[] order, bool allowIndexSupportCreation, out IEnumerable<ulong> ids)
        {
            ids = default!;
            if (order is null || order.Length == 0)
                return false;

            EnsureLibraDexConvergence(nameof(TryIterateRecordIDsByLibraDexIndexes));

            if (!order[0].Index.IsOwnedBy(this) ||
                !TryGetLibraDexIndexGroup(order[0].Index, out CatalogIdentityGroupIndexes? group, out IIndex? primary))
            {
                return false;
            }

            var indexes = new IIndex[order.Length];
            var directions = new SortDirection[order.Length];
            indexes[0] = primary;
            directions[0] = order[0].Direction;
            for (int i = 1; i < order.Length; i++)
            {
                if (!order[i].Index.IsOwnedBy(this) ||
                    !TryGetLibraDexIndexGroup(order[i].Index, out CatalogIdentityGroupIndexes? componentGroup, out IIndex? index) ||
                    !ReferenceEquals(group.Batch, componentGroup.Batch))
                {
                    return false;
                }

                indexes[i] = index;
                directions[i] = order[i].Direction;
            }

            ids = IterateLibraDexIndexOrder(group, indexes, directions, allowIndexSupportCreation);
            return true;
        }

        /// <summary>
        /// Attempts to count distinct key values present in a registered LibraDex ordered index.<br/>
        /// The implementation streams the index in key order and counts key transitions so duplicate identities under a non-unique key do not allocate per-key storage.<br/>
        /// </summary>
        /// <param name="recordIndex">Abraxas index handle that identifies the distinct key source.<br/></param>
        /// <param name="count">Distinct key count when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when LibraDex can execute the distinct count; otherwise <c>false</c>.<br/></returns>
        private bool TryCountDistinctByLibraDex(RecordIndex recordIndex, out ulong count)
        {
            using var lifecycle = EnterStoreOperationScope();
            count = 0;
            if (!TryGetLibraDexIndexGroup(recordIndex, out var group, out var index))
                return false;

            var condition = CreateLibraDexAllCondition(group, index);
            return CountDistinctKeysInTuples(group.IterateTuples(index, condition, QueryDirection.Ascending), null, out count);
        }

        /// <summary>
        /// Attempts to count distinct key values in a registered LibraDex ordered index after applying the shared automatic condition plan.<br/>
        /// Matching identities are gathered once from the direct, candidate/residual, or Fractal-scan route, then the target index is streamed in key order.<br/>
        /// </summary>
        /// <param name="recordIndex">Abraxas index handle that identifies the distinct key source.<br/></param>
        /// <param name="where">Condition that limits which records participate in the distinct count.<br/></param>
        /// <param name="count">Distinct key count when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when LibraDex can execute the filtered distinct count; otherwise <c>false</c>.<br/></returns>
        private bool TryCountDistinctWhereByLibraDex(RecordIndex recordIndex, Condition<T>.EndCondition where, out ulong count)
        {
            using var lifecycle = EnterStoreOperationScope();
            count = 0;
            if (!TryGetLibraDexIndexGroup(recordIndex, out var group, out var index))
                return false;

            var matching = new HashSet<ulong>();
            foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                where,
                nameof(CountDistinctWhere),
                includePayload: false))
            {
                matching.Add(match.RecordId);
            }

            if (matching.Count == 0)
                return true;

            var condition = CreateLibraDexAllCondition(group, index);
            return CountDistinctKeysInTuples(group.IterateTuples(index, condition, QueryDirection.Ascending), matching, out count);
        }

        private static bool CountDistinctKeysInTuples(IEnumerable<LibraDexRuntimeTuple> tuples, HashSet<ulong>? matchingIdentities, out ulong count)
        {
            count = 0;
            object? priorKey = null;
            var hasPrior = false;

            foreach (var tuple in tuples)
            {
                if (matchingIdentities is not null && !matchingIdentities.Contains(ToRecordId(tuple.Identity)))
                    continue;

                if (!hasPrior)
                {
                    hasPrior = true;
                    priorKey = tuple.Key;
                    count = 1;
                    continue;
                }

                if (CompareLibraDexSortKey(priorKey, tuple.Key) == 0)
                    continue;

                priorKey = tuple.Key;
                count++;
            }

            return true;
        }

        private bool TryGetDistinctValuesByLibraDex(RecordIndex recordIndex, Condition<T>.EndCondition? where, int? limit, out IReadOnlyList<object?> values)
        {
            using var lifecycle = EnterStoreOperationScope();
            values = default!;
            if (!TryGetLibraDexIndexGroup(recordIndex, out var group, out var index))
                return false;

            HashSet<ulong>? matching = null;
            if (where is not null)
            {
                matching = new HashSet<ulong>();
                foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                    where,
                    nameof(DistinctValues),
                    includePayload: false))
                {
                    matching.Add(match.RecordId);
                }

                if (matching.Count == 0)
                {
                    values = Array.Empty<object?>();
                    return true;
                }
            }

            var result = new List<object?>();
            var condition = CreateLibraDexAllCondition(group, index);
            object? priorKey = null;
            var hasPrior = false;
            foreach (var tuple in group.IterateTuples(index, condition, QueryDirection.Ascending))
            {
                if (matching is not null && !matching.Contains(ToRecordId(tuple.Identity)))
                    continue;

                if (hasPrior && CompareLibraDexSortKey(priorKey, tuple.Key) == 0)
                    continue;

                hasPrior = true;
                priorKey = tuple.Key;
                result.Add(tuple.Key);
                if (limit.HasValue && result.Count >= limit.Value)
                    break;
            }

            values = result.AsReadOnly();
            return true;
        }

        private bool TryIterateGroupingsByLibraDex(ISelector[] groupKeys, out IEnumerable<GroupInfo> groups)
        {
            groups = default!;
            if (groupKeys is null || groupKeys.Length != 1)
                return false;

            var selector = groupKeys[0];
            if (!TryResolveSimpleSelectorPath(selector, out var key, out _))
                return false;

            if (!TryGetOrCreateAutoLibraDexIndex(key.Path, AutoIndexUse.Distinct, out var recordIndex))
                return false;

            if (!TryGetLibraDexIndexGroup(recordIndex, out var group, out var index))
                return false;

            IEnumerable<GroupInfo> candidateGroups;
            if (!TryIterateSingleLibraDexGrouping(selector, key.Path, group, index, out candidateGroups))
                candidateGroups = IterateSingleLibraDexGroupingFallback(selector, key.Path, group, index);

            var materialized = candidateGroups.ToList();
            ulong indexedCount = 0;
            for (var i = 0; i < materialized.Count; i++)
                indexedCount = checked(indexedCount + materialized[i].Count);

            if (indexedCount != Count)
                return false;

            groups = materialized;
            return true;
        }

        private bool TryIterateSingleLibraDexGrouping(ISelector selector, string keyPath, CatalogIdentityGroupIndexes group, IIndex index, out IEnumerable<GroupInfo> groups)
        {
            groups = default!;
            switch (index)
            {
                case LibraDexIndex<int, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<uint, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<long, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<ulong, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<short, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<ushort, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<byte, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<sbyte, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<bool, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                case LibraDexIndex<Guid, ulong> typed:
                    groups = IterateSingleLibraDexGrouping(selector, keyPath, group, typed);
                    return true;
                default:
                    return false;
            }
        }

        private IEnumerable<GroupInfo> IterateSingleLibraDexGrouping<TKey>(ISelector selector, string keyPath, CatalogIdentityGroupIndexes group, LibraDexIndex<TKey, ulong> index)
        {
            using var lifecycle = EnterStoreOperationScope();
            var condition = CreateLibraDexAllCondition(group, index);
            var grouped = condition.Groups(name => string.Equals(name, index.Name, StringComparison.Ordinal) ? index : throw new KeyNotFoundException(name))
                .By(index)
                .Metadata();

            for (var i = 0; i < grouped.Count; i++)
            {
                var row = grouped[i];
                yield return CreateLibraDexGrouping(selector, keyPath, row.Key, checked((ulong)row.Count));
            }
        }

        private IEnumerable<GroupInfo> IterateSingleLibraDexGroupingFallback(ISelector selector, string keyPath, CatalogIdentityGroupIndexes group, IIndex index)
        {
            using var lifecycle = EnterStoreOperationScope();
            var condition = CreateLibraDexAllCondition(group, index);
            object? runKey = null;
            ulong count = 0;
            var hasRun = false;

            foreach (var tuple in group.IterateTuples(index, condition, QueryDirection.Ascending))
            {
                if (!hasRun)
                {
                    hasRun = true;
                    runKey = tuple.Key;
                    count = 1;
                    continue;
                }

                if (CompareLibraDexSortKey(runKey, tuple.Key) == 0)
                {
                    count++;
                    continue;
                }

                yield return CreateLibraDexGrouping(selector, keyPath, runKey, count);
                runKey = tuple.Key;
                count = 1;
            }

            if (hasRun)
                yield return CreateLibraDexGrouping(selector, keyPath, runKey, count);
        }

        private GroupInfo CreateLibraDexGrouping(ISelector selector, string keyPath, object? keyValue, ulong count)
        {
            var whereClause = AppendGroupingEquality(selector, keyValue, null);
            var where = whereClause is null ? null : whereClause.EndCondition;
            return new GroupInfo(
                this,
                where,
                Array.AsReadOnly(new[] { keyPath }),
                Array.AsReadOnly(new[] { keyValue }),
                count,
                sum: null,
                average: null,
                min: null,
                max: null,
                distinctCount: null);
        }

        /// <summary>
        /// Streams record identifiers from a resolved LibraDex index order.<br/>
        /// Secondary sort work is bounded to equal-primary-key runs so ordinary unique or high-cardinality primary indexes stay close to a direct cursor walk.<br/>
        /// </summary>
        /// <param name="group">The bound LibraDex identity group.<br/></param>
        /// <param name="indexes">Resolved LibraDex indexes in caller-requested priority order.<br/></param>
        /// <param name="direction">Whole-order traversal direction requested by the caller.<br/></param>
        /// <param name="allowIndexSupportCreation">When true, Abraxas may create or extend LibraDex inverse support for secondary keys.<br/></param>
        /// <returns>Record identifiers in requested ordered-index order.<br/></returns>
        private IEnumerable<ulong> IterateLibraDexIndexOrder(CatalogIdentityGroupIndexes group, IIndex[] indexes, RetrievalDirection direction, bool allowIndexSupportCreation)
        {
            using var lifecycle = EnterStoreOperationScope();
            var primary = indexes[0];
            var queryDirection = direction == RetrievalDirection.Descending ? QueryDirection.Descending : QueryDirection.Ascending;

            if (indexes.Length == 1)
            {
                foreach (var tuple in group.IterateTuples(primary, queryDirection))
                    yield return ToRecordId(tuple.Identity);

                yield break;
            }

            PrepareLibraDexInverseForOrdering(group, indexes, allowIndexSupportCreation);
            var secondaryNames = new string[indexes.Length - 1];
            for (var i = 1; i < indexes.Length; i++)
                secondaryNames[i - 1] = indexes[i].Name;

            var runIds = new List<ulong>(4);
            object? runKey = null;
            var hasRun = false;
            foreach (var tuple in group.IterateTuples(primary, queryDirection))
            {
                var id = ToRecordId(tuple.Identity);
                if (!hasRun)
                {
                    hasRun = true;
                    runKey = tuple.Key;
                    runIds.Add(id);
                    continue;
                }

                if (CompareLibraDexSortKey(runKey, tuple.Key) == 0)
                {
                    runIds.Add(id);
                    continue;
                }

                foreach (var orderedId in OrderedRun(group, runIds, secondaryNames, direction))
                    yield return orderedId;

                runIds.Clear();
                runKey = tuple.Key;
                runIds.Add(id);
            }

            if (!hasRun)
                yield break;

            foreach (var orderedId in OrderedRun(group, runIds, secondaryNames, direction))
                yield return orderedId;
        }

        /// <summary>
        /// Streams record identities from exact LibraDex indexes using one independently declared direction per lexicographic key.<br/>
        /// Equal complete keys use ascending record identity as the deterministic final tie-breaker.<br/>
        /// </summary>
        private IEnumerable<ulong> IterateLibraDexIndexOrder(
            CatalogIdentityGroupIndexes group,
            IIndex[] indexes,
            SortDirection[] directions,
            bool allowIndexSupportCreation)
        {
            using var lifecycle = EnterStoreOperationScope();
            if (indexes.Length != directions.Length)
                throw new ArgumentException("Every ordered index requires one direction.", nameof(directions));

            IIndex primary = indexes[0];
            QueryDirection queryDirection = directions[0] == SortDirection.Descending
                ? QueryDirection.Descending
                : QueryDirection.Ascending;

            if (indexes.Length == 1)
            {
                foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(primary, queryDirection))
                    yield return ToRecordId(tuple.Identity);

                yield break;
            }

            PrepareLibraDexInverseForOrdering(group, indexes, allowIndexSupportCreation);
            var secondaryNames = new string[indexes.Length - 1];
            var secondaryDirections = new SortDirection[indexes.Length - 1];
            for (int i = 1; i < indexes.Length; i++)
            {
                secondaryNames[i - 1] = indexes[i].Name;
                secondaryDirections[i - 1] = directions[i];
            }

            var runIds = new List<ulong>(4);
            object? runKey = null;
            bool hasRun = false;
            foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(primary, queryDirection))
            {
                ulong id = ToRecordId(tuple.Identity);
                if (!hasRun)
                {
                    hasRun = true;
                    runKey = tuple.Key;
                    runIds.Add(id);
                    continue;
                }

                if (CompareLibraDexSortKey(runKey, tuple.Key) == 0)
                {
                    runIds.Add(id);
                    continue;
                }

                foreach (ulong orderedId in OrderedRun(group, runIds, secondaryNames, secondaryDirections))
                    yield return orderedId;

                runIds.Clear();
                runKey = tuple.Key;
                runIds.Add(id);
            }

            if (!hasRun)
                yield break;

            foreach (ulong orderedId in OrderedRun(group, runIds, secondaryNames, secondaryDirections))
                yield return orderedId;
        }

        /// <summary>
        /// Resolves one public Abraxas index handle to both its exact LibraDex runtime index and owning identity group.<br/>
        /// Developer-named partial and regular-expression projections are matched by logical name before ordinary property-path fallback so a full index on the same path cannot silently replace the requested filtered or projected index.<br/>
        /// Partial indexes additionally fail closed unless their readiness watermark matches the authoritative Fractal store USN.<br/>
        /// </summary>
        /// <param name="recordIndex">The store-owned public index handle to resolve.<br/></param>
        /// <param name="group">The persistent or volatile identity group that owns the resolved index.<br/></param>
        /// <param name="index">The exact registered LibraDex index when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when the requested logical index has a ready ordered backend; otherwise <c>false</c>.<br/></returns>
        private bool TryGetLibraDexIndexGroup(RecordIndex recordIndex, out CatalogIdentityGroupIndexes group, out IIndex index)
        {
            EnsureLibraDexConvergence(nameof(TryGetLibraDexIndexGroup));
            group = default!;
            index = default!;
            var partialIndexes = libraDexPartialIndexes;
            for (var i = 0; i < partialIndexes.Length; i++)
            {
                var entry = partialIndexes[i];
                if (!string.Equals(entry.LogicalName, recordIndex.Name, StringComparison.OrdinalIgnoreCase) ||
                    !IsPartialIndexUsable(entry))
                {
                    continue;
                }

                if (entry.OwnerCatalog is null)
                {
                    var persistentGroup = libraDexIdentityIndexes;
                    if (persistentGroup is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                        return false;
                    group = persistentGroup;
                }
                else
                {
                    group = entry.OwnerCatalog.Indexes.IndexSet(indexGroupName);
                }
                index = entry.Index;
                return true;
            }

            var regexIndexes = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                var entry = regexIndexes[i];
                if (!string.Equals(entry.LogicalPrefix, recordIndex.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.OwnerCatalog is null)
                {
                    var persistentGroup = libraDexIdentityIndexes;
                    if (persistentGroup is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                        return false;

                    group = persistentGroup;
                }
                else
                {
                    group = entry.OwnerCatalog.Indexes.IndexSet(indexGroupName);
                }

                index = entry.Index;
                return true;
            }

            var simpleIndexes = libraDexSimpleIndexes;
            if (simpleIndexes.Length == 0 || string.IsNullOrWhiteSpace(recordIndex.KeyPath))
                return false;

            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                if (!string.Equals(entry.Path, recordIndex.KeyPath, StringComparison.Ordinal))
                    continue;

                if (entry.OwnerCatalog is null)
                {
                    var persistentGroup = libraDexIdentityIndexes;
                    if (persistentGroup is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                        return false;

                    group = persistentGroup;
                }
                else
                {
                    group = entry.OwnerCatalog.Indexes.IndexSet(indexGroupName);
                }

                index = entry.Index;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Opens the exact developer-selected ordered index as a source-specialized identity cursor.<br/>
        /// No index is created, substituted, or supplemented; incompatible identity multiplicity fails before the public reader is returned.<br/>
        /// Reflection is confined to this open-time generic-key dispatch and does not participate in read, pull, or skip hot loops.<br/>
        /// </summary>
        /// <param name="recordIndex">Store-owned index handle whose complete stable order is requested.<br/></param>
        /// <param name="direction">Whole-index traversal direction.<br/></param>
        /// <returns>A typed LibraDex cursor adapted to the centralized Abraxas identity source contract.<br/></returns>
        private IIdentitySourceCursor OpenIndexedIdentitySourceCursor(
            RecordIndex recordIndex,
            RetrievalDirection direction)
        {
            if (!recordIndex.IsOwnedBy(this))
                throw new ArgumentException("The selected index handle was not issued by this RecordStore.", nameof(recordIndex));
            foreach (LibraDexCompositeIndex composite in libraDexCompositeIndexes)
            {
                if (!string.Equals(composite.LogicalName, recordIndex.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                CatalogIdentityGroupIndexes group = composite.OwnerCatalog is null
                    ? libraDexIdentityIndexes ?? throw new InvalidOperationException($"Composite index '{recordIndex.Name}' has no ready identity group.")
                    : composite.OwnerCatalog.Indexes.IndexSet(indexGroupName);
                EnsureLibraDexConvergence(nameof(OpenIndexedIdentitySourceCursor));
                QueryDirection compositeDirection = direction == RetrievalDirection.Descending
                    ? QueryDirection.Descending : QueryDirection.Ascending;
                var paths = new string?[composite.Descriptors.Length];
                var types = new Type[composite.Descriptors.Length];
                for (int ordinal = 0; ordinal < composite.Descriptors.Length; ordinal++)
                {
                    SelectorDescriptor<T> descriptor = composite.Descriptors[ordinal];
                    string? path = descriptor.DirectPropertyPath;
                    if (path is not null && descriptor.ResultType != typeof(DateTimeOffset))
                    {
                        PropPathCheck checkedPath = InhetoBinary.CheckPropPath(typeof(T), path);
                        if (checkedPath.IsValid && checkedPath.ResultType == descriptor.ResultType)
                            paths[ordinal] = checkedPath.CanonicalPath;
                    }
                    types[ordinal] = descriptor.ResultType;
                }
                return new CompositeIndexIdentitySourceCursor(
                    group.IterateTuples(composite.Index, compositeDirection), paths, types);
            }
            if (!TryGetLibraDexIndexGroup(recordIndex, out _, out IIndex index))
            {
                throw new InvalidOperationException(
                    $"Index '{recordIndex.Name}' is not available as a ready ordered index. Create or rebuild that exact index before opening this reader.");
            }
            if (index.IdentityType != typeof(ulong))
            {
                throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Index '{recordIndex.Name}' does not expose Abraxas UInt64 record identities.",
                    recordIndex.Name,
                    recordIndex.KeyPath);
            }
            if (index is LibraDexStringScalar8Index stringIndex)
            {
                QueryDirection stringDirection = direction == RetrievalDirection.Descending
                    ? QueryDirection.Descending
                    : QueryDirection.Ascending;
                return new EnumerableIdentitySourceCursor(stringIndex.IterateExactIdentities(stringDirection));
            }
            if (index.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
            {
                throw new IndexContractException(
                    IndexContractFailureKind.IdentityMultiplicity,
                    $"Index '{recordIndex.Name}' may contain one identity under multiple keys and therefore cannot define an identity-reader order. Select an index created with one key per identity.",
                    recordIndex.Name,
                    recordIndex.KeyPath);
            }

            Type runtimeType = index.GetType();
            Type[] genericArguments = runtimeType.IsGenericType ? runtimeType.GetGenericArguments() : Type.EmptyTypes;
            if (genericArguments.Length != 2 || runtimeType.GetGenericTypeDefinition() != typeof(LibraDexIndex<,>))
            {
                throw new IndexContractException(
                    IndexContractFailureKind.Configuration,
                    $"Index '{recordIndex.Name}' does not expose a source-aware ordered identity cursor.",
                    recordIndex.Name,
                    recordIndex.KeyPath);
            }

            MethodInfo factory = typeof(RecordStore<T>).GetMethod(
                nameof(OpenTypedIndexedIdentitySourceCursor),
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingMethodException(nameof(OpenTypedIndexedIdentitySourceCursor));
            try
            {
                return (IIdentitySourceCursor)factory
                    .MakeGenericMethod(genericArguments[0])
                    .Invoke(this, new object[]
                    {
                        index,
                        direction,
                        InhetoBinary.CheckPropPath(typeof(T), recordIndex.KeyPath).CanonicalPath,
                        CanExposeExactOrderedIndexKey(recordIndex, index)
                    })!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        /// Performs the once-per-open typed dispatch for an exact LibraDex index key type.<br/>
        /// The returned adapter keeps subsequent identity reads and skips strongly typed and reflection-free.<br/>
        /// </summary>
        /// <typeparam name="TKey">Runtime CLR key type of the selected index.<br/></typeparam>
        /// <param name="index">Resolved exact LibraDex index.<br/></param>
        /// <param name="direction">Requested traversal direction.<br/></param>
        /// <param name="canonicalKeyPath">Canonical source property path retained by the cursor for exact-key access.<br/></param>
        /// <param name="exposesExactKey">Whether the cursor may expose its native key as the exact authoritative value of the canonical source path.<br/></param>
        /// <returns>A source-specialized Abraxas cursor.<br/></returns>
        private IIdentitySourceCursor OpenTypedIndexedIdentitySourceCursor<TKey>(
            IIndex index,
            RetrievalDirection direction,
            string canonicalKeyPath,
            bool exposesExactKey)
        {
            var typedIndex = (LibraDexIndex<TKey, ulong>)index;
            QueryDirection queryDirection = direction == RetrievalDirection.Descending
                ? QueryDirection.Descending
                : QueryDirection.Ascending;
            return new LibraDexIdentitySourceCursor<TKey>(
                typedIndex.OpenReader(queryDirection),
                canonicalKeyPath,
                exposesExactKey);
        }

        /// <summary>
        /// Determines whether an explicitly selected LibraDex ordering may also supply its current key as the authoritative Abraxas value for the same path.<br/>
        /// Exact CLR type agreement is required, nullable value paths are excluded because the generic cursor does not expose null-route provenance, and <see cref="DateTimeOffset"/> is excluded because ordered encodings may intentionally canonicalize offsets.<br/>
        /// Folded, normalized, sort-key, reversed, regex, and semantic-name projections fail the path/type proof and therefore continue through Fractal/Inheto.<br/>
        /// </summary>
        /// <param name="recordIndex">Developer-selected Abraxas index handle.<br/></param>
        /// <param name="index">Resolved physical LibraDex index.<br/></param>
        /// <returns><see langword="true"/> only when current-key projection is reversible and exact.<br/></returns>
        private static bool CanExposeExactOrderedIndexKey(RecordIndex recordIndex, IIndex index)
        {
            if (string.IsNullOrWhiteSpace(recordIndex.KeyPath))
                return false;

            PropPathCheck path = InhetoBinary.CheckPropPath(typeof(T), recordIndex.KeyPath);
            if (!path.IsValid || path.ResultType is null)
                return false;

            Type resultType = path.ResultType;
            Type? nullableType = Nullable.GetUnderlyingType(resultType);
            if (nullableType is not null)
                return false;

            if (resultType == typeof(DateTimeOffset))
                return false;

            return resultType == index.KeyType;
        }

        /// <summary>
        /// Attempts to adopt one already-ready complete LibraDex index as the source for a single-value reader.<br/>
        /// Adoption is intentionally conservative: the index must represent the exact requested path, retain null and empty participation, expose one key per identity, support ordered reads, and prove reversible current-key projection.<br/>
        /// No index is created by this discovery path; absence or incompatibility simply preserves authoritative Fractal/Inheto traversal.<br/>
        /// </summary>
        /// <param name="valuePath">Requested value path, in developer or canonical form.<br/></param>
        /// <param name="plan">Receives a replayable exact-index row plan when adoption succeeds.<br/></param>
        /// <returns><see langword="true"/> when the matching ready index can supply the complete value-reader population and exact current key.<br/></returns>
        private bool TryCreateExactValueProjectionRowPlan(string valuePath, out ReaderSourcePlan? plan)
        {
            plan = null;
            PropPathCheck requested = InhetoBinary.CheckPropPath(typeof(T), valuePath);
            if (!requested.IsValid)
                return false;

            LibraDexSimpleIndex[] indexes = libraDexSimpleIndexes;
            for (var i = 0; i < indexes.Length; i++)
            {
                LibraDexSimpleIndex candidate = indexes[i];
                if (!string.Equals(candidate.Path, requested.CanonicalPath, StringComparison.Ordinal) ||
                    !candidate.IncludeNullValues ||
                    !candidate.IncludeEmptyStrings ||
                    (candidate.Capabilities & IndexBackendCapabilities.OrderedRead) == 0 ||
                    candidate.Index.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
                {
                    continue;
                }

                var handle = new RecordIndex(candidate.Path, candidate.Path, candidate.Unique, this);
                if (!CanExposeExactOrderedIndexKey(handle, candidate.Index))
                    continue;

                plan = CreateIndexRowPlan(handle, RetrievalDirection.Ascending);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Opens one exact ordered index and filters it through the identities produced by the existing condition planner.<br/>
        /// The selected index still defines order and population; the condition remains an independent selection contract and no alternative index is invented.<br/>
        /// </summary>
        /// <param name="recordIndex">Exact ordered index selected by the developer.<br/></param>
        /// <param name="where">Completed condition used to select identities.<br/></param>
        /// <param name="direction">Whole-index traversal direction.<br/></param>
        /// <param name="operationName">Operation name used by planner diagnostics.<br/></param>
        /// <returns>An ordered filtering cursor.<br/></returns>
        private IIdentitySourceCursor OpenConditionedIndexedIdentitySourceCursor(
            RecordIndex recordIndex,
            Condition<T>.EndCondition where,
            RetrievalDirection direction,
            string operationName)
        {
            ArgumentNullException.ThrowIfNull(where);
            var accepted = new HashSet<ulong>();
            foreach (ulong identity in IteratePlannedIdentityMatches(where, operationName))
                accepted.Add(identity);

            IIdentitySourceCursor source = OpenIndexedIdentitySourceCursor(recordIndex, direction);
            return new FilteringIdentitySourceCursor(source, accepted);
        }

        private bool TryGetOrCreateAutoLibraDexIndex(string propPath, AutoIndexUse use, out RecordIndex index)
        {
            index = default;
            if (string.IsNullOrWhiteSpace(propPath))
                return false;

            if (!TryResolveSimplePropertyType(propPath, out _, out Inheto.PropPathCheck pathCheck))
                return false;
            propPath = pathCheck.CanonicalPath;
            if (TryGetRegisteredLibraDexRecordIndex(propPath, out index))
            {
                TouchAutoLibraDexIndex(index.KeyPath, use);
                return true;
            }

            var options = AutoIndexing;
            if (!options.Enabled || (options.Uses & use) == 0 || options.MaxVolatileIndexes < 1)
                return false;

            if (pathCheck.RequiresClrContinuation)
                return false;

            TrimAutoIndexes();
            if (CountAutoLibraDexIndexes() >= options.MaxVolatileIndexes)
            {
                if (options.Eviction != AutoIndexEviction.LeastRecentlyUsed &&
                    options.Eviction != AutoIndexEviction.IdleTimeout &&
                    options.Eviction != AutoIndexEviction.BackgroundIdleTimeout)
                {
                    return false;
                }

                if (!RemoveLeastRecentlyUsedAutoLibraDexIndex())
                    return false;
            }

            if (!TryRegisterLibraDexMemorySimplePathIndex(propPath, unique: false, autoCreated: true, use))
                return false;

            EnsureAutoIndexTrimTimer();
            return TryGetRegisteredLibraDexRecordIndex(propPath, out index);
        }

        /// <summary>
        /// Removes auto-created volatile helper indexes that are idle under the current store policy.<br/>
        /// Explicit developer-created memory indexes are not eligible for removal through this method.<br/>
        /// </summary>
        /// <returns>The number of auto-created volatile indexes removed.<br/></returns>
        public int TrimAutoIndexes()
        {
            using var operation = EnterStoreOperationScope();
            var options = AutoIndexing;
            if (options.Eviction != AutoIndexEviction.IdleTimeout &&
                options.Eviction != AutoIndexEviction.BackgroundIdleTimeout)
            {
                return 0;
            }

            var idleTimeout = options.IdleTimeout;
            if (idleTimeout <= TimeSpan.Zero)
                return 0;

            return TrimAutoLibraDexIndexes(DateTime.UtcNow - idleTimeout);
        }

        private int TrimAutoLibraDexIndexes(DateTime removeBeforeUtc)
        {
            var removed = 0;
            lock (libraDexIndexLock)
            {
                var current = libraDexSimpleIndexes;
                if (current.Length == 0)
                    return 0;

                LibraDexSimpleIndex[]? kept = null;
                var keptCount = 0;
                for (var i = 0; i < current.Length; i++)
                {
                    var entry = current[i];
                    if (entry.AutoCreated && entry.OwnerCatalog is not null && entry.LastUsedUtc < removeBeforeUtc)
                    {
                        TryClearLibraDexIndex(entry.Index, entry.OwnerCatalog);
                        removed++;
                        continue;
                    }

                    kept ??= new LibraDexSimpleIndex[current.Length];
                    kept[keptCount++] = entry;
                }

                if (removed == 0)
                    return 0;

                if (keptCount == 0)
                    libraDexSimpleIndexes = Array.Empty<LibraDexSimpleIndex>();
                else if (keptCount == kept!.Length)
                    libraDexSimpleIndexes = kept;
                else
                {
                    var trimmed = new LibraDexSimpleIndex[keptCount];
                    Array.Copy(kept!, trimmed, keptCount);
                    libraDexSimpleIndexes = trimmed;
                }
            }

            return removed;
        }

        private int CountAutoLibraDexIndexes()
        {
            var simpleIndexes = libraDexSimpleIndexes;
            var count = 0;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                if (simpleIndexes[i].AutoCreated)
                    count++;
            }

            return count;
        }

        private bool RemoveLeastRecentlyUsedAutoLibraDexIndex()
        {
            lock (libraDexIndexLock)
            {
                var current = libraDexSimpleIndexes;
                var removeAt = -1;
                var oldest = DateTime.MaxValue;
                for (var i = 0; i < current.Length; i++)
                {
                    var entry = current[i];
                    if (!entry.AutoCreated || entry.OwnerCatalog is null || entry.LastUsedUtc >= oldest)
                        continue;

                    oldest = entry.LastUsedUtc;
                    removeAt = i;
                }

                if (removeAt < 0)
                    return false;

                TryClearLibraDexIndex(current[removeAt].Index, current[removeAt].OwnerCatalog);
                var next = new LibraDexSimpleIndex[current.Length - 1];
                if (removeAt > 0)
                    Array.Copy(current, 0, next, 0, removeAt);
                if (removeAt < current.Length - 1)
                    Array.Copy(current, removeAt + 1, next, removeAt, current.Length - removeAt - 1);

                libraDexSimpleIndexes = next;
            }
            return true;
        }

        private void TouchAutoLibraDexIndex(string propPath, AutoIndexUse use)
        {
            if (string.IsNullOrWhiteSpace(propPath))
                return;

            lock (libraDexIndexLock)
            {
                var current = libraDexSimpleIndexes;
                for (var i = 0; i < current.Length; i++)
                {
                    var entry = current[i];
                    if (!entry.AutoCreated || (entry.AutoUse & use) == 0 || !string.Equals(entry.Path, propPath, StringComparison.Ordinal))
                        continue;

                    var next = (LibraDexSimpleIndex[])current.Clone();
                    next[i] = entry.WithLastUsedUtc(DateTime.UtcNow);
                    libraDexSimpleIndexes = next;
                    break;
                }
            }
        }

        private void EnsureAutoIndexTrimTimer()
        {
            var options = AutoIndexing;
            if (options.Eviction != AutoIndexEviction.BackgroundIdleTimeout)
                return;

            var interval = options.BackgroundTrimInterval;
            if (interval <= TimeSpan.Zero)
                interval = TimeSpan.FromMinutes(1);

            if (autoIndexTrimTimer is not null)
                return;

            var state = new AutoIndexTrimTimerState(this);
            var timer = new Timer(static stateObject =>
            {
                var state = (AutoIndexTrimTimerState)stateObject!;
                if (!state.Store.TryGetTarget(out var store))
                {
                    state.Timer?.Dispose();
                    return;
                }

                try
                {
                    store.TrimAutoIndexes();
                }
                catch
                {
                }
            }, state, interval, interval);
            state.Timer = timer;

            if (Interlocked.CompareExchange(ref autoIndexTrimTimer, timer, null) is not null)
                timer.Dispose();
        }

        private bool ShouldDisposeLibraDexCatalog(Catalog? catalog)
        {
            return catalog is not null && !ReferenceEquals(catalog, libraDexVolatileCatalog);
        }

        /// <summary>
        /// Determines whether this store currently has any registered volatile LibraDex index descriptor.<br/>
        /// </summary>
        /// <returns><see langword="true"/> when a simple, projected, composite, partial, or auto-created volatile index is registered; otherwise <see langword="false"/>.<br/></returns>
        private bool HasVolatileLibraDexIndexes()
        {
            var simpleIndexes = libraDexSimpleIndexes;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                if (simpleIndexes[i].OwnerCatalog is not null)
                    return true;
            }

            var regexIndexes = libraDexRegexProjectionIndexes;
            for (var i = 0; i < regexIndexes.Length; i++)
            {
                if (regexIndexes[i].OwnerCatalog is not null)
                    return true;
            }

            var compositeIndexes = libraDexCompositeIndexes;
            for (var i = 0; i < compositeIndexes.Length; i++)
            {
                if (compositeIndexes[i].OwnerCatalog is not null)
                    return true;
            }

            var partialIndexes = libraDexPartialIndexes;
            for (var i = 0; i < partialIndexes.Length; i++)
            {
                if (partialIndexes[i].OwnerCatalog is not null)
                    return true;
            }

            return false;
        }

        private sealed class AutoIndexTrimTimerState
        {
            internal AutoIndexTrimTimerState(RecordStore<T> store)
            {
                Store = new WeakReference<RecordStore<T>>(store);
            }

            internal WeakReference<RecordStore<T>> Store { get; }
            internal Timer? Timer { get; set; }
        }

        /// <summary>
        /// Resolves a single record identifier from a registered unique ordered backend using an exact key comparison.<br/>
        /// The public caller still speaks in Abraxas property paths and values; this method contains the LibraDex condition vocabulary and only handles key shapes already registered for ordered writes.<br/>
        /// </summary>
        /// <param name="propPath">Abraxas property path identifying the unique index.<br/></param>
        /// <param name="uniquePropValue">Exact key value requested by the caller.<br/></param>
        /// <param name="recordID">Resolved record identifier when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when a unique ordered backend produced a matching identity; otherwise <c>false</c> so the caller can use the existing fallback path.<br/></returns>
        private bool TryGetUniqueLibraDexRecordID(string propPath, object uniquePropValue, out ulong recordID)
        {
            using var lifecycle = EnterStoreOperationScope();
            recordID = 0;
            var group = libraDexIdentityIndexes;
            if (group is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly || string.IsNullOrWhiteSpace(propPath))
                return false;

            var simpleIndexes = libraDexSimpleIndexes;
            for (var i = 0; i < simpleIndexes.Length; i++)
            {
                var entry = simpleIndexes[i];
                if (!entry.Unique || !string.Equals(entry.Path, propPath, StringComparison.Ordinal))
                    continue;

                if (!TryCreateLibraDexEqualCondition(group, entry.Index, uniquePropValue, out var condition))
                    return false;

                using var cursor = group.AbraxasIdentityQuery<ulong>().OpenCursor(
                    condition,
                    deduplication: IdentityDeduplication.Distinct,
                    take: 1);

                if (!cursor.Next())
                    return false;

                recordID = ToRecordId(cursor.GetIdentity());
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to stream record identifiers from the internal persistent ordered State index.<br/>
        /// Exact and masked reads both use LibraDex's native Int64 condition path; bitmask execution therefore shares the same generic engine behavior as every ordinary integral index.<br/>
        /// </summary>
        /// <param name="state">State value to compare against.<br/></param>
        /// <param name="mask">Optional bitmask. When provided, records satisfy <c>(State &amp; mask) == state</c>.<br/></param>
        /// <param name="ids">A lazy identifier sequence when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when the internal ordered State index is available; otherwise <c>false</c>.<br/></returns>
        private bool TryIterateRecordIDsByLibraDexState(long state, long? mask, out IEnumerable<ulong> ids)
        {
            EnsureLibraDexConvergence(nameof(TryIterateRecordIDsByLibraDexState));
            ids = default!;
            var group = libraDexIdentityIndexes;
            var index = libraDexStateIndex;
            if (group is null || index is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            if (mask.HasValue)
            {
                ids = IterateLibraDexStateMask(group, index, state, mask.Value);
                return true;
            }

            var condition = group.Where(index).EqualTo(state).EndCondition;
            var cursor = group.AbraxasIdentityQuery<ulong>().OpenCursor(
                condition,
                deduplication: IdentityDeduplication.Distinct);
            ids = IterateCursor(cursor);
            return true;
        }

        /// <summary>
        /// Attempts to count records through the internal persistent ordered State index.<br/>
        /// </summary>
        /// <param name="state">State value to compare against.<br/></param>
        /// <param name="mask">Optional bitmask. When provided, records satisfy <c>(State &amp; mask) == state</c>.<br/></param>
        /// <param name="count">The matching record count when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when the internal ordered State index is available; otherwise <c>false</c>.<br/></returns>
        private bool TryCountByLibraDexState(long state, long? mask, out ulong count)
        {
            using var lifecycle = EnterStoreOperationScope();
            EnsureLibraDexConvergence(nameof(TryCountByLibraDexState));
            count = 0;
            var group = libraDexIdentityIndexes;
            var index = libraDexStateIndex;
            if (group is null || index is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            if (!mask.HasValue)
            {
                var condition = group.Where(index).EqualTo(state).EndCondition;
                count = checked((ulong)index.Count(condition));
                return true;
            }

            var maskedCondition = group.Where((IIndex)index).AsInt64.BitAnd(mask.Value, state).EndCondition;
            count = checked((ulong)index.Count(maskedCondition));

            return true;
        }

        /// <summary>
        /// Attempts to test record/state membership through the internal persistent ordered State index.<br/>
        /// Both exact and masked checks resolve the record identity directly, then compare its single assigned State value in constant expected time.<br/>
        /// </summary>
        /// <param name="recordID">The Abraxas record identifier to check.<br/></param>
        /// <param name="state">State value to compare against.<br/></param>
        /// <param name="mask">Optional bitmask. When provided, records satisfy <c>(State &amp; mask) == state</c>.<br/></param>
        /// <param name="exists"><c>true</c> when the record has a matching State tuple.<br/></param>
        /// <returns><c>true</c> when the internal ordered State index is available; otherwise <c>false</c>.<br/></returns>
        private bool TryExistsByLibraDexState(ulong recordID, long state, long? mask, out bool exists)
        {
            using var lifecycle = EnterStoreOperationScope();
            EnsureLibraDexConvergence(nameof(TryExistsByLibraDexState));
            exists = false;
            var index = libraDexStateIndex;
            if (libraDexIdentityIndexes is null ||
                index is null ||
                libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
            {
                return false;
            }

            lock (libraDexIndexLock)
            {
                if (!index.TryGetSingleKey(recordID, out long assignedState))
                    return true;

                exists = mask.HasValue
                    ? (assignedState & mask.Value) == state
                    : assignedState == state;
                return true;
            }
        }

        /// <summary>
        /// Reads the optional reserved State value for one already-admitted condition candidate without re-entering public lifecycle or Fractal-existence checks.<br/>
        /// The planned condition pipeline calls this only when its compiled semantic tree contains a State leaf.<br/>
        /// </summary>
        /// <param name="recordID">Current authoritative candidate identity.<br/></param>
        /// <returns>The assigned State value, or <c>null</c> when the record has no State tuple.<br/></returns>
        private long? ReadOptionalStateForCondition(ulong recordID)
        {
            var index = RequireLibraDexStateIndex(nameof(ReadOptionalStateForCondition));
            lock (libraDexIndexLock)
                return index.TryGetSingleKey(recordID, out long state) ? state : null;
        }

        /// <summary>
        /// Attempts to produce a State histogram from the internal persistent ordered State index.<br/>
        /// The method allocates only the returned dictionary because the public contract requires materialized grouped counts.<br/>
        /// </summary>
        /// <param name="histogram">Read-only state counts when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when the internal ordered State index is available; otherwise <c>false</c>.<br/></returns>
        private bool TryGetLibraDexStateHistogram(out IReadOnlyDictionary<long, ulong> histogram)
        {
            using var lifecycle = EnterStoreOperationScope();
            EnsureLibraDexConvergence(nameof(TryGetLibraDexStateHistogram));
            histogram = default!;
            var group = libraDexIdentityIndexes;
            var index = libraDexStateIndex;
            if (group is null || index is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            var results = new Dictionary<long, ulong>();
            var condition = CreateAllLibraDexStateCondition(group, index);
            foreach (var tuple in group.IterateTuples(index, condition, QueryDirection.Ascending))
            {
                if (!TryGetStateKey(tuple.Key, out var key))
                    continue;

                results.TryGetValue(key, out var count);
                results[key] = count + 1;
            }

            histogram = results;
            return true;
        }

        /// <summary>
        /// Attempts to read the reserved State index's validated availability and assigned-identity tuple count for backend-neutral diagnostics.<br/>
        /// The physical reserved index name remains internal; a successful result means the persisted State contract was validated when the store was bound.<br/>
        /// </summary>
        /// <param name="tupleCount">The number of identities with an assigned State value when this method returns <see langword="true"/>.<br/></param>
        /// <returns><see langword="true"/> when the validated persistent State index is available; otherwise <see langword="false"/>.<br/></returns>
        private bool TryGetLibraDexStateTupleCount(out ulong tupleCount)
        {
            using var lifecycle = EnterStoreOperationScope();
            tupleCount = 0;
            var group = libraDexIdentityIndexes;
            var index = libraDexStateIndex;
            if (group is null || index is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return false;

            var condition = CreateAllLibraDexStateCondition(group, index);
            tupleCount = checked((ulong)index.Count(condition));
            return true;
        }

        private IEnumerable<ulong> IterateLibraDexStateMask(CatalogIdentityGroupIndexes group, IIndex index, long state, long mask)
        {
            using var lifecycle = EnterStoreOperationScope();
            var condition = group.Where(index).AsInt64.BitAnd(mask, state).EndCondition;
            using var cursor = group.AbraxasIdentityQuery<ulong>().OpenCursor(
                condition,
                deduplication: IdentityDeduplication.Distinct);
            while (cursor.Next())
                yield return ToRecordId(cursor.GetIdentity());
        }

        /// <summary>
        /// Builds the exhaustive State condition used by masked scans, histograms, and repair traversal.<br/>
        /// The disjunction covers every signed 64-bit key while remaining on LibraDex's typed Int64 condition surface.<br/>
        /// </summary>
        /// <param name="group">The persistent identity group that owns the reserved State index.<br/></param>
        /// <param name="index">The reserved typed State index.<br/></param>
        /// <returns>A completed condition matching every State tuple.<br/></returns>
        private static LibraDexConditionEndCondition CreateAllLibraDexStateCondition(
            CatalogIdentityGroupIndexes group,
            IIndex index)
            => group.Where(index).AsInt64.EqualTo(0L)
                .OR.Where(index.Name).AsInt64.NotEqualTo(0L)
                .EndCondition;

        /// <summary>
        /// Requires the reserved persistent State index for an original-name State operation.<br/>
        /// State is authoritative LibraDex metadata, so an unavailable binding is an operational failure rather than evidence that no records have State.<br/>
        /// </summary>
        /// <param name="operationName">Public or internal operation requesting authoritative State access.<br/></param>
        /// <returns>The bound typed persistent State index.<br/></returns>
        private LibraDexIndex<long, ulong> RequireLibraDexStateIndex(string operationName)
        {
            if (libraDexStateIndex is not null &&
                libraDexIdentityIndexes is not null &&
                libraDexBackendMode != LibraDexIndexBackendMode.FractalOnly)
            {
                return libraDexStateIndex;
            }

            throw new InvalidOperationException(
                $"Operation '{operationName}' requires this store's reserved persistent LibraDex State index.");
        }

        /// <summary>
        /// Reconciles the reserved State index against authoritative Fractal record existence.<br/>
        /// Missing State is valid and cannot be reconstructed because State values intentionally live only in LibraDex; repair therefore removes only tuples whose identity belongs to another store or no longer exists in Fractal.<br/>
        /// The tuple scan is materialized before mutation so LibraDex cursors are never invalidated by an in-flight delete.<br/>
        /// </summary>
        /// <returns>The number of orphan State tuples removed.<br/></returns>
        private ulong RepairLibraDexStateIndex()
        {
            using var lifecycle = EnterStoreOperationScope();
            rbase.EnsureWriteAllowed(nameof(RepairStateIndex));
            var group = libraDexIdentityIndexes
                ?? throw new InvalidOperationException("The reserved persistent LibraDex State index group is not available.");
            var index = RequireLibraDexStateIndex(nameof(RepairStateIndex));

            lock (libraDexIndexLock)
            {
                var orphans = new List<(long State, ulong RecordID)>();
                var condition = CreateAllLibraDexStateCondition(group, index);
                foreach (var tuple in group.IterateTuples(index, condition, QueryDirection.Ascending))
                {
                    if (!TryGetStateKey(tuple.Key, out var state))
                        throw new InvalidDataException("The reserved State index returned a non-Int64 key.");

                    var recordID = ToRecordId(tuple.Identity);
                    if ((recordID >> 56) != storeID || !rbase.data.RecordExists(recordID))
                        orphans.Add((state, recordID));
                }

                ulong removed = 0;
                for (var i = 0; i < orphans.Count; i++)
                {
                    var orphan = orphans[i];
                    index.Delete(orphan.State, orphan.RecordID);
                    if (index.TryGetSingleKey(orphan.RecordID, out _))
                    {
                        throw new InvalidOperationException(
                            $"State-index repair could not remove orphan identity '{orphan.RecordID}'.");
                    }
                    removed++;
                }

                return removed;
            }
        }

        private static bool TryGetStateKey(object? key, out long state)
        {
            if (key is long value)
            {
                state = value;
                return true;
            }

            state = 0;
            return false;
        }

        /// <summary>
        /// Creates an exact-match LibraDex condition for a runtime index when the supplied value exactly matches the registered key type.<br/>
        /// Returning <c>false</c> is intentional for unsupported or coercion-required values so existing Abraxas fallback behavior remains available.<br/>
        /// </summary>
        /// <param name="group">Bound LibraDex identity group.<br/></param>
        /// <param name="index">Runtime ordered index handle.<br/></param>
        /// <param name="value">Caller-supplied key value.<br/></param>
        /// <param name="condition">Completed exact-match condition when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when an exact condition was created; otherwise <c>false</c>.<br/></returns>
        private static bool TryCreateLibraDexEqualCondition(CatalogIdentityGroupIndexes group, IIndex index, object value, out LibraDexConditionEndCondition condition)
        {
            condition = default!;
            var keyType = index.KeyType;

            if (keyType == typeof(string))
            {
                if (value is not string stringValue)
                    return false;

                condition = group.Where(index).AsString.EqualTo(stringValue).EndCondition;
                return true;
            }

            if (keyType == typeof(bool))
            {
                if (value is not bool boolValue)
                    return false;

                condition = group.Where(index).AsBoolean.EqualTo(boolValue).EndCondition;
                return true;
            }

            if (keyType == typeof(int))
            {
                if (value is not int intValue)
                    return false;

                condition = group.Where(index).AsInt32.EqualTo(intValue).EndCondition;
                return true;
            }

            if (keyType == typeof(uint))
            {
                if (value is not uint uintValue)
                    return false;

                condition = group.Where(index).AsUInt32.EqualTo(uintValue).EndCondition;
                return true;
            }

            if (keyType == typeof(long))
            {
                if (value is not long longValue)
                    return false;

                condition = group.Where(index).AsInt64.EqualTo(longValue).EndCondition;
                return true;
            }

            if (keyType == typeof(ulong))
            {
                if (value is not ulong ulongValue)
                    return false;

                condition = group.Where(index).AsUInt64.EqualTo(ulongValue).EndCondition;
                return true;
            }

            if (keyType == typeof(short))
            {
                if (value is not short shortValue)
                    return false;

                condition = group.Where(index).AsInt16.EqualTo(shortValue).EndCondition;
                return true;
            }

            if (keyType == typeof(ushort))
            {
                if (value is not ushort ushortValue)
                    return false;

                condition = group.Where(index).AsUInt16.EqualTo(ushortValue).EndCondition;
                return true;
            }

            if (keyType == typeof(byte))
            {
                if (value is not byte byteValue)
                    return false;

                condition = group.Where(index).AsByte.EqualTo(byteValue).EndCondition;
                return true;
            }

            if (keyType == typeof(sbyte))
            {
                if (value is not sbyte sbyteValue)
                    return false;

                condition = group.Where(index).AsSByte.EqualTo(sbyteValue).EndCondition;
                return true;
            }

            if (keyType == typeof(Guid))
            {
                if (value is not Guid guidValue)
                    return false;

                condition = group.Where(index).AsGuid.EqualTo(guidValue).EndCondition;
                return true;
            }

            return false;
        }

        private static LibraDexConditionEndCondition CreateLibraDexAllCondition(CatalogIdentityGroupIndexes group, IIndex index)
        {
            if (index.KeyType == typeof(string))
                return group.Where(index).AsString.EqualTo(string.Empty).OR.Where(index.Name).AsString.NotEqualTo(string.Empty).EndCondition;
            if (index.KeyType == typeof(bool))
                return group.Where(index).AsBoolean.EqualTo(false).OR.Where(index.Name).AsBoolean.NotEqualTo(false).EndCondition;
            if (index.KeyType == typeof(int))
                return group.Where(index).AsInt32.EqualTo(0).OR.Where(index.Name).AsInt32.NotEqualTo(0).EndCondition;
            if (index.KeyType == typeof(uint))
                return group.Where(index).AsUInt32.EqualTo(0U).OR.Where(index.Name).AsUInt32.NotEqualTo(0U).EndCondition;
            if (index.KeyType == typeof(long))
                return group.Where(index).AsInt64.EqualTo(0L).OR.Where(index.Name).AsInt64.NotEqualTo(0L).EndCondition;
            if (index.KeyType == typeof(ulong))
                return group.Where(index).AsUInt64.EqualTo(0UL).OR.Where(index.Name).AsUInt64.NotEqualTo(0UL).EndCondition;
            if (index.KeyType == typeof(short))
                return group.Where(index).AsInt16.EqualTo((short)0).OR.Where(index.Name).AsInt16.NotEqualTo((short)0).EndCondition;
            if (index.KeyType == typeof(ushort))
                return group.Where(index).AsUInt16.EqualTo((ushort)0).OR.Where(index.Name).AsUInt16.NotEqualTo((ushort)0).EndCondition;
            if (index.KeyType == typeof(byte))
                return group.Where(index).AsByte.EqualTo((byte)0).OR.Where(index.Name).AsByte.NotEqualTo((byte)0).EndCondition;
            if (index.KeyType == typeof(sbyte))
                return group.Where(index).AsSByte.EqualTo((sbyte)0).OR.Where(index.Name).AsSByte.NotEqualTo((sbyte)0).EndCondition;
            if (index.KeyType == typeof(float))
                return group.Where(index).AsSingle.EqualTo(0F).OR.Where(index.Name).AsSingle.NotEqualTo(0F).EndCondition;
            if (index.KeyType == typeof(double))
                return group.Where(index).AsDouble.EqualTo(0D).OR.Where(index.Name).AsDouble.NotEqualTo(0D).EndCondition;
            if (index.KeyType == typeof(decimal))
                return group.Where(index).AsDecimal.EqualTo(0M).OR.Where(index.Name).AsDecimal.NotEqualTo(0M).EndCondition;
            if (index.KeyType == typeof(Guid))
                return group.Where(index).AsGuid.EqualTo(Guid.Empty).OR.Where(index.Name).AsGuid.NotEqualTo(Guid.Empty).EndCondition;

            throw new NotSupportedException($"Index '{index.Name}' key type '{index.KeyType.FullName}' is not supported for ordered iteration.");
        }

        private static void PrepareLibraDexInverseForOrdering(CatalogIdentityGroupIndexes group, IIndex[] indexes, bool allowIndexSupportCreation)
        {
            var inverse = group.Inverse;
            var names = new string[indexes.Length];
            for (var i = 0; i < indexes.Length; i++)
                names[i] = indexes[i].Name;

            if (!inverse.IsCreated)
            {
                if (!allowIndexSupportCreation)
                    throw new InvalidOperationException("Multi-index ordered iteration requires LibraDex inverse support. Pass allowIndexSupportCreation: true to let Abraxas create reusable index support for the requested indexes.");

                inverse.Create(names);
                return;
            }

            var included = inverse.IncludedIndexNames;
            for (var i = 0; i < names.Length; i++)
            {
                if (ContainsOrdinal(included, names[i]))
                    continue;

                if (!allowIndexSupportCreation)
                    throw new InvalidOperationException($"Multi-index ordered iteration requires inverse support for index '{names[i]}'. Pass allowIndexSupportCreation: true to let Abraxas include it.");

                inverse.Include(names[i]);
                included = inverse.IncludedIndexNames;
            }

            if (!inverse.IsFresh)
                inverse.Rebuild();
        }

        private static bool ContainsOrdinal(string[] values, string value)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static IEnumerable<ulong> OrderedRun(CatalogIdentityGroupIndexes group, List<ulong> runIds, string[] secondaryNames, RetrievalDirection direction)
        {
            if (runIds.Count <= 1)
            {
                for (var i = 0; i < runIds.Count; i++)
                    yield return runIds[i];

                yield break;
            }

            var rows = group.Inverse.Get(runIds, secondaryNames);
            var keysByIdentity = new Dictionary<ulong, IReadOnlyDictionary<string, IReadOnlyList<object?>>>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
                keysByIdentity[rows[i].Identity] = rows[i].Keys;

            InsertionSortRun(runIds, keysByIdentity, secondaryNames, direction);
            for (var i = 0; i < runIds.Count; i++)
                yield return runIds[i];
        }

        private static IEnumerable<ulong> OrderedRun(
            CatalogIdentityGroupIndexes group,
            List<ulong> runIds,
            string[] secondaryNames,
            SortDirection[] secondaryDirections)
        {
            if (runIds.Count <= 1)
            {
                for (int i = 0; i < runIds.Count; i++)
                    yield return runIds[i];

                yield break;
            }

            var rows = group.Inverse.Get(runIds, secondaryNames);
            var keysByIdentity = new Dictionary<ulong, IReadOnlyDictionary<string, IReadOnlyList<object?>>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
                keysByIdentity[rows[i].Identity] = rows[i].Keys;

            InsertionSortRun(runIds, keysByIdentity, secondaryNames, secondaryDirections);
            for (int i = 0; i < runIds.Count; i++)
                yield return runIds[i];
        }

        private static void InsertionSortRun(List<ulong> ids, Dictionary<ulong, IReadOnlyDictionary<string, IReadOnlyList<object?>>> keysByIdentity, string[] secondaryNames, RetrievalDirection direction)
        {
            for (var i = 1; i < ids.Count; i++)
            {
                var current = ids[i];
                var j = i - 1;
                while (j >= 0 && CompareSecondaryKeys(ids[j], current, keysByIdentity, secondaryNames, direction) > 0)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = current;
            }
        }

        private static void InsertionSortRun(
            List<ulong> ids,
            Dictionary<ulong, IReadOnlyDictionary<string, IReadOnlyList<object?>>> keysByIdentity,
            string[] secondaryNames,
            SortDirection[] secondaryDirections)
        {
            for (int i = 1; i < ids.Count; i++)
            {
                ulong current = ids[i];
                int j = i - 1;
                while (j >= 0 && CompareSecondaryKeys(
                    ids[j],
                    current,
                    keysByIdentity,
                    secondaryNames,
                    secondaryDirections) > 0)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = current;
            }
        }

        private static int CompareSecondaryKeys(ulong left, ulong right, Dictionary<ulong, IReadOnlyDictionary<string, IReadOnlyList<object?>>> keysByIdentity, string[] secondaryNames, RetrievalDirection direction)
        {
            for (var i = 0; i < secondaryNames.Length; i++)
            {
                var leftKey = GetFirstInverseKey(keysByIdentity, left, secondaryNames[i]);
                var rightKey = GetFirstInverseKey(keysByIdentity, right, secondaryNames[i]);
                var comparison = CompareLibraDexSortKey(leftKey, rightKey);
                if (comparison == 0)
                    continue;

                return direction == RetrievalDirection.Descending ? -comparison : comparison;
            }

            return 0;
        }

        private static int CompareSecondaryKeys(
            ulong left,
            ulong right,
            Dictionary<ulong, IReadOnlyDictionary<string, IReadOnlyList<object?>>> keysByIdentity,
            string[] secondaryNames,
            SortDirection[] secondaryDirections)
        {
            for (int i = 0; i < secondaryNames.Length; i++)
            {
                object? leftKey = GetFirstInverseKey(keysByIdentity, left, secondaryNames[i]);
                object? rightKey = GetFirstInverseKey(keysByIdentity, right, secondaryNames[i]);
                int comparison = CompareLibraDexSortKey(leftKey, rightKey);
                if (comparison == 0)
                    continue;

                return secondaryDirections[i] == SortDirection.Descending ? -comparison : comparison;
            }

            return left.CompareTo(right);
        }

        private static object? GetFirstInverseKey(Dictionary<ulong, IReadOnlyDictionary<string, IReadOnlyList<object?>>> keysByIdentity, ulong identity, string indexName)
        {
            if (!keysByIdentity.TryGetValue(identity, out var keys) ||
                !keys.TryGetValue(indexName, out var values) ||
                values.Count == 0)
            {
                return null;
            }

            return values[0];
        }

        private static int CompareLibraDexSortKey(object? left, object? right)
        {
            if (left is null || right is null)
                return left is null ? right is null ? 0 : -1 : 1;

            if (left is byte[] leftBytes && right is byte[] rightBytes)
                return CompareBytes(leftBytes, rightBytes);

            if (left is string leftText && right is string rightText)
                return string.CompareOrdinal(leftText, rightText);

            if (left is IComparable comparable)
                return comparable.CompareTo(right);

            return string.CompareOrdinal(left.ToString(), right.ToString());
        }

        private static int CompareBytes(byte[] left, byte[] right)
        {
            var length = Math.Min(left.Length, right.Length);
            for (var i = 0; i < length; i++)
            {
                var comparison = left[i].CompareTo(right[i]);
                if (comparison != 0)
                    return comparison;
            }

            return left.Length.CompareTo(right.Length);
        }

        private static ulong ToRecordId(object identity)
        {
            if (identity is ulong id)
                return id;

            throw new InvalidOperationException($"LibraDex returned identity type '{identity.GetType().FullName}' where Abraxas expected UInt64 record identifiers.");
        }

        /// <summary>
        /// Resolves a simple dotted Abraxas property path against the store record type.<br/>
        /// </summary>
        /// <param name="propPath">The Abraxas property path, such as <c>.Age</c>.<br/></param>
        /// <param name="type">The resolved member type when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when all path segments resolve to fields or properties; otherwise <c>false</c>.</returns>
        private static bool TryResolveSimplePropertyType(string propPath, out Type type)
            => TryResolveSimplePropertyType(propPath, out type, out _);

        /// <summary>
        /// Resolves and canonicalizes a dotted Abraxas property path against the store record type while retaining its reusable Inheto execution plan.<br/>
        /// </summary>
        /// <param name="propPath">The Abraxas property path, such as <c>.CompanyName.Length</c>.<br/></param>
        /// <param name="type">The resolved member type when this method returns <see langword="true"/>.<br/></param>
        /// <param name="check">The immutable Inheto property-path plan, including any CLR continuation boundary.<br/></param>
        /// <returns><see langword="true"/> when the complete path resolves under Inheto's admitted member policy; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryResolveSimplePropertyType(string propPath, out Type type, out Inheto.PropPathCheck check)
        {
            type = typeof(object);
            check = InhetoBinary.CheckPropPath(typeof(T), propPath);
            if (!check.IsValid || check.ResultType is null)
                return false;

            type = check.ResultType;
            return true;
        }

        /// <summary>
        /// Creates the shared Inheto-backed key reader required when a logical index path necessarily continues through CLR properties or parameterless methods after its serialized prefix.<br/>
        /// Directly serialized paths return <see langword="null"/> so existing borrowed UTF-8 and typed scalar hot paths remain allocation-conscious.<br/>
        /// </summary>
        /// <param name="propPath">Logical Abraxas property path used by conditions and index intent.<br/></param>
        /// <param name="keyType">CLR key type selected for the LibraDex index.<br/></param>
        /// <returns>A calculated key reader for CLR-continuation paths; otherwise <see langword="null"/>.<br/></returns>
        private Func<InhetoBinary, (bool HasValue, object? Value)>? CreateClrContinuationReader(string propPath, Type keyType)
        {
            Inheto.PropPathCheck check = InhetoBinary.CheckPropPath(typeof(T), propPath, keyType);
            if (!check.IsValid)
            {
                throw new InvalidOperationException(
                    $"Index property path '{propPath}' is not resolvable from '{typeof(T).FullName}': {check.FailureKind}" +
                    (string.IsNullOrEmpty(check.FailedSegment) ? "." : $" at segment '{check.FailedSegment}'."));
            }

            if (!check.RequiresClrContinuation)
                return null;
            if (string.IsNullOrEmpty(check.SerializedPrefixCandidate))
                throw new NotSupportedException($"Index property path '{propPath}' invokes CLR members before reaching a serialized Inheto value.");

            return binary =>
            {
                Inheto.PropPathReadState state = binary.TryReadPropPath(
                    check,
                    out object? value,
                    NativeDeserializationOptions,
                    throwOnInvocationError: true);
                return state == Inheto.PropPathReadState.Value
                    ? (true, value)
                    : (false, null);
            };
        }

        private void ThrowIfLibraDexRequired(Condition<T>.EndCondition where)
        {
            if (libraDexBackendMode != LibraDexIndexBackendMode.Required)
                return;

            throw new NotSupportedException("LibraDex is required for this RecordStore query, but the condition or index shape is not mapped.");
        }

        private IEnumerable<ulong> IterateCursor(LibraDexIdentityCursor<ulong> cursor)
        {
            using var lifecycle = EnterStoreOperationScope();
            using (cursor)
            {
                while (cursor.Next())
                {
                    yield return cursor.GetIdentity();
                }
            }
        }

        private static StringKeys ToLibraDexStringKeys(TextIndexProfile profile)
        {
            const TextIndexProfile supported = TextIndexProfile.Folded | TextIndexProfile.SortKey | TextIndexProfile.Normalized;
            if ((profile & ~supported) != 0)
                throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown text index profile.");

            StringKeys keys = StringKeys.Exact;
            if ((profile & TextIndexProfile.Folded) != 0)
                keys |= StringKeys.Folded;
            if ((profile & TextIndexProfile.SortKey) != 0)
                keys |= StringKeys.SortKey;
            if ((profile & TextIndexProfile.Normalized) != 0)
                keys |= StringKeys.Normalized;
            return keys;
        }

        /// <summary>
        /// Chooses key projections independently of the physical index sort order.<br/>
        /// Reversed keys accelerate suffix conditions; they do not represent descending traversal.<br/>
        /// </summary>
        /// <param name="direction">Abraxas index option, including the legacy bidirectional projection request.<br/></param>
        /// <returns>Forward keys for ordinary indexes, or both projections when explicitly requested.<br/></returns>
        private static LibraDexProjectionDirectionSet ToLibraDexProjectionDirections(IndexDirection direction)
        {
            return direction switch
            {
                IndexDirection.Ascending => LibraDexProjectionDirectionSet.Forward,
                IndexDirection.Descending => LibraDexProjectionDirectionSet.Forward,
                IndexDirection.Bidirectional => LibraDexProjectionDirectionSet.ForwardAndReversed,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown index direction.")
            };
        }

        private volatile CustomBinaryCodec[] customBinaryCodecs = Array.Empty<CustomBinaryCodec>();

        private readonly struct LibraDexSimpleIndex
        {
            internal LibraDexSimpleIndex(string path, Type keyType, bool unique, IndexBackendCapabilities capabilities, IIndex index, Catalog? ownerCatalog, bool autoCreated, AutoIndexUse autoUse, DateTime lastUsedUtc, CustomBinaryKeyReader? customKeyReader = null, CustomBinaryPayloadReader? customPayloadReader = null, int? maximumKeyBytes = null, SelectorDescriptor<T>? calculatedSelector = null, Func<InhetoBinary, (bool HasValue, object? Value)>? calculatedReader = null, Func<T, (bool HasValue, object? Value)>? functionRuntimeReader = null, bool includeNullValues = true, bool includeEmptyStrings = true, int? fixedBinaryKeyBytes = null, bool isFunctionIndex = false, bool isVirtualProperty = false)
            {
                Path = path;
                KeyType = keyType;
                Unique = unique;
                Capabilities = capabilities;
                Index = index;
                OwnerCatalog = ownerCatalog;
                AutoCreated = autoCreated;
                AutoUse = autoUse;
                LastUsedUtc = lastUsedUtc;
                CustomKeyReader = customKeyReader;
                CustomPayloadReader = customPayloadReader;
                MaximumKeyBytes = maximumKeyBytes;
                CalculatedSelector = calculatedSelector;
                CalculatedFingerprint = calculatedSelector?.SemanticFingerprint;
                CalculatedReader = calculatedReader;
                FunctionRuntimeReader = functionRuntimeReader;
                IncludeNullValues = includeNullValues;
                IncludeEmptyStrings = includeEmptyStrings;
                FixedBinaryKeyBytes = fixedBinaryKeyBytes;
                IsFunctionIndex = isFunctionIndex;
                IsVirtualProperty = isVirtualProperty;
            }

            internal string Path { get; }
            internal Type KeyType { get; }
            internal bool Unique { get; }
            internal IndexBackendCapabilities Capabilities { get; }
            internal IIndex Index { get; }
            internal Catalog? OwnerCatalog { get; }
            internal bool AutoCreated { get; }
            internal AutoIndexUse AutoUse { get; }
            internal DateTime LastUsedUtc { get; }
            internal CustomBinaryKeyReader? CustomKeyReader { get; }
            internal CustomBinaryPayloadReader? CustomPayloadReader { get; }
            internal int? MaximumKeyBytes { get; }
            internal int? FixedBinaryKeyBytes { get; }
            internal SelectorDescriptor<T>? CalculatedSelector { get; }
            internal string? CalculatedFingerprint { get; }
            internal Func<InhetoBinary, (bool HasValue, object? Value)>? CalculatedReader { get; }
            internal Func<T, (bool HasValue, object? Value)>? FunctionRuntimeReader { get; }
            internal bool IsFunctionIndex { get; }
            internal bool IsVirtualProperty { get; }
            internal bool IncludeNullValues { get; }
            internal bool IncludeEmptyStrings { get; }

            internal LibraDexSimpleIndex WithLastUsedUtc(DateTime value)
            {
                return new LibraDexSimpleIndex(Path, KeyType, Unique, Capabilities, Index, OwnerCatalog, AutoCreated, AutoUse, value, CustomKeyReader, CustomPayloadReader, MaximumKeyBytes, CalculatedSelector, CalculatedReader, FunctionRuntimeReader, IncludeNullValues, IncludeEmptyStrings, FixedBinaryKeyBytes, IsFunctionIndex, IsVirtualProperty);
            }

            /// <summary>
            /// Replaces only the physical index handle while preserving the complete Abraxas runtime descriptor.<br/>
            /// </summary>
            /// <param name="index">The exact reopened physical handle.<br/></param>
            /// <returns>A descriptor identical to this value except for its handle.<br/></returns>
            internal LibraDexSimpleIndex WithIndex(IIndex index)
            {
                ArgumentNullException.ThrowIfNull(index);
                return new LibraDexSimpleIndex(Path, KeyType, Unique, Capabilities, index, OwnerCatalog, AutoCreated, AutoUse, LastUsedUtc, CustomKeyReader, CustomPayloadReader, MaximumKeyBytes, CalculatedSelector, CalculatedReader, FunctionRuntimeReader, IncludeNullValues, IncludeEmptyStrings, FixedBinaryKeyBytes, IsFunctionIndex, IsVirtualProperty);
            }
        }

        /// <summary>
        /// Reattaches supported persisted simple Abraxas indexes to their existing LibraDex handles when a store is opened or rebound.<br/>
        /// Partial indexes remain excluded because their membership predicate requires separate semantic maintenance, and text indexes are reattached only when an existing exact-text LibraDex shape proves the intended projection contract.<br/>
        /// A missing LibraDex shape is created and backfilled once; an existing shape is opened without an unconditional full Fractal walk.<br/>
        /// </summary>
        /// <param name="indexes">The persistent UInt64 identity group bound to this store.<br/></param>
        private void RegisterExistingLibraDexSimplePathIndexes(CatalogIdentityGroupIndexes indexes)
        {
            RefreshUnavailableFunctionIndexProjection();
            var ordinaryIntents = rbase.GetOrdinaryIndexIntentEntries(storeID);
            var intendedPhysicalNames = new HashSet<string>(
                ordinaryIntents.Select(static entry => entry.PhysicalName),
                StringComparer.Ordinal);
            for (var i = 0; i < ordinaryIntents.Length; i++)
            {
                var intent = ordinaryIntents[i];
                if (rbase.IsOrdinaryIndexRemoved(storeID, intent.LogicalName))
                    continue;

                try
                {
                    intent = NormalizeLegacyExactStringIntent(intent, indexes);
                    int requiredPopulationRevision = GetRequiredPopulationRevision(intent);
                    if (intent.Ready && intent.PopulationRevision < requiredPopulationRevision)
                    {
                        intent = intent.Clone();
                        intent.Ready = false;
                        rbase.UpsertOrdinaryIndexIntent(intent);
                    }

                    _ = TryRestoreOrdinaryIndexIntent(intent, indexes);
                }
                catch (IndexContractException exception)
                    when (exception.FailureKind == IndexContractFailureKind.CodecIdentity)
                {
                    throw;
                }
                catch (IndexContractException)
                    when (intent.Kind == OrdinaryIndexIntentKind.FunctionCalculated)
                {
                    throw;
                }
                catch
                {
                    // Keep the authoritative Fractal/Inheto store available when one intended companion cannot yet be recovered.
                }
            }

            CatalogIndexInfo[] catalogIndexes;
            try
            {
                catalogIndexes = indexes.List();
            }
            catch
            {
                return;
            }

            for (var i = 0; i < catalogIndexes.Length; i++)
            {
                var info = catalogIndexes[i];
                if (intendedPhysicalNames.Contains(info.Name))
                    continue;
                if (rbase.IsOrdinaryIndexRemoved(storeID, info.Name))
                    continue;
                if (info.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                    !TryResolveSimplePropertyType(info.Name, out var keyType))
                {
                    continue;
                }

                keyType = NormalizeValueType(keyType);
                if (keyType == typeof(byte[]))
                    continue;

                try
                {
                    if (keyType == typeof(string))
                    {
                        const StringKeys supportedStringKeys = StringKeys.Exact | StringKeys.Folded | StringKeys.SortKey | StringKeys.Normalized;
                        if ((info.StringKeys & StringKeys.Exact) == 0 || (info.StringKeys & ~supportedStringKeys) != 0)
                            throw new NotSupportedException($"Persisted string-key profile '{info.StringKeys}' is not supported by Abraxas.");

                        var profile = TextIndexProfile.Exact;
                        if ((info.StringKeys & StringKeys.Folded) != 0)
                            profile |= TextIndexProfile.Folded;
                        if ((info.StringKeys & StringKeys.SortKey) != 0)
                            profile |= TextIndexProfile.SortKey;
                        if ((info.StringKeys & StringKeys.Normalized) != 0)
                            profile |= TextIndexProfile.Normalized;
                        var direction = info.Directions == LibraDexProjectionDirectionSet.ForwardAndReversed
                            ? IndexDirection.Bidirectional
                            : info.SortOrder == LibraDexIndexSortOrder.Descending
                                ? IndexDirection.Descending
                                : IndexDirection.Ascending;
                        TryRegisterLibraDexTextPathIndex(
                            info.Name,
                            profile,
                            direction,
                            info.KeyContract == IndexKeys.Unique,
                            indexes,
                            ownerCatalog: null,
                            populateExisting: false,
                            manageIntent: false,
                            sortKeyProfiles: ToAbraxasSortKeyProfiles(info.SortKeyProfiles));
                    }
                    else
                    {
                        TryRegisterLibraDexSimplePathIndex(
                            info.Name,
                            info.KeyContract == IndexKeys.Unique,
                            indexes,
                            ownerCatalog: null,
                            populateExisting: false,
                            autoCreated: false,
                            AutoIndexUse.None,
                            manageIntent: false,
                            direction: info.SortOrder == LibraDexIndexSortOrder.Descending
                                ? IndexDirection.Descending
                                : IndexDirection.Ascending);
                    }
                }
                catch (IndexContractException exception)
                    when (exception.FailureKind == IndexContractFailureKind.CodecIdentity)
                {
                    throw;
                }
                catch
                {
                    // Keep store opening available through the authoritative Fractal/Inheto path when one persisted companion shape cannot be reattached.
                }
            }

            for (var i = 0; i < catalogIndexes.Length; i++)
            {
                var info = catalogIndexes[i];
                if (intendedPhysicalNames.Contains(info.Name))
                    continue;
                if (rbase.IsOrdinaryIndexRemoved(storeID, info.Name))
                    continue;
                if (info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                    info.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                    info.Projections.Count == 0 ||
                    info.Projections[0].Kind != LibraDexIndexProjectionKind.VariableBlobExact ||
                    info.VarKeyMaxKeyLength < 2 ||
                    !TryResolveSimplePropertyType(info.Name, out var keyType) ||
                    NormalizeValueType(keyType) != typeof(byte[]))
                {
                    continue;
                }

                try
                {
                    TryRegisterLibraDexBinaryPathIndex(
                        info.Name,
                        info.VarKeyMaxKeyLength - 1,
                        info.KeyContract == IndexKeys.Unique,
                        indexes,
                        populateExisting: false,
                        manageIntent: false);
                }
                catch
                {
                    // Keep store opening available through the authoritative Fractal/Inheto path when one variable-blob companion cannot be attached.
                }
            }
        }

        /// <summary>
        /// Canonicalizes only legacy direct Exact string declarations whose existing physical shape proves equivalence to the current forward text contract.<br/>
        /// Historical Scalar Bidirectional declarations remain unchanged because they created Forward storage, not reversed projections.<br/>
        /// Older population evidence is retained, forcing authoritative recovery before the current revision can be published; no profile or physical shape is widened here.<br/>
        /// </summary>
        /// <param name="intent">Detached legacy or current durable definition.<br/></param>
        /// <param name="indexes">Persistent identity group used to verify existing physical shape.<br/></param>
        /// <returns>The original definition, or a persisted canonical Text copy requiring recovery when its revision is obsolete.<br/></returns>
        private OrdinaryIndexIntentEntry NormalizeLegacyExactStringIntent(
            OrdinaryIndexIntentEntry intent,
            CatalogIdentityGroupIndexes indexes)
        {
            if (intent.Kind != OrdinaryIndexIntentKind.Scalar ||
                intent.TextProfile != TextIndexProfile.Exact ||
                intent.Direction == IndexDirection.Bidirectional ||
                !string.Equals(intent.LogicalName, intent.PhysicalName, StringComparison.Ordinal) ||
                !TryResolveSimplePropertyType(intent.LogicalName, out var keyType) ||
                NormalizeValueType(keyType) != typeof(string))
                return intent;

            var sortOrder = intent.Direction == IndexDirection.Descending
                ? LibraDexIndexSortOrder.Descending
                : LibraDexIndexSortOrder.Ascending;
            if (indexes.HasIndex(intent.PhysicalName) &&
                (!indexes.TryGetInfo(intent.PhysicalName, out var physical) ||
                 physical.StringKeys != StringKeys.Exact ||
                 physical.Directions != LibraDexProjectionDirectionSet.Forward ||
                 physical.SortOrder != sortOrder || (physical.SortKeyProfiles?.Count ?? 0) != 0))
                return intent;

            var canonical = intent.Clone();
            canonical.Kind = OrdinaryIndexIntentKind.Text;
            canonical.Validate();
            if (canonical.PopulationRevision < CurrentTextPopulationRevision)
                canonical.Ready = false;
            rbase.UpsertOrdinaryIndexIntent(canonical);
            return canonical;
        }

        /// <summary>
        /// Recreates or reattaches one persisted Abraxas ordinary-index definition without replacing its developer-owned contract from physical metadata.<br/>
        /// A preparing definition is rebuilt from authoritative Fractal/Inheto records before its ready state is persisted.<br/>
        /// </summary>
        /// <param name="intent">The detached preparing or ready ordinary-index definition.<br/></param>
        /// <param name="indexes">The persistent LibraDex identity group that owns physical reality.<br/></param>
        /// <returns><c>true</c> when the definition was attached and, when necessary, recovered; otherwise <c>false</c>.<br/></returns>
        private bool TryRestoreOrdinaryIndexIntent(
            OrdinaryIndexIntentEntry intent,
            CatalogIdentityGroupIndexes indexes)
        {
            try
            {
                return TryRestoreOrdinaryIndexIntentCore(intent, indexes);
            }
            catch
            {
                _ = TryUnregisterLibraDexIndex(
                    new RecordIndex(intent.LogicalName, intent.LogicalName, intent.Unique, this),
                    clearPhysical: false,
                    constrainOwner: true,
                    requiredOwnerCatalog: null);
                throw;
            }
        }

        /// <summary>
        /// Performs ordinary-index intent attachment and interrupted-population recovery while the public recovery wrapper owns failure detachment.<br/>
        /// </summary>
        /// <param name="intent">The detached preparing or ready ordinary-index definition.<br/></param>
        /// <param name="indexes">The persistent LibraDex identity group that owns physical reality.<br/></param>
        /// <returns><c>true</c> when the definition was attached and recovered as required; otherwise <c>false</c>.<br/></returns>
        private bool TryRestoreOrdinaryIndexIntentCore(
            OrdinaryIndexIntentEntry intent,
            CatalogIdentityGroupIndexes indexes)
        {
            ArgumentNullException.ThrowIfNull(intent);
            ArgumentNullException.ThrowIfNull(indexes);
            intent.Validate();

            var registered = intent.Kind switch
            {
                OrdinaryIndexIntentKind.Scalar => TryRegisterLibraDexSimplePathIndex(
                    intent.LogicalName,
                    intent.Unique,
                    indexes,
                    ownerCatalog: null,
                    populateExisting: false,
                    autoCreated: false,
                    AutoIndexUse.None,
                    manageIntent: false,
                    recoverPreparingIntent: !intent.Ready,
                    direction: intent.Direction),
                OrdinaryIndexIntentKind.Text => TryRegisterLibraDexTextPathIndex(
                    intent.LogicalName,
                    intent.TextProfile,
                    intent.Direction,
                    intent.Unique,
                    indexes,
                    ownerCatalog: null,
                    populateExisting: false,
                    manageIntent: false,
                    sortKeyProfiles: intent.TextSortProfiles),
                OrdinaryIndexIntentKind.Binary => TryRegisterLibraDexBinaryPathIndex(
                    intent.LogicalName,
                    intent.MaximumKeyBytes,
                    intent.Unique,
                    indexes,
                    populateExisting: false,
                    manageIntent: false),
                OrdinaryIndexIntentKind.Calculated => RestoreCalculatedIndexIntent(intent, indexes),
                OrdinaryIndexIntentKind.FunctionCalculated => RestoreFunctionCalculatedIndexIntent(intent, indexes),
                OrdinaryIndexIntentKind.Composite => RestoreCompositeIndexIntent(intent),
                _ => false
            };
            if (!registered)
                return false;
            if (intent.Ready)
                return true;

            if (intent.Kind == OrdinaryIndexIntentKind.Composite)
            {
                LibraDexCompositeIndex? composite = null;
                var composites = libraDexCompositeIndexes;
                for (var i = 0; i < composites.Length; i++)
                {
                    if (string.Equals(composites[i].LogicalName, intent.LogicalName, StringComparison.Ordinal))
                    {
                        composite = composites[i];
                        break;
                    }
                }
                if (composite is null)
                    return false;

                var preparedComposite = PrepareLibraDexCompositeIndexPopulation(composite);
                RebuildPreparedLibraDexIndex(
                    composite.Index,
                    ownerCatalog: null,
                    () => ApplyLibraDexCompositeIndexPopulation(composite, preparedComposite),
                    composite.PhysicalName,
                    composite.LogicalName);
                intent.PopulationRevision = GetRequiredPopulationRevision(intent);
                intent.Ready = true;
                rbase.UpsertOrdinaryIndexIntent(intent);
                return true;
            }

            var entries = libraDexSimpleIndexes;
            LibraDexSimpleIndex? runtime = null;
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].OwnerCatalog is null &&
                    string.Equals(entries[i].Path, intent.LogicalName, StringComparison.Ordinal))
                {
                    runtime = entries[i];
                    break;
                }
            }
            if (runtime is null)
                return false;

            var restoredRuntime = runtime.Value;
            var prepared = PrepareLibraDexSimpleIndexPopulation(restoredRuntime);
            RebuildPreparedLibraDexSimpleIndex(restoredRuntime, prepared);
            CompleteRecoveredOrdinaryApplicationCodecBinding(intent);
            intent.PopulationRevision = GetRequiredPopulationRevision(intent);
            intent.Ready = true;
            rbase.UpsertOrdinaryIndexIntent(intent);
            return true;
        }

        /// <summary>
        /// Rehydrates one durable calculated selector and attaches its exact persisted physical and participation contract.<br/>
        /// The independently stored result type must agree with the selector envelope before any LibraDex handle is opened.<br/>
        /// </summary>
        /// <param name="intent">Validated calculated ordinary-index intent.<br/></param>
        /// <param name="indexes">Persistent store identity group that owns the physical definition.<br/></param>
        /// <returns><see langword="true"/> when the calculated runtime descriptor was attached.<br/></returns>
        private bool RestoreCalculatedIndexIntent(
            OrdinaryIndexIntentEntry intent,
            CatalogIdentityGroupIndexes indexes)
        {
            SelectorDescriptor<T> selector = SelectorDescriptor<T>.Deserialize(intent.SelectorDescriptor);
            Type resultType = NormalizeValueType(selector.ResultType);
            if (!string.Equals(intent.ResultTypeCode, ConditionWireTypes.GetCode(resultType), StringComparison.Ordinal))
                throw new InvalidDataException($"Calculated index '{intent.LogicalName}' result type contradicts its selector descriptor.");
            return TryRegisterLibraDexCalculatedIndex(
                intent.LogicalName,
                intent.PhysicalName,
                selector,
                intent.TextProfile,
                intent.Direction,
                intent.Unique,
                intent.IncludeNullValues,
                intent.IncludeEmptyStrings,
                intent.CalculatedBinaryStorage,
                intent.MaximumKeyBytes,
                indexes,
                ownerCatalog: null,
                populateExisting: false,
                manageIntent: false,
                isVirtualProperty: IsVirtualPropertyPhysicalName(intent.PhysicalName),
                virtualValuesAreAuthoritative: VirtualPropertyUsesAuthoritativeIndexValues(intent.PhysicalName));
        }

        /// <summary>
        /// Reattaches one durable function-calculated index only after its exact function projection ID, version, and result type have been registered for this store instance.<br/>
        /// Executable code remains process-local; persisted intent carries only the stable identity and physical participation contract required to fail closed on mismatched application setup.<br/>
        /// </summary>
        /// <param name="intent">Validated function-calculated ordinary-index intent.<br/></param>
        /// <param name="indexes">Persistent store identity group that owns the physical definition.<br/></param>
        /// <returns><see langword="true"/> when matching application code was registered and the runtime descriptor attached; otherwise <see langword="false"/>.<br/></returns>
        private bool RestoreFunctionCalculatedIndexIntent(
            OrdinaryIndexIntentEntry intent,
            CatalogIdentityGroupIndexes indexes)
        {
            Type resultType = NormalizeValueType(ConditionWireTypes.Resolve(intent.ResultTypeCode));
            FunctionIndexProjection[] projections = functionIndexProjections;
            for (int i = 0; i < projections.Length; i++)
            {
                FunctionIndexProjection projection = projections[i];
                if (!string.Equals(projection.FunctionId, intent.FunctionId, StringComparison.Ordinal))
                    continue;
                if (projection.FunctionVersion != intent.FunctionVersion || projection.ResultType != resultType)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.Configuration,
                        $"FunctionIndex '{intent.FunctionId}' was persisted as version {intent.FunctionVersion} returning '{resultType.FullName}', but runtime setup registered version {projection.FunctionVersion} returning '{projection.ResultType.FullName}'.",
                        intent.LogicalName,
                        intent.FunctionId);
                }
                return TryRegisterLibraDexCalculatedIndex(
                    intent.LogicalName,
                    intent.PhysicalName,
                    selector: null!,
                    intent.TextProfile,
                    intent.Direction,
                    intent.Unique,
                    intent.IncludeNullValues,
                    intent.IncludeEmptyStrings,
                    intent.CalculatedBinaryStorage,
                    intent.MaximumKeyBytes,
                    indexes,
                    ownerCatalog: null,
                    populateExisting: false,
                    manageIntent: false,
                    functionResultType: resultType,
                    functionBinaryReader: projection.BinaryReader,
                    functionRuntimeReader: projection.RuntimeReader,
                    functionId: projection.FunctionId,
                    functionVersion: projection.FunctionVersion);
            }
            return false;
        }

        /// <summary>
        /// Publishes a matching preparing custom-codec binding only after its ordinary index has been rebuilt from authoritative data.<br/>
        /// Built-in scalar, text, and binary definitions have no application-codec binding and return without catalog mutation.<br/>
        /// </summary>
        /// <param name="intent">The ordinary definition whose physical tuple set just completed recovery.<br/></param>
        private void CompleteRecoveredOrdinaryApplicationCodecBinding(OrdinaryIndexIntentEntry intent)
        {
            if (intent.Kind != OrdinaryIndexIntentKind.Scalar ||
                !TryResolveSimplePropertyType(intent.LogicalName, out var keyType))
            {
                return;
            }

            keyType = NormalizeValueType(keyType);
            if (!TryGetCustomBinaryCodec(keyType, intent.LogicalName, out var codec) ||
                !rbase.TryGetApplicationCodecBinding(
                    storeID,
                    ApplicationCodecIndexKind.Ordinary,
                    intent.LogicalName,
                    out var binding) ||
                binding.Ready)
            {
                return;
            }

            var expected = CreateApplicationCodecBinding(
                codec,
                ApplicationCodecIndexKind.Ordinary,
                intent.LogicalName,
                intent.PhysicalName,
                intent.LogicalName,
                keyType,
                ready: true);
            if (!binding.ContractEquals(expected, includeReady: false))
            {
                throw new IndexContractException(
                    IndexContractFailureKind.CodecIdentity,
                    $"Persistent index '{intent.LogicalName}' has an interrupted application-codec binding that conflicts with runtime recovery.",
                    intent.LogicalName,
                    intent.LogicalName);
            }

            rbase.UpsertApplicationCodecBinding(expected);
        }

        /// <summary>
        /// Attempts to read one fixed-width custom binary key directly from an authoritative Inheto payload.<br/>
        /// Returning <c>false</c> represents a missing or explicit-null property; an empty present payload remains a successful read and is validated against the configured fixed width.<br/>
        /// </summary>
        /// <param name="binary">The authoritative serialized record payload.<br/></param>
        /// <param name="keyPath">The exact indexed property path.<br/></param>
        /// <param name="key">Receives the caller-owned fixed-width byte array required by the LibraDex mutation surface.<br/></param>
        /// <returns><c>true</c> when a custom binary payload was present; otherwise <c>false</c>.<br/></returns>
        private delegate bool CustomBinaryKeyReader(InhetoBinary binary, string keyPath, out object key);

        /// <summary>
        /// Attempts to borrow one fixed-width custom binary payload directly from an authoritative Inheto record.<br/>
        /// The returned memory remains owned by the supplied <paramref name="binary"/> and must not be retained beyond the synchronous operation that requested it.<br/>
        /// </summary>
        /// <param name="binary">The authoritative serialized record payload.<br/></param>
        /// <param name="keyPath">The exact indexed property path.<br/></param>
        /// <param name="payload">Receives borrowed fixed-width payload memory when present.<br/></param>
        /// <returns><see langword="true"/> when a custom payload is present; otherwise <see langword="false"/>.<br/></returns>
        private delegate bool CustomBinaryPayloadReader(
            InhetoBinary binary,
            string keyPath,
            out ReadOnlyMemory<byte> payload);

        /// <summary>
        /// Stores the immutable setup-time delegates and fixed-width shape for one application-defined binary index codec.<br/>
        /// </summary>
        private readonly struct CustomBinaryCodec
        {
            /// <summary>
            /// Creates one custom binary codec descriptor.<br/>
            /// </summary>
            internal CustomBinaryCodec(
                Type valueType,
                string? registrationPath,
                int fixedKeyWidth,
                bool orderPreserving,
                string? formatId,
                int formatVersion,
                Func<object, string, byte[]> encode,
                CustomBinaryKeyReader readKey,
                CustomBinaryPayloadReader readPayload)
            {
                ValueType = valueType;
                RegistrationPath = registrationPath;
                FixedKeyWidth = fixedKeyWidth;
                OrderPreserving = orderPreserving;
                FormatId = formatId;
                FormatVersion = formatVersion;
                Encode = encode;
                ReadKey = readKey;
                ReadPayload = readPayload;
            }

            internal Type ValueType { get; }
            internal string? RegistrationPath { get; }
            internal int FixedKeyWidth { get; }
            internal bool OrderPreserving { get; }
            internal string? FormatId { get; }
            internal int FormatVersion { get; }
            internal bool IsPersistent => FormatId is not null;
            internal Func<object, string, byte[]> Encode { get; }
            internal CustomBinaryKeyReader ReadKey { get; }
            internal CustomBinaryPayloadReader ReadPayload { get; }
            internal LibraDexScalarWidth Width => FixedKeyWidth switch
            {
                8 => LibraDexScalarWidth.Bytes8,
                16 => LibraDexScalarWidth.Bytes16,
                32 => LibraDexScalarWidth.Bytes32,
                _ => throw new InvalidOperationException($"Unsupported registered custom key width '{FixedKeyWidth}'.")
            };

            /// <summary>
            /// Compares the complete stable format and ordered-shape contract while deliberately ignoring refreshable executable delegates.<br/>
            /// </summary>
            internal bool HasSamePersistentContract(CustomBinaryCodec other)
                => ValueType == other.ValueType &&
                   string.Equals(RegistrationPath, other.RegistrationPath, StringComparison.Ordinal) &&
                   FixedKeyWidth == other.FixedKeyWidth &&
                   OrderPreserving == other.OrderPreserving &&
                   string.Equals(FormatId, other.FormatId, StringComparison.Ordinal) &&
                   FormatVersion == other.FormatVersion;
        }
        private readonly struct LibraDexRegexProjectionIndex
        {
            internal LibraDexRegexProjectionIndex(string logicalPrefix, string sourcePath, Regex regex, MatchValueIndexOptions? settings, bool unique, IndexBackendCapabilities capabilities, IIndex index, Catalog? ownerCatalog)
            {
                LogicalPrefix = logicalPrefix;
                SourcePath = sourcePath;
                Regex = regex;
                Settings = settings;
                Unique = unique;
                Capabilities = capabilities;
                Index = index;
                OwnerCatalog = ownerCatalog;
            }

            internal string LogicalPrefix { get; }
            internal string SourcePath { get; }
            internal Regex Regex { get; }
            internal MatchValueIndexOptions? Settings { get; }
            internal bool Unique { get; }
            internal IndexBackendCapabilities Capabilities { get; }
            internal IIndex Index { get; }
            internal Catalog? OwnerCatalog { get; }

            /// <summary>
            /// Replaces only the physical index handle while preserving regex, projection, capability, and ownership intent.<br/>
            /// </summary>
            /// <param name="index">The exact reopened physical handle.<br/></param>
            /// <returns>A descriptor identical to this value except for its handle.<br/></returns>
            internal LibraDexRegexProjectionIndex WithIndex(IIndex index)
            {
                ArgumentNullException.ThrowIfNull(index);
                return new LibraDexRegexProjectionIndex(LogicalPrefix, SourcePath, Regex, Settings, Unique, Capabilities, index, OwnerCatalog);
            }
        }

        /// <summary>
        /// Creates or opens and registers one bounded variable-length byte-array index in the currently bound persistent LibraDex identity group.<br/>
        /// Existing records are walked only when requested or when the catalog shape is newly created; reopen attaches the existing handle without unconditional backfill.<br/>
        /// </summary>
        /// <param name="propPath">The direct byte-array property path and logical LibraDex index name.<br/></param>
        /// <param name="maxKeyBytes">The maximum developer-facing byte-array payload length.<br/></param>
        /// <param name="unique">Whether different identities may share one byte-array key.<br/></param>
        /// <returns><c>true</c> when the index was registered; otherwise <c>false</c>.</returns>
        private bool TryRegisterLibraDexBinaryPathIndex(string propPath, int maxKeyBytes, bool unique)
        {
            var indexes = libraDexIdentityIndexes;
            if (indexes is null || string.IsNullOrWhiteSpace(propPath))
                return false;

            return TryRegisterLibraDexBinaryPathIndex(propPath, maxKeyBytes, unique, indexes, populateExisting: true);
        }

        /// <summary>
        /// Registers one bounded variable-length byte-array property path against an explicit LibraDex identity group.<br/>
        /// Setup-time validation proves the CLR property type and persisted maximum before the descriptor is published to lock-free write/query snapshots.<br/>
        /// </summary>
        /// <param name="propPath">The direct byte-array property path.<br/></param>
        /// <param name="maxKeyBytes">The maximum developer-facing byte-array payload length.<br/></param>
        /// <param name="unique">Whether different identities may share one byte-array key.<br/></param>
        /// <param name="indexes">The persistent LibraDex identity group.<br/></param>
        /// <param name="populateExisting">Whether existing Fractal/Inheto records should be mirrored immediately.<br/></param>
        /// <param name="manageIntent">Whether this call owns preparing and completing the persistent index intent; recovery callers can manage it separately.<br/></param>
        /// <param name="ownerCatalog">The volatile catalog that owns the index, or null for persistent store-owned indexes.<br/></param>
        /// <returns><c>true</c> when the index was registered; otherwise <c>false</c>.</returns>
        private bool TryRegisterLibraDexBinaryPathIndex(
            string propPath,
            int maxKeyBytes,
            bool unique,
            CatalogIdentityGroupIndexes indexes,
            bool populateExisting,
            bool manageIntent = true,
            Catalog? ownerCatalog = null)
        {
            if (maxKeyBytes < 1 ||
                !TryResolveSimplePropertyType(propPath, out var keyType, out Inheto.PropPathCheck pathCheck) ||
                NormalizeValueType(keyType) != typeof(byte[]))
            {
                return false;
            }
            propPath = pathCheck.CanonicalPath;

            ThrowIfLibraDexSimpleIndexBackingConflicts(propPath, ownerCatalog);

            var ordinaryIntent = ownerCatalog is null && manageIntent
                ? PrepareOrdinaryIndexIntent(
                    OrdinaryIndexIntentKind.Binary,
                    propPath,
                    propPath,
                    unique,
                    maximumKeyBytes: maxKeyBytes)
                : null;
            // Abraxas permits multiple null binary keys, so its non-null uniqueness contract is enforced
            // by the mutation preflight while LibraDex retains every null identity on its key-state route.
            var keyContract = IndexKeys.NonUnique;
            var indexExisted = indexes.HasIndex(propPath);
            using var indexMutation = EnterPartialIndexMutationScope();
            IIndex index;
            try
            {
                index = indexes.Define(propPath).Blob.Variable<ulong>(maxKeyBytes).CreateOrOpen(keyContract);
            }
            catch (Exception exception)
            {
                throw new IndexContractException(
                    indexExisted
                        ? IndexContractFailureKind.Configuration
                        : IndexContractFailureKind.BackendMutation,
                    indexExisted
                        ? $"Binary index '{propPath}' conflicts with its persisted payload bound or uniqueness definition."
                        : $"Binary index '{propPath}' could not create its ordered-backend definition.",
                    propPath,
                    propPath,
                    innerException: exception);
            }
            if (ownerCatalog is null)
                EnsureLibraDexStateIndex(indexes);

            var capabilities = IndexBackendCapabilities.Equality | IndexBackendCapabilities.Range;
            if (unique)
                capabilities |= IndexBackendCapabilities.Unique;
            var entry = new LibraDexSimpleIndex(
                propPath,
                typeof(byte[]),
                unique,
                capabilities,
                index,
                ownerCatalog,
                autoCreated: false,
                AutoIndexUse.None,
                DateTime.UtcNow,
                maximumKeyBytes: maxKeyBytes,
                calculatedReader: CreateClrContinuationReader(propPath, typeof(byte[])));

            lock (libraDexIndexLock)
            {
                var current = libraDexSimpleIndexes;
                for (var i = 0; i < current.Length; i++)
                {
                    if (!string.Equals(current[i].Path, propPath, StringComparison.Ordinal))
                        continue;

                    var next = (LibraDexSimpleIndex[])current.Clone();
                    next[i] = entry;
                    libraDexSimpleIndexes = next;
                    if (populateExisting || !indexExisted)
                    PopulateLibraDexSimpleIndex(entry, physicalIndexIsNew: !indexExisted);
                    CompleteOrdinaryIndexRegistration(ownerCatalog, ordinaryIntent);
                    return true;
                }

                var appended = new LibraDexSimpleIndex[current.Length + 1];
                Array.Copy(current, appended, current.Length);
                appended[current.Length] = entry;
                libraDexSimpleIndexes = appended;
            }

            if (populateExisting || !indexExisted)
                PopulateLibraDexSimpleIndex(entry, physicalIndexIsNew: !indexExisted);
            CompleteOrdinaryIndexRegistration(ownerCatalog, ordinaryIntent);
            return true;
        }

        /// <summary>
        /// Executes one runtime property-path aggregate over records selected by the authoritative LibraDex State index.<br/>
        /// A usable ordered aggregate index stays entirely in LibraDex; otherwise only State-selected authoritative Fractal/Inheto payloads are read as the correctness fallback.<br/>
        /// </summary>
        /// <param name="propPath">The simple or nested property path whose values are aggregated.<br/></param>
        /// <param name="state">The developer-owned State value to match.<br/></param>
        /// <param name="mask">The optional bitmask applying <c>(State &amp; mask) == state</c>.<br/></param>
        /// <param name="kind">The requested minimum, maximum, or distinct-count operation.<br/></param>
        /// <returns>The boxed aggregate result; extrema are null for empty input and distinct count is boxed UInt64 zero for empty input.<br/></returns>
        private object? ExecuteAggregateByState(
            string propPath,
            long state,
            long? mask,
            StateAggregateKind kind)
        {
            using var lifecycle = EnterStoreOperationScope();
            ArgumentException.ThrowIfNullOrWhiteSpace(propPath);
            if (TryGetIndex(propPath, out RecordIndex recordIndex) &&
                TryAggregateIndexedValuesByState(recordIndex, state, mask, kind, out object? indexed))
            {
                return indexed;
            }

            return AggregateFractalValuesByState(propPath, state, mask, kind);
        }

        /// <summary>
        /// Attempts to aggregate one ordered LibraDex value index after applying an exact or masked authoritative State identity condition.<br/>
        /// Exact State against a persistent target composes two native indexes directly; masked or volatile-target requests use LibraDex's external identity-source condition.<br/>
        /// </summary>
        /// <param name="recordIndex">The aggregate property index handle.<br/></param>
        /// <param name="state">The State value to match.<br/></param>
        /// <param name="mask">The optional State bitmask.<br/></param>
        /// <param name="kind">The requested aggregate operation.<br/></param>
        /// <param name="result">The completed aggregate when this method returns <c>true</c>.<br/></param>
        /// <returns><c>true</c> when both the State index and aggregate property index are usable through LibraDex.<br/></returns>
        private bool TryAggregateIndexedValuesByState(
            RecordIndex recordIndex,
            long state,
            long? mask,
            StateAggregateKind kind,
            out object? result)
        {
            result = kind == StateAggregateKind.DistinctCount ? 0UL : null;
            CatalogIdentityGroupIndexes? stateGroup = libraDexIdentityIndexes;
            LibraDexIndex<long, ulong>? stateIndex = libraDexStateIndex;
            if (stateGroup is null ||
                stateIndex is null ||
                libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
                !TryGetLibraDexIndexGroup(recordIndex, out CatalogIdentityGroupIndexes targetGroup, out IIndex targetIndex))
            {
                return false;
            }

            LibraDexConditionEndCondition condition;
            if (!mask.HasValue && ReferenceEquals(stateGroup, targetGroup))
            {
                condition = stateGroup.Where(stateIndex).EqualTo(state).EndCondition;
            }
            else
            {
                LibraDexConditionEndCondition targetAll = CreateLibraDexAllCondition(targetGroup, targetIndex);
                LibraDexConditionEndCondition externalState = targetGroup
                    .External(() => IterateRequiredLibraDexStateIds(state, mask))
                    .EndCondition;
                condition = targetAll.AndAlso(externalState);
            }

            object? prior = null;
            bool hasPrior = false;
            ulong distinctCount = 0;
            foreach (LibraDexRuntimeTuple tuple in targetGroup.IterateTuples(targetIndex, condition, QueryDirection.Ascending))
            {
                if (tuple.Key is null)
                    continue;

                if (kind == StateAggregateKind.Minimum)
                {
                    result = tuple.Key;
                    return true;
                }

                if (kind == StateAggregateKind.Maximum)
                {
                    result = tuple.Key;
                    continue;
                }

                if (hasPrior && CompareLibraDexSortKey(prior, tuple.Key) == 0)
                    continue;

                prior = tuple.Key;
                hasPrior = true;
                distinctCount = checked(distinctCount + 1);
            }

            if (kind == StateAggregateKind.DistinctCount)
                result = distinctCount;
            return true;
        }

        /// <summary>
        /// Reopens the authoritative State identity stream each time LibraDex evaluates an external identity-source condition.<br/>
        /// This keeps cursor ownership and disposal inside the lazy enumeration and supports both exact and masked State rules.<br/>
        /// </summary>
        /// <param name="state">The State value to match.<br/></param>
        /// <param name="mask">The optional State bitmask.<br/></param>
        /// <returns>Matching UInt64 record identities.<br/></returns>
        private IEnumerable<ulong> IterateRequiredLibraDexStateIds(long state, long? mask)
        {
            if (!TryIterateRecordIDsByLibraDexState(state, mask, out IEnumerable<ulong>? identities))
                yield break;

            foreach (ulong identity in identities)
                yield return identity;
        }

        /// <summary>
        /// Aggregates State-selected authoritative Fractal/Inheto values when no usable ordered aggregate index exists.<br/>
        /// The fallback reads only matching identities and preserves null exclusion, ordinal byte/string comparison, and value-based distinct semantics.<br/>
        /// </summary>
        /// <param name="propPath">The property path read from each matching payload.<br/></param>
        /// <param name="state">The State value to match.<br/></param>
        /// <param name="mask">The optional State bitmask.<br/></param>
        /// <param name="kind">The requested aggregate operation.<br/></param>
        /// <returns>The boxed aggregate result.<br/></returns>
        private object? AggregateFractalValuesByState(
            string propPath,
            long state,
            long? mask,
            StateAggregateKind kind)
        {
            object? extreme = null;
            List<object>? distinct = kind == StateAggregateKind.DistinctCount ? new List<object>() : null;
            Type? scalarWrapperType = null;
            PropertyInfo? scalarValueProperty = null;
            foreach (PlannedConditionMatch match in IteratePlannedStateMatches(
                state,
                mask,
                nameof(ExecuteAggregateByState),
                includePayload: true))
            {
                InhetoBinary binary = match.Payload
                    ?? throw new InvalidDataException("State aggregate fallback requested a match without its retained Inheto payload.");
                object? rawValue = binary.PropExists(propPath)
                    ? binary.ReadProp(propPath, NativeDeserializationOptions)
                    : null;
                object? value;
                if (rawValue is IInhetoObject)
                {
                    Type currentWrapperType = rawValue.GetType();
                    if (currentWrapperType != scalarWrapperType)
                    {
                        scalarWrapperType = currentWrapperType;
                        scalarValueProperty = currentWrapperType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                    }

                    value = scalarValueProperty?.GetValue(rawValue);
                }
                else
                {
                    value = rawValue;
                }

                if (value is null)
                    continue;

                if (distinct is not null)
                {
                    bool exists = false;
                    for (int i = 0; i < distinct.Count; i++)
                    {
                        if (CompareLibraDexSortKey(distinct[i], value) != 0)
                            continue;

                        exists = true;
                        break;
                    }

                    if (!exists)
                        distinct.Add(value);
                    continue;
                }

                if (extreme is null ||
                    (kind == StateAggregateKind.Minimum && CompareLibraDexSortKey(value, extreme) < 0) ||
                    (kind == StateAggregateKind.Maximum && CompareLibraDexSortKey(value, extreme) > 0))
                {
                    extreme = value;
                }
            }

            return distinct is null ? extreme : checked((ulong)distinct.Count);
        }

        /// <summary>
        /// Feeds identities selected by the authoritative persistent State index through the shared planned-match payload pipeline.<br/>
        /// Exact and masked State selection remain native LibraDex operations, while payload consumers reuse the same single-read and bounded-parallel machinery as condition plans.<br/>
        /// </summary>
        /// <param name="state">The developer-owned State value to match.<br/></param>
        /// <param name="mask">The optional bitmask applying <c>(State &amp; mask) == state</c>.<br/></param>
        /// <param name="operationName">The public operation name used in validation failures.<br/></param>
        /// <param name="includePayload">Whether each match retains its authoritative Inheto payload.<br/></param>
        /// <returns>Validated State-selected identities with optional retained payloads.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IteratePlannedStateMatches(
            long state,
            long? mask,
            string operationName,
            bool includePayload)
        {
            if (!TryIterateRecordIDsByLibraDexState(state, mask, out IEnumerable<ulong>? identities))
            {
                throw new InvalidOperationException(
                    $"Operation '{operationName}' requires this store's reserved persistent LibraDex State index.");
            }

            IEnumerable<ulong> ValidateIdentities()
            {
                foreach (ulong identity in identities)
                {
                    if ((byte)(identity >> 56) != storeID)
                    {
                        throw new InvalidDataException(
                            $"The LibraDex State index returned record identity '{identity:X16}' for store '{storeID:X2}'.");
                    }

                    yield return identity;
                }
            }

            foreach (PlannedConditionMatch match in IteratePlannedIdentityMatches(
                ValidateIdentities(),
                evaluator: null,
                includePayload: includePayload))
            {
                yield return match;
            }
        }

        /// <summary>
        /// Lazily executes one completed condition through the shared direct-LibraDex, candidate-plus-residual, or full Fractal/Inheto plan.<br/>
        /// Deferred operands and clock-sensitive values are materialized once before plan selection; candidate and scan paths evaluate each authoritative payload only once.<br/>
        /// </summary>
        /// <param name="where">The completed Abraxas condition to plan and execute.<br/></param>
        /// <param name="operationName">The public operation name reported through condition lifecycle and planning diagnostics.<br/></param>
        /// <param name="includePayload">When true, each returned match retains the already-read Inheto payload for projection; direct identity-only consumers can pass false to avoid hydration.<br/></param>
        /// <param name="retainOnlyWhenEvaluated">When true, a residual-evaluation payload is retained but a complete direct-LibraDex match remains identity-only so its consumer can borrow Fractal bytes directly.<br/></param>
        /// <param name="maxDegreeOfParallelism">Exact payload-read/evaluation worker count; one preserves lazy single-thread traversal.<br/></param>
        /// <param name="channelCapacity">Maximum number of completed parallel matches buffered before consumer backpressure applies.<br/></param>
        /// <param name="cancellationToken">Token observed during planning, identity traversal, payload evaluation, and channel consumption.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind counters; null keeps ordinary condition execution free of diagnostic bookkeeping.<br/></param>
        /// <param name="preferCompletePhysicalFractalScan">Whether an identity-only full-scan plan should use the complete physical Fractal scan rather than the ordinary sequential match iterator.<br/></param>
        /// <param name="countResult">Optional callback that receives an exact native count without yielding identities when the complete condition supports count-only execution.<br/></param>
        /// <returns>Matching identities with an optional already-read Inheto payload.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IteratePlannedConditionMatches(
            Condition<T>.EndCondition where,
            string operationName,
            bool includePayload,
            bool retainOnlyWhenEvaluated = false,
            int maxDegreeOfParallelism = 1,
            int channelCapacity = 256,
            CancellationToken cancellationToken = default,
            FastFindExecutionObserver? fastFindObserver = null,
            bool preferCompletePhysicalFractalScan = false,
            Action<ulong>? countResult = null)
        {
            using var lifecycle = EnterStoreOperationScope();
            ArgumentNullException.ThrowIfNull(where);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
            cancellationToken.ThrowIfCancellationRequested();

            var builder = where.builder;
            builder.BindSemanticIndexReaderResolver(TryResolveFunctionSemanticIndexReader);
            using var parameterScope = SubscribeToConditionParameters(where, operationName);
            using var context = builder.PushParameterContext(where, this, rbase);
            OnConditionBuilding(operationName, where);
            builder.BeginParameterMaterialization();

            if (countResult is not null && TryCountNativeCondition(where, out ulong nativeCount))
            {
                OnConditionBuilt(operationName, where);
                OnConditionExecutionPlanned(operationName, where, ConditionExecutionPlanKind.IndexDirect, null,
                    "A complete maintained scalar leaf used native count-only execution without streaming identities.");
                countResult(nativeCount);
                yield break;
            }

            IEnumerable<ulong> identities;
            LibraDexIdentityPrimitivePartitionSet<ulong>? directPartitions = null;
            Func<ulong, InhetoBinary, bool>? evaluator = null;
            bool useFractalPhysicalPartitions = false;
            ConditionExecutionPlanKind selectedPlanKind;
            if (TryOpenLibraDexIdentityCursor(where, out var directCursor, out var directCondition))
            {
                selectedPlanKind = ConditionExecutionPlanKind.IndexDirect;
                if (maxDegreeOfParallelism > 1)
                {
                    fastFindObserver?.SetSemanticPlanningComplete();
                    long topologyStarted = fastFindObserver is null ? 0 : Stopwatch.GetTimestamp();
                    directCursor!.Dispose();
                    bool partitionsCreated = TryCreateLibraDexIdentityPartitions(
                        where,
                        maxDegreeOfParallelism,
                        out directPartitions,
                        out string? partitionReason);
                    if (fastFindObserver is not null)
                    {
                        fastFindObserver.SetTopologyPartitioning(
                            Stopwatch.GetTimestamp() - topologyStarted,
                            directPartitions?.TopologyRoutersRead ?? 0,
                            directPartitions?.TopologyTargetsSelected ?? 0);
                    }
                    if (!partitionsCreated)
                    {
                        throw new NotSupportedException(
                            $"{operationName} requested exactly {maxDegreeOfParallelism} workers, but the selected complete LibraDex condition does not expose {maxDegreeOfParallelism} independent physical partitions. {partitionReason ?? "No shape-specific reason was reported."} Request one worker or use a partition-capable condition; FastFind will not silently collapse the worker count.");
                    }
                }

                OnConditionBuilt(operationName, where);
                OnConditionExecutionPlanned(
                    operationName,
                    where,
                    ConditionExecutionPlanKind.IndexDirect,
                    directCondition,
                    "Every condition leaf resolved to an available maintained LibraDex index, so the complete tree executes directly.");
                identities = directPartitions is null
                    ? IterateCursor(directCursor!)
                    : Array.Empty<ulong>();
            }
            else
            {
                ThrowIfLibraDexRequired(where);
                if (!builder.TryBuildRecordSemanticEvaluator(
                        NativeDeserializationOptions,
                        out var semanticEvaluator,
                        out bool requiresState))
                {
                    throw new NotSupportedException(
                        $"{operationName} cannot execute this condition because one or more condition fragments have neither LibraDex translation nor Inheto scan semantics.");
                }

                evaluator = requiresState
                    ? (recordID, payload) => semanticEvaluator(payload, ReadOptionalStateForCondition(recordID))
                    : (_, payload) => semanticEvaluator(payload, null);

                OnConditionBuilt(operationName, where);
                if (TryOpenLibraDexCompositeCandidateCursor(where, out var compositeCursor, out var compositeCondition, out int compositeBranchCount))
                {
                    selectedPlanKind = ConditionExecutionPlanKind.IndexCandidateWithResidual;
                    OnConditionExecutionPlanned(
                        operationName,
                        where,
                        ConditionExecutionPlanKind.IndexCandidateWithResidual,
                        compositeCondition,
                        compositeBranchCount == 1
                            ? "A composite index supplied routed named-part candidates; Fractal/Inheto evaluates the complete authoritative condition."
                            : $"{compositeBranchCount} composite routed branches supplied union candidates; Fractal/Inheto evaluates the complete authoritative condition.");
                    identities = IterateCursor(compositeCursor!);
                }
                else if (TryGetLibraDexCompositeCandidateIdentities(where, out var compositeIdentities, out var compositeName))
                {
                    selectedPlanKind = ConditionExecutionPlanKind.IndexCandidateWithResidual;
                    OnConditionExecutionPlanned(
                        operationName,
                        where,
                        ConditionExecutionPlanKind.IndexCandidateWithResidual,
                        null,
                        "Composite index '", compositeName,
                        "' supplied an exact full-key candidate set; Fractal/Inheto evaluates any additional AND semantics.");
                    identities = compositeIdentities;
                }
                else if (TryOpenLibraDexAndCandidateCursor(where, out var candidateCursor, out var candidateCondition))
                {
                    selectedPlanKind = ConditionExecutionPlanKind.IndexCandidateWithResidual;
                    OnConditionExecutionPlanned(
                        operationName,
                        where,
                        ConditionExecutionPlanKind.IndexCandidateWithResidual,
                        candidateCondition,
                        "A safe top-level AND leaf supplies LibraDex candidates; Fractal/Inheto evaluates the complete residual condition.");
                    identities = IterateCursor(candidateCursor!);
                }
                else if (TryGetTimeOfDayCandidateIdentities(where, out var timeOfDayIdentities, out var timeOfDayIndex))
                {
                    selectedPlanKind = ConditionExecutionPlanKind.IndexCandidateWithResidual;
                    OnConditionExecutionPlanned(
                        operationName,
                        where,
                        ConditionExecutionPlanKind.IndexCandidateWithResidual,
                        null,
                        "Abraxas mapped an executable TimeOfDay PropPath to structured DateTime index '",
                        timeOfDayIndex,
                        "'; the index supplies conservative candidates and Fractal/Inheto evaluates the exact residual condition.");
                    identities = timeOfDayIdentities;
                }
                else
                {
                    selectedPlanKind = ConditionExecutionPlanKind.RecordScan;
                    OnConditionExecutionPlanned(
                        operationName,
                        where,
                        ConditionExecutionPlanKind.RecordScan,
                        null,
                        "The condition has no safe complete LibraDex tree or candidate-reducing top-level AND leaf, so Fractal/Inheto evaluates every authoritative record.");
                    identities = IterateRecordIDs();
                    useFractalPhysicalPartitions = maxDegreeOfParallelism > 1;
                }
            }

            bool directParallel = directPartitions is not null;
            bool parallelEvaluation = evaluator is not null && maxDegreeOfParallelism > 1;
            bool useFractalBlockProducer = useFractalPhysicalPartitions &&
                FastFindFractalStrategy == FastFindFractalParallelStrategy.PooledBlockProducer;
            FastFindExecutionRoute executionRoute = selectedPlanKind switch
            {
                ConditionExecutionPlanKind.IndexDirect => FastFindExecutionRoute.IndexDirectCursor,
                ConditionExecutionPlanKind.IndexCandidateWithResidual => FastFindExecutionRoute.IndexCandidateResidual,
                _ when useFractalBlockProducer => FastFindExecutionRoute.RecordPooledBlockProducer,
                _ when useFractalPhysicalPartitions => FastFindExecutionRoute.RecordPhysicalPartitions,
                _ => FastFindExecutionRoute.RecordNaturalSequential
            };
            fastFindObserver?.SetSemanticPlanningComplete();
            fastFindObserver?.SetPlan(
                selectedPlanKind,
                executionRoute,
                directParallel || parallelEvaluation ? FastFindResultOrder.WorkerCompletionOrder : FastFindResultOrder.SourceOrder,
                directParallel || parallelEvaluation ? maxDegreeOfParallelism : 1,
                directParallel ? directPartitions!.Partitions.Count : 0);

            if (directPartitions is not null)
            {
                foreach (PlannedConditionMatch match in IterateLibraDexConditionMatchesByPhysicalPartitions(
                    directPartitions,
                    includePayload,
                    retainOnlyWhenEvaluated,
                    channelCapacity,
                    cancellationToken,
                    fastFindObserver))
                {
                    yield return match;
                }

                yield break;
            }

            if (useFractalPhysicalPartitions && evaluator is not null)
            {
                IEnumerable<PlannedConditionMatch> matches = useFractalBlockProducer
                    ? IterateFractalConditionMatchesByPooledBlockProducer(
                        evaluator,
                        includePayload,
                        maxDegreeOfParallelism,
                        channelCapacity,
                        cancellationToken,
                        fastFindObserver)
                    : IterateFractalConditionMatchesByPhysicalPartitions(
                        evaluator,
                        includePayload,
                        maxDegreeOfParallelism,
                        channelCapacity,
                        cancellationToken,
                        fastFindObserver);
                foreach (PlannedConditionMatch match in matches)
                {
                    yield return match;
                }

                yield break;
            }

            if (selectedPlanKind == ConditionExecutionPlanKind.RecordScan && evaluator is not null)
            {
                if (preferCompletePhysicalFractalScan && !includePayload)
                {
                    foreach (ulong recordId in ListFractalConditionRecordIDsInNaturalOrder(
                        evaluator,
                        cancellationToken,
                        fastFindObserver))
                    {
                        yield return new PlannedConditionMatch(recordId, null);
                    }

                    yield break;
                }

                foreach (PlannedConditionMatch match in IterateFractalConditionMatchesSequential(
                    evaluator,
                    includePayload,
                    cancellationToken,
                    fastFindObserver))
                {
                    yield return match;
                }

                yield break;
            }

            foreach (PlannedConditionMatch match in IteratePlannedIdentityMatches(
                identities,
                evaluator,
                includePayload,
                retainOnlyWhenEvaluated,
                maxDegreeOfParallelism,
                channelCapacity,
                cancellationToken,
                fastFindObserver))
            {
                yield return match;
            }
        }

        /// <summary>
        /// Materializes one unavoidable complete Fractal condition scan through the single-threaded physical bulk reader, then restores exact natural order by retained header ordinal.<br/>
        /// The physical snapshot coalesces nearby payload reads while each match stores only its identity and natural ordinal; no source payload or hydrated record survives the scan.<br/>
        /// This complete-list path deliberately trades lazy first-row delivery and bounded match memory for substantially fewer data-file calls, while the ordinary iterator retains its streaming contract.<br/>
        /// </summary>
        /// <param name="evaluator">Complete backend-neutral semantic evaluator applied to every authoritative payload.<br/></param>
        /// <param name="cancellationToken">Token observed before each physical record and during natural-order publication.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped counters; ordinary complete-list callers pass null.<br/></param>
        /// <returns>A detached identity list in exact Fractal natural order.<br/></returns>
        private List<ulong> ListFractalConditionRecordIDsInNaturalOrder(
            Func<ulong, InhetoBinary, bool> evaluator,
            CancellationToken cancellationToken,
            FastFindExecutionObserver? fastFindObserver)
        {
            ArgumentNullException.ThrowIfNull(evaluator);

            ulong[] orderedMatches = ArrayPool<ulong>.Shared.Rent(256);
            Array.Clear(orderedMatches, 0, orderedMatches.Length);
            try
            {
                int matchCount = 0;
                int greatestNaturalOrdinal = -1;
                using var stream = new BufferStream(ReadOnlyMemory<byte>.Empty);
                InhetoBinary borrowed = InhetoBinary.FromStream(stream, NativeDeserializationOptions);
                using FractalKVS.FractalStore.GroupBulkRecordReader reader =
                    rbase.data.OpenGroupBulkRecordReader(storeID);
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ulong recordId = reader.ID;
                    fastFindObserver?.RecordSourceIdentity();
                    fastFindObserver?.RecordPayloadRead();
                    RebindRecordBinary(borrowed, recordId, reader.Data);
                    if (!evaluator(recordId, borrowed))
                        continue;

                    int naturalOrdinal = reader.NaturalOrdinal;
                    if (naturalOrdinal >= orderedMatches.Length)
                    {
                        int expandedLength = orderedMatches.Length;
                        while (expandedLength <= naturalOrdinal)
                            expandedLength = checked(expandedLength * 2);
                        ulong[] expanded = ArrayPool<ulong>.Shared.Rent(expandedLength);
                        Array.Clear(expanded, 0, expanded.Length);
                        orderedMatches.AsSpan(0, greatestNaturalOrdinal + 1).CopyTo(expanded);
                        ArrayPool<ulong>.Shared.Return(orderedMatches);
                        orderedMatches = expanded;
                    }

                    if (orderedMatches[naturalOrdinal] != 0)
                    {
                        throw new InvalidDataException(
                            $"Fractal physical scan emitted duplicate natural ordinal '{naturalOrdinal}'.");
                    }

                    orderedMatches[naturalOrdinal] = recordId;
                    greatestNaturalOrdinal = Math.Max(greatestNaturalOrdinal, naturalOrdinal);
                    matchCount++;
                }

                var identities = new List<ulong>(matchCount);
                for (int naturalOrdinal = 0; naturalOrdinal <= greatestNaturalOrdinal; naturalOrdinal++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ulong recordId = orderedMatches[naturalOrdinal];
                    if (recordId != 0)
                        identities.Add(recordId);
                }
                if (identities.Count != matchCount)
                {
                    throw new InvalidDataException(
                        "Fractal physical scan produced an inconsistent natural-order match count.");
                }

                return identities;
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(orderedMatches);
            }
        }

        /// <summary>
        /// Evaluates an unavoidable single-thread Fractal scan while each authoritative record and its payload are already present in the natural physical traversal.<br/>
        /// This avoids the former two-step shape that enumerated every identity and then issued one point lookup per identity, while preserving natural order and borrowing unmatched payload bytes without copying them.<br/>
        /// A retained Inheto payload is copied only for a matching row whose caller requested payload retention; identity-only consumers therefore perform one Fractal read and no payload-array copy per source record.<br/>
        /// </summary>
        /// <param name="evaluator">The complete semantic condition evaluator compiled for the current operation.<br/></param>
        /// <param name="includePayload">Whether matching rows retain a detached Inheto payload for the caller.<br/></param>
        /// <param name="cancellationToken">Token observed before every source record is evaluated.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind counters.<br/></param>
        /// <returns>Matching identities with a retained payload only when requested.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IterateFractalConditionMatchesSequential(
            Func<ulong, InhetoBinary, bool> evaluator,
            bool includePayload,
            CancellationToken cancellationToken,
            FastFindExecutionObserver? fastFindObserver)
        {
            ArgumentNullException.ThrowIfNull(evaluator);

            using var stream = new BufferStream(ReadOnlyMemory<byte>.Empty);
            InhetoBinary borrowed = InhetoBinary.FromStream(stream, NativeDeserializationOptions);
            foreach (var record in IterateFractalRecordsByGroupKey(storeID))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PlannedConditionMatch match = default;
                bool matched = false;
                try
                {
                    ulong recordId = record.Header.ID;
                    fastFindObserver?.RecordSourceIdentity();
                    fastFindObserver?.RecordPayloadRead();

                    RebindRecordBinary(borrowed, recordId, record.Data);
                    if (!evaluator(recordId, borrowed))
                    {
                        continue;
                    }

                    if (!includePayload)
                    {
                        match = new PlannedConditionMatch(recordId, null);
                    }
                    else
                    {
                        InhetoBinary retained = CreateRecordBinary(recordId, record.DataSpan.ToArray());
                        match = new PlannedConditionMatch(recordId, retained);
                    }

                    matched = true;
                }
                finally
                {
                    record.Dispose();
                }

                if (matched)
                {
                    yield return match;
                }
            }
        }

        /// <summary>
        /// Executes a complete LibraDex primitive through exactly the independently opened physical partitions requested by FastFind.<br/>
        /// Every worker opens and primes its own cursor on that worker thread, then waits until all sibling readers hold coherent snapshots before the coordinator releases the topology-planning transition read.<br/>
        /// Identity-only calls publish directly from LibraDex with zero Fractal reads; projected calls hydrate each selected identity once on the owning worker and retain only the requested payload.<br/>
        /// </summary>
        /// <param name="partitions">The exact disjoint LibraDex partition set, including its coordinator-owned transition read.<br/></param>
        /// <param name="includePayload">Whether each matching identity must retain its Fractal/Inheto payload for projection.<br/></param>
        /// <param name="retainOnlyWhenEvaluated">Whether payload retention is limited to rows that required semantic evaluation.<br/></param>
        /// <param name="channelCapacity">Maximum completed matches buffered before worker backpressure applies.<br/></param>
        /// <param name="cancellationToken">Token observed while readers open, traverse, hydrate, publish, and stop after consumer abandonment.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind counters.<br/></param>
        /// <returns>Completed matches in unordered worker-completion sequence.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IterateLibraDexConditionMatchesByPhysicalPartitions(
            LibraDexIdentityPrimitivePartitionSet<ulong> partitions,
            bool includePayload,
            bool retainOnlyWhenEvaluated,
            int channelCapacity,
            CancellationToken cancellationToken,
            FastFindExecutionObserver? fastFindObserver)
        {
            ArgumentNullException.ThrowIfNull(partitions);
            int workerCount = partitions.Partitions.Count;
            if (workerCount < 2)
                throw new InvalidOperationException("An exact-worker LibraDex partition set must contain at least two physical partitions.");

            int capacity = Math.Max(1, channelCapacity);
            long workerPreparationStarted = fastFindObserver is null ? 0 : Stopwatch.GetTimestamp();
            using (partitions)
            using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var readersReady = new CountdownEvent(workerCount))
            using (var releaseWorkers = new ManualResetEventSlim(initialState: false))
            {
                CancellationToken workerToken = linkedCancellation.Token;
                var channel = System.Threading.Channels.Channel.CreateBounded<PlannedConditionMatch>(
                    new System.Threading.Channels.BoundedChannelOptions(capacity)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                    });
                Task[] workers = new Task[workerCount];
                for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
                {
                    int ownedWorkerIndex = workerIndex;
                    IIdentityPrimitivePartition<ulong> partition = partitions.Partitions[workerIndex];
                    workers[workerIndex] = Task.Factory.StartNew(
                        () =>
                        {
                            IEnumerator<ulong>? enumerator = null;
                            FastFindWorkerExecutionObserver? workerObserver = null;
                            bool signaledReady = false;
                            try
                            {
                                workerObserver = fastFindObserver?.BeginWorker(ownedWorkerIndex);
                                enumerator = partition.OpenEnumerator();
                                bool hasCurrent = enumerator.MoveNext();
                                readersReady.Signal();
                                signaledReady = true;
                                releaseWorkers.Wait(workerToken);
                                while (hasCurrent)
                                {
                                    workerToken.ThrowIfCancellationRequested();
                                    ulong recordId = enumerator.Current;
                                    if (TryCreatePlannedConditionMatch(
                                            recordId,
                                            evaluator: null,
                                            includePayload,
                                            retainOnlyWhenEvaluated,
                                            out PlannedConditionMatch match,
                                            fastFindObserver))
                                    {
                                        ValueTask write = channel.Writer.WriteAsync(match, workerToken);
                                        if (!write.IsCompletedSuccessfully)
                                        {
                                            long waitStarted = workerObserver is null ? 0 : Stopwatch.GetTimestamp();
                                            write.AsTask().GetAwaiter().GetResult();
                                            workerObserver?.RecordPublicationWait(Stopwatch.GetTimestamp() - waitStarted);
                                        }
                                        else
                                        {
                                            write.GetAwaiter().GetResult();
                                        }

                                        workerObserver?.RecordMatchPublished();
                                    }

                                    hasCurrent = enumerator.MoveNext();
                                }
                            }
                            catch
                            {
                                linkedCancellation.Cancel();
                                throw;
                            }
                            finally
                            {
                                if (!signaledReady)
                                    readersReady.Signal();
                                enumerator?.Dispose();
                                IdentityPrimitivePartitionWork work = partition.CaptureWork();
                                workerObserver?.Complete(work.WorkItemsClaimed, work.SourceItemsExamined);
                            }
                        },
                        workerToken,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                }

                Task completion = Task.WhenAll(workers).ContinueWith(
                    completed =>
                    {
                        if (completed.IsFaulted)
                            channel.Writer.TryComplete(completed.Exception?.GetBaseException());
                        else if (completed.IsCanceled)
                            channel.Writer.TryComplete(new OperationCanceledException(workerToken));
                        else
                            channel.Writer.TryComplete();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                try
                {
                    readersReady.Wait(cancellationToken);
                    if (fastFindObserver is not null)
                        fastFindObserver.SetWorkerPreparation(Stopwatch.GetTimestamp() - workerPreparationStarted);
                    partitions.ReleaseTransitionRead();
                    releaseWorkers.Set();
                    while (channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
                    {
                        while (channel.Reader.TryRead(out PlannedConditionMatch match))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            yield return match;
                        }
                    }

                    completion.GetAwaiter().GetResult();
                }
                finally
                {
                    linkedCancellation.Cancel();
                    releaseWorkers.Set();
                    try
                    {
                        completion.GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Evaluates an unavoidable complete Fractal scan through disjoint contiguous physical partitions instead of unrelated point reads.<br/>
        /// Fractal materializes and sorts authoritative record locations once, byte-balances them across workers, and lets each worker reuse one pooled sequential read window.<br/>
        /// Identity-only callers evaluate directly over borrowed payload bytes; payload-projecting callers copy only successful matches so each returned Inheto view remains valid after its partition advances.<br/>
        /// Completion order is intentionally unspecified and callers that expose natural or sorted order must project the selected identities afterward.<br/>
        /// </summary>
        /// <param name="evaluator">Complete backend-neutral condition evaluator applied to every authoritative payload.<br/></param>
        /// <param name="includePayload">Whether successful matches retain payloads for a downstream projection.<br/></param>
        /// <param name="maxDegreeOfParallelism">Exact number of disjoint physical partition workers; the compatibility parameter name is retained for source stability.<br/></param>
        /// <param name="channelCapacity">Maximum successful matches buffered before producer backpressure applies.<br/></param>
        /// <param name="cancellationToken">Token observed by snapshot traversal, evaluation, publication, and result consumption.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind work counters.<br/></param>
        /// <returns>Successful matches in unspecified worker-completion order.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IterateFractalConditionMatchesByPhysicalPartitions(
            Func<ulong, InhetoBinary, bool> evaluator,
            bool includePayload,
            int maxDegreeOfParallelism,
            int channelCapacity,
            CancellationToken cancellationToken,
            FastFindExecutionObserver? fastFindObserver)
        {
            ArgumentNullException.ThrowIfNull(evaluator);
            int capacity = Math.Max(1, channelCapacity);
            long preparationStarted = Stopwatch.GetTimestamp();
            using FractalKVS.FractalStore.GroupBulkRecordPartitionSet partitions =
                rbase.data.OpenGroupBulkRecordPartitions(storeID, maxDegreeOfParallelism);
            if (partitions.PartitionCount != maxDegreeOfParallelism)
            {
                throw new NotSupportedException(
                    $"FastFind requested exactly {maxDegreeOfParallelism} Fractal workers, but the current physical source supplied {partitions.PartitionCount} non-empty partitions. FastFind will not silently collapse the worker count.");
            }

            fastFindObserver?.SetPhysicalPreparation(
                Stopwatch.GetTimestamp() - preparationStarted,
                partitions.PartitionCount);

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var workersReady = new CountdownEvent(partitions.PartitionCount);
            using var releaseWorkers = new ManualResetEventSlim(initialState: false);
            CancellationToken workerToken = linkedCancellation.Token;
            var channel = System.Threading.Channels.Channel.CreateBounded<PlannedConditionMatch>(
                new System.Threading.Channels.BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                });
            long workerPreparationStarted = Stopwatch.GetTimestamp();
            Task[] workers = new Task[partitions.PartitionCount];
            for (int partitionIndex = 0; partitionIndex < partitions.PartitionCount; partitionIndex++)
            {
                int ownedPartitionIndex = partitionIndex;
                workers[partitionIndex] = Task.Factory.StartNew(
                    () =>
                    {
                        FastFindWorkerExecutionObserver? workerObserver = null;
                        long sourceItemsExamined = 0;
                        bool signaledReady = false;
                        try
                        {
                            using Condition<T>.SemanticWorkerRegexScope semanticWorkerRegexScope =
                                Condition<T>.SemanticWorkerRegexScope.Enter();
                            workerObserver = fastFindObserver?.BeginWorker(ownedPartitionIndex);
                            using FractalKVS.FractalStore.GroupBulkRecordReader reader =
                                partitions.OpenPartition(ownedPartitionIndex);
                            using var stream = new BufferStream(ReadOnlyMemory<byte>.Empty);
                            InhetoBinary borrowed = InhetoBinary.FromStream(stream, NativeDeserializationOptions);
                            workersReady.Signal();
                            signaledReady = true;
                            releaseWorkers.Wait(workerToken);
                            while (reader.Read())
                            {
                                workerToken.ThrowIfCancellationRequested();
                                ulong recordId = reader.ID;
                                sourceItemsExamined++;
                                fastFindObserver?.RecordSourceIdentity();
                                fastFindObserver?.RecordPayloadRead();
                                RebindRecordBinary(borrowed, recordId, reader.Data);
                                if (!includePayload)
                                {
                                    if (evaluator(recordId, borrowed))
                                    {
                                        ValueTask write = channel.Writer.WriteAsync(
                                            new PlannedConditionMatch(recordId, null),
                                            workerToken);
                                        if (!write.IsCompletedSuccessfully)
                                        {
                                            long waitStarted = workerObserver is null ? 0 : Stopwatch.GetTimestamp();
                                            write.AsTask().GetAwaiter().GetResult();
                                            workerObserver?.RecordPublicationWait(Stopwatch.GetTimestamp() - waitStarted);
                                        }
                                        else
                                        {
                                            write.GetAwaiter().GetResult();
                                        }

                                        workerObserver?.RecordMatchPublished();
                                    }
                                    continue;
                                }

                                if (evaluator(recordId, borrowed))
                                {
                                    var retained = CreateRecordBinary(recordId, reader.DataSpan.ToArray());
                                    ValueTask write = channel.Writer.WriteAsync(
                                        new PlannedConditionMatch(recordId, retained),
                                        workerToken);
                                    if (!write.IsCompletedSuccessfully)
                                    {
                                        long waitStarted = workerObserver is null ? 0 : Stopwatch.GetTimestamp();
                                        write.AsTask().GetAwaiter().GetResult();
                                        workerObserver?.RecordPublicationWait(Stopwatch.GetTimestamp() - waitStarted);
                                    }
                                    else
                                    {
                                        write.GetAwaiter().GetResult();
                                    }

                                    workerObserver?.RecordMatchPublished();
                                }
                            }
                        }
                        catch
                        {
                            linkedCancellation.Cancel();
                            throw;
                        }
                        finally
                        {
                                if (!signaledReady)
                                    workersReady.Signal();
                                workerObserver?.Complete(1, sourceItemsExamined);
                            }
                    },
                    workerToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            Task completion = Task.WhenAll(workers).ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                        channel.Writer.TryComplete(completed.Exception?.GetBaseException());
                    else if (completed.IsCanceled)
                        channel.Writer.TryComplete(new OperationCanceledException(workerToken));
                    else
                        channel.Writer.TryComplete();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            try
            {
                workersReady.Wait(cancellationToken);
                fastFindObserver?.SetWorkerPreparation(
                    Stopwatch.GetTimestamp() - workerPreparationStarted);
                releaseWorkers.Set();
                while (channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
                {
                    while (channel.Reader.TryRead(out PlannedConditionMatch match))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return match;
                    }
                }

                completion.GetAwaiter().GetResult();
            }
            finally
            {
                linkedCancellation.Cancel();
                releaseWorkers.Set();
                try
                {
                    completion.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                }
            }
        }

        /// <summary>
        /// Evaluates an unavoidable Fractal scan through one sequential pooled-block producer and exactly the requested number of unordered evaluation workers.<br/>
        /// Fractal reads each physical block once; workers overlay borrowed Inheto views directly on that leased buffer and copy only successful projected payloads that must outlive block disposal.<br/>
        /// Bounded block and match channels preserve backpressure, cancellation drains every unconsumed block, and no worker-count or identity-materialization fallback is permitted.<br/>
        /// </summary>
        /// <param name="evaluator">Complete backend-neutral condition evaluator applied to every authoritative payload.<br/></param>
        /// <param name="includePayload">Whether successful matches retain payloads for downstream projection.<br/></param>
        /// <param name="workerCount">Exact number of evaluation workers consuming produced blocks.<br/></param>
        /// <param name="channelCapacity">Maximum successful matches buffered before worker backpressure applies.<br/></param>
        /// <param name="cancellationToken">Token observed by production, evaluation, publication, and result consumption.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind work counters.<br/></param>
        /// <returns>Successful matches in unspecified worker-completion order.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IterateFractalConditionMatchesByPooledBlockProducer(
            Func<ulong, InhetoBinary, bool> evaluator,
            bool includePayload,
            int workerCount,
            int channelCapacity,
            CancellationToken cancellationToken,
            FastFindExecutionObserver? fastFindObserver)
        {
            ArgumentNullException.ThrowIfNull(evaluator);
            int capacity = Math.Max(1, channelCapacity);
            long preparationStarted = Stopwatch.GetTimestamp();
            using FractalKVS.FractalStore.GroupBulkRecordBlockProducer producer =
                rbase.data.OpenGroupBulkRecordBlockProducer(storeID);
            if (producer.RecordCount < workerCount)
            {
                throw new NotSupportedException(
                    $"FastFind requested exactly {workerCount} Fractal workers, but the current source contains only {producer.RecordCount} records.");
            }

            fastFindObserver?.SetPhysicalPreparation(
                Stopwatch.GetTimestamp() - preparationStarted,
                workerCount);

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var workersReady = new CountdownEvent(workerCount);
            using var releaseWorkers = new ManualResetEventSlim(initialState: false);
            CancellationToken workerToken = linkedCancellation.Token;
            var blocks = System.Threading.Channels.Channel.CreateBounded<FractalKVS.FractalStore.GroupBulkRecordBlock>(
                new System.Threading.Channels.BoundedChannelOptions(Math.Max(1, workerCount * 2))
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                });
            var matches = System.Threading.Channels.Channel.CreateBounded<PlannedConditionMatch>(
                new System.Threading.Channels.BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                });

            long workerPreparationStarted = Stopwatch.GetTimestamp();
            Task source = Task.Factory.StartNew(
                () =>
                {
                    FractalKVS.FractalStore.GroupBulkRecordBlock? block = null;
                    try
                    {
                        releaseWorkers.Wait(workerToken);
                        while ((block = producer.ReadBlock()) is not null)
                        {
                            blocks.Writer.WriteAsync(block, workerToken).AsTask().GetAwaiter().GetResult();
                            block = null;
                        }

                        blocks.Writer.TryComplete();
                    }
                    catch (Exception exception)
                    {
                        block?.Dispose();
                        blocks.Writer.TryComplete(exception);
                        linkedCancellation.Cancel();
                        throw;
                    }
                },
                workerToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            Task[] workers = new Task[workerCount];
            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                int ownedWorkerIndex = workerIndex;
                workers[workerIndex] = Task.Factory.StartNew(
                    () =>
                    {
                        FastFindWorkerExecutionObserver? workerObserver = null;
                        long sourceItemsExamined = 0;
                        int workItemsClaimed = 0;
                        bool signaledReady = false;
                        try
                        {
                            using Condition<T>.SemanticWorkerRegexScope semanticWorkerRegexScope =
                                Condition<T>.SemanticWorkerRegexScope.Enter();
                            workerObserver = fastFindObserver?.BeginWorker(ownedWorkerIndex);
                            using var stream = new BufferStream(ReadOnlyMemory<byte>.Empty);
                            InhetoBinary borrowed = InhetoBinary.FromStream(stream, NativeDeserializationOptions);
                            workersReady.Signal();
                            signaledReady = true;
                            releaseWorkers.Wait(workerToken);
                            while (WaitForSourceWork())
                            {
                                while (blocks.Reader.TryRead(out FractalKVS.FractalStore.GroupBulkRecordBlock? block))
                                {
                                    using (block)
                                    {
                                        workItemsClaimed++;
                                        while (block.Read())
                                        {
                                            workerToken.ThrowIfCancellationRequested();
                                            sourceItemsExamined++;
                                            ulong recordId = block.ID;
                                            fastFindObserver?.RecordSourceIdentity();
                                            fastFindObserver?.RecordPayloadRead();
                                            RebindRecordBinary(borrowed, recordId, block.Data);
                                            if (!evaluator(recordId, borrowed))
                                                continue;

                                            PlannedConditionMatch match;
                                            if (!includePayload)
                                            {
                                                match = new PlannedConditionMatch(recordId, null);
                                            }
                                            else
                                            {
                                                var retained = CreateRecordBinary(recordId, block.DataSpan.ToArray());
                                                match = new PlannedConditionMatch(recordId, retained);
                                            }

                                            ValueTask write = matches.Writer.WriteAsync(match, workerToken);
                                            if (!write.IsCompletedSuccessfully)
                                            {
                                                long waitStarted = workerObserver is null ? 0 : Stopwatch.GetTimestamp();
                                                write.AsTask().GetAwaiter().GetResult();
                                                workerObserver?.RecordPublicationWait(Stopwatch.GetTimestamp() - waitStarted);
                                            }
                                            else
                                            {
                                                write.GetAwaiter().GetResult();
                                            }

                                            workerObserver?.RecordMatchPublished();
                                        }
                                    }
                                }
                            }

                            bool WaitForSourceWork()
                            {
                                ValueTask<bool> wait = blocks.Reader.WaitToReadAsync(workerToken);
                                if (wait.IsCompletedSuccessfully)
                                    return wait.Result;

                                long waitStarted = workerObserver is null ? 0 : Stopwatch.GetTimestamp();
                                bool readable = wait.AsTask().GetAwaiter().GetResult();
                                workerObserver?.RecordSourceWait(Stopwatch.GetTimestamp() - waitStarted);
                                return readable;
                            }
                        }
                        catch
                        {
                            linkedCancellation.Cancel();
                            throw;
                        }
                        finally
                        {
                            if (!signaledReady)
                                workersReady.Signal();
                            workerObserver?.Complete(workItemsClaimed, sourceItemsExamined);
                        }
                    },
                    workerToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            Task completion = Task.WhenAll(workers.Prepend(source)).ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                        matches.Writer.TryComplete(completed.Exception?.GetBaseException());
                    else if (completed.IsCanceled)
                        matches.Writer.TryComplete(new OperationCanceledException(workerToken));
                    else
                        matches.Writer.TryComplete();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            try
            {
                workersReady.Wait(cancellationToken);
                fastFindObserver?.SetWorkerPreparation(
                    Stopwatch.GetTimestamp() - workerPreparationStarted);
                releaseWorkers.Set();
                while (matches.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
                {
                    while (matches.Reader.TryRead(out PlannedConditionMatch match))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return match;
                    }
                }

                completion.GetAwaiter().GetResult();
            }
            finally
            {
                linkedCancellation.Cancel();
                releaseWorkers.Set();
                try
                {
                    completion.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                }

                while (blocks.Reader.TryRead(out FractalKVS.FractalStore.GroupBulkRecordBlock? block))
                    block.Dispose();
            }
        }

        /// <summary>
        /// Materializes an already-planned identity source through one shared lazy or bounded-parallel payload pipeline.<br/>
        /// Condition, State, and future backend-neutral identity planners can therefore reuse identical single-read, missing-record, cancellation, and backpressure behavior.<br/>
        /// </summary>
        /// <param name="identities">The identities selected by an authoritative plan.<br/></param>
        /// <param name="evaluator">An optional semantic residual evaluated against each payload.<br/></param>
        /// <param name="includePayload">Whether returned matches retain their Inheto payload.<br/></param>
        /// <param name="retainOnlyWhenEvaluated">Whether payload retention is limited to rows that already required semantic evaluation.<br/></param>
        /// <param name="maxDegreeOfParallelism">Exact payload-read/evaluation worker count; one preserves lazy single-thread traversal.<br/></param>
        /// <param name="channelCapacity">Maximum completed parallel matches buffered before consumer backpressure applies.<br/></param>
        /// <param name="cancellationToken">Token observed during identity traversal, payload evaluation, and channel consumption.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind work counters.<br/></param>
        /// <returns>Matching identities with optional already-read payloads.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IteratePlannedIdentityMatches(
            IEnumerable<ulong> identities,
            Func<ulong, InhetoBinary, bool>? evaluator,
            bool includePayload,
            bool retainOnlyWhenEvaluated = false,
            int maxDegreeOfParallelism = 1,
            int channelCapacity = 256,
            CancellationToken cancellationToken = default,
            FastFindExecutionObserver? fastFindObserver = null)
        {
            ArgumentNullException.ThrowIfNull(identities);
            cancellationToken.ThrowIfCancellationRequested();

            if (maxDegreeOfParallelism < 2)
            {
                foreach (ulong recordId in identities)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryCreatePlannedConditionMatch(recordId, evaluator, includePayload, retainOnlyWhenEvaluated, out PlannedConditionMatch match, fastFindObserver))
                        yield return match;
                }

                yield break;
            }

            if (evaluator is null)
            {
                foreach (ulong recordId in identities)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryCreatePlannedConditionMatch(recordId, evaluator, includePayload, retainOnlyWhenEvaluated, out PlannedConditionMatch match, fastFindObserver))
                        yield return match;
                }

                yield break;
            }

            foreach (PlannedConditionMatch match in IteratePlannedConditionMatchesParallel(
                identities,
                evaluator,
                includePayload,
                retainOnlyWhenEvaluated,
                maxDegreeOfParallelism,
                channelCapacity,
                cancellationToken,
                fastFindObserver))
            {
                yield return match;
            }
        }

        /// <summary>
        /// Reads and evaluates one planned identity directly from Fractal, retaining its Inheto payload only when the consumer requests it.<br/>
        /// A direct LibraDex identity-only plan performs no payload read; residual and scan plans always evaluate the complete semantic predicate before returning a match.<br/>
        /// </summary>
        /// <param name="recordId">The authoritative Abraxas record identity.<br/></param>
        /// <param name="evaluator">The complete semantic evaluator for candidate/scan plans, or null for a complete direct-LibraDex plan.<br/></param>
        /// <param name="includePayload">Whether the returned match must retain the deserialized Inheto payload.<br/></param>
        /// <param name="retainOnlyWhenEvaluated">Whether a direct match should remain identity-only even when evaluated matches retain payloads.<br/></param>
        /// <param name="match">The completed match when this method returns true.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind work counters.<br/></param>
        /// <returns>True when the identity exists and satisfies the selected plan; otherwise false.<br/></returns>
        private bool TryCreatePlannedConditionMatch(
            ulong recordId,
            Func<ulong, InhetoBinary, bool>? evaluator,
            bool includePayload,
            bool retainOnlyWhenEvaluated,
            out PlannedConditionMatch match,
            FastFindExecutionObserver? fastFindObserver = null)
        {
            fastFindObserver?.RecordSourceIdentity();
            if (evaluator is null && (!includePayload || retainOnlyWhenEvaluated))
            {
                match = new PlannedConditionMatch(recordId, null);
                return true;
            }

            if (!rbase.data.TryGetOne(recordId, out var record))
            {
                match = default;
                return false;
            }

            fastFindObserver?.RecordPayloadRead();

            using (record)
            {
                if (!includePayload)
                {
                    var borrowedBinary = CreateRecordBinary(recordId, record.Data);
                    if (evaluator is not null && !evaluator(recordId, borrowedBinary))
                    {
                        match = default;
                        return false;
                    }

                    match = new PlannedConditionMatch(recordId, null);
                    return true;
                }

                var binary = CreateRecordBinary(recordId, record.DataSpan.ToArray());
                if (evaluator is not null && !evaluator(recordId, binary))
                {
                    match = default;
                    return false;
                }

                match = new PlannedConditionMatch(recordId, includePayload ? binary : null);
                return true;
            }
        }

        /// <summary>
        /// Evaluates planned identities concurrently while bounding retained payloads through a single-consumer channel.<br/>
        /// Identity enumeration remains single-owner, each Fractal record is read at most once, and worker failures or cancellation are propagated to the consuming iterator.<br/>
        /// </summary>
        /// <param name="identities">The direct, candidate, or full-scan identity source selected by the planner.<br/></param>
        /// <param name="evaluator">The complete semantic evaluator for candidate/scan plans, or null for a complete direct-LibraDex plan.<br/></param>
        /// <param name="includePayload">Whether matches retain their already-read Inheto payloads.<br/></param>
        /// <param name="retainOnlyWhenEvaluated">Whether direct matches remain identity-only while evaluated matches retain their payloads.<br/></param>
        /// <param name="maxDegreeOfParallelism">Exact concurrent record readers/evaluators; the compatibility parameter name is retained for source stability.<br/></param>
        /// <param name="channelCapacity">Maximum completed matches buffered for the consumer.<br/></param>
        /// <param name="cancellationToken">Token observed by producer workers and the consumer.<br/></param>
        /// <param name="fastFindObserver">Optional operation-scoped FastFind work counters.<br/></param>
        /// <returns>Completed matches in unspecified parallel completion order.<br/></returns>
        private IEnumerable<PlannedConditionMatch> IteratePlannedConditionMatchesParallel(
            IEnumerable<ulong> identities,
            Func<ulong, InhetoBinary, bool>? evaluator,
            bool includePayload,
            bool retainOnlyWhenEvaluated,
            int maxDegreeOfParallelism,
            int channelCapacity,
            CancellationToken cancellationToken,
            FastFindExecutionObserver? fastFindObserver)
        {
            int capacity = Math.Max(1, channelCapacity);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var workersReady = new CountdownEvent(maxDegreeOfParallelism);
            using var releaseWorkers = new ManualResetEventSlim(initialState: false);
            CancellationToken workerToken = linkedCancellation.Token;
            var input = System.Threading.Channels.Channel.CreateBounded<ulong>(
                new System.Threading.Channels.BoundedChannelOptions(capacity)
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                });
            var output = System.Threading.Channels.Channel.CreateBounded<PlannedConditionMatch>(
                new System.Threading.Channels.BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                });
            Task[] workers = new Task[maxDegreeOfParallelism];
            for (int workerIndex = 0; workerIndex < maxDegreeOfParallelism; workerIndex++)
            {
                workers[workerIndex] = Task.Factory.StartNew(
                    () =>
                    {
                        bool signaledReady = false;
                        try
                        {
                            using Condition<T>.SemanticWorkerRegexScope semanticWorkerRegexScope =
                                Condition<T>.SemanticWorkerRegexScope.Enter();
                            fastFindObserver?.RecordWorkerStarted();
                            workersReady.Signal();
                            signaledReady = true;
                            releaseWorkers.Wait(workerToken);
                            while (input.Reader.WaitToReadAsync(workerToken).AsTask().GetAwaiter().GetResult())
                            {
                                while (input.Reader.TryRead(out ulong recordId))
                                {
                                    workerToken.ThrowIfCancellationRequested();
                                    if (!TryCreatePlannedConditionMatch(
                                            recordId,
                                            evaluator,
                                            includePayload,
                                            retainOnlyWhenEvaluated,
                                            out PlannedConditionMatch match,
                                            fastFindObserver))
                                    {
                                        continue;
                                    }

                                    output.Writer.WriteAsync(match, workerToken).AsTask().GetAwaiter().GetResult();
                                }
                            }
                        }
                        catch
                        {
                            linkedCancellation.Cancel();
                            throw;
                        }
                        finally
                        {
                            if (!signaledReady)
                                workersReady.Signal();
                        }
                    },
                    workerToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            Task sourceProducer = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        foreach (ulong recordId in identities)
                        {
                            workerToken.ThrowIfCancellationRequested();
                            input.Writer.WriteAsync(recordId, workerToken).AsTask().GetAwaiter().GetResult();
                        }

                        input.Writer.TryComplete();
                    }
                    catch (Exception exception)
                    {
                        input.Writer.TryComplete(exception);
                        throw;
                    }
                },
                workerToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            Task completion = Task.WhenAll(workers.Append(sourceProducer)).ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                        output.Writer.TryComplete(completed.Exception?.GetBaseException());
                    else if (completed.IsCanceled)
                        output.Writer.TryComplete(new OperationCanceledException(workerToken));
                    else
                        output.Writer.TryComplete();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            try
            {
                workersReady.Wait(cancellationToken);
                releaseWorkers.Set();
                while (output.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
                {
                    while (output.Reader.TryRead(out PlannedConditionMatch match))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return match;
                    }
                }

                completion.GetAwaiter().GetResult();
            }
            finally
            {
                linkedCancellation.Cancel();
                releaseWorkers.Set();
                try
                {
                    completion.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                }
            }
        }

        /// <summary>
        /// Materializes typed records directly from payloads retained by the shared condition match stream.<br/>
        /// Candidate/residual and full-scan plans therefore evaluate and materialize from one Fractal read, while direct plans hydrate only the identities selected by LibraDex.<br/>
        /// </summary>
        /// <param name="where">The completed condition selecting aggregate fallback records.<br/></param>
        /// <param name="operationName">The operation name reported through condition and record materialization lifecycle events.<br/></param>
        /// <returns>Typed records selected by the shared condition planner.<br/></returns>
        private IEnumerable<T> IteratePlannedConditionRecords(
            Condition<T>.EndCondition where,
            string operationName)
        {
            foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(
                where,
                operationName,
                includePayload: true))
            {
                InhetoBinary payload = match.Payload
                    ?? throw new InvalidDataException($"{operationName} requested a typed match without a retained Inheto payload.");
                payload.SetMaterializationContext(match.RecordId);
                OnRecordMaterializing(match.RecordId, typeof(T), operationName);
                T? record = payload.ToObject<T>(NativeDeserializationOptions);
                OnRecordMaterialized(match.RecordId, typeof(T), record, operationName);
                if (record is not null)
                    yield return record;
            }
        }

        /// <summary>
        /// Materializes every authoritative store record directly from Fractal/Inheto for an unfiltered aggregate fallback.<br/>
        /// </summary>
        /// <param name="operationName">The operation name reported through record materialization lifecycle events.<br/></param>
        /// <returns>All typed records in native Fractal store order.<br/></returns>
        private IEnumerable<T> IterateFractalRecordsForAggregate(string operationName)
        {
            foreach (var storedRecord in IterateFractalRecordsByGroupKey(storeID))
            {
                try
                {
                    ulong recordId = storedRecord.Header.ID;
                    var binary = CreateRecordBinary(recordId, storedRecord.Data);
                    binary.SetMaterializationContext(recordId);
                    OnRecordMaterializing(recordId, typeof(T), operationName);
                    T? record = binary.ToObject<T>(NativeDeserializationOptions);
                    OnRecordMaterialized(recordId, typeof(T), record, operationName);
                    if (record is not null)
                        yield return record;
                }
                finally
                {
                    storedRecord.Dispose();
                }
            }
        }

        /// <summary>
        /// Identifies the legacy runtime State aggregate requested by an original-name public overload.<br/>
        /// </summary>
        private enum StateAggregateKind
        {
            Minimum,
            Maximum,
            DistinctCount
        }

        /// <summary>
        /// Carries one identity selected by the shared condition planner and, when requested, the same Inheto payload already used for residual evaluation.<br/>
        /// </summary>
        internal readonly struct PlannedConditionMatch
        {
            /// <summary>
            /// Creates one planned condition match.<br/>
            /// </summary>
            /// <param name="recordId">The matching Abraxas record identity.<br/></param>
            /// <param name="payload">The retained Inheto payload, or null for an identity-only consumer.<br/></param>
            internal PlannedConditionMatch(ulong recordId, InhetoBinary? payload)
            {
                RecordId = recordId;
                Payload = payload;
            }

            internal ulong RecordId { get; }
            internal InhetoBinary? Payload { get; }
        }

        /// <summary>
        /// Fractional results are truncated toward zero to preserve the historical <see cref="ulong"/> return shape; use a strongly typed <c>Average</c> overload when fractional output is required.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path whose non-null numeric values contribute.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked, truncated unsigned average.<br/></returns>
        public ulong Average(string propPath, bool useMemoryIndex = false)
        {
            _ = useMemoryIndex;
            return (ulong)ExecuteLegacyPathAggregate(propPath, null, null, LegacyPathAggregateKind.Average, nameof(Average))!;
        }

        /// <summary>
        /// Fractional results are truncated toward zero to preserve the historical <see cref="ulong"/> return shape; use a strongly typed <c>Average</c> overload when fractional output is required.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the simple numeric property path.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked, truncated unsigned average.<br/></returns>
        public ulong Average(Expression<Func<T, object>> propPath, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return Average(propPath.GetPropertyPath(allowExtensionMethods: false), useMemoryIndex);
        }

        /// <summary>
        /// The condition uses the shared direct, candidate/residual, or Fractal-scan plan, and fractional output is truncated toward zero for compatibility.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path whose non-null numeric values contribute.<br/></param>
        /// <param name="Where">The completed condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked, truncated unsigned average.<br/></returns>
        public ulong Average(string propPath, Condition<T>.EndCondition Where, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(Where);
            _ = useMemoryIndex;
            return (ulong)ExecuteLegacyPathAggregate(propPath, Where, null, LegacyPathAggregateKind.Average, nameof(Average))!;
        }

        /// <summary>
        /// The condition uses the shared planner, and fractional output is truncated toward zero for compatibility.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the simple numeric property path.<br/></param>
        /// <param name="Where">The completed condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked, truncated unsigned average.<br/></returns>
        public ulong Average(Expression<Func<T, object>> propPath, Condition<T>.EndCondition Where, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return Average(propPath.GetPropertyPath(allowExtensionMethods: false), Where, useMemoryIndex);
        }

        /// <summary>
        /// Numeric accumulation and the final conversion are checked; null or missing values do not contribute.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path whose numeric values contribute.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked unsigned sum.<br/></returns>
        public ulong Sum(string propPath, bool useMemoryIndex = false)
        {
            _ = useMemoryIndex;
            return (ulong)ExecuteLegacyPathAggregate(propPath, null, null, LegacyPathAggregateKind.Sum, nameof(Sum))!;
        }

        /// <summary>
        /// Numeric accumulation and the final conversion are checked; null or missing values do not contribute.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the simple numeric property path.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked unsigned sum.<br/></returns>
        public ulong Sum(Expression<Func<T, object>> propPath, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return Sum(propPath.GetPropertyPath(allowExtensionMethods: false), useMemoryIndex);
        }

        /// <summary>
        /// Duplicate identities have set semantics: repeating an identity does not add another result.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path whose numeric values contribute.<br/></param>
        /// <param name="recordIDs">Record identities selected for aggregation.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked unsigned sum.<br/></returns>
        public ulong Sum(string propPath, IEnumerable<ulong> recordIDs, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(recordIDs);
            _ = useMemoryIndex;
            return (ulong)ExecuteLegacyPathAggregate(propPath, null, recordIDs, LegacyPathAggregateKind.Sum, nameof(Sum))!;
        }

        /// <summary>
        /// Duplicate identities have set semantics: repeating an identity does not add another result.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the simple numeric property path.<br/></param>
        /// <param name="recordIDs">Record identities selected for aggregation.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked unsigned sum.<br/></returns>
        public ulong Sum(Expression<Func<T, object>> propPath, IEnumerable<ulong> recordIDs, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return Sum(propPath.GetPropertyPath(allowExtensionMethods: false), recordIDs, useMemoryIndex);
        }

        /// <summary>
        /// The condition uses the shared direct, candidate/residual, or Fractal-scan plan, and all numeric conversions are checked.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path whose numeric values contribute.<br/></param>
        /// <param name="Where">The completed condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked unsigned sum.<br/></returns>
        public ulong Sum(string propPath, Condition<T>.EndCondition Where, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(Where);
            _ = useMemoryIndex;
            return (ulong)ExecuteLegacyPathAggregate(propPath, Where, null, LegacyPathAggregateKind.Sum, nameof(Sum))!;
        }

        /// <summary>
        /// The condition uses the shared planner, and all numeric conversions are checked.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the simple numeric property path.<br/></param>
        /// <param name="Where">The completed condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>Zero for no contributing values; otherwise the checked unsigned sum.<br/></returns>
        public ulong Sum(Expression<Func<T, object>> propPath, Condition<T>.EndCondition Where, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return Sum(propPath.GetPropertyPath(allowExtensionMethods: false), Where, useMemoryIndex);
        }

        /// <summary>
        /// An existing ordered LibraDex index is streamed when available; otherwise authoritative Fractal/Inheto payloads supply the values.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path to compare.<br/></param>
        /// <param name="where">Optional condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>The minimum value, or null when no non-null values contribute.<br/></returns>
        public object? Min(string propPath, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            _ = useMemoryIndex;
            return ExecuteLegacyPathAggregate(propPath, where, null, LegacyPathAggregateKind.Minimum, nameof(Min));
        }

        /// <summary>
        /// An existing ordered LibraDex index is streamed when available; otherwise authoritative Fractal/Inheto payloads supply the values.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the simple property path.<br/></param>
        /// <param name="where">Optional condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>The minimum value, or null when no non-null values contribute.<br/></returns>
        public object? Min(Expression<Func<T, object>> propPath, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return Min(propPath.GetPropertyPath(allowExtensionMethods: false), where, useMemoryIndex);
        }

        /// <summary>
        /// Returns the minimum non-null value from a direct or backend-neutral computed selector.<br/>
        /// </summary>
        /// <param name="selector">Direct property or computed selector with complete backend-neutral semantics.<br/></param>
        /// <param name="where">Optional condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>The minimum value, or null when no non-null values contribute.<br/></returns>
        public object? Min(ISelector selector, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (TryResolveLegacySimpleSelectorPath(selector, out string path))
                return Min(path, where, useMemoryIndex);
            return ExecuteComputedSelectorAggregate(
                selector,
                where,
                LegacyPathAggregateKind.Minimum,
                nameof(Min));
        }

        /// <summary>
        /// An existing ordered LibraDex index is streamed when available; otherwise authoritative Fractal/Inheto payloads supply the values.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path to compare.<br/></param>
        /// <param name="where">Optional condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>The maximum value, or null when no non-null values contribute.<br/></returns>
        public object? Max(string propPath, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            _ = useMemoryIndex;
            return ExecuteLegacyPathAggregate(propPath, where, null, LegacyPathAggregateKind.Maximum, nameof(Max));
        }

        /// <summary>
        /// An existing ordered LibraDex index is streamed when available; otherwise authoritative Fractal/Inheto payloads supply the values.<br/>
        /// </summary>
        /// <param name="propPath">Expression resolving to the simple property path.<br/></param>
        /// <param name="where">Optional condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>The maximum value, or null when no non-null values contribute.<br/></returns>
        public object? Max(Expression<Func<T, object>> propPath, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(propPath);
            return Max(propPath.GetPropertyPath(allowExtensionMethods: false), where, useMemoryIndex);
        }

        /// <summary>
        /// Returns the maximum non-null value from a direct or backend-neutral computed selector.<br/>
        /// </summary>
        /// <param name="selector">Direct property or computed selector with complete backend-neutral semantics.<br/></param>
        /// <param name="where">Optional condition selecting contributing records.<br/></param>
        /// <param name="useMemoryIndex">Legacy compatibility parameter; backend selection now follows registered LibraDex indexes and the store policy.<br/></param>
        /// <returns>The maximum value, or null when no non-null values contribute.<br/></returns>
        public object? Max(ISelector selector, Condition<T>.EndCondition? where = null, bool useMemoryIndex = false)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (TryResolveLegacySimpleSelectorPath(selector, out string path))
                return Max(path, where, useMemoryIndex);
            return ExecuteComputedSelectorAggregate(
                selector,
                where,
                LegacyPathAggregateKind.Maximum,
                nameof(Max));
        }

        /// <summary>
        /// The selector reader is constructed once before enumeration; each record then performs only its typed semantic transformation and the existing compact aggregate update.<br/>
        /// </summary>
        /// <param name="selector">Computed selector carrying a complete backend-neutral descriptor and CLR result type.<br/></param>
        /// <param name="where">Optional planned condition selecting contributing records.<br/></param>
        /// <param name="kind">Minimum or maximum reduction requested by the public selector overload.<br/></param>
        /// <param name="operationName">Public operation name used by planner diagnostics.<br/></param>
        /// <returns>The boxed extreme value, or <see langword="null"/> when no valid selector value contributes.<br/></returns>
        private object? ExecuteComputedSelectorAggregate(
            ISelector selector,
            Condition<T>.EndCondition? where,
            LegacyPathAggregateKind kind,
            string operationName)
        {
            using var lifecycle = EnterStoreOperationScope();
            if (!TryCreateStoreSelectorReader(selector, out var readValue))
            {
                throw new NotSupportedException(
                    $"{operationName}(ISelector, ...) requires a direct property or a computed selector with complete backend-neutral semantics.");
            }

            var accumulator = new LegacyPathAggregateAccumulator(kind);
            foreach (InhetoBinary payload in IterateAuthoritativeGroupingPayloads(where, operationName))
            {
                var candidate = readValue(payload);
                if (candidate.HasValue)
                    accumulator.Add(candidate.Value);
            }
            return accumulator.Complete();
        }

        /// <summary>
        /// Executes one original-name path aggregate through an existing LibraDex value index or authoritative Fractal/Inheto payloads.<br/>
        /// Conditions always use the shared planner, while explicit identity inputs retain historical set semantics.<br/>
        /// </summary>
        /// <param name="propPath">The simple property path to aggregate.<br/></param>
        /// <param name="where">Optional condition selecting records.<br/></param>
        /// <param name="recordIDs">Optional explicit identity set; mutually exclusive with <paramref name="where"/>.<br/></param>
        /// <param name="kind">The aggregate operation to perform.<br/></param>
        /// <param name="operationName">The public operation name used by planning diagnostics.<br/></param>
        /// <returns>The boxed aggregate result under the legacy public return contract.<br/></returns>
        private object? ExecuteLegacyPathAggregate(
            string propPath,
            Condition<T>.EndCondition? where,
            IEnumerable<ulong>? recordIDs,
            LegacyPathAggregateKind kind,
            string operationName)
        {
            using var lifecycle = EnterStoreOperationScope();
            if (where is not null && recordIDs is not null)
                throw new ArgumentException("A legacy path aggregate cannot combine a condition with an explicit record-identity set.");

            string path = new PropPath(propPath).Path;
            var accumulator = new LegacyPathAggregateAccumulator(kind);
            CatalogIdentityGroupIndexes? group = null;
            IIndex? index = null;
            bool hasIndexedTarget = false;
            if (TryGetIndex(path, out RecordIndex recordIndex) &&
                TryGetLibraDexIndexGroup(recordIndex, out CatalogIdentityGroupIndexes resolvedGroup, out IIndex resolvedIndex))
            {
                group = resolvedGroup;
                index = resolvedIndex;
                hasIndexedTarget = true;
            }

            HashSet<ulong>? selected = null;
            if (recordIDs is not null)
            {
                selected = new HashSet<ulong>();
                foreach (ulong recordID in recordIDs)
                {
                    EnsureRecordBelongsToCurrentStore(recordID, nameof(recordIDs));
                    selected.Add(recordID);
                }
            }
            else if (where is not null && hasIndexedTarget)
            {
                selected = new HashSet<ulong>();
                foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(where, operationName, includePayload: false))
                    selected.Add(match.RecordId);
            }

            if (hasIndexedTarget)
            {
                if (TryExecuteTypedNumericPathAggregate(index!, selected, kind, path, operationName, out object? typedResult))
                    return typedResult;
                if (selected is { Count: 0 })
                    return accumulator.Complete();

                LibraDexConditionEndCondition all = CreateLibraDexAllCondition(group!, index!);
                foreach (LibraDexRuntimeTuple tuple in group!.IterateTuples(index!, all, QueryDirection.Ascending))
                {
                    if (selected is not null && !selected.Contains(ToRecordId(tuple.Identity)))
                        continue;
                    accumulator.Add(tuple.Key);
                }

                return accumulator.Complete();
            }

            if (selected is not null)
            {
                foreach (ulong recordID in selected)
                {
                    if (TryReadLegacyAggregateValue(recordID, path, out object? value))
                        accumulator.Add(value);
                }
                return accumulator.Complete();
            }

            if (where is not null)
            {
                foreach (PlannedConditionMatch match in IteratePlannedConditionMatches(where, operationName, includePayload: true))
                {
                    if (TryReadLegacyAggregateValue(match.Payload, path, out object? value))
                        accumulator.Add(value);
                }
                return accumulator.Complete();
            }

            foreach (var storedRecord in IterateFractalRecordsByGroupKey(storeID))
            {
                try
                {
                    InhetoBinary payload = CreateRecordBinary(storedRecord.Header.ID, storedRecord.Data);
                    if (TryReadLegacyAggregateValue(payload, path, out object? value))
                        accumulator.Add(value);
                }
                finally
                {
                    storedRecord.Dispose();
                }
            }

            return accumulator.Complete();
        }

        /// <summary>
        /// Reads one aggregate property directly from an authoritative Fractal record identity.<br/>
        /// </summary>
        /// <param name="recordID">The Abraxas record identity to read.<br/></param>
        /// <param name="propPath">The normalized property path.<br/></param>
        /// <param name="value">The unwrapped scalar value when available.<br/></param>
        /// <returns><c>true</c> when the record and property exist, including an explicit null property value.<br/></returns>
        private bool TryReadLegacyAggregateValue(ulong recordID, string propPath, out object? value)
        {
            value = null;
            if (!rbase.data.TryGetOne(recordID, out var storedRecord))
                return false;

            using (storedRecord)
            {
                InhetoBinary payload = CreateRecordBinary(recordID, storedRecord.Data);
                return TryReadLegacyAggregateValue(payload, propPath, out value);
            }
        }

        /// <summary>
        /// Reads and unwraps one scalar property from an already materialized Inheto payload.<br/>
        /// Wrapper metadata is cached by runtime wrapper type so repeated aggregate values avoid repeated property discovery.<br/>
        /// </summary>
        /// <param name="payload">The authoritative Inheto payload.<br/></param>
        /// <param name="propPath">The normalized property path.<br/></param>
        /// <param name="value">The unwrapped scalar value when the property exists.<br/></param>
        /// <returns><c>true</c> when the property exists; otherwise <c>false</c>.<br/></returns>
        private bool TryReadLegacyAggregateValue(InhetoBinary? payload, string propPath, out object? value)
        {
            value = null;
            if (payload is null)
                return false;

            if (!payload.PropExists(propPath))
            {
                Inheto.PropPathCheck check = ExecutableLegacyPropPathChecks.GetOrAdd(
                    propPath,
                    static path => InhetoBinary.CheckPropPath(typeof(T), path));
                if (!check.IsValid || !check.RequiresClrContinuation)
                    return false;

                Inheto.PropPathReadState state = payload.TryReadPropPath(
                    check,
                    out object? projected,
                    NativeDeserializationOptions,
                    throwOnInvocationError: false);
                if (state == Inheto.PropPathReadState.Value)
                {
                    value = projected;
                    return true;
                }
                return state is Inheto.PropPathReadState.Null or Inheto.PropPathReadState.SourceNull;
            }

            object? raw = payload.ReadProp(propPath, NativeDeserializationOptions);
            if (raw is not IInhetoObject)
            {
                value = raw;
                return true;
            }

            Type wrapperType = raw.GetType();
            PropertyInfo? valueProperty = LegacyScalarValueProperties.GetOrAdd(
                wrapperType,
                static type => type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance));
            value = valueProperty?.GetValue(raw);
            return true;
        }

        /// <summary>
        /// Resolves a selector only when its builder still describes exactly one unmodified property access.<br/>
        /// </summary>
        /// <param name="selector">The selector to inspect.<br/></param>
        /// <param name="path">The normalized simple property path when resolution succeeds.<br/></param>
        /// <returns><c>true</c> only for an exact simple property selector.<br/></returns>
        private static bool TryResolveLegacySimpleSelectorPath(ISelector selector, out string path)
        {
            path = string.Empty;
            if (selector is not ISelectorStructure structure ||
                string.IsNullOrWhiteSpace(structure.SimplePropertyPath))
            {
                return false;
            }

            path = structure.SimplePropertyPath;
            return true;
        }

        /// <summary>
        /// Caches the scalar <c>Value</c> property exposed by Inheto primitive wrapper types.<br/>
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo?> LegacyScalarValueProperties = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Inheto.PropPathCheck> ExecutableLegacyPropPathChecks = new(StringComparer.Ordinal);

        /// <summary>
        /// </summary>
        private enum LegacyPathAggregateKind
        {
            Sum,
            Average,
            Minimum,
            Maximum
        }

        /// <summary>
        /// Accumulates one legacy property-path aggregate while preserving its historical boxed extrema and unsigned numeric result shapes.<br/>
        /// Numeric accumulation uses Decimal so all 64-bit integral inputs remain exact; the final unsigned conversion truncates toward zero and is range checked.<br/>
        /// </summary>
        private sealed class LegacyPathAggregateAccumulator
        {
            private readonly LegacyPathAggregateKind kind;
            private ulong count;
            private decimal sum;
            private decimal mean;
            private object? extreme;

            /// <summary>
            /// Creates one accumulator for the requested legacy operation.<br/>
            /// </summary>
            /// <param name="kind">The aggregate operation to perform.<br/></param>
            internal LegacyPathAggregateAccumulator(LegacyPathAggregateKind kind)
            {
                this.kind = kind;
            }

            /// <summary>
            /// Adds one non-null scalar value to the selected aggregate.<br/>
            /// Extrema retain the original CLR value; numeric operations reject nonnumeric values and use checked Decimal arithmetic.<br/>
            /// </summary>
            /// <param name="value">The candidate scalar value.<br/></param>
            internal void Add(object? value)
            {
                if (value is null)
                    return;

                if (kind is LegacyPathAggregateKind.Minimum or LegacyPathAggregateKind.Maximum)
                {
                    if (extreme is null ||
                        (kind == LegacyPathAggregateKind.Minimum && CompareLibraDexSortKey(value, extreme) < 0) ||
                        (kind == LegacyPathAggregateKind.Maximum && CompareLibraDexSortKey(value, extreme) > 0))
                    {
                        extreme = value;
                    }
                    return;
                }

                AddNumeric(ToLegacyAggregateDecimal(value));
            }

            /// <summary>
            /// Adds a typed-reader numeric value without boxing its source key.<br/>
            /// Shares checked accumulation and incremental-average semantics with the compatibility path.<br/>
            /// </summary>
            /// <param name="numeric">Checked Decimal conversion of one non-null key.<br/></param>
            internal void AddNumeric(decimal numeric)
            {
                count = checked(count + 1);
                if (kind == LegacyPathAggregateKind.Sum)
                {
                    sum = checked(sum + numeric);
                    return;
                }

                mean = checked(mean + ((numeric - mean) / count));
            }

            /// <summary>
            /// Completes the aggregate under the original public return contract.<br/>
            /// Empty numeric input returns UInt64 zero, empty extrema return null, and negative or oversized numeric results throw <see cref="OverflowException"/>.<br/>
            /// </summary>
            /// <returns>The boxed completed aggregate result.<br/></returns>
            internal object? Complete()
            {
                if (kind is LegacyPathAggregateKind.Minimum or LegacyPathAggregateKind.Maximum)
                    return extreme;
                if (count == 0)
                    return 0UL;

                decimal numeric = kind == LegacyPathAggregateKind.Sum ? sum : mean;
                decimal truncated = decimal.Truncate(numeric);
                if (truncated < 0 || truncated > ulong.MaxValue)
                    throw new OverflowException($"The {kind} result '{numeric}' cannot be represented as UInt64.");
                return decimal.ToUInt64(truncated);
            }

            /// <summary>
            /// Converts one supported CLR numeric scalar to Decimal with checked range behavior.<br/>
            /// This preserves exact 64-bit integral inputs and rejects NaN, infinity, unsupported values, and values outside Decimal range.<br/>
            /// </summary>
            /// <param name="value">The scalar value to convert.<br/></param>
            /// <returns>The checked Decimal representation.<br/></returns>
            internal static decimal ToLegacyAggregateDecimal(object value)
            {
                return value switch
                {
                    byte number => number,
                    sbyte number => number,
                    short number => number,
                    ushort number => number,
                    int number => number,
                    uint number => number,
                    long number => number,
                    ulong number => number,
                    Half number => checked((decimal)(float)number),
                    float number => checked((decimal)number),
                    double number => checked((decimal)number),
                    decimal number => number,
                    Int128 number => checked((decimal)number),
                    UInt128 number => checked((decimal)number),
                    System.Numerics.BigInteger number => checked((decimal)number),
                    _ when value.GetType().IsEnum => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
                    _ => throw new InvalidOperationException(
                        $"Legacy {nameof(Sum)}/{nameof(Average)} property-path aggregates require a numeric scalar, but '{value.GetType().FullName}' was read.")
                };
            }
        }

        /// <summary>
        /// Resolves one simple property path exclusively from the store's already registered LibraDex descriptors.<br/>
        /// </summary>
        /// <param name="propPath">Simple Abraxas property path to resolve.<br/></param>
        /// <param name="index">Small store-owned record-index handle when a LibraDex descriptor is registered.<br/></param>
        /// <returns><see langword="true"/> when an exact registered LibraDex path was found; otherwise <see langword="false"/>.<br/></returns>
        private bool TryGetRegisteredLibraDexRecordIndex(string propPath, out RecordIndex index)
        {
            index = default;
            var registered = libraDexSimpleIndexes;
            for (var i = 0; i < registered.Length; i++)
            {
                var entry = registered[i];
                if (!string.Equals(entry.Path, propPath, StringComparison.Ordinal))
                    continue;

                index = new RecordIndex(entry.Path, entry.Path, entry.Unique, this);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts one legacy-shaped grouped aggregate entirely through compatible LibraDex simple indexes.<br/>
        /// The grouping index supplies keys and identities, the optional aggregate index supplies values, and a fully translatable condition restricts both streams inside one identity group.<br/>
        /// Missing or null grouping keys cause an explicit fallback because ordered indexes may omit those identities and must never silently produce incomplete groups.<br/>
        /// </summary>
        /// <param name="aggregate">Grouped aggregate operation to execute.<br/></param>
        /// <param name="aggregatePath">Normalized aggregate property path for non-count operations.<br/></param>
        /// <param name="where">Optional condition that must translate completely into the selected LibraDex identity group.<br/></param>
        /// <param name="name">Optional result and group label.<br/></param>
        /// <param name="groupKeys">Exactly one simple grouping selector for the indexed path.<br/></param>
        /// <param name="result">Materialized indexed groups when this method succeeds.<br/></param>
        /// <returns><see langword="true"/> when LibraDex executed the complete grouped aggregate; otherwise <see langword="false"/> for authoritative-payload fallback.<br/></returns>
        private bool TryMaterializeAggregateGroupingsByLibraDex(
            AggregateFunction aggregate,
            string? aggregatePath,
            Condition<T>.EndCondition? where,
            string? name,
            ISelector[] groupKeys,
            out GroupedRecords result)
        {
            using var lifecycle = EnterStoreOperationScope();
            result = default!;
            if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
                groupKeys.Length != 1 ||
                !TryResolveSimpleSelectorPath(groupKeys[0], out PropPath groupingPath, out _) ||
                !TryGetOrCreateAutoLibraDexIndex(groupingPath.Path, AutoIndexUse.Distinct, out RecordIndex groupingRecordIndex) ||
                !TryGetLibraDexIndexGroup(groupingRecordIndex, out CatalogIdentityGroupIndexes group, out IIndex groupingIndex))
            {
                return false;
            }

            LibraDexConditionEndCondition condition;
            if (where is null)
            {
                condition = CreateLibraDexAllCondition(group, groupingIndex);
            }
            else if (!where.builder.TryBuildLibraDexConditionTree(
                group,
                HasLibraDexIndexInBoundGroup,
                TryResolveLibraDexDerivedSelector,
                out condition))
            {
                return false;
            }

            IIndex? aggregateIndex = null;
            if (aggregate != AggregateFunction.Count)
            {
                if (aggregatePath is null ||
                    !TryGetOrCreateAutoLibraDexIndex(aggregatePath, AutoIndexUse.Distinct, out RecordIndex aggregateRecordIndex) ||
                    !TryGetLibraDexIndexGroup(aggregateRecordIndex, out CatalogIdentityGroupIndexes aggregateGroup, out aggregateIndex) ||
                    !ReferenceEquals(aggregateGroup, group))
                {
                    return false;
                }
            }

            var valuesByIdentity = new Dictionary<ulong, object?>();
            if (aggregateIndex is not null)
            {
                foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(aggregateIndex, condition, QueryDirection.Ascending))
                    valuesByIdentity[ToRecordId(tuple.Identity)] = tuple.Key;
            }

            var buckets = new Dictionary<object?[], DirectGroupingAccumulator>(DirectGroupingKeyComparer.Instance);
            var ordered = new List<DirectGroupingAccumulator>();
            ulong groupedIdentityCount = 0;
            foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(groupingIndex, condition, QueryDirection.Ascending))
            {
                ulong recordID = ToRecordId(tuple.Identity);
                object?[] keyValues = [tuple.Key];
                if (!buckets.TryGetValue(keyValues, out DirectGroupingAccumulator? bucket))
                {
                    bucket = new DirectGroupingAccumulator(keyValues, aggregate);
                    buckets.Add(keyValues, bucket);
                    ordered.Add(bucket);
                }

                object? value = null;
                bool valueExists = aggregateIndex is not null && valuesByIdentity.TryGetValue(recordID, out value);
                bucket.AddRow(valueExists, value);
                groupedIdentityCount = checked(groupedIdentityCount + 1);
            }

            ulong selectedIdentityCount = where is null
                ? Count
                : CountLibraDexConditionIdentities(group, condition);
            if (groupedIdentityCount != selectedIdentityCount)
                return false;

            string[] paths = [groupingPath.Path];
            var groups = new List<GroupInfo>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
                groups.Add(CreateSemanticGroupingInfo(groupKeys, paths, ordered[i], name));
            result = new GroupedRecords(groups, where, name);
            return true;
        }

        /// <summary>
        /// Converts developer-facing Abraxas culture profiles into the exact LibraDex projection contracts used during physical index creation.<br/>
        /// A null request remains null so the legacy single-sort-key shorthand can retain its compatibility semantics.<br/>
        /// </summary>
        /// <param name="profiles">Optional Abraxas culture profiles supplied by the developer.<br/></param>
        /// <returns>The equivalent LibraDex profiles, or <c>null</c> when no explicit profile list was supplied.<br/></returns>
        private static IReadOnlyList<LibraDexStringSortKeyProfile>? ToLibraDexSortKeyProfiles(
            IReadOnlyList<TextSortProfile>? profiles)
        {
            if (profiles is null)
                return null;

            var converted = new LibraDexStringSortKeyProfile[profiles.Count];
            for (var i = 0; i < profiles.Count; i++)
            {
                TextSortProfile profile = profiles[i];
                converted[i] = new LibraDexStringSortKeyProfile(profile.CultureName, profile.CompareOptions);
            }
            return converted;
        }

        /// <summary>
        /// Expands an existing compatible text index when durable Abraxas intent contains a strict append-only superset of its persisted culture profiles.<br/>
        /// This is the interrupted-operation recovery lane: direction and every non-sort-key projection must already match, while each persisted profile must remain the same ordered prefix.<br/>
        /// </summary>
        /// <param name="indexes">The owning LibraDex index set.<br/></param>
        /// <param name="propPath">The logical text-index property path.<br/></param>
        /// <param name="requestedKeys">The complete requested string projection flags.<br/></param>
        /// <param name="requestedDirections">The complete requested projection directions.<br/></param>
        /// <param name="requestedProfiles">The durable requested culture profiles.<br/></param>
        /// <param name="persisted">The current disconnected LibraDex metadata.<br/></param>
        /// <returns><c>true</c> when one or more missing profiles were added; otherwise <c>false</c>.<br/></returns>
        private static bool TryAddMissingTextSortProfiles(
            CatalogIdentityGroupIndexes indexes,
            string propPath,
            StringKeys requestedKeys,
            LibraDexProjectionDirectionSet requestedDirections,
            IReadOnlyList<TextSortProfile>? requestedProfiles,
            CatalogIndexInfo persisted)
        {
            if (requestedProfiles is not { Count: > 0 } ||
                persisted.Directions != requestedDirections ||
                (persisted.StringKeys | StringKeys.SortKey) != requestedKeys)
            {
                return false;
            }

            int persistedCount = persisted.SortKeyProfiles?.Count ?? 0;
            if (persistedCount >= requestedProfiles.Count)
                return false;
            for (var i = 0; i < persistedCount; i++)
            {
                TextSortProfile requested = CanonicalTextSortProfile(requestedProfiles[i]);
                LibraDexStringSortKeyProjectionInfo current = persisted.SortKeyProfiles![i];
                if (!string.Equals(requested.CultureName ?? string.Empty, current.CultureName, StringComparison.Ordinal) ||
                    requested.CompareOptions != current.CompareOptions)
                {
                    return false;
                }
            }

            CatalogNamedStringKeyBuilder builder = indexes.Define(propPath).StringKeys();
            for (var i = persistedCount; i < requestedProfiles.Count; i++)
            {
                TextSortProfile requested = CanonicalTextSortProfile(requestedProfiles[i]);
                using LibraDexStringScalar8Index added = builder.AddSortKeyProfile(
                    new LibraDexStringSortKeyProfile(requested.CultureName, requested.CompareOptions));
            }
            return true;
        }

        /// <summary>
        /// Canonicalizes one developer-facing text sort profile through the .NET culture registry and validates its comparison options.<br/>
        /// </summary>
        /// <param name="profile">The profile to canonicalize.<br/></param>
        /// <returns>The invariant-or-named canonical profile.<br/></returns>
        private static TextSortProfile CanonicalTextSortProfile(TextSortProfile profile)
        {
            CultureInfo culture = string.IsNullOrEmpty(profile.CultureName)
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(profile.CultureName);
            _ = culture.CompareInfo.GetSortKey(string.Empty, profile.CompareOptions);
            return new TextSortProfile(
                culture.Equals(CultureInfo.InvariantCulture) ? string.Empty : culture.Name,
                profile.CompareOptions);
        }

        /// <summary>
        /// Converts persisted LibraDex projection metadata into Abraxas culture-profile intent for catalog discovery and reattachment.<br/>
        /// Persisted sort-version fields remain LibraDex-owned compatibility evidence and are deliberately not duplicated in the developer contract.<br/>
        /// </summary>
        /// <param name="profiles">Persisted LibraDex culture projection metadata.<br/></param>
        /// <returns>The equivalent Abraxas profiles, or <c>null</c> when the index owns no culture projections.<br/></returns>
        private static IReadOnlyList<TextSortProfile>? ToAbraxasSortKeyProfiles(
            IReadOnlyList<LibraDexStringSortKeyProjectionInfo>? profiles)
        {
            if (profiles is not { Count: > 0 })
                return null;

            var converted = new TextSortProfile[profiles.Count];
            for (var i = 0; i < profiles.Count; i++)
            {
                LibraDexStringSortKeyProjectionInfo profile = profiles[i];
                converted[i] = new TextSortProfile(profile.CultureName, profile.CompareOptions);
            }
            return converted;
        }

        /// <summary>
        /// Resolves modern multi-profile metadata and the legacy single sort-key fields into one canonical Abraxas contract array.<br/>
        /// </summary>
        /// <param name="info">The persisted LibraDex text-index metadata.<br/></param>
        /// <returns>Every persisted sort-key profile in physical ordinal order.<br/></returns>
        private static TextSortProfile[] ResolveAbraxasPersistedSortProfiles(CatalogIndexInfo info)
        {
            if (info.SortKeyProfiles is { Count: > 0 } profiles)
                return ToAbraxasSortKeyProfiles(profiles)!.ToArray();
            if (info.SortKeyProjectionSlotIndex < 0)
                return Array.Empty<TextSortProfile>();

            return [new TextSortProfile(info.SortKeyCulture, CompareOptions.IgnoreCase)];
        }

        /// <summary>
        /// Tests whether an explicit Abraxas culture-profile request matches every persisted LibraDex projection in the same declared order.<br/>
        /// A null or empty request means the caller used compatibility defaults and therefore does not impose a new explicit profile-list contract.<br/>
        /// Durable ordinary-index intents normalize an omitted list to an empty array, while LibraDex materializes its default sort-key projection explicitly; treating both caller shapes as unspecified preserves the creation contract across reattachment.<br/>
        /// </summary>
        /// <param name="requested">Optional explicit developer profile list.<br/></param>
        /// <param name="persisted">Persisted LibraDex culture projection metadata.<br/></param>
        /// <returns><c>true</c> when no explicit list was requested or every requested profile matches exactly.<br/></returns>
        private static bool TextSortProfilesMatch(
            IReadOnlyList<TextSortProfile>? requested,
            IReadOnlyList<LibraDexStringSortKeyProjectionInfo>? persisted)
        {
            if (requested is null || requested.Count == 0)
                return true;
            if (requested.Count != (persisted?.Count ?? 0))
                return false;

            for (var i = 0; i < requested.Count; i++)
            {
                TextSortProfile left = requested[i];
                LibraDexStringSortKeyProjectionInfo right = persisted![i];
                CultureInfo culture = string.IsNullOrEmpty(left.CultureName)
                    ? CultureInfo.InvariantCulture
                    : CultureInfo.GetCultureInfo(left.CultureName);
                string cultureName = culture.Equals(CultureInfo.InvariantCulture) ? string.Empty : culture.Name;
                if (!string.Equals(cultureName, right.CultureName, StringComparison.Ordinal) ||
                    left.CompareOptions != right.CompareOptions)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Attempts to populate one newly created ordinary index through a bounded, typed, locality-aware LibraDex batch path.<br/>
        /// Directly serialized strings, built-in value scalars, and calculated strings are admitted; calculated non-string selectors, custom binary codecs, and variable binary keys retain the legacy fully prepared path until they have equally strict typed streaming contracts.<br/>
        /// Each authoritative Fractal record remains live only while one Inheto view extracts its key, after which only a compact tuple survives in the bounded buffer.<br/>
        /// </summary>
        /// <param name="entry">New physical index descriptor receiving the authoritative population.<br/></param>
        /// <returns><see langword="true"/> when optimized population handled the key family; otherwise <see langword="false"/> so the caller can use the legacy path.<br/></returns>
        private bool TryPopulateLibraDexSimpleIndexOptimized(LibraDexSimpleIndex entry)
        {
            if (entry.CustomKeyReader is not null ||
                entry.CustomPayloadReader is not null ||
                entry.KeyType == typeof(byte[]))
            {
                return false;
            }

            Type type = entry.KeyType;
            if (entry.CalculatedReader is not null)
                return type == typeof(string) && PopulateLibraDexCalculatedStringIndexOptimized(entry);
            if (type == typeof(string)) return PopulateLibraDexStringIndexOptimized(entry);
            if (type == typeof(bool)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadBoolean(path));
            if (type == typeof(byte)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadByte(path));
            if (type == typeof(sbyte)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadSByte(path));
            if (type == typeof(short)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadInt16(path));
            if (type == typeof(ushort)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadUInt16(path));
            if (type == typeof(char)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadChar(path));
            if (type == typeof(int)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadInt32(path));
            if (type == typeof(uint)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadUInt32(path));
            if (type == typeof(long)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadInt64(path));
            if (type == typeof(ulong)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadUInt64(path));
            if (type == typeof(Int128)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadInt128(path));
            if (type == typeof(UInt128)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadUInt128(path));
            if (type == typeof(float)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadSingle(path));
            if (type == typeof(double)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadDouble(path));
            if (type == typeof(decimal)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadDecimal(path));
            if (type == typeof(Guid)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadGuid(path));
            if (type == typeof(DateTime)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadDateTime(path));
            if (type == typeof(DateTimeOffset)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadDateTimeOffset(path));
            if (type == typeof(DateOnly)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadDateOnly(path));
            if (type == typeof(TimeOnly)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadTimeOnly(path));
            if (type == typeof(TimeSpan)) return PopulateLibraDexScalarIndexOptimized(entry, static (binary, path) => binary.ReadTimeSpan(path));
            return false;
        }

        /// <summary>
        /// Streams one directly serialized value-type key family into a newly created LibraDex index using bounded sorted chunks and typed insertion.<br/>
        /// Authoritative payloads are visited in physical Fractal order through one pooled contiguous read window; index tuple sorting supplies the logical key order independently.<br/>
        /// Unique non-null keys retain one typed hash-set entry for complete-source validation; non-unique populations retain only the configured chunk.<br/>
        /// Tuple chunks are released after typed insertion, while sorted order feeds LibraDex's established adaptive routing and durability machinery.<br/>
        /// A later failure is safe because the caller drops the entire newly created physical definition.<br/>
        /// </summary>
        /// <typeparam name="TKey">Supported non-nullable LibraDex scalar key type.<br/></typeparam>
        /// <param name="entry">New physical scalar index descriptor.<br/></param>
        /// <param name="readKey">Typed Inheto property reader selected once before traversal.<br/></param>
        /// <returns>Always <see langword="true"/> after the supported family completes successfully.<br/></returns>
        private bool PopulateLibraDexScalarIndexOptimized<TKey>(
            LibraDexSimpleIndex entry,
            Func<InhetoBinary, string, TKey?> readKey)
            where TKey : struct
        {
            int batchSize = IndexBackfill.GetValidatedBatchSize();
            bool sortWithinBatch = IndexBackfill.SortWithinBatch;
            var tuples = new List<(ulong RecordID, TKey? Key)>(batchSize);
            HashSet<TKey>? uniqueKeys = entry.Unique ? new HashSet<TKey>() : null;
            LibraDexIndex<TKey, ulong> index = RequireTypedScalarIndex<TKey>(entry);
            long extractionTicks = 0;
            long sortTicks = 0;
            long insertTicks = 0;
            long publishTicks = 0;
            int recordsRead = 0;
            int tupleCount = 0;
            int peakBuffered = 0;
            int publishedBatches = 0;

            void Flush()
            {
                if (tuples.Count == 0)
                    return;

                if (sortWithinBatch && tuples.Count > 1)
                {
                    long started = Stopwatch.GetTimestamp();
                    tuples.Sort(static (left, right) =>
                    {
                        int keyOrder = CompareNullableBackfillKeys(left.Key, right.Key);
                        return keyOrder != 0 ? keyOrder : left.RecordID.CompareTo(right.RecordID);
                    });
                    sortTicks += Stopwatch.GetTimestamp() - started;
                }

                CatalogIdentityGroupBatchManager batch = GetLibraDexBackfillBatch(entry);
                if (!IndexBackfill.UseIdentityGroupBatch)
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (var i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key.HasValue
                            ? index.Insert(tuple.Key.Value, tuple.RecordID)
                            : entry.Index.Insert(null, tuple.RecordID);
                        if (!result.Inserted)
                            EnsureLibraDexInsertAccepted(entry, tuple.Key, tuple.RecordID, result);
                    }
                    insertTicks += Stopwatch.GetTimestamp() - insertStarted;
                    tuples.Clear();
                    return;
                }
                if (batch.IsEnabled)
                    throw new InvalidOperationException($"Index '{entry.Path}' cannot start optimized population while its LibraDex identity-group batch is already enabled.");
                batch.Enable(new LibraDexWriteIntent(
                    sortWithinBatch ? LibraDexWriteOrder.Sorted : LibraDexWriteOrder.Default,
                    LibraDexWriteVolume.Thousands,
                    LibraDexWriteLocality.Broad,
                    LibraDexWritePriority.WriteSpeed));
                try
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (var i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key.HasValue
                            ? index.Insert(tuple.Key.Value, tuple.RecordID)
                            : entry.Index.Insert(null, tuple.RecordID);
                        if (!result.Inserted)
                            EnsureLibraDexInsertAccepted(entry, tuple.Key, tuple.RecordID, result);
                    }
                    insertTicks += Stopwatch.GetTimestamp() - insertStarted;

                    long publishStarted = Stopwatch.GetTimestamp();
                    _ = batch.PublishAndDisable();
                    publishTicks += Stopwatch.GetTimestamp() - publishStarted;
                    publishedBatches++;
                }
                catch
                {
                    if (batch.IsEnabled)
                        _ = batch.AbortAndDisable();
                    throw;
                }

                tuples.Clear();
            }

            using var records = rbase.data.OpenGroupBulkRecordReader(storeID);
            while (records.Read())
            {
                ulong recordID = records.ID;
                long extractionStarted = Stopwatch.GetTimestamp();
                TKey? key;
                try
                {
                    InhetoBinary binary = CreateRecordBinary(recordID, records.Data);
                    binary.SetMaterializationContext(recordID);
                    key = readKey(binary, entry.Path);
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Index '{entry.Path}' could not read authoritative record '{recordID}' during optimized population.",
                        entry.Index.Name,
                        entry.Path,
                        recordID,
                        exception);
                }
                extractionTicks += Stopwatch.GetTimestamp() - extractionStarted;
                recordsRead++;

                if (!key.HasValue && !entry.IncludeNullValues)
                    continue;
                if (key.HasValue && uniqueKeys is not null && !uniqueKeys.Add(key.Value))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.UniqueConstraint,
                        $"Unique index '{entry.Path}' found the same non-null key on more than one existing record.",
                        entry.Index.Name,
                        entry.Path,
                        recordID);
                }

                tuples.Add((recordID, key));
                tupleCount++;
                peakBuffered = Math.Max(peakBuffered, tuples.Count);
                if (tuples.Count >= batchSize)
                    Flush();
            }
            Flush();

            LastIndexBackfillDiagnostics = new IndexBackfillDiagnostics(
                IndexBackfill.Mode,
                IndexBackfillMode.Optimized,
                recordsRead,
                tupleCount,
                BackfillElapsed(extractionTicks),
                BackfillElapsed(sortTicks),
                BackfillElapsed(insertTicks),
                BackfillElapsed(publishTicks),
                peakBuffered,
                publishedBatches);
            return true;
        }

        /// <summary>
        /// Streams directly serialized UTF-8 keys into a newly created logical LibraDex string index without materializing CLR strings.<br/>
        /// Authoritative payloads are visited in physical Fractal order through one pooled contiguous read window.<br/>
        /// Each borrowed payload key is copied only into the bounded key chunk because the pooled Fractal window is reused before storage insertion.<br/>
        /// Unique text indexes retain structural UTF-8 keys for complete-source validation, while non-unique text indexes remain bounded to one chunk.<br/>
        /// </summary>
        /// <param name="entry">New logical string index descriptor.<br/></param>
        /// <returns>Always <see langword="true"/> after optimized string population completes successfully.<br/></returns>
        private bool PopulateLibraDexStringIndexOptimized(LibraDexSimpleIndex entry)
        {
            int batchSize = IndexBackfill.GetValidatedBatchSize();
            bool sortWithinBatch = IndexBackfill.SortWithinBatch;
            var tuples = new List<(ulong RecordID, byte[]? Key)>(batchSize);
            HashSet<byte[]>? uniqueKeys = entry.Unique
                ? new HashSet<byte[]>(LibraDexBackfillByteArrayComparer.Instance)
                : null;
            LibraDexStringScalar8Index index = RequireLibraDexStringIndex(entry);
            long extractionTicks = 0;
            long sortTicks = 0;
            long insertTicks = 0;
            long publishTicks = 0;
            int recordsRead = 0;
            int tupleCount = 0;
            int peakBuffered = 0;
            int publishedBatches = 0;

            void Flush()
            {
                if (tuples.Count == 0)
                    return;

                if (sortWithinBatch && tuples.Count > 1)
                {
                    long started = Stopwatch.GetTimestamp();
                    tuples.Sort(static (left, right) =>
                    {
                        int keyOrder = LibraDexBackfillByteArrayComparer.CompareNullable(left.Key, right.Key);
                        return keyOrder != 0 ? keyOrder : left.RecordID.CompareTo(right.RecordID);
                    });
                    sortTicks += Stopwatch.GetTimestamp() - started;
                }

                CatalogIdentityGroupBatchManager batch = GetLibraDexBackfillBatch(entry);
                if (!IndexBackfill.UseTextIdentityGroupBatch)
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (var i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key is null
                            ? index.Insert(null, tuple.RecordID)
                            : index.InsertUtf8(tuple.Key, tuple.RecordID);
                        if (!result.Inserted && !LibraDexStringTupleExists(index, tuple.Key, tuple.RecordID))
                        {
                            throw new IndexContractException(
                                entry.Unique && tuple.Key is not null
                                    ? IndexContractFailureKind.UniqueConstraint
                                    : IndexContractFailureKind.BackendMutation,
                                $"Index '{entry.Path}' rejected a validated optimized string tuple.",
                                entry.Index.Name,
                                entry.Path,
                                tuple.RecordID);
                        }
                    }
                    insertTicks += Stopwatch.GetTimestamp() - insertStarted;
                    tuples.Clear();
                    return;
                }
                if (batch.IsEnabled)
                    throw new InvalidOperationException($"Index '{entry.Path}' cannot start optimized population while its LibraDex identity-group batch is already enabled.");
                batch.Enable(new LibraDexWriteIntent(
                    sortWithinBatch ? LibraDexWriteOrder.Sorted : LibraDexWriteOrder.Default,
                    LibraDexWriteVolume.Thousands,
                    LibraDexWriteLocality.Broad,
                    LibraDexWritePriority.WriteSpeed));
                try
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (var i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = tuple.Key is null
                            ? index.Insert(null, tuple.RecordID)
                            : index.InsertUtf8(tuple.Key, tuple.RecordID);
                        if (!result.Inserted && !LibraDexStringTupleExists(index, tuple.Key, tuple.RecordID))
                        {
                            throw new IndexContractException(
                                entry.Unique && tuple.Key is not null
                                    ? IndexContractFailureKind.UniqueConstraint
                                    : IndexContractFailureKind.BackendMutation,
                                $"Index '{entry.Path}' rejected a validated optimized string tuple.",
                                entry.Index.Name,
                                entry.Path,
                                tuple.RecordID);
                        }
                    }
                    insertTicks += Stopwatch.GetTimestamp() - insertStarted;

                    long publishStarted = Stopwatch.GetTimestamp();
                    _ = batch.PublishAndDisable();
                    publishTicks += Stopwatch.GetTimestamp() - publishStarted;
                    publishedBatches++;
                }
                catch
                {
                    if (batch.IsEnabled)
                        _ = batch.AbortAndDisable();
                    throw;
                }

                tuples.Clear();
            }

            using var records = rbase.data.OpenGroupBulkRecordReader(storeID);
            while (records.Read())
            {
                ulong recordID = records.ID;
                long extractionStarted = Stopwatch.GetTimestamp();
                byte[]? key;
                try
                {
                    InhetoBinary binary = CreateRecordBinary(recordID, records.Data);
                    binary.SetMaterializationContext(recordID);
                    key = binary.TryReadStringUtf8Payload(entry.Path, out ReadOnlyMemory<byte> utf8)
                        ? utf8.ToArray()
                        : null;
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Index '{entry.Path}' could not read authoritative record '{recordID}' during optimized string population.",
                        entry.Index.Name,
                        entry.Path,
                        recordID,
                        exception);
                }
                extractionTicks += Stopwatch.GetTimestamp() - extractionStarted;
                recordsRead++;

                if (key is null && !entry.IncludeNullValues)
                    continue;
                if (key is { Length: 0 } && !entry.IncludeEmptyStrings)
                    continue;
                if (key is not null && uniqueKeys is not null && !uniqueKeys.Add(key))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.UniqueConstraint,
                        $"Unique index '{entry.Path}' found the same non-null text key on more than one existing record.",
                        entry.Index.Name,
                        entry.Path,
                        recordID);
                }

                tuples.Add((recordID, key));
                tupleCount++;
                peakBuffered = Math.Max(peakBuffered, tuples.Count);
                if (tuples.Count >= batchSize)
                    Flush();
            }
            Flush();

            LastIndexBackfillDiagnostics = new IndexBackfillDiagnostics(
                IndexBackfill.Mode,
                IndexBackfillMode.Optimized,
                recordsRead,
                tupleCount,
                BackfillElapsed(extractionTicks),
                BackfillElapsed(sortTicks),
                BackfillElapsed(insertTicks),
                BackfillElapsed(publishTicks),
                peakBuffered,
                publishedBatches);
            return true;
        }

        /// <summary>
        /// Populates one newly created calculated string index without returning to the legacy prepare-all and per-tuple publication path.<br/>
        /// Populations no larger than the configured batch bound use LibraDex's native unordered <c>VS8</c> replacement for forward exact-only and exact-plus-folded profiles; each distinct string representation is encoded once and each maintained projection publishes one packed root.<br/>
        /// Larger populations and other projected profiles retain bounded sorted identity-group batches.<br/>
        /// One physical-order Fractal bulk reader supplies each identity and borrowed payload together through a pooled contiguous read window.<br/>
        /// Abraxas owns the authoritative monotonic identity enumeration, so native construction skips a redundant identity-deduplication set while uniqueness remains a key-value contract.<br/>
        /// </summary>
        /// <param name="entry">New calculated string index descriptor receiving the authoritative population.<br/></param>
        /// <returns>Always <see langword="true"/> after the calculated string family completes successfully.<br/></returns>
        private bool PopulateLibraDexCalculatedStringIndexOptimized(LibraDexSimpleIndex entry)
        {
            int batchSize = IndexBackfill.GetValidatedBatchSize();
            bool sortWithinBatch = IndexBackfill.SortWithinBatch;
            var tuples = new List<(string? Key, ulong Identity)>(batchSize);
            HashSet<string>? uniqueKeys = entry.Unique ? new HashSet<string>(StringComparer.Ordinal) : null;
            LibraDexStringScalar8Index index = RequireLibraDexStringIndex(entry);
            LibraDexIndexShapeSpec shape = index.LogicalShape;
            bool nativeExact = shape.StringKeys == StringKeys.Exact && shape.Projections.Count == 1;
            bool nativeExactAndFolded =
                shape.StringKeys is StringKeys.ExactAndFolded or StringKeys.ExactFoldedAndSortKey &&
                shape.Projections.Count >= 2;
            bool native = IndexBackfill.UseNativeSortedBuild &&
                shape.Directions == LibraDexProjectionDirectionSet.Forward &&
                (nativeExact || nativeExactAndFolded);
            bool nativeUsed = false;
            long extractionTicks = 0;
            long sortTicks = 0;
            long insertTicks = 0;
            long publishTicks = 0;
            int recordsRead = 0;
            int tupleCount = 0;
            int peakBuffered = 0;
            int publishedBatches = 0;

            void Flush()
            {
                if (tuples.Count == 0)
                    return;

                if (sortWithinBatch && tuples.Count > 1)
                {
                    long started = Stopwatch.GetTimestamp();
                    tuples.Sort(static (left, right) =>
                    {
                        int keyOrder;
                        if (ReferenceEquals(left.Key, right.Key))
                            keyOrder = 0;
                        else if (left.Key is null)
                            keyOrder = -1;
                        else if (right.Key is null)
                            keyOrder = 1;
                        else
                            keyOrder = StringComparer.Ordinal.Compare(left.Key, right.Key);
                        return keyOrder != 0 ? keyOrder : left.Identity.CompareTo(right.Identity);
                    });
                    sortTicks += Stopwatch.GetTimestamp() - started;
                }

                CatalogIdentityGroupBatchManager batch = GetLibraDexBackfillBatch(entry);
                if (!IndexBackfill.UseTextIdentityGroupBatch)
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (var i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = index.Insert(tuple.Key, tuple.Identity);
                        if (!result.Inserted)
                            EnsureLibraDexInsertAccepted(entry, tuple.Key, tuple.Identity, result);
                    }
                    insertTicks += Stopwatch.GetTimestamp() - insertStarted;
                    tuples.Clear();
                    return;
                }
                if (batch.IsEnabled)
                    throw new InvalidOperationException($"Index '{entry.Path}' cannot start calculated string population while its LibraDex identity-group batch is already enabled.");
                batch.Enable(new LibraDexWriteIntent(
                    sortWithinBatch ? LibraDexWriteOrder.Sorted : LibraDexWriteOrder.Default,
                    LibraDexWriteVolume.Thousands,
                    LibraDexWriteLocality.Broad,
                    LibraDexWritePriority.WriteSpeed));
                try
                {
                    long insertStarted = Stopwatch.GetTimestamp();
                    for (var i = 0; i < tuples.Count; i++)
                    {
                        var tuple = tuples[i];
                        LibraDexGenericInsertResult result = index.Insert(tuple.Key, tuple.Identity);
                        if (!result.Inserted)
                            EnsureLibraDexInsertAccepted(entry, tuple.Key, tuple.Identity, result);
                    }
                    insertTicks += Stopwatch.GetTimestamp() - insertStarted;

                    long publishStarted = Stopwatch.GetTimestamp();
                    _ = batch.PublishAndDisable();
                    publishTicks += Stopwatch.GetTimestamp() - publishStarted;
                    publishedBatches++;
                }
                catch
                {
                    if (batch.IsEnabled)
                        _ = batch.AbortAndDisable();
                    throw;
                }

                tuples.Clear();
            }

            using var records = rbase.data.OpenGroupBulkRecordReader(storeID);
            while (true)
            {
                long extractionStarted = Stopwatch.GetTimestamp();
                ulong recordID = 0;
                string? key = null;
                bool hasKey;
                try
                {
                    if (!records.Read())
                    {
                        extractionTicks += Stopwatch.GetTimestamp() - extractionStarted;
                        break;
                    }
                    recordID = records.ID;
                    InhetoBinary binary = CreateRecordBinary(recordID, records.Data);
                    binary.SetMaterializationContext(recordID);
                    hasKey = TryReadLibraDexKey(binary, entry, out object calculatedKey, strict: true);
                    if (hasKey && calculatedKey is not null && calculatedKey is not string)
                    {
                        throw new InvalidDataException(
                            $"Calculated string index '{entry.Path}' produced runtime key type '{calculatedKey.GetType().FullName}'.");
                    }
                    key = hasKey ? (string?)calculatedKey : null;
                }
                catch (Exception exception)
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.KeyRead,
                        $"Index '{entry.Path}' could not evaluate authoritative record '{recordID}' during optimized calculated string population.",
                        entry.Index.Name,
                        entry.Path,
                        recordID,
                        exception);
                }
                extractionTicks += Stopwatch.GetTimestamp() - extractionStarted;
                recordsRead++;
                if (!hasKey)
                    continue;

                if (key is not null && uniqueKeys is not null && !uniqueKeys.Add(key))
                {
                    throw new IndexContractException(
                        IndexContractFailureKind.UniqueConstraint,
                        $"Unique calculated index '{entry.Path}' found the same non-null text key on more than one existing record.",
                        entry.Index.Name,
                        entry.Path,
                        recordID);
                }

                if (native && tuples.Count >= batchSize)
                {
                    native = false;
                    Flush();
                }
                tuples.Add((key, recordID));
                tupleCount++;
                peakBuffered = Math.Max(peakBuffered, tuples.Count);
                if (!native && tuples.Count >= batchSize)
                    Flush();
            }

            if (native && tuples.Count != 0)
            {
                long started = Stopwatch.GetTimestamp();
                long replaced = nativeExactAndFolded
                    ? index.ReplaceExactFoldedAndSortKeysFromUnordered(
                        tuples,
                        CancellationToken.None,
                        identityMultiplicityAlreadyValidated: true)
                    : index.ReplaceExactFromUnordered(
                        tuples,
                        CancellationToken.None,
                        identityMultiplicityAlreadyValidated: true);
                insertTicks += Stopwatch.GetTimestamp() - started;
                if (replaced != tuples.Count)
                    throw new InvalidDataException($"Index '{entry.Path}' natively replaced {replaced:N0} tuples instead of {tuples.Count:N0} authoritative tuples.");
                publishedBatches = 1;
                nativeUsed = true;
                tuples.Clear();
            }
            else
            {
                Flush();
            }

            LastIndexBackfillDiagnostics = new IndexBackfillDiagnostics(
                IndexBackfill.Mode,
                IndexBackfillMode.Optimized,
                recordsRead,
                tupleCount,
                BackfillElapsed(extractionTicks),
                BackfillElapsed(sortTicks),
                BackfillElapsed(insertTicks),
                BackfillElapsed(publishTicks),
                peakBuffered,
                publishedBatches)
            {
                NativeBuildUsed = nativeUsed
            };
            return true;
        }

        /// <summary>
        /// Resolves the identity-group batch manager that owns one persistent or volatile ordinary index.<br/>
        /// The manager shares one durability scope across the logical index and any physical projections without exposing those storage details to Abraxas callers.<br/>
        /// </summary>
        /// <param name="entry">Index descriptor whose owning group controls publication.<br/></param>
        /// <returns>The matching persistent or volatile identity-group batch manager.<br/></returns>
        private CatalogIdentityGroupBatchManager GetLibraDexBackfillBatch(LibraDexSimpleIndex entry)
        {
            if (entry.OwnerCatalog is not null)
                return entry.OwnerCatalog.IndexSet(entry.Index.Group).Batch;
            return libraDexIdentityIndexes?.Batch
                ?? throw new InvalidOperationException("Persistent LibraDex indexes are not attached to this RecordStore.");
        }

        /// <summary>
        /// Compares nullable typed backfill keys with null ordered before every concrete value.<br/>
        /// The comparison is used only inside bounded preparation chunks and therefore does not alter the persisted index's configured traversal direction.<br/>
        /// </summary>
        /// <typeparam name="TKey">Comparable CLR scalar key type.<br/></typeparam>
        /// <param name="left">First nullable key.<br/></param>
        /// <param name="right">Second nullable key.<br/></param>
        /// <returns>A standard negative, zero, or positive comparison result.<br/></returns>
        private static int CompareNullableBackfillKeys<TKey>(TKey? left, TKey? right)
            where TKey : struct
        {
            if (!left.HasValue)
                return right.HasValue ? -1 : 0;
            if (!right.HasValue)
                return 1;
            return Comparer<TKey>.Default.Compare(left.Value, right.Value);
        }

        /// <summary>
        /// Tests whether a rejected optimized string insertion already exists as the exact logical tuple.<br/>
        /// This preserves idempotent population semantics without retaining the originating Inheto payload after its Fractal record scope ends.<br/>
        /// </summary>
        /// <param name="index">Logical LibraDex string index receiving the tuple.<br/></param>
        /// <param name="key">Borrowed-and-copied UTF-8 key, or null for the logical null route.<br/></param>
        /// <param name="recordID">Expected UInt64 identity.<br/></param>
        /// <returns><see langword="true"/> when the exact tuple already exists; otherwise <see langword="false"/>.<br/></returns>
        private static bool LibraDexStringTupleExists(LibraDexStringScalar8Index index, byte[]? key, ulong recordID)
            => key is null
                ? index.Entries.Exists(null!, recordID)
                : index.ContainsTupleUtf8(key, recordID);

        /// <summary>
        /// Proves that one completed exact-string population exposes every admitted identity before Abraxas publishes its Ready intent.<br/>
        /// The validation traverses only the just-built LibraDex identity stream; it does not reread Fractal payloads and is never part of steady-state query execution.<br/>
        /// </summary>
        /// <param name="entry">Logical index definition whose population just completed.<br/></param>
        /// <param name="index">Exact VS8 physical index to validate.<br/></param>
        /// <param name="expectedTupleCount">Number of authoritative tuples admitted by null and empty-value policy.<br/></param>
        /// <param name="cancellationToken">Cancellation observed while validating the completed identity stream.<br/></param>
        private void ValidateLibraDexExactStringPopulation(
            LibraDexSimpleIndex entry,
            LibraDexStringScalar8Index index,
            int expectedTupleCount,
            CancellationToken cancellationToken)
        {
            int actualTupleCount = 0;
            HashSet<ulong> uniqueIdentities = new(Math.Max(0, expectedTupleCount));
            List<ulong> duplicateIdentitySamples = new(capacity: 8);
            foreach (ulong identity in index.IterateExactIdentities(QueryDirection.Ascending))
            {
                cancellationToken.ThrowIfCancellationRequested();
                actualTupleCount = checked(actualTupleCount + 1);
                if (!uniqueIdentities.Add(identity) && duplicateIdentitySamples.Count < duplicateIdentitySamples.Capacity)
                {
                    duplicateIdentitySamples.Add(identity);
                }
            }

            if (actualTupleCount != expectedTupleCount)
            {
                string duplicateSamples = duplicateIdentitySamples.Count == 0
                    ? "(none observed)"
                    : string.Join(", ", duplicateIdentitySamples.Select(static identity => identity.ToString("N0", CultureInfo.InvariantCulture)));
                throw new IndexContractException(
                    IndexContractFailureKind.BackendMutation,
                    $"Index '{entry.Path}' exposed {actualTupleCount:N0} identities after population instead of {expectedTupleCount:N0}; " +
                    $"Unique identities={uniqueIdentities.Count:N0}; duplicate emissions={actualTupleCount - uniqueIdentities.Count:N0}; duplicate samples={duplicateSamples}; " +
                    "its Ready intent was not published.",
                    entry.Index.Name,
                    entry.Path);
            }
        }

        /// <summary>
        /// Converts accumulated <see cref="Stopwatch"/> timestamp ticks into a stable elapsed duration.<br/>
        /// Accumulation avoids allocating or repeatedly starting stopwatch instances in the per-record extraction loop.<br/>
        /// </summary>
        /// <param name="timestampTicks">Sum of timestamp deltas measured with <see cref="Stopwatch.GetTimestamp"/>.<br/></param>
        /// <returns>The equivalent elapsed duration.<br/></returns>
        private static TimeSpan BackfillElapsed(long timestampTicks)
            => TimeSpan.FromSeconds(timestampTicks / (double)Stopwatch.Frequency);

        /// <summary>
        /// Compares retained UTF-8 backfill keys by their exact bytes and supplies matching structural hashes for unique-key validation.<br/>
        /// The comparer is scoped to bounded or uniqueness-required setup state and is not used by steady-state LibraDex traversal.<br/>
        /// </summary>
        private sealed class LibraDexBackfillByteArrayComparer : IEqualityComparer<byte[]>
        {
            /// <summary>Gets the shared stateless comparer.<br/></summary>
            internal static LibraDexBackfillByteArrayComparer Instance { get; } = new();

            /// <summary>
            /// Compares two nullable UTF-8 keys lexicographically with null ordered first.<br/>
            /// </summary>
            /// <param name="left">First key.<br/></param>
            /// <param name="right">Second key.<br/></param>
            /// <returns>A standard negative, zero, or positive comparison result.<br/></returns>
            internal static int CompareNullable(byte[]? left, byte[]? right)
            {
                if (left is null)
                    return right is null ? 0 : -1;
                if (right is null)
                    return 1;
                return left.AsSpan().SequenceCompareTo(right);
            }

            /// <summary>
            /// Determines whether two UTF-8 keys contain identical bytes.<br/>
            /// </summary>
            /// <param name="x">First key.<br/></param>
            /// <param name="y">Second key.<br/></param>
            /// <returns><see langword="true"/> when the byte sequences match exactly.<br/></returns>
            public bool Equals(byte[]? x, byte[]? y)
                => ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

            /// <summary>
            /// Returns a structural process-local hash for one UTF-8 key.<br/>
            /// </summary>
            /// <param name="value">Key bytes to hash.<br/></param>
            /// <returns>A hash code consistent with exact-byte equality.<br/></returns>
            public int GetHashCode(byte[] value)
            {
                var hash = new HashCode();
                hash.AddBytes(value);
                return hash.ToHashCode();
            }
        }

        /// <summary>
        /// Compares setup-time runtime keys using structural byte-array equality and ordinary CLR equality for every other supported scalar key.<br/>
        /// The comparer exists only for one-time unique-index population validation and is never allocated or invoked by steady-state query traversal.<br/>
        /// </summary>
        private sealed class LibraDexRuntimeKeyComparer : IEqualityComparer<object?>
        {
            /// <summary>Gets the shared stateless runtime-key comparer.<br/></summary>
            internal static LibraDexRuntimeKeyComparer Instance { get; } = new();

            /// <summary>
            /// Determines whether two runtime index keys represent the same developer-facing value.<br/>
            /// </summary>
            /// <param name="x">The first runtime key.<br/></param>
            /// <param name="y">The second runtime key.<br/></param>
            /// <returns><c>true</c> when both values are equal, using structural equality for byte arrays.<br/></returns>
            public new bool Equals(object? x, object? y)
            {
                if (ReferenceEquals(x, y))
                    return true;
                if (x is byte[] left && y is byte[] right)
                    return left.AsSpan().SequenceEqual(right);
                return object.Equals(x, y);
            }

            /// <summary>
            /// Returns a hash code consistent with <see cref="Equals(object?, object?)"/>.<br/>
            /// </summary>
            /// <param name="value">The runtime key whose hash is requested.<br/></param>
            /// <returns>A process-local hash suitable for setup-time duplicate detection.<br/></returns>
            public int GetHashCode(object? value)
            {
                if (value is not byte[] bytes)
                    return value?.GetHashCode() ?? 0;

                var hash = new HashCode();
                hash.AddBytes(bytes);
                return hash.ToHashCode();
            }
        }
    }

    /// <summary>
    /// Selects the implementation used to populate a newly created physical index from authoritative Fractal records.<br/>
    /// Existing-index repair retains the legacy atomic preparation path until replacement-index swapping is available.<br/>
    /// </summary>
    public enum IndexBackfillMode
    {
        /// <summary>Uses bounded typed chunks, borrowed Inheto reads, and LibraDex identity-group batching when the key family supports that contract.<br/></summary>
        Optimized = 0,

        /// <summary>Uses the compatibility path that materializes and validates the complete tuple set before mutating the index.<br/></summary>
        Legacy = 1
    }

    /// <summary>
    /// Configures newly created physical-index population without changing ordinary mutation semantics.<br/>
    /// Optimized grouped builds share one aggregate retained-tuple byte budget and keep a separate per-target tuple-count ceiling; a unique compatibility index may additionally retain complete uniqueness state.<br/>
    /// </summary>
    public sealed class IndexBackfillOptions
    {
        private const int DefaultMaximumSortRunTupleCount = 1_000_000;
        private const int MaximumSortRunTupleCountLimit = 100_000_000;
        private const long MinimumAutomaticMemoryBudgetBytes = 4L * 1024 * 1024;
        private const long PreferredAutomaticMemoryBudgetFloorBytes = 32L * 1024 * 1024;
        private const long MaximumAutomaticMemoryBudgetBytes = 512L * 1024 * 1024;
        private const int DefaultMergeFanIn = 32;
        private const int MaximumMergeFanIn = 256;
        private int maximumSortRunTupleCount = DefaultMaximumSortRunTupleCount;
        private long? memoryBudgetBytes;
        private int mergeFanIn = DefaultMergeFanIn;

        /// <summary>
        /// Gets the requested backfill implementation.<br/>
        /// Ordinary callers always receive the optimized bounded implementation; compatibility forcing is reserved for Abraxas diagnostics and regression controls.<br/>
        /// </summary>
        internal IndexBackfillMode Mode { get; private set; } = IndexBackfillMode.Optimized;

        /// <summary>
        /// Gets or sets the maximum number of tuples retained by one index before a sorted run is closed.<br/>
        /// This is a pathological-count safety ceiling, not the ordinary memory policy; the aggregate byte budget normally closes variable-key runs first.<br/>
        /// Values must be between 1 and 100,000,000 inclusive, and the default is 1,000,000.<br/>
        /// </summary>
        public int MaximumSortRunTupleCount
        {
            get => maximumSortRunTupleCount;
            set
            {
                if (value < 1 || value > MaximumSortRunTupleCountLimit)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        $"Index backfill maximum sort-run tuple count must be between 1 and {MaximumSortRunTupleCountLimit:N0}.");
                }
                maximumSortRunTupleCount = value;
            }
        }

        /// <summary>
        /// Gets or sets the aggregate retained-tuple memory budget for one grouped index build.<br/>
        /// A null value selects the automatic policy, which is computed once when the operation begins from GC-reported available memory and current high-memory headroom.<br/>
        /// The automatic budget is capped at 512 MiB, may contract if later memory pressure rises, and never expands during an active build.<br/>
        /// An explicit value must be at least 1 MiB and is used as supplied; callers coordinating several simultaneous builds should divide their intended process budget accordingly.<br/>
        /// </summary>
        public long? MemoryBudgetBytes
        {
            get => memoryBudgetBytes;
            set
            {
                if (value.HasValue && value.Value < 1024L * 1024)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "An explicit index-backfill memory budget must be at least 1 MiB.");
                memoryBudgetBytes = value;
            }
        }

        /// <summary>
        /// Gets or sets the former tuple-chunk option as a source-compatible alias for <see cref="MaximumSortRunTupleCount"/>.<br/>
        /// New code should name the physical safety control explicitly through <see cref="MaximumSortRunTupleCount"/>; this alias no longer represents the primary memory breakpoint.<br/>
        /// </summary>
        public int BatchSize
        {
            get => MaximumSortRunTupleCount;
            set => MaximumSortRunTupleCount = value;
        }

        /// <summary>
        /// Gets or sets whether optimized tuple chunks are ordered by key before LibraDex insertion.<br/>
        /// Sorting improves write locality at the cost of one bounded in-memory sort per chunk.<br/>
        /// </summary>
        public bool SortWithinBatch { get; set; } = true;

        /// <summary>
        /// Gets or sets whether all insertions from one prepared tuple chunk remain inside a single LibraDex identity-group durability batch.<br/>
        /// The default is <see langword="false"/> because measurements show that retaining thousands of scalar/date mutations in one pending generation is substantially slower than immediate publication.<br/>
        /// The option remains available for A/B diagnostics, durability-contract proofs, and future storage engines with a native packed batch path.<br/>
        /// </summary>
        public bool UseIdentityGroupBatch { get; set; }

        /// <summary>
        /// Gets or sets whether exact-string tuple chunks use one LibraDex identity-group durability batch.<br/>
        /// The default is <see langword="true"/> because measured text backfills become substantially faster, unlike scalar/date families whose default remains immediate publication.<br/>
        /// Disable this option together with <see cref="UseIdentityGroupBatch"/> for a complete immediate-publication control.<br/>
        /// </summary>
        public bool UseTextIdentityGroupBatch { get; set; } = true;

        /// <summary>
        /// Gets whether optimized scalar and eligible exact calculated-string backfills may use LibraDex's native packed builders.<br/>
        /// Native populations retain tuples in RAM while both the aggregate byte budget and <see cref="MaximumSortRunTupleCount"/> permit, then use sorted temporary runs and a bounded k-way merge.<br/>
        /// Unsupported shapes keep their existing optimized per-item path, while null scalar keys use their dedicated bounded key-state route alongside the native non-null generation.<br/>
        /// Ordinary callers always receive automatic native eligibility; disabling it is reserved for Abraxas diagnostics and regression controls.<br/>
        /// </summary>
        internal bool UseNativeSortedBuild { get; private set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of sorted spill runs merged in one bounded pass.<br/>
        /// Values must be between 2 and 256 inclusive; the default of 32 bounds open streams and merge-queue state while avoiding unnecessary passes.<br/>
        /// </summary>
        public int MergeFanIn
        {
            get => mergeFanIn;
            set
            {
                if (value < 2 || value > MaximumMergeFanIn)
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"Index backfill merge fan-in must be between 2 and {MaximumMergeFanIn:N0}.");
                mergeFanIn = value;
            }
        }

        /// <summary>
        /// Gets or sets an optional advanced directory override for temporary native-builder spill runs.<br/>
        /// A null or blank value places non-authoritative run files beside the RecordBase files with the same RecordBase basename so capacity, device performance, ACLs, encryption, and storage policy remain aligned with the RecordBase.<br/>
        /// An override can consume substantial free space, move sensitive encoded index keys to a differently protected volume, weaken inherited ACL or encryption expectations, and make build performance depend on another device.<br/>
        /// Spill files are never mutation logs or recovery authority and are deleted by exact path after completion, cancellation, or failure; a process termination can leave inert orphan files for later administrative cleanup.<br/>
        /// </summary>
        public string? SpillDirectory { get; set; }

        /// <summary>
        /// Returns the already validated tuple-count safety ceiling used by compatibility and native hot paths.<br/>
        /// Validation occurs in the public setter so population does not repeat it for every record.<br/>
        /// </summary>
        /// <returns>The configured positive batch size.<br/></returns>
        internal int GetValidatedBatchSize() => maximumSortRunTupleCount;

        /// <summary>
        /// Resolves the effective aggregate byte budget at one operation boundary.<br/>
        /// Explicit developer configuration wins; automatic mode uses the smaller of one eighth of GC total available memory, half the remaining high-memory headroom, and 512 MiB.<br/>
        /// A 32 MiB preferred floor is used only when both total capacity and current headroom can safely support it; otherwise the bounded result may contract to the 4 MiB emergency floor.<br/>
        /// </summary>
        /// <returns>The positive effective budget and whether it came from automatic policy.<br/></returns>
        internal (long Bytes, bool Automatic) ResolveMemoryBudget()
        {
            if (memoryBudgetBytes.HasValue)
                return (memoryBudgetBytes.Value, false);

            return (ResolveAutomaticMemoryBudget(), true);
        }

        /// <summary>
        /// Re-evaluates only the automatic memory policy so an active automatic operation can contract under pressure without observing later option mutation.<br/>
        /// The calculation has no dependence on <see cref="MemoryBudgetBytes"/> and therefore cannot switch an in-flight automatic build to a concurrently assigned explicit value.<br/>
        /// </summary>
        /// <returns>The current positive automatic retained-tuple budget candidate.<br/></returns>
        internal static long ResolveAutomaticMemoryBudget()
        {
            GCMemoryInfo memory = GC.GetGCMemoryInfo();
            long totalAvailable = Math.Max(MinimumAutomaticMemoryBudgetBytes, memory.TotalAvailableMemoryBytes);
            long highThreshold = memory.HighMemoryLoadThresholdBytes > 0
                ? memory.HighMemoryLoadThresholdBytes
                : totalAvailable;
            long currentLoad = Math.Max(0, memory.MemoryLoadBytes);
            long headroom = Math.Max(0, highThreshold - currentLoad);
            long capacityShare = Math.Max(MinimumAutomaticMemoryBudgetBytes, totalAvailable / 8);
            long pressureShare = Math.Max(MinimumAutomaticMemoryBudgetBytes, headroom / 2);
            long resolved = Math.Min(MaximumAutomaticMemoryBudgetBytes, Math.Min(capacityShare, pressureShare));
            if (totalAvailable >= PreferredAutomaticMemoryBudgetFloorBytes * 8 &&
                headroom >= PreferredAutomaticMemoryBudgetFloorBytes * 2)
            {
                resolved = Math.Max(PreferredAutomaticMemoryBudgetFloorBytes, resolved);
            }
            return Math.Max(MinimumAutomaticMemoryBudgetBytes, resolved);
        }

        /// <summary>Returns the already validated bounded merge fan-in used by the internal spill path.<br/></summary>
        internal int GetValidatedMergeFanIn() => mergeFanIn;

        /// <summary>
        /// Selects compatibility implementations for an explicit Abraxas diagnostic or regression comparison.<br/>
        /// This method is internal so ordinary developers receive automatic optimized/native routing while friend diagnostic applications can preserve controlled A/B coverage.<br/>
        /// Unsupported native shapes continue to fall back automatically to the optimized per-item path regardless of this override.<br/>
        /// </summary>
        /// <param name="mode">Requested optimized or legacy backfill implementation for the diagnostic operation.<br/></param>
        /// <param name="useNativeSortedBuild">Whether eligible scalar/date and exact-string populations may use native packed construction.<br/></param>
        internal void SetDiagnosticImplementationOverrides(IndexBackfillMode mode, bool useNativeSortedBuild)
        {
            Mode = mode;
            UseNativeSortedBuild = useNativeSortedBuild;
        }
    }

    /// <summary>
    /// Reports the effective path and phase costs of the most recently completed physical-index population.<br/>
    /// The diagnostics distinguish extraction and conversion cost from locality preparation, typed insertion, and durability publication.<br/>
    /// </summary>
    /// <param name="RequestedMode">Mode requested by the caller.<br/></param>
    /// <param name="EffectiveMode">Mode actually used after key-family compatibility routing.<br/></param>
    /// <param name="RecordsRead">Authoritative records inspected.<br/></param>
    /// <param name="TupleCount">Index tuples admitted after null and empty-value policy filtering.<br/></param>
    /// <param name="ExtractionTime">Time spent acquiring records and extracting typed keys.<br/></param>
    /// <param name="SortTime">Time spent ordering bounded tuple chunks.<br/></param>
    /// <param name="InsertTime">Time spent issuing typed LibraDex insertions.<br/></param>
    /// <param name="PublishTime">Time spent publishing or finalizing LibraDex identity-group batches.<br/></param>
    /// <param name="PeakBufferedTupleCount">Largest tuple chunk retained simultaneously.<br/></param>
    /// <param name="PublishedBatchCount">Number of non-empty tuple chunks published.<br/></param>
    public sealed record IndexBackfillDiagnostics(
        IndexBackfillMode RequestedMode,
        IndexBackfillMode EffectiveMode,
        int RecordsRead,
        int TupleCount,
        TimeSpan ExtractionTime,
        TimeSpan SortTime,
        TimeSpan InsertTime,
        TimeSpan PublishTime,
        int PeakBufferedTupleCount,
        int PublishedBatchCount)
    {
        /// <summary>Gets the number of initial sorted spill runs produced by the population.<br/></summary>
        public int SortedRunCount { get; init; }

        /// <summary>Gets cumulative temporary tuple bytes written across initial runs and merge outputs.<br/></summary>
        public long SpillBytes { get; init; }

        /// <summary>Gets the number of bounded k-way merge passes completed by the population.<br/></summary>
        public int MergePassCount { get; init; }

        /// <summary>Gets the largest number of run heads retained simultaneously by the merge queue.<br/></summary>
        public int PeakMergeQueueCount { get; init; }

        /// <summary>Gets whether the completed population used a shape-native packed builder instead of tuple-by-tuple physical mutation.<br/></summary>
        public bool NativeBuildUsed { get; init; }

        /// <summary>Gets the effective aggregate grouped-build memory budget observed by this index.<br/></summary>
        public long EffectiveMemoryBudgetBytes { get; init; }

        /// <summary>Gets the maximum tuple count permitted in one sort run.<br/></summary>
        public int MaximumSortRunTupleCount { get; init; }

        /// <summary>Gets the largest conservatively accounted tuple-buffer allocation retained by this index.<br/></summary>
        public long PeakBufferedBytes { get; init; }

        /// <summary>Gets the resolved directory used when this population required temporary sorted runs.<br/></summary>
        public string? SpillDirectory { get; init; }
    }
}
