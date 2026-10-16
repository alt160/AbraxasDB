using System.Buffers;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using LibraDex;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private volatile FunctionIndexProjection[] functionIndexProjections = Array.Empty<FunctionIndexProjection>();
        private volatile string? unavailableFunctionIndexId;
        private static readonly object MissingFunctionIndexValue = new();

        /// <summary>
        /// Registers developer-supplied function code that can maintain a persistent or volatile FunctionIndex without persisting executable code.<br/>
        /// Persistent index intent stores the stable ID and positive version and requires setup-callback registration after reopen; volatile projections use the same identity only for the current open store.<br/>
        /// The expression is compiled once per registration and ordinary mutations reuse the attached function through Abraxas's prepared function-key lifecycle.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the function projection.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the function semantics.<br/></param>
        /// <param name="functionVersion">Positive version that must change whenever the function semantics change.<br/></param>
        /// <param name="function">Developer expression compiled once and reused for ordinary record writes.<br/></param>
        public void RegisterFunctionIndex<TValue>(
            string functionId,
            int functionVersion,
            Expression<Func<T, TValue>> function)
        {
            ValidateFunctionIndexIdentity(functionId, functionVersion);
            ArgumentNullException.ThrowIfNull(function);

            Func<T, TValue> compiled = function.Compile();
            Func<T, (bool HasValue, object? Value)> runtimeReader = source =>
            {
                TValue value = compiled(source);
                return value is null ? (false, null) : (true, value);
            };
            Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> binaryReader = binary =>
            {
                T? source = binary.ToObject<T>(NativeDeserializationOptions);
                if (source is null)
                    return (false, null);
                return runtimeReader(source);
            };
            var descriptor = new FunctionIndexProjection(
                functionId,
                functionVersion,
                NormalizeValueType(typeof(TValue)),
                runtimeReader,
                binaryReader);

            RegisterFunctionIndexDefinition(descriptor);
        }

        /// <summary>
        /// Registers one source-aware developer function for a persistent or volatile FunctionIndex without materializing complete records during binary evaluation.<br/>
        /// The existing <see cref="Selector{T}.ExternalSelector{TValue}"/> supplies Abraxas-readable source semantics plus the process-local function, so backfill, repair, and prior-key removal read only the required Inheto source values.<br/>
        /// Ordinary puts reuse the record's single Inheto serialization and retain the projected key once across validation and physical mutation.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the function.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the executable function semantics.<br/></param>
        /// <param name="functionVersion">Positive version that changes whenever the function meaning changes.<br/></param>
        /// <param name="function">Source-aware external selector containing the Abraxas-readable source and developer function.<br/></param>
        public void RegisterFunctionIndex<TValue>(
            string functionId,
            int functionVersion,
            Selector<T>.ExternalSelector<TValue> function)
        {
            ValidateFunctionIndexIdentity(functionId, functionVersion);
            ArgumentNullException.ThrowIfNull(function);
            if (!TryCreateStoreSelectorReader<TValue>(function, out var typedReader))
                throw new NotSupportedException("The FunctionIndex source cannot be evaluated from authoritative Inheto values.");

            Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> binaryReader = binary =>
            {
                (bool hasValue, TValue value) = typedReader(binary);
                return hasValue ? (true, value) : (false, null);
            };
            RegisterFunctionIndexDefinition(new FunctionIndexProjection(
                functionId,
                functionVersion,
                NormalizeValueType(typeof(TValue)),
                RuntimeReader: null,
                binaryReader));
        }

        /// <summary>
        /// Publishes one immutable FunctionIndex execution definition and reconciles any matching persisted index intent.<br/>
        /// Registration replaces only an exactly matching ID, version, and result type; divergent executable semantics must use a new version and fail closed before mutation.<br/>
        /// </summary>
        /// <param name="descriptor">Validated process-local FunctionIndex definition.<br/></param>
        private void RegisterFunctionIndexDefinition(FunctionIndexProjection descriptor)
        {
            string functionId = descriptor.FunctionId;
            int functionVersion = descriptor.FunctionVersion;

            bool replacedExisting = false;
            lock (libraDexIndexLock)
            {
                FunctionIndexProjection[] current = functionIndexProjections;
                for (int i = 0; i < current.Length; i++)
                {
                    if (!string.Equals(current[i].FunctionId, functionId, StringComparison.Ordinal))
                        continue;
                    if (current[i].FunctionVersion != functionVersion || current[i].ResultType != descriptor.ResultType)
                    {
                        throw new IndexContractException(
                            IndexContractFailureKind.Configuration,
                            $"FunctionIndex '{functionId}' is already registered with version {current[i].FunctionVersion} and result type '{current[i].ResultType.FullName}'.",
                            functionId,
                            functionId);
                    }

                    FunctionIndexProjection[] replaced = (FunctionIndexProjection[])current.Clone();
                    replaced[i] = descriptor;
                    functionIndexProjections = replaced;
                    replacedExisting = true;
                    break;
                }

                if (!replacedExisting)
                {
                    FunctionIndexProjection[] appended = new FunctionIndexProjection[current.Length + 1];
                    Array.Copy(current, appended, current.Length);
                    appended[current.Length] = descriptor;
                    functionIndexProjections = appended;
                }
            }

            AttachPersistedFunctionIndexProjection(descriptor);
            RefreshUnavailableFunctionIndexProjection();
        }

        /// <summary>
        /// Creates and backfills one durable function-calculated index while registering its projection code for automatic CRUD maintenance.<br/>
        /// Ordinary writes evaluate the compiled expression against the CLR object already supplied to Abraxas; delete, repair, and existing-data backfill materialize only when the projection must be reconstructed from Fractal/Inheto bytes.<br/>
        /// The index remains ordinary LibraDex physical reality owned by this store; developers do not add, delete, or rekey its tuples separately.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the function projection.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the function semantics.<br/></param>
        /// <param name="functionVersion">Positive version that must change whenever the function semantics change.<br/></param>
        /// <param name="function">Developer expression compiled once and reused for ordinary record writes.<br/></param>
        /// <param name="direction">Physical ordered traversal direction maintained by LibraDex.<br/></param>
        /// <param name="caseType">Text representation profile when <typeparamref name="TValue"/> is <see cref="string"/>; non-text results require <see cref="IndexCaseType.None"/>.<br/></param>
        /// <param name="options">Optional uniqueness, null, empty-string, and binary-shape participation contract.<br/></param>
        /// <returns><see langword="true"/> after the projection is registered and its complete index population is ready.<br/></returns>
        public bool CreateFunctionIndex<TValue>(
            string functionId,
            int functionVersion,
            Expression<Func<T, TValue>> function,
            IndexDirection direction = IndexDirection.Ascending,
            IndexCaseType caseType = IndexCaseType.None,
            CalculatedIndexOptions? options = null)
        {
            options ??= new CalculatedIndexOptions();
            ValidateFunctionCalculatedIndexOptions(typeof(TValue), direction, caseType, options);
            RegisterFunctionIndex(functionId, functionVersion, function);
            return CreateFunctionIndexCore<TValue>(functionId, functionVersion, direction, caseType, options);
        }

        /// <summary>
        /// Creates and backfills one persistent source-aware FunctionIndex through the ordinary Abraxas index lifecycle.<br/>
        /// The external selector reads only its declared source values from Inheto while the stable function ID and version provide the executable-semantics reconciliation contract required after reopen.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the function.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the executable function semantics.<br/></param>
        /// <param name="functionVersion">Positive semantic version for the executable function.<br/></param>
        /// <param name="function">Source-aware external selector used for backfill and mutation maintenance.<br/></param>
        /// <param name="direction">Physical ordered traversal direction maintained by LibraDex.<br/></param>
        /// <param name="caseType">Text profile when the result is string; otherwise none.<br/></param>
        /// <param name="options">Optional uniqueness, null, empty-string, and binary-shape participation contract.<br/></param>
        /// <returns><see langword="true"/> after the complete FunctionIndex is ready.<br/></returns>
        public bool CreateFunctionIndex<TValue>(
            string functionId,
            int functionVersion,
            Selector<T>.ExternalSelector<TValue> function,
            IndexDirection direction = IndexDirection.Ascending,
            IndexCaseType caseType = IndexCaseType.None,
            CalculatedIndexOptions? options = null)
        {
            options ??= new CalculatedIndexOptions();
            ValidateFunctionCalculatedIndexOptions(typeof(TValue), direction, caseType, options);
            RegisterFunctionIndex(functionId, functionVersion, function);
            return CreateFunctionIndexCore<TValue>(functionId, functionVersion, direction, caseType, options);
        }

        /// <summary>
        /// Creates the persistent physical reality for one previously registered FunctionIndex definition.<br/>
        /// Both full-record and source-aware functions converge here so readiness, persistence, mutation maintenance, and query eligibility remain one ordinary Abraxas index contract.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the registered function.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the registered function semantics.<br/></param>
        /// <param name="functionVersion">Positive version of the registered function semantics.<br/></param>
        /// <param name="direction">Physical ordered traversal direction maintained by LibraDex.<br/></param>
        /// <param name="caseType">Text profile when the result is string; otherwise none.<br/></param>
        /// <param name="options">Validated participation, uniqueness, and binary-shape contract.<br/></param>
        /// <returns><see langword="true"/> after the persistent FunctionIndex is complete and ready.<br/></returns>
        private bool CreateFunctionIndexCore<TValue>(
            string functionId,
            int functionVersion,
            IndexDirection direction,
            IndexCaseType caseType,
            CalculatedIndexOptions options)
        {
            FunctionIndexProjection descriptor = RequireFunctionIndexProjection(
                functionId,
                functionVersion,
                NormalizeValueType(typeof(TValue)));
            string logicalName = GetFunctionIndexLogicalName(functionId);
            Type resultType = descriptor.ResultType;
            bool registered = TryRegisterLibraDexCalculatedIndex(
                logicalName,
                logicalName,
                selector: null!,
                resultType == typeof(string) ? ToTextIndexProfile(caseType, ignoreCase: false) : TextIndexProfile.Exact,
                direction,
                options.Unique,
                options.NullValues == CalculatedIndexNullValues.Include,
                options.EmptyStrings == CalculatedIndexEmptyStrings.Include,
                ToCalculatedBinaryKeyStorage(resultType, options.BinaryShape),
                GetCalculatedBinaryKeyBytes(resultType, options.BinaryShape),
                libraDexIdentityIndexes ?? throw new InvalidOperationException("A persistent LibraDex identity group is not bound to this store."),
                ownerCatalog: null,
                populateExisting: true,
                functionResultType: resultType,
                functionBinaryReader: descriptor.BinaryReader,
                functionRuntimeReader: descriptor.RuntimeReader,
                functionId: functionId,
                functionVersion: functionVersion);
            if (!registered)
                throw new NotSupportedException($"Function index projection result type '{resultType.FullName}' does not have a supported ordered representation.");
            return true;
        }

        /// <summary>
        /// Gets the ordinary Abraxas index handle associated with a registered function projection ID.<br/>
        /// The returned handle can be passed to existing record-ID, record, property-reader, bookmark, and ordered traversal APIs without exposing the generated LibraDex definition name.<br/>
        /// </summary>
        /// <param name="functionId">Stable function projection identity supplied during creation and registration.<br/></param>
        /// <returns>The attached persistent or volatile function projection index handle.<br/></returns>
        public RecordIndex GetFunctionIndex(string functionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
            return GetIndex(GetFunctionIndexLogicalName(functionId));
        }

        /// <summary>
        /// Attempts to get the ordinary Abraxas index handle associated with a registered function projection ID.<br/>
        /// This nonthrowing discovery form preserves the same generated-name encapsulation as <see cref="GetFunctionIndex(string)"/>.<br/>
        /// </summary>
        /// <param name="functionId">Stable function projection identity supplied during creation and registration.<br/></param>
        /// <param name="index">Receives the attached persistent or volatile function projection index handle when found.<br/></param>
        /// <returns><see langword="true"/> when the projection index is attached; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetFunctionIndex(string functionId, out RecordIndex index)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
            return TryGetIndex(GetFunctionIndexLogicalName(functionId), out index);
        }

        /// <summary>
        /// Removes attached persistent or volatile function projection reality by stable projection ID, including durable intent when present.<br/>
        /// Persistent removal remains available when function code was intentionally not registered, providing a direct recovery/offboarding path without first executing obsolete developer semantics.<br/>
        /// </summary>
        /// <param name="functionId">Stable function projection identity supplied during creation.<br/></param>
        /// <returns><see langword="true"/> when attached or detached durable projection intent was removed; otherwise <see langword="false"/>.<br/></returns>
        public bool RemoveFunctionIndex(string functionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
            string logicalName = GetFunctionIndexLogicalName(functionId);
            bool removed;
            if (TryGetIndex(logicalName, out RecordIndex attached))
            {
                removed = RemoveIndex(attached);
            }
            else if (rbase.TryGetOrdinaryIndexIntent(storeID, logicalName, out OrdinaryIndexIntentEntry intent) &&
                intent.Kind == OrdinaryIndexIntentKind.FunctionCalculated &&
                string.Equals(intent.FunctionId, functionId, StringComparison.Ordinal))
            {
                var detached = new RecordIndex(intent.LogicalName, intent.LogicalName, intent.Unique, this);
                removed = PersistDropAndUnregisterOrdinaryIndex(
                    detached,
                    intent.LogicalName,
                    intent.PhysicalName,
                    intent.FunctionId);
            }
            else
            {
                removed = false;
            }

            RefreshUnavailableFunctionIndexProjection();
            return removed;
        }

        /// <summary>
        /// Ensures one developer-owned volatile FunctionIndex is attached, complete, and mutation-maintained for this open store.<br/>
        /// The first call registers the projection, backfills existing authoritative records, and returns only after the ordered index reports ready.<br/>
        /// Later calls with the same projection ID, version, result type, and options reuse the attached index without rescanning records; callers must increment the version whenever projection semantics change.<br/>
        /// Volatile index bytes and executable projection code are process-local, while ordinary Put, overwrite, delete, and repair operations participate through the same Abraxas mutation contract as persistent indexes.<br/>
        /// Closing the store discards the index. Callers that intentionally recreate a store within one session may call this method again on the replacement store to preserve their session-level FunctionIndex intent.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the function projection.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the function semantics.<br/></param>
        /// <param name="functionVersion">Positive semantic version; increment it whenever the function meaning changes.<br/></param>
        /// <param name="function">Developer expression compiled once and reused for ordinary record writes.<br/></param>
        /// <param name="direction">Physical ordered traversal direction maintained by the volatile index.<br/></param>
        /// <param name="caseType">Text representation profile when <typeparamref name="TValue"/> is <see cref="string"/>; non-text results require <see cref="IndexCaseType.None"/>.<br/></param>
        /// <param name="options">Optional uniqueness, null, empty-string, and binary-shape participation contract.<br/></param>
        /// <returns>The complete attached volatile index handle suitable for exact-index ordered readers.<br/></returns>
        public RecordIndex EnsureMemoryFunctionIndex<TValue>(
            string functionId,
            int functionVersion,
            Expression<Func<T, TValue>> function,
            IndexDirection direction = IndexDirection.Ascending,
            IndexCaseType caseType = IndexCaseType.None,
            CalculatedIndexOptions? options = null)
        {
            options ??= new CalculatedIndexOptions();
            ValidateFunctionCalculatedIndexOptions(typeof(TValue), direction, caseType, options);
            RegisterFunctionIndex(functionId, functionVersion, function);
            return EnsureMemoryFunctionIndexCore<TValue>(functionId, functionVersion, direction, caseType, options);
        }

        /// <summary>
        /// Ensures one source-aware volatile FunctionIndex is complete and attached to the current store.<br/>
        /// First use backfills through the external selector's direct Inheto reader; repeated calls reuse the same complete index, and every subsequent mutation maintains it automatically.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the source-aware function.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the function semantics.<br/></param>
        /// <param name="functionVersion">Positive semantic version; increment it whenever the function meaning changes.<br/></param>
        /// <param name="function">Source-aware external selector used for backfill and mutation maintenance.<br/></param>
        /// <param name="direction">Physical ordered traversal direction maintained by the volatile index.<br/></param>
        /// <param name="caseType">Text profile when the result is string; otherwise none.<br/></param>
        /// <param name="options">Optional uniqueness, null, empty-string, and binary-shape participation contract.<br/></param>
        /// <returns>The complete attached volatile FunctionIndex handle suitable for exact-index ordered readers.<br/></returns>
        public RecordIndex EnsureMemoryFunctionIndex<TValue>(
            string functionId,
            int functionVersion,
            Selector<T>.ExternalSelector<TValue> function,
            IndexDirection direction = IndexDirection.Ascending,
            IndexCaseType caseType = IndexCaseType.None,
            CalculatedIndexOptions? options = null)
        {
            options ??= new CalculatedIndexOptions();
            ValidateFunctionCalculatedIndexOptions(typeof(TValue), direction, caseType, options);
            RegisterFunctionIndex(functionId, functionVersion, function);
            return EnsureMemoryFunctionIndexCore<TValue>(functionId, functionVersion, direction, caseType, options);
        }

        /// <summary>
        /// Reuses or creates the volatile physical reality for one registered FunctionIndex definition.<br/>
        /// The shared core prevents full-record and source-aware declarations from producing competing index lifecycles or retrieval behavior.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR key type produced by the registered function.<br/></typeparam>
        /// <param name="functionId">Stable developer identity for the registered function semantics.<br/></param>
        /// <param name="functionVersion">Positive version of the registered function semantics.<br/></param>
        /// <param name="direction">Physical ordered traversal direction maintained by the volatile index.<br/></param>
        /// <param name="caseType">Text profile when the result is string; otherwise none.<br/></param>
        /// <param name="options">Validated participation, uniqueness, and binary-shape contract.<br/></param>
        /// <returns>The complete attached volatile FunctionIndex handle.<br/></returns>
        private RecordIndex EnsureMemoryFunctionIndexCore<TValue>(
            string functionId,
            int functionVersion,
            IndexDirection direction,
            IndexCaseType caseType,
            CalculatedIndexOptions options)
        {

            string logicalName = GetFunctionIndexLogicalName(functionId);
            if (TryGetIndex(logicalName, out RecordIndex existing, useMemoryIndex: true))
                return existing;

            FunctionIndexProjection descriptor = RequireFunctionIndexProjection(
                functionId,
                functionVersion,
                NormalizeValueType(typeof(TValue)));
            Type resultType = descriptor.ResultType;
            bool registered = TryRegisterLibraDexCalculatedIndex(
                logicalName,
                logicalName,
                selector: null!,
                resultType == typeof(string) ? ToTextIndexProfile(caseType, ignoreCase: false) : TextIndexProfile.Exact,
                direction,
                options.Unique,
                options.NullValues == CalculatedIndexNullValues.Include,
                options.EmptyStrings == CalculatedIndexEmptyStrings.Include,
                ToCalculatedBinaryKeyStorage(resultType, options.BinaryShape),
                GetCalculatedBinaryKeyBytes(resultType, options.BinaryShape),
                LibraDexVolatileIndexes,
                libraDexVolatileCatalog,
                populateExisting: true,
                manageIntent: false,
                functionResultType: resultType,
                functionBinaryReader: descriptor.BinaryReader,
                functionRuntimeReader: descriptor.RuntimeReader,
                functionId: functionId,
                functionVersion: functionVersion);
            if (!registered)
                throw new NotSupportedException($"Function index projection result type '{resultType.FullName}' does not have a supported ordered representation.");
            if (!TryGetIndex(logicalName, out RecordIndex created, useMemoryIndex: true))
                throw new InvalidOperationException($"Volatile FunctionIndex '{functionId}' completed registration but did not attach an Abraxas index handle.");
            return created;
        }

        /// <summary>
        /// Validates the developer-facing shape options for one function-calculated index before physical or intent mutation begins.<br/>
        /// </summary>
        /// <param name="declaredType">Exact result type declared by the application expression.<br/></param>
        /// <param name="direction">Requested physical traversal direction.<br/></param>
        /// <param name="caseType">Requested text profile, or none for non-text keys.<br/></param>
        /// <param name="options">Participation, uniqueness, and binary-shape settings to validate.<br/></param>
        private void ValidateFunctionCalculatedIndexOptions(
            Type declaredType,
            IndexDirection direction,
            IndexCaseType caseType,
            CalculatedIndexOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!Enum.IsDefined(direction))
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown index direction.");
            if (!Enum.IsDefined(caseType))
                throw new ArgumentOutOfRangeException(nameof(caseType), caseType, "Unknown function index text profile.");
            if (!Enum.IsDefined(options.NullValues) || !Enum.IsDefined(options.EmptyStrings) || !Enum.IsDefined(options.BinaryShape))
                throw new ArgumentOutOfRangeException(nameof(options), "FunctionIndex options contain an unknown value.");
            Type resultType = NormalizeValueType(declaredType);
            if (resultType != typeof(string) && caseType != IndexCaseType.None)
                throw new InvalidOperationException("Function index case options apply only to string results.");
            if (resultType != typeof(byte[]) && options.BinaryShape != CalculatedIndexBinaryShape.VariableLength)
                throw new InvalidOperationException("CalculatedIndexOptions.BinaryShape applies only to function projections returning byte[].");
        }

        /// <summary>
        /// Validates the bounded durable identity used to reconnect developer function code with persisted FunctionIndex intent.<br/>
        /// </summary>
        /// <param name="functionId">Stable nonempty function projection identity.<br/></param>
        /// <param name="functionVersion">Positive semantic version supplied by the application.<br/></param>
        private static void ValidateFunctionIndexIdentity(string functionId, int functionVersion)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
            if (Encoding.UTF8.GetByteCount(functionId) > 256)
                throw new ArgumentOutOfRangeException(nameof(functionId), "Function projection IDs cannot exceed 256 UTF-8 bytes.");
            if (functionVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(functionVersion), functionVersion, "Function projection versions must be positive integers.");
        }

        /// <summary>
        /// Finds one exact process-local function projection without reflection or delegate construction on the mutation path.<br/>
        /// </summary>
        /// <param name="functionId">Stable function projection identity.<br/></param>
        /// <param name="functionVersion">Required semantic version.<br/></param>
        /// <param name="resultType">Required normalized key type.<br/></param>
        /// <returns>The immutable registered execution descriptor.<br/></returns>
        private FunctionIndexProjection RequireFunctionIndexProjection(string functionId, int functionVersion, Type resultType)
        {
            FunctionIndexProjection[] current = functionIndexProjections;
            for (int i = 0; i < current.Length; i++)
            {
                if (string.Equals(current[i].FunctionId, functionId, StringComparison.Ordinal) &&
                    current[i].FunctionVersion == functionVersion &&
                    current[i].ResultType == resultType)
                    return current[i];
            }
            throw new InvalidOperationException($"Function index projection '{functionId}' version {functionVersion} is not registered for '{resultType.FullName}'.");
        }

        /// <summary>
        /// Resolves one exact semantic function-index name to its process-local projection reader for authoritative-payload fallback.<br/>
        /// Result types must match exactly; Abraxas does not introduce silent numeric coercion or string conversion at the condition boundary.<br/>
        /// </summary>
        /// <param name="semanticName">Exact case-sensitive function projection identity.<br/></param>
        /// <param name="expectedType">Exact normalized CLR type requested by the condition operator.<br/></param>
        /// <param name="reader">Registered allocation-bounded binary projection reader when found.<br/></param>
        /// <returns><see langword="true"/> only when one exact registered projection matches the name and type.<br/></returns>
        private bool TryResolveFunctionSemanticIndexReader(
            string semanticName,
            Type expectedType,
            out Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader)
        {
            reader = default!;
            Type normalizedExpectedType = NormalizeValueType(expectedType);
            FunctionIndexProjection[] current = functionIndexProjections;
            for (int i = 0; i < current.Length; i++)
            {
                if (string.Equals(current[i].FunctionId, semanticName, StringComparison.Ordinal) &&
                    current[i].ResultType == normalizedExpectedType)
                {
                    reader = current[i].BinaryReader;
                    return true;
                }
            }


            Inheto.PropPathCheck check = Inheto.InhetoBinary.CheckPropPath(typeof(T), semanticName, normalizedExpectedType);
            if (check.IsValid)
            {
                reader = binary =>
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
                return true;
            }
            return false;
        }

        /// <summary>
        /// Builds one boxed selector reader while adding store-bound semantic-index resolution to the ordinary backend-neutral selector reader.<br/>
        /// Named selectors use an exact registered projection or admitted executable path; all other selectors retain the established implementation.<br/>
        /// </summary>
        internal bool TryCreateStoreSelectorReader(
            ISelector selector,
            out Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader)
        {
            ArgumentNullException.ThrowIfNull(selector);
            reader = default!;
            if (selector is ISelectorStructure structure &&
                structure.SemanticDescriptor is NamedIndexSelectorDescriptor named &&
                structure.ResultType is Type resultType)
            {
                return TryResolveFunctionSemanticIndexReader(named.SemanticName, resultType, out reader);
            }
            return SelectorSemanticReader<T>.TryCreate(selector, NativeDeserializationOptions, out reader);
        }

        /// <summary>
        /// Builds one exact typed selector reader with the same store-bound semantic-index behavior used by condition fallback.<br/>
        /// </summary>
        internal bool TryCreateStoreSelectorReader<TValue>(
            ISelector selector,
            out Func<Inheto.InhetoBinary, (bool HasValue, TValue Value)> reader)
        {
            ArgumentNullException.ThrowIfNull(selector);
            reader = default!;
            if (selector is ISelectorStructure structure &&
                structure.SemanticDescriptor is NamedIndexSelectorDescriptor named)
            {
                if (structure.ResultType != typeof(TValue) ||
                    !TryResolveFunctionSemanticIndexReader(named.SemanticName, typeof(TValue), out var untyped))
                    return false;
                reader = binary =>
                {
                    (bool hasValue, object? value) = untyped(binary);
                    return hasValue && value is TValue typed ? (true, typed) : (false, default!);
                };
                return true;
            }
            return SelectorSemanticReader<T>.TryCreate<TValue>(selector, NativeDeserializationOptions, out reader);
        }

        /// <summary>
        /// Produces a deterministic bounded physical/logical index name from the store name and durable function projection identity.<br/>
        /// The readable token aids diagnostics while the hash suffix prevents sanitized-name aliases.<br/>
        /// </summary>
        /// <param name="functionId">Stable function projection identity.<br/></param>
        /// <returns>A deterministic Abraxas-owned ordinary-index name.<br/></returns>
        private string GetFunctionIndexLogicalName(string functionId)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(functionId));
            string suffix = Convert.ToHexString(hash).ToLowerInvariant()[..12];
            return $"aidx_{name}_{SanitizeTokenForIndex(functionId, 32)}_{suffix}";
        }

        /// <summary>
        /// Attaches and rebuilds matching persisted intent when projection code is registered after ordinary store attachment.<br/>
        /// Setup-callback registration returns immediately here because normal binding subsequently performs the one required attachment.<br/>
        /// </summary>
        /// <param name="projection">Newly published immutable function execution descriptor.<br/></param>
        private void AttachPersistedFunctionIndexProjection(FunctionIndexProjection projection)
        {
            CatalogIdentityGroupIndexes? indexes = libraDexIdentityIndexes;
            if (indexes is null || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly)
                return;
            OrdinaryIndexIntentEntry[] intents = rbase.GetOrdinaryIndexIntentEntries(storeID);
            for (int i = 0; i < intents.Length; i++)
            {
                OrdinaryIndexIntentEntry intent = intents[i];
                if (intent.Kind != OrdinaryIndexIntentKind.FunctionCalculated ||
                    !string.Equals(intent.FunctionId, projection.FunctionId, StringComparison.Ordinal) ||
                    intent.FunctionVersion != projection.FunctionVersion ||
                    !string.Equals(intent.ResultTypeCode, ConditionWireTypes.GetCode(projection.ResultType), StringComparison.Ordinal))
                    continue;
                if (TryRestoreOrdinaryIndexIntent(intent, indexes))
                {
                    var handle = new RecordIndex(intent.LogicalName, intent.LogicalName, intent.Unique, this);
                    _ = TryRebuildLibraDexIndex(handle);
                }
            }
        }

        /// <summary>
        /// Rejects object mutation while durable FunctionIndex intent lacks the developer function code required to maintain it.<br/>
        /// Reads may continue through Fractal and other attached indexes, but allowing a write would make the detached projection silently stale.<br/>
        /// </summary>
        /// <param name="operationName">Public mutation operation that requires the complete maintained-index set.<br/></param>
        private void EnsureFunctionIndexProjectionsAvailable(string operationName)
        {
            string? functionId = unavailableFunctionIndexId;
            if (functionId is null)
                return;
            throw new IndexContractException(
                IndexContractFailureKind.Configuration,
                $"Operation '{operationName}' cannot mutate this store until function index projection '{functionId}' is registered in the store setup callback.",
                functionId,
                functionId);
        }

        /// <summary>
        /// Recomputes the fail-closed mutation guard from durable application intent and the current immutable registration snapshot.<br/>
        /// The first unavailable projection ID is retained for bounded steady-state checking and useful diagnostics.<br/>
        /// </summary>
        private void RefreshUnavailableFunctionIndexProjection()
        {
            OrdinaryIndexIntentEntry[] intents = rbase.GetOrdinaryIndexIntentEntries(storeID);
            FunctionIndexProjection[] projections = functionIndexProjections;
            for (int i = 0; i < intents.Length; i++)
            {
                OrdinaryIndexIntentEntry intent = intents[i];
                if (intent.Kind != OrdinaryIndexIntentKind.FunctionCalculated || rbase.IsOrdinaryIndexRemoved(storeID, intent.LogicalName))
                    continue;
                Type resultType = NormalizeValueType(ConditionWireTypes.Resolve(intent.ResultTypeCode));
                bool available = false;
                for (int p = 0; p < projections.Length; p++)
                {
                    if (string.Equals(projections[p].FunctionId, intent.FunctionId, StringComparison.Ordinal) &&
                        projections[p].FunctionVersion == intent.FunctionVersion &&
                        projections[p].ResultType == resultType)
                    {
                        available = true;
                        break;
                    }
                }
                if (!available)
                {
                    unavailableFunctionIndexId = intent.FunctionId;
                    return;
                }
            }
            unavailableFunctionIndexId = null;
        }

        /// <summary>
        /// Evaluates every function projection exactly once for one supplied CLR record and retains the boxed results in pooled write-scoped storage.<br/>
        /// The store mutation scope keeps descriptor ordinals stable until preflight and insertion both consume the prepared keys.<br/>
        /// </summary>
        /// <param name="record">CLR record already supplied to the ordinary Abraxas persistence path.<br/></param>
        /// <param name="binary">The record's authoritative Inheto serialization, reused by source-aware functions without materializing the complete record.<br/></param>
        /// <returns>A disposable pooled key snapshot, or an empty value when no function projections are attached.<br/></returns>
        private PreparedFunctionIndexKeys PrepareFunctionIndexKeys(T record, Inheto.InhetoBinary binary)
        {
            LibraDexSimpleIndex[] indexes = libraDexSimpleIndexes;
            bool required = false;
            for (int i = 0; i < indexes.Length; i++)
            {
                if (indexes[i].IsFunctionIndex)
                {
                    required = true;
                    break;
                }
            }
            if (!required)
                return default;

            object?[] values = ArrayPool<object?>.Shared.Rent(indexes.Length);
            Array.Clear(values,0,indexes.Length);
            try
            {
                for (int i = 0; i < indexes.Length; i++)
                {
                    LibraDexSimpleIndex index = indexes[i];
                    if (!index.IsFunctionIndex)
                        continue;
                    (bool hasValue, object? value) = index.FunctionRuntimeReader is { } runtimeReader
                        ? runtimeReader(record)
                        : index.CalculatedReader!(binary);
                    values[i] = hasValue ? value : MissingFunctionIndexValue;
                }
                return new PreparedFunctionIndexKeys(indexes,values);
            }
            catch
            {
                Array.Clear(values,0,indexes.Length);
                ArrayPool<object?>.Shared.Return(values);
                throw;
            }
        }

        /// <summary>
        /// Retains one ordinal-aligned pooled function-key snapshot across preflight and insertion for a single record mutation.<br/>
        /// </summary>
        private struct PreparedFunctionIndexKeys : IDisposable
        {
            private readonly LibraDexSimpleIndex[]? indexes;
            private object?[]? values;

            /// <summary>
            /// Creates one prepared-key lease over the exact immutable descriptor snapshot used during evaluation.<br/>
            /// </summary>
            /// <param name="indexes">Stable simple-index descriptor snapshot.<br/></param>
            /// <param name="values">Rented ordinal-aligned projected values.<br/></param>
            internal PreparedFunctionIndexKeys(LibraDexSimpleIndex[] indexes, object?[] values)
            {
                this.indexes = indexes;
                this.values = values;
            }

            /// <summary>
            /// Reads the prepared function projection aligned with one simple-index ordinal.<br/>
            /// </summary>
            /// <param name="index">Current immutable simple-index descriptor snapshot.<br/></param>
            /// <param name="ordinal">Descriptor ordinal being validated or inserted.<br/></param>
            /// <param name="hasValue">Whether the function projection produced a non-null key.<br/></param>
            /// <param name="value">Prepared boxed key when present.<br/></param>
            /// <returns><see langword="true"/> when this ordinal belongs to a function projection; otherwise <see langword="false"/>.<br/></returns>
            internal bool TryGet(LibraDexSimpleIndex[] index, int ordinal, out bool hasValue, out object? value)
            {
                object?[]? currentValues = values;
                if (!ReferenceEquals(indexes,index) || currentValues is null || !index[ordinal].IsFunctionIndex)
                {
                    hasValue = false;
                    value = null;
                    return false;
                }
                object? prepared = currentValues[ordinal];
                hasValue = !ReferenceEquals(prepared,MissingFunctionIndexValue);
                value = hasValue ? prepared : null;
                return true;
            }

            /// <summary>
            /// Clears referenced values and returns the scratch array to the shared pool exactly once.<br/>
            /// </summary>
            public void Dispose()
            {
                object?[]? returned = values;
                if (returned is null)
                    return;
                values = null;
                Array.Clear(returned,0,indexes!.Length);
                ArrayPool<object?>.Shared.Return(returned);
            }
        }

        /// <summary>
        /// Holds one compiled process-local function projection and its exact durable reconciliation identity.<br/>
        /// Runtime and binary readers separate the allocation-lean common Put path from backfill, repair, and prior-key removal.<br/>
        /// </summary>
        private readonly record struct FunctionIndexProjection(
            string FunctionId,
            int FunctionVersion,
            Type ResultType,
            Func<T, (bool HasValue, object? Value)>? RuntimeReader,
            Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> BinaryReader);
    }
}
