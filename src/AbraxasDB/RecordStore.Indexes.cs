using System.Linq.Expressions;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Reflection;
using System.Globalization;

namespace AbraxasDB
{
    //====== TYPES ======
    public partial class RecordStore<T>
    {
        /// <summary>Specifies legacy text-representation preferences carried by an index selector.<br/>Exact keys remain available; folded and culture sort-key representations provide additional lookup or ordering support.<br/></summary>
        public enum IndexCaseType
        {
            /// <summary>Maintains exact text keys without requesting folded or culture sort-key representations.<br/></summary>
            None,
            /// <summary>Requests culture sort-key support in addition to exact text keys.<br/></summary>
            Collated,
            /// <summary>Requests folded text support for case-insensitive lookup in addition to exact text keys.<br/></summary>
            Folded,
            /// <summary>Requests both culture sort-key and folded text support in addition to exact text keys.<br/></summary>
            CollatedAndFolded
        }

        /// <summary>Specifies the key ordering direction carried by an index selector declaration.<br/></summary>
        public enum IndexSortDirection
        {
            /// <summary>Declares ascending key order for the index selector.<br/></summary>
            Ascending,
            /// <summary>Declares descending key order for the index selector.<br/></summary>
            Descending
        }

        /// <summary>Describes an index key selector with text-representation, ordering, and naming metadata.<br/>Construction configures a declaration only; an explicit index creation call applies it to a store.<br/></summary>
        public class IndexSelectorBuilder
        {
            private readonly ISelector selector;
            private readonly bool isText;
            private readonly string? tokenSource;
            private readonly IndexCaseType caseType;
            private readonly IndexSortDirection sort;
            private readonly Type? valueType;
            internal ISelector CalculatedSelector => selector;
            internal bool IsTextResult => isText;
            internal IndexCaseType CaseType => caseType;
            internal Type? DeclaredValueType => valueType;

            private protected IndexSelectorBuilder(ISelector selector, bool isText, string? tokenSource, IndexCaseType caseType, IndexSortDirection sort, Type? valueType)
            {
                this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
                this.isText = isText;
                this.tokenSource = tokenSource;
                this.caseType = caseType;
                this.sort = sort;
                this.valueType = valueType;
            }

            internal static IndexSelectorBuilder FromSelector(ISelector selector, bool isText, string? tokenSource, Type? valueType)
            {
                if (valueType is not null)
                {
                    var typedTypeDef = typeof(RecordStore<>.TypedIndexSelectorBuilder<>);
                    var typedType = typedTypeDef.MakeGenericType(typeof(T), valueType);
                    return (IndexSelectorBuilder)Activator.CreateInstance(
                        typedType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        args: new object?[] { selector, isText, tokenSource, IndexCaseType.None, IndexSortDirection.Ascending, valueType },
                        culture: null)!;
                }

                return new IndexSelectorBuilder(selector, isText, tokenSource, IndexCaseType.None, IndexSortDirection.Ascending, valueType);
            }

            /// <summary>Returns a new declaration with the requested text-representation preference, preserving the selector, token, and ordering metadata.<br/>This does not create an index or change the current declaration.<br/></summary>
            /// <param name="type">Exact, folded, culture sort-key, or combined text-representation preference.<br/></param>
            /// <returns>A new index selector declaration with the selected text preference.<br/></returns>
            /// <exception cref="InvalidOperationException">The selector is not text-valued.<br/></exception>
            public IndexSelectorBuilder IgnoreCase(IndexCaseType type = IndexCaseType.CollatedAndFolded)
            {
                if (!isText)
                    throw new InvalidOperationException("IgnoreCase is only valid for text selectors.");
                if (type == IndexCaseType.None)
                    return Create(selector, isText, tokenSource, IndexCaseType.None, sort, valueType);
                return Create(selector, isText, tokenSource, type, sort, valueType);
            }

            /// <summary>Returns a new declaration carrying an explicit readable index-naming token without changing the selector's canonical semantic identity.<br/></summary>
            /// <param name="token">Naming token carried by the new declaration.<br/></param>
            /// <returns>A new declaration preserving the selector, text preference, and ordering.<br/></returns>
            public IndexSelectorBuilder WithToken(string token)
            {
                return Create(selector, isText, token, caseType, sort, valueType);
            }

            /// <summary>Gets a new declaration requesting ascending key order while preserving the other selector metadata.<br/></summary>
            public IndexSelectorBuilder Ascending => Create(selector, isText, tokenSource, caseType, IndexSortDirection.Ascending, valueType);
            /// <summary>Gets a new declaration requesting descending key order while preserving the other selector metadata.<br/></summary>
            public IndexSelectorBuilder Descending => Create(selector, isText, tokenSource, caseType, IndexSortDirection.Descending, valueType);

            /// <summary>Gets the explicit naming token, or the selector-derived source path or canonical identity when no token was supplied.<br/></summary>
            public string TokenSource => tokenSource ?? GetSelectorTokenSource(selector);
            /// <summary>Gets the key ordering direction carried by this declaration.<br/></summary>
            public IndexSortDirection Sort => sort;

            /// <summary>Returns the selector's canonical semantic identity independently of the readable naming token.<br/></summary>
            /// <returns>The validated canonical representation of the selector expression.<br/></returns>
            public string GetCanonicalSelectorIdentity() => ConditionDescriptor<T>.ToCanonicalSelectorString(selector);

            private protected virtual IndexSelectorBuilder Create(ISelector newSelector, bool newIsText, string? newTokenSource, IndexCaseType newCaseType, IndexSortDirection newSort, Type? newValueType)
            {
                // If the chain is no longer text-capable, drop text-specific options to avoid invalid variants.
                var normalizedCaseType = newIsText ? newCaseType : IndexCaseType.None;
                return new IndexSelectorBuilder(newSelector, newIsText, newTokenSource, normalizedCaseType, newSort, newValueType);
            }
        }

        /// <summary>
        /// Retains the exact CLR result type of a completed selector indexing declaration.<br/>
        /// The active factory closes this type during declaration construction; fluent metadata changes preserve that result type.<br/>
        /// </summary>
        internal sealed class TypedIndexSelectorBuilder<TProp> : IndexSelectorBuilder
        {
            internal TypedIndexSelectorBuilder(ISelector selector, bool isText, string? tokenSource, IndexCaseType caseType, IndexSortDirection sort, Type? valueType)
                : base(selector, isText, tokenSource, caseType, sort, valueType)
            {
            }

            private protected override IndexSelectorBuilder Create(ISelector newSelector, bool newIsText, string? newTokenSource, IndexCaseType newCaseType, IndexSortDirection newSort, Type? newValueType)
            {
                // Preserve typed builder when possible
                return new TypedIndexSelectorBuilder<TProp>(newSelector, newIsText, newTokenSource, newIsText ? newCaseType : IndexCaseType.None, newSort, newValueType ?? typeof(TProp));
            }
        }

        /// <summary>
        /// Selects whether a missing, null, or invalid calculated selector result participates through LibraDex's distinct null-key route.<br/>
        /// Invalid calculations are treated as missing results because a persistable selector is a total nullable projection over authoritative record values.<br/>
        /// </summary>
        public enum CalculatedIndexNullValues
        {
            /// <summary>Indexes missing, null, or invalid calculated results through the null-key route.<br/></summary>
            Include = 0,
            /// <summary>Omits missing, null, or invalid calculated results from the index.<br/></summary>
            Exclude = 1
        }

        /// <summary>
        /// Selects whether an empty calculated string remains a distinct indexed key or is omitted from the calculated index.<br/>
        /// This does not affect whitespace-only strings because trimming is an explicit selector operation.<br/>
        /// </summary>
        public enum CalculatedIndexEmptyStrings
        {
            /// <summary>Retains an empty calculated string as an indexed value distinct from null.<br/></summary>
            Include = 0,
            /// <summary>Omits empty calculated strings; whitespace-only strings are unaffected unless the selector trims them.<br/></summary>
            Exclude = 1
        }

        /// <summary>
        /// Selects the bounded LibraDex key shape used when a calculated selector returns <c>byte[]</c>.<br/>
        /// Variable-length keys accept payloads through 1,023 bytes so LibraDex's internal key-state byte remains inside its 1 KiB physical-key ceiling.<br/>
        /// Fixed keys require the selector to return exactly the selected width and retain LibraDex's uniform shelf layout.<br/>
        /// </summary>
        public enum CalculatedIndexBinaryShape
        {
            /// <summary>Uses variable-length binary keys with a maximum developer payload of 1,023 bytes.<br/></summary>
            VariableLength = 0,
            /// <summary>Requires every present binary key to contain exactly eight bytes.<br/></summary>
            Fixed8 = 8,
            /// <summary>Requires every present binary key to contain exactly sixteen bytes.<br/></summary>
            Fixed16 = 16,
            /// <summary>Requires every present binary key to contain exactly thirty-two bytes.<br/></summary>
            Fixed32 = 32
        }

        /// <summary>
        /// Defines the developer-owned participation and uniqueness contract for one calculated selector index.<br/>
        /// Selector result type, text representation, and direction remain part of the typed `.ForIndexing()` descriptor so normal creation requires no duplicated settings.<br/>
        /// </summary>
        public class CalculatedIndexOptions
        {
            /// <summary>Gets or initializes whether non-null calculated keys must be unique across record identities.<br/></summary>
            public bool Unique { get; init; }

            /// <summary>Gets or initializes whether missing/null/invalid calculated results use the LibraDex null-key route.<br/></summary>
            public CalculatedIndexNullValues NullValues { get; init; } = CalculatedIndexNullValues.Include;

            /// <summary>Gets or initializes whether empty calculated strings remain indexed distinctly from null and non-empty text.<br/></summary>
            public CalculatedIndexEmptyStrings EmptyStrings { get; init; } = CalculatedIndexEmptyStrings.Include;

            /// <summary>Gets or initializes the bounded physical key shape used only when the calculated selector returns <c>byte[]</c>.<br/></summary>
            public CalculatedIndexBinaryShape BinaryShape { get; init; } = CalculatedIndexBinaryShape.VariableLength;
        }
        /// <summary>
        /// Produces the readable token source for one direct or computed selector without rendering backend syntax.<br/>
        /// Direct selectors use their normalized source property path; computed selectors use their validated durable canonical identity.<br/>
        /// </summary>
        /// <param name="selector">Selector whose readable index-name token is required.<br/></param>
        /// <returns>The normalized source path or complete canonical semantic selector identity.<br/></returns>
        internal static string GetSelectorTokenSource(ISelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (selector is ISelectorStructure structure &&
                !string.IsNullOrWhiteSpace(structure.TypedSourcePropertyPath))
            {
                return structure.TypedSourcePropertyPath;
            }

            return ConditionDescriptor<T>.ToCanonicalSelectorString(selector);
        }

        private static Type NormalizeValueType(Type type)
        {
            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target.IsEnum)
                return Enum.GetUnderlyingType(target);
            return target;
        }
        /// <summary>
        /// </summary>
        /// <param name="input">The raw input string to sanitize. May be <c>null</c> or whitespace; in that case a stable default token is returned.</param>
        /// <param name="maxTokenLength">
        /// Maximum allowed length for the returned token. When the sanitized value exceeds this length a deterministic short
        /// hex suffix (derived from the SHA‑256 of the original input) is appended to a truncated base to preserve uniqueness.
        /// </param>
        /// <returns>
        /// A lowercased token that contains only ASCII letters and digits and underscores. Runs of non-alphanumeric characters
        /// are replaced with a single underscore and leading/trailing underscores are removed. If the cleaned token is empty,
        /// the stable identifier <c>"col"</c> is returned. If the cleaned token is longer than <paramref name="maxTokenLength"/>,
        /// the token is truncated and a short deterministic 8-hex-character suffix is appended to avoid collisions.
        /// </returns>
        /// <remarks>
        ///   collision-resistant for long or complex input strings (for example JSON property paths or expressions).
        /// - The deterministic suffix uses the first 4 bytes of SHA‑256, encoded in lower-case hex. This choice trades
        ///   a small probability of collision for compactness; callers that require guaranteed uniqueness should include
        ///   additional entropy in the canonical input or persist metadata outside of the name.
        /// - The method never throws for <c>null</c> or whitespace input; it returns the stable token <c>"col"</c>.
        /// </remarks>
        private static string SanitizeTokenForIndex(string input, int maxTokenLength = 48)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "col";

            // map anything not alnum to underscore
            var t = Regex.Replace(input, @"[^A-Za-z0-9]+", "_");
            // collapse runs of underscores
            t = Regex.Replace(t, @"_+", "_");
            t = t.Trim('_').ToLowerInvariant();
            if (string.IsNullOrEmpty(t))
                t = "col";

            if (t.Length <= maxTokenLength)
                return t;

            // short deterministic hash suffix
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var hex = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant(); // 8 hex chars
            return t.Substring(0, Math.Max(0, maxTokenLength - 9)) + "_" + hex;
        }

        /// <summary>
        /// Creates an Abraxas ordered index for a property path.<br/>
        /// The key path decides which object value is indexed; the optional condition decides which object identities participate in the index.<br/>
        /// String keys use TextIndexProfile.Exact and share the same definition as the explicit Exact profile overload.<br/>
        /// </summary>
        /// <param name="key">Property path that provides the index key.<br/></param>
        /// <param name="direction">Physical direction support requested for the index.<br/></param>
        /// <param name="unique">When true, the index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional Abraxas object-evaluation filter for partial index membership.<br/></param>
        /// <returns><c>true</c> when index creation succeeded; otherwise <c>false</c>.<br/></returns>
        public bool CreateIndex(PropPath key, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null)
        {
            if (string.IsNullOrWhiteSpace(key.Path))
                throw new ArgumentException("Index key path cannot be empty.", nameof(key));

            if (!TryResolveSimplePropertyType(key.Path, out var keyType))
                throw new InvalidOperationException($"Index key path '{key.Path}' does not resolve to a simple field or property on '{typeof(T).FullName}'.");

            if ((uint)direction > (uint)IndexDirection.Bidirectional)
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown index direction.");

            keyType = NormalizeValueType(keyType);
            if (keyType == typeof(string))
                return CreateTextIndexCore(key, TextIndexProfile.Exact, direction, unique, where, indexName: null, useMemoryIndex: false);

            if (where is not null)
                return CreateStaticPartialIndex(
                    key.Path,
                    unique,
                    where,
                    logicalName: null,
                    textProfile: null,
                    direction);

            if (!TryRegisterLibraDexSimplePathIndex(key.Path, unique, direction))
                throw new NotSupportedException($"Index key path '{key.Path}' resolves to '{keyType.FullName}', which is not currently supported by the ordered backend.");

            return true;
        }

        /// <summary>
        /// Creates an Abraxas ordered index for the property path identified by an expression.<br/>
        /// This overload keeps the public call shape compact while still requiring explicit physical direction intent.<br/>
        /// String keys use TextIndexProfile.Exact; selecting a different profile requires the profile overload during index setup.<br/>
        /// </summary>
        /// <typeparam name="TValue">Value type produced by the key expression.<br/></typeparam>
        /// <param name="key">Expression that identifies the property path used as the index key.<br/></param>
        /// <param name="direction">Physical direction support requested for the index.<br/></param>
        /// <param name="unique">When true, the index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional Abraxas object-evaluation filter for partial index membership.<br/></param>
        /// <returns><c>true</c> when index creation succeeded; otherwise <c>false</c>.<br/></returns>
        public bool CreateIndex<TValue>(Expression<Func<T, TValue>> key, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null)
        {
            ArgumentNullException.ThrowIfNull(key);
            return CreateIndex(PropPath.FromExpression(key), direction, unique, where);
        }

        /// <summary>
        /// Creates a persistent ordered index over one direct or computed `.ForIndexing()` selector.<br/>
        /// The selector is captured into durable backend-neutral intent, evaluated from authoritative Inheto payloads during population and mutation, and recreated automatically when the record base reopens.<br/>
        /// Runtime-only `.External(...)` selectors are rejected because application delegates cannot be reconstructed safely.<br/>
        /// </summary>
        /// <param name="selector">Typed selector indexing descriptor, including its text profile and ordering direction.<br/></param>
        /// <param name="options">Optional uniqueness and null/empty participation controls; omitted values use predictable inclusive defaults.<br/></param>
        /// <returns><see langword="true"/> when the calculated index is registered and populated.<br/></returns>
        public bool CreateExpressionIndex(IndexSelectorBuilder selector, CalculatedIndexOptions? options = null)
            => CreateCalculatedIndexCore(selector, options, useMemoryIndex: false);

        /// <summary>
        /// Creates a persistent expression index from an Inheto/CLR property path such as <c>.CompanyName.Length</c>.<br/>
        /// The path itself is the semantic name later accepted by <c>Where.Index(path)</c>; Abraxas infers the exact result family and retains existing scalar-index efficiencies.<br/>
        /// </summary>
        /// <param name="semanticPath">Resolvable field, property, or admitted parameterless-member path.<br/></param>
        /// <param name="direction">Physical ordered traversal direction maintained by LibraDex.<br/></param>
        /// <param name="caseType">Text profile used only when the resolved result is a string.<br/></param>
        /// <param name="unique">Whether keys must be unique.<br/></param>
        /// <returns><see langword="true"/> after the inferred persistent index is populated.<br/></returns>
        public bool CreateExpressionIndex(
            string semanticPath,
            IndexDirection direction = IndexDirection.Ascending,
            IndexCaseType caseType = IndexCaseType.None,
            bool unique = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticPath);
            if (!TryResolveSimplePropertyType(semanticPath, out Type resultType, out Inheto.PropPathCheck check))
            {
                throw new InvalidOperationException(
                    $"Expression index path '{semanticPath}' is not resolvable from '{typeof(T).FullName}': {check.FailureKind}" +
                    (string.IsNullOrEmpty(check.FailedSegment) ? "." : $" at segment '{check.FailedSegment}'."));
            }

            resultType = NormalizeValueType(resultType);
            if (resultType == typeof(string))
            {
                TextIndexProfile profile = ToTextIndexProfile(caseType, ignoreCase: false);
                return CreateIndex(new PropPath(semanticPath), profile, direction, unique);
            }
            if (caseType != IndexCaseType.None)
                throw new InvalidOperationException("Expression-index case options apply only to string results.");
            return CreateIndex(new PropPath(semanticPath), direction, unique);
        }

        /// <summary>
        /// Compatibility forwarder preserving the previous calculated-selector creation syntax.<br/>
        /// </summary>
        /// <summary>
        /// Creates a volatile ordered index over one direct or computed `.ForIndexing()` selector.<br/>
        /// The same durable semantic capture boundary is enforced as persistent creation, while physical tuples and registration disappear when this store instance closes.<br/>
        /// </summary>
        /// <param name="selector">Typed selector indexing descriptor, including its text profile and ordering direction.<br/></param>
        /// <param name="options">Optional uniqueness and null/empty participation controls; omitted values use predictable inclusive defaults.<br/></param>
        /// <returns><see langword="true"/> when the volatile calculated index is registered and populated.<br/></returns>
        public bool CreateMemoryIndex(IndexSelectorBuilder selector, CalculatedIndexOptions? options = null)
            => CreateCalculatedIndexCore(selector, options, useMemoryIndex: true);

        /// <summary>
        /// Creates the memory-only counterpart of a semantic-path expression index, such as <c>.CompanyName.Length</c>.<br/>
        /// The exact result type is inferred from the path and indexed through the same volatile scalar or text route used by direct memory indexes.<br/>
        /// Its keys and registration do not survive store close; call this method again after reopening when the index is needed.<br/>
        /// </summary>
        /// <param name="semanticPath">Resolvable field, property, or admitted parameterless-member path used as the semantic index name.<br/></param>
        /// <param name="direction">Requested physical ordered traversal direction.<br/></param>
        /// <param name="caseType">Text profile when the resolved result is a string; otherwise <see cref="IndexCaseType.None"/>.<br/></param>
        /// <param name="unique">Whether complete non-null keys must be unique across records.<br/></param>
        /// <returns><see langword="true"/> after the volatile expression index is populated.<br/></returns>
        public bool CreateMemoryExpressionIndex(
            string semanticPath,
            IndexDirection direction = IndexDirection.Ascending,
            IndexCaseType caseType = IndexCaseType.None,
            bool unique = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticPath);
            if (!TryResolveSimplePropertyType(semanticPath, out Type resultType, out Inheto.PropPathCheck check))
            {
                throw new InvalidOperationException(
                    $"Expression index path '{semanticPath}' is not resolvable from '{typeof(T).FullName}': {check.FailureKind}" +
                    (string.IsNullOrEmpty(check.FailedSegment) ? "." : $" at segment '{check.FailedSegment}'."));
            }

            resultType = NormalizeValueType(resultType);
            if (resultType == typeof(string))
                return CreateMemoryIndex(new PropPath(semanticPath), ToTextIndexProfile(caseType, ignoreCase: false), direction, unique);
            if (caseType != IndexCaseType.None)
                throw new InvalidOperationException("Expression-index case options apply only to string results.");
            return CreateMemoryIndex(new PropPath(semanticPath), direction, unique);
        }

        /// <summary>
        /// Validates, names, and registers one calculated selector index without exposing LibraDex catalog mechanics.<br/>
        /// Canonical semantic bytes determine identity; a developer token affects readability only and cannot cause two different selectors to alias.<br/>
        /// </summary>
        /// <param name="selector">Typed selector indexing descriptor.<br/></param>
        /// <param name="options">Optional uniqueness and participation contract.<br/></param>
        /// <param name="useMemoryIndex">Whether the physical index belongs to the store-owned volatile catalog.<br/></param>
        /// <returns><see langword="true"/> after registration and population.<br/></returns>
        private bool CreateCalculatedIndexCore(
            IndexSelectorBuilder selector,
            CalculatedIndexOptions? options,
            bool useMemoryIndex)
        {
            ArgumentNullException.ThrowIfNull(selector);
            options ??= new CalculatedIndexOptions();
            if (!Enum.IsDefined(options.NullValues))
                throw new ArgumentOutOfRangeException(nameof(options), options.NullValues, "Unknown calculated-index null participation policy.");
            if (!Enum.IsDefined(options.EmptyStrings))
                throw new ArgumentOutOfRangeException(nameof(options), options.EmptyStrings, "Unknown calculated-index empty-string participation policy.");
            if (!Enum.IsDefined(options.BinaryShape))
                throw new ArgumentOutOfRangeException(nameof(options), options.BinaryShape, "Unknown calculated-index binary key shape.");

            SelectorDescriptor<T> descriptor = SelectorDescriptor<T>.FromSelector(selector.CalculatedSelector);
            Type resultType = NormalizeValueType(descriptor.ResultType);
            if (selector.DeclaredValueType is not null &&
                NormalizeValueType(selector.DeclaredValueType) != resultType)
            {
                throw new InvalidOperationException(
                    $"Calculated selector declares '{selector.DeclaredValueType.FullName}' but its durable result is '{resultType.FullName}'.");
            }
            if (selector.IsTextResult != (resultType == typeof(string)))
                throw new InvalidOperationException("Calculated selector text metadata does not match its exact durable result type.");
            if (resultType != typeof(byte[]) && options.BinaryShape != CalculatedIndexBinaryShape.VariableLength)
                throw new InvalidOperationException("CalculatedIndexOptions.BinaryShape applies only to selectors whose durable result type is byte[].");

            TextIndexProfile profile = resultType == typeof(string)
                ? ToTextIndexProfile(selector.CaseType, ignoreCase: false)
                : TextIndexProfile.Exact;
            IndexDirection direction = selector.Sort == IndexSortDirection.Descending
                ? IndexDirection.Descending
                : IndexDirection.Ascending;
            string fingerprint = descriptor.SemanticFingerprint.ToLowerInvariant();
            string token = SanitizeTokenForIndex(selector.TokenSource, 32);
            string logicalName = $"cidx_{name}_{token}_{fingerprint[..12]}";
            bool registered = TryRegisterLibraDexCalculatedIndex(
                logicalName,
                logicalName,
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
                populateExisting: true);
            if (!registered)
            {
                throw new NotSupportedException(
                    $"Calculated selector result type '{resultType.FullName}' does not have a supported ordered representation.");
            }
            return true;
        }

        /// <summary>
        /// Converts the public calculated-binary creation choice into the durable Abraxas storage-shape contract.<br/>
        /// Non-binary selector results explicitly carry no binary storage shape.<br/>
        /// </summary>
        private static CalculatedBinaryKeyStorage ToCalculatedBinaryKeyStorage(Type resultType, CalculatedIndexBinaryShape shape)
        {
            if (resultType != typeof(byte[]))
                return CalculatedBinaryKeyStorage.None;

            return shape == CalculatedIndexBinaryShape.VariableLength
                ? CalculatedBinaryKeyStorage.VariableLength
                : CalculatedBinaryKeyStorage.FixedLength;
        }

        /// <summary>
        /// Resolves the developer-facing calculated-binary payload bound or exact fixed width without exposing LibraDex's reserved variable-key state byte.<br/>
        /// Non-binary selector results return zero because they do not own a binary sizing contract.<br/>
        /// </summary>
        private static int GetCalculatedBinaryKeyBytes(Type resultType, CalculatedIndexBinaryShape shape)
        {
            if (resultType != typeof(byte[]))
                return 0;

            return shape == CalculatedIndexBinaryShape.VariableLength ? 1023 : (int)shape;
        }

        /// <summary>
        /// Creates a volatile Abraxas ordered index for a property path.<br/>
        /// The index key material is kept in memory and is lost when the application closes or this store instance is closed.<br/>
        /// This is intended for values that should remain queryable while the process runs without persisting decrypted or sensitive index keys to disk.<br/>
        /// </summary>
        /// <param name="key">Property path that provides the volatile index key.<br/></param>
        /// <param name="direction">Physical direction support requested for the volatile index.<br/></param>
        /// <param name="unique">When true, the volatile index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional complete static membership condition for a volatile partial index.<br/></param>
        /// <returns><c>true</c> when the volatile ordered index was created; otherwise <c>false</c>.<br/></returns>
        public bool CreateMemoryIndex(PropPath key, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null)
        {
            if (string.IsNullOrWhiteSpace(key.Path))
                throw new ArgumentException("Index key path cannot be empty.", nameof(key));

            if (!TryResolveSimplePropertyType(key.Path, out var keyType))
                throw new InvalidOperationException($"Index key path '{key.Path}' does not resolve to a simple field or property on '{typeof(T).FullName}'.");

            if ((uint)direction > (uint)IndexDirection.Bidirectional)
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown index direction.");

            if (NormalizeValueType(keyType) == typeof(string))
                return CreateTextIndexCore(key, TextIndexProfile.Exact, direction, unique, where, indexName: null, useMemoryIndex: true);

            if (where is not null)
                return CreateStaticPartialIndex(key.Path, unique, where, logicalName: null, textProfile: null, direction, libraDexVolatileCatalog);

            if (!TryRegisterLibraDexMemorySimplePathIndex(key.Path, unique, direction))
                throw new NotSupportedException($"Memory index key path '{key.Path}' uses a key type that is not currently supported by the ordered backend.");

            return true;
        }

        /// <summary>
        /// Creates a volatile Abraxas ordered index for the property path identified by an expression.<br/>
        /// The index key material is kept in memory and is lost when the application closes or this store instance is closed.<br/>
        /// This is intended for values that should remain queryable while the process runs without persisting decrypted or sensitive index keys to disk.<br/>
        /// </summary>
        /// <typeparam name="TValue">Value type produced by the key expression.<br/></typeparam>
        /// <param name="key">Expression that identifies the property path used as the volatile index key.<br/></param>
        /// <param name="direction">Physical direction support requested for the volatile index.<br/></param>
        /// <param name="unique">When true, the volatile index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional complete static membership condition for a volatile partial index.<br/></param>
        /// <returns><c>true</c> when the volatile ordered index was created; otherwise <c>false</c>.<br/></returns>
        public bool CreateMemoryIndex<TValue>(Expression<Func<T, TValue>> key, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null)
        {
            ArgumentNullException.ThrowIfNull(key);
            return CreateMemoryIndex(PropPath.FromExpression(key), direction, unique, where);
        }

        /// <summary>
        /// Creates a numeric index on the supplied property path.
        /// </summary>
        /// <param name="propPath">Property path (e.g. ".Age").</param>
        /// <param name="unique">When true, creates a UNIQUE index.</param>
        /// <param name="where">Optional partial index predicate.</param>
        /// <param name="indexName">Optional explicit index name.</param>
        /// <param name="useMemoryIndex">When true, creates the numeric index in the store's volatile memory catalog; otherwise uses persistent storage.<br/></param>
        /// <returns>true when the index was created; throws on error or name collision.</returns>
        /// <example>
        /// <code><![CDATA[
        /// store.CreateIndexNumeric(".Age");
        /// ]]></code>
        /// </example>
        public bool CreateIndexNumeric(string propPath, bool unique = false, Condition<T>.EndCondition? where = null, string? indexName = null, bool useMemoryIndex = false)
        {
            if (string.IsNullOrWhiteSpace(propPath))
                throw new ArgumentNullException(nameof(propPath));

            if (where is null && !string.IsNullOrWhiteSpace(indexName))
                throw new NotSupportedException("Custom physical names are not part of the Abraxas index contract; use the returned logical index name for management.");

            if (!TryResolveSimplePropertyType(propPath, out var keyType))
                throw new InvalidOperationException($"Numeric index key path '{propPath}' does not resolve to a simple field or property on '{typeof(T).FullName}'.");

            keyType = NormalizeValueType(keyType);
            var isNumeric = keyType == typeof(byte) ||
                keyType == typeof(sbyte) ||
                keyType == typeof(short) ||
                keyType == typeof(ushort) ||
                keyType == typeof(int) ||
                keyType == typeof(uint) ||
                keyType == typeof(long) ||
                keyType == typeof(ulong) ||
                keyType == typeof(float) ||
                keyType == typeof(double) ||
                keyType == typeof(decimal);
            if (!isNumeric)
                throw new InvalidOperationException($"Numeric index key path '{propPath}' resolves to '{keyType.FullName}', which is not a supported numeric scalar.");

            if (where is not null)
            {
                return CreateStaticPartialIndex(
                    propPath,
                    unique,
                    where,
                    indexName,
                    textProfile: null,
                    IndexDirection.Ascending,
                    useMemoryIndex ? libraDexVolatileCatalog : null);
            }

            var registered = useMemoryIndex
                ? TryRegisterLibraDexMemorySimplePathIndex(propPath, unique)
                : TryRegisterLibraDexSimplePathIndex(propPath, unique);
            if (!registered)
                throw new NotSupportedException($"Numeric index key path '{propPath}' uses a key type that is not currently supported by the ordered backend.");

            return true;
        }

        /// <summary>
        /// Creates an Abraxas text index for a property path.<br/>
        /// The text profile decides which exact, folded, normalized, and sort-key representations are maintained for supported lookup and ordering work.<br/>
        /// Exact is equivalent to the ordinary string overload; non-equivalent existing definitions require explicit removal and recreation rather than implicit conversion.<br/>
        /// </summary>
        /// <param name="key">Property path that provides the text key.<br/></param>
        /// <param name="profile">Text representations maintained by the index.<br/></param>
        /// <param name="direction">Physical direction support requested for the text index.<br/></param>
        /// <param name="unique">When true, the primary Abraxas index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional Abraxas object-evaluation filter for partial index membership.<br/></param>
        /// <param name="indexName">Optional logical management name for a partial index; ordinary indexes use their source path and reject custom names.<br/></param>
        /// <param name="sortKeyProfiles">Optional explicit culture and comparison-options subindexes; supported only for ordinary indexes, not partial membership.<br/></param>
        /// <returns><c>true</c> when index creation succeeded; otherwise <c>false</c>.<br/></returns>
        public bool CreateIndex(PropPath key, TextIndexProfile profile, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null, string? indexName = null, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
            => CreateTextIndexCore(key, profile, direction, unique, where, indexName, useMemoryIndex: false, sortKeyProfiles: sortKeyProfiles);

        /// <summary>
        /// Creates an Abraxas text index for the property path identified by an expression.<br/>
        /// This overload keeps the public call shape compact while still making text profile and direction intent explicit.<br/>
        /// </summary>
        /// <param name="key">Expression that identifies the text property path used as the index key.<br/></param>
        /// <param name="profile">Text representations maintained by the index.<br/></param>
        /// <param name="direction">Physical direction support requested for the text index.<br/></param>
        /// <param name="unique">When true, the primary Abraxas index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional Abraxas object-evaluation filter for partial index membership.<br/></param>
        /// <param name="indexName">Optional logical management name for a partial index; ordinary indexes use their source path and reject custom names.<br/></param>
        /// <param name="sortKeyProfiles">Optional explicit culture and comparison-options subindexes; supported only for ordinary indexes, not partial membership.<br/></param>
        /// <returns><c>true</c> when index creation succeeded; otherwise <c>false</c>.<br/></returns>
        public bool CreateIndex(Expression<Func<T, string>> key, TextIndexProfile profile, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null, string? indexName = null, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
        {
            ArgumentNullException.ThrowIfNull(key);
            return CreateIndex(PropPath.FromExpression(key), profile, direction, unique, where, indexName, sortKeyProfiles);
        }

        /// <summary>
        /// Creates a volatile Abraxas text index for a property path.<br/>
        /// The index key material is kept in memory and is lost when the application closes or this store instance is closed.<br/>
        /// </summary>
        /// <param name="key">Property path that provides the volatile text key.<br/></param>
        /// <param name="profile">Text representations maintained by the volatile index.<br/></param>
        /// <param name="direction">Physical direction support requested for the volatile text index.<br/></param>
        /// <param name="unique">When true, the primary Abraxas index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional complete static membership condition for a volatile partial index.<br/></param>
        /// <param name="sortKeyProfiles">Optional explicit culture and comparison-options subindexes maintained in memory; supported only for ordinary indexes.<br/></param>
        /// <param name="indexName">Optional logical management name when <paramref name="where"/> defines a partial index.<br/></param>
        /// <returns><c>true</c> when the volatile text ordered index was created; otherwise <c>false</c>.<br/></returns>
        public bool CreateMemoryIndex(PropPath key, TextIndexProfile profile, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null, string? indexName = null, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
        {
            return CreateTextIndexCore(key, profile, direction, unique, where, indexName, useMemoryIndex: true, sortKeyProfiles: sortKeyProfiles);
        }

        /// <summary>
        /// Creates a volatile Abraxas text index for the property path identified by an expression.<br/>
        /// The index key material is kept in memory and is lost when the application closes or this store instance is closed.<br/>
        /// </summary>
        /// <param name="key">Expression that identifies the text property path used as the volatile index key.<br/></param>
        /// <param name="profile">Text representations maintained by the volatile index.<br/></param>
        /// <param name="direction">Physical direction support requested for the volatile text index.<br/></param>
        /// <param name="unique">When true, the primary Abraxas index should enforce unique keys where supported.<br/></param>
        /// <param name="where">Optional complete static membership condition for a volatile partial index.<br/></param>
        /// <param name="sortKeyProfiles">Optional explicit culture and comparison-options subindexes maintained in memory; supported only for ordinary indexes.<br/></param>
        /// <param name="indexName">Optional logical management name when <paramref name="where"/> defines a partial index.<br/></param>
        /// <returns><c>true</c> when the volatile text ordered index was created; otherwise <c>false</c>.<br/></returns>
        public bool CreateMemoryIndex(Expression<Func<T, string>> key, TextIndexProfile profile, IndexDirection direction, bool unique = false, Condition<T>.EndCondition? where = null, string? indexName = null, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
        {
            ArgumentNullException.ThrowIfNull(key);
            return CreateMemoryIndex(PropPath.FromExpression(key), profile, direction, unique, where, indexName, sortKeyProfiles);
        }

        /// <summary>
        /// The selector view's CLR result type must exactly match the normalized source member type, preventing type-only views from changing index or grouping semantics.<br/>
        /// Computed selectors, untyped selectors, unresolved paths, and mismatched views are rejected conservatively.<br/>
        /// </summary>
        /// <param name="selector">Selector supplied to an original-name backend-neutral API.<br/></param>
        /// <param name="key">Resolved normalized property path when successful.<br/></param>
        /// <param name="keyType">Normalized CLR source-member type when successful.<br/></param>
        /// <returns><c>true</c> only for a structurally direct, type-compatible property selector; otherwise <c>false</c>.<br/></returns>
        private static bool TryResolveSimpleSelectorPath(
            ISelector selector,
            out PropPath key,
            out Type keyType)
        {
            key = default;
            keyType = typeof(object);
            if (selector is not ISelectorStructure structure ||
                string.IsNullOrWhiteSpace(structure.TypedSourcePropertyPath) ||
                structure.ResultType is null ||
                !TryResolveSimplePropertyType(structure.TypedSourcePropertyPath, out keyType))
            {
                return false;
            }

            keyType = NormalizeValueType(keyType);
            if (NormalizeValueType(structure.ResultType) != keyType)
                return false;

            key = new PropPath(structure.TypedSourcePropertyPath);
            return true;
        }

        /// <summary>
        /// Validates one string-key declaration and routes ordinary or partial creation to the existing persistent or volatile index lifecycle.<br/>
        /// Plain string declarations pass Exact, so public overload choice cannot change the durable definition or physical projection shape.<br/>
        /// </summary>
        /// <param name="key">Actual string property or field path.<br/></param>
        /// <param name="profile">Required maintained text representations.<br/></param>
        /// <param name="direction">Required physical direction contract.<br/></param>
        /// <param name="unique">Whether non-null keys must be unique.<br/></param>
        /// <param name="where">Optional complete partial membership condition.<br/></param>
        /// <param name="indexName">Optional logical name for a partial index only.<br/></param>
        /// <param name="useMemoryIndex">Whether to use the volatile catalog.<br/></param>
        /// <param name="sortKeyProfiles">Optional explicit ordinary-index culture profiles.<br/></param>
        /// <returns>True for ordinary registration, or the existing partial creation result.<br/></returns>
        private bool CreateTextIndexCore(PropPath key, TextIndexProfile profile, IndexDirection direction, bool unique, Condition<T>.EndCondition? where, string? indexName, bool useMemoryIndex, IReadOnlyList<TextSortProfile>? sortKeyProfiles = null)
        {
            if (string.IsNullOrWhiteSpace(key.Path))
                throw new ArgumentException("Index key path cannot be empty.", nameof(key));

            if (!TryResolveSimplePropertyType(key.Path, out var keyType))
                throw new InvalidOperationException($"Text index key path '{key.Path}' does not resolve to a simple field or property on '{typeof(T).FullName}'.");

            keyType = NormalizeValueType(keyType);
            if (keyType != typeof(string))
                throw new InvalidOperationException($"Text index key path '{key.Path}' resolves to '{keyType.FullName}', not '{typeof(string).FullName}'.");

            if ((uint)direction > (uint)IndexDirection.Bidirectional)
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown index direction.");

            _ = ToLibraDexStringKeys(profile);

            if (where is null && !string.IsNullOrWhiteSpace(indexName))
                throw new NotSupportedException("Custom physical names are not part of the Abraxas text-index contract; use the returned logical index name for management.");

            if (where is not null)
            {
                if (sortKeyProfiles is { Count: > 0 })
                    throw new NotSupportedException("Culture sort-key profiles currently require a complete ordinary text index; partial index population cannot define complete OpenSorted order.");
                return CreateStaticPartialIndex(
                    key.Path,
                    unique,
                    where,
                    indexName,
                    profile,
                    direction,
                    useMemoryIndex ? libraDexVolatileCatalog : null);
            }

            if (useMemoryIndex)
            {
                if (!TryRegisterLibraDexMemoryTextPathIndex(key.Path, profile, direction, unique, sortKeyProfiles))
                    throw new NotSupportedException($"Memory text index key path '{key.Path}' uses a key type that is not currently supported by the ordered backend.");

                return true;
            }

            if (!TryRegisterLibraDexTextPathIndex(key.Path, profile, direction, unique, sortKeyProfiles))
                throw new NotSupportedException($"Text index key path '{key.Path}' uses a profile or key type that is not currently supported by the ordered backend.");

            return true;
        }

        /// <summary>
        /// Creates a persistent ordered index over the match value produced from one string property path.<br/>
        /// Abraxas evaluates the pattern during initial population and every later insert, overwrite, and delete; LibraDex stores only the calculated text key and record identity.<br/>
        /// </summary>
        /// <param name="source">The simple string property path whose value supplies pattern input.<br/></param>
        /// <param name="pattern">The regular expression used to select a whole match or configured capture group.<br/></param>
        /// <param name="options">Optional capture, occurrence, text-profile, direction, and validation options.<br/></param>
        /// <param name="unique">When <see langword="true"/>, requires unique non-null calculated keys.<br/></param>
        /// <returns><see langword="true"/> when the calculated index is registered and populated.<br/></returns>
        /// <exception cref="ArgumentNullException">Thrown when the property path is empty or <paramref name="pattern"/> is <see langword="null"/>.<br/></exception>
        /// <exception cref="ArgumentException">Thrown when capture or occurrence selection is ambiguous or invalid.<br/></exception>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="source"/> does not resolve to a string member.<br/></exception>
        /// <exception cref="NotSupportedException">Thrown when the active ordered backend cannot represent the requested projection.<br/></exception>
        public bool CreateIndex(PropPath source, Regex pattern, MatchValueIndexOptions? options = null, bool unique = false)
            => CreateMatchValueIndexCore(unique, source.Path, pattern, options, useMemoryIndex: false);

        /// <summary>
        /// Creates a persistent ordered index over the match value produced from one string property selected by an expression.<br/>
        /// Abraxas maintains the calculated key during backfill and every later mutation while LibraDex stores and orders the resulting text value.<br/>
        /// </summary>
        /// <param name="source">Expression identifying the string source property.<br/></param>
        /// <param name="pattern">Regular expression that produces the whole-match or selected capture value.<br/></param>
        /// <param name="options">Optional capture, occurrence, text-profile, direction, and validation options.<br/></param>
        /// <param name="unique">Whether non-null calculated keys must be unique.<br/></param>
        /// <returns><see langword="true"/> when the calculated index is created and populated.<br/></returns>
        public bool CreateIndex(Expression<Func<T, string>> source, Regex pattern, MatchValueIndexOptions? options = null, bool unique = false)
        {
            ArgumentNullException.ThrowIfNull(source);
            return CreateIndex(PropPath.FromExpression(source), pattern, options, unique);
        }

        /// <summary>
        /// Creates a volatile ordered index over the match value produced from one string property.<br/>
        /// Calculated keys remain in the store-owned memory catalog and disappear when the store closes.<br/>
        /// </summary>
        /// <param name="source">String property path supplying pattern input.<br/></param>
        /// <param name="pattern">Regular expression that produces the whole-match or selected capture value.<br/></param>
        /// <param name="options">Optional capture, occurrence, text-profile, direction, and validation options.<br/></param>
        /// <param name="unique">Whether non-null calculated keys must be unique.<br/></param>
        /// <returns><see langword="true"/> when the volatile calculated index is created and populated.<br/></returns>
        public bool CreateMemoryIndex(PropPath source, Regex pattern, MatchValueIndexOptions? options = null, bool unique = false)
            => CreateMatchValueIndexCore(unique, source.Path, pattern, options, useMemoryIndex: true);

        /// <summary>
        /// Creates a volatile ordered index over the match value produced from one string property selected by an expression.<br/>
        /// This overload retains the same calculated-key contract as the persistent overload without exposing catalog mechanics.<br/>
        /// </summary>
        /// <param name="source">Expression identifying the string source property.<br/></param>
        /// <param name="pattern">Regular expression that produces the whole-match or selected capture value.<br/></param>
        /// <param name="options">Optional capture, occurrence, text-profile, direction, and validation options.<br/></param>
        /// <param name="unique">Whether non-null calculated keys must be unique.<br/></param>
        /// <returns><see langword="true"/> when the volatile calculated index is created and populated.<br/></returns>
        public bool CreateMemoryIndex(Expression<Func<T, string>> source, Regex pattern, MatchValueIndexOptions? options = null, bool unique = false)
        {
            ArgumentNullException.ThrowIfNull(source);
            return CreateMemoryIndex(PropPath.FromExpression(source), pattern, options, unique);
        }

        private bool CreateMatchValueIndexCore(bool unique, string propPath, Regex rx, MatchValueIndexOptions? settings, bool useMemoryIndex)
        {
            if (string.IsNullOrWhiteSpace(propPath))
                throw new ArgumentNullException(nameof(propPath));
            ArgumentNullException.ThrowIfNull(rx);

            if (settings is null)
            {
                var groupNumbers = rx.GetGroupNumbers() ?? Array.Empty<int>();
                if (Math.Max(0, groupNumbers.Length - 1) >= 1)
                    throw new ArgumentException("Ambiguous pattern: capture groups require MatchValueIndexOptions.ForGroupName(...) or ForGroupNumber(...).");
            }
            else
            {
                settings.Validate(rx);
            }

            var normalizedPath = new PropPath(propPath).Path;
            if (!TryResolveSimplePropertyType(normalizedPath, out var sourceType))
                throw new InvalidOperationException($"Regex index source path '{normalizedPath}' does not resolve to a simple field or property on '{typeof(T).FullName}'.");

            sourceType = NormalizeValueType(sourceType);
            if (sourceType != typeof(string))
                throw new InvalidOperationException($"Regex index source path '{normalizedPath}' resolves to '{sourceType.FullName}', not '{typeof(string).FullName}'.");

            var indexType = settings?.IndexType switch
            {
                MatchValueIndexOptions.IndexTypeEnum.CollationKey => IndexCaseType.Collated,
                MatchValueIndexOptions.IndexTypeEnum.FoldedText => IndexCaseType.Folded,
                MatchValueIndexOptions.IndexTypeEnum.CollatedAndFoldedText => IndexCaseType.CollatedAndFolded,
                _ => settings?.IgnoreCase == true ? IndexCaseType.CollatedAndFolded : IndexCaseType.None
            };
            int groupNumber = settings?.SelectedGroupNumber
                ?? (!string.IsNullOrEmpty(settings?.SelectedGroupName)
                    ? rx.GroupNumberFromName(settings.SelectedGroupName)
                    : 0);
            var selector = new Selector<T>()
                .PropPath(normalizedPath)
                .AsString
                .MatchValue(rx, groupNumber, settings?.Occurrence)
                .ForIndexing(
                    indexType,
                    settings?.Descending == true
                        ? IndexSortDirection.Descending
                        : IndexSortDirection.Ascending,
                    tokenSource: $"{normalizedPath}_match_value");
            var calculatedOptions = new CalculatedIndexOptions
            {
                Unique = unique,
                NullValues = CalculatedIndexNullValues.Include,
                EmptyStrings = CalculatedIndexEmptyStrings.Include
            };
            return useMemoryIndex
                ? CreateMemoryIndex(selector, calculatedOptions)
                : CreateExpressionIndex(selector, calculatedOptions);
        }

        private static TextIndexProfile ToTextIndexProfile(IndexCaseType indexType, bool ignoreCase)
        {
            return indexType switch
            {
                IndexCaseType.CollatedAndFolded => TextIndexProfile.ExactFoldedAndSortKey,
                IndexCaseType.Collated => TextIndexProfile.ExactAndSortKey,
                IndexCaseType.Folded => TextIndexProfile.ExactAndFolded,
                _ => ignoreCase ? TextIndexProfile.ExactFoldedAndSortKey : TextIndexProfile.Exact
            };
        }

        /// <summary>
        /// Persistent scalar, text, binary, composite, custom-codec, regex-projection, and partial indexes are returned with their volatile counterparts through one backend-neutral surface.<br/>
        /// </summary>
        /// <param name="useMemoryIndex">When <see langword="true"/>, returns only volatile indexes; otherwise returns persistent and volatile indexes.<br/></param>
        /// <returns>A stable snapshot-backed sequence of Abraxas index metadata.<br/></returns>
        public IEnumerable<IndexInfo> GetIndexes(bool useMemoryIndex = false)
        {
            using var operation = EnterStoreOperationScope();
            if (!useMemoryIndex)
            {
                foreach (var info in GetPersistentLibraDexIndexInfos())
                    yield return info;
            }

            foreach (var info in GetVolatileLibraDexIndexInfos())
                yield return info;
        }

        /// <summary>
        /// Returns a small Abraxas record-index handle for a logical index name, physical index name, or property path.<br/>
        /// Property-path matching accepts both <c>.Name</c> and <c>Name</c> forms when the index metadata exposes a simple path.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Logical index name, physical index name, or indexed property path.<br/></param>
        /// <param name="useMemoryIndex">When <see langword="true"/>, resolves only volatile indexes; otherwise resolves persistent and volatile indexes.<br/></param>
        /// <returns>A record-index handle suitable for ordered retrieval overloads.<br/></returns>
        public RecordIndex GetIndex(string indexIdentifier, bool useMemoryIndex = false)
        {
            if (TryGetIndex(indexIdentifier, out var index, useMemoryIndex))
                return index;

            throw new KeyNotFoundException($"No index found for '{indexIdentifier}'.");
        }

        /// <summary>
        /// Returns the attached index handle for a logical name, physical name, or property path.<br/>
        /// This terse member is equivalent to <see cref="GetIndex(string, bool)"/> and supports fluent index-owned lifecycle calls such as <c>store.Index(".Name").AddSubIndex(...)</c>.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Logical index name, physical index name, or indexed property path.<br/></param>
        /// <param name="useMemoryIndex">When <see langword="true"/>, resolves only volatile indexes; otherwise resolves persistent and volatile indexes.<br/></param>
        /// <returns>An attached record-index handle.<br/></returns>
        public RecordIndex Index(string indexIdentifier, bool useMemoryIndex = false)
            => GetIndex(indexIdentifier, useMemoryIndex);

        /// <summary>
        /// Attempts to resolve a logical index name, physical index name, or property path into a small Abraxas record-index handle.<br/>
        /// The handle deliberately keeps the public retrieval surface independent of any storage-engine vocabulary.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Logical index name, physical index name, or indexed property path.<br/></param>
        /// <param name="index">Resolved record-index handle when this method returns <c>true</c>.<br/></param>
        /// <param name="useMemoryIndex">When <see langword="true"/>, resolves only volatile indexes; otherwise resolves persistent and volatile indexes.<br/></param>
        /// <returns><c>true</c> when a matching index was found; otherwise <c>false</c>.<br/></returns>
        public bool TryGetIndex(string indexIdentifier, out RecordIndex index, bool useMemoryIndex = false)
        {
            index = default;
            if (string.IsNullOrWhiteSpace(indexIdentifier))
                return false;

            foreach (var info in GetIndexes(useMemoryIndex))
            {
                if (!IndexIdentifierMatches(info, indexIdentifier))
                    continue;

                index = new RecordIndex(info.LogicalPrefix, info.Backend.KeyPath, info.IsUnique, this);
                return true;
            }

            return false;
        }

        private static bool IndexIdentifierMatches(IndexInfo info, string indexIdentifier)
        {
            if (string.Equals(info.LogicalPrefix, indexIdentifier, StringComparison.OrdinalIgnoreCase))
                return true;

            var normalizedIdentifier = NormalizeIndexIdentifier(indexIdentifier);
            if (!string.IsNullOrEmpty(info.Backend.KeyPath) &&
                string.Equals(NormalizeIndexIdentifier(info.Backend.KeyPath), normalizedIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string NormalizeIndexIdentifier(string value)
        {
            var trimmed = value.Trim();
            return trimmed.Length > 0 && trimmed[0] == '.' ? trimmed[1..] : trimmed;
        }

        /// <summary>
        /// The supplied name is matched case-insensitively against stable logical names produced by <see cref="GetIndexes(bool)"/>.<br/>
        /// </summary>
        /// <param name="logicalPrefix">Stable Abraxas logical index name.<br/></param>
        /// <param name="useMemoryIndex">When <see langword="true"/>, searches only volatile indexes; otherwise searches persistent and volatile indexes.<br/></param>
        /// <returns>The matching index metadata, or an empty primary-only descriptor retaining the requested logical name when no match exists.<br/></returns>
        public IndexInfo GetIndexInfo(string logicalPrefix, bool useMemoryIndex = false)
        {
            if (string.IsNullOrWhiteSpace(logicalPrefix))
                throw new ArgumentNullException(nameof(logicalPrefix));

            foreach (var info in GetIndexes(useMemoryIndex))
            {
                if (string.Equals(info.LogicalPrefix, logicalPrefix, StringComparison.OrdinalIgnoreCase))
                    return info;
            }

            return new IndexInfo(
                logicalPrefix,
                false,
                "No registered Abraxas index was found for this logical name.");
        }

        /// <summary>
        /// Removes all Abraxas indexes currently visible through <see cref="GetIndexes(bool)"/>.<br/>
        /// </summary>
        public void RemoveAllIndexes()
        {
            var indexes = GetIndexes().ToArray();
            for (var i = 0; i < indexes.Length; i++)
            {
                RemoveIndex(new RecordIndex(indexes[i].LogicalPrefix, indexes[i].Backend.KeyPath, indexes[i].IsUnique, this));
            }
        }

        /// <summary>
        /// Removes the index represented by an Abraxas record-index handle.<br/>
        /// </summary>
        /// <param name="index">The record-index handle returned by index lookup or creation APIs.<br/></param>
        /// <returns><c>true</c> when an index or ordered backend was removed; otherwise <c>false</c>.<br/></returns>
        public bool RemoveIndex(RecordIndex index)
        {
            if (string.IsNullOrWhiteSpace(index.Name))
                throw new ArgumentNullException(nameof(index));

            return TryRemoveLibraDexIndex(index);
        }

        /// <summary>
        /// Removes an index resolved by logical name, physical name, or property path.<br/>
        /// The identifier accepts the same forms as <see cref="GetIndex(string, bool)"/>, including simple paths with or without the leading dot.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Logical index name, physical index name, or indexed property path.<br/></param>
        /// <returns><c>true</c> when an index or ordered backend was removed; otherwise <c>false</c>.<br/></returns>
        public bool RemoveIndex(string indexIdentifier)
        {
            if (!TryGetIndex(indexIdentifier, out var index))
                return false;

            return RemoveIndex(index);
        }

        /// <summary>
        /// Removes an index resolved by property path.<br/>
        /// This overload keeps path-based removal explicit without exposing logical-prefix or storage-engine vocabulary.<br/>
        /// </summary>
        /// <param name="key">The indexed property path to remove.<br/></param>
        /// <returns><c>true</c> when an index or ordered backend was removed; otherwise <c>false</c>.<br/></returns>
        public bool RemoveIndex(PropPath key)
        {
            return RemoveIndex(key.Path);
        }

        /// <summary>
        /// Describes one Abraxas logical index using engine-independent management metadata.
        /// <br/>
        /// - <see cref="LogicalPrefix"/> is the stable management name.<br/>
        /// - <see cref="IsUnique"/> reports the logical uniqueness contract.<br/>
        /// - <see cref="InfoText"/> provides a human-readable description of the index intent and lifetime.<br/>
        /// </summary>
        public readonly record struct IndexInfo(string LogicalPrefix, bool IsUnique, string InfoText)
        {
            /// <summary>
            /// Describes optional ordered-backend support for this Abraxas index.<br/>
            /// Primary-only indexes remain fully usable through the ordinary Abraxas index path; ordered-ready indexes can also be served by the alternate ordered backend when that route is selected.<br/>
            /// </summary>
            public IndexBackendInfo Backend { get; init; } = IndexBackendInfo.PrimaryOnly;
        }

        /// <summary>
        /// Describes whether an Abraxas index has only the primary index path or also has an ordered backend available.<br/>
        /// The names are intentionally Abraxas-facing so application code does not need to know which engine supplies the ordered backend.<br/>
        /// </summary>
        public enum IndexBackendStatus
        {
            /// <summary>
            /// The index is available through the primary Abraxas index path only.<br/>
            /// </summary>
            PrimaryOnly = 0,
            /// <summary>
            /// The index has an ordered backend ready for supported equality, range, and ordered retrieval operations.<br/>
            /// </summary>
            OrderedReady = 1
        }

        /// <summary>
        /// Flags the operations an Abraxas index backend can support without forcing callers to know the underlying engine vocabulary.<br/>
        /// </summary>
        [Flags]
        public enum IndexBackendCapabilities
        {
            /// <summary>
            /// No alternate backend capabilities are available.<br/>
            /// </summary>
            None = 0,
            /// <summary>
            /// Equality predicates can be served by the ordered backend.<br/>
            /// </summary>
            Equality = 1,
            /// <summary>
            /// Range predicates can be served by the ordered backend.<br/>
            /// </summary>
            Range = 2,
            /// <summary>
            /// Full-index reads can be streamed in key order by the ordered backend.<br/>
            /// </summary>
            OrderedRead = 4,
            /// <summary>
            /// The backend mirrors a unique logical index.<br/>
            /// </summary>
            Unique = 8,
            /// <summary>
            /// The backend stores its key material only in memory and does not persist it to disk.<br/>
            /// </summary>
            Volatile = 16,
            /// <summary>
            /// The volatile backend was created automatically by Abraxas to serve a reusable operation such as distinct value retrieval.<br/>
            /// </summary>
            AutoCreated = 32
        }

        /// <summary>
        /// Identifies the operation families that may create reusable volatile helper indexes when auto-indexing is enabled.<br/>
        /// </summary>
        [Flags]
        public enum AutoIndexUse
        {
            /// <summary>
            /// No operations may auto-create volatile helper indexes.<br/>
            /// </summary>
            None = 0,
            /// <summary>
            /// Distinct count and distinct value operations may auto-create volatile helper indexes.<br/>
            /// </summary>
            Distinct = 1,
            /// <summary>
            /// Ordered iteration operations may auto-create volatile helper indexes.<br/>
            /// </summary>
            Ordering = 2,
            /// <summary>
            /// Filter operations may auto-create volatile helper indexes after condition planning supports that behavior.<br/>
            /// </summary>
            Filtering = 4,
            /// <summary>
            /// All currently supported auto-index operation families are enabled.<br/>
            /// </summary>
            All = Distinct | Ordering | Filtering
        }

        /// <summary>
        /// Controls how Abraxas removes auto-created volatile helper indexes while the store remains open.<br/>
        /// Explicit memory indexes created by the developer are never removed by these policies.<br/>
        /// </summary>
        public enum AutoIndexEviction
        {
            /// <summary>
            /// Auto-created volatile indexes remain live until explicitly removed, evicted, or their owning store is no longer reachable.<br/>
            /// </summary>
            None = 0,
            /// <summary>
            /// When the auto-index cap would be exceeded, Abraxas removes the least recently used auto-created index.<br/>
            /// </summary>
            LeastRecentlyUsed = 1,
            /// <summary>
            /// Abraxas trims auto-created indexes whose last use is older than <see cref="AutoIndexingOptions.IdleTimeout"/> during normal store calls.<br/>
            /// </summary>
            IdleTimeout = 2,
            /// <summary>
            /// Abraxas uses a background timer to trim auto-created indexes whose last use is older than <see cref="AutoIndexingOptions.IdleTimeout"/>.<br/>
            /// This is opt-in for memory-sensitive applications that may not call back into Abraxas for long periods.<br/>
            /// </summary>
            BackgroundIdleTimeout = 3
        }

        /// <summary>
        /// Store-level policy for reusable auto-created volatile helper indexes.<br/>
        /// Auto indexes are memory-backed, are visible through index metadata, and keep key material out of persistent storage.<br/>
        /// </summary>
        public sealed class AutoIndexingOptions
        {
            /// <summary>
            /// Enables Abraxas to create volatile helper indexes for eligible operations when no matching index already exists.<br/>
            /// The default is disabled so index creation remains an explicit developer decision; enabling this option applies only to the families selected by <see cref="Uses"/>.<br/>
            /// </summary>
            public bool Enabled { get; set; }

            /// <summary>
            /// Maximum number of auto-created volatile helper indexes allowed for this store.<br/>
            /// Explicit developer-created memory indexes do not count against this limit.<br/>
            /// </summary>
            public int MaxVolatileIndexes { get; set; } = 8;

            /// <summary>
            /// Operation families that may auto-create volatile helper indexes.<br/>
            /// </summary>
            public AutoIndexUse Uses { get; set; } = AutoIndexUse.Distinct | AutoIndexUse.Ordering;

            /// <summary>
            /// Policy used to remove auto-created volatile helper indexes while the store remains open.<br/>
            /// </summary>
            public AutoIndexEviction Eviction { get; set; } = AutoIndexEviction.LeastRecentlyUsed;

            /// <summary>
            /// Idle duration used by idle-based eviction policies.<br/>
            /// </summary>
            public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

            /// <summary>
            /// Background trim interval used only when <see cref="Eviction"/> is <see cref="AutoIndexEviction.BackgroundIdleTimeout"/>.<br/>
            /// </summary>
            public TimeSpan BackgroundTrimInterval { get; set; } = TimeSpan.FromMinutes(1);
        }

        /// <summary>
        /// Controls behavior when requested ordering cannot use a complete compatible LibraDex index.<br/>
        /// </summary>
        public enum OrderingFallback
        {
            /// <summary>
            /// Reads each surviving authoritative Fractal/Inheto record once and sorts only that candidate set.<br/>
            /// No persistent or volatile index is created by this fallback.<br/>
            /// </summary>
            AllowCandidateSort = 0,
            /// <summary>
            /// Rejects ordering unless existing or explicitly enabled auto-index support provides complete compatible ordered coverage.<br/>
            /// </summary>
            RequireIndex = 1
        }

        /// <summary>
        /// Store-level policy for ordered retrieval when native index coverage is unavailable or incomplete.<br/>
        /// </summary>
        public sealed class OrderingOptions
        {
            /// <summary>
            /// Gets or sets the fallback used after existing indexes and explicitly enabled automatic helper indexes cannot provide the requested order.<br/>
            /// </summary>
            public OrderingFallback Fallback { get; set; } = OrderingFallback.AllowCandidateSort;
        }

        /// <summary>
        /// Abraxas-facing management metadata for an index's optional ordered backend.<br/>
        /// This is intended for diagnostics, backend selection, and UI affordances; it is not used in record hot paths.<br/>
        /// </summary>
        /// <param name="Status">Whether an ordered backend is ready for this index.<br/></param>
        /// <param name="Capabilities">The supported backend operation flags.<br/></param>
        /// <param name="KeyPath">The simple property path mirrored by the ordered backend, or an empty string for primary-only indexes.<br/></param>
        /// <param name="KeyType">The CLR key type mirrored by the ordered backend, or <c>null</c> for primary-only indexes.<br/></param>
        public readonly record struct IndexBackendInfo(IndexBackendStatus Status, IndexBackendCapabilities Capabilities, string KeyPath, Type? KeyType)
        {
            /// <summary>
            /// Represents an index that currently has no alternate ordered backend attached.<br/>
            /// </summary>
            public static IndexBackendInfo PrimaryOnly => new(IndexBackendStatus.PrimaryOnly, IndexBackendCapabilities.None, string.Empty, null);
        }

        /// <summary>
        /// Creates a persistent bounded variable-length byte-array index backed directly by LibraDex.<br/>
        /// The explicit maximum payload length is part of the durable index contract and prevents arbitrary application blobs from creating unbounded index keys.<br/>
        /// </summary>
        /// <param name="key">The direct byte-array property path used as the index key.<br/></param>
        /// <param name="maxKeyBytes">The maximum byte-array payload length accepted by the index.<br/></param>
        /// <param name="unique">When <c>true</c>, different record identities may not share one exact byte-array key.<br/></param>
        /// <returns><c>true</c> when the LibraDex index was created or opened and registered with this store.<br/></returns>
        public bool CreateIndexBinary(PropPath key, int maxKeyBytes, bool unique = false)
        {
            if (string.IsNullOrWhiteSpace(key.Path))
                throw new ArgumentException("Binary index key path cannot be empty.", nameof(key));
            if (maxKeyBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxKeyBytes), maxKeyBytes, "Binary indexes require a positive maximum payload byte count.");
            if (!TryResolveSimplePropertyType(key.Path, out var keyType) || NormalizeValueType(keyType) != typeof(byte[]))
                throw new InvalidOperationException($"Binary index key path '{key.Path}' does not resolve to a byte[] field or property on '{typeof(T).FullName}'.");
            if (!TryRegisterLibraDexBinaryPathIndex(key.Path, maxKeyBytes, unique))
                throw new InvalidOperationException("A persistent LibraDex identity group must be bound before creating a binary index.");

            return true;
        }

        /// <summary>
        /// Creates a persistent bounded variable-length byte-array index from a strongly typed property expression.<br/>
        /// This overload preserves compile-time member selection while using the same explicit durable payload cap as <see cref="CreateIndexBinary(PropPath, int, bool)"/>.<br/>
        /// </summary>
        /// <param name="key">The expression that identifies the byte-array property used as the index key.<br/></param>
        /// <param name="maxKeyBytes">The maximum byte-array payload length accepted by the index.<br/></param>
        /// <param name="unique">When <c>true</c>, different record identities may not share one exact byte-array key.<br/></param>
        /// <returns><c>true</c> when the LibraDex index was created or opened and registered with this store.<br/></returns>
        public bool CreateIndexBinary(Expression<Func<T, byte[]>> key, int maxKeyBytes, bool unique = false)
        {
            ArgumentNullException.ThrowIfNull(key);
            return CreateIndexBinary(PropPath.FromExpression(key), maxKeyBytes, unique);
        }

        /// <summary>
        /// Creates a bounded byte-array index whose physical keys and definition exist only for this open store instance.<br/>
        /// Existing records are populated from authoritative payloads, and subsequent writes and deletes use the same validation and maintenance contract as the file-backed binary index.<br/>
        /// Recreate the index after reopening the store when its ordered access is needed again.<br/>
        /// </summary>
        /// <param name="key">Direct byte-array property path used as the index key.<br/></param>
        /// <param name="maxKeyBytes">Positive maximum payload length accepted for each indexed key.<br/></param>
        /// <param name="unique">Whether complete non-null byte-array keys must be unique across record identities.<br/></param>
        /// <returns><see langword="true"/> after the memory-backed index is attached and populated.<br/></returns>
        public bool CreateMemoryIndexBinary(PropPath key, int maxKeyBytes, bool unique = false)
        {
            if (string.IsNullOrWhiteSpace(key.Path))
                throw new ArgumentException("Binary index key path cannot be empty.", nameof(key));
            if (maxKeyBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxKeyBytes), maxKeyBytes, "Binary indexes require a positive maximum payload byte count.");
            if (!TryResolveSimplePropertyType(key.Path, out var keyType) || NormalizeValueType(keyType) != typeof(byte[]))
                throw new InvalidOperationException($"Binary index key path '{key.Path}' does not resolve to a byte[] field or property on '{typeof(T).FullName}'.");
            if (!TryRegisterLibraDexBinaryPathIndex(key.Path, maxKeyBytes, unique, LibraDexVolatileIndexes, populateExisting: true, manageIntent: false, ownerCatalog: libraDexVolatileCatalog))
                throw new NotSupportedException($"Memory binary index key path '{key.Path}' cannot be represented by the ordered backend.");
            return true;
        }

        /// <summary>
        /// Creates the memory-only bounded binary counterpart of <see cref="CreateIndexBinary(Expression{Func{T, byte[]}}, int, bool)"/> using a compile-time property selector.<br/>
        /// The byte cap, uniqueness contract, backfill, and mutation behavior are identical to the property-path overload; no binary index intent survives reopen.<br/>
        /// </summary>
        /// <param name="key">Expression identifying the direct byte-array property.<br/></param>
        /// <param name="maxKeyBytes">Positive maximum payload length accepted for each indexed key.<br/></param>
        /// <param name="unique">Whether complete non-null byte-array keys must be unique across record identities.<br/></param>
        /// <returns><see langword="true"/> after the memory-backed index is attached and populated.<br/></returns>
        public bool CreateMemoryIndexBinary(Expression<Func<T, byte[]>> key, int maxKeyBytes, bool unique = false)
        {
            ArgumentNullException.ThrowIfNull(key);
            return CreateMemoryIndexBinary(PropPath.FromExpression(key), maxKeyBytes, unique);
        }
        /// <summary>
        /// Confirms that intrinsic developer-owned State indexing is available for this store.<br/>
        /// This compatibility method performs no index creation and does not scan or rewrite existing records.<br/>
        /// </summary>
        /// <returns><see langword="true"/> when the reserved persistent LibraDex State index is bound to this store; otherwise <see langword="false"/>.<br/></returns>
        public bool CreateStateIndex()
            => libraDexStateIndex is not null;

        /// <summary>
        /// Retains the historical State-index confirmation signature while directing callers to the intrinsic persistent State contract.<br/>
        /// </summary>
        /// <param name="useMemoryIndex">Ignored compatibility value; State is always persistent.<br/></param>
        /// <returns><see langword="true"/> when the reserved persistent LibraDex State index is bound to this store.<br/></returns>
        [Obsolete("State is always persistent and intrinsic. Use CreateStateIndex() instead.")]
        public bool CreateStateIndex(bool useMemoryIndex)
        {
            _ = useMemoryIndex;
            return CreateStateIndex();
        }

        /// <summary>
        /// The reserved developer-owned State index and persistent partial indexes are not ordinary indexes and are never cleared by this operation.<br/>
        /// </summary>
        /// <param name="index">The attached index handle returned by this store's creation or lookup APIs.<br/></param>
        /// <returns><c>true</c> when the matching ordinary index was rebuilt; otherwise <c>false</c>.<br/></returns>
        public bool RebuildIndex(RecordIndex index)
        {
            if (string.IsNullOrWhiteSpace(index.Name))
                throw new ArgumentNullException(nameof(index));

            return TryRebuildLibraDexIndex(index);
        }

        /// <summary>
        /// Resolves and rebuilds one ordinary Abraxas index from authoritative Fractal/Inheto records.<br/>
        /// Logical names, physical names, and simple property paths use the same discovery rules as <see cref="GetIndex(string, bool)"/>.<br/>
        /// </summary>
        /// <param name="indexIdentifier">Logical index name, physical index name, or indexed property path.<br/></param>
        /// <returns><c>true</c> when a matching ordinary index was rebuilt; otherwise <c>false</c>.<br/></returns>
        public bool RebuildIndex(string indexIdentifier)
        {
            if (string.IsNullOrWhiteSpace(indexIdentifier))
                throw new ArgumentNullException(nameof(indexIdentifier));
            if (!TryGetIndex(indexIdentifier, out var index))
                return false;

            return RebuildIndex(index);
        }

        /// <summary>
        /// Rebuilds the ordinary Abraxas index registered for one property path from authoritative Fractal/Inheto records.<br/>
        /// This overload keeps the common strongly named path workflow free of logical or physical backend identifiers.<br/>
        /// </summary>
        /// <param name="key">The indexed property path to rebuild.<br/></param>
        /// <returns><c>true</c> when a matching ordinary index was rebuilt; otherwise <c>false</c>.<br/></returns>
        public bool RebuildIndex(PropPath key)
            => RebuildIndex(key.Path);
    }

    /// <summary>
    /// Extension helpers that let selector fluency pivot into index selectors without losing type-specific operations.
    /// </summary>
    public static class SelectorIndexingExtensions
    {
        private static RecordStore<T>.IndexSelectorBuilder ForIndexingCore<T>(ISelector selector, bool isText, RecordStore<T>.IndexCaseType caseType, RecordStore<T>.IndexSortDirection sort, string? tokenSource, Type? valueType)
        {
            ArgumentNullException.ThrowIfNull(selector);
            var token = tokenSource ?? RecordStore<T>.GetSelectorTokenSource(selector);
            var builder = RecordStore<T>.IndexSelectorBuilder.FromSelector(selector, isText, token, valueType);
            if (isText && caseType != RecordStore<T>.IndexCaseType.None)
                builder = builder.IgnoreCase(caseType);
            builder = sort == RecordStore<T>.IndexSortDirection.Descending ? builder.Descending : builder.Ascending;
            return builder;
        }

        /// <summary>
        /// Converts a selector into an index selector builder. Use this when you need to specify the record type explicitly (e.g., from non-typed contexts).
        /// </summary>
        /// <summary>
        /// Converts a string selector into an index selector builder, preserving string-specific shaping and optional case/sort options.
        /// <br/>
        /// Use this at the end of your string selector fluency, e.g. <c>new Selector&lt;T&gt;().Prop(u =&gt; u.Name).AsString.Substring(0,4).ForIndexing()</c>.
        /// </summary>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(
            this Selector<T>.StringSelector selector,
            RecordStore<T>.IndexCaseType caseType = RecordStore<T>.IndexCaseType.None,
            RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending,
            string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: true, caseType, sort, tokenSource, typeof(string));
        }

        // Value-type selectors
        /// <summary>
        /// Creates an index selector declaration from this IntegralSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <typeparam name="TNum">Exact numeric value type declared by the selector.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T, TNum>(this Selector<T>.IntegralSelector<TNum> selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
            where TNum : System.Numerics.INumber<TNum>, System.Numerics.IBinaryInteger<TNum>
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(TNum));
        }

        /// <summary>
        /// Creates an index selector declaration from this RealSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <typeparam name="TNum">Exact numeric value type declared by the selector.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T, TNum>(this Selector<T>.RealSelector<TNum> selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
            where TNum : System.Numerics.INumber<TNum>
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(TNum));
        }

        /// <summary>
        /// Creates an index selector declaration from this DecimalSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.DecimalSelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(decimal));
        }

        /// <summary>
        /// Creates an index selector declaration from this BooleanSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.BooleanSelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(bool));
        }

        /// <summary>
        /// Creates an index selector declaration from this GuidSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.GuidSelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(Guid));
        }

        /// <summary>
        /// Creates an index selector declaration from this BinarySelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.BinarySelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(byte[]));
        }

        // Date/Time selectors
        /// <summary>
        /// Creates an index selector declaration from this DateTimeSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.DateTimeSelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(DateTime));
        }

        /// <summary>
        /// Creates an index selector declaration from this DateOnlySelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.DateOnlySelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(DateOnly));
        }

        /// <summary>
        /// Creates an index selector declaration from this TimeOnlySelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.TimeOnlySelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(TimeOnly));
        }

        /// <summary>
        /// Creates an index selector declaration from this TimeSpanSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T>(this Selector<T>.TimeSpanSelector selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null)
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(TimeSpan));
        }

        // Numeric selector umbrella
        /// <summary>
        /// Creates an index selector declaration from this NumericSelector while retaining its exact result type.<br/>
        /// The declaration carries ordering and naming metadata; it does not create or populate an index.<br/>
        /// </summary>
        /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
        /// <typeparam name="TNum">Exact numeric value type declared by the selector.<br/></typeparam>
        /// <param name="selector">Typed source selector whose direct or computed result becomes the index key.<br/></param>
        /// <param name="sort">Key ordering direction recorded in the declaration.<br/></param>
        /// <param name="tokenSource">Optional readable naming token; null derives a source path or canonical selector identity.<br/></param>
        /// <returns>An index selector declaration suitable for an explicit store index-creation call.<br/></returns>
        public static RecordStore<T>.IndexSelectorBuilder ForIndexing<T, TNum>(this Selector<T>.NumericSelector<TNum> selector, RecordStore<T>.IndexSortDirection sort = RecordStore<T>.IndexSortDirection.Ascending, string? tokenSource = null) where TNum : System.Numerics.INumber<TNum>
        {
            return ForIndexingCore(selector, isText: false, RecordStore<T>.IndexCaseType.None, sort, tokenSource, typeof(TNum));
        }
    }
}
